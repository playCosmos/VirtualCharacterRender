using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Output;

namespace VCR.Editor.P10
{
    public static class P10BroadcastOutputValidation
    {
        [MenuItem("VCR/P10/Validate Broadcast Output")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateStatusCompatibility(
                failures);
            ValidateCaptureReadiness(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P10 broadcast output validation: PASS " +
                    "(status compatibility, pending/fault/unsupported containment, transparency and client-size readiness)");
                return true;
            }

            Debug.LogError(
                "VCR P10 broadcast output validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateStatusCompatibility(
            List<string> failures)
        {
            var active =
                new OverlayOutputStatus(
                    supported: true,
                    active: true,
                    adapterId: "legacy",
                    lastError: null,
                    clientWidth: 1280,
                    clientHeight: 720);

            Expect(
                active.State ==
                    OverlayOutputState.Active &&
                active.Supported &&
                active.Active &&
                !active.Pending &&
                !active.Faulted,
                "legacy active status constructor must map to Active without breaking existing adapters",
                failures);

            var fault =
                new OverlayOutputStatus(
                    supported: true,
                    active: false,
                    adapterId: "legacy",
                    lastError: "fault",
                    clientWidth: 0,
                    clientHeight: 0);

            Expect(
                fault.State ==
                    OverlayOutputState.Faulted &&
                fault.Faulted &&
                fault.Supported,
                "legacy status with an error must map to Faulted",
                failures);

            var unsupported =
                new OverlayOutputStatus(
                    supported: false,
                    active: false,
                    adapterId: "legacy",
                    lastError: null,
                    clientWidth: 0,
                    clientHeight: 0);

            Expect(
                unsupported.State ==
                    OverlayOutputState.Unsupported &&
                !unsupported.Supported,
                "legacy unsupported status must map to Unsupported",
                failures);
        }

        private static void ValidateCaptureReadiness(
            List<string> failures)
        {
            var transparent =
                new OverlayOutputSettings(
                    transparent: true,
                    topmost: true,
                    clickThrough: false);

            var ready =
                OverlayCaptureReadinessEvaluator
                    .Evaluate(
                        new OverlayOutputStatus(
                            OverlayOutputState.Active,
                            "test",
                            null,
                            1920,
                            1080),
                        transparent);

            Expect(
                ready.Ready &&
                ready.Failure ==
                    OverlayCaptureReadinessFailure.None,
                "active transparent output with positive client size must be capture-ready",
                failures);

            ExpectFailure(
                OverlayOutputState.PendingNativeApply,
                null,
                1920,
                1080,
                transparent,
                OverlayCaptureReadinessFailure
                    .NativeApplyPending,
                failures);

            ExpectFailure(
                OverlayOutputState.Faulted,
                "native failure",
                1920,
                1080,
                transparent,
                OverlayCaptureReadinessFailure
                    .Faulted,
                failures);

            ExpectFailure(
                OverlayOutputState.Unsupported,
                null,
                1920,
                1080,
                transparent,
                OverlayCaptureReadinessFailure
                    .Unsupported,
                failures);

            ExpectFailure(
                OverlayOutputState.Inactive,
                null,
                1920,
                1080,
                transparent,
                OverlayCaptureReadinessFailure
                    .NotActive,
                failures);

            var opaque =
                new OverlayOutputSettings(
                    transparent: false,
                    topmost: true,
                    clickThrough: false);

            ExpectFailure(
                OverlayOutputState.Active,
                null,
                1920,
                1080,
                opaque,
                OverlayCaptureReadinessFailure
                    .TransparencyDisabled,
                failures);

            ExpectFailure(
                OverlayOutputState.Active,
                null,
                0,
                1080,
                transparent,
                OverlayCaptureReadinessFailure
                    .InvalidClientSize,
                failures);
        }

        private static void ExpectFailure(
            OverlayOutputState state,
            string error,
            int width,
            int height,
            OverlayOutputSettings settings,
            OverlayCaptureReadinessFailure expected,
            List<string> failures)
        {
            var result =
                OverlayCaptureReadinessEvaluator
                    .Evaluate(
                        new OverlayOutputStatus(
                            state,
                            "test",
                            error,
                            width,
                            height),
                        settings);

            Expect(
                !result.Ready &&
                result.Failure == expected,
                $"capture readiness failure must be {expected} for state={state}",
                failures);
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
