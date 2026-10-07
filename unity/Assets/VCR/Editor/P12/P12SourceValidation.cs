using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P11;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Environment.Unity;

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
            RunPropActionChecks(
                failures);
            RunSceneAutomationAuthoringChecks(
                failures);
            RunEffectPresetChecks(
                failures);
            RunSceneSequenceChecks(
                failures);
            RunSceneSequenceHandlerLifetimeChecks(
                failures);
            RunEventRuleLibraryBrowserChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P12 source validation: PASS " +
                    "(appearance authoring/anchors/packages/preset preview, skinned structural compatibility, transition dependency graph authoring, event-node grouping/library revision workflows, prop/effect automation including project-local effect presets, and serialized contracts)");
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
                P11TransitionDependencyAuthoringUtility
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
                P11TransitionDependencyAuthoringUtility
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
                P11TransitionDependencyAuthoringUtility
                    .HasDependency(
                        transition,
                        0,
                        2) &&
                P11TransitionDependencyAuthoringUtility
                    .HasDependency(
                        transition,
                        1,
                        2),
                "P12 dependency graph authoring must report existing source-to-target edges",
                failures);

            Expect(
                P11TransitionDependencyAuthoringUtility
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
                P11TransitionDependencyAuthoringUtility
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

            transition.Steps[0]
                .AuthoringGroup =
                    "wardrobe/change/spin";
            transition.Steps[1]
                .AuthoringGroup =
                    "wardrobe/change/effects";
            transition.Steps[2]
                .AuthoringGroup =
                    "wardrobe/change";

            var hierarchyPaths =
                P11TransitionDependencyAuthoringUtility
                    .CaptureGroupPaths(
                        transition);

            Expect(
                Array.IndexOf(
                    hierarchyPaths,
                    "wardrobe") >= 0 &&
                Array.IndexOf(
                    hierarchyPaths,
                    "wardrobe/change") >= 0 &&
                Array.IndexOf(
                    hierarchyPaths,
                    "wardrobe/change/spin") >= 0 &&
                Array.IndexOf(
                    hierarchyPaths,
                    "wardrobe/change/effects") >= 0 &&
                P11TransitionDependencyAuthoringUtility
                    .TryValidateGroupMetadata(
                        transition,
                        out var validHierarchyError),
                "P12 graph group hierarchy capture must include implicit parent paths and valid nested metadata: " +
                validHierarchyError,
                failures);

            Expect(
                P11TransitionDependencyAuthoringUtility
                    .TryRewriteGroupHierarchy(
                        transition,
                        " wardrobe / change ",
                        "show/wardrobe",
                        includeDescendants:
                            true,
                        out var rewrittenGroupCount,
                        out var rewriteGroupError) &&
                rewrittenGroupCount ==
                    3 &&
                transition.Steps[0]
                    .AuthoringGroup ==
                    "show/wardrobe/spin" &&
                transition.Steps[1]
                    .AuthoringGroup ==
                    "show/wardrobe/effects" &&
                transition.Steps[2]
                    .AuthoringGroup ==
                    "show/wardrobe" &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    2,
                "P12 graph group hierarchy rewrite must move the selected parent and descendants without mutating dependency edges: " +
                rewriteGroupError,
                failures);

            Expect(
                !P11TransitionDependencyAuthoringUtility
                    .TryRewriteGroupHierarchy(
                        transition,
                        "show/wardrobe",
                        "show/wardrobe/nested",
                        includeDescendants:
                            true,
                        out _,
                        out var selfNestedGroupError) &&
                !string.IsNullOrWhiteSpace(
                    selfNestedGroupError),
                "P12 graph group hierarchy must reject moving a parent inside its own descendant path",
                failures);

            Expect(
                P11TransitionDependencyAuthoringUtility
                    .TryClearGroupHierarchy(
                        transition,
                        "show/wardrobe",
                        includeDescendants:
                            false,
                        out var exactClearCount,
                        out var exactClearError) &&
                exactClearCount ==
                    1 &&
                transition.Steps[2]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[0]
                    .AuthoringGroup ==
                    "show/wardrobe/spin" &&
                transition.Steps[1]
                    .AuthoringGroup ==
                    "show/wardrobe/effects",
                "P12 graph group hierarchy exact clear must leave child groups intact: " +
                exactClearError,
                failures);

            Expect(
                P11TransitionDependencyAuthoringUtility
                    .TryClearGroupHierarchy(
                        transition,
                        "show/wardrobe",
                        includeDescendants:
                            true,
                        out var descendantClearCount,
                        out var descendantClearError) &&
                descendantClearCount ==
                    2 &&
                transition.Steps[0]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[1]
                    .AuthoringGroup ==
                    string.Empty &&
                transition.Steps[2]
                    .DependsOnStepIds.Length ==
                    2,
                "P12 graph group hierarchy descendant clear must remove only authoring metadata and preserve dependency edges: " +
                descendantClearError,
                failures);

            transition.Steps[0]
                .AuthoringGroup =
                    "bad//path";

            Expect(
                !P11TransitionDependencyAuthoringUtility
                    .TryValidateGroupMetadata(
                        transition,
                        out var invalidGroupPathError) &&
                invalidGroupPathError != null &&
                invalidGroupPathError.IndexOf(
                    "empty",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 graph group metadata validation must reject empty hierarchy segments",
                failures);

            transition.Steps[0]
                .AuthoringGroup =
                    string.Empty;

            Expect(
                P11TransitionDependencyAuthoringUtility
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
                P11TransitionDependencyAuthoringUtility
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
                !P11TransitionDependencyAuthoringUtility
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
                !P11TransitionDependencyAuthoringUtility
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
            transition.Steps[0]
                .AuthoringGroup =
                    "Wardrobe Swap";
            transition.Steps[1]
                .AuthoringGroup =
                    string.Empty;
            transition.Steps[2]
                .AuthoringGroup =
                    "Wardrobe Swap";

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

            var invalidGroupPackage =
                VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .CreatePackage(
                        "invalid-group-path",
                        new[]
                        {
                            transition
                        });
            invalidGroupPackage.Transitions[0]
                .Steps[0]
                .AuthoringGroup =
                    "wardrobe//change";

            Expect(
                !VCR.Editor.P11
                    .P11AppearanceTransitionPackageUtility
                    .Validate(
                        invalidGroupPackage,
                        out var invalidPackageGroupError) &&
                invalidPackageGroupError != null &&
                invalidPackageGroupError.IndexOf(
                    "graph",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 transition package validation must reject malformed graph group hierarchy paths: " +
                invalidPackageGroupError,
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
                    GraphLabel =
                        "Donation Thanks",
                    GraphGroup =
                        "broadcast-reactions",
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

            var groupedSecond =
                CloneRule(
                    valid);
            groupedSecond.Id =
                "donation-thanks-secondary";
            groupedSecond.GraphLabel =
                "Secondary Thanks";
            groupedSecond.Enabled =
                false;
            var groupSummaries =
                P12EventRuleAuthoringUtility
                    .CaptureGroupSummaries(
                        new[]
                        {
                            valid,
                            groupedSecond
                        });

            Expect(
                groupSummaries.Length ==
                    1 &&
                groupSummaries[0].Group ==
                    "broadcast-reactions" &&
                groupSummaries[0].RuleIds.Length ==
                    2 &&
                groupSummaries[0].RuleIds[0] ==
                    "donation-thanks" &&
                groupSummaries[0].RuleIds[1] ==
                    "donation-thanks-secondary" &&
                groupSummaries[0].EnabledCount ==
                    1,
                "P12 event graph grouping must summarize ordered rule ids and enabled counts without changing runtime semantics",
                failures);

            Expect(
                P12EventRuleAuthoringUtility
                    .FindAdjacentRuleIndexInGroup(
                        new[]
                        {
                            valid,
                            groupedSecond
                        },
                        0,
                        1) ==
                    1 &&
                P12EventRuleAuthoringUtility
                    .FindAdjacentRuleIndexInGroup(
                        new[]
                        {
                            valid,
                            groupedSecond
                        },
                        1,
                        1) ==
                    0,
                "P12 event graph grouping must navigate within a group deterministically with wraparound",
                failures);

            var groupEnableRules =
                new[]
                {
                    CloneRule(
                        valid),
                    CloneRule(
                        groupedSecond)
                };
            var enabledChanges =
                P12EventRuleAuthoringUtility
                    .SetGroupEnabled(
                        groupEnableRules,
                        "broadcast-reactions",
                        true);
            var disabledChanges =
                P12EventRuleAuthoringUtility
                    .SetGroupEnabled(
                        groupEnableRules,
                        "broadcast-reactions",
                        false);

            Expect(
                enabledChanges ==
                    1 &&
                disabledChanges ==
                    2 &&
                !groupEnableRules[0].Enabled &&
                !groupEnableRules[1].Enabled,
                "P12 event graph group enable/disable must mutate only matching grouped rules and report changed counts",
                failures);

            var hierarchyHigh =
                CloneRule(
                    valid);
            hierarchyHigh.Id =
                "donation-high";
            hierarchyHigh.GraphGroup =
                "broadcast/donation/high";
            hierarchyHigh.Enabled =
                true;

            var hierarchyLow =
                CloneRule(
                    valid);
            hierarchyLow.Id =
                "donation-low";
            hierarchyLow.GraphGroup =
                "broadcast/donation/low";
            hierarchyLow.Enabled =
                false;

            var hierarchyRoot =
                CloneRule(
                    valid);
            hierarchyRoot.Id =
                "donation-root";
            hierarchyRoot.GraphGroup =
                "broadcast/donation";
            hierarchyRoot.Enabled =
                true;

            var hierarchyRules =
                new[]
                {
                    hierarchyHigh,
                    hierarchyLow,
                    hierarchyRoot
                };
            var eventGroupPaths =
                P12EventRuleAuthoringUtility
                    .CaptureGroupPaths(
                        hierarchyRules);

            Expect(
                Array.IndexOf(
                    eventGroupPaths,
                    "broadcast") >= 0 &&
                Array.IndexOf(
                    eventGroupPaths,
                    "broadcast/donation") >= 0 &&
                Array.IndexOf(
                    eventGroupPaths,
                    "broadcast/donation/high") >= 0 &&
                Array.IndexOf(
                    eventGroupPaths,
                    "broadcast/donation/low") >= 0,
                "P12 event rule group hierarchy must expose implicit parent paths",
                failures);

            var disabledHierarchy =
                P12EventRuleAuthoringUtility
                    .SetGroupEnabled(
                        hierarchyRules,
                        "broadcast/donation",
                        false,
                        includeDescendants:
                            true);

            Expect(
                disabledHierarchy ==
                    2 &&
                !hierarchyRules[0].Enabled &&
                !hierarchyRules[1].Enabled &&
                !hierarchyRules[2].Enabled,
                "P12 event rule group hierarchy enable/disable must include descendants only when requested",
                failures);

            Expect(
                P12EventRuleAuthoringUtility
                    .TryRewriteGroupHierarchy(
                        hierarchyRules,
                        " broadcast / donation ",
                        "audience/support",
                        includeDescendants:
                            true,
                        out var rewrittenRuleGroups,
                        out var rewriteRuleGroupError) &&
                rewrittenRuleGroups ==
                    3 &&
                hierarchyRules[0]
                    .GraphGroup ==
                    "audience/support/high" &&
                hierarchyRules[1]
                    .GraphGroup ==
                    "audience/support/low" &&
                hierarchyRules[2]
                    .GraphGroup ==
                    "audience/support",
                "P12 event rule group hierarchy rewrite must preserve descendant suffixes: " +
                rewriteRuleGroupError,
                failures);

            var capturedHierarchyRules =
                P12EventRuleAuthoringUtility
                    .CaptureGroupHierarchyRules(
                        hierarchyRules,
                        "audience/support",
                        includeDescendants:
                            true);

            Expect(
                capturedHierarchyRules.Length ==
                    3 &&
                capturedHierarchyRules[0].Id ==
                    "donation-high" &&
                capturedHierarchyRules[1].Id ==
                    "donation-low" &&
                capturedHierarchyRules[2].Id ==
                    "donation-root",
                "P12 event rule hierarchy capture must preserve authored rule order for composition/export",
                failures);

            Expect(
                P12EventRuleAuthoringUtility
                    .TryDuplicateGroupHierarchy(
                        hierarchyRules,
                        "audience/support",
                        "library/support-copy",
                        includeDescendants:
                            true,
                        out var duplicatedHierarchyRules,
                        out var duplicatedHierarchyCount,
                        out var duplicateHierarchyError) &&
                duplicatedHierarchyCount ==
                    3 &&
                duplicatedHierarchyRules.Length ==
                    6 &&
                duplicatedHierarchyRules[0].Id ==
                    "donation-high" &&
                duplicatedHierarchyRules[3].Id ==
                    "donation-high-copy" &&
                duplicatedHierarchyRules[3]
                    .GraphGroup ==
                    "library/support-copy/high" &&
                duplicatedHierarchyRules[4].Id ==
                    "donation-low-copy" &&
                duplicatedHierarchyRules[4]
                    .GraphGroup ==
                    "library/support-copy/low" &&
                duplicatedHierarchyRules[5].Id ==
                    "donation-root-copy" &&
                duplicatedHierarchyRules[5]
                    .GraphGroup ==
                    "library/support-copy" &&
                hierarchyRules.Length ==
                    3 &&
                hierarchyRules[0].Id ==
                    "donation-high",
                "P12 event rule hierarchy duplicate must deep-clone the selected subtree, preserve original rules, suffix ids deterministically, and rewrite destination groups: " +
                duplicateHierarchyError,
                failures);

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryRewriteGroupHierarchy(
                        hierarchyRules,
                        "audience/support",
                        "audience/support/nested",
                        includeDescendants:
                            true,
                        out _,
                        out var eventSelfNestError) &&
                !string.IsNullOrWhiteSpace(
                    eventSelfNestError),
                "P12 event rule group hierarchy must reject self-nesting moves",
                failures);

            Expect(
                P12EventRuleAuthoringUtility
                    .TryClearGroupHierarchy(
                        hierarchyRules,
                        "audience/support",
                        includeDescendants:
                            false,
                        out var exactEventClear,
                        out var exactEventClearError) &&
                exactEventClear ==
                    1 &&
                hierarchyRules[2]
                    .GraphGroup ==
                    string.Empty &&
                hierarchyRules[0]
                    .GraphGroup ==
                    "audience/support/high" &&
                hierarchyRules[1]
                    .GraphGroup ==
                    "audience/support/low",
                "P12 event rule group hierarchy exact clear must keep child groups: " +
                exactEventClearError,
                failures);

            Expect(
                P12EventRuleAuthoringUtility
                    .TryClearGroupHierarchy(
                        hierarchyRules,
                        "audience/support",
                        includeDescendants:
                            true,
                        out var recursiveEventClear,
                        out var recursiveEventClearError) &&
                recursiveEventClear ==
                    2 &&
                hierarchyRules[0]
                    .GraphGroup ==
                    string.Empty &&
                hierarchyRules[1]
                    .GraphGroup ==
                    string.Empty,
                "P12 event rule group hierarchy recursive clear must remove descendant authoring metadata: " +
                recursiveEventClearError,
                failures);

            var invalidRuleGroup =
                CloneRule(
                    valid);
            invalidRuleGroup.Id =
                "invalid-group-path";
            invalidRuleGroup.GraphGroup =
                "broadcast//donation";

            Expect(
                !P12EventRuleAuthoringUtility
                    .TryValidateRules(
                        new[]
                        {
                            invalidRuleGroup
                        },
                        out var invalidRuleGroupError) &&
                invalidRuleGroupError != null &&
                invalidRuleGroupError.IndexOf(
                    "group",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 event graph authoring validation must reject malformed nested group paths",
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

            var sceneSequence =
                P12EventRuleLibraryUtility
                    .CreateTemplate(
                        P12BuiltInEventRuleTemplate
                            .ManualSceneSequence);
            var sceneEngine =
                new EventRuntimeEngine();
            var sceneCommands =
                new List<
                    EventActionCommand>();
            sceneEngine.SetRules(
                sceneSequence);
            sceneEngine.Process(
                new VCR.Runtime.Events.NormalizedEvent(
                    VCR.Runtime.Events
                        .NormalizedEventTypes
                        .LocalManual,
                    "validation",
                    1_000_000,
                    sequence:
                        1),
                sceneCommands);

            Expect(
                sceneSequence.GraphGroup ==
                    "scene-automation/manual" &&
                sceneSequence.Actions.Length ==
                    3 &&
                sceneCommands.Count ==
                    3 &&
                sceneCommands[0].ActionType ==
                    EventActionTypes.EnvironmentSetState &&
                sceneCommands[0].Name ==
                    "Fade" &&
                sceneCommands[0].Text ==
                    "state-id" &&
                sceneCommands[0].HasValue &&
                Math.Abs(
                    sceneCommands[0].Value -
                    0.5) <
                    0.0001 &&
                sceneCommands[1].ActionType ==
                    EventActionTypes.PropSetActive &&
                sceneCommands[1].TargetId ==
                    "props.main" &&
                sceneCommands[1].Text ==
                    "prop-id" &&
                sceneCommands[1].HasValue &&
                Math.Abs(
                    sceneCommands[1].Value -
                    1.0) <
                    0.0001 &&
                sceneCommands[2].ActionType ==
                    EventActionTypes.EffectPlay &&
                sceneCommands[2].TargetId ==
                    "effects.main" &&
                sceneCommands[2].Text ==
                    "effect-id",
                "P12 manual scene automation template must emit environment → prop → effect commands in authored order with explicit placeholder ids and activation value",
                failures);

            var donationBurst =
                P12EventRuleLibraryUtility
                    .CreateTemplate(
                        P12BuiltInEventRuleTemplate
                            .DonationSceneBurst);

            Expect(
                donationBurst.GraphGroup ==
                    "scene-automation/broadcast" &&
                donationBurst.Filter != null &&
                donationBurst.Filter.Type ==
                    VCR.Runtime.Events
                        .NormalizedEventTypes
                        .BroadcastDonation &&
                donationBurst.Filter
                    .HasMinimumAmount &&
                donationBurst.Actions.Length ==
                    2 &&
                donationBurst.Actions[0]
                    .ActionType ==
                    EventActionTypes.PropSetActive &&
                donationBurst.Actions[0]
                    .HasValue &&
                Math.Abs(
                    donationBurst.Actions[0]
                        .ConstantNumber -
                    1.0) <
                    0.0001 &&
                donationBurst.Actions[1]
                    .ActionType ==
                    EventActionTypes.EffectPlay,
                "P12 donation scene burst template must compose prop activation before effect playback under the broadcast scene-automation group",
                failures);

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

            valid.GraphGroup =
                " broadcast / reactions ";

            var packageCreated =
                P12EventRuleLibraryUtility
                    .TryCreatePackage(
                        "validation-library",
                        "Broadcast reaction examples",
                        new[]
                        {
                            "broadcast",
                            "appearance"
                        },
                        3,
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
                roundTripPackage.Description ==
                    "Broadcast reaction examples" &&
                roundTripPackage.Tags.Length ==
                    2 &&
                roundTripPackage.Tags[0] ==
                    "broadcast" &&
                roundTripPackage.Tags[1] ==
                    "appearance" &&
                roundTripPackage.Revision ==
                    3 &&
                roundTripPackage.Rules[0].Id ==
                    "donation-thanks" &&
                roundTripPackage.Rules[0].GraphLabel ==
                    "Donation Thanks" &&
                roundTripPackage.Rules[0].GraphGroup ==
                    "broadcast/reactions" &&
                roundTripPackage.Rules[1].Id ==
                    "manual-restore-default",
                "P12 event rule library JSON must preserve metadata and ordered rules: " +
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

            var duplicateTags =
                new P12EventRuleLibraryPackage
                {
                    PackageId =
                        "duplicate-tags",
                    Description =
                        "duplicate validation",
                    Tags =
                        new[]
                        {
                            "Broadcast",
                            "broadcast"
                        },
                    Revision = 1,
                    Rules =
                        new[]
                        {
                            manualTemplate
                        }
                };

            Expect(
                !P12EventRuleLibraryUtility
                    .TryValidatePackage(
                        duplicateTags,
                        out var duplicateTagError) &&
                duplicateTagError != null &&
                duplicateTagError.IndexOf(
                    "duplicate tag",
                    StringComparison.OrdinalIgnoreCase) >=
                    0,
                "P12 event rule library metadata must reject duplicate tags case-insensitively",
                failures);

            const string legacyV1Json =
@"{
  ""Version"": 1,
  ""PackageId"": ""legacy-v1-library"",
  ""Rules"": []
}";

            Expect(
                P12EventRuleLibraryUtility
                    .TryParse(
                        legacyV1Json,
                        out var legacyV1Package,
                        out var legacyV1Error) &&
                legacyV1Package.Version ==
                    P12EventRuleLibraryPackage
                        .CurrentVersion &&
                legacyV1Package.Description ==
                    string.Empty &&
                legacyV1Package.Tags.Length ==
                    0 &&
                legacyV1Package.Revision ==
                    1,
                "P12 event rule library v1 must migrate to v2 metadata defaults: " +
                legacyV1Error,
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
                        "GraphLabel") !=
                        null &&
                    first.FindPropertyRelative(
                        "GraphGroup") !=
                        null &&
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

        private static void RunPropActionChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Prop Action Validation");
                var propA =
                    new GameObject(
                        "Prop A");
                var propB =
                    new GameObject(
                        "Prop B");
                propA.transform.SetParent(
                    root.transform,
                    false);
                propB.transform.SetParent(
                    root.transform,
                    false);
                propA.SetActive(
                    false);
                propB.SetActive(
                    false);

                var handler =
                    root.AddComponent<
                        PropEventActionHandler>();
                handler.ConfigureBindings(
                    new PropEventActionHandler
                        .PropBinding
                    {
                        PropId =
                            "desk-lamp",
                        Roots =
                            new[]
                            {
                                propA,
                                propB
                            }
                    });

                Expect(
                    handler.RebuildBindings(
                        out var bindingError),
                    "P12 prop handler must accept one logical prop id mapped to unique scene roots: " +
                    bindingError,
                    failures);

                var setCommand =
                    new EventActionCommand(
                        "prop-validation",
                        EventActionTypes
                            .PropSetActive,
                        "props.main",
                        null,
                        "desk-lamp",
                        1.0,
                        true,
                        1);

                Expect(
                    handler.TryExecute(
                        setCommand,
                        out var setError) &&
                    propA.activeSelf &&
                    propB.activeSelf,
                    "P12 prop.set_active must activate every root in one logical prop binding: " +
                    setError,
                    failures);

                Expect(
                    handler.CanTrackCompletion(
                        setCommand) &&
                    handler.TryIsComplete(
                        setCommand,
                        out var setComplete,
                        out var completionError) &&
                    setComplete,
                    "P12 prop actions must expose immediate completion for transition dependency use: " +
                    completionError,
                    failures);

                var toggleCommand =
                    new EventActionCommand(
                        "prop-validation",
                        EventActionTypes
                            .PropToggle,
                        "props.main",
                        null,
                        "desk-lamp",
                        0.0,
                        false,
                        2);

                Expect(
                    handler.TryExecute(
                        toggleCommand,
                        out var toggleError) &&
                    !propA.activeSelf &&
                    !propB.activeSelf,
                    "P12 prop.toggle must invert a uniformly active/inactive logical prop binding: " +
                    toggleError,
                    failures);

                propA.SetActive(
                    true);
                propB.SetActive(
                    false);

                Expect(
                    !handler.TryExecute(
                        toggleCommand,
                        out var mixedError) &&
                    mixedError != null &&
                    mixedError.IndexOf(
                        "mixed",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 prop.toggle must reject mixed root states instead of choosing an arbitrary toggle direction",
                    failures);

                handler.ConfigureBindings(
                    new PropEventActionHandler
                        .PropBinding
                    {
                        PropId =
                            "prop-one",
                        Roots =
                            new[]
                            {
                                propA
                            }
                    },
                    new PropEventActionHandler
                        .PropBinding
                    {
                        PropId =
                            "prop-two",
                        Roots =
                            new[]
                            {
                                propA
                            }
                    });

                Expect(
                    !handler.RebuildBindings(
                        out var sharedRootError) &&
                    sharedRootError != null &&
                    sharedRootError.IndexOf(
                        "shared",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 prop bindings must reject the same GameObject root being owned by multiple logical prop ids",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 prop automation validation unexpected exception: " +
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

        private static void RunSceneAutomationAuthoringChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Scene Automation Validation");

                var propRoot =
                    new GameObject(
                        "Desk Lamp");
                propRoot.transform.SetParent(
                    root.transform,
                    false);

                var effectRoot =
                    new GameObject(
                        "Sparkle Effect");
                effectRoot.transform.SetParent(
                    root.transform,
                    false);
                var particle =
                    effectRoot.AddComponent<
                        ParticleSystem>();

                var dayRoot =
                    new GameObject(
                        "Day Environment");
                dayRoot.transform.SetParent(
                    root.transform,
                    false);
                var nightRoot =
                    new GameObject(
                        "Night Environment");
                nightRoot.transform.SetParent(
                    root.transform,
                    false);

                var propHandler =
                    root.AddComponent<
                        PropEventActionHandler>();
                propHandler.ConfigureBindings(
                    new PropEventActionHandler
                        .PropBinding
                    {
                        PropId =
                            "desk-lamp",
                        Roots =
                            new[]
                            {
                                propRoot
                            }
                    });

                var effectHandler =
                    root.AddComponent<
                        EffectEventActionHandler>();
                effectHandler.ConfigureBindings(
                    new EffectEventActionHandler
                        .EffectBinding
                    {
                        EffectId =
                            "sparkle",
                        Root =
                            effectRoot,
                        ParticleSystems =
                            new[]
                            {
                                particle
                            },
                        RestartOnPlay =
                            true,
                        DeactivateOnStop =
                            true
                    });

                var environment =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                environment.Configure(
                    "environment.validation",
                    "day");

                var day =
                    new EnvironmentStateBinding();
                day.Configure(
                    "day",
                    dayRoot);
                var night =
                    new EnvironmentStateBinding();
                night.Configure(
                    "night",
                    nightRoot);

                Expect(
                    environment.ConfigureStateBindings(
                        new[]
                        {
                            day,
                            night
                        },
                        out var environmentError),
                    "P12 scene automation validation environment must accept explicit logical state bindings: " +
                    environmentError,
                    failures);

                var propSerialized =
                    new SerializedObject(
                        propHandler);
                var effectSerialized =
                    new SerializedObject(
                        effectHandler);
                var environmentSerialized =
                    new SerializedObject(
                        environment);

                var props =
                    propSerialized.FindProperty(
                        "props");
                var effects =
                    effectSerialized.FindProperty(
                        "effects");
                var states =
                    environmentSerialized.FindProperty(
                        "stateBindings");

                Expect(
                    props != null &&
                    props.arraySize ==
                        1 &&
                    props.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "PropId") !=
                        null &&
                    props.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "Roots") !=
                        null,
                    "P12 Scene Automation Prop SerializedProperty contract must match PropEventActionHandler bindings",
                    failures);

                Expect(
                    effects != null &&
                    effects.arraySize ==
                        1 &&
                    effects.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "EffectId") !=
                        null &&
                    effects.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "Root") !=
                        null &&
                    effects.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "ParticleSystems") !=
                        null &&
                    effects.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "RestartOnPlay") !=
                        null &&
                    effects.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "DeactivateOnStop") !=
                        null,
                    "P12 Scene Automation Effect SerializedProperty contract must match EffectEventActionHandler bindings",
                    failures);

                Expect(
                    states != null &&
                    states.arraySize ==
                        2 &&
                    states.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "stateId") !=
                        null &&
                    states.GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "root") !=
                        null &&
                    environmentSerialized.FindProperty(
                        "environmentId") !=
                        null &&
                    environmentSerialized.FindProperty(
                        "stateId") !=
                        null &&
                    environmentSerialized.FindProperty(
                        "defaultTransitionMode") !=
                        null &&
                    environmentSerialized.FindProperty(
                        "defaultTransitionDuration") !=
                        null,
                    "P12 Scene Automation Environment SerializedProperty contract must expose editable state bindings and transition defaults",
                    failures);

                string environmentRebuildError =
                    null;
                string propRebuildError =
                    null;
                string effectRebuildError =
                    null;
                var environmentRebuilt =
                    environment.RebuildStateBindings(
                        out environmentRebuildError);
                var propsRebuilt =
                    propHandler.RebuildBindings(
                        out propRebuildError);
                var effectsRebuilt =
                    effectHandler.RebuildBindings(
                        out effectRebuildError);

                Expect(
                    environmentRebuilt &&
                    propsRebuilt &&
                    effectsRebuilt,
                    "P12 Scene Automation valid environment/prop/effect registries must rebuild through their runtime validation boundaries: " +
                    environmentRebuildError +
                    " / " +
                    propRebuildError +
                    " / " +
                    effectRebuildError,
                    failures);

                var propSnapshot =
                    EditorJsonUtility.ToJson(
                        propHandler);
                props.InsertArrayElementAtIndex(
                    0);
                propSerialized
                    .ApplyModifiedProperties();

                Expect(
                    !propHandler.RebuildBindings(
                        out var duplicatePropError) &&
                    duplicatePropError != null &&
                    duplicatePropError.IndexOf(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 Scene Automation authoring must reject duplicate logical prop ids",
                    failures);

                EditorJsonUtility.FromJsonOverwrite(
                    propSnapshot,
                    propHandler);
                propSerialized =
                    new SerializedObject(
                        propHandler);

                Expect(
                    propHandler.RebuildBindings(
                        out var restoredPropError),
                    "restoring the last valid prop snapshot must rebuild after failed authoring validation: " +
                    restoredPropError,
                    failures);

                var effectSnapshot =
                    EditorJsonUtility.ToJson(
                        effectHandler);
                effectSerialized.Update();
                effects =
                    effectSerialized.FindProperty(
                        "effects");
                effects.InsertArrayElementAtIndex(
                    0);
                effectSerialized
                    .ApplyModifiedProperties();

                Expect(
                    !effectHandler.RebuildBindings(
                        out var duplicateEffectError) &&
                    duplicateEffectError != null &&
                    duplicateEffectError.IndexOf(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 Scene Automation authoring must reject duplicate logical effect ids",
                    failures);

                EditorJsonUtility.FromJsonOverwrite(
                    effectSnapshot,
                    effectHandler);
                effectSerialized =
                    new SerializedObject(
                        effectHandler);

                Expect(
                    effectHandler.RebuildBindings(
                        out var restoredEffectError),
                    "restoring the last valid effect snapshot must rebuild after failed authoring validation: " +
                    restoredEffectError,
                    failures);

                var environmentSnapshot =
                    EditorJsonUtility.ToJson(
                        environment);
                environmentSerialized.Update();
                states =
                    environmentSerialized.FindProperty(
                        "stateBindings");
                states.GetArrayElementAtIndex(
                        1)
                    .FindPropertyRelative(
                        "stateId")
                    .stringValue =
                        "day";
                environmentSerialized
                    .ApplyModifiedProperties();

                Expect(
                    !environment.RebuildStateBindings(
                        out var duplicateStateError) &&
                    duplicateStateError != null &&
                    duplicateStateError.IndexOf(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 Scene Automation authoring must reject duplicate environment state ids",
                    failures);

                EditorJsonUtility.FromJsonOverwrite(
                    environmentSnapshot,
                    environment);
                environmentSerialized =
                    new SerializedObject(
                        environment);

                Expect(
                    environment.RebuildStateBindings(
                        out var restoredEnvironmentError),
                    "restoring the last valid environment snapshot must rebuild after failed authoring validation: " +
                    restoredEnvironmentError,
                    failures);

                var propTemplate =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            P12BuiltInEventRuleTemplate
                                .ManualPropToggle);
                var environmentTemplate =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            P12BuiltInEventRuleTemplate
                                .ChatEnvironmentState);

                Expect(
                    propTemplate.GraphGroup ==
                        "scene-controls" &&
                    propTemplate.Actions.Length ==
                        1 &&
                    propTemplate.Actions[0]
                        .ActionType ==
                        EventActionTypes
                            .PropToggle &&
                    propTemplate.Actions[0]
                        .ConstantText ==
                        "prop-id",
                    "P12 manual prop starter template must target logical prop ids through prop.toggle",
                    failures);

                Expect(
                    environmentTemplate.GraphGroup ==
                        "scene-controls" &&
                    environmentTemplate.Actions.Length ==
                        1 &&
                    environmentTemplate.Actions[0]
                        .ActionType ==
                        EventActionTypes
                            .EnvironmentSetState &&
                    environmentTemplate.Actions[0]
                        .Name ==
                        "Fade" &&
                    environmentTemplate.Actions[0]
                        .HasValue &&
                    Math.Abs(
                        environmentTemplate.Actions[0]
                            .ConstantNumber -
                        0.5) <
                        0.001 &&
                    environmentTemplate.Actions[0]
                        .ConstantText ==
                        "state-id",
                    "P12 chat environment starter template must author a logical environment.set_state Fade transition",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 scene automation authoring validation unexpected exception: " +
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

        private static void RunEffectPresetChecks(
            List<string> failures)
        {
            const string folder =
                "Assets/VCR/Editor/P12/__EffectPresetValidation";
            const string validPrefabPath =
                folder +
                "/valid-effect.prefab";
            const string emptyPrefabPath =
                folder +
                "/empty-effect.prefab";
            const string unsafePrefabPath =
                folder +
                "/unsafe-effect.prefab";

            GameObject validSource = null;
            GameObject emptySource = null;
            GameObject unsafeSource = null;
            GameObject handlerRoot = null;
            P12EffectPresetAsset preset = null;

            try
            {
                if (AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                }

                EnsureAssetFolder(
                    folder);

                validSource =
                    new GameObject(
                        "Valid Effect Preset");
                validSource.AddComponent<
                    ParticleSystem>();
                var validPrefab =
                    PrefabUtility.SaveAsPrefabAsset(
                        validSource,
                        validPrefabPath);

                emptySource =
                    new GameObject(
                        "Empty Effect Preset");
                var emptyPrefab =
                    PrefabUtility.SaveAsPrefabAsset(
                        emptySource,
                        emptyPrefabPath);

                unsafeSource =
                    new GameObject(
                        "Unsafe Effect Preset");
                unsafeSource.AddComponent<
                    ParticleSystem>();
                unsafeSource.AddComponent<
                    AudioSource>();
                var unsafePrefab =
                    PrefabUtility.SaveAsPrefabAsset(
                        unsafeSource,
                        unsafePrefabPath);

                preset =
                    ScriptableObject.CreateInstance<
                        P12EffectPresetAsset>();
                preset.EffectId =
                    "sparkle";
                preset.Prefab =
                    validPrefab;
                preset.RestartOnPlay =
                    true;
                preset.DeactivateOnStop =
                    true;
                preset.StartInactive =
                    true;

                Expect(
                    P12EffectPresetUtility
                        .TryValidatePreset(
                            preset,
                            out var validError),
                    "project-local ParticleSystem-only effect preset must validate: " +
                    validError,
                    failures);

                Expect(
                    P12EffectPresetUtility
                        .TryCreateFromSceneRoot(
                            validSource,
                            "authored-sparkle",
                            folder,
                            out var authoredPreset,
                            out var authoringError) &&
                    authoredPreset != null &&
                    EditorUtility.IsPersistent(
                        authoredPreset) &&
                    authoredPreset.Prefab != null &&
                    EditorUtility.IsPersistent(
                        authoredPreset.Prefab) &&
                    authoredPreset.EffectId ==
                        "authored-sparkle",
                    "effect preset authoring must convert a valid scene ParticleSystem root into persistent prefab + preset assets: " +
                    authoringError,
                    failures);

                var invalidLibraryPreset =
                    ScriptableObject.CreateInstance<
                        P12EffectPresetAsset>();
                invalidLibraryPreset.EffectId =
                    "invalid-library";
                invalidLibraryPreset.Prefab =
                    emptyPrefab;
                AssetDatabase.CreateAsset(
                    invalidLibraryPreset,
                    folder +
                    "/invalid-library.asset");
                AssetDatabase.SaveAssets();

                Expect(
                    P12EffectPresetLibraryUtility
                        .TryScan(
                            folder,
                            out var libraryEntries,
                            out var libraryError) &&
                    libraryEntries.Length ==
                        2 &&
                    Array.Exists(
                        libraryEntries,
                        entry =>
                            entry != null &&
                            entry.Valid &&
                            entry.EffectId ==
                                "authored-sparkle") &&
                    Array.Exists(
                        libraryEntries,
                        entry =>
                            entry != null &&
                            !entry.Valid &&
                            entry.EffectId ==
                                "invalid-library") &&
                    Array.Exists(
                        libraryEntries,
                        entry =>
                            P12EffectPresetLibraryUtility
                                .MatchesSearch(
                                    entry,
                                    "sparkle")),
                    "effect preset library must index valid and invalid project presets while supporting id/path search: " +
                    libraryError,
                    failures);

                preset.Prefab =
                    emptyPrefab;

                Expect(
                    !P12EffectPresetUtility
                        .TryValidatePreset(
                            preset,
                            out var emptyError) &&
                    !string.IsNullOrWhiteSpace(
                        emptyError),
                    "effect preset must reject a prefab with no ParticleSystem",
                    failures);

                preset.Prefab =
                    unsafePrefab;

                Expect(
                    !P12EffectPresetUtility
                        .TryValidatePreset(
                            preset,
                            out var unsafeError) &&
                    unsafeError != null &&
                    unsafeError.IndexOf(
                        "unsupported component",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "effect preset v1 must reject unsupported prefab components such as AudioSource",
                    failures);

                preset.Prefab =
                    validPrefab;
                preset.EffectId =
                    "../unsafe";

                Expect(
                    !P12EffectPresetUtility
                        .TryValidatePreset(
                            preset,
                            out var unsafeIdError) &&
                    !string.IsNullOrWhiteSpace(
                        unsafeIdError),
                    "effect preset must reject unsafe logical ids",
                    failures);

                preset.EffectId =
                    "sparkle";

                handlerRoot =
                    new GameObject(
                        "Effect Preset Handler");
                var handler =
                    handlerRoot.AddComponent<
                        EffectEventActionHandler>();

                P12EffectPresetInstallResult
                    installed = null;
                string installError = null;
                var installedSuccessfully =
                    P12EffectPresetUtility
                        .TryInstall(
                            preset,
                            handler,
                            out installed,
                            out installError);
                string rebuildError = null;
                var rebuilt =
                    installedSuccessfully &&
                    handler.RebuildBindings(
                        out rebuildError);

                Expect(
                    installedSuccessfully &&
                    installed != null &&
                    installed.Instance !=
                        null &&
                    installed.EffectId ==
                        "sparkle" &&
                    installed.ParticleSystemCount ==
                        1 &&
                    !installed.Instance.activeSelf &&
                    rebuilt,
                    "effect preset install must create one inactive scene instance and register a valid logical effect binding: " +
                    installError +
                    " / " +
                    rebuildError,
                    failures);

                Expect(
                    !P12EffectPresetUtility
                        .TryInstall(
                            preset,
                            handler,
                            out _,
                            out var duplicateError) &&
                    duplicateError != null &&
                    duplicateError.IndexOf(
                        "already registered",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "effect preset install must reject duplicate EffectId registration",
                    failures);

                var serialized =
                    new SerializedObject(
                        handler);
                serialized.Update();
                var effects =
                    serialized.FindProperty(
                        "effects");

                Expect(
                    effects != null &&
                    effects.arraySize ==
                        1 &&
                    effects
                        .GetArrayElementAtIndex(
                            0)
                        .FindPropertyRelative(
                            "EffectId")
                        .stringValue ==
                        "sparkle",
                    "effect preset install must append exactly one serialized EffectEventActionHandler binding",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 effect preset validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (handlerRoot != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            handlerRoot);
                }

                if (validSource != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            validSource);
                }

                if (emptySource != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            emptySource);
                }

                if (unsafeSource != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            unsafeSource);
                }

                if (preset != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            preset);
                }

                if (AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                    AssetDatabase.Refresh();
                }
            }
        }

        private static void RunSceneSequenceChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Scene Sequence Validation");
                var handler =
                    root.AddComponent<
                        SceneSequenceEventActionHandler>();

                var valid =
                    new SceneSequenceEventActionHandler
                        .SequenceBinding
                    {
                        SequenceId =
                            "scene-change",
                        Steps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0f,
                                    ActionType =
                                        EventActionTypes
                                            .EnvironmentSetState,
                                    Name =
                                        "Fade",
                                    Text =
                                        "night",
                                    Value =
                                        0.5,
                                    HasValue =
                                        true
                                },
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0.2f,
                                    ActionType =
                                        EventActionTypes
                                            .PropSetActive,
                                    TargetId =
                                        "props.main",
                                    Text =
                                        "desk-lamp",
                                    Value =
                                        1.0,
                                    HasValue =
                                        true
                                },
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0.35f,
                                    ActionType =
                                        EventActionTypes
                                            .EffectPlay,
                                    TargetId =
                                        "effects.main",
                                    Text =
                                        "sparkle",
                                    Required =
                                        false
                                }
                            },
                        CancellationSteps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0f,
                                    ActionType =
                                        EventActionTypes
                                            .EffectStop,
                                    TargetId =
                                        "effects.main",
                                    Text =
                                        "sparkle",
                                    Required =
                                        false
                                },
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0f,
                                    ActionType =
                                        EventActionTypes
                                            .PropSetActive,
                                    TargetId =
                                        "props.main",
                                    Text =
                                        "desk-lamp",
                                    Value =
                                        0.0,
                                    HasValue =
                                        true,
                                    Required =
                                        false
                                }
                            }
                    };

                handler.ConfigureSequences(
                    valid);

                Expect(
                    handler.RebuildBindings(
                        out var validError),
                    "P12 timed scene sequence must accept ordered logical actions and immediate cancellation cleanup: " +
                    validError,
                    failures);

                var playCommand =
                    new EventActionCommand(
                        "sequence-validation",
                        EventActionTypes
                            .SceneSequencePlay,
                        "scene.sequences",
                        null,
                        "scene-change",
                        0.0,
                        false,
                        1);
                var cancelCommand =
                    new EventActionCommand(
                        "sequence-validation",
                        EventActionTypes
                            .SceneSequenceCancel,
                        "scene.sequences",
                        null,
                        null,
                        0.0,
                        false,
                        2);

                Expect(
                    handler.CanHandle(
                        playCommand) &&
                    handler.CanHandle(
                        cancelCommand) &&
                    handler.CanTrackCompletion(
                        playCommand) &&
                    handler.CanTrackCompletion(
                        cancelCommand),
                    "P12 timed scene sequence handler must expose play/cancel routing and completion-probe contracts",
                    failures);

                handler.ConfigureSequences(
                    valid,
                    new SceneSequenceEventActionHandler
                        .SequenceBinding
                    {
                        SequenceId =
                            "scene-change",
                        Steps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    ActionType =
                                        EventActionTypes
                                            .PropToggle,
                                    Text =
                                        "desk-lamp"
                                }
                            }
                    });

                Expect(
                    !handler.RebuildBindings(
                        out var duplicateError) &&
                    duplicateError != null &&
                    duplicateError.IndexOf(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase) >=
                        0 &&
                    handler.CanTrackCompletion(
                        playCommand),
                    "P12 timed scene sequence must reject duplicate sequence ids without discarding the previously valid runtime registry",
                    failures);

                handler.ConfigureSequences(
                    new SceneSequenceEventActionHandler
                        .SequenceBinding
                    {
                        SequenceId =
                            "backward-time",
                        Steps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        1f,
                                    ActionType =
                                        EventActionTypes
                                            .PropToggle,
                                    Text =
                                        "desk-lamp"
                                },
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0.5f,
                                    ActionType =
                                        EventActionTypes
                                            .EffectPlay,
                                    Text =
                                        "sparkle"
                                }
                            }
                    });

                Expect(
                    !handler.RebuildBindings(
                        out var timeError) &&
                    timeError != null &&
                    timeError.IndexOf(
                        "non-decreasing",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 timed scene sequence must reject decreasing step times",
                    failures);

                handler.ConfigureSequences(
                    new SceneSequenceEventActionHandler
                        .SequenceBinding
                    {
                        SequenceId =
                            "recursive",
                        Steps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    ActionType =
                                        EventActionTypes
                                            .SceneSequencePlay,
                                    Text =
                                        "recursive"
                                }
                            }
                    });

                Expect(
                    !handler.RebuildBindings(
                        out var recursiveError) &&
                    recursiveError != null &&
                    recursiveError.IndexOf(
                        "recursively",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 timed scene sequence must reject recursive scene.sequence actions",
                    failures);

                handler.ConfigureSequences(
                    new SceneSequenceEventActionHandler
                        .SequenceBinding
                    {
                        SequenceId =
                            "cleanup-time",
                        Steps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    ActionType =
                                        EventActionTypes
                                            .PropToggle,
                                    Text =
                                        "desk-lamp"
                                }
                            },
                        CancellationSteps =
                            new[]
                            {
                                new SceneSequenceEventActionHandler
                                    .SequenceStep
                                {
                                    TimeSeconds =
                                        0.1f,
                                    ActionType =
                                        EventActionTypes
                                            .EffectStop,
                                    Text =
                                        "sparkle"
                                }
                            }
                    });

                Expect(
                    !handler.RebuildBindings(
                        out var cleanupTimeError) &&
                    cleanupTimeError != null &&
                    cleanupTimeError.IndexOf(
                        "time 0",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 timed scene sequence cancellation cleanup must be immediate",
                    failures);

                handler.ConfigureSequences(
                    valid);
                var serialized =
                    new SerializedObject(
                        handler);
                var sequences =
                    serialized.FindProperty(
                        "sequences");
                var first =
                    sequences != null &&
                    sequences.arraySize >
                        0
                        ? sequences
                            .GetArrayElementAtIndex(
                                0)
                        : null;
                var firstStep =
                    first != null &&
                    first.FindPropertyRelative(
                            "Steps")
                        .arraySize >
                        0
                        ? first
                            .FindPropertyRelative(
                                "Steps")
                            .GetArrayElementAtIndex(
                                0)
                        : null;

                Expect(
                    first != null &&
                    first.FindPropertyRelative(
                        "SequenceId") !=
                        null &&
                    first.FindPropertyRelative(
                        "Steps") !=
                        null &&
                    first.FindPropertyRelative(
                        "CancellationSteps") !=
                        null &&
                    firstStep != null &&
                    firstStep.FindPropertyRelative(
                        "TimeSeconds") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "ActionType") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "TargetId") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "Name") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "Text") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "Value") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "HasValue") !=
                        null &&
                    firstStep.FindPropertyRelative(
                        "Required") !=
                        null,
                    "P12 Scene Automation timed-sequence SerializedProperty contract must match SceneSequenceEventActionHandler",
                    failures);

                var timedTemplate =
                    P12EventRuleLibraryUtility
                        .CreateTemplate(
                            P12BuiltInEventRuleTemplate
                                .ManualTimedSceneSequence);

                Expect(
                    timedTemplate.Actions.Length ==
                        1 &&
                    timedTemplate.Actions[0]
                        .ActionType ==
                        EventActionTypes
                            .SceneSequencePlay &&
                    timedTemplate.Actions[0]
                        .TargetId ==
                        "scene.sequences" &&
                    timedTemplate.Actions[0]
                        .ConstantText ==
                        "sequence-id",
                    "P12 timed scene sequence starter template must route one logical sequence id through scene.sequence_play",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 timed scene sequence validation unexpected exception: " +
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

        private static void RunSceneSequenceHandlerLifetimeChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Scene Sequence Handler Lifetime");

                var sequenceHandler =
                    root.AddComponent<
                        SceneSequenceEventActionHandler>();
                var oldHandler =
                    root.AddComponent<
                        P12SceneSequenceProbeHandler>();

                sequenceHandler.SetActionHandlers(
                    oldHandler);

                var command =
                    new EventActionCommand(
                        "p12-sequence-lifetime",
                        "p12.sequence.probe",
                        null,
                        null,
                        null,
                        0.0,
                        false,
                        1);

                Expect(
                    InvokeSceneSequenceHandlerCount(
                        sequenceHandler,
                        command) == 1,
                    "scene sequence lifetime validation must initially resolve one configured action handler",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldHandler);

                var replacement =
                    root.AddComponent<
                        P12SceneSequenceProbeHandler>();
                var throwing =
                    root.AddComponent<
                        P12ThrowingSceneSequenceProbeHandler>();

                InvokeSceneSequenceEnsureHandlers(
                    sequenceHandler);

                Expect(
                    InvokeSceneSequenceHandlerCount(
                        sequenceHandler,
                        command) == 1 &&
                    replacement != null &&
                    throwing != null,
                    "scene sequence handler must discard a destroyed cached handler, auto-discover its replacement, and isolate throwing capability probes",
                    failures);

                var metrics =
                    new List<VCR.Runtime.Core.RuntimeMetric>();
                sequenceHandler.CollectMetrics(
                    metrics);

                var probeFailures = 0.0;
                var foundProbeMetric = false;

                foreach (var metric in metrics)
                {
                    if (!string.Equals(
                            metric.Name,
                            "scene.sequence.handler_probe_failures",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    probeFailures =
                        metric.Value;
                    foundProbeMetric =
                        true;
                    break;
                }

                Expect(
                    foundProbeMetric &&
                    probeFailures >= 1.0,
                    "scene sequence diagnostics must expose isolated action-handler capability probe failures",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 scene sequence handler lifetime validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void InvokeSceneSequenceEnsureHandlers(
            SceneSequenceEventActionHandler handler)
        {
            var method =
                typeof(SceneSequenceEventActionHandler)
                    .GetMethod(
                        "EnsureHandlers",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(SceneSequenceEventActionHandler)
                        .FullName,
                    "EnsureHandlers");
            }

            method.Invoke(
                handler,
                null);
        }

        private static int InvokeSceneSequenceHandlerCount(
            SceneSequenceEventActionHandler handler,
            EventActionCommand command)
        {
            var method =
                typeof(SceneSequenceEventActionHandler)
                    .GetMethod(
                        "FindHandlerCount",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(SceneSequenceEventActionHandler)
                        .FullName,
                    "FindHandlerCount");
            }

            var arguments =
                new object[]
                {
                    command,
                    null
                };

            return (int)method.Invoke(
                handler,
                arguments);
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
                            "Reaction library for streamer events",
                            new[]
                            {
                                "broadcast",
                                "effects"
                            },
                            4,
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

                Expect(
                    P12EventRuleLibraryUtility
                        .TryCreatePackage(
                            "browser-validation",
                            "Earlier streamer reaction library",
                            new[]
                            {
                                "broadcast"
                            },
                            3,
                            new[]
                            {
                                template
                            },
                            out var previousPackage,
                            out var previousPackageError) &&
                    P12EventRuleLibraryUtility
                        .TrySerialize(
                            previousPackage,
                            out var previousJson,
                            out var previousSerializeError),
                    "P12 event rule library previous revision must serialize for history validation: " +
                    previousPackageError +
                    " / " +
                    previousSerializeError,
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
                var previousPath =
                    folder +
                    "/revision3.json";
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
                        "revision3.json"),
                    previousJson);
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
                    previousPath,
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
                        3,
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
                            entry.Valid &&
                            entry.Revision ==
                                4)
                        {
                            valid =
                                entry;
                        }
                        else if (entry != null &&
                                 !entry.Valid)
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
                    valid.Description ==
                        "Reaction library for streamer events" &&
                    valid.Revision ==
                        4 &&
                    valid.Tags.Length ==
                        2 &&
                    valid.RuleIds.Length ==
                        2 &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "manual-restore-default") &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "browser-validation") &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "streamer") &&
                    P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            valid,
                            "effects"),
                    "P12 event rule library browser must expose package/rule ids plus description/tags to search",
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

                Expect(
                    valid != null &&
                    P12EventRuleLibraryBrowserUtility
                        .TryGetRevisionHistory(
                            entries,
                            valid,
                            out var history,
                            out var historyError) &&
                    history.Length ==
                        2 &&
                    history[0].Revision ==
                        3 &&
                    history[1].Revision ==
                        4,
                    "P12 event rule library revision history must group matching PackageId files and sort them by revision: " +
                    historyError,
                    failures);

                Expect(
                    valid != null &&
                    P12EventRuleLibraryBrowserUtility
                        .TryFindPreviousRevision(
                            entries,
                            valid,
                            out var previousEntry,
                            out var previousError) &&
                    previousEntry != null &&
                    previousEntry.Revision ==
                        3,
                    "P12 event rule library must resolve the immediate previous revision deterministically: " +
                    previousError,
                    failures);

                if (valid != null &&
                    P12EventRuleLibraryBrowserUtility
                        .TryFindPreviousRevision(
                            entries,
                            valid,
                            out var diffFrom,
                            out _) &&
                    diffFrom != null)
                {
                    Expect(
                        P12EventRuleLibraryUtility
                            .TryDiffPackages(
                                diffFrom.Package,
                                valid.Package,
                                out var revisionDiff,
                                out var revisionDiffError) &&
                        revisionDiff.FromRevision ==
                            3 &&
                        revisionDiff.ToRevision ==
                            4 &&
                        revisionDiff.DescriptionChanged &&
                        revisionDiff.TagsChanged &&
                        revisionDiff.AddedRuleIds.Length ==
                            1,
                        "P12 event rule library previous-revision diff must report metadata and added-rule changes: " +
                        revisionDiffError,
                        failures);
                }

                File.WriteAllText(
                    Path.Combine(
                        absoluteFolder,
                        "revision3-duplicate.json"),
                    previousJson);
                AssetDatabase.ImportAsset(
                    folder +
                    "/revision3-duplicate.json",
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);

                Expect(
                    P12EventRuleLibraryBrowserUtility
                        .TryScan(
                            folder,
                            out var duplicateRevisionEntries,
                            out var duplicateRevisionScanError) &&
                    valid != null &&
                    !P12EventRuleLibraryBrowserUtility
                        .TryGetRevisionHistory(
                            duplicateRevisionEntries,
                            valid,
                            out _,
                            out var duplicateRevisionError) &&
                    duplicateRevisionError != null &&
                    duplicateRevisionError.IndexOf(
                        "ambiguous",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 event rule library history must reject duplicate valid files for the same PackageId revision instead of choosing one silently: " +
                    duplicateRevisionScanError +
                    " / " +
                    duplicateRevisionError,
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

    internal sealed class P12SceneSequenceProbeHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        public bool CanHandle(
            EventActionCommand command)
        {
            return string.Equals(
                command.ActionType,
                "p12.sequence.probe",
                StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;
            return CanHandle(
                command);
        }
    }

    internal sealed class P12ThrowingSceneSequenceProbeHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        public bool CanHandle(
            EventActionCommand command)
        {
            throw new InvalidOperationException(
                "synthetic P12 sequence probe failure");
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error =
                "should not execute";
            return false;
        }
    }
}
