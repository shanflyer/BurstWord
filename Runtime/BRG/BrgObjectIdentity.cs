using UnityEngine;
using UnityEngine.Rendering;
namespace BurstWord.BRG
{
    internal static class BrgObjectIdentity
    {
        public static long Of(Object value)
        {
#if UNITY_6000_6_OR_NEWER
            return unchecked((long)value.GetEntityId().GetRawData());
#else
            return value.GetInstanceID();
#endif
        }
        public static long Of(BatchPackedCullingViewID value)
        {
#if UNITY_6000_6_OR_NEWER
            return unchecked((long)value.GetEntityId().GetRawData());
#else
            return value.GetInstanceID();
#endif
        }
    }
}
