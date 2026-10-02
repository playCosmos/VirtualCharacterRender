using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Materials;
using VCR.Runtime.Materials.Unity;

namespace VCR.Editor.P2
{
    public static class P2MaterialRuntimeValidation
    {
        [MenuItem("VCR/P2/Validate Material Shader Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            GameObject root = null;
            Material source = null;
            Texture2D registeredTexture = null;

            try
            {
                var shader =
                    Shader.Find(
                        "Universal Render Pipeline/Unlit");

                if (shader == null)
                {
                    failures.Add(
                        "URP Unlit shader was not found.");
                    return Finish(failures);
                }

                root =
                    new GameObject(
                        "VCR P2 Material Validation");

                var child =
                    GameObject.CreatePrimitive(
                        PrimitiveType.Quad);
                child.name =
                    "MaterialTarget";
                child.transform.SetParent(
                    root.transform,
                    false);

                source =
                    new Material(shader)
                    {
                        name =
                            "VCR P2 Source Material"
                    };

                var renderer =
                    child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial =
                    source;

                var sourceColor =
                    source.HasProperty(
                        "_BaseColor")
                        ? source.GetColor(
                            "_BaseColor")
                        : Color.white;

                var controller =
                    root.AddComponent<
                        MaterialOverrideController>();
                controller.RefreshSlots();

                var slots =
                    controller.GetSlots();

                Expect(
                    slots.Length == 1,
                    "validation model must expose exactly one material slot",
                    failures);

                if (slots.Length != 1)
                {
                    return Finish(failures);
                }

                var slot =
                    slots[0];

                var preservePreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p2.preserve-source",
                        ShaderId =
                            null,
                        Parameters =
                            new[]
                            {
                                MaterialParameterOverride.Color(
                                    "_BaseColor",
                                    1f,
                                    0f,
                                    1f,
                                    1f)
                            }
                    };

                var preserveReport =
                    controller.EvaluatePreset(
                        slot.Id,
                        preservePreset);

                Expect(
                    preserveReport.Compatible,
                    "source-shader preset must pass compatibility when its property exists",
                    failures);

                var preserveApplied =
                    controller.TryApplyPreset(
                        slot.Id,
                        preservePreset,
                        out var appliedReport,
                        out var applyError);

                Expect(
                    preserveApplied &&
                    appliedReport.Compatible &&
                    string.IsNullOrEmpty(
                        applyError),
                    "source-shader preset must apply",
                    failures);

                Expect(
                    renderer.sharedMaterial !=
                        source &&
                    renderer.sharedMaterial.shader ==
                        source.shader,
                    "source-shader preset must use a runtime clone and preserve the source shader",
                    failures);

                Expect(
                    source.GetColor(
                        "_BaseColor") ==
                        sourceColor &&
                    renderer.sharedMaterial.GetColor(
                        "_BaseColor") ==
                        Color.magenta,
                    "preset parameter mutation must remain isolated from the source material",
                    failures);

                Expect(
                    controller.TryGetStatus(
                        slot.Id,
                        out var activeStatus) &&
                    activeStatus.Health ==
                        MaterialOverrideHealth.Active &&
                    activeStatus.PresetId ==
                        preservePreset.PresetId,
                    "active material status must expose the applied preset id",
                    failures);

                var activeMaterial =
                    renderer.sharedMaterial;

                var invalidPreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p2.invalid-property",
                        Parameters =
                            new[]
                            {
                                MaterialParameterOverride.Float(
                                    "_DefinitelyMissingProperty",
                                    1f)
                            }
                    };

                var invalidReport =
                    controller.EvaluatePreset(
                        slot.Id,
                        invalidPreset);

                Expect(
                    !invalidReport.Compatible &&
                    invalidReport.Issues.Length > 0 &&
                    invalidReport.Issues[0].Code ==
                        "property_missing",
                    "missing shader properties must be rejected before mutation",
                    failures);

                var invalidApplied =
                    controller.TryApplyPreset(
                        slot.Id,
                        invalidPreset,
                        out _,
                        out var invalidError);

                Expect(
                    !invalidApplied &&
                    !string.IsNullOrWhiteSpace(
                        invalidError) &&
                    renderer.sharedMaterial ==
                        activeMaterial,
                    "preflight-incompatible presets must leave the current material unchanged",
                    failures);

                registeredTexture =
                    new Texture2D(
                        2,
                        2)
                    {
                        name =
                            "VCR P2 Registered Texture"
                    };

                RuntimeTextureRegistry.Register(
                    "texture-id",
                    registeredTexture);

                var texturePreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p2.texture-binding",
                        Parameters =
                            new[]
                            {
                                new MaterialParameterOverride
                                {
                                    Name =
                                        "_BaseMap",
                                    Kind =
                                        ShaderParameterKind.Texture,
                                    StringValue =
                                        "texture-id"
                                }
                            }
                    };

                var textureReport =
                    controller.EvaluatePreset(
                        slot.Id,
                        texturePreset);

                Expect(
                    textureReport.Compatible,
                    "registered texture ids must pass preset compatibility",
                    failures);

                Expect(
                    controller.TryApplyPreset(
                        slot.Id,
                        texturePreset,
                        out var textureApplyReport,
                        out var textureApplyError) &&
                    textureApplyReport.Compatible &&
                    string.IsNullOrEmpty(
                        textureApplyError) &&
                    renderer.sharedMaterial.GetTexture(
                        "_BaseMap") ==
                        registeredTexture,
                    "registered texture ids must apply through the preset resolver path",
                    failures);

                var missingTexturePreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p2.texture-missing",
                        Parameters =
                            new[]
                            {
                                new MaterialParameterOverride
                                {
                                    Name =
                                        "_BaseMap",
                                    Kind =
                                        ShaderParameterKind.Texture,
                                    StringValue =
                                        "missing-texture-id"
                                }
                            }
                    };

                var missingTextureReport =
                    controller.EvaluatePreset(
                        slot.Id,
                        missingTexturePreset);

                Expect(
                    !missingTextureReport.Compatible &&
                    Array.Exists(
                        missingTextureReport.Issues,
                        issue =>
                            issue.Code ==
                            "texture_unavailable"),
                    "missing serialized texture ids must fail preflight explicitly",
                    failures);

                var explicitShaderPreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p2.explicit-shader",
                        ShaderId =
                            shader.name,
                        Parameters =
                            Array.Empty<
                                MaterialParameterOverride>()
                    };

                Expect(
                    controller.TryApplyPreset(
                        slot.Id,
                        explicitShaderPreset,
                        out var explicitReport,
                        out var explicitError) &&
                    explicitReport.Compatible &&
                    string.IsNullOrEmpty(
                        explicitError),
                    "registered/built-in shader ids must apply through the preset path",
                    failures);

                var statuses =
                    controller.GetStatuses();

                Expect(
                    statuses.Length == 1 &&
                    statuses[0].PresetId ==
                        explicitShaderPreset.PresetId,
                    "status snapshot must report the latest active preset",
                    failures);

                Expect(
                    controller.ClearOverride(
                        slot.Id) &&
                    renderer.sharedMaterial ==
                        source &&
                    controller.TryGetStatus(
                        slot.Id,
                        out var restoredStatus) &&
                    restoredStatus.Health ==
                        MaterialOverrideHealth.Source &&
                    restoredStatus.PresetId == null,
                    "clear override must restore the exact source material and clear preset state",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(root);
                }

                if (source != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(source);
                }

                if (registeredTexture != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            registeredTexture);
                }

                RuntimeTextureRegistry
                    .ClearRegistered();
                RuntimeShaderRegistry
                    .ClearRegistered();
            }

            return Finish(failures);
        }

        private static bool Finish(
            List<string> failures)
        {
            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P2 material/shader runtime validation: PASS " +
                    "(source shader preservation, preset compatibility, isolated parameters, texture resolution, explicit rejection, status, restore)");
                return true;
            }

            Debug.LogError(
                "VCR P2 material/shader runtime validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }
}
