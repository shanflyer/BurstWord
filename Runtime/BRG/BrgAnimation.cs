using System.Collections.Generic;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Header("GPU animation")]
        [Tooltip("Null keeps the original low-cost linear animation. Curves are shared GPU samples; no per-label CPU curve evaluation.")]
        public BrgTextAnimation defaultAnimation;
        public BrgTextAnimation[] animationPresets = new BrgTextAnimation[0];
        private sealed class AnimationEntry
        {
            public BrgTextAnimation asset;
            public int revision = int.MinValue;
            public readonly Color[] samples = new Color[BrgTextAnimation.SampleCount * BrgTextAnimation.Rows];
        }
        private readonly List<AnimationEntry> animationEntries = new List<AnimationEntry>();
        private readonly Dictionary<BrgTextAnimation, int> animationLookup = new Dictionary<BrgTextAnimation, int>();
        private Vector4[] animationLabels;
        private GraphicsBuffer animationLabelBuffer;
        private Texture2D animationTexture;
        private int animationFirst = int.MaxValue, animationLast = -1, animationRows, animationFrame = -1;
        private int activeAnimationLabels;
        private bool appliedAnimationEnabled;
        public int RegisteredAnimationCount => animationEntries.Count;
        public int AnimationUploadBytesLastFrame { get; private set; }
        public int AnimationBakeCount { get; private set; }
        private int animationUploadFrame = -1;
        private void InitializeAnimations()
        {
            animationLabels = new Vector4[Capacity];
            if (UsingBrg)
            {
                animationLabelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, 16);
                animationLabelBuffer.SetData(animationLabels);
            }
        }
        private int RegisterAnimation(BrgTextAnimation asset)
        {
            if (asset == null) return 0;
            if (animationLookup.TryGetValue(asset, out int index)) return index + 1;
            index = animationEntries.Count;
            animationEntries.Add(new AnimationEntry { asset = asset });
            animationLookup.Add(asset, index);
            animationFrame = -1;
            return index + 1;
        }
        private void SetAnimation(int id, BrgTextAnimation asset, float amplitude)
        {
            int index = RegisterAnimation(asset);
            if (index == 0 && animationEntries.Count == 0) return;
            labels[id].animation = index;
            if (index != 0) activeAnimationLabels++;
            labels[id].animationAmplitude = amplitude;
            var value = new Vector4(index, amplitude, 0, 0);
            // A recycled slot usually keeps the same preset and amplitude. Its GPU
            // metadata is already correct: only birth/life data needs uploading.
            if (animationLabels[id].Equals(value)) return;
            animationLabels[id] = value;
            animationFirst = Mathf.Min(animationFirst, id); animationLast = Mathf.Max(animationLast, id);
        }
        private void BindAnimation(Material material)
        {
            if (animationLabelBuffer != null) material.SetBuffer("_BurstAnimationLabels", animationLabelBuffer);
            material.SetTexture("_BurstAnimationCurves", animationTexture != null ? animationTexture : Texture2D.whiteTexture);
            material.SetVector("_BurstAnimationInfo", new Vector4(activeAnimationLabels > 0 ? 1 : 0,
                BrgTextAnimation.SampleCount, Mathf.Max(1, animationRows), 0));
        }
        private void UpdateAnimations()
        {
            if (animationUploadFrame != Time.frameCount) { AnimationUploadBytesLastFrame = 0; animationUploadFrame = Time.frameCount; }
            if (animationFrame != Time.frameCount)
            {
                animationFrame = Time.frameCount;
                bool rebuild = animationEntries.Count * BrgTextAnimation.Rows > animationRows;
                if (rebuild)
                {
                    if (animationTexture != null) DestroyAnimationTexture();
                    animationRows = Mathf.NextPowerOfTwo(Mathf.Max(4, animationEntries.Count * BrgTextAnimation.Rows));
                    animationTexture = new Texture2D(BrgTextAnimation.SampleCount, animationRows, (SystemInfo.IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat, UnityEngine.Experimental.Rendering.FormatUsage.Linear) ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf), false, true)
                        { name = "BurstWord shared animation curves", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                }
                bool dirty = rebuild;
                for (int i = 0; i < animationEntries.Count; i++)
                {
                    var entry = animationEntries[i];
                    int revision = entry.asset != null ? entry.asset.Revision : entry.revision;
                    if (!rebuild && entry.revision == revision) continue;
                    if (entry.asset != null) entry.asset.Bake(entry.samples, 0);
                    animationTexture.SetPixels(0, i * BrgTextAnimation.Rows, BrgTextAnimation.SampleCount, BrgTextAnimation.Rows, entry.samples);
                    entry.revision = revision; dirty = true; AnimationBakeCount++;
                }
                if (dirty)
                {
                    animationTexture.Apply(false, false);
                    AnimationUploadBytesLastFrame += BrgTextAnimation.SampleCount * animationRows * (animationTexture.format == TextureFormat.RGBAFloat ? 16 : 8);
                    foreach (var page in glyphPages) BindAnimation(page.Material);
                    orderDirty = true;
                }
            }
            bool enabled = activeAnimationLabels > 0;
            if (appliedAnimationEnabled != enabled)
            {
                foreach (var page in glyphPages) BindAnimation(page.Material);
                appliedAnimationEnabled = enabled;
            }
            if (animationFirst <= animationLast)
            {
                int count = animationLast - animationFirst + 1;
                if (animationLabelBuffer != null)
                {
                    animationLabelBuffer.SetData(animationLabels, animationFirst, animationFirst, count);
                    AnimationUploadBytesLastFrame += count * 16;
                }
                animationFirst = int.MaxValue; animationLast = -1;
            }
        }
        // Sorting alone needs the animated center in world space. It reads the same baked
        // samples; curves are never evaluated per label and no Transform is animated here.
        private Vector2 AnimationOffset(ref Label label, float t)
        {
            if (label.animation == 0) return new Vector2(label.drift * t, label.rise * t);
            var samples = animationEntries[label.animation - 1].samples;
            float p = t * (BrgTextAnimation.SampleCount - 1); int first = Mathf.FloorToInt(p), last = Mathf.Min(first + 1, BrgTextAnimation.SampleCount - 1);
            Color value = Color.LerpUnclamped(samples[first], samples[last], p - first);
            Color options = samples[BrgTextAnimation.SampleCount + first];
            return new Vector2(value.r * label.animationAmplitude + label.drift * t * options.a, value.g * label.animationAmplitude);
        }
        private void DestroyAnimationTexture()
        {
            if (Application.isPlaying) Destroy(animationTexture); else DestroyImmediate(animationTexture);
            animationTexture = null;
        }
        private void DisposeAnimations()
        {
            animationLabelBuffer?.Dispose(); animationLabelBuffer = null;
            if (animationTexture != null) DestroyAnimationTexture();
            animationEntries.Clear(); animationLookup.Clear(); animationLabels = null;
            activeAnimationLabels = 0; appliedAnimationEnabled = false;
            animationRows = 0; animationFrame = -1; animationFirst = int.MaxValue; animationLast = -1;
        }
#if UNITY_EDITOR
        private bool previewClock;
        private float previewTime;
        public void EditorDispose() => OnDisable();
        public void EditorPreviewFrame(float time)
        {
            previewClock = true; previewTime = time; Initialize();
            // Time.frameCount does not advance during manual Edit-mode rendering.
            animationFrame = -1; animationUploadFrame = -1; labelFrame = -1; LateUpdate();
        }
#endif
    }
}
