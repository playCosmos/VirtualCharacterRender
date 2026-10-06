using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Application;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal static class P11AppearanceRuntimeValidation
    {
        public static void RunChecks(
            List<string> failures)
        {
            GameObject root = null;
            AudioClip audioClip = null;
            AnimationClip motionClip = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Appearance Validation");

                var casual =
                    new GameObject(
                        "Outfit Casual");
                casual.transform.SetParent(
                    root.transform,
                    false);

                var formal =
                    new GameObject(
                        "Outfit Formal");
                formal.transform.SetParent(
                    root.transform,
                    false);

                var hat =
                    new GameObject(
                        "Accessory Hat");
                hat.transform.SetParent(
                    root.transform,
                    false);

                var crown =
                    new GameObject(
                        "Accessory Crown");
                crown.transform.SetParent(
                    root.transform,
                    false);

                casual.SetActive(true);
                formal.SetActive(false);
                hat.SetActive(false);
                crown.SetActive(false);

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                runtime.ConfigureBindings(
                    new[]
                    {
                        new AppearanceOutfitBinding
                        {
                            OutfitId = "casual",
                            Roots = new[]
                            {
                                casual
                            }
                        },
                        new AppearanceOutfitBinding
                        {
                            OutfitId = "formal",
                            Roots = new[]
                            {
                                formal
                            }
                        }
                    },
                    new[]
                    {
                        new AppearanceAccessoryBinding
                        {
                            SlotId = "head",
                            AccessoryId = "hat",
                            Root = hat
                        },
                        new AppearanceAccessoryBinding
                        {
                            SlotId = "head",
                            AccessoryId = "crown",
                            Root = crown
                        }
                    },
                    new[]
                    {
                        new AppearancePresetBinding
                        {
                            PresetId = "casual-hat",
                            OutfitId = "casual",
                            Accessories =
                                new[]
                                {
                                    new AppearanceAccessorySelectionBinding
                                    {
                                        SlotId = "head",
                                        AccessoryId = "hat"
                                    }
                                }
                        },
                        new AppearancePresetBinding
                        {
                            PresetId = "formal-crown",
                            OutfitId = "formal",
                            PreferredTransitionId =
                                "spin-confetti",
                            Accessories =
                                new[]
                                {
                                    new AppearanceAccessorySelectionBinding
                                    {
                                        SlotId = "head",
                                        AccessoryId = "crown"
                                    }
                                }
                        }
                    },
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "spin-confetti",
                            DurationSeconds =
                                1.2f,
                            FallbackPolicy =
                                AppearanceTransitionFallbackPolicy
                                    .Immediate,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds = 0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play",
                                        Text =
                                            "confetti",
                                        Required = true
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds = 0.55f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    executors:
                        Array.Empty<MonoBehaviour>(),
                    nextDefaultPresetId:
                        "casual-hat");

                Expect(
                    runtime.TrySetMaxQueuedTransitions(
                        32,
                        out var queueLimitError) &&
                    runtime.MaxQueuedTransitions == 32 &&
                    runtime.PendingTransitionCount == 0 &&
                    string.IsNullOrEmpty(
                        queueLimitError),
                    "appearance queue limit must accept the bounded default range",
                    failures);

                Expect(
                    !runtime.TrySetMaxQueuedTransitions(
                        0,
                        out var queueMinError) &&
                    !string.IsNullOrEmpty(
                        queueMinError) &&
                    !runtime.TrySetMaxQueuedTransitions(
                        257,
                        out var queueMaxError) &&
                    !string.IsNullOrEmpty(
                        queueMaxError) &&
                    runtime.MaxQueuedTransitions == 32,
                    "appearance queue limit must reject values outside the 1..256 safety range",
                    failures);

                Expect(
                    runtime.RebuildConfiguration(
                        out var configError),
                    "appearance configuration must validate: " +
                    configError,
                    failures);

                var aliasRuntimeRoot =
                    new GameObject(
                        "Appearance Binding Isolation");
                aliasRuntimeRoot.transform.SetParent(
                    root.transform,
                    false);
                var aliasOutfitA =
                    new GameObject(
                        "Alias Outfit A");
                aliasOutfitA.transform.SetParent(
                    aliasRuntimeRoot.transform,
                    false);
                var aliasOutfitB =
                    new GameObject(
                        "Alias Outfit B");
                aliasOutfitB.transform.SetParent(
                    aliasRuntimeRoot.transform,
                    false);
                aliasOutfitA.SetActive(
                    true);
                aliasOutfitB.SetActive(
                    false);

                var aliasRuntime =
                    aliasRuntimeRoot.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var aliasOutfitBinding =
                    new AppearanceOutfitBinding
                    {
                        OutfitId =
                            "alias-a",
                        Roots =
                            new[]
                            {
                                aliasOutfitA
                            }
                    };
                var aliasPresetBinding =
                    new AppearancePresetBinding
                    {
                        PresetId =
                            "alias-a",
                        OutfitId =
                            "alias-a",
                        Accessories =
                            Array.Empty<
                                AppearanceAccessorySelectionBinding>()
                    };
                var aliasTransitionStep =
                    new AppearanceTransitionStepBinding
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Commit
                    };
                var aliasTransitionBinding =
                    new AppearanceTransitionBinding
                    {
                        TransitionId =
                            "alias-transition",
                        Steps =
                            new[]
                            {
                                aliasTransitionStep
                            }
                    };

                aliasRuntime.ConfigureBindings(
                    new[]
                    {
                        aliasOutfitBinding
                    },
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    new[]
                    {
                        aliasPresetBinding
                    },
                    new[]
                    {
                        aliasTransitionBinding
                    },
                    Array.Empty<MonoBehaviour>());

                aliasOutfitBinding.OutfitId =
                    "external-outfit";
                aliasOutfitBinding.Roots[0] =
                    aliasOutfitB;
                aliasPresetBinding.PresetId =
                    "external-preset";
                aliasPresetBinding.OutfitId =
                    "external-outfit";
                aliasTransitionBinding.TransitionId =
                    "external-transition";
                aliasTransitionStep.Kind =
                    AppearanceTransitionStepKind
                        .Action;
                aliasTransitionStep.ActionType =
                    "effect.play";

                var aliasRebuildSucceeded =
                    aliasRuntime.RebuildConfiguration(
                        out var aliasRebuildError);
                var aliasApplySucceeded =
                    aliasRuntime.SetPreset(
                        "alias-a",
                        "Immediate",
                        out var aliasApplyError);

                Expect(
                    aliasRebuildSucceeded &&
                    string.IsNullOrWhiteSpace(
                        aliasRebuildError) &&
                    aliasApplySucceeded &&
                    string.IsNullOrWhiteSpace(
                        aliasApplyError) &&
                    aliasRuntime.PresetIds.Count == 1 &&
                    aliasRuntime.PresetIds[0] ==
                        "alias-a" &&
                    aliasRuntime.TransitionIds.Count == 1 &&
                    aliasRuntime.TransitionIds[0] ==
                        "alias-transition" &&
                    aliasOutfitA.activeSelf &&
                    !aliasOutfitB.activeSelf,
                    "appearance ConfigureBindings must deep-clone mutable binding definitions so later caller mutation cannot rewrite live or future rebuilt configuration",
                    failures);

                Expect(
                    runtime.PresetIds.Count == 2 &&
                    runtime.PresetIds[0] ==
                        "casual-hat" &&
                    runtime.PresetIds[1] ==
                        "formal-crown",
                    "appearance quick-change order must preserve authoring order",
                    failures);

                var presetIdsView =
                    runtime.PresetIds;
                var transitionIdsView =
                    runtime.TransitionIds;
                var userPresetIdsView =
                    runtime.UserPresetIds;

                var presetMutationRejected =
                    false;
                var transitionMutationRejected =
                    false;
                var userPresetMutationRejected =
                    false;

                try
                {
                    ((IList<string>)presetIdsView)
                        .Add("external-preset");
                }
                catch (NotSupportedException)
                {
                    presetMutationRejected =
                        true;
                }

                try
                {
                    ((IList<string>)transitionIdsView)
                        .Add("external-transition");
                }
                catch (NotSupportedException)
                {
                    transitionMutationRejected =
                        true;
                }

                try
                {
                    ((IList<string>)userPresetIdsView)
                        .Add("external-user-preset");
                }
                catch (NotSupportedException)
                {
                    userPresetMutationRejected =
                        true;
                }

                Expect(
                    presetMutationRejected &&
                    transitionMutationRejected &&
                    userPresetMutationRejected &&
                    runtime.PresetIds.Count == 2 &&
                    runtime.TransitionIds.Count == 1 &&
                    runtime.UserPresetIds.Count == 0,
                    "appearance id collections must expose stable read-only views rather than mutable internal List instances",
                    failures);

                runtime.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "rejected-configure",
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play"
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    runtime.Status.State ==
                        AppearanceRuntimeState.Ready &&
                    !string.IsNullOrWhiteSpace(
                        runtime.Status.LastError) &&
                    runtime.PresetIds.Count == 2 &&
                    runtime.PresetIds[0] ==
                        "casual-hat" &&
                    runtime.PresetIds[1] ==
                        "formal-crown" &&
                    runtime.TransitionIds.Count == 1 &&
                    runtime.TransitionIds[0] ==
                        "spin-confetti",
                    "rejected ConfigureBindings input must preserve the last known-good runtime configuration and keep it operational",
                    failures);

                var transitionsField =
                    typeof(
                        BasicCharacterAppearanceRuntime)
                    .GetField(
                        "transitions",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);
                var validTransitions =
                    (AppearanceTransitionBinding[])
                        transitionsField?.GetValue(
                            runtime);

                transitionsField?.SetValue(
                    runtime,
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "rollback-invalid",
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play"
                                    }
                                }
                        }
                    });

                var rollbackRejected =
                    !runtime.RebuildConfiguration(
                        out var rollbackError);

                Expect(
                    rollbackRejected &&
                    !string.IsNullOrWhiteSpace(
                        rollbackError) &&
                    runtime.Status.State ==
                        AppearanceRuntimeState.Ready &&
                    runtime.PresetIds.Count == 2 &&
                    runtime.PresetIds[0] ==
                        "casual-hat" &&
                    runtime.PresetIds[1] ==
                        "formal-crown" &&
                    runtime.TransitionIds.Count == 1 &&
                    runtime.TransitionIds[0] ==
                        "spin-confetti",
                    "failed appearance configuration rebuild must restore the previous live preset/transition caches and runtime state",
                    failures);

                transitionsField?.SetValue(
                    runtime,
                    validTransitions);

                Expect(
                    runtime.RebuildConfiguration(
                        out var rollbackRestoreError) &&
                    string.IsNullOrWhiteSpace(
                        rollbackRestoreError) &&
                    runtime.TransitionIds.Count == 1 &&
                    runtime.TransitionIds[0] ==
                        "spin-confetti",
                    "appearance configuration rollback validation must restore the original authoring transition input and rebuild cleanly",
                    failures);

                var authoredOutfitsField =
                    typeof(
                        BasicCharacterAppearanceRuntime)
                    .GetField(
                        "outfits",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);
                var authoredAccessoriesField =
                    typeof(
                        BasicCharacterAppearanceRuntime)
                    .GetField(
                        "accessories",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);
                var authoredOutfits =
                    (AppearanceOutfitBinding[])
                        authoredOutfitsField?.GetValue(
                            runtime);
                var authoredAccessories =
                    (AppearanceAccessoryBinding[])
                        authoredAccessoriesField?.GetValue(
                            runtime);

                var originalAuthoredOutfitId =
                    authoredOutfits?[0]
                        ?.OutfitId;
                var originalAuthoredAccessoryId =
                    authoredAccessories?[0]
                        ?.AccessoryId;

                if (authoredOutfits != null &&
                    authoredOutfits.Length > 0 &&
                    authoredOutfits[0] != null)
                {
                    authoredOutfits[0].OutfitId =
                        "mutated-authoring-outfit";
                }

                if (authoredAccessories != null &&
                    authoredAccessories.Length > 0 &&
                    authoredAccessories[0] != null)
                {
                    authoredAccessories[0].AccessoryId =
                        "mutated-authoring-accessory";
                }

                Expect(
                    runtime.SetPreset(
                        "casual-hat",
                        "Immediate",
                        out var applyError),
                    "immediate default appearance must apply: " +
                    applyError,
                    failures);

                Expect(
                    casual.activeSelf &&
                    !formal.activeSelf &&
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "live appearance resolution must remain isolated from in-place mutation of serialized authoring binding objects until rebuild",
                    failures);

                if (authoredOutfits != null &&
                    authoredOutfits.Length > 0 &&
                    authoredOutfits[0] != null)
                {
                    authoredOutfits[0].OutfitId =
                        originalAuthoredOutfitId;
                }

                if (authoredAccessories != null &&
                    authoredAccessories.Length > 0 &&
                    authoredAccessories[0] != null)
                {
                    authoredAccessories[0].AccessoryId =
                        originalAuthoredAccessoryId;
                }

                Expect(
                    casual.activeSelf &&
                    !formal.activeSelf &&
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "immediate preset must atomically select the configured outfit and accessory",
                    failures);

                var healthyAppearanceNotifications = 0;
                var healthyStatusNotifications = 0;
                Action<AppearanceStateSnapshot>
                    throwingAppearanceSubscriber =
                        _ =>
                            throw new InvalidOperationException(
                                "appearance subscriber failure");
                Action<AppearanceStateSnapshot>
                    healthyAppearanceSubscriber =
                        _ =>
                            healthyAppearanceNotifications++;
                Action<AppearanceRuntimeStatus>
                    throwingStatusSubscriber =
                        _ =>
                            throw new InvalidOperationException(
                                "status subscriber failure");
                Action<AppearanceRuntimeStatus>
                    healthyStatusSubscriber =
                        _ =>
                            healthyStatusNotifications++;

                runtime.AppearanceChanged +=
                    throwingAppearanceSubscriber;
                runtime.AppearanceChanged +=
                    healthyAppearanceSubscriber;
                runtime.StatusChanged +=
                    throwingStatusSubscriber;
                runtime.StatusChanged +=
                    healthyStatusSubscriber;

                var subscriberIsolated =
                    runtime.SetPreset(
                        "formal-crown",
                        "Immediate",
                        out var subscriberIsolationError);

                runtime.AppearanceChanged -=
                    throwingAppearanceSubscriber;
                runtime.AppearanceChanged -=
                    healthyAppearanceSubscriber;
                runtime.StatusChanged -=
                    throwingStatusSubscriber;
                runtime.StatusChanged -=
                    healthyStatusSubscriber;

                Expect(
                    subscriberIsolated &&
                    string.IsNullOrEmpty(
                        subscriberIsolationError) &&
                    runtime.Status.State ==
                        AppearanceRuntimeState.Ready &&
                    runtime.Current.PresetId ==
                        "formal-crown" &&
                    healthyAppearanceNotifications > 0 &&
                    healthyStatusNotifications > 0,
                    "appearance/status subscriber exceptions must not abort an already-committed appearance change or block healthy subscribers: " +
                    subscriberIsolationError,
                    failures);

                Expect(
                    runtime.SetPreset(
                        "casual-hat",
                        "Immediate",
                        out var subscriberRestoreError),
                    "appearance subscriber isolation validation must restore the baseline preset: " +
                    subscriberRestoreError,
                    failures);

                Expect(
                    runtime.SetPreset(
                        "formal-crown",
                        "spin-confetti",
                        out var transitionError),
                    "transition with Immediate fallback must be accepted when its required effect executor is unavailable: " +
                    transitionError,
                    failures);

                Expect(
                    !casual.activeSelf &&
                    formal.activeSelf &&
                    !hat.activeSelf &&
                    crown.activeSelf &&
                    runtime.Status.CurrentPresetId ==
                        "formal-crown" &&
                    !runtime.Status.Busy,
                    "missing required transition executor with Immediate fallback must commit the target appearance synchronously",
                    failures);

                Expect(
                    runtime.SetAccessory(
                        "head",
                        "hat",
                        "Immediate",
                        out var accessoryError),
                    "direct accessory quick change must apply: " +
                    accessoryError,
                    failures);

                Expect(
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "single-selection accessory slot must disable the previous registered accessory",
                    failures);

                Expect(
                    runtime.ClearAccessory(
                        "head",
                        "Immediate",
                        out var clearError),
                    "accessory clear must apply: " +
                    clearError,
                    failures);

                Expect(
                    !hat.activeSelf &&
                    !crown.activeSelf,
                    "clearing a slot must disable every registered accessory in that slot",
                    failures);

                Expect(
                    runtime.RestoreDefault(
                        "Immediate",
                        out var restoreError),
                    "default appearance restore must apply: " +
                    restoreError,
                    failures);

                Expect(
                    casual.activeSelf &&
                    !formal.activeSelf &&
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "default restore must return the complete outfit/accessory state",
                    failures);

                var userRegistry =
                    (IAppearanceUserPresetRegistry)
                    runtime;

                Expect(
                    userRegistry.SaveCurrentAsUserPreset(
                        "user-casual",
                        "Immediate",
                        out var savedUserPreset,
                        out var userPresetError),
                    "current appearance must be savable as a user preset: " +
                    userPresetError,
                    failures);

                Expect(
                    savedUserPreset != null &&
                    savedUserPreset.Id ==
                        "user-casual" &&
                    savedUserPreset.OutfitId ==
                        "casual" &&
                    userRegistry.UserPresetIds.Count ==
                        1 &&
                    runtime.PresetIds.Count ==
                        3,
                    "saved user preset must join quick-change order without replacing authored presets",
                    failures);

                Expect(
                    !userRegistry.SaveCurrentAsUserPreset(
                        "casual-hat",
                        "Immediate",
                        out _,
                        out var collisionError) &&
                    !string.IsNullOrWhiteSpace(
                        collisionError),
                    "user presets must not replace authored appearance preset ids",
                    failures);

                var profileDirectory =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-p11-appearance-" +
                        Guid.NewGuid()
                            .ToString("N"));

                try
                {
                    var store =
                        new AppearanceUserPresetStore(
                            profileDirectory);
                    var characterPath =
                        Path.Combine(
                            profileDirectory,
                            "avatar.vrm");

                    Expect(
                        store.TrySave(
                            characterPath,
                            userRegistry
                                .CaptureUserPresets(),
                            out var profileSaveError),
                        "user appearance preset profile must save atomically: " +
                        profileSaveError,
                        failures);

                    Expect(
                        store.TryLoad(
                            characterPath,
                            out var loadedUserPresets,
                            out var profileLoadError),
                        "user appearance preset profile must load: " +
                        profileLoadError,
                        failures);

                    Expect(
                        loadedUserPresets.Length ==
                            1 &&
                        loadedUserPresets[0] !=
                            null &&
                        loadedUserPresets[0].Id ==
                            "user-casual" &&
                        loadedUserPresets[0]
                            .Accessories.Length ==
                            1,
                        "saved appearance profile must preserve preset and accessory selections",
                        failures);

                    Expect(
                        userRegistry.ReplaceUserPresets(
                            Array.Empty<
                                AppearancePreset>(),
                            out var clearUserError),
                        "user preset registry must support profile replacement: " +
                        clearUserError,
                        failures);

                    Expect(
                        !runtime.SetPreset(
                            "user-casual",
                            "Immediate",
                            out _),
                        "cleared user preset must leave the active registry",
                        failures);

                    var restoredUserPresets =
                        userRegistry.ReplaceUserPresets(
                            loadedUserPresets,
                            out var restoreUserError);
                    string applyUserError = null;
                    var appliedUserPreset =
                        restoredUserPresets &&
                        runtime.SetPreset(
                            "user-casual",
                            "Immediate",
                            out applyUserError);

                    if (!restoredUserPresets)
                    {
                        applyUserError =
                            "restore failed before apply";
                    }

                    Expect(
                        restoredUserPresets &&
                        appliedUserPreset,
                        "loaded user preset must register and apply through the normal quick-change path: " +
                        restoreUserError +
                        " / " +
                        applyUserError,
                        failures);

                    Expect(
                        userRegistry.DuplicateUserPreset(
                            "user-casual",
                            "user-copy",
                            out var duplicatedUserPreset,
                            out var duplicateUserError) &&
                        duplicatedUserPreset != null &&
                        userRegistry.UserPresetIds.Count ==
                            2 &&
                        userRegistry.UserPresetIds[0] ==
                            "user-casual" &&
                        userRegistry.UserPresetIds[1] ==
                            "user-copy",
                        "user preset duplication must insert the clone immediately after its source: " +
                        duplicateUserError,
                        failures);

                    Expect(
                        userRegistry.RenameUserPreset(
                            "user-copy",
                            "user-renamed",
                            out var renamedUserPreset,
                            out var renameUserError) &&
                        renamedUserPreset != null &&
                        renamedUserPreset.Id ==
                            "user-renamed" &&
                        userRegistry.UserPresetIds[1] ==
                            "user-renamed",
                        "user preset rename must preserve list position while replacing the id: " +
                        renameUserError,
                        failures);

                    Expect(
                        !userRegistry.RenameUserPreset(
                            "user-renamed",
                            "casual-hat",
                            out _,
                            out var authoredRenameCollisionError) &&
                        !string.IsNullOrWhiteSpace(
                            authoredRenameCollisionError),
                        "user preset rename must not collide with authored preset ids",
                        failures);

                    Expect(
                        userRegistry.MoveUserPreset(
                            "user-renamed",
                            -1,
                            out var moveUserUpError) &&
                        userRegistry.UserPresetIds[0] ==
                            "user-renamed" &&
                        userRegistry.UserPresetIds[1] ==
                            "user-casual",
                        "user preset reorder must move the selected preset upward while preserving ids: " +
                        moveUserUpError,
                        failures);

                    string applyRenamedError =
                        null;
                    var appliedRenamed =
                        runtime.SetPreset(
                            "user-renamed",
                            "Immediate",
                            out applyRenamedError);

                    Expect(
                        appliedRenamed &&
                        runtime.Current.PresetId ==
                            "user-renamed",
                        "renamed user preset must remain applicable through the normal preset path: " +
                        applyRenamedError,
                        failures);

                    var activeRenameAppearanceNotifications =
                        0;
                    var activeRenameStatusNotifications =
                        0;
                    var activeRenameSawEmptyPreset =
                        false;

                    Action<AppearanceStateSnapshot>
                        activeRenameAppearanceHandler =
                            snapshot =>
                            {
                                activeRenameAppearanceNotifications++;
                                if (string.IsNullOrWhiteSpace(
                                        snapshot.PresetId))
                                {
                                    activeRenameSawEmptyPreset =
                                        true;
                                }
                            };
                    Action<AppearanceRuntimeStatus>
                        activeRenameStatusHandler =
                            _ =>
                                activeRenameStatusNotifications++;

                    runtime.AppearanceChanged +=
                        activeRenameAppearanceHandler;
                    runtime.StatusChanged +=
                        activeRenameStatusHandler;

                    var activeRenameSucceeded =
                        userRegistry.RenameUserPreset(
                            "user-renamed",
                            "user-final",
                            out _,
                            out var currentRenameError);

                    runtime.AppearanceChanged -=
                        activeRenameAppearanceHandler;
                    runtime.StatusChanged -=
                        activeRenameStatusHandler;

                    Expect(
                        activeRenameSucceeded &&
                        runtime.Current.PresetId ==
                            "user-final" &&
                        activeRenameAppearanceNotifications ==
                            1 &&
                        activeRenameStatusNotifications ==
                            1 &&
                        !activeRenameSawEmptyPreset,
                        "renaming the currently selected user preset must atomically remap its current identity without emitting a transient empty preset or duplicate status notification: " +
                        currentRenameError,
                        failures);

                    Expect(
                        userRegistry.MoveUserPreset(
                            "user-final",
                            1,
                            out var moveUserDownError) &&
                        userRegistry.UserPresetIds[0] ==
                            "user-casual" &&
                        userRegistry.UserPresetIds[1] ==
                            "user-final",
                        "user preset reorder must move the selected preset downward: " +
                        moveUserDownError,
                        failures);

                    var beforeDefinitionReplacement =
                        userRegistry.CaptureUserPresets();
                    var mismatchedDefinitions =
                        userRegistry.CaptureUserPresets();
                    var mismatchedDefinitionIndex =
                        Array.FindIndex(
                            mismatchedDefinitions,
                            candidate =>
                                candidate != null &&
                                candidate.Id ==
                                    "user-final");
                    var appearanceBeforeDefinitionReplacement =
                        runtime.Current;

                    if (mismatchedDefinitionIndex >= 0)
                    {
                        mismatchedDefinitions[
                            mismatchedDefinitionIndex]
                            .OutfitId =
                                appearanceBeforeDefinitionReplacement
                                    .OutfitId ==
                                "formal"
                                    ? "casual"
                                    : "formal";
                    }

                    var definitionReplacementNotifications =
                        0;
                    Action<AppearanceStateSnapshot>
                        definitionReplacementHandler =
                            _ =>
                                definitionReplacementNotifications++;

                    runtime.AppearanceChanged +=
                        definitionReplacementHandler;

                    var definitionReplacementSucceeded =
                        mismatchedDefinitionIndex >= 0 &&
                        userRegistry.ReplaceUserPresets(
                            mismatchedDefinitions,
                            out var definitionReplacementError);

                    runtime.AppearanceChanged -=
                        definitionReplacementHandler;

                    Expect(
                        definitionReplacementSucceeded &&
                        runtime.Current.PresetId ==
                            null &&
                        runtime.Current.OutfitId ==
                            appearanceBeforeDefinitionReplacement
                                .OutfitId &&
                        definitionReplacementNotifications ==
                            1,
                        "replacing the active user preset definition with different appearance content must invalidate only the stale preset identity while preserving the actual appearance: " +
                        definitionReplacementError,
                        failures);

                    var restoredDefinitionReplacement =
                        userRegistry.ReplaceUserPresets(
                            beforeDefinitionReplacement,
                            out var restoreDefinitionError);
                    string reapplyDefinitionError =
                        null;
                    var reappliedDefinitionPreset =
                        restoredDefinitionReplacement &&
                        runtime.SetPreset(
                            "user-final",
                            "Immediate",
                            out reapplyDefinitionError);

                    Expect(
                        restoredDefinitionReplacement &&
                        reappliedDefinitionPreset &&
                        runtime.Current.PresetId ==
                            "user-final",
                        "appearance definition-mismatch validation must restore and reapply the original current user preset: " +
                        restoreDefinitionError +
                        " / " +
                        reapplyDefinitionError,
                        failures);

                    var beforeCurrentInvalidation =
                        userRegistry.CaptureUserPresets();
                    var currentInvalidationNotifications =
                        0;
                    var invalidatedSnapshot =
                        default(AppearanceStateSnapshot);
                    Action<AppearanceStateSnapshot>
                        currentInvalidationHandler =
                            snapshot =>
                            {
                                currentInvalidationNotifications++;
                                invalidatedSnapshot =
                                    snapshot;
                            };

                    runtime.AppearanceChanged +=
                        currentInvalidationHandler;

                    var invalidatedCurrentPreset =
                        userRegistry.ReplaceUserPresets(
                            Array.Empty<
                                AppearancePreset>(),
                            out var currentInvalidationError);

                    runtime.AppearanceChanged -=
                        currentInvalidationHandler;

                    Expect(
                        invalidatedCurrentPreset &&
                        runtime.Status.CurrentPresetId ==
                            null &&
                        invalidatedSnapshot.PresetId ==
                            null &&
                        currentInvalidationNotifications ==
                            1,
                        "replacing the user-preset registry must publish one appearance snapshot when it invalidates the current preset id: " +
                        currentInvalidationError,
                        failures);

                    var restoredAfterInvalidation =
                        userRegistry.ReplaceUserPresets(
                            beforeCurrentInvalidation,
                            out var currentRestoreError);
                    string currentReapplyError =
                        null;
                    var reappliedAfterInvalidation =
                        restoredAfterInvalidation &&
                        runtime.SetPreset(
                            "user-final",
                            "Immediate",
                            out currentReapplyError);

                    if (!restoredAfterInvalidation)
                    {
                        currentReapplyError =
                            "preset restore failed before reapply";
                    }

                    Expect(
                        restoredAfterInvalidation &&
                        reappliedAfterInvalidation &&
                        runtime.Status.CurrentPresetId ==
                            "user-final",
                        "current-preset invalidation validation must restore the prior user presets and active preset: " +
                        currentRestoreError +
                        " / " +
                        currentReapplyError,
                        failures);

                    Expect(
                        store.TrySave(
                            characterPath,
                            userRegistry
                                .CaptureUserPresets(),
                            out var managedProfileSaveError) &&
                        store.TryLoad(
                            characterPath,
                            out var managedUserPresets,
                            out var managedProfileLoadError) &&
                        managedUserPresets.Length ==
                            2 &&
                        managedUserPresets[0].Id ==
                            "user-casual" &&
                        managedUserPresets[1].Id ==
                            "user-final",
                        "appearance profile persistence must preserve managed user preset ids and order: " +
                        managedProfileSaveError +
                        " / " +
                        managedProfileLoadError,
                        failures);

                    var excessivePresets =
                        new AppearancePreset[
                            AppearanceUserPresetStore
                                .MaxPresets +
                            1];

                    Expect(
                        !store.TrySave(
                            characterPath,
                            excessivePresets,
                            out var excessivePresetError) &&
                        !string.IsNullOrWhiteSpace(
                            excessivePresetError),
                        "appearance profile persistence must reject excessive preset fanout before serialization",
                        failures);

                    var excessiveAccessories =
                        new AppearancePreset
                        {
                            Id =
                                "too-many-accessories",
                            Accessories =
                                new AppearanceAccessorySelection[
                                    AppearanceUserPresetStore
                                        .MaxAccessoriesPerPreset +
                                    1]
                        };

                    Expect(
                        !store.TrySave(
                            characterPath,
                            new[]
                            {
                                excessiveAccessories
                            },
                            out var excessiveAccessoryError) &&
                        !string.IsNullOrWhiteSpace(
                            excessiveAccessoryError),
                        "appearance profile persistence must reject excessive per-preset accessory fanout before serialization",
                        failures);

                    var profilePath =
                        store.GetProfilePath(
                            characterPath);

                    using (var stream =
                           new FileStream(
                               profilePath,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        stream.SetLength(
                            16L * 1024L * 1024L +
                            1L);
                    }

                    Expect(
                        !store.TryLoad(
                            characterPath,
                            out var oversizedUserPresets,
                            out var oversizedProfileError) &&
                        oversizedUserPresets.Length == 0 &&
                        !string.IsNullOrWhiteSpace(
                            oversizedProfileError),
                        "appearance profile persistence must reject oversized profile files before JSON allocation",
                        failures);
                }
                finally
                {
                    if (Directory.Exists(
                            profileDirectory))
                    {
                        Directory.Delete(
                            profileDirectory,
                            true);
                    }
                }

                var previous =
                    runtime.Current;

                Expect(
                    !runtime.SetOutfit(
                        "missing-outfit",
                        "Immediate",
                        out _),
                    "unknown outfit must be rejected",
                    failures);

                Expect(
                    runtime.Current.OutfitId ==
                        previous.OutfitId &&
                    casual.activeSelf &&
                    !formal.activeSelf,
                    "rejected appearance request must leave the previous appearance intact",
                    failures);

                var appearanceHandler =
                    root.AddComponent<
                        AppearanceEventActionHandler>();
                appearanceHandler.SetAppearanceRuntime(
                    runtime);

                var eventCommand =
                    new EventActionCommand(
                        ruleId:
                            "appearance-validation",
                        actionType:
                            EventActionTypes
                                .AppearanceSetPreset,
                        targetId:
                            runtime.Status.RuntimeId,
                        name:
                            "Immediate",
                        text:
                            "formal-crown",
                        value:
                            0.0,
                        hasValue:
                            false,
                        eventSequence:
                            1);

                Expect(
                    appearanceHandler.CanHandle(
                        eventCommand) &&
                    appearanceHandler.TryExecute(
                        eventCommand,
                        out var eventError),
                    "event runtime appearance.set_preset must reach the same appearance runtime contract: " +
                    eventError,
                    failures);

                var cancelCommand =
                    new EventActionCommand(
                        ruleId:
                            "appearance-validation",
                        actionType:
                            EventActionTypes
                                .AppearanceCancelTransition,
                        targetId:
                            runtime.Status.RuntimeId,
                        name:
                            null,
                        text:
                            null,
                        value:
                            0.0,
                        hasValue:
                            false,
                        eventSequence:
                            2);

                Expect(
                    appearanceHandler.CanHandle(
                        cancelCommand) &&
                    !appearanceHandler.TryExecute(
                        cancelCommand,
                        out var inactiveCancelError) &&
                    inactiveCancelError != null &&
                    inactiveCancelError.Contains(
                        "No appearance transition",
                        StringComparison.OrdinalIgnoreCase),
                    "appearance.cancel_transition must route through the appearance runtime and fail clearly when no transition is active",
                    failures);

                Expect(
                    runtime.Status.CurrentPresetId ==
                        "formal-crown" &&
                    formal.activeSelf &&
                    crown.activeSelf,
                    "event-driven appearance preset change must produce the same atomic outfit/accessory state",
                    failures);

                var fakeAction =
                    root.AddComponent<
                        P11FakeTransitionActionHandler>();
                var effectRoot =
                    new GameObject(
                        "Confetti Effect Root");
                effectRoot.transform.SetParent(
                    root.transform,
                    false);
                effectRoot.SetActive(false);

                var effectHandler =
                    root.AddComponent<
                        EffectEventActionHandler>();
                effectHandler.ConfigureBindings(
                    new EffectEventActionHandler
                        .EffectBinding
                    {
                        EffectId =
                            "confetti",
                        Root =
                            effectRoot,
                        ParticleSystems =
                            Array.Empty<
                                ParticleSystem>(),
                        DeactivateOnStop =
                            true
                    });

                var transitionExecutor =
                    root.AddComponent<
                        AppearanceTransitionActionExecutor>();
                transitionExecutor.SetActionHandlers(
                    fakeAction,
                    effectHandler);

                var customStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            "custom.transition",
                        Text =
                            "user-action"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        customStep) &&
                    transitionExecutor.TryExecute(
                        customStep,
                        out var customError) &&
                    fakeAction.ExecutionCount == 1 &&
                    fakeAction.LastText ==
                        "user-action",
                    "appearance transition action executor must reuse exactly one user-registered application action handler: " +
                    customError,
                    failures);

                var throwingAction =
                    root.AddComponent<
                        P11ThrowingTransitionActionHandler>();
                var throwingActionExecutor =
                    root.AddComponent<
                        AppearanceTransitionActionExecutor>();
                throwingActionExecutor.SetActionHandlers(
                    throwingAction);

                throwingAction.ThrowCanHandle =
                    true;

                var throwingCanExecuteEscaped = false;
                var throwingCanExecuteResult = false;

                try
                {
                    throwingCanExecuteResult =
                        throwingActionExecutor
                            .CanExecute(
                                customStep);
                }
                catch
                {
                    throwingCanExecuteEscaped =
                        true;
                }

                Expect(
                    !throwingCanExecuteEscaped &&
                    !throwingCanExecuteResult &&
                    !throwingActionExecutor.TryExecute(
                        customStep,
                        out var throwingHandleError) &&
                    !string.IsNullOrEmpty(
                        throwingHandleError) &&
                    throwingHandleError.Contains(
                        "CanHandle",
                        StringComparison.Ordinal),
                    "throwing delegated CanHandle probes must fail closed without escaping the appearance transition action bridge",
                    failures);

                throwingAction.ThrowCanHandle =
                    false;
                throwingAction.ThrowCanTrackCompletion =
                    true;

                var throwingTrackEscaped = false;
                var throwingTrackResult = false;

                try
                {
                    throwingTrackResult =
                        throwingActionExecutor
                            .CanTrackCompletion(
                                customStep);
                }
                catch
                {
                    throwingTrackEscaped =
                        true;
                }

                Expect(
                    !throwingTrackEscaped &&
                    !throwingTrackResult &&
                    !throwingActionExecutor.TryIsComplete(
                        customStep,
                        out _,
                        out var throwingTrackError) &&
                    !string.IsNullOrEmpty(
                        throwingTrackError) &&
                    throwingTrackError.Contains(
                        "CanTrackCompletion",
                        StringComparison.Ordinal),
                    "throwing delegated completion capability probes must fail closed with an explicit error",
                    failures);

                var blockingCustomStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            "custom.transition",
                        Text =
                            "blocking-action",
                        Blocking =
                            true,
                        CompletionTimeoutSeconds =
                            1.0
                    };

                fakeAction.ResetCompletion(
                    checksBeforeComplete:
                        2);

                var firstComplete =
                    false;
                string firstCompletionError =
                    null;
                var firstCompletionTracked =
                    transitionExecutor
                        .CanTrackCompletion(
                            blockingCustomStep) &&
                    transitionExecutor
                        .TryIsComplete(
                            blockingCustomStep,
                            out firstComplete,
                            out firstCompletionError);

                var secondComplete =
                    false;
                string secondCompletionError =
                    null;
                var secondCompletionTracked =
                    transitionExecutor
                        .TryIsComplete(
                            blockingCustomStep,
                            out secondComplete,
                            out secondCompletionError);

                Expect(
                    firstCompletionTracked &&
                    !firstComplete &&
                    secondCompletionTracked &&
                    secondComplete,
                    "transition completion bridge must poll a custom action completion probe until it reports complete: " +
                    firstCompletionError +
                    " / " +
                    secondCompletionError,
                    failures);

                var effectStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .EffectPlay,
                        TargetId =
                            "effects.main",
                        Text =
                            "confetti"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        effectStep) &&
                    transitionExecutor.TryExecute(
                        effectStep,
                        out var effectError) &&
                    effectRoot.activeSelf,
                    "appearance transition must be able to play a registered built-in effect through the shared action bridge: " +
                    effectError,
                    failures);

                var stopEffect =
                    new EventActionCommand(
                        ruleId:
                            "appearance-validation",
                        actionType:
                            EventActionTypes
                                .EffectStop,
                        targetId:
                            "effects.main",
                        name:
                            null,
                        text:
                            "confetti",
                        value:
                            0.0,
                        hasValue:
                            false,
                        eventSequence:
                            2);

                Expect(
                    effectHandler.TryExecute(
                        stopEffect,
                        out var stopError) &&
                    !effectRoot.activeSelf,
                    "effect.stop must stop/deactivate a registered quick-change effect: " +
                    stopError,
                    failures);

                var audioSource =
                    root.AddComponent<
                        AudioSource>();
                audioClip =
                    AudioClip.Create(
                        "Wardrobe Chime",
                        441,
                        1,
                        44100,
                        false);

                var audioHandler =
                    root.AddComponent<
                        AudioEventActionHandler>();
                audioHandler.ConfigureBindings(
                    new AudioEventActionHandler
                        .AudioBinding
                    {
                        AudioId =
                            "wardrobe-chime",
                        Source =
                            audioSource,
                        Clip =
                            audioClip,
                        RestartOnPlay =
                            true,
                        Loop =
                            true
                    });

                transitionExecutor.SetActionHandlers(
                    fakeAction,
                    effectHandler,
                    audioHandler);

                var audioPlayStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .AudioPlay,
                        TargetId =
                            "audio.main",
                        Text =
                            "wardrobe-chime",
                        Value =
                            0.35,
                        HasValue =
                            true
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        audioPlayStep) &&
                    transitionExecutor.TryExecute(
                        audioPlayStep,
                        out var audioPlayError) &&
                    audioSource.clip ==
                        audioClip &&
                    audioSource.loop &&
                    Math.Abs(
                        audioSource.volume -
                        0.35f) <
                        0.001f,
                    "appearance transition must be able to play registered audio and apply optional volume through audio.play: " +
                    audioPlayError,
                    failures);

                var audioStopStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .AudioStop,
                        TargetId =
                            "audio.main",
                        Text =
                            "wardrobe-chime"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        audioStopStep) &&
                    transitionExecutor.TryExecute(
                        audioStopStep,
                        out var audioStopError),
                    "appearance transition must stop registered audio through audio.stop: " +
                    audioStopError,
                    failures);

                var motionMixer =
                    root.AddComponent<
                        MotionExpressionMixer>();
                var motionSource =
                    root.AddComponent<
                        ProceduralMotionCueSource>();

                var callerOwnedProceduralCue =
                    new ProceduralMotionCueDefinition
                    {
                        CueId =
                            "spin",
                        DurationSeconds =
                            1f,
                        RootEulerDegrees =
                            new Vector3(
                                0f,
                                180f,
                                0f),
                        ProgressCurve =
                            AnimationCurve.Linear(
                                0f,
                                0f,
                                1f,
                                1f)
                    };

                motionSource.ConfigureCues(
                    callerOwnedProceduralCue);

                callerOwnedProceduralCue.CueId =
                    "caller-mutated-spin";
                callerOwnedProceduralCue.RootEulerDegrees =
                    Vector3.zero;
                callerOwnedProceduralCue.ProgressCurve =
                    AnimationCurve.Constant(
                        0f,
                        1f,
                        0f);

                var proceduralCueIdMutationRejected =
                    false;

                try
                {
                    ((IList<string>)
                        motionSource.CueIds)
                        .Add(
                            "external-procedural-cue");
                }
                catch (NotSupportedException)
                {
                    proceduralCueIdMutationRejected =
                        true;
                }

                Expect(
                    proceduralCueIdMutationRejected &&
                    motionSource.CueIds.Count == 1 &&
                    motionSource.CueIds[0] ==
                        "spin",
                    "procedural motion cue source must expose a read-only cue-id view and isolate caller-owned ConfigureCues definitions",
                    failures);

                Expect(
                    motionSource.TrySampleCue(
                        "spin",
                        0.5f,
                        out var sampledPose,
                        out var sampleError),
                    "procedural quick-change motion cue must be sampleable: " +
                    sampleError,
                    failures);

                var sampledRotation =
                    sampledPose?.RootRotation ??
                    VCR.Runtime.Tracking
                        .TrackingQuaternion.Identity;

                Expect(
                    Math.Abs(
                        Math.Abs(
                            sampledRotation.Y) -
                        0.7071f) <
                    0.02f &&
                    Math.Abs(
                        Math.Abs(
                            sampledRotation.W) -
                        0.7071f) <
                    0.02f,
                    "halfway through a 180-degree root spin cue must sample approximately 90 degrees",
                    failures);

                motionSource.ConfigureCues(
                    new ProceduralMotionCueDefinition
                    {
                        CueId =
                            "invalid-procedural",
                        DurationSeconds =
                            0f
                    });

                Expect(
                    motionSource.CueIds.Count == 1 &&
                    motionSource.CueIds[0] ==
                        "spin" &&
                    motionSource.TrySampleCue(
                        "spin",
                        0.5f,
                        out var rollbackProceduralPose,
                        out var rollbackProceduralError) &&
                    rollbackProceduralPose != null &&
                    string.IsNullOrWhiteSpace(
                        rollbackProceduralError),
                    "failed procedural ConfigureCues replacement must preserve the previous validated live cue set",
                    failures);

                var clipRig =
                    new GameObject(
                        "Clip Reference Rig");
                clipRig.transform.SetParent(
                    root.transform,
                    false);
                var clipHips =
                    new GameObject(
                        "Hips");
                clipHips.transform.SetParent(
                    clipRig.transform,
                    false);

                motionClip =
                    new AnimationClip
                    {
                        name =
                            "Quick Change Clip",
                        legacy =
                            true
                    };
                motionClip.SetCurve(
                    "Hips",
                    typeof(Transform),
                    "localPosition.x",
                    AnimationCurve.Linear(
                        0f,
                        0f,
                        1f,
                        1f));
                motionClip.AddEvent(
                    new AnimationEvent
                    {
                        functionName =
                            "VCRMarker",
                        stringParameter =
                            "swap",
                        time =
                            0.55f
                    });
                motionClip.AddEvent(
                    new AnimationEvent
                    {
                        functionName =
                            "VCRMarker_spin-end",
                        time =
                            0.90f
                    });
                motionClip.AddEvent(
                    new AnimationEvent
                    {
                        functionName =
                            "UnrelatedAnimationEvent",
                        stringParameter =
                            "ignore-me",
                        time =
                            0.20f
                    });

                Expect(
                    P11AnimationClipMotionCueBaker
                        .TryBake(
                            motionClip,
                            clipRig,
                            "clip-step",
                            4f,
                            false,
                            false,
                            out var bakedCue,
                            out var bakeError),
                    "AnimationClip quick-change cue must bake against a reference humanoid hierarchy: " +
                    bakeError,
                    failures);

                Expect(
                    bakedCue != null &&
                    bakedCue.Markers != null &&
                    bakedCue.Markers.Length ==
                        2 &&
                    bakedCue.Markers[0].Name ==
                        "swap" &&
                    Math.Abs(
                        bakedCue.Markers[0]
                            .TimeSeconds -
                        0.55f) <
                        0.001f &&
                    bakedCue.Markers[1].Name ==
                        "spin-end" &&
                    Math.Abs(
                        bakedCue.Markers[1]
                            .TimeSeconds -
                        0.90f) <
                        0.001f,
                    "AnimationClip baker must preserve only explicit VCRMarker events as sorted baked cue markers",
                    failures);

                motionClip.AddEvent(
                    new AnimationEvent
                    {
                        functionName =
                            "VCRMarker",
                        stringParameter =
                            "swap",
                        time =
                            0.70f
                    });

                Expect(
                    !P11MotionMarkerUtility
                        .TryExtractFromAnimationClip(
                            motionClip,
                            out _,
                            out var duplicateMarkerError) &&
                    duplicateMarkerError != null &&
                    duplicateMarkerError.Contains(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase),
                    "AnimationClip marker extraction must reject duplicate marker names",
                    failures);

                var bakedSource =
                    root.AddComponent<
                        BakedMotionCueSource>();
                bakedSource.ConfigureCues(
                    bakedCue);

                Expect(
                    bakedSource.TrySampleCue(
                        "clip-step",
                        0.5f,
                        out var bakedSample,
                        out var bakedSampleError) &&
                    bakedSample.TryGet(
                        VCR.Runtime.Tracking
                            .HumanoidBoneId.Hips,
                        out var bakedHips) &&
                    Math.Abs(
                        bakedHips.LocalPosition.X -
                        0.5f) <
                        0.08f,
                    "baked AnimationClip cue must interpolate additive humanoid pose data without sampling Animator at playback time: " +
                    bakedSampleError,
                    failures);

                var motionHandler =
                    root.AddComponent<
                        MotionCueEventActionHandler>();
                motionHandler.SetMotionRuntimes(
                    motionSource,
                    bakedSource);

                transitionExecutor.SetActionHandlers(
                    fakeAction,
                    effectHandler,
                    audioHandler,
                    motionHandler);

                var motionPlayStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .MotionPlay,
                        TargetId =
                            "motion.quickchange",
                        Text =
                            "spin"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        motionPlayStep) &&
                    transitionExecutor.TryExecute(
                        motionPlayStep,
                        out var motionPlayError) &&
                    motionSource.Status.Playing &&
                    motionSource.TryGetLatestHumanoidPose(
                        out _),
                    "appearance transition must start a registered procedural motion cue through motion.play: " +
                    motionPlayError,
                    failures);

                var motionCompletionBeforeRelease =
                    false;
                string motionCompletionError =
                    null;

                Expect(
                    transitionExecutor
                        .CanTrackCompletion(
                            motionPlayStep) &&
                    transitionExecutor
                        .TryIsComplete(
                            motionPlayStep,
                            out motionCompletionBeforeRelease,
                            out motionCompletionError) &&
                    !motionCompletionBeforeRelease,
                    "active procedural motion.play must expose incomplete completion state for blocking transitions: " +
                    motionCompletionError,
                    failures);

                var motionReleaseStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .MotionRelease,
                        TargetId =
                            "motion.quickchange",
                        Text =
                            "spin"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        motionReleaseStep) &&
                    transitionExecutor.TryExecute(
                        motionReleaseStep,
                        out var motionReleaseError) &&
                    !motionSource.Status.Playing &&
                    !motionSource.TryGetLatestHumanoidPose(
                        out _),
                    "appearance transition motion.release must remove the procedural pose contribution: " +
                    motionReleaseError,
                    failures);

                var motionCompletionAfterRelease =
                    false;
                string motionCompletionAfterReleaseError =
                    null;

                Expect(
                    transitionExecutor
                        .TryIsComplete(
                            motionPlayStep,
                            out motionCompletionAfterRelease,
                            out motionCompletionAfterReleaseError) &&
                    motionCompletionAfterRelease,
                    "released motion cue must report the prior motion.play blocking action complete: " +
                    motionCompletionAfterReleaseError,
                    failures);

                var bakedPlayStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .MotionPlay,
                        Text =
                            "clip-step"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        bakedPlayStep) &&
                    transitionExecutor.TryExecute(
                        bakedPlayStep,
                        out var bakedPlayError) &&
                    bakedSource.Status.Playing &&
                    bakedSource.TryGetLatestHumanoidPose(
                        out _),
                    "motion.play without TargetId must route an AnimationClip-derived cue to its single owning runtime: " +
                    bakedPlayError,
                    failures);

                var bakedReleaseStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .MotionRelease,
                        Text =
                            "clip-step"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        bakedReleaseStep) &&
                    transitionExecutor.TryExecute(
                        bakedReleaseStep,
                        out var bakedReleaseError) &&
                    !bakedSource.Status.Playing,
                    "motion.release without TargetId must route to the runtime that owns the baked cue: " +
                    bakedReleaseError,
                    failures);

                var recursiveStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .AppearanceSetPreset,
                        Text =
                            "casual-hat"
                    };

                Expect(
                    !transitionExecutor.CanExecute(
                        recursiveStep) &&
                    !transitionExecutor.TryExecute(
                        recursiveStep,
                        out _),
                    "appearance transition executor must reject recursive appearance.* actions",
                    failures);

                var conventionRoot =
                    new GameObject(
                        "Convention Appearance Runtime");
                conventionRoot.transform.SetParent(
                    root.transform,
                    false);

                var appearanceRoot =
                    new GameObject(
                        "VCRAppearance");
                appearanceRoot.transform.SetParent(
                    conventionRoot.transform,
                    false);

                var conventionOutfits =
                    new GameObject(
                        "Outfits");
                conventionOutfits.transform.SetParent(
                    appearanceRoot.transform,
                    false);

                var conventionCasual =
                    new GameObject(
                        "casual");
                conventionCasual.transform.SetParent(
                    conventionOutfits.transform,
                    false);
                conventionCasual.SetActive(
                    true);

                var conventionFormal =
                    new GameObject(
                        "formal");
                conventionFormal.transform.SetParent(
                    conventionOutfits.transform,
                    false);
                conventionFormal.SetActive(
                    false);

                var conventionAccessories =
                    new GameObject(
                        "Accessories");
                conventionAccessories.transform.SetParent(
                    appearanceRoot.transform,
                    false);

                var conventionHead =
                    new GameObject(
                        "head");
                conventionHead.transform.SetParent(
                    conventionAccessories.transform,
                    false);

                var conventionHat =
                    new GameObject(
                        "hat");
                conventionHat.transform.SetParent(
                    conventionHead.transform,
                    false);
                conventionHat.SetActive(
                    false);

                var conventionRuntime =
                    conventionRoot.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                Expect(
                    conventionRuntime.RebuildConfiguration(
                        out var conventionError) &&
                    conventionRuntime.PresetIds.Count == 2 &&
                    conventionRuntime.PresetIds[0] ==
                        "casual" &&
                    conventionRuntime.PresetIds[1] ==
                        "formal",
                    "dynamically loaded characters must auto-discover VCRAppearance/Outfits convention bindings: " +
                    conventionError,
                    failures);

                Expect(
                    conventionRuntime.SetPreset(
                        "formal",
                        "Immediate",
                        out var conventionApplyError) &&
                    !conventionCasual.activeSelf &&
                    conventionFormal.activeSelf,
                    "auto-discovered outfit roots must support immediate quick change: " +
                    conventionApplyError,
                    failures);

                Expect(
                    conventionRuntime.SetAccessory(
                        "head",
                        "hat",
                        "Immediate",
                        out var conventionAccessoryError) &&
                    conventionHat.activeSelf,
                    "auto-discovered accessory slots must support direct quick change: " +
                    conventionAccessoryError,
                    failures);

                var invalidRoot =
                    new GameObject(
                        "Invalid Appearance Runtime");
                invalidRoot.transform.SetParent(
                    root.transform,
                    false);

                var invalid =
                    invalidRoot.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "missing-commit",
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play",
                                        Required = false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var invalidError) &&
                    invalidError != null &&
                    invalidError.Contains(
                        "exactly one",
                        StringComparison.OrdinalIgnoreCase),
                    "transition definition without exactly one appearance commit must fail closed",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "out-of-order",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.75f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .EffectPlay,
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.50f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var orderingError) &&
                    orderingError != null &&
                    orderingError.Contains(
                        "ordered",
                        StringComparison.OrdinalIgnoreCase),
                    "appearance transition steps must reject decreasing timeline times",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "duration-too-short",
                            DurationSeconds =
                                0.25f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.50f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var durationError) &&
                    durationError != null &&
                    durationError.Contains(
                        "duration",
                        StringComparison.OrdinalIgnoreCase),
                    "appearance transition duration must not end before its final step",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "interrupt-without-cleanup",
                            DurationSeconds =
                                0.5f,
                            QueuePolicy =
                                AppearanceTransitionQueuePolicy
                                    .Interrupt,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.25f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var interruptError) &&
                    interruptError != null &&
                    interruptError.Contains(
                        "cleanup",
                        StringComparison.OrdinalIgnoreCase),
                    "Interrupt transitions must fail closed without explicit cancellation cleanup actions",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "interrupt-with-cleanup",
                            DurationSeconds =
                                0.5f,
                            QueuePolicy =
                                AppearanceTransitionQueuePolicy
                                    .Interrupt,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .EffectPlay,
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.25f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                },
                            CancellationSteps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .EffectStop,
                                        Text =
                                            "confetti",
                                        Required =
                                            false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    invalid.RebuildConfiguration(
                        out var cleanupError),
                    "Interrupt transitions with explicit immediate cleanup actions must validate: " +
                    cleanupError,
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "dependency-all-valid",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        StepId =
                                            "motion-a",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.1f,
                                        StepId =
                                            "motion-b",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.2f,
                                        DependencyMode =
                                            AppearanceTransitionDependencyMode
                                                .All,
                                        DependsOnStepIds =
                                            new[]
                                            {
                                                "motion-a",
                                                "motion-b"
                                            },
                                        DependencyTimeoutSeconds =
                                            2f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                },
                            CancellationSteps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .MotionRelease,
                                        Required =
                                            false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    invalid.RebuildConfiguration(
                        out var validDependencyError),
                    "All dependency graph with two earlier action step ids must validate: " +
                    validDependencyError,
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "dependency-any-valid",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        StepId =
                                            "audio-a",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .AudioPlay,
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.2f,
                                        DependencyMode =
                                            AppearanceTransitionDependencyMode
                                                .Any,
                                        DependsOnStepIds =
                                            new[]
                                            {
                                                "audio-a"
                                            },
                                        DependencyTimeoutSeconds =
                                            1f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                },
                            CancellationSteps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            EventActionTypes
                                                .AudioStop,
                                        Required =
                                            false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    invalid.RebuildConfiguration(
                        out var validAnyError),
                    "Any dependency graph must validate when it references an earlier action step: " +
                    validAnyError,
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "dependency-forward-reference",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.1f,
                                        DependencyMode =
                                            AppearanceTransitionDependencyMode
                                                .All,
                                        DependsOnStepIds =
                                            new[]
                                            {
                                                "later-action"
                                            },
                                        DependencyTimeoutSeconds =
                                            1f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.2f,
                                        StepId =
                                            "later-action",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    }
                                },
                            CancellationSteps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        ActionType =
                                            EventActionTypes
                                                .EffectStop,
                                        Required =
                                            false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var forwardDependencyError) &&
                    forwardDependencyError != null &&
                    forwardDependencyError.Contains(
                        "earlier",
                        StringComparison.OrdinalIgnoreCase),
                    "dependency graph must reject forward references",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "dependency-duplicate-step-id",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        StepId =
                                            "duplicate",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.1f,
                                        StepId =
                                            "duplicate",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.2f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var duplicateStepIdError) &&
                    duplicateStepIdError != null &&
                    duplicateStepIdError.Contains(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase),
                    "transition definition must reject duplicate action StepId values",
                    failures);

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "dependency-timeout-invalid",
                            DurationSeconds =
                                1f,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0f,
                                        StepId =
                                            "source",
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "custom.transition",
                                        Required =
                                            false
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds =
                                            0.2f,
                                        DependencyMode =
                                            AppearanceTransitionDependencyMode
                                                .All,
                                        DependsOnStepIds =
                                            new[]
                                            {
                                                "source"
                                            },
                                        DependencyTimeoutSeconds =
                                            0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                },
                            CancellationSteps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        ActionType =
                                            EventActionTypes
                                                .EffectStop,
                                        Required =
                                            false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var dependencyTimeoutError) &&
                    dependencyTimeoutError != null &&
                    dependencyTimeoutError.Contains(
                        "dependency timeout",
                        StringComparison.OrdinalIgnoreCase),
                    "dependency waits must require a finite positive timeout",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "appearance validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(root);
                }

                if (audioClip != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            audioClip);
                }

                if (motionClip != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            motionClip);
                }
            }

            ValidateCompletionProbeCache(
                failures);
            ValidateExecutorProbeIsolation(
                failures);
            ValidateDestroyedExecutorRefresh(
                failures);
            ValidateDestroyedActionHandlerRefresh(
                failures);
            ValidateDestroyedAppearanceHandlerRuntime(
                failures);
            ValidateDestroyedMotionRuntimeRefresh(
                failures);
        }

        private static void ValidateExecutorProbeIsolation(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Executor Probe Isolation Validation");

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var throwing =
                    root.AddComponent<
                        P11ThrowingProbeExecutor>();

                var runtimeType =
                    typeof(
                        BasicCharacterAppearanceRuntime);
                var executorsField =
                    runtimeType.GetField(
                        "_executors",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);
                var countExecutorsMethod =
                    runtimeType.GetMethod(
                        "TryCountExecutors",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);
                var countCompletionMethod =
                    runtimeType.GetMethod(
                        "TryCountCompletionProbes",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

                if (executorsField == null ||
                    countExecutorsMethod == null ||
                    countCompletionMethod == null)
                {
                    failures.Add(
                        "appearance executor-probe isolation validation could not resolve runtime internals");
                    return;
                }

                executorsField.SetValue(
                    runtime,
                    new IAppearanceTransitionStepExecutor[]
                    {
                        throwing
                    });

                var step =
                    new AppearanceTransitionStep
                    {
                        ActionType =
                            P11ThrowingProbeExecutor
                                .ActionType,
                        Blocking =
                            true
                    };

                throwing.ThrowCanExecute = true;
                var executorArguments =
                    new object[]
                    {
                        step,
                        0,
                        null
                    };
                var executorResult =
                    countExecutorsMethod.Invoke(
                        runtime,
                        executorArguments) is bool
                        executorSucceeded &&
                    executorSucceeded;

                Expect(
                    !executorResult &&
                    executorArguments[2] is string
                        executorError &&
                    executorError.Contains(
                        "CanExecute",
                        StringComparison.Ordinal),
                    "throwing transition CanExecute probes must fail closed with an explicit error instead of escaping the runtime",
                    failures);

                throwing.ThrowCanExecute = false;
                throwing.ThrowCanTrackCompletion = true;
                var completionArguments =
                    new object[]
                    {
                        step,
                        0,
                        null
                    };
                var completionResult =
                    countCompletionMethod.Invoke(
                        runtime,
                        completionArguments) is bool
                        completionSucceeded &&
                    completionSucceeded;

                Expect(
                    !completionResult &&
                    completionArguments[2] is string
                        completionError &&
                    completionError.Contains(
                        "CanTrackCompletion",
                        StringComparison.Ordinal),
                    "throwing completion CanTrackCompletion probes must fail closed with an explicit error instead of escaping the runtime",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "appearance executor-probe isolation validation unexpected exception: " +
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

        private static void ValidateCompletionProbeCache(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Completion Probe Cache Validation");

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var decoy =
                    root.AddComponent<
                        P11CountingDecoyExecutor>();
                var probe =
                    root.AddComponent<
                        P11CountingCompletionExecutor>();

                var executorsField =
                    typeof(
                        BasicCharacterAppearanceRuntime)
                        .GetField(
                            "_executors",
                            System.Reflection
                                .BindingFlags.Instance |
                            System.Reflection
                                .BindingFlags.NonPublic);
                var pollMethod =
                    typeof(
                        BasicCharacterAppearanceRuntime)
                        .GetMethod(
                            "TryIsStepComplete",
                            System.Reflection
                                .BindingFlags.Instance |
                            System.Reflection
                                .BindingFlags.NonPublic);

                if (executorsField == null ||
                    pollMethod == null)
                {
                    failures.Add(
                        "appearance completion-probe cache validation could not resolve runtime internals");
                    return;
                }

                executorsField.SetValue(
                    runtime,
                    new IAppearanceTransitionStepExecutor[]
                    {
                        decoy,
                        probe
                    });

                var step =
                    new AppearanceTransitionStep
                    {
                        ActionType =
                            P11CountingCompletionExecutor
                                .ActionType,
                        Blocking =
                            true
                    };

                object cache = null;
                var firstArguments =
                    new object[]
                    {
                        step,
                        cache,
                        false,
                        null
                    };
                var firstResult =
                    pollMethod.Invoke(
                        runtime,
                        firstArguments) is bool
                        firstSucceeded &&
                    firstSucceeded;
                cache =
                    firstArguments[1];
                var decoyCallsAfterFirst =
                    decoy.CanExecuteCount;
                var probeCallsAfterFirst =
                    probe.CanExecuteCount;

                var secondArguments =
                    new object[]
                    {
                        step,
                        cache,
                        false,
                        null
                    };
                var secondResult =
                    pollMethod.Invoke(
                        runtime,
                        secondArguments) is bool
                        secondSucceeded &&
                    secondSucceeded;

                Expect(
                    firstResult &&
                    secondResult &&
                    cache != null &&
                    decoyCallsAfterFirst == 1 &&
                    decoy.CanExecuteCount ==
                        decoyCallsAfterFirst &&
                    probeCallsAfterFirst == 1 &&
                    probe.CanExecuteCount ==
                        probeCallsAfterFirst + 1 &&
                    probe.CanTrackCompletionCount ==
                        2 &&
                    probe.CompletionPollCount ==
                        2,
                    "appearance transition completion polling must reuse the cached probe instead of rescanning unrelated executors on every wait-frame",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "appearance completion-probe cache validation unexpected exception: " +
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

        private static void ValidateDestroyedExecutorRefresh(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Executor Refresh Validation");

                var outfit =
                    new GameObject(
                        "Outfit");
                outfit.transform.SetParent(
                    root.transform,
                    false);

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var oldExecutor =
                    root.AddComponent<
                        P11ProbeAppearanceExecutor>();

                runtime.ConfigureBindings(
                    new[]
                    {
                        new AppearanceOutfitBinding
                        {
                            OutfitId = "target",
                            Roots = new[]
                            {
                                outfit
                            }
                        }
                    },
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    new[]
                    {
                        new AppearancePresetBinding
                        {
                            PresetId = "target",
                            OutfitId = "target"
                        }
                    },
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "refresh-executor",
                            DurationSeconds = 0.1f,
                            FallbackPolicy =
                                AppearanceTransitionFallbackPolicy
                                    .Fail,
                            Steps = new[]
                            {
                                new AppearanceTransitionStepBinding
                                {
                                    TimeSeconds = 0f,
                                    Kind =
                                        AppearanceTransitionStepKind
                                            .Action,
                                    ActionType =
                                        P11ProbeAppearanceExecutor
                                            .ActionType,
                                    Required = true
                                },
                                new AppearanceTransitionStepBinding
                                {
                                    TimeSeconds = 0.05f,
                                    Kind =
                                        AppearanceTransitionStepKind
                                            .Commit
                                }
                            }
                        }
                    },
                    new MonoBehaviour[]
                    {
                        oldExecutor
                    });

                UnityEngine.Object.DestroyImmediate(
                    oldExecutor);

                var replacementExecutor =
                    root.AddComponent<
                        P11ProbeAppearanceExecutor>();

                Expect(
                    runtime.SetPreset(
                        "target",
                        "refresh-executor",
                        out var error),
                    "appearance runtime must discard a destroyed cached transition executor and auto-discover its replacement: " +
                    error,
                    failures);

                Expect(
                    replacementExecutor != null,
                    "replacement transition executor must remain alive during refresh validation",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed transition executor refresh validation unexpected exception: " +
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

        private static void ValidateDestroyedActionHandlerRefresh(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Action Handler Refresh Validation");

                var oldHandler =
                    root.AddComponent<
                        P11FakeTransitionActionHandler>();
                var executor =
                    root.AddComponent<
                        AppearanceTransitionActionExecutor>();

                executor.SetActionHandlers(
                    oldHandler);

                var step =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind.Action,
                        ActionType =
                            "custom.transition",
                        Text =
                            "replacement-handler"
                    };

                Expect(
                    executor.CanExecute(step),
                    "transition action executor must initially resolve the configured fake handler",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldHandler);

                var replacement =
                    root.AddComponent<
                        P11FakeTransitionActionHandler>();

                Expect(
                    executor.CanExecute(step) &&
                    executor.TryExecute(
                        step,
                        out var error) &&
                    replacement.ExecutionCount == 1 &&
                    replacement.LastText ==
                        "replacement-handler",
                    "transition action executor must discard a destroyed cached event handler and auto-discover its replacement: " +
                    error,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed transition action handler refresh validation unexpected exception: " +
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

        private static void ValidateDestroyedAppearanceHandlerRuntime(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Appearance Handler Runtime Recovery");

                var oldRuntime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var handler =
                    root.AddComponent<
                        AppearanceEventActionHandler>();

                handler.SetAppearanceRuntime(
                    oldRuntime);

                var command =
                    new EventActionCommand(
                        "appearance-recovery",
                        EventActionTypes
                            .AppearanceRestoreDefault,
                        null,
                        null,
                        null,
                        0.0,
                        false,
                        40);

                Expect(
                    handler.CanHandle(
                        command),
                    "appearance event handler recovery validation must start with the configured runtime",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldRuntime);

                var replacement =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                Expect(
                    handler.CanHandle(
                        command) &&
                    replacement != null,
                    "appearance event handler must discard a destroyed cached runtime and auto-discover a live replacement",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed appearance handler runtime recovery unexpected exception: " +
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

        private static void ValidateDestroyedMotionRuntimeRefresh(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Motion Runtime Refresh Validation");

                var oldRuntime =
                    root.AddComponent<
                        BakedMotionCueSource>();
                var handler =
                    root.AddComponent<
                        MotionCueEventActionHandler>();

                handler.SetMotionRuntime(
                    oldRuntime);

                var command =
                    new EventActionCommand(
                        "motion-runtime-recovery",
                        EventActionTypes
                            .MotionRelease,
                        "motion.clips",
                        null,
                        null,
                        0.0,
                        false,
                        41);

                Expect(
                    handler.CanHandle(
                        command),
                    "motion cue handler recovery validation must start with the configured runtime",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldRuntime);

                var replacement =
                    root.AddComponent<
                        BakedMotionCueSource>();

                Expect(
                    handler.CanHandle(
                        command) &&
                    replacement != null,
                    "motion cue handler must ignore a destroyed serialized runtime during rebuild and auto-discover a live replacement",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed motion cue runtime refresh validation unexpected exception: " +
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

    internal sealed class P11ThrowingProbeExecutor :
        MonoBehaviour,
        IAppearanceTransitionStepExecutor,
        IAppearanceTransitionStepCompletionProbe
    {
        public const string ActionType =
            "probe.throwing";

        public bool ThrowCanExecute { get; set; }
        public bool ThrowCanTrackCompletion { get; set; }

        public bool CanExecute(
            AppearanceTransitionStep step)
        {
            if (ThrowCanExecute)
            {
                throw new InvalidOperationException(
                    "CanExecute validation failure");
            }

            return step != null &&
                string.Equals(
                    step.ActionType,
                    ActionType,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            AppearanceTransitionStep step,
            out string error)
        {
            error = null;
            return true;
        }

        public bool CanTrackCompletion(
            AppearanceTransitionStep step)
        {
            if (ThrowCanTrackCompletion)
            {
                throw new InvalidOperationException(
                    "CanTrackCompletion validation failure");
            }

            return CanExecute(
                step);
        }

        public bool TryIsComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;
            return true;
        }
    }

    internal sealed class P11CountingDecoyExecutor :
        MonoBehaviour,
        IAppearanceTransitionStepExecutor
    {
        public int CanExecuteCount
        {
            get;
            private set;
        }

        public bool CanExecute(
            AppearanceTransitionStep step)
        {
            CanExecuteCount++;
            return false;
        }

        public bool TryExecute(
            AppearanceTransitionStep step,
            out string error)
        {
            error =
                "decoy executor does not execute";
            return false;
        }
    }

    internal sealed class P11CountingCompletionExecutor :
        MonoBehaviour,
        IAppearanceTransitionStepExecutor,
        IAppearanceTransitionStepCompletionProbe
    {
        public const string ActionType =
            "cache.completion";

        public int CanExecuteCount
        {
            get;
            private set;
        }

        public int CanTrackCompletionCount
        {
            get;
            private set;
        }

        public int CompletionPollCount
        {
            get;
            private set;
        }

        public bool CanExecute(
            AppearanceTransitionStep step)
        {
            CanExecuteCount++;
            return step != null &&
                string.Equals(
                    step.ActionType,
                    ActionType,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            AppearanceTransitionStep step,
            out string error)
        {
            error = null;
            return true;
        }

        public bool CanTrackCompletion(
            AppearanceTransitionStep step)
        {
            CanTrackCompletionCount++;
            return step != null &&
                string.Equals(
                    step.ActionType,
                    ActionType,
                    StringComparison.Ordinal);
        }

        public bool TryIsComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            CompletionPollCount++;
            complete = false;
            error = null;
            return true;
        }
    }

    internal sealed class P11ProbeAppearanceExecutor :
        MonoBehaviour,
        IAppearanceTransitionStepExecutor
    {
        public const string ActionType =
            "probe.executor";

        public bool CanExecute(
            AppearanceTransitionStep step)
        {
            return step != null &&
                   string.Equals(
                       step.ActionType,
                       ActionType,
                       StringComparison.Ordinal);
        }

        public bool TryExecute(
            AppearanceTransitionStep step,
            out string error)
        {
            error = null;
            return CanExecute(step);
        }
    }

    internal sealed class P11ThrowingTransitionActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe
    {
        public bool ThrowCanHandle { get; set; }
        public bool ThrowCanTrackCompletion { get; set; }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (ThrowCanHandle)
            {
                throw new InvalidOperationException(
                    "CanHandle validation failure");
            }

            return true;
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;
            return true;
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            if (ThrowCanTrackCompletion)
            {
                throw new InvalidOperationException(
                    "CanTrackCompletion validation failure");
            }

            return true;
        }

        public bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;
            return true;
        }
    }

    internal sealed class P11FakeTransitionActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe
    {
        private int _checksBeforeComplete;
        private int _completionChecks;

        public int ExecutionCount { get; private set; }
        public string LastText { get; private set; }

        public void ResetCompletion(
            int checksBeforeComplete)
        {
            _checksBeforeComplete =
                Math.Max(
                    0,
                    checksBeforeComplete);
            _completionChecks = 0;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            return command.ActionType ==
                "custom.transition";
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Unsupported fake transition action.";
                return false;
            }

            ExecutionCount++;
            LastText =
                command.Text;
            return true;
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            return CanHandle(
                command);
        }

        public bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (!CanTrackCompletion(
                    command))
            {
                error =
                    "Unsupported fake transition completion action.";
                return false;
            }

            _completionChecks++;
            complete =
                _completionChecks >=
                _checksBeforeComplete;
            return true;
        }
    }
}
