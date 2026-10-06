#if UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace BurstWord.Baseline.Editor
{
            // Compile the same OpenType shapers for device and simulator with Xcode's target
    // architecture. No external download, framework variant or user setup is needed.
    internal static class BrgAppleNativeBuild
    {
        [PostProcessBuild(100)]
        private static void IncludeHarfBuzz(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS && target != BuildTarget.tvOS && target.ToString() != "VisionOS") return;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.shanflyer.burstword/package.json");
            string source = Path.Combine(package.resolvedPath, "Runtime/Plugins/HarfBuzzSource~");
            string relative = "Libraries/BurstWordHarfBuzz";
            string destination = Path.Combine(output, relative);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string local = file.Substring(source.Length + 1);
                string path = Path.Combine(destination, local);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(file, path, true);
            }
            const string wrapper = "BurstWordHarfBuzz.cpp";
            File.WriteAllText(Path.Combine(destination, wrapper),
                "#define HB_NO_MMAP\n#define HB_NO_PRAGMA_GCC_DIAGNOSTIC\n#define HB_EXTERN __attribute__((visibility(\"default\")))\n#include \"harfbuzz.cc\"\n");
            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject(); project.ReadFromFile(projectPath);
            string framework = project.GetUnityFrameworkTargetGuid();
            string pathInProject = relative + "/" + wrapper;
            string guid = project.FindFileGuidByProjectPath(pathInProject);
            if (string.IsNullOrEmpty(guid)) guid = project.AddFile(pathInProject, pathInProject, PBXSourceTree.Source);
            project.AddFileToBuildWithFlags(framework, guid, "-std=c++11");
            project.WriteToFile(projectPath);
        }
    }
}
#endif
