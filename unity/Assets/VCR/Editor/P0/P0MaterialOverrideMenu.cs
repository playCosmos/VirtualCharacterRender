using UnityEditor;
using UnityEngine;
using VCR.Runtime.Materials;
using VCR.Runtime.Materials.Unity;

namespace VCR.Editor.P0
{
    public static class P0MaterialOverrideMenu
    {
        [MenuItem("VCR/P0/Validate Material Override Runtime")]
        public static void Validate()
        {
            var root =
                new GameObject("VCR P0 Material Test");
            var child =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);
            child.name = "MaterialTarget";
            child.transform.SetParent(
                root.transform,
                false);

            Material source = null;

            try
            {
                var shader =
                    Shader.Find(
                        "Universal Render Pipeline/Unlit");

                if (shader == null)
                {
                    Debug.LogError(
                        "VCR P0 material override: FAIL - URP Unlit shader not found.");
                    return;
                }

                source =
                    new Material(shader)
                    {
                        name = "VCR P0 Source Material"
                    };

                var renderer =
                    child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = source;

                var controller =
                    root.AddComponent<
                        MaterialOverrideController>();
                controller.RefreshSlots();

                var slots =
                    controller.GetSlots();

                if (slots.Length != 1)
                {
                    Debug.LogError(
                        $"VCR P0 material override: FAIL - expected 1 slot, got {slots.Length}.");
                    return;
                }

                var slot = slots[0];
                var sourceShader = source.shader;
                var sourceName = source.name;

                var applied =
                    controller.TryApplyShaderId(
                        slot.Id,
                        shader.name,
                        out var applyError);

                var cloneApplied =
                    applied &&
                    renderer.sharedMaterial != null &&
                    renderer.sharedMaterial != source &&
                    renderer.sharedMaterial.shader == shader;

                var sourcePreserved =
                    source.shader == sourceShader &&
                    source.name == sourceName;

                var sourceColorBefore =
                    source.HasProperty("_BaseColor")
                        ? source.GetColor("_BaseColor")
                        : Color.white;

                var parameterApplied =
                    controller.TrySetColor(
                        slot.Id,
                        "_BaseColor",
                        Color.magenta,
                        out var parameterError);

                var parameterIsolated =
                    parameterApplied &&
                    source.HasProperty("_BaseColor") &&
                    source.GetColor("_BaseColor") ==
                        sourceColorBefore &&
                    renderer.sharedMaterial != source &&
                    renderer.sharedMaterial.GetColor(
                        "_BaseColor") ==
                        Color.magenta;

                var invalidApplied =
                    controller.TryApplyShaderId(
                        slot.Id,
                        "VCR/DefinitelyMissingShader",
                        out var invalidError);

                var fallbackRestored =
                    !invalidApplied &&
                    !string.IsNullOrWhiteSpace(
                        invalidError) &&
                    renderer.sharedMaterial == source &&
                    controller.TryGetStatus(
                        slot.Id,
                        out var status) &&
                    status.Health ==
                        MaterialOverrideHealth.Fallback;

                var pass =
                    cloneApplied &&
                    sourcePreserved &&
                    parameterIsolated &&
                    fallbackRestored &&
                    controller.ErrorCount >= 1;

                if (pass)
                {
                    Debug.Log(
                        "VCR P0 material override: PASS - source material preserved, runtime clone parameter mutation isolated, invalid shader fell back to source.");
                }
                else
                {
                    Debug.LogError(
                        "VCR P0 material override: FAIL - " +
                        $"applied={applied}, cloneApplied={cloneApplied}, " +
                        $"sourcePreserved={sourcePreserved}, parameterIsolated={parameterIsolated}, " +
                        $"fallbackRestored={fallbackRestored}, applyError='{applyError}', " +
                        $"parameterError='{parameterError}', invalidError='{invalidError}'.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);

                if (source != null)
                {
                    Object.DestroyImmediate(source);
                }
            }
        }
    }
}
