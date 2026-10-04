using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;

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
            RunTransitionDependencyGraphChecks(
                failures);
            RunEventNodeAuthoringChecks(
                failures);
            RunEventRuleLibraryBrowserChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P12 source validation: PASS " +
                    "(appearance authoring/anchors/packages/preset preview, skinned structural compatibility, transition dependency graph authoring, event-node rule validation/library browsing and serialized contracts)");
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

                Expect(
                    P12SkinnedRebindPreview
                        .TryCreate(
                            renderer,
                            targetRoot.transform,
                            compatibleReport,
                            out var previewSession,
                            out var previewError) &&
                    previewSession != null &&
                    previewSession.PreviewObject !=
                        null &&
                    previewSession.PreviewRenderer !=
                        null &&
                    !ReferenceEquals(
                        previewSession.PreviewRenderer,
                        renderer) &&
                    ReferenceEquals(
                        previewSession.PreviewRenderer
                            .sharedMesh,
                        renderer.sharedMesh) &&
                    previewSession.PreviewRenderer
                        .bones.Length ==
                        2 &&
                    ReferenceEquals(
                        previewSession.PreviewRenderer
                            .bones[0],
                        targetHips) &&
                    ReferenceEquals(
                        previewSession.PreviewRenderer
                            .bones[1],
                        targetHead) &&
                    ReferenceEquals(
                        previewSession.PreviewRenderer
                            .rootBone,
                        targetHips) &&
                    (previewSession.PreviewObject.hideFlags &
                     HideFlags.DontSaveInEditor) !=
                        0 &&
                    (previewSession.PreviewObject.hideFlags &
                     HideFlags.DontSaveInBuild) !=
                        0 &&
                    !float.IsNaN(
                        previewSession
                            .AverageBindMatrixDelta) &&
                    !float.IsInfinity(
                        previewSession
                            .AverageBindMatrixDelta) &&
                    !float.IsNaN(
                        previewSession
                            .MaxBindMatrixDelta) &&
                    !float.IsInfinity(
                        previewSession
                            .MaxBindMatrixDelta) &&
                    renderer.bones.Length ==
                        2 &&
                    ReferenceEquals(
                        renderer.bones[0],
                        sourceHips) &&
                    ReferenceEquals(
                        renderer.bones[1],
                        sourceHead) &&
                    ReferenceEquals(
                        renderer.rootBone,
                        sourceHips),
                    "non-destructive rebind preview must create a DontSave renderer mapped to target bones without mutating the source renderer: " +
                    previewError,
                    failures);

                if (previewSession != null)
                {
                    var previewObject =
                        previewSession
                            .PreviewObject;
                    previewSession.Dispose();

                    Expect(
                        previewSession
                            .PreviewObject ==
                            null &&
                        previewSession
                            .PreviewRenderer ==
                            null &&
                        previewObject ==
                            null,
                        "disposing a skinned rebind preview must destroy the temporary preview object",
                        failures);
                }

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

                Expect(
                    !P12SkinnedRebindPreview
                        .TryCreate(
                            renderer,
                            targetRoot.transform,
                            missingReport,
                            out _,
                            out var incompatiblePreviewError) &&
                    incompatiblePreviewError !=
                        null,
                    "rebind preview must reject a structurally incompatible report",
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

        private static void RunTransitionDependencyGraphChecks(
            List<string> failures)
        {
            var transition =
                new AppearanceTransitionPreset
                {
                    Id =
                        "dependency-authoring",
                    DurationSeconds =
                        1.0,
                    Steps =
                        new[]
                        {
                            new AppearanceTransitionStep
                            {
                                TimeSeconds =
                                    0.0,
                                StepId =
                                    "motion",
                                AuthoringLabel =
                                    "Spin Motion",
                                AuthoringGroup =
                                    "Wardrobe Swap",
                                Kind =
                                    AppearanceTransitionStepKind
                                        .Action,
                                ActionType =
                                    "motion.play"
                            },
                            new AppearanceTransitionStep
                            {
                                TimeSeconds =
                                    0.1,
                                StepId =
                                    "effect",
                                Kind =
                                    AppearanceTransitionStepKind
                                        .Action,
                                ActionType =
                                    "effect.play"
                            },
                            new AppearanceTransitionStep
                            {
                                TimeSeconds =
                                    0.2,
                                AuthoringLabel =
                                    "Commit Outfit",
                                AuthoringGroup =
                                    "Wardrobe Swap",
                                Kind =
                                    AppearanceTransitionStepKind
                                        .Commit
                            }
                        }
                };

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryAddDependency(
                        transition,
                        0,
                        2,
                        AppearanceTransitionDependencyMode
                            .All,
                        out var addAllError) &&
                transition.Steps[2]
                    .DependencyMode ==
                    AppearanceTransitionDependencyMode
                        .All &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    1 &&
                transition.Steps[2]
                    .DependsOnStepIds[0] ==
                    "motion",
                "P12 dependency graph authoring must add an earlier Action StepId to a later commit target: " +
                addAllError,
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryAddDependency(
                        transition,
                        1,
                        2,
                        AppearanceTransitionDependencyMode
                            .Any,
                        out var addAnyError) &&
                transition.Steps[2]
                    .DependencyMode ==
                    AppearanceTransitionDependencyMode
                        .Any &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    2 &&
                transition.Steps[2]
                    .DependsOnStepIds[0] ==
                    "motion" &&
                transition.Steps[2]
                    .DependsOnStepIds[1] ==
                    "effect",
                "P12 dependency graph authoring must preserve existing edges while switching the target group to Any: " +
                addAnyError,
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .HasDependency(
                        transition,
                        0,
                        2) &&
                P12TransitionDependencyAuthoringUtility
                    .HasDependency(
                        transition,
                        1,
                        2),
                "P12 dependency graph authoring must report existing source-to-target edges",
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryAssignDependencyGroup(
                        transition,
                        2,
                        "Wardrobe Swap",
                        includeSources:
                            true,
                        out var groupedCount,
                        out var groupError) &&
                groupedCount ==
                    3 &&
                transition.Steps[0]
                    .AuthoringGroup ==
                    "Wardrobe Swap" &&
                transition.Steps[1]
                    .AuthoringGroup ==
                    "Wardrobe Swap" &&
                transition.Steps[2]
                    .AuthoringGroup ==
                    "Wardrobe Swap" &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    2,
                "P12 dependency cluster grouping must label the target and all connected sources without changing dependency edges: " +
                groupError,
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryAssignDependencyGroup(
                        transition,
                        2,
                        string.Empty,
                        includeSources:
                            true,
                        out var clearedGroupCount,
                        out var clearGroupError) &&
                clearedGroupCount ==
                    3 &&
                transition.Steps[0]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[1]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[2]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    2,
                "P12 dependency cluster group clearing must remove only authoring metadata and preserve dependency edges: " +
                clearGroupError,
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryRemoveDependency(
                        transition,
                        0,
                        2,
                        out var removeFirstError) &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    1 &&
                transition.Steps[2]
                    .DependencyMode ==
                    AppearanceTransitionDependencyMode
                        .Any,
                "removing one dependency must preserve the target mode while other edges remain: " +
                removeFirstError,
                failures);

            Expect(
                P12TransitionDependencyAuthoringUtility
                    .TryRemoveDependency(
                        transition,
                        1,
                        2,
                        out var removeLastError) &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    0 &&
                transition.Steps[2]
                    .DependencyMode ==
                    AppearanceTransitionDependencyMode
                        .None,
                "removing the final dependency must reset the target mode to None: " +
                removeLastError,
                failures);

            Expect(
                !P12TransitionDependencyAuthoringUtility
                    .TryAddDependency(
                        transition,
                        2,
                        1,
                        AppearanceTransitionDependencyMode
                            .All,
                        out var forwardError) &&
                forwardError != null &&
                forwardError.IndexOf(
                    "earlier",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 dependency graph authoring must reject forward/backward-invalid endpoint ordering",
                failures);

            var missingId =
                transition.Steps[0].StepId;
            transition.Steps[0].StepId =
                string.Empty;

            Expect(
                !P12TransitionDependencyAuthoringUtility
                    .TryAddDependency(
                        transition,
                        0,
                        2,
                        AppearanceTransitionDependencyMode
                            .All,
                        out var missingIdError) &&
                missingIdError != null &&
                missingIdError.IndexOf(
                    "StepId",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 dependency graph authoring must reject source Actions without StepId",
                failures);

            transition.Steps[0].StepId =
                missingId;

            var cloned =
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .CloneTransition(
                        transition);

            Expect(
                cloned != null &&
                cloned.Steps.Length ==
                    transition.Steps.Length &&
                cloned.Steps[0].AuthoringLabel ==
                    "Spin Motion" &&
                cloned.Steps[0].AuthoringGroup ==
                    "Wardrobe Swap" &&
                cloned.Steps[2].AuthoringLabel ==
                    "Commit Outfit" &&
                cloned.Steps[2].AuthoringGroup ==
                    "Wardrobe Swap",
                "P12 transition graph labels/groups must survive the existing transition package clone path",
                failures);

            var metadataPackage =
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .CreatePackage(
                        "p12-authoring-metadata",
                        new[]
                        {
                            transition
                        });
            string metadataJson = null;
            string metadataSerializeError = null;
            string metadataParseError = null;
            AppearanceTransitionPackage
                metadataRoundTrip = null;

            var metadataSerialized =
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .TrySerialize(
                        metadataPackage,
                        out metadataJson,
                        out metadataSerializeError);
            var metadataParsed =
                metadataSerialized &&
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .TryDeserialize(
                        metadataJson,
                        out metadataRoundTrip,
                        out metadataParseError);

            Expect(
                metadataParsed &&
                metadataRoundTrip != null &&
                metadataRoundTrip.Version ==
                    AppearanceTransitionPackage
                        .CurrentVersion &&
                metadataRoundTrip.Transitions.Length ==
                    1 &&
                metadataRoundTrip.Transitions[0]
                    .Steps[0]
                    .AuthoringLabel ==
                    "Spin Motion" &&
                metadataRoundTrip.Transitions[0]
                    .Steps[0]
                    .AuthoringGroup ==
                    "Wardrobe Swap",
                "P12 transition package v3 JSON must preserve graph authoring labels/groups: " +
                metadataSerializeError +
                " / " +
                metadataParseError,
                failures);

            var v2Package =
                new AppearanceTransitionPackage
                {
                    Version = 2,
                    PackageId =
                        "legacy-v2",
                    Transitions =
                        new[]
                        {
                            new AppearanceTransitionPreset
                            {
                                Id =
                                    "legacy-v2-transition",
                                DurationSeconds =
                                    0.5,
                                Steps =
                                    new[]
                                    {
                                        new AppearanceTransitionStep
                                        {
                                            TimeSeconds =
                                                0.25,
                                            Kind =
                                                AppearanceTransitionStepKind
                                                    .Commit,
                                            AuthoringLabel =
                                                null,
                                            AuthoringGroup =
                                                null
                                        }
                                    }
                            }
                        }
                };

            Expect(
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .Validate(
                        v2Package,
                        out var v2MigrationError) &&
                v2Package.Version ==
                    AppearanceTransitionPackage
                        .CurrentVersion &&
                v2Package.Transitions[0]
                    .Steps[0]
                    .AuthoringLabel ==
                    string.Empty &&
                v2Package.Transitions[0]
                    .Steps[0]
                    .AuthoringGroup ==
                    string.Empty,
                "P12 transition package v2 must migrate to v3 authoring metadata defaults: " +
                v2MigrationError,
                failures);
        }

        private static void RunEventNodeAuthoringChecks(
            List<string> failures)
        {
            var valid =
                new EventRuntimeRule
                {
                    Id =
                        "donation-thanks",
                    Enabled =
                        true,
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                "donation",
                            RequireAmount =
                                true,
                            HasMinimumAmount =
                                true,
                            MinimumAmount =
                                1000.0
                        },
                    Conditions =
                        new[]
                        {
                            new EventStateCondition
                            {
                                Kind =
                                    EventStateConditionKind
                                        .Missing,
                                Key =
                                    "busy"
                            }
                        },
                    StateMutations =
                        new[]
                        {
                            new EventStateMutation
                            {
                                Kind =
                                    EventStateMutationKind
                                        .SetText,
                                Key =
                                    "last.donor",
                                TextSource =
                                    EventTextValueSource
                                        .EventActorName
                            }
                        },
                    Actions =
                        new[]
                        {
                            new EventActionTemplate
                            {
                                ActionType =
                                    "expression.set",
                                Name =
                                    "Joy",
                                HasValue =
                                    true,
                                ConstantNumber =
                                    1.0
                            }
                        },
                    CooldownSeconds =
                        0.5,
                    RateLimitWindowSeconds =
                        10.0,
                    RateLimitMaxExecutions =
                        5
                };

            Expect(
                P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            valid
                        },
                        out var validError),
                "P12 event graph must accept a complete rule matching the existing runtime contract: " +
                validError,
                failures);

            var duplicate =
                new EventRuntimeRule
                {
                    Id =
                        valid.Id
                };

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            valid,
                            duplicate
                        },
                        out var duplicateError) &&
                duplicateError != null &&
                duplicateError.IndexOf(
                    "Duplicate",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 event graph must reject duplicate rule ids",
                failures);

            var invalidRange =
                CloneRule(
                    valid);
            invalidRange.Filter.HasMaximumAmount =
                true;
            invalidRange.Filter.MaximumAmount =
                100.0;

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            invalidRange
                        },
                        out var rangeError) &&
                !string.IsNullOrWhiteSpace(
                    rangeError),
                "P12 event graph must reject minimum amount greater than maximum amount",
                failures);

            var halfRateLimit =
                CloneRule(
                    valid);
            halfRateLimit.RateLimitWindowSeconds =
                10.0;
            halfRateLimit.RateLimitMaxExecutions =
                0;

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            halfRateLimit
                        },
                        out var rateError) &&
                !string.IsNullOrWhiteSpace(
                    rateError),
                "P12 event graph must reject incomplete rate-limit configuration",
                failures);

            var invalidCondition =
                CloneRule(
                    valid);
            invalidCondition.Conditions[0].Key =
                " ";

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            invalidCondition
                        },
                        out var conditionError) &&
                !string.IsNullOrWhiteSpace(
                    conditionError),
                "P12 event graph must reject a condition without a state key",
                failures);

            var invalidAction =
                CloneRule(
                    valid);
            invalidAction.Actions[0].ActionType =
                string.Empty;

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            invalidAction
                        },
                        out var actionError) &&
                !string.IsNullOrWhiteSpace(
                    actionError),
                "P12 event graph must reject an action node without ActionType",
                failures);

            var nonFinite =
                CloneRule(
                    valid);
            nonFinite.Actions[0].NumericScale =
                double.NaN;

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            nonFinite
                        },
                        out var finiteError) &&
                !string.IsNullOrWhiteSpace(
                    finiteError),
                "P12 event graph must reject non-finite action numeric values",
                failures);

            var unique =
                P12EventRuleAuthoringUtility
                    .BuildUniqueRuleId(
                        "rule",
                        candidate =>
                            candidate ==
                                "rule" ||
                            candidate ==
                                "rule-2");

            Expect(
                unique ==
                    "rule-3",
                "P12 event graph must generate deterministic suffix ids",
                failures);

            foreach (P12BuiltInEventRuleTemplate
                     templateKind in
                     Enum.GetValues(
                         typeof(
                             P12BuiltInEventRuleTemplate)))
            {
                var template =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            templateKind);

                Expect(
                    P12EventRuleAuthoringUtility
                        .TryValidateRules(
                            new[]
                            {
                                template
                            },
                            out var templateError),
                    $"P12 built-in event template '{templateKind}' must satisfy the existing runtime rule contract: " +
                    templateError,
                    failures);
            }

            var manualTemplate =
                P12EventRuleLibraryUtility
                    .CreateTemplate(
                        P12BuiltInEventRuleTemplate
                            .ManualRestoreDefault);

            P12EventRuleLibraryPackage
                libraryPackage = null;
            P12EventRuleLibraryPackage
                roundTripPackage = null;
            string packageError = null;
            string serializeError = null;
            string parseError = null;
            string packageJson = null;

            var packageCreated =
                P12EventRuleLibraryUtility
                    .TryCreatePackage(
                        "validation-library",
                        new[]
                        {
                            valid,
                            manualTemplate
                        },
                        out libraryPackage,
                        out packageError);
            var packageSerialized =
                packageCreated &&
                P12EventRuleLibraryUtility
                    .TrySerialize(
                        libraryPackage,
                        out packageJson,
                        out serializeError);
            var packageParsed =
                packageSerialized &&
                P12EventRuleLibraryUtility
                    .TryParse(
                        packageJson,
                        out roundTripPackage,
                        out parseError);

            Expect(
                packageParsed &&
                roundTripPackage != null &&
                roundTripPackage.Rules.Length ==
                    2 &&
                roundTripPackage.Rules[0].Id ==
                    "donation-thanks" &&
                roundTripPackage.Rules[1].Id ==
                    "manual-restore-default",
                "P12 event rule library JSON must preserve package id and ordered rules: " +
                packageError +
                " / " +
                serializeError +
                " / " +
                parseError,
                failures);

            var collisionMerge =
                P12EventRuleLibraryUtility
                    .MergeRules(
                        new[]
                        {
                            manualTemplate
                        },
                        new[]
                        {
                            manualTemplate,
                            valid
                        });

            Expect(
                collisionMerge.Length ==
                    3 &&
                collisionMerge[0].Id ==
                    "manual-restore-default" &&
                collisionMerge[1].Id ==
                    "manual-restore-default-2" &&
                collisionMerge[2].Id ==
                    "donation-thanks" &&
                P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        collisionMerge,
                        out var collisionError),
                "P12 event rule library merge must suffix colliding rule ids deterministically without invalidating rules: " +
                collisionError,
                failures);

            var newerPackage =
                new P12EventRuleLibraryPackage
                {
                    Version =
                        P12EventRuleLibraryPackage
                            .CurrentVersion +
                        1,
                    PackageId =
                        "future",
                    Rules =
                        Array.Empty<
                            EventRuntimeRule>()
                };

            Expect(
                !P12EventRuleLibraryUtility
                    .TryValidatePackage(
                        newerPackage,
                        out var newerPackageError) &&
                newerPackageError != null &&
                newerPackageError.IndexOf(
                    "newer",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 event rule library must reject unsupported newer package versions",
                failures);

            GameObject hostRoot = null;

            try
            {
                hostRoot =
                    new GameObject(
                        "P12 Event Node Validation");
                var host =
                    hostRoot.AddComponent<
                        EventRuntimeHost>();
                host.SetRules(
                    valid);

                var serialized =
                    new SerializedObject(
                        host);
                var rules =
                    serialized.FindProperty(
                        "rules");
                var first =
                    rules != null &&
                    rules.arraySize >
                        0
                        ? rules.GetArrayElementAtIndex(
                            0)
                        : null;

                Expect(
                    rules != null &&
                    first != null &&
                    first.FindPropertyRelative(
                        "Filter") !=
                        null &&
                    first.FindPropertyRelative(
                        "Conditions") !=
                        null &&
                    first.FindPropertyRelative(
                        "StateMutations") !=
                        null &&
                    first.FindPropertyRelative(
                        "Actions") !=
                        null &&
                    first.FindPropertyRelative(
                        "CooldownSeconds") !=
                        null &&
                    first.FindPropertyRelative(
                        "RateLimitWindowSeconds") !=
                        null &&
                    first.FindPropertyRelative(
                        "RateLimitMaxExecutions") !=
                        null,
                    "P12 event node editor SerializedProperty contract must match EventRuntimeHost/EventRuntimeRule fields",
                    failures);
            }
            finally
            {
                if (hostRoot != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            hostRoot);
                }
            }
        }

        private static void RunEventRuleLibraryBrowserChecks(
            List<string> failures)
        {
            const string folder =
                "Assets/VCR/Editor/P12/__EventRuleLibraryValidation";

            try
            {
                if (AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                }

                Expect(
                    P12EventRuleLibraryBrowserUtility
                        .TryEnsureFolder(
                            folder,
                            out var folderError),
                    "P12 event rule library browser must create an Assets-scoped library folder: " +
                    folderError,
                    failures);

                var template =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            P12BuiltInEventRuleTemplate
                                .ManualRestoreDefault);
                var donation =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            P12BuiltInEventRuleTemplate
                                .DonationEffect);

                Expect(
                    P12EventRuleLibraryUtility
                        .TryCreatePackage(
                            "browser-validation",
                            new[]
                            {
                                template,
                                donation
                            },
                            out var package,
                            out var packageError) &&
                    P12EventRuleLibraryUtility
                        .TrySerialize(
                            package,
                            out var json,
                            out var serializeError),
                    "P12 event rule library browser validation package must serialize: " +
                    packageError +
                    " / " +
                    serializeError,
                    failures);

                var projectRoot =
                    Directory.GetParent(
                            Application.dataPath)
                        ?.FullName;

                if (string.IsNullOrWhiteSpace(
                        projectRoot))
                {
                    failures.Add(
                        "P12 event rule library validation could not resolve Unity project root");
                    return;
                }

                var absoluteFolder =
                    Path.Combine(
                        projectRoot,
                        folder.Replace(
                            '/',
                            Path.DirectorySeparatorChar));
                var validPath =
                    folder +
                    "/valid.json";
                var invalidPath =
                    folder +
                    "/future.json";

                File.WriteAllText(
                    Path.Combine(
                        absoluteFolder,
                        "valid.json"),
                    json);
                File.WriteAllText(
                    Path.Combine(
                        absoluteFolder,
                        "future.json"),
@"{
  ""Version"": 999,
  ""PackageId"": ""future-library"",
  ""Rules"": []
}");

                AssetDatabase.ImportAsset(
                    validPath,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);
                AssetDatabase.ImportAsset(
                    invalidPath,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);

                Expect(
                    P12EventRuleLibraryBrowserUtility
                        .TryScan(
                            folder,
                            out var entries,
                            out var scanError) &&
                    entries.Length ==
                        2,
                    "P12 event rule library browser must index valid and invalid project JSON files: " +
                    scanError,
                    failures);

                P12EventRuleLibraryEntry valid =
                    null;
                P12EventRuleLibraryEntry invalid =
                    null;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        if (entry != null &&
                            entry.Valid)
                        {
                            valid =
                                entry;
                        }
                        else if (entry != null)
                        {
                            invalid =
                                entry;
                        }
                    }
                }

                Expect(
                    valid != null &&
                    valid.PackageId ==
                        "browser-validation" &&
                    valid.RuleIds.Length ==
                        2 &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "manual-restore-default") &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "browser-validation"),
                    "P12 event rule library browser must expose package/rule ids to search",
                    failures);

                Expect(
                    invalid != null &&
                    !invalid.Valid &&
                    invalid.PackageId ==
                        "future-library" &&
                    !string.IsNullOrWhiteSpace(
                        invalid.Error),
                    "P12 event rule library browser must retain invalid/future packages for diagnostics",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 event rule library browser validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                    AssetDatabase.Refresh();
                }
            }
        }

        private static EventRuntimeRule CloneRule(
            EventRuntimeRule source)
        {
            var envelope =
                new EventRuntimeConfigurationEnvelope
                {
                    Version = 1,
                    Rules =
                        new[]
                        {
                            source
                        }
                };

            return JsonUtility.FromJson<
                    EventRuntimeConfigurationEnvelope>(
                    JsonUtility.ToJson(
                        envelope))
                .Rules[0];
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
