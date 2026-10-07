using TMPro;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        // Per-emission layout inputs. Manager settings and already-live labels stay unchanged.
        private TMP_FontAsset emissionFont;
        private Material emissionMaterial;
        private int emissionFontSize;
        private bool hasEmissionAppearance;
        private TMP_FontAsset LayoutFont => hasEmissionAppearance ? emissionFont : font;
        private Material DefaultLayoutMaterial => useMaterialPresets ? fontMaterial : null;
        private Material LayoutMaterial => hasEmissionAppearance ? emissionMaterial : DefaultLayoutMaterial;
        private TMP_SpriteAsset ActiveSpriteAsset => useSprites ? (spriteAsset != null ? spriteAsset : TMP_Settings.defaultSpriteAsset) : null;
        private int LayoutFontSize => hasEmissionAppearance ? emissionFontSize : fontSize;
        // The numeric Job shares one immutable table. Other fonts use their own cached layout.
        private bool CanUseNumericLayout => ReferenceEquals(LayoutFont, font) && LayoutFontSize == fontSize && LayoutMaterial == null;

        private readonly struct EmissionAppearanceScope : System.IDisposable
        {
            private readonly BrgDamageTextRenderer owner;
            private readonly TMP_FontAsset previousFont;
            private readonly Material previousMaterial;
            private readonly int previousSize;
            private readonly bool previousActive;

            public EmissionAppearanceScope(BrgDamageTextRenderer renderer, TMP_FontAsset selectedFont,
                Material selectedMaterial, int selectedSize)
            {
                owner = renderer;
                previousFont = renderer.emissionFont;
                previousMaterial = renderer.emissionMaterial;
                previousSize = renderer.emissionFontSize;
                previousActive = renderer.hasEmissionAppearance;
                renderer.emissionFont = selectedFont != null ? selectedFont : renderer.font;
                renderer.emissionMaterial = selectedMaterial != null ? selectedMaterial :
                    (selectedFont == null || ReferenceEquals(selectedFont, renderer.font) ? renderer.DefaultLayoutMaterial : null);
                renderer.emissionFontSize = selectedSize > 0 ? selectedSize : renderer.fontSize;
                renderer.hasEmissionAppearance = true;
            }

            public void Dispose()
            {
                owner.emissionFont = previousFont;
                owner.emissionMaterial = previousMaterial;
                owner.emissionFontSize = previousSize;
                owner.hasEmissionAppearance = previousActive;
            }
        }
    }
}
