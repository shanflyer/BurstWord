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

        private static bool KeepBrgShaderVariants()
        {
            var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if(settings.Length==0)return false;
            var serialized=new SerializedObject(settings[0]);var stripping=serialized.FindProperty("m_BrgStripping");
            bool changed = false;
            var glyphShader = Shader.Find("BurstWord/BRG TMP Glyph");
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
