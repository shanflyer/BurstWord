using UnityEngine;

namespace BurstWord.BRG
{
    /// <summary>Authoring curves are baked once into a shared GPU lookup table.</summary>
    [CreateAssetMenu(menuName = "BurstWord/Text Animation Preset", fileName = "Text Animation")]
    public sealed class BrgTextAnimation : ScriptableObject
    {
        public const int SampleCount = 256;
        public const int Rows = 3;
        [Tooltip("Edit this clip in Unity's Animation window. Only the fixed BurstWord authoring bindings are accepted.")]
        public AnimationClip animationClip;
        [SerializeField, HideInInspector] private Color[] clipSamples;
        [SerializeField, HideInInspector] private float clipDuration;
        [SerializeField, HideInInspector] private string clipError;
        public float ClipDuration => clipDuration;
        public string ClipError => clipError;
        public bool HasClipSamples => clipSamples != null && clipSamples.Length == SampleCount * Rows;
#if UNITY_EDITOR
        public void SetCompiledClip(Color[] samples, float duration, string error)
        {
            if (clipError == error && (samples == null || (clipDuration == duration && SameSamples(samples)))) return;
            if (samples != null) { clipSamples = samples; clipDuration = duration; }
            clipError = error; NotifyChanged();
        }
        private bool SameSamples(Color[] samples)
        {
            if (clipSamples == null || clipSamples.Length != samples.Length) return false;
            for (int i = 0; i < samples.Length; i++) if (!clipSamples[i].Equals(samples[i])) return false;
            return true;
        }
#endif
        public AnimationCurve positionX = AnimationCurve.Linear(0, 0, 1, 0);
        public AnimationCurve positionY = AnimationCurve.Linear(0, 0, 1, 90);
        public AnimationCurve scaleX = AnimationCurve.Linear(0, 1, 1, 1);
        public AnimationCurve scaleY = AnimationCurve.Linear(0, 1, 1, 1);
        public AnimationCurve rotation = AnimationCurve.Linear(0, 0, 1, 0);
        public AnimationCurve opacity = new AnimationCurve(new Keyframe(0, 1), new Keyframe(.5f, 1), new Keyframe(1, 0));
        public AnimationCurve brightness = AnimationCurve.Linear(0, 1, 1, 1);
        public Gradient color = new Gradient();
        [Tooltip("Add the emission's random horizontal drift to the X curve.")]
        public bool addHorizontalDrift = true;
        public int Revision { get; private set; }
        private void OnValidate() => NotifyChanged();
        private void OnEnable() => NotifyChanged();
        public void NotifyChanged() { unchecked { Revision++; } }
        public void Bake(Color[] destination, int first)
        {
            if (animationClip != null)
            {
                if (!HasClipSamples) throw new System.InvalidOperationException("BurstWord clip has not been compiled: " + name + ". " + clipError);
                System.Array.Copy(clipSamples, 0, destination, first, clipSamples.Length);
                for (int i = SampleCount; i < SampleCount * 2; i++) destination[first + i].a = addHorizontalDrift ? 1 : 0;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    for (int i = SampleCount * 2; i < SampleCount * Rows; i++) destination[first + i] = destination[first + i].linear;
                return;
            }
            for (int i = 0; i < SampleCount; i++)
            {
                float t = (float)i / (SampleCount - 1);
                destination[first + i] = new Color(Value(positionX, t, 0), Value(positionY, t, 0), Value(scaleX, t, 1), Value(scaleY, t, 1));
                destination[first + SampleCount + i] = new Color(Value(rotation, t, 0) * Mathf.Deg2Rad, Mathf.Clamp01(Value(opacity, t, 1)),
                    Mathf.Max(0, Value(brightness, t, 1)), addHorizontalDrift ? 1 : 0);
                Color tint = color != null ? color.Evaluate(t) : Color.white;
                destination[first + SampleCount * 2 + i] = QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint;
            }
        }
        private static float Value(AnimationCurve curve, float t, float fallback) => curve != null && curve.length > 0 ? curve.Evaluate(t) : fallback;
    }
}
