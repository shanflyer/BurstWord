using System;
using System.Collections.Generic;
using UnityEngine;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Tooltip("Effect materials by index. Index 0 is the default; an empty list / slot uses built-in shading. -1 explicitly selects built-in rendering.")]
        public Material[] effectMaterials = Array.Empty<Material>();
        // Preserve existing serialized shader selections until converted in the Inspector.
        [HideInInspector] public Shader[] shaderEffects = Array.Empty<Shader>();
        public int EffectCount => effectMaterials != null && effectMaterials.Length > 0 ? effectMaterials.Length : shaderEffects?.Length ?? 0;

        private readonly struct EffectSelection
        {
            public readonly Shader Shader;
            public readonly Material Material;
            public EffectSelection(Shader shader, Material material) { Shader = shader; Material = material; }
        }
        private readonly Dictionary<Shader, (bool brg, bool instancing, string error)> effectContracts =
            new Dictionary<Shader, (bool, bool, string)>();
        private readonly Dictionary<(int, Shader, Material), int> effectBatches = new Dictionary<(int, Shader, Material), int>();
        private readonly Dictionary<(Shader, Material), Material> instancingEffects = new Dictionary<(Shader, Material), Material>();
        private Material InstancingEffectMaterial(GlyphPage page)
        {
            if (page.Material.shader == glyphShader && page.EffectMaterial == null) return instancingMaterial;
            var key = (page.Material.shader, page.EffectMaterial);
            if (!instancingEffects.TryGetValue(key, out var material))
            { material = page.Material; instancingEffects.Add(key, material); }
            return material;
        }
        private Vector4[] effectLabels;
        private UnityEngine.GraphicsBuffer effectLabelBuffer;
        private int effectFirst = int.MaxValue, effectLast = -1;

        private void ValidateEffectIndex(int effectIndex)
        {
            if (effectIndex == -1 || (effectIndex == 0 && EffectCount == 0)) return;
            if (effectIndex < 0 || effectIndex >= EffectCount)
                throw new ArgumentOutOfRangeException(nameof(effectIndex), effectIndex, "Effect index must be -1 (built-in) or match the Shader Effects list.");
        }

        /// <summary>0 selects the first/default material. Empty lists/slots and -1 use built-in shading.</summary>
        public Material GetEffectMaterial(int effectIndex)
        {
            ValidateEffectIndex(effectIndex);
            return effectIndex < 0 || effectMaterials == null || effectMaterials.Length == 0 ? null : effectMaterials[effectIndex];
        }

        public Shader GetShaderEffect(int effectIndex)
        {
            var material = GetEffectMaterial(effectIndex);
            if (effectMaterials != null && effectMaterials.Length > 0) return material != null ? material.shader : null;
            return effectIndex < 0 || EffectCount == 0 ? null : shaderEffects[effectIndex];
        }

        private EffectSelection ResolveEffect(int index, Vector4 parameters, Material selected = null)
        {
            ValidateEffectParameters(parameters);
            var material = selected != null ? selected : GetEffectMaterial(index);
            var shader = material != null ? material.shader : GetShaderEffect(index);
            if (shader == null) return new EffectSelection(glyphShader, null);
            if (!effectContracts.TryGetValue(shader, out var contract))
            {
                bool valid = BrgShaderContract.Validate(shader, out bool brg, out bool instancing, out string reason);
                contract = (brg, instancing, valid ? null : reason); effectContracts.Add(shader, contract);
            }
            if (contract.error != null) throw new ArgumentException("Shader effect '" + shader.name + "': " + contract.error, nameof(index));
            if (UsingBrg ? !contract.brg : !contract.instancing)
                throw new NotSupportedException("Shader effect '" + shader.name + "' does not support the active " + ActiveBackend + " backend.");
            if (!shader.isSupported) throw new NotSupportedException("Shader effect '" + shader.name + "' is unsupported on this graphics device or has shader compilation errors.");
            return new EffectSelection(shader, material);
        }

        private static void ValidateEffectParameters(Vector4 parameters)
        {
            for (int i = 0; i < 4; i++)
                if (float.IsNaN(parameters[i]) || float.IsInfinity(parameters[i]))
                    throw new ArgumentOutOfRangeException(nameof(parameters), "Effect parameters must be finite.");
        }

        // Map layout resources at commit time. All shaders reuse the same numeric, wrapping,
        // shaping and preparation caches; effect selection does not multiply layout work.
        private int EffectBatch(int source, EffectSelection effect)
        {
            if (effect.Shader == glyphShader && effect.Material == null) return source;
            var key = (source, effect.Shader, effect.Material);
            if (effectBatches.TryGetValue(key, out int id)) return id;
            var original = atlasBatches[source];
            id = atlasBatches.Count;
            atlasBatches.Add(new AtlasBatch(this, effect.Shader, original.Font, original.Texture, original.SourceMaterial, original.Mode, effect.Material));
            effectBatches.Add(key, id);
            return id;
        }

        /// <summary>Refresh cached draw materials after changing application-owned material properties.
        /// Does not rebuild layouts or modify the supplied material. Change shaders only after reinitializing.</summary>
        public int RefreshEffectMaterial(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            ResolveEffect(0, default, material);
            foreach (var page in glyphPages)
                if (page.EffectMaterial == material && page.Material.shader != material.shader)
                    throw new InvalidOperationException("An active effect material's shader changed. Disable and re-enable the manager before emitting with the new shader.");
            int count = 0;
            foreach (var page in glyphPages)
                if (page.EffectMaterial == material) { page.RefreshEffect(this); count++; }
            return count;
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
