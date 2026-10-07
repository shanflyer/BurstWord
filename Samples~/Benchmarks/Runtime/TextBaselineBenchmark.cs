using UnityEngine;

namespace BurstWord.Baseline
{
    [DefaultExecutionOrder(100)]
    public sealed class TextBaselineBenchmark : MonoBehaviour
    {
        public bool continuous = true;
        [Min(0)] public int emissionsPerSecond = 200;
        [Min(0)] public int burstCount = 200;
        public int randomSeed = 12345;
        public Transform[] combatants;
        [Min(0)] public float warmupSeconds = 3;
        public bool showOverlay = true;
        public bool unlockFrameRate = true;

        private IDamageTextBackend textPool;
        private System.Random random;
        private double pending, previousEmissionTime=-1;
        private BenchmarkStatistics statistics;
        private string rateInput, burstInput;
        private Vector2 panelScroll;
        private float elapsed, nextOverlay;
        private int oldVSync, oldTarget;
        private string overlay = "Warming up...";
        public float AverageFrameMilliseconds => statistics == null ? 0 : statistics.AverageFrameMilliseconds;

        private void Awake()
        {
            textPool = GetComponent<BurstWord.BRG.BrgDamageTextRenderer>();
            if (textPool == null) textPool = GetComponent<PooledTextRenderer>();
            if (textPool == null)
            {
                Debug.LogError("Attach a damage text renderer to the benchmark.", this);
                enabled = false;
                return;
            }
            random = new System.Random(randomSeed);
        }

        private void OnEnable()
        {
            oldVSync = QualitySettings.vSyncCount;
            oldTarget = Application.targetFrameRate;
            if (unlockFrameRate) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
        }

        private void Update()
        {
            if(statistics==null) { statistics=new BenchmarkStatistics(); rateInput=emissionsPerSecond.ToString();burstInput=burstCount.ToString(); }
            double now=Time.realtimeSinceStartupAsDouble;
            if(previousEmissionTime<0) { previousEmissionTime=now;return; }
            float dt=(float)(now-previousEmissionTime);previousEmissionTime=now;
            elapsed += dt; statistics.Sample(dt,warmupSeconds);
            if (continuous)
            {
                pending += Mathf.Max(0, emissionsPerSecond) * (double)dt;
                // Keep fractional events; never silently cap emissions when frame rate falls.
                int count = (int)System.Math.Min(pending, int.MaxValue);
                pending -= count;
                Spawn(count);
            }
            if (showOverlay && elapsed >= nextOverlay)
            {
                nextOverlay = elapsed + 0.5f;
                overlay = $"{textPool.BackendName}\nActive {textPool.ActiveCount:N0} / {textPool.Capacity:N0} | Rate {emissionsPerSecond:N0}/s"+
                    $"\nEmitted {textPool.EmittedCount:N0} | Dropped {textPool.DroppedCount:N0} | Text objects {textPool.CreatedCount:N0}";

            }
        }

        private void Spawn(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (combatants == null || combatants.Length == 0) return;
                var target = combatants[random.Next(combatants.Length)];
                if (target == null) continue;
                var position = target.position + Vector3.up * 1.8f +
                    new Vector3(((float)random.NextDouble() - 0.5f) * 0.8f, (float)random.NextDouble() * 0.4f, 0);
                int value = random.Next(10, 100000);
                bool critical = random.Next(5) == 0;
                float drift = ((float)random.NextDouble() - 0.5f) * 100;
                float duration = 1.2f + (float)random.NextDouble() * 0.6f;
                textPool.Emit(position, value, critical ? new Color(1, 0.65f, 0.15f) : Color.white, drift, duration);
            }
        }

        [ContextMenu("Emit Burst")]
        public void EmitBurst() { if (Application.isPlaying) Spawn(Mathf.Max(0, burstCount)); }

        [ContextMenu("Reset Run")]
        public void ResetRun()
        {
            if (!Application.isPlaying) return;
            textPool.Clear();
            textPool.ResetCounters();
            random = new System.Random(randomSeed);
            pending = 0; previousEmissionTime=-1; statistics?.Reset();
            elapsed = nextOverlay = 0;

        }

        public void SetEmissionRate(int rate)
        { emissionsPerSecond=Mathf.Max(0,rate);rateInput=emissionsPerSecond.ToString();ResetRun(); }
        private void LateUpdate() { if(Input.GetKeyDown(KeyCode.F1))showOverlay=!showOverlay; }
        private void OnGUI()
        {
            if (!showOverlay) return;
            panelScroll=GUI.BeginScrollView(new Rect(0,0,Mathf.Min(610,Screen.width),Mathf.Min(Screen.height,410)),panelScroll,new Rect(0,0,595,395));
            GUI.Box(new Rect(10,10,580,375),GUIContent.none);
            GUILayout.BeginArea(new Rect(20,18,560,360));
            GUILayout.Label(overlay);GUILayout.Label(statistics==null?"Preparing statistics...":statistics.Text);
            GUILayout.Space(8);
            BenchmarkControls.Rate(ref rateInput,emissionsPerSecond,SetEmissionRate);
            BenchmarkControls.Burst(ref burstInput,burstCount,count=>burstCount=count);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(continuous?"Pause":"Resume"))continuous=!continuous;
            if(GUILayout.Button("Emit burst"))EmitBurst();
            if(GUILayout.Button("Clear / reset"))ResetRun();
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Hide panel (F1 restores)"))showOverlay=false;
            GUILayout.Label("Pool capacity is configured before Play. Overflow is counted as Dropped.");
            GUILayout.EndArea();GUI.EndScrollView();
        }

        private void OnDisable()
        {
            statistics?.Dispose();statistics=null;previousEmissionTime=-1;
            if (unlockFrameRate) { QualitySettings.vSyncCount = oldVSync; Application.targetFrameRate = oldTarget; }
        }
    }
}
