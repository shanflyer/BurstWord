using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        public enum SortingMode { AlwaysInFront, OpaqueOcclusion, SceneTransparent }
        public enum SpaceMode { ScreenSnapshot, ScreenFollow, WorldFollow }
        internal bool useVisibleIndexJobs = true;
        internal bool useBurstSortMerge = true;
        internal bool usePlainSdfFastPath = true;
        internal bool useLinkedIndexJobs = true, useBoundedPoseUploads = true;
        internal bool useBurstVisibleInputs = true;
        private bool appliedPlainSdfFastPath;
        private NativeArray<ulong> liveLabelOrders;
        private NativeArray<int> mergeResult;
        private struct VisibleLink { public int slot, next; }
        private NativeArray<VisibleLink> visibleLinks;
        private struct VisibleLabel { public int count, first, head, contiguous; }
        private NativeArray<VisibleLabel> visibleLabels;
        private JobHandle visibilityWork;
        private bool visibilityWorkPending;
        private double visibilityWait;
        private int visibilityWaitFrame = -1;
        public double VisibilityJobWaitMilliseconds => visibilityWaitFrame == Time.frameCount ? visibilityWait : 0;
        private void CompleteVisibilityWork()
        {
            if (!visibilityWorkPending) return;
            long began = collectLayoutTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            visibilityWork.Complete(); visibilityWorkPending = false; visibilityWork = default;
            if (collectLayoutTimings)
            {
                if (visibilityWaitFrame != Time.frameCount) { visibilityWaitFrame = Time.frameCount; visibilityWait = 0; }
                visibilityWait += (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        [Header("Space and ordering")]
        [Tooltip("All modes sort whole labels back-to-front. The first two use the BurstWord URP feature; SceneTransparent participates in ordinary scene transparency sorting.")]
        public SortingMode sortingMode = SortingMode.AlwaysInFront;
        [Tooltip("Default for newly emitted labels. Screen modes retain pixel size. WorldFollow uses the complete target/local transform and perspective.")]
        public SpaceMode spaceMode = SpaceMode.ScreenSnapshot;
        [Min(0.000001f)] public float worldUnitsPerLayoutUnit = 0.01f;
        public int UploadedTransformBytesLastFrame { get; private set; }
        public int SortedLabelCount { get; private set; }
        public int SortedInputsLastFrame { get; private set; }
        public string LastSortPath { get; private set; } = "未排序";
        public int TransformUploadCallsLastFrame { get; private set; }
        public int FollowingLabelCount => followingLabelCount;
        public int FollowTargetCount => followTargets.Count;

        /// <summary>A value handle, not an object or component. Reuse, Clear and disable invalidate it.</summary>
        public readonly struct TextHandle
        {
            internal readonly BrgDamageTextRenderer owner;
            internal readonly int index;
            internal readonly ulong generation;
            internal TextHandle(BrgDamageTextRenderer owner, int index, ulong generation)
            { this.owner = owner; this.index = index; this.generation = generation; }
            public bool IsAlive => owner != null && owner.IsAlive(this);
        }

        [Serializable]
        public struct TextPose
        {
            public Vector3 position, scale, offset;
            public Quaternion rotation;
            public TextPose(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 offset = default)
            { this.position = position; this.rotation = rotation; this.scale = scale; this.offset = offset; }
        }

        private struct SortLabel { public int id; public float depth; public ulong order; public Vector3 position; public uint depthKey; }
        private sealed class FollowTarget
        {
            public Transform target;
            public Matrix4x4 matrix;
            public int head=-1, index;
        }
        private sealed class LabelComparer : IComparer<SortLabel>
        {
            public int Compare(SortLabel a, SortLabel b)
            { int result = b.depth.CompareTo(a.depth); return result != 0 ? result : a.order.CompareTo(b.order); }
        }
        private static readonly LabelComparer labelComparer = new LabelComparer();
        private static readonly List<BrgDamageTextRenderer> spatialRenderers = new List<BrgDamageTextRenderer>();
        private readonly Dictionary<Transform, FollowTarget> targetLookup = new Dictionary<Transform, FollowTarget>();
        private readonly List<FollowTarget> followTargets = new List<FollowTarget>();
        private Vector4[][] labelValues;
        private GraphicsBuffer labelBuffer;
        private byte[] poseDirty;
        private int[] poseDirtyIds, activeLabels;
        private int poseDirtyCount;
        private bool poseDirtySorted;
        private readonly int[] poseFirst={int.MaxValue,int.MaxValue,int.MaxValue}, poseLast={-1,-1,-1}, poseFieldCount=new int[3];
        private SortLabel[] sortedLabels;
        private SortLabel[] sortScratch;
        private int[] radixIndices, radixScratch;
        private readonly int[] radixCounts = new int[256];
        private int sortedCount;
        private bool orderDirty = true;
        private bool existingPoseChanged;
        private ulong lastSortedSequence;
        private SortingMode orderedSortingMode;
        private ulong labelGeneration, labelSequence;
        private SortingMode appliedSortingMode;
        private Vector3 orderedCameraPosition, orderedCameraForward, orderedSortAxis;
        private TransparencySortMode orderedSortMode;
        private int animatedWorldLabels;
        private int animatedLabels;
        private int followingLabelCount;
        private int labelFrame;

        public static bool NeedsOverlay(Camera camera)
        {
            foreach (var renderer in spatialRenderers)
                if (renderer != null && renderer.worldCamera == camera && renderer.ActiveGlyphCount > 0 &&
                    renderer.sortingMode != SortingMode.SceneTransparent) return true;
            return false;
        }
        public static bool NeedsOpaqueDepth(Camera camera)
        {
            foreach (var renderer in spatialRenderers)
                if (renderer != null && renderer.worldCamera == camera && renderer.ActiveGlyphCount > 0 &&
                    renderer.sortingMode == SortingMode.OpaqueOcclusion) return true;
            return false;
        }
        private static void BeforeCameraRender(ScriptableRenderContext context, Camera camera)
        {
            foreach (var renderer in spatialRenderers)
                if (renderer != null && renderer.worldCamera == camera && renderer.IsInitialized)
                {
                    renderer.UpdateScreenParameters();
                    renderer.UpdateAnimations();
                    renderer.UpdateEffects();
                    renderer.UpdateSpatial();
                    if (!renderer.UsingBrg && renderer.sortingMode == SortingMode.SceneTransparent)
                        renderer.DrawInstanced(null);
                    // Damage may be emitted by another LateUpdate after this manager ran.
                    using (UploadMarker.Auto())
                        foreach(var page in renderer.glyphPages)
                        {
                            renderer.UploadedBytesLastFrame += page.Upload();
                            renderer.UploadCallsLastFrame += page.UploadCalls;
                        }
                }
        }
        private void InitializeSpatial()
        {
            labelValues = new[] { new Vector4[labels.Length], new Vector4[labels.Length], new Vector4[labels.Length] };
            poseDirty = new byte[labels.Length]; poseDirtyIds = new int[labels.Length];
            activeLabels = new int[labels.Length]; sortedLabels = new SortLabel[labels.Length]; sortScratch = new SortLabel[labels.Length];
            liveLabelOrders = new NativeArray<ulong>(labels.Length, Allocator.Persistent);
            mergeResult = new NativeArray<int>(1, Allocator.Persistent);
            radixIndices=new int[labels.Length]; radixScratch=new int[labels.Length];
            if (UsingBrg)
            {
                labelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 16 + labels.Length * 12, 4);
                labelBuffer.SetData(new Vector4[4]);
            }
            poseDirtyCount = sortedCount = animatedWorldLabels = animatedLabels = followingLabelCount = 0; orderDirty = true;
            lastSortedSequence = labelSequence; existingPoseChanged = false;
            ResetPoseSpans();
            appliedSortingMode = sortingMode;
            appliedPlainSdfFastPath = usePlainSdfFastPath;
            if (spatialRenderers.Count == 0) RenderPipelineManager.beginCameraRendering += BeforeCameraRender;
            spatialRenderers.Add(this);
        }
        private void DisposeSpatial()
        {
            spatialRenderers.Remove(this);
            if (spatialRenderers.Count == 0) RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
            labelBuffer?.Dispose(); labelBuffer = null;
            if (liveLabelOrders.IsCreated) liveLabelOrders.Dispose(); if (mergeResult.IsCreated) mergeResult.Dispose();
            labelValues = null; activeLabels = poseDirtyIds = null; poseDirty = null; sortedLabels = sortScratch = null;
            targetLookup.Clear(); followTargets.Clear(); radixIndices=radixScratch=null;
            poseDirtyCount = sortedCount = animatedWorldLabels = animatedLabels = followingLabelCount = 0;
            SortedLabelCount = UploadedTransformBytesLastFrame = 0;
        }

        public TextHandle EmitText(Transform target, string text, Color color, Vector3 offset,
            Quaternion? rotation, Vector3? scale, float horizontalDrift, float duration,
            BrgTextAnimation animation, float animationAmplitude)
            => EmitText(target, text, color, offset, rotation, scale, horizontalDrift, duration,
                animation, animationAmplitude, font: null);

        public TextHandle EmitText(Transform target, string text, Color color, Vector3 offset = default,
            Quaternion? rotation = null, Vector3? scale = null, float horizontalDrift = 0, float duration = 1.5f,
            BrgTextAnimation animation = null, float animationAmplitude = 1, TMP_FontAsset font = null,
            int fontSize = 0, bool useLegacyAnimation = false, TextAnchor? alignment = null, Vector2? textAreaSize = null, int fontIndex = 0, int animationIndex = 0, int effectIndex = 0, Vector4 effectParameters = default)
        {
            if (target == null) return default;
            var local = new TextPose(Vector3.zero, rotation ?? Quaternion.identity, scale ?? Vector3.one, offset);
            return EmitSpatial(text, color, target, local, horizontalDrift, duration,
                ResolveAnimation(animation, animationIndex, useLegacyAnimation), animationAmplitude, true, font, fontSize, alignment, textAreaSize, fontIndex, effectIndex, effectParameters);
        }
        public TextHandle EmitText(TextPose pose, string text, Color color, float horizontalDrift, float duration,
            BrgTextAnimation animation, float animationAmplitude)
            => EmitText(pose, text, color, horizontalDrift, duration, animation, animationAmplitude, font: null);

        public TextHandle EmitText(TextPose pose, string text, Color color, float horizontalDrift = 0, float duration = 1.5f,
            BrgTextAnimation animation = null, float animationAmplitude = 1, TMP_FontAsset font = null,
            int fontSize = 0, bool useLegacyAnimation = false, TextAnchor? alignment = null, Vector2? textAreaSize = null, int fontIndex = 0, int animationIndex = 0, int effectIndex = 0, Vector4 effectParameters = default)
            => EmitSpatial(text, color, null, pose, horizontalDrift, duration,
                ResolveAnimation(animation, animationIndex, useLegacyAnimation), animationAmplitude, true, font, fontSize, alignment, textAreaSize, fontIndex, effectIndex, effectParameters);

        public bool IsAlive(TextHandle handle) => ReferenceEquals(handle.owner, this) && labels != null &&
            handle.index >= 0 && handle.index < labels.Length && labels[handle.index].active &&
            labels[handle.index].generation == handle.generation;

        /// <summary>For an explicit pose source, call whenever it changes. Detaches a Transform source.</summary>
        public bool TryUpdatePose(TextHandle handle, TextPose pose)
        {
            if (!IsAlive(handle) || labels[handle.index].space == SpaceMode.ScreenSnapshot) return false;
            ref var label = ref labels[handle.index];
            DetachFollow(handle.index);
            label.target = null; label.pose = pose;
            label.localMatrix = PoseMatrix(null, pose);
            SetLabelMatrix(handle.index, label.localMatrix); return true;
        }
        public bool TryRelease(TextHandle handle)
        { if (!IsAlive(handle)) return false; ReturnLabel(handle.index); return true; }

        private static void ValidateDuration(float duration)
        {
            if (!(duration > 0) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be finite and greater than zero (seconds).");
        }

        private TextHandle EmitSpatial(string text, Color color, Transform target, TextPose pose, float drift, float duration,
            BrgTextAnimation animation = null, float amplitude = 1, bool resolvedAnimation = false,
            TMP_FontAsset selectedFont = null, int selectedSize = 0,
            TextAnchor? selectedAlignment = null, Vector2? selectedTextArea = null, int fontIndex = 0, int effectIndex = 0, Vector4 effectParameters = default)
        {
            ValidateDuration(duration);
            var effectShader = ResolveEffect(effectIndex, effectParameters);
            using var appearance = new EmissionAppearanceScope(this, selectedFont, selectedSize, selectedAlignment, selectedTextArea, fontIndex);
            using (preparingBatch ? default(Unity.Profiling.ProfilerMarker.AutoScope) : GenerateMarker.Auto())
            {
                if (!isActiveAndEnabled || !IsInitialized || string.IsNullOrEmpty(text)) return default;
                if (freeLabelCount == 0) { DroppedCount++; return default; }
                bool built;
                using (LayoutMarker.Auto()) built = BuildLayout(text, color);
                if (!built) { FailedLayoutCount++; return default; }
                return CommitSpatial(target, pose, drift, duration, default, 0, layout.Count,
                    resolvedAnimation ? animation : animation ?? GetAnimation(0), amplitude, effectShader, effectParameters);
            }
        }
        private unsafe TextHandle CommitSpatial(Transform target, TextPose pose, float drift, float duration,
            NativeArray<PreparedGlyph> prepared, int first, int count, BrgTextAnimation animation = null, float amplitude = 1, Shader effectShader = null, Vector4 effectParameters = default)
        {
                CompleteVisibilityWork();
                using var instancesScope = InstancesMarker.Auto();
                int id = freeLabels[--freeLabelCount];
                float birth = Now;
                effectShader = effectShader != null ? effectShader : glyphShader;
                bool customEffect = effectShader != glyphShader;
                SetEffectParameters(id, effectParameters, customEffect);
                var label = new Label { active = true, end = birth + duration, birth = birth, duration = duration,
                    head = -1, tail = -1, firstGlyphSlot = -1, contiguousGlyphs = true, space = spaceMode, pose = pose,
                    target = spaceMode == SpaceMode.ScreenSnapshot ? null : target,
                    units = Mathf.Max(0.000001f, worldUnitsPerLayoutUnit), rise = risePixels, drift = drift,
                    generation = ++labelGeneration, order = ++labelSequence, activeIndex = ActiveCount,
                    previousFollow=-1, nextFollow=-1, customEffect=customEffect };
                label.localMatrix = PoseMatrix(null, pose);
                Matrix4x4 targetMatrix = target != null ? target.localToWorldMatrix : Matrix4x4.identity;
                labels[id] = label;
                liveLabelOrders[id] = label.order;
                activeLabels[ActiveCount] = id;
                SetLabelMatrix(id, targetMatrix * label.localMatrix, true);
                Vector4 anchor = labelValues[0][id]; anchor.w = birth;
                Vector4 motion = new Vector4(duration, risePixels, drift, (float)spaceMode);
                bool batchedWrite = queueInstanceWrites && prepared.IsCreated;
                var preparedPointer = prepared.IsCreated ? (PreparedGlyph*)prepared.GetUnsafeReadOnlyPtr() + first : null;
                if (batchedWrite) instanceLabelWrites[instanceWriteCount++] = new InstanceLabelWrite { first = first, count = count, label = id, anchor = anchor, motion = motion };
                // Remap only this request's worker output, never shared preparation templates.
                if (customEffect && preparedPointer != null)
                    for (int i = 0; i < count; i++) preparedPointer[i].group = EffectBatch(preparedPointer[i].group, effectShader);
                int runEnd = 0, runSlot = 0;
                for (int glyphIndex = 0; glyphIndex < count; glyphIndex++)
                {
                    var placed = batchedWrite ? default : preparedPointer != null ? preparedPointer[glyphIndex] : PreparedGlyph.From(layout[glyphIndex]);
                    int linkId = AllocateLink();
                    int groupIndex = batchedWrite ? preparedPointer[glyphIndex].group : placed.group;
                    if (customEffect && preparedPointer == null) groupIndex = EffectBatch(groupIndex, effectShader);
                    var group = atlasBatches[groupIndex];
                    var style = placed.style; style.w = id + 1;
                    anchor.x = group.Resource;
                    int slot;
                    if (useContiguousGlyphAllocation)
                    {
                        if (glyphIndex == runEnd)
                        {
                            runEnd = glyphIndex + 1;
                            while (runEnd < count)
                            {
                                int next = preparedPointer != null ? preparedPointer[runEnd].group : layout[runEnd].glyph.group;
                                if (customEffect && preparedPointer == null) next = EffectBatch(next, effectShader);
                                if (atlasBatches[next].Page != group.Page) break;
                                runEnd++;
                            }
                            runSlot = group.Page.ReserveRange(runEnd - glyphIndex);
                        }
                        slot = runSlot >= 0 ? runSlot++ : group.Page.ReserveSlot();
                    }
                    else slot = group.Page.ReserveSlot();
                    if (!batchedWrite) group.Page.Write(slot, anchor, placed.rect, placed.uv, motion, placed.tint, style);
                    if (batchedWrite) instanceWriteSlots[first + glyphIndex] = slot;
                    if (label.glyphCount == 0) label.firstGlyphSlot = slot;
                    else if (slot != label.firstGlyphSlot + label.glyphCount || links[label.tail].group != group.Page.Index)
                        label.contiguousGlyphs = false;
                    label.glyphCount++;
                    links[linkId] = new GlyphLink { group = group.Page.Index, slot = slot, next = -1 };
                    var visibility = (VisibleLink*)visibleLinks.GetUnsafePtr();
                    visibility[linkId] = new VisibleLink { slot = slot, next = -1 };
                    if (label.tail >= 0) { links[label.tail].next = linkId; visibility[label.tail].next = linkId; } else label.head = linkId;
                    label.tail = linkId; ActiveGlyphCount++;
                }
                labels[id] = label;
                if (!label.contiguousGlyphs) { FragmentedLabelCount++; FragmentedGlyphCount += label.glyphCount; }
                ((VisibleLabel*)visibleLabels.GetUnsafePtr())[id] = new VisibleLabel { count=label.glyphCount, first=label.firstGlyphSlot,
                    head=label.head, contiguous=label.contiguousGlyphs ? 1 : 0 };
                SetAnimation(id, animation, amplitude);
                if (label.space == SpaceMode.WorldFollow && (animation != null || label.rise != 0 || label.drift != 0)) animatedWorldLabels++;
                if (animation != null || label.rise != 0 || label.drift != 0) animatedLabels++;
                ActiveCount++; EmittedCount++; orderDirty = true;
                if (!ReferenceEquals(label.target,null)) AttachFollow(id,label.target,targetMatrix);
                return new TextHandle(this, id, label.generation);
        }
        private Matrix4x4 PoseMatrix(Transform target, TextPose pose)
        {
            var rotation = pose.rotation;
            if (rotation.Equals(default(Quaternion))) rotation = Quaternion.identity;
            var local = Matrix4x4.TRS(pose.position + pose.offset, rotation, pose.scale);
            return target != null ? target.localToWorldMatrix * local : local;
        }
        private void SetLabelMatrix(int id, Matrix4x4 matrix, bool force = false)
        {
            ref var label = ref labels[id];
            Vector3 origin = matrix.GetColumn(3);
            SetLabelValue(id, 0, new Vector4(origin.x, origin.y, origin.z, 0), force);
            if (label.space == SpaceMode.WorldFollow)
            {
                Vector3 right = (Vector3)matrix.GetColumn(0) * label.units;
                Vector3 up = (Vector3)matrix.GetColumn(1) * label.units;
                SetLabelValue(id, 1, new Vector4(right.x, right.y, right.z, 0), force);
                SetLabelValue(id, 2, new Vector4(up.x, up.y, up.z, 0), force);
            }
        }
        private void SetLabelValue(int id, int field, Vector4 value, bool force)
        {
            if (!force && labelValues[field][id].Equals(value)) return;
            // New slots must be initialized even when their first position is exactly zero.
            labelValues[field][id] = value;
            if (poseDirty[id] == 0)
            {
                if (poseDirtyCount == 0) poseDirtySorted = true;
                else if (id < poseDirtyIds[poseDirtyCount - 1]) poseDirtySorted = false;
                poseDirtyIds[poseDirtyCount++] = id;
            }
            if((poseDirty[id] & (1<<field))==0)
            {
                poseFieldCount[field]++;
                poseFirst[field]=Math.Min(poseFirst[field],id);poseLast[field]=Math.Max(poseLast[field],id);
            }
            poseDirty[id] |= (byte)(1 << field); orderDirty = true;
            if (!force) existingPoseChanged = true;
        }
        private void UpdateFollowTransforms()
        {
            if (followingLabelCount == 0) return;
            for (int i = followTargets.Count-1; i >= 0; i--)
            {
                var source=followTargets[i];
                if(source.target==null)
                {
                    while(source.head>=0) DetachFollow(source.head); // Freeze at last valid pose.
                    continue;
                }
                Matrix4x4 matrix=source.target.localToWorldMatrix;
                if(matrix.Equals(source.matrix)) continue;
                source.matrix=matrix;
                for(int id=source.head;id>=0;id=labels[id].nextFollow)
                {
                    ref var label=ref labels[id];
                    Vector3 origin=matrix.MultiplyPoint3x4(label.localMatrix.GetColumn(3));
                    SetLabelValue(id,0,new Vector4(origin.x,origin.y,origin.z,0),false);
                    if(label.space==SpaceMode.WorldFollow)
                    {
                        Vector3 right=matrix.MultiplyVector(label.localMatrix.GetColumn(0))*label.units;
                        Vector3 up=matrix.MultiplyVector(label.localMatrix.GetColumn(1))*label.units;
                        SetLabelValue(id,1,new Vector4(right.x,right.y,right.z,0),false);
                        SetLabelValue(id,2,new Vector4(up.x,up.y,up.z,0),false);
                    }
                }
            }
        }
        private void AttachFollow(int id,Transform target,Matrix4x4 matrix)
        {
            if(!targetLookup.TryGetValue(target,out var source))
            {
                source=new FollowTarget { target=target,matrix=matrix,index=followTargets.Count };
                targetLookup.Add(target,source);followTargets.Add(source);
            }
            ref var label=ref labels[id];label.followTarget=source;label.nextFollow=source.head;
            if(source.head>=0) labels[source.head].previousFollow=id;
            source.head=id;followingLabelCount++;
        }
        private void DetachFollow(int id)
        {
            ref var label=ref labels[id];var source=label.followTarget;
            if(source==null) return;
            if(label.previousFollow>=0) labels[label.previousFollow].nextFollow=label.nextFollow;
            else source.head=label.nextFollow;
            if(label.nextFollow>=0) labels[label.nextFollow].previousFollow=label.previousFollow;
            label.followTarget=null;label.target=null;label.previousFollow=label.nextFollow=-1;followingLabelCount--;
            if(source.head<0)
            {
                targetLookup.Remove(source.target);
                int last=followTargets.Count-1;
                var moved=followTargets[last];followTargets[source.index]=moved;moved.index=source.index;
                followTargets.RemoveAt(last);
            }
        }
        private void FlushLabelTransforms()
        {
            if (poseDirtyCount == 0) return;
            if (labelBuffer == null)
            {
                for (int i = 0; i < poseDirtyCount; i++) poseDirty[poseDirtyIds[i]] = 0;
                poseDirtyCount = 0; ResetPoseSpans(); return;
            }
            bool sparse=false;
            for(int field=0;field<3;field++)
                if(poseFieldCount[field]>0 && poseLast[field]-poseFirst[field]+1>poseFieldCount[field]*5/4+8) sparse=true;
            if (sparse && !poseDirtySorted) Array.Sort(poseDirtyIds, 0, poseDirtyCount);
            int bytes = 0;
            for (int field = 0; field < 3; field++)
            {
                if(poseFieldCount[field]==0) continue;
                if(poseLast[field]-poseFirst[field]+1<=poseFieldCount[field]*5/4+8)
                {
                    bytes+=UploadLabelSpan(field,poseFirst[field],poseLast[field]+1);continue;
                }
                // Updating hundreds of tiny ranges costs more submissions than one bounded
                // label-buffer span. Uploading unchanged entries is safe and does not change poses.
                int segments = 0, previous = -1;
                for (int i = 0; useBoundedPoseUploads && i < poseDirtyCount && segments <= 32; i++)
                {
                    int id = poseDirtyIds[i]; if ((poseDirty[id] & (1 << field)) == 0) continue;
                    if (previous < 0 || id > previous + 8) segments++;
                    previous = id;
                }
                if (segments > 32) { bytes += UploadLabelSpan(field, poseFirst[field], poseLast[field] + 1); continue; }
                int first = -1, last = -1;
                for (int i = 0; i < poseDirtyCount; i++)
                {
                    int id = poseDirtyIds[i]; if ((poseDirty[id] & (1 << field)) == 0) continue;
                    if (first >= 0 && id > last + 8)
                    { bytes += UploadLabelSpan(field, first, last + 1); first = -1; }
                    if (first < 0) first = id; last = id;
                }
                if (first >= 0) bytes += UploadLabelSpan(field, first, last + 1);
            }
            for (int i = 0; i < poseDirtyCount; i++) poseDirty[poseDirtyIds[i]] = 0;
            poseDirtyCount = 0;
            ResetPoseSpans();
            if (labelFrame != Time.frameCount) { UploadedTransformBytesLastFrame = TransformUploadCallsLastFrame = 0; labelFrame = Time.frameCount; }
            UploadedTransformBytesLastFrame += bytes;
        }
        private void ResetPoseSpans()
        {
            for(int i=0;i<3;i++) {poseFirst[i]=int.MaxValue;poseLast[i]=-1;poseFieldCount[i]=0;}
        }
        private int UploadLabelSpan(int field, int first, int last)
        {
            labelBuffer.SetData(labelValues[field], first, 4 + field * labels.Length + first, last - first);
            TransformUploadCallsLastFrame++;
            return (last - first) * 16;
        }
        private void ConfigureSpatialMaterial(Material material)
        {
            BindAnimation(material);
            BindEffect(material);
            bool scene = sortingMode == SortingMode.SceneTransparent;
            if (labelBuffer != null) material.SetBuffer("_BurstLabels", labelBuffer);
            material.SetInt("_BurstLabelCapacity", labels.Length);
            material.SetFloat("_BurstZTest", scene ? (float)CompareFunction.LessEqual : (float)CompareFunction.Always);
            material.SetFloat("_BurstSortingMode", (float)sortingMode);
            material.SetFloat("_BurstPlainSdfFastPath", usePlainSdfFastPath ? 1 : 0);
            material.SetFloat("_BurstTime",Now); material.SetVector("_BurstScreen",ScreenParameters());
            material.renderQueue = scene ? 3000 : 5000;
            material.SetShaderPassEnabled("SRPDefaultUnlit", scene);
            material.SetShaderPassEnabled("BurstWordOverlay", !scene);
        }
        private void UpdateSpatial()
        {
            if (appliedPlainSdfFastPath != usePlainSdfFastPath)
            {
                foreach (var page in glyphPages) page.Material.SetFloat("_BurstPlainSdfFastPath", usePlainSdfFastPath ? 1 : 0);
                appliedPlainSdfFastPath = usePlainSdfFastPath;
            }
            if (worldCamera != null) cameraId = BrgObjectIdentity.Of(worldCamera);
            if (labelFrame != Time.frameCount) { UploadedTransformBytesLastFrame = TransformUploadCallsLastFrame = 0; labelFrame = Time.frameCount; }
            if (appliedSortingMode != sortingMode)
            {
                foreach (var batch in glyphPages) ConfigureSpatialMaterial(batch.Material);
                appliedSortingMode = sortingMode; orderDirty = true;
            }
            using (FollowMarker.Auto()) UpdateFollowTransforms();
            using (PoseUploadMarker.Auto()) FlushLabelTransforms();
        }
        private Vector3 SortPosition(int id, float now, Matrix4x4 projection, Matrix4x4 inverseProjection, Vector2 pixelScale)
        {
            Vector3 position = labelValues[0][id]; ref var label = ref labels[id];
            if (label.space == SpaceMode.WorldFollow)
            {
                float t = Mathf.Clamp01((now - label.birth) / label.duration);
                Vector2 offset = AnimationOffset(ref label, t);
                position += (Vector3)labelValues[1][id] * offset.x + (Vector3)labelValues[2][id] * offset.y;
            }
            else if (sortingMode == SortingMode.SceneTransparent && (label.animation != 0 || label.rise != 0 || label.drift != 0))
            {
                float t = Mathf.Clamp01((now-label.birth)/label.duration);
                Vector4 clip = projection * new Vector4(position.x,position.y,position.z,1);
                if (Mathf.Abs(clip.w) > .000001f)
                {
                    clip /= clip.w;
                    Vector2 offset = AnimationOffset(ref label, t);
                    clip.x += offset.x*pixelScale.x; clip.y += offset.y*pixelScale.y;
                    Vector4 world=inverseProjection*clip;
                    if (Mathf.Abs(world.w) > .000001f) position=new Vector3(world.x,world.y,world.z)/world.w;
                }
            }
            return position;
        }
        private unsafe void SortLabels()
        {
            var camera = worldCamera;
            Vector3 position = camera.transform.position, forward = camera.transform.forward;
            TransparencySortMode mode = camera.transparencySortMode;
            Vector3 axis = camera.transparencySortAxis;
            if (mode == TransparencySortMode.Default)
            {
                mode = GraphicsSettings.transparencySortMode; axis = GraphicsSettings.transparencySortAxis;
                if (mode == TransparencySortMode.Default) mode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
            }
            bool cameraUnchanged=position.Equals(orderedCameraPosition) && forward.Equals(orderedCameraForward) &&
                axis.Equals(orderedSortAxis) && mode == orderedSortMode && sortingMode == orderedSortingMode;
            bool animated=sortingMode == SortingMode.SceneTransparent ? animatedLabels > 0 : animatedWorldLabels > 0;
            if (!orderDirty && !animated && cameraUnchanged)
            { SortedInputsLastFrame = 0; LastSortPath = "复用"; return; }
            bool merge = cameraUnchanged && !animated && !existingPoseChanged;
            LastSortPath = merge ? "增量合并" : "全量排序";
            orderedCameraPosition = position; orderedCameraForward = forward; orderedSortAxis = axis; orderedSortMode = mode;
            orderedSortingMode=sortingMode;
            Matrix4x4 projection=Matrix4x4.identity, inverse=Matrix4x4.identity;
            Vector2 pixelScale=Vector2.zero;
            if (sortingMode == SortingMode.SceneTransparent && animatedLabels > animatedWorldLabels)
            {
                projection=camera.projectionMatrix*camera.worldToCameraMatrix; inverse=projection.inverse;
                var screen = ScreenParameters();
                pixelScale = new Vector2(2 * screen.z / screen.x, 2 * screen.z / screen.y);
            }
            float now = Now; int added=0;
            var liveOrders = (ulong*)liveLabelOrders.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < ActiveCount; i++)
            {
                int id = activeLabels[i];
                if (useBurstSortMerge && merge && liveOrders[id] <= lastSortedSequence) continue;
                if (labels[id].head < 0) continue;
                if (merge && labels[id].order <= lastSortedSequence) continue;
                Vector3 point = SortPosition(id, now, projection, inverse, pixelScale), delta = point - position;
                float depth = sortingMode != SortingMode.SceneTransparent ? Vector3.Dot(delta, forward) :
                    mode == TransparencySortMode.Perspective ? delta.sqrMagnitude : Vector3.Dot(point, mode == TransparencySortMode.CustomAxis ? axis : forward);
                sortScratch[added++] = new SortLabel { id = id, depth = depth, depthKey = DepthKey(depth), order = labels[id].order, position = point };
            }
            if (added > 1)
            {
                if(added<128) Array.Sort(sortScratch,0,added,labelComparer);
                else RadixSortLabels(added);
            }
            SortedInputsLastFrame = added;
            if (merge && useBurstSortMerge && sortedCount >= 512)
            {
                ulong keptHandle = 0, newHandle = 0;
                try
                {
                    var kept = (SortLabel*)UnsafeUtility.PinGCArrayAndGetDataAddress(sortedLabels, out keptHandle);
                    var fresh = (SortLabel*)UnsafeUtility.PinGCArrayAndGetDataAddress(sortScratch, out newHandle);
                    // Run the small merge kernel directly: scheduling a worker just to wait would cost more.
                    new MergeSortedLabelsJob { kept = kept, fresh = fresh, liveOrders = liveLabelOrders,
                        oldCount = sortedCount, newCount = added, result = mergeResult }.Run();
                    sortedCount = mergeResult[0];
                }
                finally
                { if (newHandle != 0) UnsafeUtility.ReleaseGCObject(newHandle); if (keptHandle != 0) UnsafeUtility.ReleaseGCObject(keptHandle); }
            }
            else if (merge)
            {
                int kept=0;
                for(int i=0;i<sortedCount;i++)
                {
                    var item=sortedLabels[i];
                    if(labels[item.id].active && labels[item.id].order==item.order) sortedLabels[kept++]=item;
                }
                // Only new labels are sorted; compact expired entries and merge backwards.
                // Existing glyph buffers and the relative order of surviving labels stay intact.
                sortedCount=kept+added;
                int old=kept-1, fresh=added-1;
                for(int to=sortedCount-1;to>=0;to--)
                    sortedLabels[to]=fresh<0 || old>=0 && labelComparer.Compare(sortedLabels[old],sortScratch[fresh])>0
                        ? sortedLabels[old--] : sortScratch[fresh--];
            }
            else
            {
                var swap=sortedLabels; sortedLabels=sortScratch; sortScratch=swap; sortedCount=added;
            }
            lastSortedSequence=labelSequence; existingPoseChanged=false;
            SortedLabelCount = sortedCount; orderDirty = false;
        }

        [Unity.Burst.BurstCompile(FloatMode = Unity.Burst.FloatMode.Strict)]
        private unsafe struct MergeSortedLabelsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public SortLabel* kept;
            [NativeDisableUnsafePtrRestriction] public SortLabel* fresh;
            [ReadOnly] public NativeArray<ulong> liveOrders;
            public NativeArray<int> result;
            public int oldCount, newCount;
            public void Execute()
            {
                int count = 0;
                for (int i = 0; i < oldCount; i++)
                { var item = kept[i]; if (liveOrders[item.id] == item.order) kept[count++] = item; }
                result[0] = count + newCount; int old = count - 1, added = newCount - 1;
                for (int to = count + newCount - 1; to >= 0; to--)
                {
                    bool takeOld = added < 0;
                    if (!takeOld && old >= 0)
                    {
                        int depth = fresh[added].depth.CompareTo(kept[old].depth);
                        takeOld = depth > 0 || depth == 0 && kept[old].order > fresh[added].order;
                    }
                    kept[to] = takeOld ? kept[old--] : fresh[added--];
                }
            }
        }

        private static unsafe uint DepthKey(float depth)
        {
            if(depth==0) depth=0; // +0 and -0 share the same tie order.
            uint bits=*(uint*)&depth;
            return ~(bits ^ ((uint)((int)bits >> 31) | 0x80000000u));
        }
        private void RadixSortLabels(int count)
        {
            ulong varyingOrder=0;uint varyingDepth=0;
            ulong firstOrder=sortScratch[0].order;uint firstDepth=sortScratch[0].depthKey;
            for(int i=0;i<count;i++)
            {
                radixIndices[i]=i;
                varyingOrder|=firstOrder^sortScratch[i].order;
                varyingDepth|=firstDepth^sortScratch[i].depthKey;
            }
            for(int pass=0;pass<12;pass++)
            {
                bool depth=pass>=8;int shift=(depth?pass-8:pass)*8;
                if((depth?(varyingDepth>>shift)&255:(varyingOrder>>shift)&255)==0) continue;
                Array.Clear(radixCounts,0,256);
                for(int i=0;i<count;i++)
                {
                    int index=radixIndices[i];
                    int key=depth?(int)((sortScratch[index].depthKey>>shift)&255):(int)((sortScratch[index].order>>shift)&255);
                    radixCounts[key]++;
                }
                int prefix=0;
                for(int i=0;i<256;i++) { int amount=radixCounts[i];radixCounts[i]=prefix;prefix+=amount; }
                for(int i=0;i<count;i++)
                {
                    int index=radixIndices[i];
                    int key=depth?(int)((sortScratch[index].depthKey>>shift)&255):(int)((sortScratch[index].order>>shift)&255);
                    radixScratch[radixCounts[key]++]=index;
                }
                var swap=radixIndices;radixIndices=radixScratch;radixScratch=swap;
            }
            // Existing order is no longer needed on a full sort. During a large incremental
            // merge preserve it: use the scratch array itself via cycle permutation.
            for(int i=0;i<count;i++)
            {
                int from=radixIndices[i];
                if(from==i) continue;
                var held=sortScratch[i];int to=i;
                while(from!=i)
                {
                    sortScratch[to]=sortScratch[from];radixIndices[to]=to;to=from;from=radixIndices[to];
                }
                sortScratch[to]=held;radixIndices[to]=to;
            }
        }

        private unsafe JobHandle CullSorted(BatchCullingContext context, BatchCullingOutput output)
        {
            using (CullMarker.Auto())
            {
                var commands = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr(); *commands = default;
                if (context.viewType != BatchCullingViewType.Camera || BrgObjectIdentity.Of(context.viewID) != cameraId) return default;
                if (ActiveGlyphCount == 0) { DrawCommandCount = SubmittedGlyphCount = SortedLabelCount = 0; return default; }
                using (SortMarker.Auto()) SortLabels();
                using var visibleScope = VisibleMarker.Auto();
                bool scene = sortingMode == SortingMode.SceneTransparent;
                // Unused layout-resource pages must not disable the single-page Job path.
                GlyphPage singlePage = null;
                foreach (var page in glyphPages)
                    if (page.Count > 0) { if (singlePage != null) { singlePage = null; break; } singlePage = page; }
                int commandCount = singlePage != null ? (scene ? sortedCount : 1) : 0, previousGroup = -1;
                for (int i = 0; singlePage == null && i < sortedCount; i++)
                {
                    if (scene) previousGroup = -1;
                    for (int link = labels[sortedLabels[i].id].head; link >= 0; link = links[link].next)
                        if (links[link].group != previousGroup) { commandCount++; previousGroup = links[link].group; }
                }
                if (commandCount == 0) return default;
                int alignment = UnsafeUtility.AlignOf<long>();
                commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(sizeof(BatchDrawCommand) * commandCount, alignment, Allocator.TempJob);
                commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(sizeof(BatchDrawRange), alignment, Allocator.TempJob);
                commands->visibleInstances = (int*)UnsafeUtility.Malloc(sizeof(int) * ActiveGlyphCount, alignment, Allocator.TempJob);
                if (scene)
                {
                    commands->instanceSortingPositions = (float*)UnsafeUtility.Malloc(sizeof(float) * ActiveGlyphCount * 3, alignment, Allocator.TempJob);
                    commands->instanceSortingPositionFloatCount = ActiveGlyphCount * 3;
                }
                commands->drawCommandCount = commandCount; commands->drawRangeCount = 1;
                commands->visibleInstanceCount = ActiveGlyphCount;
                int draw = -1, offset = 0; previousGroup = -1;
                JobHandle visibleJob = default;
                if (singlePage != null && useContiguousIndexFastPath)
                {
                    var spans = useVisibleIndexJobs ? new NativeArray<VisibleSpan>(sortedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory) : default;
                    var batch = singlePage;
                    var template = new BatchDrawCommand { batchID = batch.BatchId, materialID = batch.MaterialId,
                        meshID = meshId, submeshIndex = 0, splitVisibilityMask = 0xff,
                        flags = scene ? BatchDrawCommandFlags.HasSortingPosition : BatchDrawCommandFlags.None };
                    if (!scene) { template.visibleCount = (uint)ActiveGlyphCount; commands->drawCommands[0] = template; }
                    if (useVisibleIndexJobs && useBurstVisibleInputs && useLinkedIndexJobs)
                    {
                        ulong pin=0;
                        try
                        {
                            var sorted=(SortLabel*)UnsafeUtility.PinGCArrayAndGetDataAddress(sortedLabels,out pin);
                            new BuildVisibleInputsJob { labels=visibleLabels, sorted=sorted, count=sortedCount, spans=(VisibleSpan*)spans.GetUnsafePtr(),
                                commands=commands->drawCommands, template=template, scene=scene, linkedJobs=useLinkedIndexJobs }.Run();
                            offset=ActiveGlyphCount;
                        }
                        finally { if(pin!=0) UnsafeUtility.ReleaseGCObject(pin); }
                    }
                    else for (int i = 0; i < sortedCount; i++)
                    {
                        var sorted = sortedLabels[i]; ref var label = ref labels[sorted.id];
                        if (scene)
                        {
                            template.visibleOffset = (uint)offset; template.visibleCount = (uint)label.glyphCount;
                            template.sortingPosition = offset * 3; commands->drawCommands[i] = template;
                        }
                        if (useVisibleIndexJobs) spans[i] = new VisibleSpan { offset = offset, count = label.glyphCount,
                            first = label.contiguousGlyphs ? label.firstGlyphSlot : useLinkedIndexJobs ? -1 : -2, head = label.head, position = sorted.position };
                        else
                        {
                            if (label.contiguousGlyphs)
                                for (int j = 0; j < label.glyphCount; j++) commands->visibleInstances[offset + j] = label.firstGlyphSlot + j;
                            if (scene)
                                for (int j = offset, end = offset + label.glyphCount; j < end; j++)
                                { commands->instanceSortingPositions[j * 3] = sorted.position.x; commands->instanceSortingPositions[j * 3 + 1] = sorted.position.y; commands->instanceSortingPositions[j * 3 + 2] = sorted.position.z; }
                        }
                        if (!label.contiguousGlyphs && (!useVisibleIndexJobs || !useLinkedIndexJobs))
                        {
                            int at = offset;
                            for (int link = label.head; link >= 0; link = links[link].next)
                                commands->visibleInstances[at++] = links[link].slot;
                        }
                        offset += label.glyphCount;
                    }
                    if (useVisibleIndexJobs)
                    {
                        visibleJob = new FillVisibleSpansJob { spans = spans, links = visibleLinks, indices = commands->visibleInstances,
                            positions = commands->instanceSortingPositions }.Schedule(sortedCount, 32);
                        visibleJob = spans.Dispose(visibleJob);
                        visibilityWork = visibilityWorkPending ? JobHandle.CombineDependencies(visibilityWork, visibleJob) : visibleJob;
                        visibilityWorkPending = true;
                    }
                }
                else
                for (int i = 0; i < sortedCount; i++)
                {
                    var label = sortedLabels[i]; if (scene) previousGroup = -1;
                    for (int link = labels[label.id].head; link >= 0; link = links[link].next)
                    {
                        var item = links[link];
                        if (item.group != previousGroup)
                        {
                            var batch = glyphPages[item.group];
                            commands->drawCommands[++draw] = new BatchDrawCommand { visibleOffset = (uint)offset,
                                batchID = batch.BatchId, materialID = batch.MaterialId, meshID = meshId,
                                submeshIndex = 0, splitVisibilityMask = 0xff,
                                flags = scene ? BatchDrawCommandFlags.HasSortingPosition : BatchDrawCommandFlags.None,
                                sortingPosition = scene ? offset * 3 : 0 };
                            previousGroup = item.group;
                        }
                        commands->drawCommands[draw].visibleCount++;
                        commands->visibleInstances[offset] = item.slot;
                        if (scene)
                        {
                            commands->instanceSortingPositions[offset * 3] = label.position.x;
                            commands->instanceSortingPositions[offset * 3 + 1] = label.position.y;
                            commands->instanceSortingPositions[offset * 3 + 2] = label.position.z;
                        }
                        offset++;
                    }
                }
                commands->drawRanges[0] = new BatchDrawRange { drawCommandsCount = (uint)commandCount,
                    filterSettings = new BatchFilterSettings { renderingLayerMask = uint.MaxValue, layer = (byte)gameObject.layer,
                        shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, allDepthSorted = scene } };
                DrawCommandCount = commandCount; SubmittedGlyphCount = offset;
                // Unity waits for this handle before consuming the BRG output. No main-thread Complete.
                return visibleJob;
            }
        }
        private struct VisibleSpan { public int offset, count, first, head; public Vector3 position; }
        [Unity.Burst.BurstCompile]
        private unsafe struct BuildVisibleInputsJob : IJob
        {
            [ReadOnly] public NativeArray<VisibleLabel> labels;
            [NativeDisableUnsafePtrRestriction] public SortLabel* sorted;
            [NativeDisableUnsafePtrRestriction] public VisibleSpan* spans;
            [NativeDisableUnsafePtrRestriction] public BatchDrawCommand* commands;
            public BatchDrawCommand template;
            public int count;
            public bool scene, linkedJobs;
            public void Execute()
            {
                var data=(VisibleLabel*)labels.GetUnsafeReadOnlyPtr(); int offset=0;
                for(int i=0;i<count;i++)
                {
                    var item=sorted[i]; var label=data[item.id];
                    spans[i]=new VisibleSpan { offset=offset, count=label.count, head=label.head,
                        first=label.contiguous!=0 ? label.first : linkedJobs ? -1 : -2, position=item.position };
                    if(scene) { var command=template; command.visibleOffset=(uint)offset; command.visibleCount=(uint)label.count;
                        command.sortingPosition=offset*3; commands[i]=command; }
                    offset+=label.count;
                }
            }
        }
        [Unity.Burst.BurstCompile]
        private unsafe struct FillVisibleSpansJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<VisibleSpan> spans;
            [ReadOnly] public NativeArray<VisibleLink> links;
            [NativeDisableUnsafePtrRestriction] public int* indices;
            [NativeDisableUnsafePtrRestriction] public float* positions;
            public void Execute(int index)
            {
                var span = spans[index];
                if (span.first >= 0)
                    for (int i = 0; i < span.count; i++) indices[span.offset + i] = span.first + i;
                else if (span.first == -1)
                {
                    var data = (VisibleLink*)links.GetUnsafeReadOnlyPtr();
                    int at = span.offset;
                    for (int link = span.head; link >= 0; link = data[link].next) indices[at++] = data[link].slot;
                }
                if (positions != null)
                    for (int i = span.offset, end = span.offset + span.count; i < end; i++)
                    { positions[i * 3] = span.position.x; positions[i * 3 + 1] = span.position.y; positions[i * 3 + 2] = span.position.z; }
            }
        }
    }
}
