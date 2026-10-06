using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace BurstWord.Baseline
{
    /// <summary>Conventional TextMeshProUGUI baseline: one pooled GameObject per label.</summary>
    public sealed class PooledTextRenderer : MonoBehaviour, IDamageTextBackend
    {
        [Min(1)] public int capacity = 10000;
        public TMP_FontAsset font;
        public Camera worldCamera;
        [Min(1)] public int fontSize = 28;
        [Min(0.01f)] public float lifetime = 1.5f;
        public float risePixels = 90;
        public Vector2 referenceResolution = new Vector2(1920, 1080);

        public int ActiveCount { get; private set; }
        public int CreatedCount { get; private set; }
        public long EmittedCount { get; private set; }
        public long DroppedCount { get; private set; }
        public string BackendName => "TextMeshProUGUI + GameObject Pool";
        public int Capacity => available == null ? capacity : available.Length;

        private PooledDamageText[] available, active;
        private int availableCount;
        private GameObject canvasObject;
        private static readonly ProfilerMarker EmitMarker = new ProfilerMarker("Baseline.TMP.Emit");

        private void Awake() => Initialize();

        public void Initialize()
        {
            // Awake also runs on disabled components. A leftover baseline component must
            // never prewarm its GameObjects when this node uses the BRG backend.
            if (GetComponent<BurstWord.BRG.BrgDamageTextRenderer>() != null) return;
            if (available != null) return;
            capacity = Mathf.Max(1, capacity);
            fontSize = Mathf.Max(1, fontSize);
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                Debug.LogError("TextMeshPro baseline requires a TMP font asset. Import TMP Essential Resources or assign Font.", this);
                enabled = false;
                return;
            }
            available = new PooledDamageText[capacity];
            if (worldCamera == null) worldCamera = Camera.main;
            active = new PooledDamageText[capacity];
            canvasObject = new GameObject("Damage Text Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            // Fully prewarm. No Instantiate, Destroy, coroutine or layout component in the hot path.
            for (int i = 0; i < capacity; i++)
            {
                var go = new GameObject("Damage Text", typeof(RectTransform));
                go.SetActive(false);
                go.transform.SetParent(canvasObject.transform, false);
                var text = go.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = fontSize;
                text.alignment = TextAlignmentOptions.Center;
#if BURSTWORD_UGUI_TMP
                text.textWrappingMode = TextWrappingModes.NoWrap;
#else
                text.enableWordWrapping = false;
#endif
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                text.richText = false;
                text.enableAutoSizing = false;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(240, 64);
                var item = go.AddComponent<PooledDamageText>();
                item.Initialize(this, rect, text, (RectTransform)canvasObject.transform);
                available[availableCount++] = item;
                CreatedCount++;
            }
        }

        public bool Emit(Vector3 worldPosition, int damage, Color color, float horizontalDrift = 0, float durationScale = 1)
        {
            using (EmitMarker.Auto())
            {
                if (available == null || !isActiveAndEnabled || worldCamera == null) return false;
                if (availableCount == 0) { DroppedCount++; return false; }
                var item = available[--availableCount];
                available[availableCount] = null;
                item.ActiveIndex = ActiveCount;
                active[ActiveCount++] = item;
                item.Show(worldPosition, damage, color, Mathf.Max(0.01f, lifetime * durationScale), risePixels, horizontalDrift);
                EmittedCount++;
                return true;
            }
        }

        internal void Return(PooledDamageText item)
        {
            int index = item.ActiveIndex;
            if (index < 0 || index >= ActiveCount || active[index] != item) return;
            var last = active[--ActiveCount];
            active[index] = last;
            last.ActiveIndex = index;
            active[ActiveCount] = null;
            item.ActiveIndex = -1;
            item.gameObject.SetActive(false);
            available[availableCount++] = item;
        }

        public void Clear()
        {
            while (ActiveCount > 0) Return(active[ActiveCount - 1]);
        }

        public void ResetCounters() { EmittedCount = 0; DroppedCount = 0; }
        private void OnDisable() => Clear();
    }
}
