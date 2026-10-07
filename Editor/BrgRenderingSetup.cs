using BurstWord.BRG;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BurstWord.Baseline.Editor
{
    [InitializeOnLoad]
    public static class BrgRenderingSetup
    {
        static BrgRenderingSetup()
        {
            EditorApplication.delayCall += EnsureConfiguredPipelines;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) EnsureConfiguredPipelines();
            };
        }

        // AssetDatabase must be ready, and registrations must reach the loaded asset,
        // not just its YAML file (which a running editor can overwrite when saving).
        private static void EnsureConfiguredPipelines()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += EnsureConfiguredPipelines; return; }
            int count = KeepBrgShaderVariants() ? 1 : 0;
            count += InstallPipeline(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset);
            if (QualitySettings.renderPipeline != GraphicsSettings.defaultRenderPipeline)
                count += InstallPipeline(QualitySettings.renderPipeline as UniversalRenderPipelineAsset);
            SaveChanges(count);
        }

        [MenuItem("Tools/BurstWord/Install BRG Rendering")]
        public static void Install()
        {
            int count = KeepBrgShaderVariants() ? 1 : 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                count += InstallPipeline(pipeline);
            }
            SaveChanges(count);
        }

        /// <summary>Checks the renderer selected by this camera, including quality overrides.</summary>
        public static bool CheckCamera(Camera camera, out ScriptableRendererData rendererData, out string message)
        {
            rendererData = null;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            { message = "BurstWord requires an active URP pipeline. Assign your URP asset in Graphics / Quality settings."; return false; }
            var serialized = new SerializedObject(pipeline);
            var renderers = serialized.FindProperty("m_RendererDataList");
            var defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            int index = defaultIndex != null ? defaultIndex.intValue : 0;
            var additional = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (additional != null)
            {
                var cameraIndex = new SerializedObject(additional).FindProperty("m_RendererIndex");
                if (cameraIndex != null && cameraIndex.intValue >= 0) index = cameraIndex.intValue;
            }
            if (renderers == null || index < 0 || index >= renderers.arraySize ||
                (rendererData = renderers.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData) == null)
            { message = "The selected camera has no valid URP Renderer Data. Check the URP asset's Renderer List and the camera's Renderer selection."; return false; }
            foreach (var feature in rendererData.rendererFeatures)
                if (feature is BrgTextRendererFeature && feature.isActive)
                { message = "BurstWord ordered text is enabled on " + rendererData.name + "."; return true; }
            message = "Enable BurstWord ordered text in " + rendererData.name + " → Renderer Features, or click Install BRG Rendering.";
            return false;
        }

        private static bool KeepBrgShaderVariants()
        {
            var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if(settings.Length==0)return false;
            var serialized=new SerializedObject(settings[0]);var stripping=serialized.FindProperty("m_BrgStripping");
            bool changed = false;
            foreach (string shaderName in new[] { "BurstWord/BRG TMP Glyph", "BurstWord/Instanced TMP Glyph" })
            {
                var glyphShader = Shader.Find(shaderName);
                var included = serialized.FindProperty("m_AlwaysIncludedShaders");
                if (glyphShader != null && included != null)
                {
                    bool found = false;
                    for (int i = 0; i < included.arraySize; i++)
                        if (included.GetArrayElementAtIndex(i).objectReferenceValue == glyphShader) { found = true; break; }
                    if (!found)
                    {
                        int index = included.arraySize;
                        included.arraySize++;
                        included.GetArrayElementAtIndex(index).objectReferenceValue = glyphShader;
                        changed = true;
                    }
                }
            }
            // Runtime-created instancing materials have no scene Renderer for Unity's
            // StripUnused scan. Retain the regular INSTANCING_ON variants as well.
            var instancing = serialized.FindProperty("m_InstancingStripping");
            if (instancing != null)
            {
                int keep = System.Array.FindIndex(instancing.enumNames, name => name.Replace("_", "").Replace(" ", "").Equals("KeepAll", System.StringComparison.OrdinalIgnoreCase));
                if (keep >= 0 && instancing.enumValueIndex != keep) { instancing.enumValueIndex = keep; changed = true; }
            }
            if(stripping==null) { if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();return changed; }
            int keepAll=System.Array.FindIndex(stripping.enumNames,name=>name.Replace("_","").Replace(" ","").Equals("KeepAll",System.StringComparison.OrdinalIgnoreCase));
            if(keepAll<0 || stripping.enumValueIndex==keepAll) { if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();return changed; }
            stripping.enumValueIndex=keepAll;serialized.ApplyModifiedPropertiesWithoutUndo();return true;
        }

        private static int InstallPipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return 0;
            int count = 0;
            var array = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < array.arraySize; i++)
            {
                var data = array.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                BrgTextRendererFeature existing = null;
                int index = -1;
                for (int j = 0; j < data.rendererFeatures.Count; j++)
                    if (data.rendererFeatures[j] is BrgTextRendererFeature feature)
                    { existing = feature; index = j; break; }
                string path = AssetDatabase.GetAssetPath(data);
                bool changed = false;
                // Keep the feature inside its Renderer Data so URP's local-ID
                // recovery map can restore it after an import or script reload.
                if (existing == null || AssetDatabase.GetAssetPath(existing) != path)
                {
                    BrgTextRendererFeature embedded = null;
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is BrgTextRendererFeature feature) { embedded = feature; break; }
                    existing = embedded ?? ScriptableObject.CreateInstance<BrgTextRendererFeature>();
                    existing.name = "BurstWord ordered text";
                    if (embedded == null) AssetDatabase.AddObjectToAsset(existing, data);
                    if (index < 0) data.rendererFeatures.Add(existing);
                    else data.rendererFeatures[index] = existing;
                    changed = true;
                }
                if (!existing.isActive)
                { existing.SetActive(true); EditorUtility.SetDirty(existing); changed = true; }
                var serialized = new SerializedObject(data);
                var map = serialized.FindProperty("m_RendererFeatureMap");
                if (map.arraySize != data.rendererFeatures.Count)
                { map.arraySize = data.rendererFeatures.Count; changed = true; }
                for (int j = 0; j < data.rendererFeatures.Count; j++)
                {
                    long id = 0;
                    if (data.rendererFeatures[j] != null)
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[j], out string _, out id);
                    if (map.GetArrayElementAtIndex(j).longValue == id) continue;
                    map.GetArrayElementAtIndex(j).longValue = id; changed = true;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (!changed) continue;
                existing.Create(); data.SetDirty(); EditorUtility.SetDirty(data); count++;
            }
            return count;
        }

        private static void SaveChanges(int count)
        {
            if (count == 0) return;
            AssetDatabase.SaveAssets();
        }
    }
}
