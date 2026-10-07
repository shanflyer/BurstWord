using System;
using System.Collections.Generic;
using BurstWord.BRG;
using BurstWord.Typography;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BurstWord.Baseline.Editor
{
    public sealed class BrgLayoutPreviewWindow : EditorWindow
    {
        [SerializeField] private BrgDamageTextRenderer source;
        [SerializeField] private string text = "Critical 12345\nPreview text: change the width to see automatic wrapping.";
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontIndex;
        [SerializeField] private bool autoFit = true;
        [SerializeField] private float zoom = 1;
        [SerializeField] private Vector2 pan;
        private Vector2 scroll;
        private float lastWrapWidth = 400;
        private string configuration, error;
        private double nextPoll;
        private bool dirty = true, draggingArea;
        private int resizeControl;
        private Rect resizeStart;
        private Vector2 resizeMouse;
        private BrgLayoutPreview preview;
        private ITextShaper shaping;
        private int resourceStamp;
        private static readonly Color AreaColor = new Color(.25f, .85f, 1);
        private static readonly Color GlyphColor = new Color(1, .8f, .3f);
        private static readonly Color WrapColor = new Color(.95f, .5f, .85f);

        public static void Open(BrgDamageTextRenderer renderer)
        {
            var window = GetWindow<BrgLayoutPreviewWindow>("BurstWord Layout");
            window.minSize = new Vector2(820, 550);
            window.SetSource(renderer); window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(820, 550);
            EditorApplication.update += Poll;
            EditorApplication.projectChanged += Invalidate;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            Undo.undoRedoPerformed += Invalidate;
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreview;
            Invalidate();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Poll;
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            Undo.undoRedoPerformed -= Invalidate;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreview;
            if (GUIUtility.hotControl == resizeControl) GUIUtility.hotControl = 0;
            DisposePreview();
        }

        private void PlayModeChanged(PlayModeStateChange state) { DisposePreview(); Invalidate(); }
        private void DisposePreview() { preview?.Dispose(); preview = null; }
        private void Invalidate() { configuration = null; dirty = true; error = null; Repaint(); }
        private void SetSource(BrgDamageTextRenderer value)
        {
            if (source == value && preview != null) return;
            source = value; fontIndex = 0; pan = Vector2.zero;
            if (source != null && source.wrapWidth > 0) lastWrapWidth = source.wrapWidth;
            DisposePreview(); Invalidate();
        }

        private void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .2;
            if (source == null) { if (preview != null) { DisposePreview(); Repaint(); } return; }
            if (EditorJsonUtility.ToJson(source) != configuration || !ReferenceEquals(shaping, source.EditorTextShaper) || ResourceStamp() != resourceStamp)
            { dirty = true; error = null; Repaint(); }
        }

        private int ResourceStamp()
        {
            int stamp = 17;
            var visited = new HashSet<Object>();
            bool Add(Object asset)
            {
                if (asset == null || !visited.Add(asset)) return false;
                unchecked { stamp = stamp * 31 + asset.GetHashCode(); stamp = stamp * 31 + EditorUtility.GetDirtyCount(asset); }
                return true;
            }
            void Font(TMP_FontAsset asset)
            {
                if (!Add(asset)) return;
                Add(asset.material);
                if (asset.fallbackFontAssetTable != null) foreach (var fallback in asset.fallbackFontAssetTable) Font(fallback);
                if (asset.fontWeightTable != null) foreach (var pair in asset.fontWeightTable) { Font(pair.regularTypeface); Font(pair.italicTypeface); }
            }
            void Sprite(TMP_SpriteAsset asset)
            {
                if (!Add(asset)) return;
                Add(asset.material);
                if (asset.fallbackSpriteAssets != null) foreach (var fallback in asset.fallbackSpriteAssets) Sprite(fallback);
            }
            Font(source.font);
            if (source.fonts != null) foreach (var font in source.fonts) Font(font);
            if (TMP_Settings.fallbackFontAssets != null) foreach (var font in TMP_Settings.fallbackFontAssets) Font(font);
            if (source.useSprites)
            {
                Sprite(source.spriteAsset != null ? source.spriteAsset : TMP_Settings.defaultSpriteAsset);
                if (source.additionalSpriteAssets != null) foreach (var sprite in source.additionalSpriteAssets) Sprite(sprite);
            }
            Add(source.EditorTextShaper as Object);
            return stamp;
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Manager", GUILayout.Width(55));
                var selected = (BrgDamageTextRenderer)EditorGUILayout.ObjectField(source, typeof(BrgDamageTextRenderer), true);
                if (selected != source) { SetSource(selected); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("Select", EditorStyles.toolbarButton, GUILayout.Width(55)) && source != null)
                    Selection.activeObject = source;
            }
            if (source == null)
            {
                EditorGUILayout.HelpBox("Select a BurstWord manager, or open this window from Text Layout → Open Layout Preview.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSettings();
                using (new EditorGUILayout.VerticalScope()) DrawView();
            }
        }

        private void DrawSettings()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(300)))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.LabelField("Preview Content", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                text = EditorGUILayout.TextArea(text, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.MinHeight(115));
                textColor = EditorGUILayout.ColorField("Preview Color", textColor);
                int count = 1 + (source.fonts?.Length ?? 0);
                var names = new string[count]; names[0] = "[0] " + (source.font != null ? source.font.name : "Default (unassigned)");
                for (int i = 1; i < count; i++) names[i] = "[" + i + "] " + (source.fonts[i - 1] != null ? source.fonts[i - 1].name : "Empty");
                fontIndex = EditorGUILayout.Popup("Preview Font", Mathf.Clamp(fontIndex, 0, count - 1), names);
                if (EditorGUI.EndChangeCheck()) { dirty = true; error = null; }
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Manager Layout", EditorStyles.boldLabel);
                // These are the manager's actual serialized settings, with normal Undo support.
                // In Play, view only: the preview never restarts or clears the game manager.
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    var settings = new SerializedObject(source); settings.Update();
                    EditorGUILayout.PropertyField(settings.FindProperty("fontSize"), new GUIContent("Font Size"));
                    BrgRendererInspector.DrawLayoutControls(settings, ref lastWrapWidth);
                    if (!Application.isPlaying && settings.ApplyModifiedProperties()) Invalidate();
                }
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("Cyan: fixed area. Yellow: glyph bounds (includes effect padding). Pink: wrap limit. +: emission point.\nDimensions are layout units; view zoom does not change your settings.", MessageType.None);
                EditorGUILayout.LabelField("Scroll: zoom • Middle / Alt+Left drag: pan", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Drag the cyan corner to resize the fixed area. Layout edits support Undo.", EditorStyles.wordWrappedMiniLabel);
                if (Application.isPlaying) EditorGUILayout.LabelField("Manager layout is read-only during Play.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void EnsurePreview()
        {
            string current = EditorJsonUtility.ToJson(source);
            int stamp = ResourceStamp();
            if (preview != null && configuration == current && ReferenceEquals(shaping, source.EditorTextShaper) && stamp == resourceStamp) return;
            DisposePreview(); configuration = current; shaping = source.EditorTextShaper; resourceStamp = stamp;
            preview = new BrgLayoutPreview(source, current); dirty = true;
        }

        private void DrawView()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool fit = GUILayout.Toggle(autoFit, "Auto Fit", EditorStyles.toolbarButton, GUILayout.Width(65));
                if (fit != autoFit) { autoFit = fit; dirty = true; }
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(80))) { autoFit = true; pan = Vector2.zero; dirty = true; }
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                float selectedZoom = EditorGUILayout.Slider(zoom, .02f, 8, GUILayout.Width(170));
                if (EditorGUI.EndChangeCheck()) { zoom = selectedZoom; autoFit = false; dirty = true; }
                GUILayout.Label(zoom.ToString("0.##") + "×", GUILayout.Width(45));
            }
            Rect canvas = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            // Reserve the same controls on Layout and Repaint, including the very first frame
            // before the GPU preview exists. Dynamic GUILayout footers would break IMGUI.
            Rect footer = GUILayoutUtility.GetRect(100, 36, GUILayout.ExpandWidth(true));
            resizeControl = GUIUtility.GetControlID("BurstWordLayoutResize".GetHashCode(), FocusType.Passive, canvas);
            if (canvas.width <= 1 || canvas.height <= 1) return;
            try
            {
                // Graphics work only on Repaint. Layout/input events allocate no preview scene.
                if (Event.current.type == EventType.Repaint && error == null)
                {
                    EnsurePreview();
                    if (dirty)
                    {
                        preview.Measure(text, textColor, fontIndex);
                        dirty = false;
                    }
                    if (autoFit && !draggingArea) Fit(canvas);
                }
            }
            catch (Exception exception) { DisposePreview(); error = exception.Message; dirty = false; }
            if (preview == null || error != null)
            {
                GUI.Box(canvas, GUIContent.none);
                GUI.Label(new Rect(canvas.x + 20, canvas.y + 20, canvas.width - 40, canvas.height - 40), error ?? "Preparing preview…", EditorStyles.wordWrappedLabel);
                if (error != null && GUI.Button(new Rect(canvas.x + 20, canvas.yMax - 40, 110, 24), "Retry")) Invalidate();
                DrawFooter(footer);
                return;
            }
            HandleViewInput(canvas);
            if (Event.current.type == EventType.Repaint)
            {
                try { GUI.DrawTexture(canvas, preview.Render(canvas.size, zoom, pan, EditorGUIUtility.pixelsPerPoint), ScaleMode.StretchToFill, false); }
                catch (Exception exception) { DisposePreview(); error = exception.Message; Repaint(); DrawFooter(footer); return; }
                GUI.BeginClip(canvas);
                DrawGuides(new Rect(Vector2.zero, canvas.size));
                GUI.EndClip();
            }
            HandleAreaResize(canvas);
            DrawFooter(footer);
        }

        private void DrawFooter(Rect footer)
        {
            string area = source.useTextArea ? "Area " + Dimensions(source.textAreaSize) : "Point alignment";
            string wrap = EffectiveWrap > 0 ? "Wrap " + EffectiveWrap.ToString("0.##") : "Wrapping off";
            GUI.Label(new Rect(footer.x, footer.y, footer.width, 18), area + "  |  " + wrap, EditorStyles.miniLabel);
            string measured = preview != null ? "Text " + Dimensions(preview.LayoutSize) + "  |  " + preview.LineCount + " lines  |  " + preview.GlyphCount + " glyphs" : "";
            GUI.Label(new Rect(footer.x, footer.y + 18, footer.width, 18), measured, EditorStyles.miniLabel);
        }

        private float EffectiveWrap => source.wrapWidth > 0 && source.useTextArea && source.textAreaSize.x > 0
            ? Mathf.Min(source.wrapWidth, source.textAreaSize.x) : source.wrapWidth;
        private Rect Area => new Rect(-source.textAreaSize * .5f, source.textAreaSize);
        private static string Dimensions(Vector2 size) => size.x.ToString("0.##") + " × " + size.y.ToString("0.##");
        private Vector2 Origin(Rect canvas) => canvas.center + pan;
        private Vector2 ToView(Vector2 point, Rect canvas) => Origin(canvas) + new Vector2(point.x, -point.y) * zoom;
        private Rect ToView(Rect rect, Rect canvas) => new Rect(ToView(new Vector2(rect.xMin, rect.yMax), canvas), rect.size * zoom);

        private void Fit(Rect canvas)
        {
            Rect extent = new Rect(-20, -20, 40, 40);
            if (source.useTextArea) extent = Union(extent, Area);
            if (preview.HasBounds) extent = Union(extent, preview.Bounds);
            if (EffectiveWrap > 0) extent = Union(extent, new Rect(WrapStart(), -20, EffectiveWrap, 40));
            zoom = Mathf.Clamp(Mathf.Min(Mathf.Max(1, canvas.width - 100) / Mathf.Max(1, extent.width),
                Mathf.Max(1, canvas.height - 90) / Mathf.Max(1, extent.height)), .02f, 8);
            pan = new Vector2(-extent.center.x, extent.center.y) * zoom;
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        private float WrapStart()
        {
            float half = source.useTextArea ? Mathf.Max(0, source.textAreaSize.x) * .5f : 0;
            switch ((int)source.alignment % 3) { case 0: return -half; case 2: return half - EffectiveWrap; default: return -EffectiveWrap * .5f; }
        }

        private void DrawGuides(Rect canvas)
        {
            Vector2 origin = Origin(canvas);
            float step = Mathf.Pow(10, Mathf.Ceil(Mathf.Log10(45 / zoom)));
            if (step * zoom > 120) step *= .5f;
            var grid = new Color(1, 1, 1, .07f);
            float pixels = step * zoom;
            for (float x = origin.x % pixels; x < canvas.width; x += pixels) EditorGUI.DrawRect(new Rect(x, 0, 1, canvas.height), grid);
            for (float y = origin.y % pixels; y < canvas.height; y += pixels) EditorGUI.DrawRect(new Rect(0, y, canvas.width, 1), grid);
            if (source.useTextArea)
            {
                Rect area = ToView(Area, canvas); Outline(area, AreaColor);
                Caption(new Vector2(area.x, area.y - 20), "Area " + Dimensions(source.textAreaSize), AreaColor);
                EditorGUI.DrawRect(new Rect(area.xMax - 4, area.yMax - 4, 8, 8), AreaColor);
            }
            if (preview.HasBounds)
            {
                Rect bounds = ToView(preview.Bounds, canvas); Outline(bounds, new Color(GlyphColor.r, GlyphColor.g, GlyphColor.b, .55f));
                Caption(new Vector2(bounds.x, bounds.yMax + 4), "Glyphs " + Dimensions(preview.Bounds.size), GlyphColor);
            }
            if (EffectiveWrap > 0)
            {
                float left = ToView(new Vector2(WrapStart(), 0), canvas).x, right = left + EffectiveWrap * zoom;
                for (float y = 0; y < canvas.height; y += 8)
                { EditorGUI.DrawRect(new Rect(left, y, 1, 4), WrapColor); EditorGUI.DrawRect(new Rect(right, y, 1, 4), WrapColor); }
                Caption(new Vector2(left, 6), "Wrap " + EffectiveWrap.ToString("0.##"), WrapColor);
            }
            EditorGUI.DrawRect(new Rect(origin.x - 7, origin.y, 15, 1), Color.white);
            EditorGUI.DrawRect(new Rect(origin.x, origin.y - 7, 1, 15), Color.white);
            Caption(new Vector2(origin.x + 10, origin.y + 3), "0, 0", Color.white);
            Caption(new Vector2(8, canvas.height - 22), "Grid " + step.ToString("0.##") + " layout units", new Color(.65f, .7f, .75f));
        }

        private static void Outline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color); EditorGUI.DrawRect(new Rect(rect.x, rect.yMax, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color); EditorGUI.DrawRect(new Rect(rect.xMax, rect.y, 1, rect.height), color);
        }
        private static void Caption(Vector2 position, string label, Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            var size = style.CalcSize(new GUIContent(label));
            EditorGUI.DrawRect(new Rect(position, size + new Vector2(6, 2)), new Color(.035f, .05f, .07f, .92f));
            GUI.Label(new Rect(position + new Vector2(3, 1), size), label, style);
        }

        private void HandleViewInput(Rect canvas)
        {
            var e = Event.current;
            if (!canvas.Contains(e.mousePosition) || draggingArea) return;
            if (e.type == EventType.ScrollWheel)
            {
                Vector2 before = e.mousePosition - canvas.center - pan;
                float next = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -e.delta.y), .02f, 8);
                pan += before * (1 - next / zoom); zoom = next; autoFit = false; e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 2 || e.button == 0 && e.alt))
            { pan += e.delta; autoFit = false; e.Use(); Repaint(); }
        }

        private void HandleAreaResize(Rect canvas)
        {
            if (Application.isPlaying || !source.useTextArea || preview == null) return;
            var e = Event.current;
            Rect area = ToView(Area, canvas);
            var handle = new Rect(area.xMax - 7, area.yMax - 7, 14, 14);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeUpLeft);
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && canvas.Contains(e.mousePosition) && handle.Contains(e.mousePosition))
            {
                Undo.RegisterCompleteObjectUndo(source, "Resize BurstWord text area");
                resizeStart = Area; resizeMouse = e.mousePosition; draggingArea = true;
                GUIUtility.hotControl = resizeControl; e.Use();
            }
            else if (draggingArea && GUIUtility.hotControl == resizeControl && e.type == EventType.MouseDrag)
            {
                Vector2 delta = (e.mousePosition - resizeMouse) / zoom;
                source.textAreaSize = Vector2.Max(Vector2.one, resizeStart.size + delta * 2);
                EditorUtility.SetDirty(source); PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                if (source.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
                configuration = null; dirty = true; error = null; e.Use(); Repaint();
            }
            else if (draggingArea && e.type == EventType.MouseUp)
            { draggingArea = false; GUIUtility.hotControl = 0; e.Use(); Repaint(); }
        }
    }

    // One hidden data manager in an isolated, unsaved preview scene. No per-glyph objects.
    internal sealed class BrgLayoutPreview : IDisposable
    {
        private Scene scene;
        private Camera camera;
        private RenderTexture target;
        private string text;
        private Color color;
        private int fontIndex;
        internal BrgDamageTextRenderer Renderer { get; private set; }
        internal Rect Bounds { get; private set; }
        internal bool HasBounds { get; private set; }
        internal Vector2 LayoutSize { get; private set; }
        internal int LineCount { get; private set; }
        internal int GlyphCount { get; private set; }

        internal BrgLayoutPreview(BrgDamageTextRenderer source, string configuration)
        {
            try
            {
                if (source.font == null) throw new InvalidOperationException("Assign a Default Text Font in Fonts to preview this layout.");
                scene = EditorSceneManager.NewPreviewScene();
                var cameraObject = new GameObject("BurstWord layout preview camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.overrideSceneCullingMask = ulong.MaxValue; camera.cullingMask = 1 << 31; camera.cameraType = CameraType.Game;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .05f, .07f);
                camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                var sourceCamera = source.worldCamera != null ? source.worldCamera : Camera.main;
                if (sourceCamera != null && sourceCamera.TryGetComponent<UniversalAdditionalCameraData>(out var sourceData))
                {
                    var index = new SerializedObject(sourceData).FindProperty("m_RendererIndex");
                    if (index != null) data.SetRenderer(index.intValue);
                }
                if (!BrgRenderingSetup.CheckCamera(camera, out _, out string message)) throw new InvalidOperationException(message + " Use Setup → Install BRG Rendering.");
                Resize(640, 480);
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var root = new GameObject("BurstWord layout preview data") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false); root.layer = 31; SceneManager.MoveGameObjectToScene(root, scene);
                Renderer = root.AddComponent<BrgDamageTextRenderer>();
                EditorJsonUtility.FromJsonOverwrite(configuration, Renderer);
                Renderer.SetTextShaperProvider(source.EditorTextShaper);
                Renderer.capacity = 4; Renderer.worldCamera = camera; Renderer.enabled = true;
                // Single-label editor view: use our existing instancing path, which shares
                // layout and glyph shading with BRG and supports immediate isolated render requests.
                // This choice never changes the source manager's game backend.
                Renderer.renderBackend = BrgDamageTextRenderer.RenderBackend.Instancing;
                Renderer.spaceMode = BrgDamageTextRenderer.SpaceMode.ScreenSnapshot;
                Renderer.sortingMode = BrgDamageTextRenderer.SortingMode.AlwaysInFront;
                Renderer.scalingCanvas = null; Renderer.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; Renderer.scaleFactor = 1;
                Renderer.animations = Array.Empty<BrgTextAnimation>(); Renderer.risePixels = 0;
                root.SetActive(true); Renderer.EditorPreviewFrame(0);
                if (!Renderer.IsInitialized) throw new InvalidOperationException("Could not initialize the layout preview renderer.");
            }
            catch { Dispose(); throw; }
        }

        internal void Measure(string content, Color tint, int selectedFont)
        {
            text = content; color = tint; fontIndex = selectedFont;
            Emit(Vector3.zero);
            HasBounds = Renderer.EditorTryGetLayoutBounds(out Rect bounds); Bounds = HasBounds ? bounds : default;
            bool anyText = !string.IsNullOrEmpty(text);
            LayoutSize = anyText ? Renderer.LastLayoutSize : Vector2.zero;
            LineCount = anyText ? Renderer.LastLayoutLineCount : 0; GlyphCount = Renderer.ActiveGlyphCount;
        }

        private void Emit(Vector3 position)
        {
            Renderer.Clear(); Renderer.ResetCounters(); Renderer.EditorPreviewFrame(0);
            if (!string.IsNullOrEmpty(text))
                Renderer.EmitText(position, text, color, duration: 100, fontIndex: fontIndex, useLegacyAnimation: true);
        }

        internal RenderTexture Render(Vector2 size, float zoom, Vector2 pan, float pixelsPerPoint)
        {
            // Preserve the GUI aspect ratio even when a large window needs a capped texture.
            float density = Mathf.Min(pixelsPerPoint, 2048 / Mathf.Max(1, size.x), 2048 / Mathf.Max(1, size.y));
            Resize(Mathf.Max(1, Mathf.RoundToInt(size.x * density)), Mathf.Max(1, Mathf.RoundToInt(size.y * density)));
            Renderer.scaleFactor = zoom * density;
            Vector3 position = camera.ScreenToWorldPoint(new Vector3(target.width * .5f + pan.x * density, target.height * .5f - pan.y * density, 10));
            Emit(position); Renderer.EditorPreviewFrame(0);
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            }
            finally { ShaderUtil.allowAsyncCompilation = asynchronous; }
            return target;
        }

        private void Resize(int width, int height)
        {
            if (target != null && target.width == width && target.height == height) return;
            if (target != null) { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            target = new RenderTexture(width, height, 24) { name = "BurstWord layout preview", hideFlags = HideFlags.HideAndDontSave };
            target.Create(); camera.targetTexture = target;
        }

        public void Dispose()
        {
            if (Renderer != null && Renderer.IsInitialized) Renderer.EditorDispose();
            Renderer = null;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); target = null; }
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scene = default; camera = null;
        }
    }
}
