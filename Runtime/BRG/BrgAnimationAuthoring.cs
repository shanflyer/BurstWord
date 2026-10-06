using UnityEngine;

namespace BurstWord.BRG
{
    // Fixed component type for Clip bindings. The editor writes curves directly;
    // no authoring GameObject or Animator is instantiated.
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class BrgAnimationAuthoring : MonoBehaviour
    {
        public Color color = Color.white;
        [Range(0, 1)] public float opacity = 1;
        [Min(0)] public float brightness = 1;
    }
}
