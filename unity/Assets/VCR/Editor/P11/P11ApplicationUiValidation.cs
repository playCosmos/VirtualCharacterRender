using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Capabilities;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.UI;

namespace VCR.Editor.P11
{
    public static class P11ApplicationUiValidation
    {
        [MenuItem("VCR/P11/Validate Application UI")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            var model =
                new ApplicationUiModel();

            var sections =
                model.CaptureSections();

            Expect(
                sections.Length ==
                    (int)ApplicationUiSection.Count,
                "application UI must expose exactly the roadmap section count",
                failures);

            Expect(
                sections.Length == 9 &&
                sections[0].Section ==
                    ApplicationUiSection.Character &&
                sections[8].Section ==
                    ApplicationUiSection.Diagnostics,
                "application UI section order must remain stable from Character through Diagnostics",
                failures);

            Expect(
                model.SelectedSection ==
                    ApplicationUiSection.Character,
                "application UI must start on Character when it is available",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Character,
                false,
                "character unavailable");

            Expect(
                model.SelectedSection ==
                    ApplicationUiSection.Tracking,
                "disabling the selected section must select the first available section",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Tracking,
                false,
                "tracking unavailable");

            Expect(
                !model.TrySelect(
                    ApplicationUiSection.Tracking) &&
                model.SelectedSection ==
                    ApplicationUiSection.MotionExpression,
                "unavailable sections must reject navigation and preserve a valid selection",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Diagnostics,
                true);

            Expect(
                model.TrySelect(
                    ApplicationUiSection.Diagnostics) &&
                model.SelectedSection ==
                    ApplicationUiSection.Diagnostics,
                "available sections must be selectable",
                failures);

            Expect(
                ApplicationUiModel.GetTitle(
                    ApplicationUiSection
                        .MotionExpression) ==
                    "Motion / Expression" &&
                ApplicationUiModel.GetTitle(
                    ApplicationUiSection
                        .CameraOutput) ==
                    "Camera / Output",
                "section titles must use stable user-facing labels",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanBrowseCharacterFile(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        true) &&
                !ApplicationUiActionPolicy
                    .CanBrowseCharacterFile(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.LoadingCharacter,
                        true) &&
                !ApplicationUiActionPolicy
                    .CanBrowseCharacterFile(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        false),
                "character browse action must require an operational scene runtime and a supported file-selection adapter",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanApplyOverlaySetting(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        true) &&
                !ApplicationUiActionPolicy
                    .CanApplyOverlaySetting(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.LoadingCharacter,
                        true) &&
                !ApplicationUiActionPolicy
                    .CanApplyOverlaySetting(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        false),
                "overlay setting actions must require an operational scene runtime and configured output adapter",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready) &&
                !ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.LoadingCharacter) &&
                !ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        false,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready),
                "runtime Settings actions must require an available operational scene runtime",
                failures);

            Expect(
                !ApplicationUiActionPolicy
                    .CanLoadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        "   "),
                "character load action must reject a blank path",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanLoadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        "C:/avatar.vrm"),
                "character load action must allow a non-empty path while the scene is operational",
                failures);

            Expect(
                !ApplicationUiActionPolicy
                    .CanLoadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.LoadingCharacter,
                        "C:/avatar.vrm") &&
                !ApplicationUiActionPolicy
                    .CanApplyBroadcastTarget(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.LoadingCharacter),
                "scene-mutating UI actions must be disabled while a character load is in progress",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanReloadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.CharacterReady,
                        true,
                        "C:/avatar.vrm") &&
                ApplicationUiActionPolicy
                    .CanUnloadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.CharacterReady,
                        true),
                "reload and unload actions must require an active character in an operational state",
                failures);

            Expect(
                !ApplicationUiActionPolicy
                    .CanReloadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        false,
                        null) &&
                !ApplicationUiActionPolicy
                    .CanUnloadCharacter(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready,
                        false),
                "reload and unload actions must be disabled when no character is loaded",
                failures);

            Expect(
                !ApplicationUiActionPolicy
                    .CanApplyBroadcastTarget(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Suspended) &&
                ApplicationUiActionPolicy
                    .CanApplyBroadcastTarget(
                        true,
                        VCR.Runtime.Scene.SceneRuntimeState.Ready),
                "broadcast target actions must follow operational scene state",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanApplyAppearancePreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "formal-crown") &&
                !ApplicationUiActionPolicy
                    .CanApplyAppearancePreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "   ") &&
                !ApplicationUiActionPolicy
                    .CanApplyAppearancePreset(
                        true,
                        AppearanceRuntimeState.Transitioning,
                        "formal-crown"),
                "direct appearance preset controls must require a ready runtime and non-empty id",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanSetAppearanceAccessory(
                        true,
                        AppearanceRuntimeState.Ready,
                        "head",
                        "crown") &&
                !ApplicationUiActionPolicy
                    .CanSetAppearanceAccessory(
                        true,
                        AppearanceRuntimeState.Ready,
                        "head",
                        "   ") &&
                ApplicationUiActionPolicy
                    .CanClearAppearanceAccessory(
                        true,
                        AppearanceRuntimeState.Ready,
                        "head"),
                "direct accessory controls must validate slot and accessory ids independently",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanPreviewAppearanceTransition(
                        true,
                        AppearanceRuntimeState.Ready,
                        "spin-confetti",
                        "formal") &&
                !ApplicationUiActionPolicy
                    .CanPreviewAppearanceTransition(
                        true,
                        AppearanceRuntimeState.Ready,
                        "Immediate",
                        "formal") &&
                !ApplicationUiActionPolicy
                    .CanPreviewAppearanceTransition(
                        true,
                        AppearanceRuntimeState.Ready,
                        "spin-confetti",
                        null),
                "transition preview must require a non-immediate transition and an active outfit",
                failures);

            Expect(
                ApplicationUiActionPolicy
                    .CanSaveAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        "C:/avatar.vrm") &&
                !ApplicationUiActionPolicy
                    .CanSaveAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "   ",
                        "C:/avatar.vrm") &&
                !ApplicationUiActionPolicy
                    .CanSaveAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        null) &&
                !ApplicationUiActionPolicy
                    .CanDeleteAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Transitioning,
                        "stream-look",
                        "C:/avatar.vrm") &&
                ApplicationUiActionPolicy
                    .CanRenameAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        "stream-look-2",
                        "C:/avatar.vrm") &&
                !ApplicationUiActionPolicy
                    .CanRenameAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        "   ",
                        "C:/avatar.vrm") &&
                ApplicationUiActionPolicy
                    .CanDuplicateAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        "stream-look-copy",
                        "C:/avatar.vrm") &&
                ApplicationUiActionPolicy
                    .CanMoveAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Ready,
                        "stream-look",
                        "C:/avatar.vrm") &&
                !ApplicationUiActionPolicy
                    .CanMoveAppearanceUserPreset(
                        true,
                        AppearanceRuntimeState.Transitioning,
                        "stream-look",
                        "C:/avatar.vrm"),
                "saved appearance preset save/delete/rename/duplicate/reorder actions must require a ready runtime, valid ids, and active character path",
                failures);

            var cancelableTransitionStatus =
                new AppearanceRuntimeStatus(
                    "appearance.main",
                    AppearanceRuntimeState
                        .Transitioning,
                    "formal-crown",
                    "formal",
                    "spin-confetti",
                    false,
                    0.4,
                    1.0,
                    0.4,
                    true,
                    null);

            Expect(
                ApplicationUiActionPolicy
                    .CanCancelAppearanceTransition(
                        true,
                        cancelableTransitionStatus) &&
                !ApplicationUiActionPolicy
                    .CanCancelAppearanceTransition(
                        false,
                        cancelableTransitionStatus) &&
                !ApplicationUiActionPolicy
                    .CanCancelAppearanceTransition(
                        true,
                        new AppearanceRuntimeStatus(
                            "appearance.main",
                            AppearanceRuntimeState
                                .Ready,
                            "formal-crown",
                            "formal",
                            null,
                            false,
                            null)),
                "transition cancel UI must require an active transition with an executable cleanup contract",
                failures);

            var motionUiObject =
                new GameObject(
                    "P11 Motion UI Validation");

            try
            {
                var validationMixer =
                    motionUiObject.AddComponent<
                        VCR.Runtime.Tracking.Mixing
                            .MotionExpressionMixer>();
                var validationManual =
                    motionUiObject.AddComponent<
                        VCR.Runtime.Tracking.Mixing
                            .ManualExpressionLayerSource>();

                validationMixer
                    .SetExpressionLayerProvider(
                        validationManual);
                validationMixer
                    .ConfigureExpressionLayer(
                        VCR.Runtime.Tracking.Mixing
                            .ExpressionBlendMode.Maximum,
                        1f,
                        0f,
                        0f);
                validationMixer
                    .SetPoseLayerProvider(
                        validationManual);

                Expect(
                    validationMixer
                        .IsExpressionLayerProvider(
                            validationManual) &&
                    validationMixer
                        .ExpressionLayerBlendMode ==
                        VCR.Runtime.Tracking.Mixing
                            .ExpressionBlendMode.Maximum &&
                    validationMixer
                        .TrySetPrimaryPoseLayerWeight(
                            0.35f,
                            out var poseWeightError) &&
                    Math.Abs(
                        validationMixer
                            .PrimaryPoseLayerWeight -
                        0.35f) <
                        0.001f,
                    "motion UI runtime contract must expose a configurable primary pose weight and Maximum manual-expression layer: " +
                    poseWeightError,
                    failures);

                Expect(
                    validationManual.SetExpression(
                        VCR.Runtime.Tracking
                            .StandardExpression.Happy,
                        0.75f) &&
                    Math.Abs(
                        validationManual.GetExpression(
                            VCR.Runtime.Tracking
                                .StandardExpression.Happy) -
                        0.75f) <
                        0.001f &&
                    validationManual.ClearExpression(
                        VCR.Runtime.Tracking
                            .StandardExpression.Happy) &&
                    Math.Abs(
                        validationManual.GetExpression(
                            VCR.Runtime.Tracking
                                .StandardExpression.Happy)) <
                        0.001f,
                    "manual expression UI source must support set/read/clear semantics",
                    failures);

                var picker =
                    motionUiObject.AddComponent<
                        DesktopCharacterFileSelectionAdapter>();

                Expect(
                    picker.IsSupported &&
                    !string.IsNullOrWhiteSpace(
                        picker.AdapterId),
                    "P11 editor validation must expose a supported character file-selection adapter without opening the dialog",
                    failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(
                    motionUiObject);
            }

            var capabilityRegistry =
                new CapabilityRegistry();

            try
            {
                Expect(
                    capabilityRegistry.Register(
                        "zeta",
                        () =>
                            new MemoryStream()) &&
                    capabilityRegistry.Register(
                        "alpha",
                        () =>
                            new MemoryStream()),
                    "capability validation registry must accept unique factories",
                    failures);

                Expect(
                    capabilityRegistry.Enable(
                        "zeta",
                        out var capabilityEnableError),
                    "capability validation enable must succeed: " +
                    capabilityEnableError,
                    failures);

                var capabilityStatuses =
                    capabilityRegistry
                        .CaptureStatuses();

                Expect(
                    capabilityStatuses.Length ==
                        2 &&
                    capabilityStatuses[0].Id ==
                        "alpha" &&
                    capabilityStatuses[0].State ==
                        CapabilityState.Disabled &&
                    capabilityStatuses[1].Id ==
                        "zeta" &&
                    capabilityStatuses[1].State ==
                        CapabilityState.Enabled,
                    "capability status snapshots must be sorted by id and preserve enabled/disabled state",
                    failures);
            }
            finally
            {
                capabilityRegistry.Dispose();
            }

            var eventStoreDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    "vcr-p11-event-ui-" +
                    Guid.NewGuid()
                        .ToString("N"));
            var eventStorePath =
                Path.Combine(
                    eventStoreDirectory,
                    "event-rules.json");

            try
            {
                var eventStore =
                    new EventRuntimeConfigurationStore(
                        eventStorePath);
                var eventRules =
                    new[]
                    {
                        new EventRuntimeRule
                        {
                            Id =
                                "rule-b",
                            Enabled =
                                false
                        },
                        new EventRuntimeRule
                        {
                            Id =
                                "rule-a",
                            Enabled =
                                true
                        }
                    };

                string eventSaveError =
                    null;
                EventRuntimeRule[] loadedRules =
                    Array.Empty<
                        EventRuntimeRule>();
                var loadedMaxCommands =
                    0;
                string eventLoadError =
                    null;

                var saved =
                    eventStore.TrySave(
                        eventRules,
                        73,
                        out eventSaveError);
                var loaded =
                    saved &&
                    eventStore.TryLoad(
                        out loadedRules,
                        out loadedMaxCommands,
                        out eventLoadError);

                Expect(
                    saved &&
                    loaded &&
                    loadedRules.Length ==
                        2 &&
                    loadedRules[0].Id ==
                        "rule-b" &&
                    !loadedRules[0].Enabled &&
                    loadedRules[1].Id ==
                        "rule-a" &&
                    loadedRules[1].Enabled &&
                    loadedMaxCommands ==
                        73,
                    "P11 event rule persistence must preserve rule order/enabled state and max commands: " +
                    eventSaveError +
                    " / " +
                    eventLoadError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P11 event rule persistence validation unexpected exception: " +
                    exception);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(
                            eventStoreDirectory))
                    {
                        Directory.Delete(
                            eventStoreDirectory,
                            recursive:
                                true);
                    }
                }
                catch
                {
                }
            }

            P11AppearanceRuntimeValidation
                .RunChecks(
                    failures);

            P11AppearanceTransitionTimelineValidation
                .RunChecks(
                    failures);

            P11ExternalMotionImportValidation
                .RunChecks(
                    failures);

            var unavailable =
                model.CaptureSections();

            Expect(
                !unavailable[
                    (int)ApplicationUiSection
                        .Tracking].Available &&
                unavailable[
                    (int)ApplicationUiSection
                        .Tracking]
                    .UnavailableReason ==
                    "tracking unavailable",
                "section snapshots must preserve explicit unavailable reasons",
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P11 application UI validation: PASS " +
                    "(section order, availability, character file browse policy, overlay/settings policy, capability snapshots, event-rule persistence, fallback selection, direct appearance action policy, persisted quick change, baked motion cues, external motion import, cancellation policy, serialized timeline authoring contract)");
                return true;
            }

            Debug.LogError(
                "VCR P11 application UI validation: FAIL\n" +
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
