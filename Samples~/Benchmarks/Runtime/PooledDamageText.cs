using System.Globalization;
using Unity.Profiling;
using UnityEngine;
using TMPro;

namespace BurstWord.Baseline
{
    /// <summary>One traditional MonoBehaviour.Update per live damage label.</summary>
    public sealed class PooledDamageText : MonoBehaviour
    {
        internal int ActiveIndex = -1;
        private PooledTextRenderer owner;
        private RectTransform rect, canvasRect;
        private TextMeshProUGUI label;
        private Vector3 worldOrigin;
        private Color baseColor;
        private float age, duration, rise, drift;
        private static readonly ProfilerMarker AnimateMarker = new ProfilerMarker("Baseline.TMP.AnimateItem");

        internal void Initialize(PooledTextRenderer pool, RectTransform transformRect, TextMeshProUGUI text, RectTransform canvas)
        {
            owner = pool; rect = transformRect; label = text; canvasRect = canvas;
        }

        internal void Show(Vector3 origin, int damage, Color color, float life, float riseDistance, float sideways)
        {
            worldOrigin = origin; baseColor = color; duration = life; rise = riseDistance; drift = sideways; age = 0;
            label.text = damage.ToString(CultureInfo.InvariantCulture);
            label.color = color;
            UpdateVisual();
            gameObject.SetActive(true);
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        internal void Tick(float deltaTime)
        {
            using (AnimateMarker.Auto())
            {
                age += Mathf.Max(0, deltaTime);
                if (age >= duration) { owner.Return(this); return; }
                UpdateVisual();
            }
        }

        private void UpdateVisual()
        {
            if (owner.worldCamera == null) return;
            float t = age / duration;
            Vector3 screen = owner.worldCamera.WorldToScreenPoint(worldOrigin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            rect.anchoredPosition = local + new Vector2(drift * t, rise * t);
            Color color = baseColor;
            color.a *= screen.z > 0 ? 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 1, t)) : 0;
            label.color = color;
        }
    }
}
