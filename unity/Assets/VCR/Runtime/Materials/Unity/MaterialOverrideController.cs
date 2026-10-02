using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Core;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// Non-destructive runtime material-slot override controller.
    ///
    /// Source materials are never edited. Each active override owns a runtime
    /// material clone and restores the original shared-material reference on
    /// failure/removal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaterialOverrideController :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        private sealed class SlotRecord
        {
            public string Id;
            public string RendererPath;
            public Renderer Renderer;
            public int SlotIndex;
            public Material SourceMaterial;
            public Material RuntimeMaterial;
            public MaterialOverrideStatus Status;
        }

        [SerializeField] private bool discoverOnAwake = true;

        private readonly Dictionary<string, SlotRecord> _slots =
            new(StringComparer.Ordinal);
        private readonly List<MaterialSlotDescriptor> _descriptors = new();

        private int _errorCount;

        public int SlotCount => _slots.Count;
        public int ErrorCount => _errorCount;

        private void Awake()
        {
            if (discoverOnAwake)
            {
                RefreshSlots();
            }
        }

        [ContextMenu("Refresh Material Slots")]
        public void RefreshSlots()
        {
            ClearAllOverrides();
            _slots.Clear();
            _descriptors.Clear();

            var renderers =
                GetComponentsInChildren<Renderer>(
                    includeInactive: true);

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var rendererPath =
                    BuildStablePath(
                        transform,
                        renderer.transform);
                var materials =
                    renderer.sharedMaterials;

                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    var id =
                        rendererPath + "#" + i;

                    var record = new SlotRecord
                    {
                        Id = id,
                        RendererPath = rendererPath,
                        Renderer = renderer,
                        SlotIndex = i,
                        SourceMaterial = source,
                        Status = new MaterialOverrideStatus(
                            id,
                            MaterialOverrideHealth.Source,
                            source?.shader?.name,
                            null)
                    };

                    _slots[id] = record;
                    _descriptors.Add(
                        new MaterialSlotDescriptor(
                            id,
                            rendererPath,
                            i,
                            source != null
                                ? source.name
                                : string.Empty,
                            source?.shader?.name ??
                                string.Empty));
                }
            }
        }

        public MaterialSlotDescriptor[] GetSlots()
        {
            return _descriptors.ToArray();
        }

        public bool TryGetStatus(
            string slotId,
            out MaterialOverrideStatus status)
        {
            if (slotId != null &&
                _slots.TryGetValue(
                    slotId,
                    out var record))
            {
                status = record.Status;
                return true;
            }

            status = default;
            return false;
        }

        public MaterialOverrideStatus[] GetStatuses()
        {
            var statuses =
                new MaterialOverrideStatus[
                    _slots.Count];

            var index = 0;
            foreach (var record in _slots.Values)
            {
                statuses[index++] =
                    record.Status;
            }

            return statuses;
        }

        public MaterialCompatibilityReport EvaluatePreset(
            string slotId,
            MaterialOverridePreset preset)
        {
            var issues =
                new List<MaterialCompatibilityIssue>();

            if (!TryGetRecord(
                    slotId,
                    out var record,
                    out var slotError))
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "slot_not_found",
                        null,
                        slotError));

                return new MaterialCompatibilityReport(
                    slotId,
                    preset?.PresetId,
                    preset?.ShaderId,
                    issues.ToArray(),
                    Application.platform.ToString(),
                    SystemInfo.graphicsDeviceType.ToString());
            }

            if (preset == null)
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "preset_required",
                        null,
                        "Material preset is required."));

                return new MaterialCompatibilityReport(
                    slotId,
                    null,
                    null,
                    issues.ToArray(),
                    Application.platform.ToString(),
                    SystemInfo.graphicsDeviceType.ToString());
            }

            if (string.IsNullOrWhiteSpace(
                    preset.PresetId))
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "preset_id_required",
                        null,
                        "Material preset id is required."));
            }

            var shader =
                ResolvePresetShader(
                    record,
                    preset,
                    out var shaderError);

            if (shader == null)
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "shader_unavailable",
                        null,
                        shaderError));

                return new MaterialCompatibilityReport(
                    slotId,
                    preset.PresetId,
                    preset.ShaderId,
                    issues.ToArray(),
                    Application.platform.ToString(),
                    SystemInfo.graphicsDeviceType.ToString());
            }

            if (!shader.isSupported)
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "shader_unsupported",
                        null,
                        $"Shader '{shader.name}' is not supported on the current graphics device."));
            }

            var parameters =
                preset.Parameters ??
                Array.Empty<MaterialParameterOverride>();

            foreach (var parameter in parameters)
            {
                ValidateParameterCompatibility(
                    shader,
                    parameter,
                    issues);
            }

            return new MaterialCompatibilityReport(
                slotId,
                preset.PresetId,
                shader.name,
                issues.ToArray(),
                Application.platform.ToString(),
                SystemInfo.graphicsDeviceType.ToString());
        }

        public bool TryApplyPreset(
            string slotId,
            MaterialOverridePreset preset,
            out MaterialCompatibilityReport report,
            out string error)
        {
            report =
                EvaluatePreset(
                    slotId,
                    preset);

            if (!report.Compatible)
            {
                error =
                    report.Issues.Length > 0
                        ? report.Issues[0].Message
                        : "Material preset is incompatible.";
                return false;
            }

            if (!TryGetRecord(
                    slotId,
                    out var record,
                    out error))
            {
                return false;
            }

            var shader =
                ResolvePresetShader(
                    record,
                    preset,
                    out var shaderError);

            if (shader == null)
            {
                return FailAndFallback(
                    record,
                    shaderError,
                    out error);
            }

            if (!TryApplyShader(
                    slotId,
                    shader,
                    out error))
            {
                return false;
            }

            try
            {
                var runtimeMaterial =
                    record.RuntimeMaterial;

                foreach (var parameter in
                         preset.Parameters ??
                         Array.Empty<MaterialParameterOverride>())
                {
                    ApplyParameter(
                        runtimeMaterial,
                        parameter);
                }

                record.Status =
                    new MaterialOverrideStatus(
                        record.Id,
                        MaterialOverrideHealth.Active,
                        shader.name,
                        null,
                        preset.PresetId);

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                return FailAndFallback(
                    record,
                    exception.Message,
                    out error);
            }
        }

        public bool TryApplyShaderId(
            string slotId,
            string shaderId,
            out string error)
        {
            error = null;

            if (!TryGetRecord(
                slotId,
                out var record,
                out error))
            {
                return false;
            }

            if (!RuntimeShaderRegistry.TryResolve(
                    shaderId,
                    out var shader))
            {
                return FailAndFallback(
                    record,
                    $"Precompiled shader '{shaderId}' was not found. " +
                    "Built-player shaders must survive stripping; external shaders must be registered from a compatible bundle.",
                    out error);
            }

            return TryApplyShader(
                slotId,
                shader,
                out error);
        }

        public bool TryApplyShader(
            string slotId,
            Shader shader,
            out string error)
        {
            error = null;

            if (!TryGetRecord(
                slotId,
                out var record,
                out error))
            {
                return false;
            }

            if (shader == null)
            {
                return FailAndFallback(
                    record,
                    "Shader reference is null.",
                    out error);
            }

            if (!shader.isSupported)
            {
                return FailAndFallback(
                    record,
                    $"Shader '{shader.name}' is not supported on the current graphics device.",
                    out error);
            }

            Material candidate = null;

            try
            {
                candidate =
                    record.SourceMaterial != null
                        ? new Material(record.SourceMaterial)
                        : new Material(shader);

                candidate.name =
                    $"{record.SourceMaterial?.name ?? "Material"} [VCR Override]";
                candidate.hideFlags =
                    HideFlags.DontSave;

                if (candidate.shader != shader)
                {
                    candidate.shader = shader;
                }

                CopyCommonProperties(
                    record.SourceMaterial,
                    candidate);

                ReplaceSlotMaterial(
                    record,
                    candidate);

                DestroyRuntimeMaterial(record);
                record.RuntimeMaterial = candidate;
                record.Status =
                    new MaterialOverrideStatus(
                        record.Id,
                        MaterialOverrideHealth.Active,
                        shader.name,
                        null);

                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    DestroyMaterial(candidate);
                }

                return FailAndFallback(
                    record,
                    exception.Message,
                    out error);
            }
        }

        public bool TrySetFloat(
            string slotId,
            string propertyName,
            float value,
            out string error)
        {
            return TrySet(
                slotId,
                propertyName,
                material =>
                    material.SetFloat(
                        propertyName,
                        value),
                out error);
        }

        public bool TrySetInt(
            string slotId,
            string propertyName,
            int value,
            out string error)
        {
            return TrySet(
                slotId,
                propertyName,
                material =>
                    material.SetInteger(
                        propertyName,
                        value),
                out error);
        }

        public bool TrySetBool(
            string slotId,
            string propertyName,
            bool value,
            out string error)
        {
            return TrySetInt(
                slotId,
                propertyName,
                value ? 1 : 0,
                out error);
        }

        public bool TrySetColor(
            string slotId,
            string propertyName,
            Color value,
            out string error)
        {
            return TrySet(
                slotId,
                propertyName,
                material =>
                    material.SetColor(
                        propertyName,
                        value),
                out error);
        }

        public bool TrySetVector(
            string slotId,
            string propertyName,
            Vector4 value,
            out string error)
        {
            return TrySet(
                slotId,
                propertyName,
                material =>
                    material.SetVector(
                        propertyName,
                        value),
                out error);
        }

        public bool TrySetTexture(
            string slotId,
            string propertyName,
            Texture value,
            out string error)
        {
            return TrySet(
                slotId,
                propertyName,
                material =>
                    material.SetTexture(
                        propertyName,
                        value),
                out error);
        }

        public bool ClearOverride(
            string slotId)
        {
            if (slotId == null ||
                !_slots.TryGetValue(
                    slotId,
                    out var record))
            {
                return false;
            }

            RestoreSourceMaterial(record);
            record.Status =
                new MaterialOverrideStatus(
                    record.Id,
                    MaterialOverrideHealth.Source,
                    record.SourceMaterial?.shader?.name,
                    null);
            return true;
        }

        public void ClearAllOverrides()
        {
            foreach (var record in _slots.Values)
            {
                RestoreSourceMaterial(record);
            }
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            var active = 0;
            var faulted = 0;

            foreach (var record in _slots.Values)
            {
                if (record.Status.Health ==
                    MaterialOverrideHealth.Active)
                {
                    active++;
                }
                else if (
                    record.Status.Health ==
                    MaterialOverrideHealth.Faulted ||
                    record.Status.Health ==
                    MaterialOverrideHealth.Fallback)
                {
                    faulted++;
                }
            }

            output.Add(new RuntimeMetric(
                "materials.slots",
                _slots.Count,
                "count"));

            output.Add(new RuntimeMetric(
                "materials.overrides.active",
                active,
                "count"));

            output.Add(new RuntimeMetric(
                "materials.overrides.faulted",
                faulted,
                "count"));

            output.Add(new RuntimeMetric(
                "materials.override.errors",
                _errorCount,
                "count"));
        }

        private static Shader ResolvePresetShader(
            SlotRecord record,
            MaterialOverridePreset preset,
            out string error)
        {
            error = null;

            if (preset == null)
            {
                error =
                    "Material preset is required.";
                return null;
            }

            if (preset.PreserveSourceShader)
            {
                var sourceShader =
                    record.SourceMaterial?.shader;

                if (sourceShader == null)
                {
                    error =
                        "Source material has no shader to preserve.";
                }

                return sourceShader;
            }

            if (!RuntimeShaderRegistry.TryResolve(
                    preset.ShaderId,
                    out var shader))
            {
                error =
                    $"Precompiled shader '{preset.ShaderId}' was not found.";
                return null;
            }

            return shader;
        }

        private static void ValidateParameterCompatibility(
            Shader shader,
            MaterialParameterOverride parameter,
            List<MaterialCompatibilityIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(
                    parameter.Name))
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "property_name_required",
                        null,
                        "Shader property name is required."));
                return;
            }

            if (parameter.Kind ==
                ShaderParameterKind.Texture)
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "preset_texture_binding_unsupported",
                        parameter.Name,
                        "Serialized texture bindings are not supported by the P2 preset contract yet; use the runtime texture API."));
                return;
            }

            if (!TryGetShaderPropertyType(
                    shader,
                    parameter.Name,
                    out var propertyType))
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "property_missing",
                        parameter.Name,
                        $"Shader '{shader.name}' does not expose property '{parameter.Name}'."));
                return;
            }

            if (!IsCompatiblePropertyType(
                    parameter.Kind,
                    propertyType))
            {
                issues.Add(
                    new MaterialCompatibilityIssue(
                        "property_type_mismatch",
                        parameter.Name,
                        $"Preset kind '{parameter.Kind}' is incompatible with shader property type '{propertyType}' for '{parameter.Name}'."));
            }
        }

        private static bool TryGetShaderPropertyType(
            Shader shader,
            string propertyName,
            out ShaderPropertyType propertyType)
        {
            propertyType = default;

            if (shader == null ||
                string.IsNullOrWhiteSpace(
                    propertyName))
            {
                return false;
            }

            var count =
                shader.GetPropertyCount();

            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(
                        shader.GetPropertyName(i),
                        propertyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                propertyType =
                    shader.GetPropertyType(i);
                return true;
            }

            return false;
        }

        private static bool IsCompatiblePropertyType(
            ShaderParameterKind kind,
            ShaderPropertyType propertyType)
        {
            return kind switch
            {
                ShaderParameterKind.Float =>
                    propertyType == ShaderPropertyType.Float ||
                    propertyType == ShaderPropertyType.Range,

                ShaderParameterKind.Int ||
                ShaderParameterKind.Bool ||
                ShaderParameterKind.Enum =>
                    propertyType == ShaderPropertyType.Int ||
                    propertyType == ShaderPropertyType.Float ||
                    propertyType == ShaderPropertyType.Range,

                ShaderParameterKind.Color =>
                    propertyType == ShaderPropertyType.Color,

                ShaderParameterKind.Vector =>
                    propertyType == ShaderPropertyType.Vector,

                ShaderParameterKind.Texture =>
                    propertyType == ShaderPropertyType.Texture,

                _ => false
            };
        }

        private static void ApplyParameter(
            Material material,
            MaterialParameterOverride parameter)
        {
            if (material == null)
            {
                throw new InvalidOperationException(
                    "Runtime override material is missing.");
            }

            switch (parameter.Kind)
            {
                case ShaderParameterKind.Float:
                    material.SetFloat(
                        parameter.Name,
                        parameter.X);
                    break;

                case ShaderParameterKind.Int:
                case ShaderParameterKind.Enum:
                    material.SetInteger(
                        parameter.Name,
                        parameter.IntValue);
                    break;

                case ShaderParameterKind.Bool:
                    material.SetInteger(
                        parameter.Name,
                        parameter.BoolValue
                            ? 1
                            : 0);
                    break;

                case ShaderParameterKind.Color:
                    material.SetColor(
                        parameter.Name,
                        new Color(
                            parameter.X,
                            parameter.Y,
                            parameter.Z,
                            parameter.W));
                    break;

                case ShaderParameterKind.Vector:
                    material.SetVector(
                        parameter.Name,
                        new Vector4(
                            parameter.X,
                            parameter.Y,
                            parameter.Z,
                            parameter.W));
                    break;

                case ShaderParameterKind.Texture:
                    throw new NotSupportedException(
                        "Serialized texture preset bindings are not supported yet.");

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(parameter.Kind),
                        parameter.Kind,
                        "Unsupported shader parameter kind.");
            }
        }

        private bool TrySet(
            string slotId,
            string propertyName,
            Action<Material> setter,
            out string error)
        {
            error = null;

            if (!TryGetRecord(
                slotId,
                out var record,
                out error))
            {
                return false;
            }

            var material =
                record.RuntimeMaterial;

            if (material == null)
            {
                error =
                    "Material slot has no active override.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                propertyName))
            {
                error =
                    "Shader property name is required.";
                return false;
            }

            if (!material.HasProperty(propertyName))
            {
                error =
                    $"Shader '{material.shader?.name}' does not expose property '{propertyName}'.";
                return false;
            }

            try
            {
                setter(material);
                return true;
            }
            catch (Exception exception)
            {
                return FailAndFallback(
                    record,
                    exception.Message,
                    out error);
            }
        }

        private bool TryGetRecord(
            string slotId,
            out SlotRecord record,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(
                slotId))
            {
                record = null;
                error =
                    "Material slot id is required.";
                return false;
            }

            if (!_slots.TryGetValue(
                slotId,
                out record))
            {
                error =
                    $"Material slot '{slotId}' was not found.";
                return false;
            }

            error = null;
            return true;
        }

        private bool FailAndFallback(
            SlotRecord record,
            string message,
            out string error)
        {
            _errorCount++;
            error =
                string.IsNullOrWhiteSpace(message)
                    ? "Material override failed."
                    : message;

            RestoreSourceMaterial(record);

            record.Status =
                new MaterialOverrideStatus(
                    record.Id,
                    MaterialOverrideHealth.Fallback,
                    record.SourceMaterial?.shader?.name,
                    error);

            Debug.LogWarning(
                $"VCR material override fallback: slot='{record.Id}', error='{error}'",
                this);

            return false;
        }

        private static void ReplaceSlotMaterial(
            SlotRecord record,
            Material material)
        {
            if (record.Renderer == null)
            {
                throw new MissingReferenceException(
                    "Material-slot renderer was destroyed.");
            }

            var materials =
                record.Renderer.sharedMaterials;

            if (record.SlotIndex < 0 ||
                record.SlotIndex >=
                    materials.Length)
            {
                throw new IndexOutOfRangeException(
                    "Material slot index changed after discovery.");
            }

            materials[record.SlotIndex] =
                material;
            record.Renderer.sharedMaterials =
                materials;
        }

        private static void CopyCommonProperties(
            Material source,
            Material target)
        {
            if (source == null ||
                target == null)
            {
                return;
            }

            if (source.HasProperty("_MainTex") &&
                target.HasProperty("_MainTex"))
            {
                target.SetTexture(
                    "_MainTex",
                    source.GetTexture("_MainTex"));
                target.SetTextureOffset(
                    "_MainTex",
                    source.GetTextureOffset("_MainTex"));
                target.SetTextureScale(
                    "_MainTex",
                    source.GetTextureScale("_MainTex"));
            }

            if (source.HasProperty("_BaseMap") &&
                target.HasProperty("_BaseMap"))
            {
                target.SetTexture(
                    "_BaseMap",
                    source.GetTexture("_BaseMap"));
            }

            if (source.HasProperty("_Color") &&
                target.HasProperty("_Color"))
            {
                target.SetColor(
                    "_Color",
                    source.GetColor("_Color"));
            }

            if (source.HasProperty("_BaseColor") &&
                target.HasProperty("_BaseColor"))
            {
                target.SetColor(
                    "_BaseColor",
                    source.GetColor("_BaseColor"));
            }
        }

        private static string BuildStablePath(
            Transform root,
            Transform target)
        {
            if (target == root)
            {
                return ".";
            }

            var parts = new Stack<string>();
            var current = target;

            while (current != null &&
                   current != root)
            {
                parts.Push(
                    $"{current.name}[{current.GetSiblingIndex()}]");
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        private void RestoreSourceMaterial(
            SlotRecord record)
        {
            if (record.Renderer != null)
            {
                var materials =
                    record.Renderer.sharedMaterials;

                if (record.SlotIndex >= 0 &&
                    record.SlotIndex <
                        materials.Length)
                {
                    materials[record.SlotIndex] =
                        record.SourceMaterial;
                    record.Renderer.sharedMaterials =
                        materials;
                }
            }

            DestroyRuntimeMaterial(record);
        }

        private void DestroyRuntimeMaterial(
            SlotRecord record)
        {
            if (record.RuntimeMaterial == null)
            {
                return;
            }

            DestroyMaterial(
                record.RuntimeMaterial);
            record.RuntimeMaterial = null;
        }

        private static void DestroyMaterial(
            Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        private void OnDestroy()
        {
            ClearAllOverrides();
        }
    }
}
