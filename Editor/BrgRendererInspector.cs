using BurstWord.BRG;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BurstWord.Baseline.Editor
{
    [CustomEditor(typeof(BrgDamageTextRenderer))]
    public sealed class BrgRendererInspector : UnityEditor.Editor
    {
        private bool fonts, materials, sprites, typography, animation, advanced;
        private float lastWrapWidth = 400;
        private bool resourceChanges;

        private void OnEnable()
        {
            var renderer = (BrgDamageTextRenderer)target;
            fonts = renderer.additionalFonts != null && renderer.additionalFonts.Length > 0;
            materials = renderer.fontMaterial != null || renderer.materialPresets != null && renderer.materialPresets.Length > 0;
            sprites = renderer.spriteAsset != null || renderer.additionalSpriteAssets != null && renderer.additionalSpriteAssets.Length > 0 ||
                renderer.spriteSequences != null && renderer.spriteSequences.Length > 0;
            animation = renderer.defaultAnimation != null;
        }

        private void Field(string name, string label, string tooltip = null)
        {
            var property = serializedObject.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
        }

        private bool Section(ref bool open, string label)
        {
            EditorGUILayout.Space(5);
            open = EditorGUILayout.Foldout(open, label, true);
            return open;
        }

        public override void OnInspectorGUI()
        {
            var renderer = (BrgDamageTextRenderer)target;
            serializedObject.Update();
            resourceChanges = false;
            using (new EditorGUI.DisabledScope(true)) Field("m_Script", "Script");
            EditorGUILayout.LabelField("Fonts", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Field("font", "Default Font", "Default TMP Font Asset. Its own fallback font assets are used automatically. Individual emissions can override it.");
            resourceChanges |= EditorGUI.EndChangeCheck();
            Field("fontSize", "Font Size");
            if (Section(ref fonts, "Additional fonts (optional)"))
            {
                EditorGUI.BeginChangeCheck(); Field("useAdditionalFonts", "Use Additional Fonts");
                if (serializedObject.FindProperty("useAdditionalFonts").boolValue)
                {
                    EditorGUILayout.HelpBox("Extra fonts for <font> tag lookup and additional missing-glyph lookup. The Default Font's own fallback list is already supported; do not duplicate it here. Direct font: arguments require no registration.", MessageType.Info);
                    Field("additionalFonts", "Additional Fonts");
                }
                resourceChanges |= EditorGUI.EndChangeCheck();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Required settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Field("worldCamera", "Camera", "The camera that displays this manager's text.");
            resourceChanges |= EditorGUI.EndChangeCheck();
            Field("lifetime", "Lifetime (seconds)");
            using (new EditorGUI.DisabledScope(Application.isPlaying)) Field("capacity", "Maximum Live Labels");
            DrawPipeline();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Space and ordering", EditorStyles.boldLabel);
            Field("sortingMode", "Occlusion");
            Field("spaceMode", "Space Mode");
            var space = serializedObject.FindProperty("spaceMode");
            if (space.enumValueIndex == (int)BrgDamageTextRenderer.SpaceMode.WorldFollow)
                Field("worldUnitsPerLayoutUnit", "World Units Per Layout Unit");
            else DrawScreenScaling();
            if (space.enumValueIndex != (int)BrgDamageTextRenderer.SpaceMode.ScreenSnapshot)
                EditorGUILayout.HelpBox("To follow an object, call EmitText(targetTransform, ...) or update a TextHandle's pose.", MessageType.Info);

            DrawAlignment();
            var width = serializedObject.FindProperty("wrapWidth");
            bool wrapping = width.floatValue > 0;
            bool selected = EditorGUILayout.Toggle("Automatic Wrapping", wrapping);
            if (selected != wrapping)
            {
                if (wrapping) { lastWrapWidth = width.floatValue; width.floatValue = 0; }
                else width.floatValue = Mathf.Max(1, lastWrapWidth);
            }
            if (selected) Field("wrapWidth", "Wrap Width", "Maximum line width in layout units. An enabled fixed text area can reduce this to its width.");

            if (Section(ref materials, "Font material effects (optional)"))
            {
                EditorGUI.BeginChangeCheck(); Field("useMaterialPresets", "Use Default / Tag Material Overrides");
                if (serializedObject.FindProperty("useMaterialPresets").boolValue)
                {
                    EditorGUILayout.HelpBox("Use TMP material presets for outline, underlay and glow. Each material must belong to the font it is used with. Direct material: arguments need no registration.", MessageType.Info);
                    Field("fontMaterial", "Default Font Material"); Field("materialPresets", "Materials For Rich Text Tags");
                }
                resourceChanges |= EditorGUI.EndChangeCheck();
            }
            if (Section(ref sprites, "Sprites and emoji (optional)"))
            {
                EditorGUI.BeginChangeCheck();
                Field("useSprites", "Use Sprites / Emoji");
                if (serializedObject.FindProperty("useSprites").boolValue)
                {
                    Field("spriteAsset", "Default Sprite Asset"); Field("additionalSpriteAssets", "Additional Sprite Assets");
                    Field("spriteSequences", "Text To Sprite Mappings");
                }
                resourceChanges |= EditorGUI.EndChangeCheck();
            }
            if (Section(ref typography, "Text layout and optional shaping"))
            {
                EditorGUI.BeginChangeCheck();
                Field("richText", "Rich Text Tags"); Field("enableKerning", "Font Kerning");
                Field("enableShaping", "Use Shaping Provider");
                if (serializedObject.FindProperty("enableShaping").boolValue)
                {
                    Field("textShaper", "Text Shaper");
                    if (serializedObject.FindProperty("textShaper").objectReferenceValue != null)
                    { Field("enableLigatures", "Ligatures"); Field("fontSources", "Font Source Override"); }
                    else EditorGUILayout.HelpBox("None uses TMP glyph data. Install and assign a provider only when complex-script shaping is needed.", MessageType.Info);
                }
                resourceChanges |= EditorGUI.EndChangeCheck();
            }
            if (Section(ref animation, "GPU animation (optional)"))
            {
                Field("useDefaultAnimation", "Use Default Animation");
                if (serializedObject.FindProperty("useDefaultAnimation").boolValue)
                    Field("defaultAnimation", "Default Animation");
                if (serializedObject.FindProperty("useDefaultAnimation").boolValue &&
                    serializedObject.FindProperty("defaultAnimation").objectReferenceValue != null)
                {
                    if (GUILayout.Button("Edit / Preview Animation"))
                        BrgAnimationEditor.Open((BrgTextAnimation)serializedObject.FindProperty("defaultAnimation").objectReferenceValue, renderer);
                    Field("animationPresets", "Preloaded Animations");
                }
                else
                {
                    Field("risePixels", "Linear Rise Distance");
                    EditorGUILayout.HelpBox("None uses the built-in linear animation. An animation: argument can choose a different preset for each emission.", MessageType.Info);
                }
            }
            if (Section(ref advanced, "Advanced rendering"))
            {
                EditorGUI.BeginChangeCheck(); Field("renderBackend", "Backend");
                resourceChanges |= EditorGUI.EndChangeCheck();
                Field("enablePreparationJobs", "Batch Preparation Jobs"); Field("tightGlyphBounds", "Tight Glyph Bounds");
                EditorGUILayout.HelpBox("The glyph shader is selected automatically by the backend. Change it only when supplying a compatible custom shader.", MessageType.Info);
                EditorGUI.BeginChangeCheck(); Field("glyphShader", "Glyph Shader Override");
                resourceChanges |= EditorGUI.EndChangeCheck();
            }
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying && renderer.isActiveAndEnabled && resourceChanges)
            { renderer.enabled = false; renderer.enabled = true; }
            if (Application.isPlaying) DrawStatistics(renderer);
        }

        private void DrawAlignment()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Text alignment", EditorStyles.boldLabel);
            var anchor = serializedObject.FindProperty("alignment");
            int horizontal = anchor.enumValueIndex % 3, vertical = anchor.enumValueIndex / 3;
            EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel("Horizontal");
            horizontal = GUILayout.Toolbar(horizontal, new[] { "Left", "Center", "Right" });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel("Vertical");
            vertical = GUILayout.Toolbar(vertical, new[] { "Top", "Middle", "Bottom" });
            EditorGUILayout.EndHorizontal();
            anchor.enumValueIndex = vertical * 3 + horizontal;
            Field("useTextArea", "Use Fixed Text Area", "Off: align against the emission point. On: align within an area centered on that point.");
            if (serializedObject.FindProperty("useTextArea").boolValue)
                Field("textAreaSize", "Text Area Size", "Width / height in layout units, scaled with the text. This area does not clip overflowing text.");
        }

        private void DrawScreenScaling()
        {
            EditorGUILayout.LabelField("UI scaling", EditorStyles.boldLabel);
            Field("scalingCanvas", "Use Existing UI Canvas", "Optional. Uses the root Canvas's actual scale. Leave empty to configure the same modes as Canvas Scaler below.");
            var canvas = serializedObject.FindProperty("scalingCanvas").objectReferenceValue as Canvas;
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
            {
                EditorGUILayout.HelpBox("Uses the existing root Canvas's scale factor. No UI objects are created by BurstWord.", MessageType.Info);
                return;
            }
            if (canvas != null) EditorGUILayout.HelpBox("Use a screen-space Canvas. A World Space Canvas does not define screen text scaling; manual settings apply instead.", MessageType.Warning);
            Field("uiScaleMode", "UI Scale Mode");
            switch ((CanvasScaler.ScaleMode)serializedObject.FindProperty("uiScaleMode").enumValueIndex)
            {
                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    Field("scaleFactor", "Scale Factor");
                    break;
                case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                    Field("referenceResolution", "Reference Resolution");
                    Field("screenMatchMode", "Screen Match Mode");
                    if (serializedObject.FindProperty("screenMatchMode").enumValueIndex == (int)CanvasScaler.ScreenMatchMode.MatchWidthOrHeight)
                    {
                        Field("matchWidthOrHeight", "Match", "0 = Width, 1 = Height. Uses Canvas Scaler's logarithmic interpolation.");
                        EditorGUILayout.LabelField("", "0 = Width                  1 = Height", EditorStyles.miniLabel);
                    }
                    break;
                case CanvasScaler.ScaleMode.ConstantPhysicalSize:
                    Field("physicalUnit", "Physical Unit"); Field("fallbackScreenDPI", "Fallback Screen DPI");
                    break;
            }
        }

        private void DrawPipeline()
        {
            var camera = serializedObject.FindProperty("worldCamera").objectReferenceValue as Camera;
            bool ready = BrgRenderingSetup.CheckCamera(camera != null ? camera : Camera.main, out var data, out string message);
            EditorGUILayout.HelpBox(message, ready ? MessageType.Info : MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Install BRG Rendering")) BrgRenderingSetup.Install();
                using (new EditorGUI.DisabledScope(data == null))
                    if (GUILayout.Button("Select Renderer Data")) { Selection.activeObject = data; EditorGUIUtility.PingObject(data); }
            }
        }

        private static void DrawStatistics(BrgDamageTextRenderer renderer)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live rendering statistics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Active backend", renderer.ActiveBackend.ToString());
            EditorGUILayout.LabelField("Backend selection", renderer.BackendReason);
            EditorGUILayout.LabelField("Typography", renderer.ShaperName);
            EditorGUILayout.LabelField("Screen scale factor", renderer.CurrentScreenScale.ToString("0.###"));
            EditorGUILayout.LabelField("Active labels / glyphs", renderer.ActiveCount + " / " + renderer.ActiveGlyphCount);
            EditorGUILayout.LabelField("Emitted / capacity drops", renderer.EmittedCount + " / " + renderer.DroppedCount);
            EditorGUILayout.LabelField("Draw commands / submitted glyphs", renderer.DrawCommandCount + " / " + renderer.SubmittedGlyphCount);
            EditorGUILayout.LabelField("Missing glyphs / sprites / shaping sources",renderer.MissingGlyphCount+" / "+renderer.MissingSpriteCount+" / "+renderer.UnavailableShapingCount);
        }
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
