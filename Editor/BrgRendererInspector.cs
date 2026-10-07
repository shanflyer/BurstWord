using BurstWord.BRG;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace BurstWord.Baseline.Editor
{
    [CustomEditor(typeof(BrgDamageTextRenderer))]
    public sealed class BrgRendererInspector : UnityEditor.Editor
    {
        private const string SectionKey = "BurstWord.RendererInspector.";
        private ReorderableList fontList, animationList, effectList;
        private float lastWrapWidth = 400;
        private bool resourceChanges;
        private UnityEditor.Editor effectEditor;

        private void OnDisable() { if (effectEditor != null) DestroyImmediate(effectEditor); effectEditor = null; }

        private void OnEnable()
        {
            var renderer = (BrgDamageTextRenderer)target;
            if (renderer.wrapWidth > 0) lastWrapWidth = renderer.wrapWidth;
            fontList = new ReorderableList(serializedObject, serializedObject.FindProperty("fonts"), true, true, true, true);
            fontList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, new GUIContent("Additional Text Fonts [1..N]", "Select with fontIndex. Missing glyphs follow the selected TMP font's fallback chain."));
            fontList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2; rect.height = EditorGUIUtility.singleLineHeight;
                DrawIndexedAsset(rect, fontList.serializedProperty.GetArrayElementAtIndex(index), index + 1);
            };
            fontList.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize++;
                list.serializedProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
            };
            ConfigureEffectList();
            animationList = new ReorderableList(serializedObject, serializedObject.FindProperty("animations"), true, true, true, true);
            animationList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Animations (index 0 = default)");
            animationList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2; rect.height = EditorGUIUtility.singleLineHeight;
                var property = animationList.serializedProperty.GetArrayElementAtIndex(index);
                var edit = new Rect(rect.xMax - 92, rect.y, 92, rect.height);
                rect.width -= 98;
                DrawIndexedAsset(rect, property, index, "Empty uses built-in linear motion.");
                using (new EditorGUI.DisabledScope(property.objectReferenceValue == null))
                    if (GUI.Button(edit, "Edit / Preview"))
                        BrgAnimationEditor.Open((BrgTextAnimation)property.objectReferenceValue, renderer);
            };
            animationList.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize++;
                list.serializedProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
            };
        }

        private void ConfigureEffectList()
        {
            effectList = new ReorderableList(serializedObject, serializedObject.FindProperty("effectMaterials"), true, true, true, true);
            effectList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Shader Effects (index 0 = default)");
            effectList.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 10;
            effectList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2; rect.height = EditorGUIUtility.singleLineHeight;
                var property = effectList.serializedProperty.GetArrayElementAtIndex(index);
                var edit = new Rect(rect.xMax - 45, rect.y, 45, rect.height); rect.width -= 51;
                DrawIndexedAsset(rect, property, index, "Empty uses the built-in shader.");
                var material = property.objectReferenceValue as Material;
                using (new EditorGUI.DisabledScope(material == null))
                    if (GUI.Button(edit, "Edit")) { effectList.index = index; EditorGUIUtility.PingObject(material); }
                rect.y += EditorGUIUtility.singleLineHeight + 3; rect.width += 51;
                var report = BrgShaderEffectCreator.Report(material != null ? material.shader : null);
                var style = new GUIStyle(EditorStyles.miniLabel);
                if (report.type == MessageType.Error) style.normal.textColor = new Color(1, .3f, .3f);
                else if (report.type == MessageType.Warning) style.normal.textColor = new Color(1, .65f, .15f);
                EditorGUI.LabelField(rect, new GUIContent(report.text, report.text), style);
            };
            effectList.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize++;
                list.serializedProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
            };
        }

        private void Field(string name, string label, string tooltip = null)
        {
            var property = serializedObject.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
        }

        private static void DrawIndexedAsset(Rect rect, SerializedProperty property, int index, string tooltip = null)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 36;
            try { EditorGUI.PropertyField(rect, property, new GUIContent("[" + index + "]", tooltip)); }
            finally { EditorGUIUtility.labelWidth = labelWidth; }
        }

        private static bool BeginSection(string key, string label)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool previous = SessionState.GetBool(SectionKey + key, true);
            bool open = EditorGUILayout.Foldout(previous, label, true, EditorStyles.foldoutHeader);
            if (open != previous) SessionState.SetBool(SectionKey + key, open);
            if (!open) EditorGUILayout.EndVertical();
            return open;
        }

        private static void EndSection() => EditorGUILayout.EndVertical();

        public override void OnInspectorGUI()
        {
            var renderer = (BrgDamageTextRenderer)target;
            serializedObject.Update();
            resourceChanges = false;
            using (new EditorGUI.DisabledScope(true)) Field("m_Script", "Script");
            if (BeginSection("setup", "Setup"))
            { DrawSetup(); EndSection(); }
            if (BeginSection("fonts", "Fonts"))
            { DrawFonts(); EndSection(); }
            if (BeginSection("layout", "Text Layout"))
            { DrawTextLayout(); EndSection(); }
            if (BeginSection("space", "Space & Occlusion"))
            { DrawSpace(); EndSection(); }
            if (serializedObject.FindProperty("spaceMode").enumValueIndex != (int)BrgDamageTextRenderer.SpaceMode.WorldFollow &&
                BeginSection("scaling", "UI Scaling"))
            { DrawScreenScaling(); EndSection(); }
            if (BeginSection("animations", "Animations"))
            {
                animationList.DoLayoutList();
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Built-in Motion", EditorStyles.boldLabel);
                Field("risePixels", "Rise Height", "Total upward travel in layout units for built-in motion. Negative values move downward. Captured on emission; does not change animation asset curves.");
                EditorGUILayout.LabelField("Empty lists / slots use built-in motion. Speed = Rise Height / duration.", EditorStyles.wordWrappedMiniLabel);
                EndSection();
            }
            if (BeginSection("effects", "Shader Effects"))
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying)) effectList.DoLayoutList();
                EditorGUILayout.LabelField("Empty lists / slots use built-in shading. effectIndex: -1 always selects built-in.", EditorStyles.wordWrappedMiniLabel);
                if ((renderer.effectMaterials == null || renderer.effectMaterials.Length == 0) && renderer.shaderEffects?.Length > 0)
                {
                    EditorGUILayout.HelpBox("Existing shader entries still render. Convert them to materials to configure their custom properties.", MessageType.Info);
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                        if (GUILayout.Button("Convert Shader Entries to Materials"))
                        { serializedObject.ApplyModifiedProperties(); BrgShaderEffectCreator.ConvertLegacy(renderer); serializedObject.Update(); }
                }
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button("Create Custom Effect Shader"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        BrgShaderEffectCreator.CreateAndAssign(renderer);
                        serializedObject.Update();
                        effectList.index = effectList.serializedProperty.arraySize - 1;
                    }
                foreach (var material in renderer.effectMaterials ?? System.Array.Empty<Material>())
                {
                    var report = BrgShaderEffectCreator.Report(material != null ? material.shader : null);
                    if (report.type == MessageType.Error || report.type == MessageType.Warning) EditorGUILayout.HelpBox(report.text, report.type);
                }
                if (effectList.index >= 0 && effectList.index < effectList.serializedProperty.arraySize)
                {
                    var material = effectList.serializedProperty.GetArrayElementAtIndex(effectList.index).objectReferenceValue as Material;
                    if (material != null)
                    {
                        EditorGUILayout.Space(4); EditorGUILayout.LabelField("Material Properties", EditorStyles.boldLabel);
                        if (GUILayout.Button("Edit Shader")) AssetDatabase.OpenAsset(material.shader);
                        if (BrgShaderEffectCreator.DrawMaterialProperties(material, ref effectEditor) && Application.isPlaying && renderer.IsInitialized)
                            renderer.RefreshEffectMaterial(material);
                    }
                }
                EndSection();
            }
            if (BeginSection("preview", "Preview"))
            {
                EditorGUILayout.LabelField("Preview text, materials, layout and one full animation in an isolated window.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Open Preview"))
                { serializedObject.ApplyModifiedProperties(); BrgLayoutPreviewWindow.Open(renderer); }
                EndSection();
            }
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying && renderer.isActiveAndEnabled && resourceChanges)
            { renderer.enabled = false; renderer.enabled = true; }
            if (Application.isPlaying && BeginSection("status", "Runtime Status"))
            { DrawStatistics(renderer); EndSection(); }
        }

        private void DrawSetup()
        {
            EditorGUI.BeginChangeCheck();
            Field("worldCamera", "Camera", "Leave empty to use Camera.main on initialization.");
            resourceChanges |= EditorGUI.EndChangeCheck();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                Field("capacity", "Maximum Live Labels", "Maximum simultaneous messages. Configure before Play.");
            DrawPipeline();
        }

        private void DrawFonts()
        {
            EditorGUI.BeginChangeCheck();
            Field("font", "Default Text Font [0]", "TMP Font Asset. fontIndex: 0. Uses this resource's own material and fallback fonts.");
            Field("fontSize", "Font Size");
            fontList.DoLayoutList();
            EditorGUILayout.Space(4);
            Field("useSprites", "Use Sprite Fonts", "Use native TMP Sprite Assets for inline icons and emoji, through Unicode or <sprite> tags.");
            if (serializedObject.FindProperty("useSprites").boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Field("spriteAsset", "Default Sprite Font", "TMP Sprite Asset. Empty uses the TMP Settings default sprite asset. Its own fallback sprite assets are searched automatically.");
                    Field("additionalSpriteAssets", "Named Sprite Fonts", "Optional native TMP Sprite Assets referenced by <sprite=\"AssetName\"> tags. No text-to-image mapping is required.");
                }
            }
            resourceChanges |= EditorGUI.EndChangeCheck();
        }

        private void DrawSpace()
        {
            Field("spaceMode", "Space Mode", "Screen Snapshot binds position on emission; Screen Follow tracks the target at fixed screen size; World Follow uses its complete transform and perspective. Pass a target through EmitText.");
            var space = serializedObject.FindProperty("spaceMode");
            if (space.enumValueIndex == (int)BrgDamageTextRenderer.SpaceMode.WorldFollow)
                Field("worldUnitsPerLayoutUnit", "World Units Per Layout Unit", "Converts font size and animation distances from layout units into world units.");
            Field("sortingMode", "Occlusion", "Every mode sorts whole labels back-to-front. Always In Front overlays the scene; Opaque Occlusion respects opaque depth; Scene Transparent also participates in scene transparency sorting.");
        }

        private void DrawTextLayout()
        {
            resourceChanges |= DrawLayoutControls(serializedObject, ref lastWrapWidth);
        }

        internal static bool DrawLayoutControls(SerializedObject settings, ref float lastWrapWidth)
        {
            DrawAlignment(settings);
            EditorGUILayout.Space(4);
            var width = settings.FindProperty("wrapWidth");
            bool wrapping = width.floatValue > 0;
            bool selected = EditorGUILayout.Toggle("Automatic Wrapping", wrapping);
            if (selected != wrapping)
            {
                if (wrapping) { lastWrapWidth = width.floatValue; width.floatValue = 0; }
                else width.floatValue = Mathf.Max(1, lastWrapWidth);
            }
            if (selected)
            {
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.PropertyField(width, new GUIContent("Wrap Width", "Maximum line width in layout units. An enabled fixed text area can reduce this to its width."));
                width.floatValue = Mathf.Max(1, width.floatValue);
                lastWrapWidth = width.floatValue;
            }
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(settings.FindProperty("richText"), new GUIContent("Rich Text Tags"));
            EditorGUILayout.PropertyField(settings.FindProperty("enableKerning"), new GUIContent("Font Kerning"));
            return EditorGUI.EndChangeCheck();
        }

        private static void DrawAlignment(SerializedObject settings)
        {
            var anchor = settings.FindProperty("alignment");
            int horizontal = anchor.enumValueIndex % 3, vertical = anchor.enumValueIndex / 3;
            EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel("Horizontal");
            horizontal = GUILayout.Toolbar(horizontal, new[] { "Left", "Center", "Right" });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel("Vertical");
            vertical = GUILayout.Toolbar(vertical, new[] { "Top", "Middle", "Bottom" });
            EditorGUILayout.EndHorizontal();
            anchor.enumValueIndex = vertical * 3 + horizontal;
            var area = settings.FindProperty("useTextArea");
            EditorGUILayout.PropertyField(area, new GUIContent("Use Fixed Text Area", "Off: align against the emission point. On: align within an area centered on that point."));
            if (area.boolValue)
                using (new EditorGUI.IndentLevelScope())
                {
                    var size = settings.FindProperty("textAreaSize");
                    EditorGUILayout.PropertyField(size, new GUIContent("Text Area Size", "Width / height in layout units, scaled with the text. This area does not clip overflowing text."));
                    size.vector2Value = Vector2.Max(Vector2.zero, size.vector2Value);
                }
        }

        private void DrawScreenScaling()
        {
            Field("scalingCanvas", "Use Existing UI Canvas", "Optional. Uses the root Canvas's actual scale. Leave empty to configure the same modes as Canvas Scaler below.");
            var canvas = serializedObject.FindProperty("scalingCanvas").objectReferenceValue as Canvas;
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
            {
                EditorGUILayout.LabelField("Canvas Scale Factor", canvas.rootCanvas.scaleFactor.ToString("0.###"));
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
                        Field("matchWidthOrHeight", "Match", "0 = Width, 1 = Height. Uses Canvas Scaler's logarithmic interpolation.");
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
            if (ready)
            { EditorGUILayout.LabelField(new GUIContent("Rendering", message), new GUIContent("Ready")); return; }
            EditorGUILayout.HelpBox(message, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Install BRG Rendering")) BrgRenderingSetup.Install();
                using (new EditorGUI.DisabledScope(data == null))
                    if (GUILayout.Button("Select Renderer Data")) { Selection.activeObject = data; EditorGUIUtility.PingObject(data); }
            }
        }

        private static void DrawStatistics(BrgDamageTextRenderer renderer)
        {
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
