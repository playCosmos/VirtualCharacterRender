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
            ValidateRecovery(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P10 broadcast output validation: PASS " +
                    "(status compatibility, pending/fault/unsupported containment, transparency/client-size readiness, settings-preserving recovery)");
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

        private static void ValidateRecovery(
            List<string> failures)
        {
            var settings =
                new OverlayOutputSettings(
                    transparent: true,
                    topmost: false,
                    clickThrough: true);

            var recovered =
                new P10FakeOverlayAdapter(
                    settings,
                    new OverlayOutputStatus(
                        OverlayOutputState.Faulted,
                        "fake",
                        "initial fault",
                        1280,
                        720),
                    new OverlayOutputStatus(
                        OverlayOutputState.PendingNativeApply,
                        "fake",
                        null,
                        1280,
                        720));

            Expect(
                OverlayOutputRecovery.TryRestart(
                    recovered,
                    out var recoverError) &&
                string.IsNullOrEmpty(
                    recoverError) &&
                recovered.ShutdownCount == 1 &&
                recovered.ApplyCount == 1 &&
                recovered.LastAppliedSettings.Transparent ==
                    settings.Transparent &&
                recovered.LastAppliedSettings.Topmost ==
                    settings.Topmost &&
                recovered.LastAppliedSettings.ClickThrough ==
                    settings.ClickThrough,
                "overlay recovery must preserve requested settings and accept a non-faulted pending restart",
                failures);

            var unsupported =
                new P10FakeOverlayAdapter(
                    settings,
                    new OverlayOutputStatus(
                        OverlayOutputState.Faulted,
                        "fake",
                        "fault",
                        0,
                        0),
                    new OverlayOutputStatus(
                        OverlayOutputState.Unsupported,
                        "fake",
                        null,
                        0,
                        0));

            Expect(
                !OverlayOutputRecovery.TryRestart(
                    unsupported,
                    out var unsupportedError) &&
                !string.IsNullOrWhiteSpace(
                    unsupportedError),
                "overlay recovery must fail closed when the restarted adapter reports unsupported",
                failures);

            var throwing =
                new P10FakeOverlayAdapter(
                    settings,
                    default,
                    default)
                {
                    ThrowOnApply = true
                };

            Expect(
                !OverlayOutputRecovery.TryRestart(
                    throwing,
                    out var thrownError) &&
                !string.IsNullOrWhiteSpace(
                    thrownError),
                "overlay recovery must contain adapter exceptions",
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

    internal sealed class P10FakeOverlayAdapter :
        IOverlayOutputAdapter
    {
        private readonly OverlayOutputStatus _restartStatus;
        private OverlayOutputStatus _status;

        public P10FakeOverlayAdapter(
            OverlayOutputSettings settings,
            OverlayOutputStatus initialStatus,
            OverlayOutputStatus restartStatus)
        {
            Settings = settings;
            _status = initialStatus;
            _restartStatus = restartStatus;
            LastAppliedSettings = settings;
        }

        public bool ThrowOnApply { get; set; }
        public int ApplyCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public OverlayOutputSettings LastAppliedSettings { get; private set; }
        public OverlayOutputStatus Status => _status;
        public OverlayOutputSettings Settings { get; private set; }

        public void Apply(
            OverlayOutputSettings settings)
        {
            ApplyCount++;

            if (ThrowOnApply)
            {
                throw new System.InvalidOperationException(
                    "synthetic apply failure");
            }

            Settings = settings;
            LastAppliedSettings = settings;
            _status = _restartStatus;
        }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }
}
