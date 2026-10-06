using System.Collections.Generic;
using System.IO;
using BurstWord.BRG;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BurstWord.Adapters.HarfBuzz.Editor
{
    [InitializeOnLoad]
    public sealed class BrgTypographySetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        static BrgTypographySetup()
        {
            EditorApplication.playModeStateChanged += state =>
            { if (state == PlayModeStateChange.ExitingEditMode) PrepareFontSources(); };
        }
        public void OnPreprocessBuild(BuildReport report) => PrepareFontSources();
        [MenuItem("Tools/BurstWord/HarfBuzz/Prepare Font Sources")]
        public static void PrepareFontSources()
        {
            Directory.CreateDirectory("Assets/BurstWord/Resources/BurstWordOpenType");
            var entries = new List<BrgFontSources.Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(asset);
                var editorRef = serialized.FindProperty("m_SourceFontFile_EditorRef");
                var source = asset.sourceFontFile != null ? asset.sourceFontFile : editorRef?.objectReferenceValue as Font;
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : AssetDatabase.GUIDToAssetPath(serialized.FindProperty("m_SourceFontFileGUID")?.stringValue);
                sourcePath = ResolveFontPath(sourcePath);
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                {
                    Debug.LogWarning("Cannot prepare OpenType data for " + asset.name + ": original font file is unavailable.", asset);
                    continue;
                }
                string bytesPath = "Assets/BurstWord/Resources/BurstWordOpenType/" + guid + ".bytes";
                var data = File.ReadAllBytes(sourcePath);
                if (!File.Exists(bytesPath) || !SameBytes(File.ReadAllBytes(bytesPath), data)) File.WriteAllBytes(bytesPath, data);
                AssetDatabase.ImportAsset(bytesPath);
                entries.Add(new BrgFontSources.Entry { font = asset, openTypeData = AssetDatabase.LoadAssetAtPath<TextAsset>(bytesPath) });
            }
            const string path = "Assets/BurstWord/Resources/BurstWordFontSources.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BrgFontSources>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BrgFontSources>(); AssetDatabase.CreateAsset(catalog, path); }
            catalog.fonts = entries.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }

        private static string ResolveFontPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Packages/", System.StringComparison.Ordinal)) return path;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null) return path;
            return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
        }
        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
