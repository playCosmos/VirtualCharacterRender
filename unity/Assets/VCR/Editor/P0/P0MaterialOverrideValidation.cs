using UnityEditor;
using UnityEngine;
using VCR.Runtime.Rendering;

namespace VCR.Editor.P0
{
    public static class P0MaterialOverrideValidation
    {
        [MenuItem("VCR/P0/Validate Material Override Safety")]
        public static void Validate()
        {
            var shader =
                Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                Debug.LogError(
                    "VCR P0 material override: FAIL - URP Unlit shader not found.");
                return;
            }

            var root = new GameObject(
                "VCR P0 Material Override Self-Test");
            root.hideFlags = HideFlags.HideAndDontSave;

            Material source = null;

            try
            {
                var primitive =
                    GameObject.CreatePrimitive(
                        PrimitiveType.Quad);
                primitive.transform.SetParent(
                    root.transform,
                    false);

                var collider =
                    primitive.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                source =
                    new Material(shader)
                    {
                        name = "VCR P0 Source",
                        hideFlags = HideFlags.HideAndDontSave,
                    };

                source.SetColor(
                    "_BaseColor",
                    new Color(0.2f, 0.3f, 0.4f, 1f));

                var renderer =
                    primitive.GetComponent<Renderer>();
                renderer.sharedMaterial = source;

                var controller =
                    root.AddComponent<
                        MaterialOverrideController>();
                controller.DiscoverSlots();

                var statuses =
                    controller.GetStatuses();

                if (statuses.Length != 1)
                {
                    Fail(
                        "Expected exactly one discovered material slot.");
                    return;
                }

                var slotId = statuses[0].SlotId;

                RuntimeShaderRegistry.Register(
                    "p0/urp-unlit",
                    shader,
                    replace: true);

                if (!controller.TryApplyShader(
                        slotId,
                        "p0/urp-unlit"))
                {
                    Fail(
                        "Valid registered shader could not be applied.");
                    return;
                }

                var runtimeMaterial =
                    renderer.sharedMaterial;

                if (runtimeMaterial == null ||
                    ReferenceEquals(
                        runtimeMaterial,
                        source))
                {
                    Fail(
                        "Override did not create an independent runtime material.");
                    return;
                }

                var sourceBefore =
                    source.GetColor("_BaseColor");

                var parameterChanged =
                    controller.TrySetColor(
                        slotId,
                        "_BaseColor",
                        Color.magenta);

                if (!parameterChanged)
                {
                    Fail(
                        "Generic color parameter setter failed.");
                    return;
                }

                if (source.GetColor("_BaseColor") !=
                    sourceBefore)
                {
                    Fail(
                        "Runtime parameter mutation changed the source material.");
                    return;
                }

                var invalidApplied =
                    controller.TryApplyShader(
                        slotId,
                        "vcr/definitely-missing-shader");

                if (invalidApplied)
                {
                    Fail(
                        "Invalid shader ID unexpectedly applied.");
                    return;
                }

                if (!ReferenceEquals(
                        renderer.sharedMaterial,
                        source))
                {
                    Fail(
                        "Invalid shader failure did not restore the original material reference.");
                    return;
                }

                var failedStatus =
                    controller.GetStatuses()[0];

                if (failedStatus.Health !=
                    MaterialOverrideHealth.Failed ||
                    string.IsNullOrWhiteSpace(
                        failedStatus.LastError))
                {
                    Fail(
                        "Failed override did not retain diagnostic status.");
                    return;
                }

                controller.Restore(slotId);

                if (!ReferenceEquals(
                        renderer.sharedMaterial,
                        source))
                {
                    Fail(
                        "Explicit restore did not preserve the source material reference.");
                    return;
                }

                Debug.Log(
                    "VCR P0 material override SELF-TEST: PASS - runtime clone, generic parameter, invalid-shader fallback, and source-material preservation verified.");
            }
            finally
            {
                RuntimeShaderRegistry.Unregister(
                    "p0/urp-unlit");

                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }

                if (source != null)
                {
                    Object.DestroyImmediate(source);
                }
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError(
                "VCR P0 material override: FAIL - " +
                message);
        }
    }
}
