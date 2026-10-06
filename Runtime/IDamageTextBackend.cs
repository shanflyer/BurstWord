using UnityEngine;

namespace BurstWord.Baseline
{
    public interface IDamageTextBackend
    {
        string BackendName { get; }
        int Capacity { get; }
        int ActiveCount { get; }
        int CreatedCount { get; }
        long EmittedCount { get; }
        long DroppedCount { get; }
        bool Emit(Vector3 worldPosition, int damage, Color color, float horizontalDrift = 0, float durationScale = 1);
        void Clear();
        void ResetCounters();
    }
}
