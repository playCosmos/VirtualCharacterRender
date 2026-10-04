using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Editor.P12
{
    internal static class P12SourceValidation
    {
        [MenuItem("VCR/P12/Run Source Validation")]
        private static void RunFromMenu()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            RunAppearanceAuthoringChecks(
                failures);
            RunAccessoryPackageChecks(
                failures);
            RunSkinnedCompatibilityChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P12 source validation: PASS " +
                    "(appearance convention discovery, explicit wardrobe/accessory bindings, transform-anchor application/restoration, anchor preview/capture safety, authored preset capture, duplicate-id/cycle/non-finite rejection, accessory-package manifest/path/version isolation, serialized authoring contract)");
                return true;
            }

            Debug.LogError(
                "VCR P12 source validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void RunAppearanceAuthoringChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Appearance Validation");

                var appearance =
                    CreateChild(
                        root.transform,
                        "VCRAppearance");
                var outfitsRoot =
                    CreateChild(
                        appearance,
                        "Outfits");
                var accessoriesRoot =
                    CreateChild(
                        appearance,
                        "Accessories");
                var casual =
                    CreateChild(
                        outfitsRoot,
                        "Casual");
                CreateChild(
                    outfitsRoot,
                    "Formal");

                var headSlot =
                    CreateChild(
                        accessoriesRoot,
                        "Head");
                var hat =
                    CreateChild(
                        headSlot,
                        "Hat");
                CreateChild(
                    headSlot,
                    "Glasses");

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                Expect(
                    P12AppearanceAuthoringUtility
                        .TryDiscoverConvention(
                            root.transform,
                            "VCRAppearance",
                            "Outfits",
                            "Accessories",
                            out var outfits,
                            out var accessories,
                            out var discoveryError) &&
                    outfits.Length ==
                        2 &&
                    accessories.Length ==
                        2 &&
                    outfits[0].Roots.Length ==
                        1 &&
                    outfits[0].Roots[0] !=
                        null,
                    "P12 appearance convention discovery must produce explicit outfit/accessory bindings: " +
                    discoveryError,
                    failures);

                var hatBinding =
                    Array.Find(
                        accessories,
                        binding =>
                            binding != null &&
                            binding.SlotId ==
                                "Head" &&
                            binding.AccessoryId ==
                                "Hat");
                var originalHatParent =
                    hat.parent;
                hat.localPosition =
                    new Vector3(
                        0.1f,
                        0.2f,
                        0.3f);
                hat.localRotation =
                    Quaternion.Euler(
                        5f,
                        10f,
                        15f);
                var originalHatPosition =
                    hat.localPosition;
                var originalHatRotation =
                    hat.localRotation;
                var anchor =
                    CreateChild(
                        root.transform,
                        "AccessoryAnchor");

                if (hatBinding != null)
                {
                    hatBinding.AnchorMode =
                        AppearanceAccessoryAnchorMode
                            .Transform;
                    hatBinding.AnchorTransform =
                        anchor;
                    hatBinding.LocalPosition =
                        new Vector3(
                            1f,
                            2f,
                            3f);
                    hatBinding.LocalEulerAngles =
                        new Vector3(
                            10f,
                            20f,
                            30f);
                    hatBinding
                        .RestoreOriginalTransformWhenInactive =
                            true;
                }

                var preset =
                    new AppearancePresetBinding
                    {
                        PresetId =
                            "casual-hat",
                        OutfitId =
                            "Casual",
                        PreferredTransitionId =
                            "Immediate",
                        Accessories =
                            new[]
                            {
                                new AppearanceAccessorySelectionBinding
                                {
                                    SlotId =
                                        "Head",
                                    AccessoryId =
                                        "Hat"
                                }
                            }
                    };

                runtime.ConfigureBindings(
                    outfits,
                    accessories,
                    new[]
                    {
                        preset
                    },
                    Array.Empty<
                        AppearanceTransitionBinding>(),
                    Array.Empty<
                        MonoBehaviour>(),
                    "casual-hat");

                Expect(
                    runtime.Status.State ==
                        AppearanceRuntimeState.Ready,
                    "explicit discovered appearance bindings must rebuild into a ready runtime: " +
                    runtime.Status.LastError,
                    failures);

                Expect(
                    runtime.SetPreset(
                        "casual-hat",
                        "Immediate",
                        out var applyError) &&
                    runtime.Current.OutfitId ==
                        "Casual" &&
                    runtime.Current.Accessories.Length ==
                        1 &&
                    runtime.Current.Accessories[0]
                        .SlotId ==
                        "Head" &&
                    runtime.Current.Accessories[0]
                        .AccessoryId ==
                        "Hat",
                    "authored preset from explicit bindings must apply through the normal runtime path: " +
                    applyError,
                    failures);

                Expect(
                    hatBinding != null &&
                    ReferenceEquals(
                        hat.parent,
                        anchor) &&
                    Vector3.Distance(
                        hat.localPosition,
                        new Vector3(
                            1f,
                            2f,
                            3f)) <
                        0.0001f &&
                    Quaternion.Angle(
                        hat.localRotation,
                        Quaternion.Euler(
                            10f,
                            20f,
                            30f)) <
                        0.01f,
                    "active transform-anchored accessory must reparent and apply authored local offset atomically",
                    failures);

                Expect(
                    runtime.ClearAccessory(
                        "Head",
                        "Immediate",
                        out var clearAccessoryError) &&
                    ReferenceEquals(
                        hat.parent,
                        originalHatParent) &&
                    Vector3.Distance(
                        hat.localPosition,
                        originalHatPosition) <
                        0.0001f &&
                    Quaternion.Angle(
                        hat.localRotation,
                        originalHatRotation) <
                        0.01f,
                    "inactive anchored accessory must restore its original parent/local transform: " +
                    clearAccessoryError,
                    failures);

                Expect(
                    runtime.SetAccessory(
                        "Head",
                        "Hat",
                        "Immediate",
                        out var restoreAccessoryError) &&
                    ReferenceEquals(
                        hat.parent,
                        anchor),
                    "anchored accessory must remain reusable after original-transform restoration: " +
                    restoreAccessoryError,
                    failures);

                var captured =
                    P12AppearanceAuthoringUtility
                        .CreatePresetFromCurrent(
                            "captured-look",
                            runtime.Current,
                            "Immediate");

                Expect(
                    captured != null &&
                    captured.PresetId ==
                        "captured-look" &&
                    captured.OutfitId ==
                        "Casual" &&
                    captured.Accessories.Length ==
                        1 &&
                    captured.Accessories[0]
                        .SlotId ==
                        "Head" &&
                    captured.Accessories[0]
                        .AccessoryId ==
                        "Hat",
                    "P12 current-appearance capture must create an authored preset binding with outfit/accessory ids",
                    failures);

                var serialized =
                    new SerializedObject(
                        runtime);

                Expect(
                    serialized.FindProperty(
                        "outfits") !=
                        null &&
                    serialized.FindProperty(
                        "accessories") !=
                        null &&
                    serialized.FindProperty(
                        "presets") !=
                        null &&
                    serialized.FindProperty(
                        "defaultPresetId") !=
                        null &&
                    serialized.FindProperty(
                        "appearanceRootName") !=
                        null &&
                    serialized.FindProperty(
                            "accessories")
                        .GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "AnchorMode") !=
                        null &&
                    serialized.FindProperty(
                            "accessories")
                        .GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "AnchorBone") !=
                        null &&
                    serialized.FindProperty(
                            "accessories")
                        .GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "LocalPosition") !=
                        null,
                    "P12 appearance authoring window serialized property contract must remain available",
                    failures);

                Expect(
                    P12AppearanceAuthoringUtility
                        .TryCaptureAnchorOffset(
                            hat,
                            anchor,
                            out var capturedPosition,
                            out var capturedEuler,
                            out var offsetError) &&
                    Vector3.Distance(
                        capturedPosition,
                        hat.localPosition) <
                        0.0001f &&
                    Quaternion.Angle(
                        Quaternion.Euler(
                            capturedEuler),
                        hat.localRotation) <
                        0.01f,
                    "P12 anchor offset capture must derive the current accessory transform relative to its anchor: " +
                    offsetError,
                    failures);

                var cycleChild =
                    CreateChild(
                        hat,
                        "CycleAnchor");

                Expect(
                    !P12AppearanceAuthoringUtility
                        .TryCaptureAnchorOffset(
                            hat,
                            cycleChild,
                            out _,
                            out _,
                            out var cycleError) &&
                    !string.IsNullOrWhiteSpace(
                        cycleError),
                    "P12 anchor authoring must reject accessory-descendant anchors that would create a transform cycle",
                    failures);

                if (hatBinding != null)
                {
                    var validAnchor =
                        hatBinding.AnchorTransform;
                    var validLocalPosition =
                        hatBinding.LocalPosition;

                    hatBinding.AnchorTransform =
                        cycleChild;

                    Expect(
                        !runtime.RebuildConfiguration(
                            out var runtimeCycleError) &&
                        runtimeCycleError != null &&
                        runtimeCycleError.IndexOf(
                            "descendant",
                            StringComparison.OrdinalIgnoreCase) >=
                            0,
                        "appearance runtime configuration must reject descendant accessory anchors before activation",
                        failures);

                    hatBinding.AnchorTransform =
                        validAnchor;
                    hatBinding.LocalPosition =
                        new Vector3(
                            float.NaN,
                            0f,
                            0f);

                    Expect(
                        !runtime.RebuildConfiguration(
                            out var nonFiniteRuntimeError) &&
                        nonFiniteRuntimeError != null &&
                        nonFiniteRuntimeError.IndexOf(
                            "finite",
                            StringComparison.OrdinalIgnoreCase) >=
                            0,
                        "appearance runtime configuration must reject non-finite accessory anchor poses",
                        failures);

                    Expect(
                        !P12AppearanceAuthoringUtility
                            .TryValidateAccessoryAnchorPose(
                                hatBinding.LocalPosition,
                                hatBinding.LocalEulerAngles,
                                hatBinding.OverrideLocalScale,
                                hatBinding.LocalScale,
                                out var nonFiniteAuthoringError) &&
                        !string.IsNullOrWhiteSpace(
                            nonFiniteAuthoringError),
                        "P12 accessory authoring validation must reject non-finite local pose input before preview",
                        failures);

                    hatBinding.LocalPosition =
                        validLocalPosition;

                    Expect(
                        runtime.RebuildConfiguration(
                            out var restoredAnchorError),
                        "restoring a valid accessory anchor after validation failures must rebuild successfully: " +
                        restoredAnchorError,
                        failures);
                }

                var duplicate =
                    CreateChild(
                        outfitsRoot,
                        "Casual");

                Expect(
                    !P12AppearanceAuthoringUtility
                        .TryDiscoverConvention(
                            root.transform,
                            "VCRAppearance",
                            "Outfits",
                            "Accessories",
                            out _,
                            out _,
                            out var duplicateError) &&
                    duplicateError != null &&
                    duplicateError.IndexOf(
                        "more than once",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 convention discovery must reject duplicate outfit ids before runtime binding",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    duplicate.gameObject);

                var existing =
                    new HashSet<string>(
                        StringComparer.Ordinal)
                    {
                        "Hat",
                        "Hat-2"
                    };
                var unique =
                    P12AppearanceAuthoringUtility
                        .BuildUniqueId(
                            "Hat",
                            existing.Contains);

                Expect(
                    unique ==
                        "Hat-3",
                    "P12 appearance authoring must generate stable suffix ids for duplicate logical names",
                    failures);

                Expect(
                    casual.gameObject.activeSelf &&
                    hat.gameObject.activeSelf,
                    "P12 validation hierarchy objects must remain usable scene roots after discovery",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 appearance authoring validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }
            }
        }

        private static void RunAccessoryPackageChecks(
            List<string> failures)
        {
            var valid =
                new P12AccessoryPackageManifest
                {
                    PackageId =
                        "vcr.test.hat",
                    PackageVersion =
                        "1.0.0",
                    SlotId =
                        "Head",
                    AccessoryId =
                        "Hat",
                    ModelFile =
                        "models/hat.fbx",
                    AnchorMode =
                        "HumanoidBone",
                    HumanoidBone =
                        "Head",
                    LocalPosition =
                        new Vector3(
                            0f,
                            0.1f,
                            0f),
                    LocalEulerAngles =
                        new Vector3(
                            0f,
                            15f,
                            0f),
                    OverrideLocalScale =
                        true,
                    LocalScale =
                        Vector3.one
                };

            Expect(
                P12AccessoryPackageManifestValidator
                    .TryValidate(
                        valid,
                        out var anchorMode,
                        out var bone,
                        out var validError) &&
                anchorMode ==
                    AppearanceAccessoryAnchorMode
                        .HumanoidBone &&
                bone ==
                    HumanBodyBones.Head,
                "valid rigid accessory package manifest must validate and resolve humanoid anchor metadata: " +
                validError,
                failures);

            var traversal =
                CloneManifest(
                    valid);
            traversal.ModelFile =
                "../escape.fbx";

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        traversal,
                        out _,
                        out _,
                        out _),
                "accessory package manifest must reject path traversal",
                failures);

            var prefab =
                CloneManifest(
                    valid);
            prefab.ModelFile =
                "models/hat.prefab";

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        prefab,
                        out _,
                        out _,
                        out _),
                "accessory package v1 must reject external prefab payloads",
                failures);

            var unsafeId =
                CloneManifest(
                    valid);
            unsafeId.PackageId =
                "../unsafe";

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        unsafeId,
                        out _,
                        out _,
                        out _),
                "accessory package must reject unsafe package identifiers",
                failures);

            var future =
                CloneManifest(
                    valid);
            future.FormatVersion =
                P12AccessoryPackageManifest
                    .CurrentFormatVersion +
                1;

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        future,
                        out _,
                        out _,
                        out _),
                "accessory package must reject unsupported newer manifest versions",
                failures);

            var invalidBone =
                CloneManifest(
                    valid);
            invalidBone.HumanoidBone =
                "NotABone";

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        invalidBone,
                        out _,
                        out _,
                        out _),
                "humanoid-bone accessory package must reject unknown bones",
                failures);

            var sceneTransformAnchor =
                CloneManifest(
                    valid);
            sceneTransformAnchor.AnchorMode =
                "Transform";

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        sceneTransformAnchor,
                        out _,
                        out _,
                        out var sceneTransformError) &&
                !string.IsNullOrWhiteSpace(
                    sceneTransformError),
                "portable accessory package manifests must reject scene-specific Transform anchors",
                failures);

            var zeroScale =
                CloneManifest(
                    valid);
            zeroScale.LocalScale =
                new Vector3(
                    1f,
                    0f,
                    1f);

            Expect(
                !P12AccessoryPackageManifestValidator
                    .TryValidate(
                        zeroScale,
                        out _,
                        out _,
                        out _),
                "accessory package scale override must reject a zero axis",
                failures);

            var tempRoot =
                Path.Combine(
                    Path.GetTempPath(),
                    "vcr-p12-accessory-" +
                    Guid.NewGuid()
                        .ToString("N"));

            try
            {
                Directory.CreateDirectory(
                    Path.Combine(
                        tempRoot,
                        "models"));

                var manifestPath =
                    Path.Combine(
                        tempRoot,
                        "manifest.json");
                var modelPath =
                    Path.Combine(
                        tempRoot,
                        "models",
                        "hat.fbx");

                File.WriteAllText(
                    manifestPath,
                    JsonUtility.ToJson(
                        valid,
                        prettyPrint:
                            true));
                File.WriteAllBytes(
                    modelPath,
                    new byte[]
                    {
                        0x56,
                        0x43,
                        0x52,
                        0x31,
                        0x32
                    });

                Expect(
                    P12AccessoryPackageImporter
                        .TryLoadManifest(
                            manifestPath,
                            out var loaded,
                            out var loadedAnchorMode,
                            out var loadedBone,
                            out var loadError) &&
                    loaded != null &&
                    loaded.PackageId ==
                        valid.PackageId &&
                    loadedAnchorMode ==
                        AppearanceAccessoryAnchorMode
                            .HumanoidBone &&
                    loadedBone ==
                        HumanBodyBones.Head,
                    "accessory package loader must resolve a validated manifest and in-package FBX path before Unity import: " +
                    loadError,
                    failures);

                Expect(
                    !P12AccessoryPackageImporter
                        .TryResolveModelPath(
                            manifestPath,
                            "../escape.fbx",
                            out _,
                            out var escapeError) &&
                    !string.IsNullOrWhiteSpace(
                        escapeError),
                    "accessory package path resolver must reject escaping the manifest directory",
                    failures);

                const string destinationRoot =
                    "Assets/VCR/Editor/P12/__AccessoryPackageValidation";
                EnsureAssetFolder(
                    destinationRoot);
                var canonicalFolder =
                    P12AccessoryPackageImporter
                        .BuildPackageFolder(
                            destinationRoot,
                            valid.PackageId,
                            valid.PackageVersion);
                EnsureAssetFolder(
                    canonicalFolder);

                Expect(
                    !P12AccessoryPackageImporter
                        .TryImport(
                            manifestPath,
                            destinationRoot,
                            out _,
                            out var duplicateInstallError) &&
                    duplicateInstallError != null &&
                    duplicateInstallError.IndexOf(
                        "already installed",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "accessory package importer must reject duplicate installation of the same package id/version",
                    failures);

                if (AssetDatabase.IsValidFolder(
                        destinationRoot))
                {
                    AssetDatabase.DeleteAsset(
                        destinationRoot);
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 accessory package validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (Directory.Exists(
                        tempRoot))
                {
                    try
                    {
                        Directory.Delete(
                            tempRoot,
                            recursive:
                                true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void EnsureAssetFolder(
            string assetFolder)
        {
            var parts =
                assetFolder.Split(
                    new[]
                    {
                        '/'
                    },
                    StringSplitOptions
                        .RemoveEmptyEntries);
            var current =
                "Assets";

            for (var i = 1;
                 i < parts.Length;
                 i++)
            {
                var next =
                    current +
                    "/" +
                    parts[i];

                if (!AssetDatabase.IsValidFolder(
                        next))
                {
                    var guid =
                        AssetDatabase.CreateFolder(
                            current,
                            parts[i]);

                    if (string.IsNullOrWhiteSpace(
                            guid))
                    {
                        throw new InvalidOperationException(
                            $"Could not create validation asset folder '{next}'.");
                    }
                }

                current =
                    next;
            }
        }

        private static void RunSkinnedCompatibilityChecks(
            List<string> failures)
        {
            GameObject sourceRoot = null;
            GameObject targetRoot = null;
            Mesh mesh = null;

            try
            {
                sourceRoot =
                    new GameObject(
                        "P12 Source Skeleton");
                var sourceHips =
                    CreateChild(
                        sourceRoot.transform,
                        "Hips");
                var sourceHead =
                    CreateChild(
                        sourceHips,
                        "Head");
                var renderer =
                    sourceRoot.AddComponent<
                        SkinnedMeshRenderer>();

                mesh =
                    new Mesh
                    {
                        name =
                            "P12 Compatibility Mesh",
                        bindposes =
                            new[]
                            {
                                Matrix4x4.identity,
                                Matrix4x4.identity
                            }
                    };
                renderer.sharedMesh =
                    mesh;
                renderer.bones =
                    new[]
                    {
                        sourceHips,
                        sourceHead
                    };
                renderer.rootBone =
                    sourceHips;

                targetRoot =
                    new GameObject(
                        "P12 Target Skeleton");
                var targetHips =
                    CreateChild(
                        targetRoot.transform,
                        "Hips");
                var targetHead =
                    CreateChild(
                        targetHips,
                        "Head");

                Expect(
                    P12SkinnedCompatibilityAnalyzer
                        .TryAnalyzeStructure(
                            renderer,
                            targetRoot.transform,
                            null,
                            requireHumanoidTarget:
                                false,
                            out var compatibleReport,
                            out var compatibleError) &&
                    compatibleReport != null &&
                    compatibleReport
                        .StructurallyCompatible &&
                    compatibleReport.SourceBoneCount ==
                        2 &&
                    compatibleReport.MappedBoneCount ==
                        2 &&
                    compatibleReport.ExactNameMappedCount ==
                        2 &&
                    compatibleReport
                        .RequiresBindPosePreview,
                    "matching source/target skeleton names and hierarchy must pass structural compatibility while still requiring bind-pose preview: " +
                    compatibleError,
                    failures);

                targetHead.name =
                    "MissingHead";

                Expect(
                    P12SkinnedCompatibilityAnalyzer
                        .TryAnalyzeStructure(
                            renderer,
                            targetRoot.transform,
                            null,
                            requireHumanoidTarget:
                                false,
                            out var missingReport,
                            out var missingError) &&
                    missingReport != null &&
                    !missingReport
                        .StructurallyCompatible &&
                    missingReport.Errors.Length >
                        0,
                    "structural compatibility must report a missing target bone as incompatible: " +
                    missingError,
                    failures);

                targetHead.name =
                    "Head";
                var duplicateHead =
                    CreateChild(
                        targetHips,
                        "Head");

                Expect(
                    P12SkinnedCompatibilityAnalyzer
                        .TryAnalyzeStructure(
                            renderer,
                            targetRoot.transform,
                            null,
                            requireHumanoidTarget:
                                false,
                            out var ambiguousReport,
                            out var ambiguousError) &&
                    ambiguousReport != null &&
                    !ambiguousReport
                        .StructurallyCompatible &&
                    Array.Exists(
                        ambiguousReport.Errors,
                        message =>
                            message != null &&
                            message.IndexOf(
                                "ambiguous",
                                StringComparison.OrdinalIgnoreCase) >=
                            0),
                    "structural compatibility must reject ambiguous exact-name target bone mappings: " +
                    ambiguousError,
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    duplicateHead.gameObject);

                mesh.bindposes =
                    new[]
                    {
                        Matrix4x4.identity
                    };

                Expect(
                    P12SkinnedCompatibilityAnalyzer
                        .TryAnalyzeStructure(
                            renderer,
                            targetRoot.transform,
                            null,
                            requireHumanoidTarget:
                                false,
                            out var bindposeReport,
                            out var bindposeError) &&
                    bindposeReport != null &&
                    !bindposeReport
                        .StructurallyCompatible &&
                    Array.Exists(
                        bindposeReport.Errors,
                        message =>
                            message != null &&
                            message.IndexOf(
                                "bindpose",
                                StringComparison.OrdinalIgnoreCase) >=
                            0),
                    "structural compatibility must reject mismatched bindpose/bone counts: " +
                    bindposeError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 skinned compatibility validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (sourceRoot != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            sourceRoot);
                }

                if (targetRoot != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            targetRoot);
                }

                if (mesh != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            mesh);
                }
            }
        }

        private static P12AccessoryPackageManifest
            CloneManifest(
                P12AccessoryPackageManifest source)
        {
            return JsonUtility.FromJson<
                P12AccessoryPackageManifest>(
                    JsonUtility.ToJson(
                        source));
        }

        private static Transform CreateChild(
            Transform parent,
            string name)
        {
            var child =
                new GameObject(
                    name)
                    .transform;
            child.SetParent(
                parent,
                false);
            return child;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }
}
