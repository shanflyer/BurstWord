using System;
using BurstWord.BRG;
using UnityEditor;
using UnityEngine;

namespace BurstWord.Baseline.Editor
{
    // The editor can only address this fixed mapping. No hierarchy, arbitrary
    // component binding or AnimationEvent can be created through this API.
    public static class BrgAnimationClipEditing
    {
        public static bool CanEdit(AnimationClip clip) => clip!=null && AssetDatabase.IsMainAsset(clip) &&
            AssetDatabase.GetAssetPath(clip).EndsWith(".anim",StringComparison.OrdinalIgnoreCase) && AssetDatabase.IsOpenForEdit(clip);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static EditorCurveBinding Binding(int track)
        {
            if (track < 0 || track >= BrgAnimationClipCompiler.Properties.Length) throw new ArgumentOutOfRangeException(nameof(track));
            return BrgAnimationClipCompiler.Binding(BrgAnimationClipCompiler.Properties[track]);
        }
        public static AnimationCurve Curve(AnimationClip clip, int track)
        {
            var binding = Binding(track);
            return clip != null ? AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve() : new AnimationCurve();
        }
        public static float Value(AnimationClip clip, int track, float time)
        {
            var curve = Curve(clip, track);
            return curve.length > 0 ? curve.Evaluate(time) : track >= 3 ? 1 : 0;
        }
        private static AnimationClip Check(BrgTextAnimation preset)
        {
            if (preset == null || preset.animationClip == null) throw new InvalidOperationException("请选择 AnimationClip。");
            string error = BrgAnimationClipCompiler.Validate(preset.animationClip);
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 模式再编辑动画。");
            if (!CanEdit(preset.animationClip)) throw new InvalidOperationException("Clip 为只读资源，请复制为可编辑 .anim。");
            return preset.animationClip;
        }
        private static float Clamp(int track, float value)
        {
            if (!Finite(value)) throw new ArgumentException("关键帧值必须为有限数。");
            if (track == 5 || track == 10) return Mathf.Clamp01(value);
            if (track == 6) return Mathf.Max(0,value);
            return value;
        }
        private static void Commit(BrgTextAnimation preset, AnimationClip clip, int track, AnimationCurve curve, string action)
        {
            Undo.RecordObject(clip, action); AnimationUtility.SetEditorCurve(clip, Binding(track), curve);
            EditorUtility.SetDirty(clip); BrgAnimationClipCompiler.Compile(preset);
        }
        public static void SetKey(BrgTextAnimation preset, int track, float time, float value)
        {
            var clip = Check(preset); var curve = Curve(clip, track);
            if (!Finite(time)) throw new ArgumentException("时间必须为有限数。");
            time = Mathf.Clamp(time,0,clip.length); value = Clamp(track, value);
            int index = -1;
            for (int i = 0; i < curve.length; i++) if (Mathf.Abs(curve[i].time-time) < .0001f) { index=i; break; }
            if (index >= 0) { var key = curve[index]; key.value = value; curve.MoveKey(index,key); }
            else { index = curve.AddKey(time,value); if(index>=0) AnimationUtility.SetKeyBroken(curve,index,false); }
            Commit(preset,clip,track,curve,"录制飘字关键帧");
        }
        public static void SetCurve(BrgTextAnimation preset, int track, AnimationCurve curve)
        {
            var clip = Check(preset); Binding(track);
            if(curve==null || curve.length==0) curve=AnimationCurve.Linear(0,track>=3?1:0,clip.length,track>=3?1:0);
            var safe = new AnimationCurve { preWrapMode=WrapMode.ClampForever, postWrapMode=WrapMode.ClampForever };
            foreach (var original in curve.keys)
            {
                if(!Finite(original.time) || float.IsNaN(original.inTangent) || float.IsNaN(original.outTangent)) throw new ArgumentException("关键帧时间和切线无效。");
                var key=original; key.time=Mathf.Clamp(key.time,0,clip.length); key.value=Clamp(track,key.value); int index=safe.AddKey(key);
                if(index>=0)
                {
                    int source=Array.IndexOf(curve.keys,original);
                    AnimationUtility.SetKeyBroken(safe,index,AnimationUtility.GetKeyBroken(curve,source));
                    AnimationUtility.SetKeyLeftTangentMode(safe,index,AnimationUtility.GetKeyLeftTangentMode(curve,source));
                    AnimationUtility.SetKeyRightTangentMode(safe,index,AnimationUtility.GetKeyRightTangentMode(curve,source));
                }
            }
            Commit(preset,clip,track,safe,"编辑飘字曲线");
        }
        public static int MoveKey(BrgTextAnimation preset, int track, int index, float time)
        {
            var clip=Check(preset); var curve=Curve(clip,track);
            if(index<0 || index>=curve.length) return -1;
            if(!Finite(time)) throw new ArgumentException("时间必须为有限数。");
            time=Mathf.Clamp(time,0,clip.length);
            // Keep distinct keys; dragging cannot silently overwrite a neighbour.
            float low=index>0?curve[index-1].time+.0001f:0, high=index+1<curve.length?curve[index+1].time-.0001f:clip.length;
            time=Mathf.Clamp(time,low,Mathf.Max(low,high));
            var key=curve[index]; key.time=time; int moved=curve.MoveKey(index,key);
            Commit(preset,clip,track,curve,"移动飘字关键帧"); return moved;
        }
        public static void DeleteKey(BrgTextAnimation preset, int track, int index)
        {
            var clip=Check(preset); var curve=Curve(clip,track);
            if(index<0 || index>=curve.length || curve.length<=1) return;
            curve.RemoveKey(index); Commit(preset,clip,track,curve,"删除飘字关键帧");
        }
        public static void Resize(BrgTextAnimation preset, float duration)
        {
            var clip=Check(preset);
            if(!Finite(duration) || duration<=.001f) throw new ArgumentException("时长必须为正数。");
            float factor=duration/clip.length; Undo.RecordObject(clip,"修改飘字 Clip 时长");
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,binding); var keys=curve.keys;
                for(int i=0;i<keys.Length;i++) { keys[i].time*=factor; keys[i].inTangent/=factor; keys[i].outTangent/=factor; }
                curve.keys=keys; AnimationUtility.SetEditorCurve(clip,binding,curve);
            }
            EditorUtility.SetDirty(clip); BrgAnimationClipCompiler.Compile(preset);
        }
    }
}
