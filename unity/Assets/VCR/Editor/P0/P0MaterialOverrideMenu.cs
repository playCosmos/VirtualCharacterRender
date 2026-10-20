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
            Texture2D resolverTexture = null;
            Texture2D fallbackTexture = null;
            var textureRegistrySnapshot =
                RuntimeTextureRegistry
                    .CaptureRegistered();

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

                var colorBeforeDirectNonFinite =
                    renderer.sharedMaterial.GetColor(
                        "_BaseColor");
                var directNonFiniteApplied =
                    controller.TrySetColor(
                        slot.Id,
                        "_BaseColor",
                        new Color(
                            float.NaN,
                            0f,
                            0f,
                            1f),
                        out var directNonFiniteError);
                var directNonFiniteRejected =
                    !directNonFiniteApplied &&
                    !string.IsNullOrWhiteSpace(
                        directNonFiniteError) &&
                    renderer.sharedMaterial.GetColor(
                        "_BaseColor") ==
                        colorBeforeDirectNonFinite;

                var materialBeforeNonFinite =
                    renderer.sharedMaterial;
                var nonFinitePreset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p0.non-finite",
                        Parameters =
                            new[]
                            {
                                MaterialParameterOverride.Color(
                                    "_BaseColor",
                                    float.NaN,
                                    0f,
                                    0f,
                                    1f)
                            }
                    };

                var nonFiniteApplied =
                    controller.TryApplyPreset(
                        slot.Id,
                        nonFinitePreset,
                        out var nonFiniteReport,
                        out var nonFiniteError);

                var nonFiniteRejected =
                    !nonFiniteApplied &&
                    !string.IsNullOrWhiteSpace(
                        nonFiniteError) &&
                    nonFiniteReport != null &&
                    nonFiniteReport.Issues.Length >
                        0 &&
                    nonFiniteReport.Issues[0].Code ==
                        "property_value_non_finite" &&
                    renderer.sharedMaterial ==
                        materialBeforeNonFinite;

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

                // Invalid shader selection intentionally restores the
                // source and disposes the runtime material override.
                // Start a fresh valid override before testing fallback
                // texture resolution on a destroyed resolver.
                var overrideRestored =
                    controller.TryApplyShaderId(
                        slot.Id,
                        shader.name,
                        out var overrideRestoreError);

                resolverTexture =
                    new Texture2D(1, 1)
                    {
                        name =
                            "P0 Resolver Texture"
                    };
                fallbackTexture =
                    new Texture2D(1, 1)
                    {
                        name =
                            "P0 Registry Fallback Texture"
                    };

                const string textureId =
                    "p0.destroyed-resolver";
                RuntimeTextureRegistry.Register(
                    textureId,
                    fallbackTexture);

                var textureResolver =
                    root.AddComponent<
                        P0MaterialTextureResolver>();
                textureResolver.Configure(
                    textureId,
                    resolverTexture);
                controller.SetTextureResolver(
                    textureResolver);

                Object.DestroyImmediate(
                    textureResolver);

                string textureError = null;
                var destroyedResolverFallback =
                    overrideRestored &&
                    string.IsNullOrWhiteSpace(overrideRestoreError) &&
                    source.HasProperty(
                        "_BaseMap") &&
                    controller.TrySetTextureId(
                        slot.Id,
                        "_BaseMap",
                        textureId,
                        out textureError) &&
                    renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.GetTexture(
                        "_BaseMap") ==
                        fallbackTexture;

                var pass =
                    cloneApplied &&
                    sourcePreserved &&
                    parameterIsolated &&
                    directNonFiniteRejected &&
                    nonFiniteRejected &&
                    fallbackRestored &&
                    destroyedResolverFallback &&
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
                        $"directNonFiniteRejected={directNonFiniteRejected}, nonFiniteRejected={nonFiniteRejected}, fallbackRestored={fallbackRestored}, destroyedResolverFallback={destroyedResolverFallback}, " +
                        $"applyError='{applyError}', parameterError='{parameterError}', invalidError='{invalidError}', textureError='{textureError}'.");
                }
            }
            finally
            {
                RuntimeTextureRegistry
                    .RestoreRegistered(
                        textureRegistrySnapshot);

                Object.DestroyImmediate(root);

                if (source != null)
                {
                    Object.DestroyImmediate(source);
                }

                if (resolverTexture != null)
                {
                    Object.DestroyImmediate(
                        resolverTexture);
                }

                if (fallbackTexture != null)
                {
                    Object.DestroyImmediate(
                        fallbackTexture);
                }
            }
        }
    }

    internal sealed class P0MaterialTextureResolver :
        MonoBehaviour,
        IMaterialTextureResolver
    {
        private string _textureId;
        private Texture _texture;

        public void Configure(
            string textureId,
            Texture texture)
        {
            _textureId = textureId;
            _texture = texture;
        }

        public bool TryResolve(
            string textureId,
            out Texture texture)
        {
            texture =
                string.Equals(
                    textureId,
                    _textureId,
                    System.StringComparison.Ordinal)
                    ? _texture
                    : null;

            return texture != null;
        }
    }
}
