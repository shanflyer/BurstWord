using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BurstWord.BRG;
using UnityEditor;
using UnityEngine;

namespace BurstWord.Baseline.Editor
{
    /// <summary>Creates a user-owned shader in Assets; never edits the installed package.</summary>
    public static class BrgShaderEffectCreator
    {
        private static readonly Dictionary<Shader, (int dirty, int srp, string text, MessageType type)> reports =
            new Dictionary<Shader, (int, int, string, MessageType)>();
        private static readonly MethodInfo srpCompatibility = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Shader), typeof(int) }, null);
        private static readonly MethodInfo srpReason = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Shader), typeof(int), typeof(int) }, null);

        [InitializeOnLoadMethod]
        private static void WatchImports() => EditorApplication.projectChanged += reports.Clear;

        [MenuItem("Assets/Create/BurstWord/Shader Effect", priority = 215)]
        public static void Create() => CreateAndAssign(null);

        public static void CreateAndAssign(BrgDamageTextRenderer renderer)
        {
            string path = EditorUtility.SaveFilePanelInProject("Create BurstWord Shader Effect", "NewBurstWordEffect", "shader", "Choose where to save your custom effect shader.");
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string name = "BurstWord/Custom/" + Path.GetFileNameWithoutExtension(path);
            var shader = CreateAsset(path, name);
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "Add BurstWord shader effect");
                var list = new List<Shader>(renderer.shaderEffects ?? Array.Empty<Shader>()) { shader };
                renderer.shaderEffects = list.ToArray();
                EditorUtility.SetDirty(renderer); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            Selection.activeObject = shader; EditorGUIUtility.PingObject(shader);
        }

        public static Shader CreateAsset(string path, string shaderName)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Custom effects must be .shader files inside Assets.", nameof(path));
            if (File.Exists(path)) throw new IOException("Shader file already exists: " + path);
            File.WriteAllText(path, Template(shaderName), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }

        // ShaderLab declarations stay in the shader; runtime binding, instances,
        // animation, SDF shading, ordering and hook plumbing live in package HLSL.
        public static string Template(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName) || shaderName.IndexOfAny(new[] { '"', '\n', '\r', '\\' }) >= 0)
                throw new ArgumentException("Invalid shader name.", nameof(shaderName));
            var text = new StringBuilder();
            text.AppendLine("Shader \"" + shaderName + "\"\n{\n    Properties\n    {");
            void Scalar(string name, string value) => text.AppendLine("        [HideInInspector] " + name + "(\"" + name + "\", Float) = " + value);
            void Vector(string name) => text.AppendLine("        [HideInInspector] " + name + "(\"" + name + "\", Vector) = (0,0,0,0)");
            Scalar("_BurstZTest", "8"); Scalar("_BurstSortingMode", "0"); Scalar("_BurstPlainSdfFastPath", "1"); Scalar("_BurstLabelCapacity", "1"); Scalar("_BurstTime", "0");
            Vector("_BurstScreen"); Vector("_BurstAnimationInfo");
            // Instance-array bindings belong to HLSL, not ShaderLab material Properties.
            // Listing them as material properties makes a combined DOTS/classic shader
            // fail BRG's SRP Batcher check because classic arrays use an instancing CBuffer.
            for (int i = 0; i < 10; i++) Vector("_BurstResource" + i);
            for (int i = 0; i < 16; i++) text.AppendLine("        [HideInInspector] _BurstAtlas" + i + "(\"Atlas " + i + "\", 2D) = \"white\" {}");
            text.AppendLine("        [HideInInspector] _MainTex(\"Atlas\", 2D) = \"white\" {}\n    }");
            text.AppendLine(@"    SubShader
    {
        Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Transparent"" ""Queue""=""Overlay"" ""BurstWordContract""=""1"" ""BurstWordBRG""=""True"" ""BurstWordInstancing""=""True"" }
        Cull Off
        ZWrite Off
        ZTest [_BurstZTest]
        Blend SrcAlpha OneMinusSrcAlpha
        HLSLINCLUDE
        #include ""Packages/com.shanflyer.burstword/Runtime/BRG/BurstWordShaderEffects.hlsl""

        // Only edit these two functions for ordinary custom effects.
        // parameters = EmitText(..., effectParameters: new Vector4(x, y, z, w)).
        // glyphUV: 0..1 within each glyph; normalizedAge: 0..1 over its lifetime.
        void BurstWordModifyVertex(inout BurstWordEffectVertex vertex)
        {
            // Example: move right by parameters.x screen pixels.
            // vertex.positionCS.x += vertex.parameters.x * 2 / _BurstScreen.x * vertex.positionCS.w;
        }

        void BurstWordModifyFragment(inout half4 color, BurstWordEffectFragment fragment)
        {
            // Example: a vertical color gradient.
            // color.rgb *= lerp(half3(1, 0.3, 0.1), half3(1, 1, 0.4), fragment.glyphUV.y);
            // Preserve color.a to retain glyph shape and built-in fade.
        }
        ENDHLSL");
            void Pass(string name, string lightMode)
            {
                text.AppendLine("        Pass\n        {\n            Name \"" + name + "\"\n            Tags { \"LightMode\"=\"" + lightMode + "\" }\n            HLSLPROGRAM");
                text.AppendLine(@"            #pragma target 3.5
            #pragma target 4.5 DOTS_INSTANCING_ON
            #pragma instancing_options forcemaxcount:64
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex BurstWordEffectVert
            #pragma fragment BurstWordEffectFrag
            ENDHLSL
        }");
            }
            Pass("Scene", "SRPDefaultUnlit"); Pass("OrderedOverlay", "BurstWordOverlay");
            text.AppendLine("    }\n    Fallback Off\n}");
            return text.ToString().Replace("\r\n", "\n");
        }

        internal static (string text, MessageType type) Report(Shader shader)
        {
            if (shader == null) return ("Built-in effect", MessageType.None);
            int dirty = EditorUtility.GetDirtyCount(shader);
            if (reports.TryGetValue(shader, out var cached) && cached.dirty == dirty) return (cached.text, cached.type);
            int code = 0;
            bool valid = BrgShaderContract.Validate(shader, out bool brg, out bool instancing, out string message);
            MessageType type = valid ? brg && instancing ? MessageType.Info : MessageType.Warning : MessageType.Error;
            if (valid && brg && srpCompatibility != null)
            {
                // Unity exposes this editor diagnostic through ShaderUtil on supported versions.
                // A dual-backend shader must keep UnityPerMaterial identical in all variants.
                // Like Unity's Shader Inspector, compile a pass before asking for the result.
                var check = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
                bool asynchronous = ShaderUtil.allowAsyncCompilation;
                try
                {
                    ShaderUtil.allowAsyncCompilation = false;
                    check.EnableKeyword("DOTS_INSTANCING_ON"); check.SetPass(0); check.SetPass(1);
                    code = (int)srpCompatibility.Invoke(null, new object[] { shader, 0 });
                }
                finally { ShaderUtil.allowAsyncCompilation = asynchronous; UnityEngine.Object.DestroyImmediate(check); }
                if (code != 0)
                {
                    string detail = srpReason != null ? (string)srpReason.Invoke(null, new object[] { shader, 0, code }) : "Unity diagnostic " + code;
                    if (detail.StartsWith("Not initialized", StringComparison.OrdinalIgnoreCase))
                        message += " SRP compatibility pending shader compilation.";
                    else { message = "BRG unsupported: shader is not SRP Batcher compatible. " + detail; type = MessageType.Error; }
                }
            }
            foreach (var error in ShaderUtil.GetShaderMessages(shader))
                if (error.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                { message = "Shader compilation error: " + error.message; type = MessageType.Error; break; }
            if (valid && type != MessageType.Error && !shader.isSupported)
            { message += " Unsupported on the current graphics device."; type = MessageType.Error; }
            reports[shader] = (dirty, code, message, type); return (message, type);
        }
    }
}
