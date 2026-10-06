using System;
using System.IO;
using BurstWord.BRG;
using UnityEditor;
using UnityEngine;
namespace BurstWord.Baseline.Editor
{
    // Clip initialization only. The independent editor creates no authoring
    // prefab, controller, Animator, visible target or Scene editing session.
    public static class BrgAnimationAuthoringSession
    {
        private static string AuthoringFolder(BrgTextAnimation preset)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(preset)).Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("请先把动画预设保存为项目资源。");
            if (!AssetDatabase.IsValidFolder(folder + "/Authoring")) AssetDatabase.CreateFolder(folder, "Authoring");
            return folder + "/Authoring";
        }
        public static AnimationClip CreateClip(BrgTextAnimation preset)
        {
            if (preset.animationClip != null) return preset.animationClip;
            string folder = AuthoringFolder(preset);
            var clip = new AnimationClip { name = preset.name, frameRate = 60 };
            var curves = new[] { preset.positionX, preset.positionY, preset.rotation, preset.scaleX, preset.scaleY, preset.opacity, preset.brightness };
            for (int i = 0; i < curves.Length; i++)
                AnimationUtility.SetEditorCurve(clip, BrgAnimationClipCompiler.Binding(BrgAnimationClipCompiler.Properties[i]), curves[i] ?? AnimationCurve.Linear(0, i == 3 || i == 4 || i == 5 || i == 6 ? 1 : 0, 1, i == 3 || i == 4 || i == 5 || i == 6 ? 1 : 0));
            var times = new System.Collections.Generic.SortedSet<float> { 0, 1 };
            if (preset.color != null) { foreach (var key in preset.color.colorKeys) times.Add(key.time); foreach (var key in preset.color.alphaKeys) times.Add(key.time); }
            for (int channel = 0; channel < 4; channel++)
            {
                var curve = new AnimationCurve();
                foreach (float time in times)
                {
                    Color color = preset.color != null ? preset.color.Evaluate(time) : Color.white;
                    curve.AddKey(time, color[channel]);
                }
                for (int i = 0; i < curve.length; i++)
                {
                    var mode = preset.color != null && preset.color.mode == GradientMode.Fixed ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear;
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, mode); AnimationUtility.SetKeyRightTangentMode(curve, i, mode);
                }
                AnimationUtility.SetEditorCurve(clip, BrgAnimationClipCompiler.Binding(BrgAnimationClipCompiler.Properties[7 + channel]), curve);
            }
            AddNeutralTracks(clip);
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(preset)) + ".anim"));
            Undo.RecordObject(preset, "使用 AnimationClip 制作飘字"); preset.animationClip = clip;
            BrgAnimationClipCompiler.Compile(preset); EditorUtility.SetDirty(preset); AssetDatabase.SaveAssets();
            return clip;
        }
        public static void AddNeutralTracks(AnimationClip clip)
        {
            foreach (string property in new[] { "m_LocalPosition.z", "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "m_LocalScale.z" })
                if (AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), property)) == null)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), property), AnimationCurve.Linear(0, property == "m_LocalScale.z" ? 1 : 0, Mathf.Max(.0001f, clip.length), property == "m_LocalScale.z" ? 1 : 0));
        }
        public static void EnsureFixedTracks(AnimationClip clip)
        {
            float duration = clip.length > 0 ? clip.length : 1;
            for (int i = 0; i < BrgAnimationClipCompiler.Properties.Length; i++)
            {
                var binding = BrgAnimationClipCompiler.Binding(BrgAnimationClipCompiler.Properties[i]);
                if (AnimationUtility.GetEditorCurve(clip, binding) != null) continue;
                float value = i >= 3 ? 1 : 0;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0, value, duration, value));
            }
            AddNeutralTracks(clip);
        }
    }
}
