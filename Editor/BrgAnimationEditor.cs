using BurstWord.BRG;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BurstWord.Baseline.Editor
{
    [CustomEditor(typeof(BrgTextAnimation))]
    public sealed class BrgAnimationInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animationClip"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("addHorizontalDrift"), new GUIContent("叠加发射时横向漂移"));
            if (serializedObject.ApplyModifiedProperties()) BrgAnimationClipCompiler.Compile((BrgTextAnimation)target);
            var preset = (BrgTextAnimation)target;
            string error = preset.animationClip != null ? BrgAnimationClipCompiler.Validate(preset.animationClip) : null;
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                using(new EditorGUI.DisabledScope(!BrgAnimationClipEditing.CanEdit(preset.animationClip)))
                if (GUILayout.Button("修复非法轨道（可撤销）"))
                { Undo.RecordObject(preset.animationClip, "修复飘字轨道"); BrgAnimationClipCompiler.EnforceBindings(preset.animationClip); BrgAnimationClipCompiler.Compile(preset); }
            }
            EditorGUILayout.HelpBox("在独立窗口中拖动、录制关键帧、编辑曲线和播放。编辑数据完全隐藏，无需 Scene 或编辑物体。AnimationClip 编辑后采样，运行时仍由 GPU 播放。", MessageType.Info);
            if (GUILayout.Button("打开独立动画编辑器")) BrgAnimationEditor.Open(preset);
        }
    }

    public sealed class BrgAnimationEditor : EditorWindow
    {
        internal static readonly string[] TrackNames = { "位置 X", "位置 Y", "旋转 Z（度）", "缩放 X", "缩放 Y", "透明度", "亮度", "颜色 R", "颜色 G", "颜色 B", "颜色 A" };
        [SerializeField] private BrgTextAnimation preset;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string previewText = "12345";
        [SerializeField] private float duration = 1.5f, speed = 1, wrapWidth;
        [SerializeField] private int fontSize = 48;
        [SerializeField] private BrgDamageTextRenderer.SpaceMode space;
        private BrgAnimationPreview preview;
        private Vector2 scroll;
        private float time, keyTime;
        private int track, selectedKey = -1, tool, dragKey = -1, dragGroup = -1;
        private bool playing, loop = true, recording = true, showHandles = true;
        private double previous, nextRepaint;
        private string error;
        private Vector2 dragStart, dragPivot;
        private int dragAxis;
        private float[] dragValues;
        private float dragFactor;
        private AnimationClip Clip => preset != null ? preset.animationClip : null;
        private float Length => Clip != null && Clip.length > 0 ? Clip.length : 1;
        [MenuItem("Tools/BurstWord/Animation Editor")]
        public static void ShowWindow()
        {
            var animation = Selection.activeObject as BrgTextAnimation;
            if (animation == null)
                foreach (string guid in AssetDatabase.FindAssets("Float t:BrgTextAnimation", new[] { "Assets" }))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<BrgTextAnimation>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && candidate.name == "Float") { animation = candidate; break; }
                }
            Open(animation);
        }
        [UnityEditor.Callbacks.OnOpenAsset]
        #if UNITY_6000_6_OR_NEWER
        private static bool OpenAsset(EntityId entityId, int line)
        {
            var asset=EditorUtility.EntityIdToObject(entityId) as BrgTextAnimation;
#else
        private static bool OpenAsset(int instanceID, int line)
        {
            var asset=EditorUtility.InstanceIDToObject(instanceID) as BrgTextAnimation;
#endif
            if(asset==null)return false;Open(asset);return true;
        }
        public static void Open(BrgTextAnimation animation, BrgDamageTextRenderer source = null)
        {
            var window = GetWindow<BrgAnimationEditor>("飘字动画编辑器");
            window.minSize = new Vector2(960, 760);
            if (animation != null) window.preset = animation;
            if (source != null)
            { window.font = source.font; window.fontSize = source.fontSize; window.duration = Mathf.Max(.05f, source.lifetime); window.wrapWidth = source.wrapWidth; window.space = source.spaceMode; }
            window.ResetPreview(); window.Show();
        }
        private void OnEnable()
        {
            previous = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick; Undo.undoRedoPerformed += Changed;
            EditorApplication.playModeStateChanged += PlayModeChanged; AssemblyReloadEvents.beforeAssemblyReload += Release;
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= Changed;
            EditorApplication.playModeStateChanged -= PlayModeChanged; AssemblyReloadEvents.beforeAssemblyReload -= Release; Release();
        }
        private void PlayModeChanged(PlayModeStateChange state) { ResetPreview(); Repaint(); }
        private void Changed() { if (preset != null) BrgAnimationClipCompiler.Compile(preset); selectedKey = -1; error = null; Repaint(); }
        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing)
            {
                time += (float)(now - previous) * speed;
                if (time >= Length)
                { if (loop) { time %= Length; preview?.Restart(previewText, duration, preset); } else { time = Length; playing = false; } }
            }
            previous = now;
            if (now >= nextRepaint) { nextRepaint = now + 1.0 / 30; Repaint(); }
        }
        private void Release() { preview?.Dispose(); preview = null; }
        private void ResetPreview() { Release(); time = 0; selectedKey = -1; error = null; }
        private void Seek(float value) { playing = false; if (value < time) preview?.Restart(previewText, duration, preset); time = Mathf.Clamp(value, 0, Length); Repaint(); }
        private void Save() { if (preset != null) BrgAnimationClipCompiler.Compile(preset); AssetDatabase.SaveAssets(); }
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck(); preset = (BrgTextAnimation)EditorGUILayout.ObjectField(preset, typeof(BrgTextAnimation), false, GUILayout.Width(230));
                if (EditorGUI.EndChangeCheck()) ResetPreview();
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    string path = EditorUtility.SaveFilePanelInProject("创建飘字动画", "Text Animation", "asset", "选择保存位置");
                    if (!string.IsNullOrEmpty(path)) { preset = CreateInstance<BrgTextAnimation>(); AssetDatabase.CreateAsset(preset, path); BrgAnimationAuthoringSession.CreateClip(preset); ResetPreview(); }
                }
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45))) Save();
                GUILayout.FlexibleSpace();
                recording = GUILayout.Toggle(recording, "● 自动关键帧", EditorStyles.toolbarButton, GUILayout.Width(110));
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(50))) { playing = !playing; previous = EditorApplication.timeSinceStartup; }
                if (GUILayout.Button("重播", EditorStyles.toolbarButton, GUILayout.Width(50))) { Seek(0); playing = true; }
                loop = GUILayout.Toggle(loop, "循环", EditorStyles.toolbarButton, GUILayout.Width(50));
            }
            if (preset == null) { EditorGUILayout.HelpBox("选择动画预设或点击新建。", MessageType.Info); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(290)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawSettings();
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        tool = GUILayout.Toolbar(tool, new[] { "移动 W", "旋转 E", "缩放 R" }, EditorStyles.toolbarButton);
                        showHandles = GUILayout.Toggle(showHandles, "操作柄", EditorStyles.toolbarButton, GUILayout.Width(55));
                    }
                    Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    DrawPreview(rect);
                    EditorGUILayout.LabelField("拖动操作柄；自动关键帧开启时记录当前时间。关闭后只能浏览，避免误改。", EditorStyles.wordWrappedMiniLabel);
                }
            }
            string invalid = Clip != null ? BrgAnimationClipCompiler.Validate(Clip) : null;
            using (new EditorGUI.DisabledScope(Clip == null || invalid != null || EditorApplication.isPlayingOrWillChangePlaymode || !BrgAnimationClipEditing.CanEdit(Clip)))
            {
                DrawTimeline();
                DrawCurve();
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (Event.current.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
            {
                if (Event.current.keyCode == KeyCode.Space) { playing = !playing; Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.W) tool = 0;
                else if (Event.current.keyCode == KeyCode.E) tool = 1;
                else if (Event.current.keyCode == KeyCode.R) tool = 2;
                else if (Event.current.keyCode == KeyCode.Delete) { DeleteKey(); Event.current.Use(); }
            }
        }
        private bool Editable => Clip != null && BrgAnimationClipCompiler.Validate(Clip) == null && !EditorApplication.isPlayingOrWillChangePlaymode && BrgAnimationClipEditing.CanEdit(Clip);
        private float Value(int index) => BrgAnimationClipEditing.Value(Clip, index, time);
        private void Write(int index, float value)
        {
            if (!Editable || !recording) return;
            playing = false;
            try { BrgAnimationClipEditing.SetKey(preset, index, time, value); error=null; }
            catch(System.ArgumentException exception) { error=exception.Message; }
            Repaint();
        }
        private void DrawSettings()
        {
            EditorGUILayout.LabelField("AnimationClip", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); var clip = (AnimationClip)EditorGUILayout.ObjectField(Clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(preset, "选择飘字 Clip"); preset.animationClip = clip; EditorUtility.SetDirty(preset); BrgAnimationClipCompiler.Compile(preset); ResetPreview(); }
            if (Clip == null)
            { if (GUILayout.Button("从当前预设生成 Clip")) { BrgAnimationAuthoringSession.CreateClip(preset); ResetPreview(); } }
            else
            {
                string invalid = BrgAnimationClipCompiler.Validate(Clip);
                if (invalid != null)
                {
                    EditorGUILayout.HelpBox(invalid, MessageType.Error);
                    using(new EditorGUI.DisabledScope(!BrgAnimationClipEditing.CanEdit(Clip)))
                    if (GUILayout.Button("修复非法轨道（可撤销）")) { Undo.RecordObject(Clip, "修复飘字轨道"); BrgAnimationClipCompiler.EnforceBindings(Clip); if (Clip.length == 0) BrgAnimationAuthoringSession.EnsureFixedTracks(Clip); BrgAnimationClipCompiler.Compile(preset); error=null; }
                }
                if(!BrgAnimationClipEditing.CanEdit(Clip)) EditorGUILayout.HelpBox("导入 / 只读 Clip 不能直接编辑，请复制为独立 .anim。",MessageType.Warning);
                using (new EditorGUI.DisabledScope(!Editable))
                {
                    EditorGUI.BeginChangeCheck(); float fps=EditorGUILayout.DelayedFloatField("帧率",Clip.frameRate);
                    if(EditorGUI.EndChangeCheck() && fps>=1 && fps<=1000) { Undo.RecordObject(Clip,"修改 Clip 帧率"); Clip.frameRate=fps;EditorUtility.SetDirty(Clip); }
                    EditorGUI.BeginChangeCheck(); float length = EditorGUILayout.DelayedFloatField("Clip 时长", Length);
                    if (EditorGUI.EndChangeCheck() && length > .001f && !float.IsNaN(length) && !float.IsInfinity(length)) { BrgAnimationClipEditing.Resize(preset, length); time = Mathf.Min(time, Length); }
                    EditorGUI.BeginChangeCheck(); bool drift = EditorGUILayout.Toggle("叠加发射横向漂移", preset.addHorizontalDrift);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(preset, "修改漂移"); preset.addHorizontalDrift = drift; preset.NotifyChanged(); EditorUtility.SetDirty(preset); }
                }
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("当前时间的属性", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Editable || !recording))
            {
                for (int i = 0; i < 7; i++)
                { EditorGUI.BeginChangeCheck(); float value = EditorGUILayout.FloatField(TrackNames[i], Value(i)); if (EditorGUI.EndChangeCheck()) Write(i, value); }
                EditorGUI.BeginChangeCheck(); Color color = EditorGUILayout.ColorField("颜色", new Color(Value(7), Value(8), Value(9), Value(10)));
                if (EditorGUI.EndChangeCheck()) { Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); for (int i = 0; i < 4; i++) Write(7 + i, color[i]); Undo.CollapseUndoOperations(group); }
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("预览设置（不写入 Clip）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            previewText = EditorGUILayout.TextField("文字 / 富文本", previewText);
            font = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP 字体", font, typeof(TMP_FontAsset), false);
            fontSize = EditorGUILayout.IntSlider("字号", fontSize, 12, 120);
            wrapWidth = Mathf.Max(0, EditorGUILayout.FloatField("换行宽度", wrapWidth));
            duration = Mathf.Max(.05f, EditorGUILayout.FloatField("飘字寿命", duration));
            space = (BrgDamageTextRenderer.SpaceMode)EditorGUILayout.EnumPopup("空间模式", space);
            if (EditorGUI.EndChangeCheck()) { float savedTime = time; ResetPreview(); time = savedTime; }
            speed = EditorGUILayout.Slider("播放速度", speed, .1f, 3);
            EditorGUILayout.HelpBox("Clip 完整时长映射到飘字寿命。位置使用排版单位，旋转使用度。编辑数据隐藏在独立预览场景中，无层级、组件、任意属性操作入口。", MessageType.None);
        }
        private Rect ImageRect(Rect area)
        {
            float width = Mathf.Min(area.width, area.height * 960 / 540f), height = width * 540 / 960;
            return new Rect(area.center.x - width * .5f, area.center.y - height * .5f, width, height);
        }
        private void DrawPreview(Rect area)
        {
            Rect image = ImageRect(area);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(.025f, .03f, .045f));
                try
                {
                    if (preview == null && error == null) { preview = new BrgAnimationPreview(font, fontSize, wrapWidth, space); preview.Restart(previewText, duration, preset); }
                    if (preview != null) GUI.DrawTexture(image, preview.Render(Mathf.Min(time / Length * duration, duration - .00001f)), ScaleMode.StretchToFill, false);
                }
                catch (System.Exception exception) { error = exception.Message; Release(); }
            }
            if (!showHandles || Clip == null) return;
            float factor = image.width / 960 * (space == BrgDamageTextRenderer.SpaceMode.WorldFollow ? .015f * 540 / (20 * Mathf.Tan(22.5f * Mathf.Deg2Rad)) : 1);
            Vector2 pivot = image.center + new Vector2(Value(0), -Value(1)) * factor;
            if(Event.current.type == EventType.Repaint)
            {
            Handles.BeginGUI();
            Handles.color = Color.gray; Handles.DrawLine(new Vector3(image.center.x - 7, image.center.y), new Vector3(image.center.x + 7, image.center.y)); Handles.DrawLine(new Vector3(image.center.x, image.center.y - 7), new Vector3(image.center.x, image.center.y + 7));
            Handles.color = Editable && recording ? new Color(.2f, .8f, 1) : Color.gray;
            if (tool == 0)
            { Handles.DrawLine(pivot, pivot + Vector2.right * 65); Handles.DrawLine(pivot, pivot + Vector2.down * 65); Handles.DrawSolidRectangleWithOutline(new Rect(pivot.x - 8, pivot.y - 8, 16, 16), new Color(.15f,.65f,1,.3f), Handles.color); }
            else if (tool == 1) Handles.DrawWireDisc(pivot, Vector3.forward, 60);
            else
            {
                foreach (Vector2 point in new[] { pivot + Vector2.right * 60, pivot + Vector2.down * 60, pivot + new Vector2(60,-60) })
                { Handles.DrawLine(pivot, point); Handles.DrawSolidRectangleWithOutline(new Rect(point.x-7,point.y-7,14,14),new Color(.15f,.65f,1,.3f),Handles.color); }
            }
            Handles.EndGUI();
            }
            var e = Event.current;
            bool hit = tool == 0 ? new Rect(pivot.x - 12, pivot.y - 75, 87, 87).Contains(e.mousePosition) : tool == 1 ? Mathf.Abs(Vector2.Distance(pivot, e.mousePosition) - 60) < 12 : Vector2.Distance(pivot + new Vector2(60,-60), e.mousePosition) < 18 || Vector2.Distance(pivot + Vector2.right*60,e.mousePosition)<14 || Vector2.Distance(pivot + Vector2.down*60,e.mousePosition)<14;
            int control = GUIUtility.GetControlID(0x42575244, FocusType.Passive, area);
            if (e.type == EventType.MouseDown && e.button == 0 && hit && Editable && recording)
            {
                GUIUtility.hotControl = control; GUIUtility.keyboardControl = 0; playing = false; dragStart = e.mousePosition; dragPivot = pivot; dragFactor = Mathf.Max(.0001f, factor);
                Vector2 relative = e.mousePosition-pivot;
                dragAxis = tool == 0 ? (relative.x>16 && Mathf.Abs(relative.y)<12 ? 1 : relative.y < -16 && Mathf.Abs(relative.x)<12 ? 2 : 0) : tool == 2 ? (Mathf.Abs(relative.y)<14 ? 1 : Mathf.Abs(relative.x)<14 ? 2 : 0) : 0;
                dragValues = new[] { Value(0), Value(1), Value(2), Value(3), Value(4) }; Undo.IncrementCurrentGroup(); dragGroup = Undo.GetCurrentGroup(); e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control && dragValues != null)
            {
                Vector2 delta = e.mousePosition - dragStart;
                if (tool == 0) { if(dragAxis!=2) Write(0, dragValues[0] + (e.shift ? 0 : delta.x / dragFactor)); if(dragAxis!=1) Write(1, dragValues[1] - (e.control ? 0 : delta.y / dragFactor)); }
                else if (tool == 1)
                {
                    Vector2 a = dragStart - dragPivot, b = e.mousePosition - dragPivot;
                    float angle = -Vector2.SignedAngle(a, b); float value = dragValues[2] + angle;
                    Write(2, e.shift ? Mathf.Round(value / 15) * 15 : value);
                }
                else
                {
                    float x = 1 + delta.x / 60, y = 1 - delta.y / 60;
                    if (e.shift) x = y = (x + y) * .5f;
                    if(dragAxis!=2) Write(3, dragValues[3] * x); if(dragAxis!=1) Write(4, dragValues[4] * y);
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            { Undo.CollapseUndoOperations(dragGroup); GUIUtility.hotControl = 0; dragValues = null; e.Use(); }
        }
        private void DrawTimeline()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("关键帧时间轴", EditorStyles.boldLabel, GUILayout.Width(110));
                float value = EditorGUILayout.Slider(time, 0, Length); if (!Mathf.Approximately(value, time)) Seek(value);
                if (GUILayout.Button("上一帧", GUILayout.Width(65))) Seek(Mathf.Max(0, time - 1 / Mathf.Max(1, Clip != null ? Clip.frameRate : 60)));
                if (GUILayout.Button("下一帧", GUILayout.Width(65))) Seek(time + 1 / Mathf.Max(1, Clip != null ? Clip.frameRate : 60));
            }
            Rect area = GUILayoutUtility.GetRect(100, 242, GUILayout.ExpandWidth(true));
            Rect body = new Rect(area.x + 135, area.y, Mathf.Max(1, area.width - 145), area.height);
            var e = Event.current; int control = GUIUtility.GetControlID(0x54494d45, FocusType.Passive, area);
            EditorGUI.DrawRect(area, new Color(.12f,.12f,.12f));
            for (int j = 0; j <= 10; j++)
            { float x = body.x + j * body.width / 10; EditorGUI.DrawRect(new Rect(x, body.y, 1, body.height), new Color(.23f,.23f,.23f)); GUI.Label(new Rect(x+2, body.y, 48, 18), (j * Length / 10).ToString("0.##"), EditorStyles.miniLabel); }
            for (int i = 0; i < TrackNames.Length; i++)
            {
                Rect row = new Rect(area.x, area.y + 20 + i * 20, area.width, 20);
                if (i == track) EditorGUI.DrawRect(row, new Color(.15f,.27f,.36f));
                GUI.Label(new Rect(row.x + 4, row.y, 130, 20), TrackNames[i]);
                var curve = BrgAnimationClipEditing.Curve(Clip, i);
                for (int k = 0; k < curve.length; k++)
                {
                    float x = body.x + curve[k].time / Length * body.width;
                    Rect marker = new Rect(x - 6, row.y + 3, 12, 14);
                    GUI.Label(marker, "◆", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = i == track && k == selectedKey ? Color.yellow : Color.cyan } });
                    if (e.type == EventType.MouseDown && e.button == 0 && marker.Contains(e.mousePosition))
                    {
                        track = i; selectedKey = dragKey = k; keyTime = curve[k].time; Seek(keyTime);
                        GUIUtility.hotControl = control; Undo.IncrementCurrentGroup(); dragGroup = Undo.GetCurrentGroup(); e.Use();
                    }
                }
                if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
                {
                    track = i; selectedKey = -1;
                    if (e.mousePosition.x >= body.x) Seek((e.mousePosition.x - body.x) / body.width * Length);
                    e.Use();
                }
            }
            EditorGUI.DrawRect(new Rect(body.x + time / Length * body.width, body.y, 2, body.height), new Color(1,.35f,.25f));
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control && dragKey >= 0 && Editable)
            {
                float at = Mathf.Clamp((e.mousePosition.x - body.x) / body.width * Length, 0, Length);
                if (!e.shift) at = Mathf.Round(at * Clip.frameRate) / Mathf.Max(1, Clip.frameRate);
                selectedKey = dragKey = BrgAnimationClipEditing.MoveKey(preset, track, dragKey, at); keyTime = BrgAnimationClipEditing.Curve(Clip,track)[selectedKey].time; Seek(keyTime); e.Use();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            { GUIUtility.hotControl = 0; dragKey = -1; Undo.CollapseUndoOperations(dragGroup); e.Use(); }
        }
        private void DrawCurve()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int picked = EditorGUILayout.Popup(track, TrackNames, GUILayout.Width(150)); if (picked != track) { track = picked; selectedKey = -1; }
                if (GUILayout.Button("当前值插入关键帧", GUILayout.Width(140)) && Editable) BrgAnimationClipEditing.SetKey(preset, track, time, Value(track));
                using (new EditorGUI.DisabledScope(selectedKey < 0))
                {
                    if (GUILayout.Button("删除关键帧", GUILayout.Width(95))) DeleteKey();
                    EditorGUI.BeginChangeCheck(); float at = EditorGUILayout.DelayedFloatField("关键帧秒数", keyTime);
                    if (EditorGUI.EndChangeCheck() && Editable && selectedKey >= 0) { selectedKey = BrgAnimationClipEditing.MoveKey(preset, track, selectedKey, at); keyTime = BrgAnimationClipEditing.Curve(Clip,track)[selectedKey].time; Seek(keyTime); }
                }
            }
            var curve = BrgAnimationClipEditing.Curve(Clip, track);
            EditorGUI.BeginChangeCheck(); curve = EditorGUILayout.CurveField("曲线 / 切线", curve, Color.cyan, new Rect(), GUILayout.Height(65));
            if (EditorGUI.EndChangeCheck() && Editable) { try { BrgAnimationClipEditing.SetCurve(preset, track, curve); selectedKey = -1; error=null; } catch(System.ArgumentException exception) { error=exception.Message; } }
        }
        private void DeleteKey()
        { if (Editable && selectedKey >= 0) { BrgAnimationClipEditing.DeleteKey(preset, track, selectedKey); selectedKey = -1; } }
    }

    // Isolated scene and dedicated camera: preview labels cannot enter game cameras.
    internal sealed class BrgAnimationPreview : System.IDisposable
    {
        private Scene scene;
        private Camera camera;
        private RenderTexture target;
        internal BrgDamageTextRenderer Renderer { get; private set; }
        internal BrgAnimationPreview(TMP_FontAsset font, int size, float width, BrgDamageTextRenderer.SpaceMode space)
        {
            scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("BurstWord animation preview camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .05f, .07f);
            // BRG batches are not scene objects; a preview-scene-only culling mask
            // excludes them. The reserved layer keeps ordinary scene geometry out.
            camera.overrideSceneCullingMask = ulong.MaxValue;
            camera.cullingMask = 1 << 31;
            camera.cameraType = CameraType.Game;
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = space != BrgDamageTextRenderer.SpaceMode.WorldFollow;
            camera.orthographicSize = 4; camera.fieldOfView = 45; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            target = new RenderTexture(960, 540, 24) { name = "BurstWord GPU animation preview", hideFlags = HideFlags.HideAndDontSave }; target.Create();
            camera.targetTexture = target;
            // Construct the active SRP before registering BRG batches. An Edit-mode
            // window can be the first camera rendered after an asset/domain reload.
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            var root = new GameObject("BurstWord animation preview data") { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
            root.layer = 31;
            SceneManager.MoveGameObjectToScene(root, scene);
            Renderer = root.AddComponent<BrgDamageTextRenderer>(); Renderer.capacity = 64; Renderer.font = font;
            Renderer.worldCamera = camera; Renderer.fontSize = size; Renderer.wrapWidth = width; Renderer.spaceMode = space;
            Renderer.referenceResolution = new Vector2(960, 540); Renderer.worldUnitsPerLayoutUnit = .015f;
            var fonts = new System.Collections.Generic.List<TMP_FontAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset != font) fonts.Add(asset);
            }
            Renderer.fonts = fonts.ToArray(); root.SetActive(true); Renderer.Initialize();
            if (!Renderer.IsInitialized) { Dispose(); throw new System.InvalidOperationException("预览需要 TMP 字体、支持 BRG 的 URP 和 BurstWord 渲染功能。"); }
        }
        internal void Restart(string text, float duration, BrgTextAnimation animation)
        {
            Renderer.Clear(); Renderer.EditorPreviewFrame(0); Renderer.lifetime = duration;
            Renderer.defaultAnimation = animation;
            Renderer.EmitText(new BrgDamageTextRenderer.TextPose(Vector3.zero, Quaternion.identity, Vector3.one), text, Color.white);
        }
        internal void SetSpace(BrgDamageTextRenderer.SpaceMode space)
        { Renderer.spaceMode = space; camera.orthographic = space != BrgDamageTextRenderer.SpaceMode.WorldFollow; }
        internal RenderTexture Render(float time)
        {
            if (!Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
            Renderer.EditorPreviewFrame(time);
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            }
            finally { ShaderUtil.allowAsyncCompilation = asynchronous; }
            return target;
        }
        public void Dispose()
        {
            // Non-ExecuteAlways behaviours do not receive the usual edit-mode lifecycle.
            if (Renderer != null && Renderer.IsInitialized) Renderer.EditorDispose();
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); target = null; }
            Renderer = null;
        }
    }
}
