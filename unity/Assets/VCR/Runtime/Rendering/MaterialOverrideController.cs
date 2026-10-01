using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// Non-destructive material-slot override backend.
    ///
    /// Source Material assets are never modified. Each active override owns a
    /// runtime clone. Failure restores the original renderer material reference.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaterialOverrideController :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [SerializeField] private Transform targetRoot;
        [SerializeField] private bool discoverOnStart = true;

        private sealed class Slot
        {
            public string Id;
            public string RendererPath;
            public Renderer Renderer;
            public int MaterialIndex;
            public Material Source;
            public Material RuntimeClone;
            public MaterialOverrideHealth Health;
            public string OverrideShaderId;
            public string LastError;
        }

        private readonly Dictionary<string, Slot> _slots =
            new(StringComparer.Ordinal);

        private int _overrideSuccessCount;
        private int _overrideFailureCount;
        private int _restoreCount;

        public int SlotCount => _slots.Count;

        private void Awake()
        {
            if (targetRoot == null)
            {
                targetRoot = transform;
            }
        }

        private void Start()
        {
            if (discoverOnStart)
            {
                DiscoverSlots();
            }
        }

        [ContextMenu("Discover Material Slots")]
        public void DiscoverSlots()
        {
            RestoreAll();
            _slots.Clear();

            if (targetRoot == null)
            {
                targetRoot = transform;
            }

            var renderers =
                targetRoot.GetComponentsInChildren<Renderer>(
                    includeInactive: true);

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var rendererPath =
                    GetRelativePath(targetRoot, renderer.transform);
                var sameTransformRenderers =
                    renderer.GetComponents<Renderer>();
                var rendererIndex =
                    Array.IndexOf(
                        sameTransformRenderers,
                        renderer);

                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null)
                    {
                        continue;
                    }

                    var slotId =
                        rendererPath +
                        "::" +
                        renderer.GetType().Name +
                        "#" +
                        Math.Max(0, rendererIndex) +
                        ":" +
                        i;

                    _slots[slotId] = new Slot
                    {
                        Id = slotId,
                        RendererPath = rendererPath,
                        Renderer = renderer,
                        MaterialIndex = i,
                        Source = source,
                        Health = MaterialOverrideHealth.Source,
                    };
                }
            }
        }

        public MaterialSlotStatus[] GetStatuses()
        {
            var result =
                new MaterialSlotStatus[_slots.Count];
            var index = 0;

            foreach (var pair in _slots)
            {
                var slot = pair.Value;

                result[index++] =
                    new MaterialSlotStatus(
                        slot.Id,
                        slot.RendererPath,
                        slot.MaterialIndex,
                        slot.Source != null
                            ? slot.Source.name
                            : string.Empty,
                        slot.Source != null &&
                        slot.Source.shader != null
                            ? slot.Source.shader.name
                            : string.Empty,
                        slot.Health,
                        slot.OverrideShaderId,
                        slot.LastError);
            }

            return result;
        }

        public bool TryApplyShader(
            string slotId,
            string shaderId)
        {
            if (!_slots.TryGetValue(
                    slotId,
                    out var slot))
            {
                _overrideFailureCount++;
                return false;
            }

            if (!RuntimeShaderRegistry.TryResolve(
                    shaderId,
                    out var shader))
            {
                FailAndRestore(
                    slot,
                    shaderId,
                    "Shader was not registered or included in the player.");
                return false;
            }

            Material clone = null;

            try
            {
                clone = new Material(slot.Source)
                {
                    name =
                        slot.Source.name +
                        " [VCR Override]",
                    hideFlags = HideFlags.DontSave,
                    shader = shader,
                };

                Assign(slot, clone);

                DestroyRuntimeClone(slot);
                slot.RuntimeClone = clone;
                slot.Health = MaterialOverrideHealth.Active;
                slot.OverrideShaderId = shaderId;
                slot.LastError = null;
                _overrideSuccessCount++;
                return true;
            }
            catch (Exception exception)
            {
                if (clone != null)
                {
                    DestroyMaterial(clone);
                }

                FailAndRestore(
                    slot,
                    shaderId,
                    exception.Message);
                return false;
            }
        }

        public bool Restore(string slotId)
        {
            if (!_slots.TryGetValue(
                    slotId,
                    out var slot))
            {
                return false;
            }

            RestoreSlot(slot);
            return true;
        }

        [ContextMenu("Restore All Material Slots")]
        public void RestoreAll()
        {
            foreach (var slot in _slots.Values)
            {
                RestoreSlot(slot);
            }
        }

        public bool TrySetFloat(
            string slotId,
            string property,
            float value)
        {
            if (!TryGetActiveMaterial(
                    slotId,
                    property,
                    out var material))
            {
                return false;
            }

            material.SetFloat(property, value);
            return true;
        }

        public bool TrySetInt(
            string slotId,
            string property,
            int value)
        {
            if (!TryGetActiveMaterial(
                    slotId,
                    property,
                    out var material))
            {
                return false;
            }

            material.SetInt(property, value);
            return true;
        }

        public bool TrySetBool(
            string slotId,
            string property,
            bool value)
        {
            return TrySetFloat(
                slotId,
                property,
                value ? 1f : 0f);
        }

        public bool TrySetColor(
            string slotId,
            string property,
            Color value)
        {
            if (!TryGetActiveMaterial(
                    slotId,
                    property,
                    out var material))
            {
                return false;
            }

            material.SetColor(property, value);
            return true;
        }

        public bool TrySetVector(
            string slotId,
            string property,
            Vector4 value)
        {
            if (!TryGetActiveMaterial(
                    slotId,
                    property,
                    out var material))
            {
                return false;
            }

            material.SetVector(property, value);
            return true;
        }

        public bool TrySetTexture(
            string slotId,
            string property,
            Texture value)
        {
            if (!TryGetActiveMaterial(
                    slotId,
                    property,
                    out var material))
            {
                return false;
            }

            material.SetTexture(property, value);
            return true;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "render.material.slots",
                _slots.Count,
                "count"));

            output.Add(new RuntimeMetric(
                "render.material.override_success",
                _overrideSuccessCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.material.override_failure",
                _overrideFailureCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.material.restore",
                _restoreCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.shader.registry",
                RuntimeShaderRegistry.Count,
                "count"));
        }

        private bool TryGetActiveMaterial(
            string slotId,
            string property,
            out Material material)
        {
            material = null;

            if (!_slots.TryGetValue(
                    slotId,
                    out var slot) ||
                slot.RuntimeClone == null ||
                slot.Health !=
                    MaterialOverrideHealth.Active ||
                string.IsNullOrWhiteSpace(property) ||
                !slot.RuntimeClone.HasProperty(property))
            {
                return false;
            }

            material = slot.RuntimeClone;
            return true;
        }

        private void FailAndRestore(
            Slot slot,
            string shaderId,
            string error)
        {
            _overrideFailureCount++;

            RestoreSourceReference(slot);
            DestroyRuntimeClone(slot);

            slot.Health =
                MaterialOverrideHealth.Failed;
            slot.OverrideShaderId = shaderId;
            slot.LastError =
                string.IsNullOrWhiteSpace(error)
                    ? "Unknown shader override failure."
                    : error;
        }

        private void RestoreSlot(Slot slot)
        {
            if (slot == null)
            {
                return;
            }

            var changed =
                slot.RuntimeClone != null ||
                slot.Health !=
                    MaterialOverrideHealth.Source;

            RestoreSourceReference(slot);
            DestroyRuntimeClone(slot);

            slot.Health =
                MaterialOverrideHealth.Source;
            slot.OverrideShaderId = null;
            slot.LastError = null;

            if (changed)
            {
                _restoreCount++;
            }
        }

        private static void RestoreSourceReference(Slot slot)
        {
            if (slot.Renderer == null)
            {
                return;
            }

            var materials =
                slot.Renderer.sharedMaterials;

            if (slot.MaterialIndex < 0 ||
                slot.MaterialIndex >=
                    materials.Length)
            {
                return;
            }

            materials[slot.MaterialIndex] =
                slot.Source;
            slot.Renderer.sharedMaterials =
                materials;
        }

        private static void Assign(
            Slot slot,
            Material material)
        {
            if (slot.Renderer == null)
            {
                throw new InvalidOperationException(
                    "Renderer no longer exists.");
            }

            var materials =
                slot.Renderer.sharedMaterials;

            if (slot.MaterialIndex < 0 ||
                slot.MaterialIndex >=
                    materials.Length)
            {
                throw new InvalidOperationException(
                    "Material slot index is no longer valid.");
            }

            materials[slot.MaterialIndex] =
                material;
            slot.Renderer.sharedMaterials =
                materials;
        }

        private static void DestroyRuntimeClone(Slot slot)
        {
            if (slot.RuntimeClone == null)
            {
                return;
            }

            DestroyMaterial(slot.RuntimeClone);
            slot.RuntimeClone = null;
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

        private static string GetRelativePath(
            Transform root,
            Transform current)
        {
            if (current == root)
            {
                return ".";
            }

            var parts = new Stack<string>();
            var cursor = current;

            while (cursor != null &&
                   cursor != root)
            {
                parts.Push(cursor.name);
                cursor = cursor.parent;
            }

            return string.Join("/", parts);
        }

        private void OnDestroy()
        {
            RestoreAll();
            _slots.Clear();
        }
    }
}
