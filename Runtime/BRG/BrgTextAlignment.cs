using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Tooltip("Left/center/right and top/middle/bottom alignment relative to the emission point or enabled text area.")]
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public bool useTextArea;
        [Tooltip("Optional layout area centered on the emission point. Layout units scale with UI text. Does not clip overflowing text.")]
        public Vector2 textAreaSize = new Vector2(300, 100);

        private TextAnchor emissionAlignment;
        private Vector2 emissionTextArea;
        private TextAnchor LayoutAlignment => hasEmissionAppearance ? emissionAlignment : alignment;
        private Vector2 LayoutTextArea => hasEmissionAppearance ? emissionTextArea : useTextArea ? textAreaSize : Vector2.zero;
        private int AlignmentFlags => (int)LayoutAlignment << 4;
        private float LayoutWrapWidth => wrapWidth > 0 && LayoutTextArea.x > 0 ? Mathf.Min(wrapWidth, LayoutTextArea.x) : wrapWidth;

        // Shared by the main layout paths and Burst's numeric preparation kernel.
        private static float HorizontalStart(float width, TextAnchor anchor, Vector2 area)
        {
            switch ((int)anchor % 3)
            {
                case 0: return area.x > 0 ? -area.x * 0.5f : 0;
                case 2: return area.x > 0 ? area.x * 0.5f - width : -width;
                default: return -width * 0.5f;
            }
        }
        private static float VerticalOffset(float height, TextAnchor anchor, Vector2 area)
        {
            switch ((int)anchor / 3)
            {
                case 0: return area.y > 0 ? area.y * 0.5f : 0;
                case 2: return area.y > 0 ? height - area.y * 0.5f : height;
                default: return height * 0.5f;
            }
        }
        private float HorizontalStart(float width) => HorizontalStart(width, LayoutAlignment, LayoutTextArea);
        private float VerticalOffset(float height) => VerticalOffset(height, LayoutAlignment, LayoutTextArea);
        private float BaselineOffset(float ascent, float descent) => VerticalOffset(ascent - descent) - ascent;
    }
}
