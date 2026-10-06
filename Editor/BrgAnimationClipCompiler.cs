using System;
using System.Collections.Generic;
using BurstWord.BRG;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BurstWord.Baseline.Editor
{
    [InitializeOnLoad]
    public static class BrgAnimationClipCompiler
    {
        private sealed class Entry { public BrgTextAnimation preset; public int revision = -1; }
        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly HashSet<AnimationClip> pending = new HashSet<AnimationClip>();
        private static bool rescan = true, processing;
        private static double nextCheck;
        public static string LastRestriction { get; private set; }
        public static readonly string[] Properties = { "m_LocalPosition.x", "m_LocalPosition.y", "localEulerAnglesRaw.z", "m_LocalScale.x", "m_LocalScale.y", "opacity", "brightness", "color.r", "color.g", "color.b", "color.a" };
        static BrgAnimationClipCompiler()
        {
            AnimationUtility.onCurveWasModified += CurveChanged;
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += () => { rescan = true; foreach (var e in entries) if (e.preset != null && e.preset.animationClip != null) pending.Add(e.preset.animationClip); };
        }
        internal static void Invalidate() { rescan = true; }
        private static void Scan()
        {
            if (!rescan) return;
            rescan = false; entries.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:BrgTextAnimation"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<BrgTextAnimation>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) entries.Add(new Entry { preset = asset });
            }
        }
        private static void CurveChanged(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType change)
        { if (!processing && clip != null) pending.Add(clip); }
        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + .15;
            Process();
        }
        public static void Process()
        {
            if (processing) return;
            processing = true;
            try
            {
                Scan();
                foreach (var entry in entries)
                {
                    if (entry.preset == null || entry.preset.animationClip == null) continue;
                    if (entry.revision != entry.preset.Revision || pending.Contains(entry.preset.animationClip))
                    { Compile(entry.preset); entry.revision = entry.preset.Revision; }
                }
                pending.Clear();
            }
            finally { processing = false; }
        }
        public static EditorCurveBinding Binding(string property)
            => EditorCurveBinding.FloatCurve("", property.StartsWith("m_Local") || property.StartsWith("localEuler") ? typeof(Transform) : typeof(BrgAnimationAuthoring), property);
        private static bool Neutral(EditorCurveBinding binding, out float value)
        {
            value = 0;
            if (binding.type != typeof(Transform) || !string.IsNullOrEmpty(binding.path)) return false;
            if (binding.propertyName == "m_LocalScale.z") { value = 1; return true; }
            return binding.propertyName == "m_LocalPosition.z" || binding.propertyName == "localEulerAnglesRaw.x" || binding.propertyName == "localEulerAnglesRaw.y";
        }
        public static bool Allowed(EditorCurveBinding binding)
        {
            if (!string.IsNullOrEmpty(binding.path) || binding.isPPtrCurve) return false;
            foreach (string property in Properties)
            { var accepted = Binding(property); if (binding.type == accepted.type && binding.propertyName == property) return true; }
            return Neutral(binding, out _);
        }
        private static bool IsConstant(AnimationCurve curve, float value)
        {
            if (curve == null || curve.length == 0) return true;
            foreach (var key in curve.keys)
                if (Mathf.Abs(key.value - value) > .00001f || (!float.IsInfinity(key.inTangent) && Mathf.Abs(key.inTangent) > .00001f) || (!float.IsInfinity(key.outTangent) && Mathf.Abs(key.outTangent) > .00001f)) return false;
            return true;
        }
        public static string Validate(AnimationClip clip)
        {
            if (clip == null) return "请选择 AnimationClip。";
            if (clip.length <= 0 || float.IsNaN(clip.length) || float.IsInfinity(clip.length)) return "Clip 必须有正的时长。";
            if (clip.isHumanMotion) return "飘字 Clip 不支持 Humanoid 动画。";
            if (clip.legacy) return "请使用非 Legacy 的 AnimationClip，固定轨道将编译为 GPU 采样数据。";
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0) return "不允许物体、材质或资源引用动画。";
            if (AnimationUtility.GetAnimationEvents(clip).Length != 0) return "不允许 Animation Events：GPU 播放不会调用事件。";
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!Allowed(binding)) return "不支持的绑定：" + (string.IsNullOrEmpty(binding.path) ? "根物体" : binding.path) + " / " + binding.type.Name + "." + binding.propertyName;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (Neutral(binding, out float value) && !IsConstant(curve, value)) return binding.propertyName + " 必须固定为 " + value + "；只支持 XY 位移、Z 旋转和 XY 缩放。";
                if (curve != null) foreach (var key in curve.keys)
                    if (key.time < 0 || float.IsNaN(key.time) || float.IsInfinity(key.time) || float.IsNaN(key.value) || float.IsInfinity(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent) || float.IsNaN(key.inWeight) || float.IsNaN(key.outWeight)) return "关键帧的时间和值必须是有限数，时间不能为负。";
            }
            return null;
        }
        public static void EnforceBindings(AnimationClip clip)
        {
            if (clip == null) return;
            bool previous = processing; processing = true;
            try
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!Allowed(binding))
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        LastRestriction = "已拒绝非固定属性或子物体轨道：" + binding.path + " / " + binding.propertyName;
                    }
                    else if (Neutral(binding, out float value) && !IsConstant(AnimationUtility.GetEditorCurve(clip, binding), value))
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0, value, Mathf.Max(.0001f, clip.length), value));
                        LastRestriction = "已将 " + binding.propertyName + " 锁定为 " + value + "。";
                    }
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                { AnimationUtility.SetObjectReferenceCurve(clip, binding, null); LastRestriction = "已拒绝资源引用动画。"; }
                if (AnimationUtility.GetAnimationEvents(clip).Length > 0)
                { AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]); LastRestriction = "已拒绝 Animation Events。"; }
            }
            finally { processing = previous; }
        }
        public static bool Compile(BrgTextAnimation preset)
        {
            if (preset == null || preset.animationClip == null) return true;
            int previousRevision = preset.Revision;
            var clip = preset.animationClip; string error = Validate(clip);
            if (error != null) { preset.SetCompiledClip(null, clip.length, error); if (preset.Revision != previousRevision) EditorUtility.SetDirty(preset); return false; }
            var curves = new AnimationCurve[Properties.Length];
            for (int i = 0; i < curves.Length; i++) curves[i] = AnimationUtility.GetEditorCurve(clip, Binding(Properties[i]));
            float Value(int index, float time, float fallback) => curves[index] != null && curves[index].length > 0 ? curves[index].Evaluate(time) : fallback;
            var samples = new Color[BrgTextAnimation.SampleCount * BrgTextAnimation.Rows];
            for (int i = 0; i < BrgTextAnimation.SampleCount; i++)
            {
                float time = (float)i / (BrgTextAnimation.SampleCount - 1) * clip.length;
                samples[i] = new Color(Value(0, time, 0), Value(1, time, 0), Value(3, time, 1), Value(4, time, 1));
                samples[i + BrgTextAnimation.SampleCount] = new Color(Value(2, time, 0) * Mathf.Deg2Rad, Mathf.Clamp01(Value(5, time, 1)), Mathf.Max(0, Value(6, time, 1)), preset.addHorizontalDrift ? 1 : 0);
                samples[i + BrgTextAnimation.SampleCount * 2] = new Color(Value(7, time, 1), Value(8, time, 1), Value(9, time, 1), Mathf.Clamp01(Value(10, time, 1)));
            }
            preset.SetCompiledClip(samples, clip.length, null); if (preset.Revision != previousRevision) EditorUtility.SetDirty(preset); return true;
        }
        public static void CompileAll()
        {
            rescan = true; Scan();
            foreach (var e in entries)
                if (!Compile(e.preset)) throw new BuildFailedException("BurstWord 动画预设 " + AssetDatabase.GetAssetPath(e.preset) + ": " + e.preset.ClipError);
            AssetDatabase.SaveAssets();
        }
    }
    internal sealed class BrgClipImports : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        { BrgAnimationClipCompiler.Invalidate(); }
    }
    internal sealed class BrgClipBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => BrgAnimationClipCompiler.CompileAll();
    }
    internal sealed class BrgClipSave : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            BrgAnimationClipCompiler.Process();
            return paths;
        }
    }
}
