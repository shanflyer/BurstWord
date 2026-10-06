using System;
using TMPro;
using UnityEngine;

namespace BurstWord.BRG
{
    // The OpenType tables are needed for shaping; an SDF atlas alone has no GSUB/GPOS data.
    public sealed class BrgFontSources : ScriptableObject
    {
        [Serializable] public struct Entry { public TMP_FontAsset font; public TextAsset openTypeData; }
        public Entry[] fonts;
        public TextAsset Find(TMP_FontAsset font)
        {
            if (fonts != null) foreach (var item in fonts) if (item.font == font) return item.openTypeData;
            return null;
        }
    }
}
