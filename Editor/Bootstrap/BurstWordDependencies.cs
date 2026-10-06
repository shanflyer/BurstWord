using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace BurstWord.Editor.Bootstrap
{
    // This assembly deliberately has no TMP/URP references. Legacy TMP is installed only
    // where it is needed, before the dependent assemblies become eligible to compile.
    [InitializeOnLoad]
    internal static class BurstWordDependencies
    {
        private static AddRequest request;
        static BurstWordDependencies() { EditorApplication.delayCall += EnsureLegacyTmp; }
        private static void EnsureLegacyTmp()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += EnsureLegacyTmp; return; }
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name == "com.unity.textmeshpro") return;
                if (package.name == "com.unity.ugui" && Version.TryParse(package.version, out var version) && version.Major >= 2) return;
            }
#if !UNITY_2023_2_OR_NEWER
            const string key = "BurstWord.LegacyTmpInstallAttempt";
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            request = Client.Add("com.unity.textmeshpro@3.0.7");
            EditorApplication.update += Poll;
#endif
        }
        private static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Failure) Debug.LogError("BurstWord could not install Unity's legacy TMP package: " + request.Error.message);
            request = null;
        }
    }
}
