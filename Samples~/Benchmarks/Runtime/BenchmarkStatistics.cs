using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace BurstWord.Baseline
{
    // Shared, bounded statistics for both benchmark scenes. No frame history,
    // reflection, detailed stage sampling, console output or automatic file I/O.
    internal sealed class BenchmarkStatistics : IDisposable
    {
        private ProfilerRecorder gc;
        private double elapsed, seconds, nextRefresh;
        private int frames, initialCollections;
        private float largestFrame;
        public string Text { get; private set; } = "Preparing statistics...";
        public float AverageFrameMilliseconds => frames == 0 ? 0 : (float)(seconds * 1000 / frames);
        public BenchmarkStatistics()
        {
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            Reset();
        }
        public void Reset()
        { elapsed=seconds=nextRefresh=0;frames=0;largestFrame=0;initialCollections=GC.CollectionCount(0); }
        public void Sample(float dt, float warmupSeconds=3)
        {
            if(dt<=0)return;
            elapsed+=dt;
            if(elapsed>=warmupSeconds) { seconds+=dt;frames++;largestFrame=Mathf.Max(largestFrame,dt*1000); }
            if(elapsed<nextRefresh)return;
            nextRefresh=elapsed+.5;
            float ms=AverageFrameMilliseconds;
            string allocation=gc.Valid ? (gc.LastValue/1024f).ToString("F1")+" KB/frame" : "unavailable";
            Text=$"Mean {(ms>0?1000/ms:0):F1} FPS | {ms:F2} ms | Max {largestFrame:F2} ms | Frames {frames:N0}"+
                $"\nUnity memory {Profiler.GetTotalAllocatedMemoryLong()/1048576f:F1} MB | Reserved {Profiler.GetTotalReservedMemoryLong()/1048576f:F1} MB"+
                $"\nManaged {Profiler.GetMonoUsedSizeLong()/1048576f:F1} MB | GC alloc {allocation} | GC collections {GC.CollectionCount(0)-initialCollections}"+
                (elapsed<warmupSeconds?$"\nWarmup {Mathf.Max(0,warmupSeconds-(float)elapsed):F1}s":"");
        }
        public void Dispose() => gc.Dispose();
    }

    internal static class BenchmarkControls
    {
        private static readonly int[] Rates={1000,5000,10000,20000};
        private static readonly string[] RateNames={"1,000 / s","5,000 / s","10,000 / s","20,000 / s"};
        internal static void Rate(ref string value, int current, Action<int> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Emit / second",GUILayout.Width(110));value=GUILayout.TextField(value,GUILayout.Width(125));
            if(GUILayout.Button("Apply") && int.TryParse(value,out int rate))apply(Mathf.Max(0,rate));
            if(GUILayout.Button("/ 2"))apply(current/2);
            if(GUILayout.Button("x 2"))apply((int)Math.Min((long)current*2,int.MaxValue));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for(int i=0;i<Rates.Length;i++)if(GUILayout.Button(RateNames[i]))apply(Rates[i]);
            GUILayout.EndHorizontal();
        }
        internal static void Burst(ref string value, int current, Action<int> apply)
        {
            GUILayout.BeginHorizontal();GUILayout.Label("Burst count",GUILayout.Width(110));
            value=GUILayout.TextField(value,GUILayout.Width(125));
            if(GUILayout.Button("Apply") && int.TryParse(value,out int count))apply(Mathf.Max(0,count));
            GUILayout.Label("Current "+current.ToString("N0"));GUILayout.EndHorizontal();
        }
    }
}
