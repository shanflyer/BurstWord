using System;
using System.Collections.Generic;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Tooltip("User shaders by index. Index 0 is the default; an empty list / slot uses the built-in shader. -1 explicitly selects built-in rendering.")]
        public Shader[] shaderEffects = Array.Empty<Shader>();
        private readonly Dictionary<Shader, (bool brg, bool instancing, string error)> effectContracts =
            new Dictionary<Shader, (bool, bool, string)>();
        private readonly Dictionary<(int, Shader), int> effectBatches = new Dictionary<(int, Shader), int>();
        private readonly Dictionary<Shader, Material> instancingEffects = new Dictionary<Shader, Material>();
        private Material InstancingEffectMaterial(GlyphPage page)
        {
            if (page.Material.shader == glyphShader) return instancingMaterial;
            if (!instancingEffects.TryGetValue(page.Material.shader, out var material))
            { material = page.Material; instancingEffects.Add(material.shader, material); }
            return material;
        }
        private Vector4[] effectLabels;
        private UnityEngine.GraphicsBuffer effectLabelBuffer;
        private int effectFirst = int.MaxValue, effectLast = -1;

        /// <summary>0 selects the first/default shader. Empty lists/slots and -1 use built-in rendering.</summary>
        public Shader GetShaderEffect(int effectIndex)
        {
            if (effectIndex == -1 || (effectIndex == 0 && (shaderEffects == null || shaderEffects.Length == 0))) return null;
            if (effectIndex < 0 || shaderEffects == null || effectIndex >= shaderEffects.Length)
                throw new ArgumentOutOfRangeException(nameof(effectIndex), effectIndex, "Effect index must be -1 (built-in) or match the Shader Effects list.");
            return shaderEffects[effectIndex];
        }

        private Shader ResolveEffect(int index, Vector4 parameters)
        {
            ValidateEffectParameters(parameters);
            var shader = GetShaderEffect(index);
            if (shader == null) return glyphShader;
            if (!effectContracts.TryGetValue(shader, out var contract))
            {
                bool valid = BrgShaderContract.Validate(shader, out bool brg, out bool instancing, out string reason);
                contract = (brg, instancing, valid ? null : reason); effectContracts.Add(shader, contract);
            }
            if (contract.error != null) throw new ArgumentException("Shader effect '" + shader.name + "': " + contract.error, nameof(index));
            if (UsingBrg ? !contract.brg : !contract.instancing)
                throw new NotSupportedException("Shader effect '" + shader.name + "' does not support the active " + ActiveBackend + " backend.");
            if (!shader.isSupported) throw new NotSupportedException("Shader effect '" + shader.name + "' is unsupported on this graphics device or has shader compilation errors.");
            return shader;
        }

        private static void ValidateEffectParameters(Vector4 parameters)
        {
            for (int i = 0; i < 4; i++)
                if (float.IsNaN(parameters[i]) || float.IsInfinity(parameters[i]))
                    throw new ArgumentOutOfRangeException(nameof(parameters), "Effect parameters must be finite.");
        }

        // Map layout resources at commit time. All shaders reuse the same numeric, wrapping,
        // shaping and preparation caches; effect selection does not multiply layout work.
        private int EffectBatch(int source, Shader shader)
        {
            if (shader == glyphShader) return source;
            var key = (source, shader);
            if (effectBatches.TryGetValue(key, out int id)) return id;
            var original = atlasBatches[source];
            id = atlasBatches.Count;
            atlasBatches.Add(new AtlasBatch(this, shader, original.Font, original.Texture, original.SourceMaterial, original.Mode));
            effectBatches.Add(key, id);
            return id;
        }

        private void SetEffectParameters(int label, Vector4 parameters, bool custom)
        {
            if (effectLabels == null)
            {
                if (!custom) return;
                effectLabels = new Vector4[Capacity];
                if (UsingBrg)
                {
                    effectLabelBuffer = new UnityEngine.GraphicsBuffer(UnityEngine.GraphicsBuffer.Target.Structured, Capacity, 16);
                    effectLabelBuffer.SetData(effectLabels);
                    foreach (var page in glyphPages) BindEffect(page.Material);
                }
            }
            if (effectLabels[label].Equals(parameters)) return;
            effectLabels[label] = parameters;
            effectFirst = Math.Min(effectFirst, label); effectLast = Math.Max(effectLast, label);
        }

        /// <summary>Update four custom shader parameters without rebuilding the label or its layout.</summary>
        public bool TryUpdateEffectParameters(TextHandle handle, Vector4 parameters)
        {
            ValidateEffectParameters(parameters);
            if (!IsAlive(handle) || !labels[handle.index].customEffect) return false;
            SetEffectParameters(handle.index, parameters, true); return true;
        }

        private void BindEffect(Material material)
        { if (effectLabelBuffer != null) material.SetBuffer("_BurstEffectLabels", effectLabelBuffer); }

        private void UpdateEffects()
        {
            if (effectFirst > effectLast) return;
            if (effectLabelBuffer != null)
            {
                int count = effectLast - effectFirst + 1;
                effectLabelBuffer.SetData(effectLabels, effectFirst, effectFirst, count);
                UploadedBytesLastFrame += count * 16; UploadCallsLastFrame++;
            }
            effectFirst = int.MaxValue; effectLast = -1;
        }

        private void DisposeEffects()
        {
            effectLabelBuffer?.Dispose(); effectLabelBuffer = null; effectLabels = null;
            effectFirst = int.MaxValue; effectLast = -1;
            effectBatches.Clear(); effectContracts.Clear(); instancingEffects.Clear();
        }
    }
}
