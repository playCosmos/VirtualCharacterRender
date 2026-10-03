using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
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
                        "C:/avatar.vrm"),
                "saved appearance preset actions must require a ready runtime, preset id, and active character path",
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

            P11AppearanceRuntimeValidation
                .RunChecks(
                    failures);

            P11AppearanceTransitionTimelineValidation
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
                    "(section order, availability, fallback selection, direct appearance action policy, persisted quick change, baked motion cues, cancellation policy, serialized timeline authoring contract)");
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
