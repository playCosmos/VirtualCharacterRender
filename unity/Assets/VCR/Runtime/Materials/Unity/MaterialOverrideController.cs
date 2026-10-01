using System;
using System.Collections.Generic;
using UnityEngine;
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
