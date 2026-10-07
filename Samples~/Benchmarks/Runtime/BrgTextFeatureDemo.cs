using UnityEngine;
using BurstWord.Baseline;

namespace BurstWord.BRG
{
    [DefaultExecutionOrder(100)]
    public sealed class BrgTextFeatureDemo : MonoBehaviour
    {
        public enum Workload { Mixed, Numbers, CJK, ComplexScripts, RichText, Effects, Emoji, Wrapping }
        public Workload workload = Workload.Mixed;
        public bool continuous = true;
        [Min(0)] public int emissionsPerSecond = 200;
        [Min(0)] public int burstCount = 200;
        public int randomSeed = 12345;
        public Transform[] combatants;
        public bool showControls = true;
        public Font controlsFont;
        public bool animateTargets;
        private BenchmarkStatistics statistics;
        private string rateInput,burstInput;
        private Vector2 panelScroll;
        private Vector3[] targetPositions, targetScales;
        private Quaternion[] targetRotations;
        private BrgDamageTextRenderer textRenderer;
        private BrgDamageTextRenderer.TextEmission[] emissionRequests = new BrgDamageTextRenderer.TextEmission[128];
        private System.Random random;
        private double pending;
        private double previousEmissionTime = -1;
        private float elapsed, nextOverlay;
        private int sampleNumber, oldVSync, oldTarget;
        private int animationIndex;
        private string overlay = "Warming up...";
        public float AverageFrameMilliseconds => statistics == null ? 0 : statistics.AverageFrameMilliseconds;
        private static readonly string[] ModeNames = { "Mixed", "Numbers", "CJK", "Complex Scripts", "Rich Text", "Effects", "Color Emoji", "Word Wrap" };
        private static readonly string[] SortingNames = { "Overlay", "Opaque Occlusion", "Scene Transparency" };
        private static readonly string[] SpaceNames = { "Fixed Position", "Follow (Fixed Size)", "World Transform" };
        private static readonly string[] CjkText = { "暴击 {0}", "闪避 {0}", "回復 {0}", "피해 {0}" };
        private static readonly string[] ComplexText = { "سلام {0}", "مرحبا {0}", "नमस्ते {0}", "क्षि {0}", "שלום {0}", "สวัสดี {0}", "伤害 {0} العربية नमस्ते" };
        private static readonly string[] RichText = { "<b>暴击 {0}</b>", "<i>Damage {0}</i>", "<color=#FFD34D>{0}</color>", "<size=130%>{0}</size>", "<u>{0}</u>", "<s>{0}</s>", "伤害 {0} x<sup>2</sup>", "<font=\"NotoSerif SDF\">office ffi {0}</font>" };
        private static readonly string[] EffectText = { "{0}", "{0}", "<b>暴击 {0}</b>" };
        // Fonts 7 and 8 own their outline/shadow and glow materials respectively.
        private static int EffectFontIndex(int template) => template % EffectText.Length == 1 ? 8 : 7;
        private static readonly string[] EmojiText = { "<sprite=0> {0}", "<sprite=1> {0}", "😊 {0}", "<sprite name=\"1f60b\"> {0}" };
        private static readonly string[] WrapText = { "伤害 {0} 自动换行测试文字", "Damage {0} word wrapping test", "伤害 {0} العربية नमस्ते" };

        private void Start()
        {
            textRenderer = GetComponent<BrgDamageTextRenderer>();
            random = new System.Random(randomSeed);
            statistics=new BenchmarkStatistics();rateInput=emissionsPerSecond.ToString();burstInput=burstCount.ToString();
            // Only discover existing battlefield transforms; no text objects are created.
            if (combatants == null || combatants.Length == 0)
            {
                var army = GameObject.Find("Combatants (damage emission points)");
                if (army != null)
                {
                    combatants = new Transform[army.transform.childCount];
                    for (int i = 0; i < combatants.Length; i++) combatants[i] = army.transform.GetChild(i);
                }
            }
            oldVSync = QualitySettings.vSyncCount; oldTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            targetPositions = new Vector3[combatants.Length]; targetScales = new Vector3[combatants.Length];
            targetRotations = new Quaternion[combatants.Length];
            for (int i = 0; i < combatants.Length; i++)
                if (combatants[i] != null)
                { targetPositions[i] = combatants[i].position; targetScales[i] = combatants[i].localScale; targetRotations[i] = combatants[i].rotation; }
            WarmFonts();
            ResetPressure();
        }
        private void Update() => UpdatePressure();

        private void UpdatePressure()
        {
            if (animateTargets) MoveTargets();
            double now = Time.realtimeSinceStartupAsDouble;
            // Start the emission clock after initialization or a workload reset.
            if (previousEmissionTime < 0) { previousEmissionTime = now; return; }
            float dt = (float)(now - previousEmissionTime);
            previousEmissionTime = now;
            elapsed += dt;
            if (continuous)
            {
                pending += Mathf.Max(0, emissionsPerSecond) * (double)dt;
                int count = (int)System.Math.Min(pending, int.MaxValue);
                pending -= count;
                EmitPressure(count);
            }
            statistics?.Sample(dt);
            if (elapsed >= nextOverlay)
            {
                nextOverlay = elapsed + 0.5f;
                overlay = $"Mode: {ModeNames[(int)workload]}   Rate: {emissionsPerSecond:N0}/s\n"+
                    $"Active text: {textRenderer.ActiveCount:N0}/{textRenderer.Capacity:N0}   Glyphs: {textRenderer.ActiveGlyphCount:N0}\n"+
                    $"Backend: {textRenderer.ActiveBackend}   Draw commands: {textRenderer.DrawCommandCount}   Objects per text: 0\nTypography: {textRenderer.ShaperName}\n"+
                    $"Emitted: {textRenderer.EmittedCount:N0}   Dropped: {textRenderer.DroppedCount:N0}   Layout failures: {textRenderer.FailedLayoutCount:N0}\n"+
                    $"Missing glyphs/sprites/shaping sources: {textRenderer.MissingGlyphCount}/{textRenderer.MissingSpriteCount}/{textRenderer.UnavailableShapingCount}";

            }
        }

        private void WarmFonts()
        {
            var position = textRenderer.worldCamera.transform.position + textRenderer.worldCamera.transform.forward * 40;
            textRenderer.Emit(position, 1234567890, Color.white);
            string[][] groups = { CjkText, ComplexText, RichText, EmojiText, WrapText };
            foreach (var templates in groups)
                foreach (string template in templates)
                    textRenderer.EmitText(position, template.Replace("{0}", "1234567890"), Color.white);
            for (int i = 0; i < EffectText.Length; i++)
                textRenderer.EmitText(position, EffectText[i].Replace("{0}", "1234567890"), Color.white, fontIndex: EffectFontIndex(i));
            textRenderer.Clear();
        }

        public void EmitPressure(int count)
        {
            if (textRenderer == null || combatants == null || combatants.Length == 0) return;
            if (emissionRequests.Length < count) System.Array.Resize(ref emissionRequests, Mathf.NextPowerOfTwo(count));
            int requestCount = 0;
            for (int i = 0; i < count; i++)
            {
                var target = combatants[random.Next(combatants.Length)];
                if (target == null) continue;
                var position = target.position + Vector3.up * 1.8f +
                    new Vector3(((float)random.NextDouble() - 0.5f) * 0.8f, (float)random.NextDouble() * 0.4f, 0);
                int value = random.Next(10, 100000);
                bool critical = random.Next(5) == 0;
                float drift = ((float)random.NextDouble() - 0.5f) * 100;
                float duration = 1.2f + (float)random.NextDouble() * 0.6f;
                Workload mode = workload == Workload.Mixed ? (Workload)(1 + sampleNumber % 7) : workload;
                // Rotate templates independently of the event RNG, preserving battlefield positions across modes.
                int index = sampleNumber / 7;
                Color color = critical ? new Color(1, 0.65f, 0.15f) : Color.white;
                string text;
                if (mode == Workload.Numbers)
                {
                    text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    string[] templates = mode == Workload.CJK ? CjkText : mode == Workload.ComplexScripts ? ComplexText :
                        mode == Workload.RichText ? RichText : mode == Workload.Effects ? EffectText : mode == Workload.Emoji ? EmojiText : WrapText;
                    text = templates[index % templates.Length].Replace("{0}", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                bool follow = textRenderer.spaceMode != BrgDamageTextRenderer.SpaceMode.ScreenSnapshot;
                var pose = new BrgDamageTextRenderer.TextPose(follow ? Vector3.zero : position, Quaternion.identity, Vector3.one,
                    follow ? target.InverseTransformPoint(position) : Vector3.zero);
                emissionRequests[requestCount++] = new BrgDamageTextRenderer.TextEmission(text, color, pose,
                    mode == Workload.Wrapping ? 140 : 0, drift, duration, follow ? target : null,
                    fontIndex: mode == Workload.Effects ? EffectFontIndex(index) : 0, animationIndex: animationIndex);
                sampleNumber++;
            }
            try { textRenderer.EmitBatch(emissionRequests, requestCount); }
            finally { System.Array.Clear(emissionRequests, 0, requestCount); }
            textRenderer.wrapWidth = 0;
        }

        public void SelectWorkload(Workload mode)
        {
            workload = mode;
            ResetPressure();
        }
        public void ResetPressure()
        {
            if (textRenderer == null) return;
            textRenderer.Clear(); textRenderer.ResetCounters();
            random = new System.Random(randomSeed);
            pending = 0; sampleNumber = 0;
            elapsed = nextOverlay = 0;
            previousEmissionTime = -1;
            statistics?.Reset();
        }

        public void ChangeRate(int rate) { emissionsPerSecond=Mathf.Max(0,rate);rateInput=emissionsPerSecond.ToString();ResetPressure(); }
        public void ChangeBackend(BrgDamageTextRenderer.RenderBackend backend)
        {
            textRenderer.enabled = false;
            textRenderer.renderBackend = backend;
            textRenderer.enabled = true;
            WarmFonts(); ResetPressure();
        }
        private void GrowCapacity()
        {
            int desired = (int)System.Math.Min((long)textRenderer.Capacity * 2, int.MaxValue);
            textRenderer.enabled = false;
            textRenderer.capacity = desired;
            textRenderer.enabled = true;
            WarmFonts();
            ResetPressure();
        }

        private void MoveTargets()
        {
            float time = Time.unscaledTime;
            for (int i = 0; i < combatants.Length; i++)
            {
                var target = combatants[i]; if (target == null) continue;
                float phase = time + i * 0.17f;
                target.position = targetPositions[i] + new Vector3(Mathf.Sin(phase) * 0.5f, Mathf.Sin(phase * 1.3f) * 0.15f, 0);
                target.rotation = targetRotations[i] * Quaternion.Euler(0, Mathf.Sin(phase * 0.7f) * 25, Mathf.Sin(phase) * 20);
                target.localScale = targetScales[i] * (1 + Mathf.Sin(phase * 0.8f) * 0.2f);
            }
        }
        private void ToggleTargets()
        {
            animateTargets = !animateTargets;
            if (!animateTargets)
                for (int i = 0; i < combatants.Length; i++)
                    if (combatants[i] != null)
                    { combatants[i].position = targetPositions[i]; combatants[i].rotation = targetRotations[i]; combatants[i].localScale = targetScales[i]; }
            ResetPressure();
        }

        private void OnGUI()
        {
            if (!showControls) return;
            Font previous=GUI.skin.font;if(controlsFont!=null)GUI.skin.font=controlsFont;
            panelScroll=GUI.BeginScrollView(new Rect(0,0,Mathf.Min(625,Screen.width),Mathf.Min(Screen.height,710)),panelScroll,new Rect(0,0,610,700));
            GUI.Box(new Rect(10,10,600,680),GUIContent.none);GUILayout.BeginArea(new Rect(20,18,580,660));
            GUILayout.Label(overlay);GUILayout.Label(statistics==null?"Preparing statistics...":statistics.Text);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 3; i++)
            {
                var backend = (BrgDamageTextRenderer.RenderBackend)i;
                if (GUILayout.Button((textRenderer.renderBackend == backend ? "* " : "") + backend)) ChangeBackend(backend);
            }
            GUILayout.EndHorizontal();
            for(int row=0;row<2;row++)
            {
                GUILayout.BeginHorizontal();
                for(int col=0;col<4;col++) { int i=row*4+col;if(GUILayout.Button(((int)workload==i?"* ":"")+ModeNames[i]))SelectWorkload((Workload)i); }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            for(int i=0;i<3;i++)if(GUILayout.Button(((int)textRenderer.sortingMode==i?"* ":"")+SortingNames[i])) { textRenderer.sortingMode=(BrgDamageTextRenderer.SortingMode)i;ResetPressure(); }
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            for(int i=0;i<3;i++)if(GUILayout.Button(((int)textRenderer.spaceMode==i?"* ":"")+SpaceNames[i])) { textRenderer.spaceMode=(BrgDamageTextRenderer.SpaceMode)i;ResetPressure(); }
            GUILayout.EndHorizontal();
            BenchmarkControls.Rate(ref rateInput,emissionsPerSecond,ChangeRate);
            BenchmarkControls.Burst(ref burstInput,burstCount,count=>burstCount=count);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(continuous?"Pause":"Resume"))continuous=!continuous;
            if(GUILayout.Button("Burst +"+burstCount.ToString("N0")))EmitPressure(Mathf.Max(0,burstCount));
            if(GUILayout.Button("Clear / Reset"))ResetPressure();GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Capacity x2"))GrowCapacity();
            if(GUILayout.Button((animateTargets?"* ":"")+"Animate Targets"))ToggleTargets();GUILayout.EndHorizontal();
            bool previousEnabled=GUI.enabled;GUI.enabled=previousEnabled && textRenderer.spaceMode==BrgDamageTextRenderer.SpaceMode.WorldFollow;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("World Size /2")) { textRenderer.worldUnitsPerLayoutUnit=Mathf.Max(.000001f,textRenderer.worldUnitsPerLayoutUnit*.5f);ResetPressure(); }
            if(GUILayout.Button("World Size x2")) { textRenderer.worldUnitsPerLayoutUnit*=2;ResetPressure(); }
            GUILayout.EndHorizontal();GUI.enabled=previousEnabled;
            var selectedAnimation = textRenderer.GetAnimation(animationIndex);
            if(GUILayout.Button("Animation ["+animationIndex+"]: "+(selectedAnimation==null?"Linear":selectedAnimation.name)+" (Click to Cycle)"))
            {
                animationIndex = (animationIndex + 1) % Mathf.Max(1, textRenderer.animations == null ? 0 : textRenderer.animations.Length);
                ResetPressure();
            }
            if(GUILayout.Button("Hide Panel (F1 to Restore)"))showControls=false;
            GUILayout.EndArea();GUI.EndScrollView();GUI.skin.font=previous;
        }
        private void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F1)) showControls = !showControls;
        }
        private void OnDisable()
        {
            statistics?.Dispose();statistics=null;
            if (random != null) { QualitySettings.vSyncCount = oldVSync; Application.targetFrameRate = oldTarget; }
        }
    }
}
