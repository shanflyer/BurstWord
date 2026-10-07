using TMPro;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        // Per-emission layout inputs. Manager settings and already-live labels stay unchanged.
        private TMP_FontAsset emissionFont;
        private int emissionFontSize;
        private bool hasEmissionAppearance;
        private TMP_FontAsset LayoutFont => hasEmissionAppearance ? emissionFont : font;
        private TMP_SpriteAsset ActiveSpriteAsset => useSprites ? (spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset) : null;
        private int LayoutFontSize => hasEmissionAppearance ? emissionFontSize : fontSize;
        // The numeric Job shares one immutable table. Other fonts use their own cached layout.
        private bool CanUseNumericLayout => ReferenceEquals(LayoutFont, font) && LayoutFontSize == fontSize;

        /// <summary>0 selects the default font; 1..N select Fonts in Inspector order.</summary>
        public TMP_FontAsset GetFont(int fontIndex)
        {
            if (fontIndex == 0) return font;
            if (fontIndex < 0 || fonts == null || fontIndex > fonts.Length)
                throw new System.ArgumentOutOfRangeException(nameof(fontIndex), fontIndex, "Font index must be 0 (default) or 1..N from the font list.");
            var selected = fonts[fontIndex - 1];
            if (selected == null) throw new System.ArgumentException("Font index " + fontIndex + " has no TMP Font Asset assigned.", nameof(fontIndex));
            return selected;
        }

        private readonly struct EmissionAppearanceScope : System.IDisposable
        {
            private readonly BrgDamageTextRenderer owner;
            private readonly TMP_FontAsset previousFont;
            private readonly int previousSize;
            private readonly TextAnchor previousAlignment;
            private readonly Vector2 previousTextArea;
            private readonly bool previousActive;

            public EmissionAppearanceScope(BrgDamageTextRenderer renderer, TMP_FontAsset selectedFont,
                int selectedSize, TextAnchor? selectedAlignment = null, Vector2? selectedTextArea = null, int fontIndex = 0)
            {
                var selected = selectedFont != null ? selectedFont : renderer.GetFont(fontIndex);
                owner = renderer;
                previousFont = renderer.emissionFont;
                previousSize = renderer.emissionFontSize;
                previousAlignment = renderer.emissionAlignment;
                previousTextArea = renderer.emissionTextArea;
                previousActive = renderer.hasEmissionAppearance;
                renderer.emissionFont = selected;
                renderer.emissionFontSize = selectedSize > 0 ? selectedSize : renderer.fontSize;
                renderer.emissionAlignment = selectedAlignment ?? renderer.alignment;
                renderer.emissionTextArea = selectedTextArea ?? (renderer.useTextArea ? renderer.textAreaSize : Vector2.zero);
                renderer.hasEmissionAppearance = true;
            }

            public void Dispose()
            {
                owner.emissionFont = previousFont;
                owner.emissionFontSize = previousSize;
                owner.emissionAlignment = previousAlignment;
                owner.emissionTextArea = previousTextArea;
                owner.hasEmissionAppearance = previousActive;
            }
        }
    }
}
