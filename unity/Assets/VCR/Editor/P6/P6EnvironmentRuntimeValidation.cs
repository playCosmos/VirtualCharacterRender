using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;

namespace VCR.Editor.P6
{
    public static class P6EnvironmentRuntimeValidation
    {
        [MenuItem("VCR/P6/Validate Environment Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures = new List<string>();

            ValidateScheduler(failures);
            ValidateRuntime(failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P6 environment runtime validation: PASS " +
                    "(state roots, World/Camera/Screen/Character anchors, atomic binding guards, event/manual/scheduled dispatch, failure isolation, recurring-policy driver, drop-only scheduler)");
                return true;
            }

            Debug.LogError(
                "VCR P6 environment runtime validation: FAIL\n" +
                string.Join("\n", failures));
            return false;
        }

        private static void ValidateScheduler(
            List<string> failures)
        {
            var scheduler =
                new EnvironmentUpdateScheduler();

            scheduler.Configure(
                EnvironmentUpdatePolicy.Static,
                nowUs: 0);

            Expect(
                !scheduler.HasRecurringUpdates &&
                !scheduler.IsDue(1_000_000),
                "Static policy must never schedule recurring updates",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.EventDriven,
                nowUs: 0);

            Expect(
                !scheduler.HasRecurringUpdates &&
                !scheduler.IsDue(1_000_000),
                "EventDriven policy must never schedule recurring updates",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.Hz10,
                nowUs: 1_000_000);

            Expect(
                scheduler.IsDue(1_000_000),
                "Hz10 must allow its first scheduled update immediately",
                failures);

            scheduler.MarkDispatched(
                1_000_000);

            Expect(
                !scheduler.IsDue(1_099_999) &&
                scheduler.IsDue(1_100_000),
                "Hz10 interval must be 100ms",
                failures);

            scheduler.MarkDispatched(
                1_500_000);

            Expect(
                !scheduler.IsDue(1_500_000) &&
                !scheduler.IsDue(1_599_999) &&
                scheduler.IsDue(1_600_000),
                "stalled Hz10 updates must schedule from the latest dispatch instead of burst-catching up",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.EveryFrame,
                nowUs: 0);

            Expect(
                scheduler.IsDue(0) &&
                scheduler.IsDue(1),
                "EveryFrame policy must be due whenever the driver runs",
                failures);
        }

        private static void ValidateRuntime(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P6 Environment Runtime Validation");

                var day =
                    new GameObject("Day");
                var night =
                    new GameObject("Night");

                day.transform.SetParent(
                    root.transform,
                    false);
                night.transform.SetParent(
                    root.transform,
                    false);

                var runtime =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                var target =
                    root.AddComponent<
                        P6FakeEnvironmentUpdateTarget>();

                var worldAnchor =
                    new GameObject("World Anchor");
                var cameraAnchor =
                    new GameObject("Camera Anchor");
                var screenAnchor =
                    new GameObject("Screen Anchor");
                var characterAnchor =
                    new GameObject("Character Anchor");
                var spaceContent =
                    new GameObject("Space Content");

                worldAnchor.transform.SetParent(
                    root.transform,
                    false);
                cameraAnchor.transform.SetParent(
                    root.transform,
                    false);
                screenAnchor.transform.SetParent(
                    root.transform,
                    false);
                characterAnchor.transform.SetParent(
                    root.transform,
                    false);
                spaceContent.transform.SetParent(
                    worldAnchor.transform,
                    false);

                var dayCanvas =
                    day.AddComponent<CanvasGroup>();
                var nightCanvas =
                    night.AddComponent<CanvasGroup>();

                var dayCanvasBinding =
                    new EnvironmentCanvasGroupBinding();
                dayCanvasBinding.Configure(
                    "day",
                    dayCanvas);

                var nightCanvasBinding =
                    new EnvironmentCanvasGroupBinding();
                nightCanvasBinding.Configure(
                    "night",
                    nightCanvas);

                var canvasTransition =
                    root.AddComponent<
                        CanvasGroupEnvironmentTransitionTarget>();
                canvasTransition.Configure(
                    dayCanvasBinding,
                    nightCanvasBinding);

                Expect(
                    canvasTransition
                        .ValidateEnvironmentTransition(
                            new EnvironmentTransitionSpec(
                                EnvironmentTransitionMode.Crossfade,
                                0.5f),
                            "day",
                            "night",
                            out var canvasTransitionError) &&
                    string.IsNullOrEmpty(
                        canvasTransitionError),
                    "CanvasGroup transition target must validate a complete Crossfade binding",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        sequence: 1,
                        timestampUs: 0,
                        previousStateId: "day",
                        stateId: "night",
                        EnvironmentTransitionMode.Crossfade,
                        progress: 0.25f,
                        deltaSeconds: 0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha - 0.75f) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha - 0.25f) <
                        0.001f,
                    "CanvasGroup Crossfade must apply complementary outgoing/incoming alpha",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        sequence: 2,
                        timestampUs: 0,
                        previousStateId: "night",
                        stateId: "day",
                        EnvironmentTransitionMode.Cut,
                        progress: 1f,
                        deltaSeconds: 0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha - 1f) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha) <
                        0.001f,
                    "Cut notification must restore the active CanvasGroup alpha and clear inactive state alpha",
                    failures);

                Expect(
                    !canvasTransition
                        .ValidateEnvironmentTransition(
                            new EnvironmentTransitionSpec(
                                EnvironmentTransitionMode.Dissolve,
                                0.5f),
                            "day",
                            "night",
                            out var dissolveError) &&
                    !string.IsNullOrEmpty(
                        dissolveError),
                    "CanvasGroup transition target must explicitly reject shader-driven Dissolve",
                    failures);

                var spaceTarget =
                    spaceContent.AddComponent<
                        EnvironmentSpaceAnchor>();

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    screenAnchor.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                runtime.Configure(
                    "environment.p6.test",
                    "day",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                runtime.SetSpaceTargets(
                    spaceTarget,
                    spaceTarget);

                Expect(
                    runtime.SpaceTargetCount == 1 &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        worldAnchor.transform),
                    "space targets must be de-duplicated and apply the current World anchor",
                    failures);

                spaceContent.transform.localPosition =
                    new Vector3(
                        3f,
                        4f,
                        5f);

                var cameraApplied =
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Camera,
                        out var spaceError);

                Expect(
                    cameraApplied &&
                    string.IsNullOrEmpty(
                        spaceError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Camera &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        cameraAnchor.transform) &&
                    spaceContent.transform.localPosition ==
                        Vector3.zero,
                    "Camera space must reparent to the camera anchor and reset local transform",
                    failures);

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    spaceContent.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                var invalidScreen =
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Screen,
                        out var invalidScreenError);

                Expect(
                    !invalidScreen &&
                    !string.IsNullOrEmpty(
                        invalidScreenError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Camera &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        cameraAnchor.transform),
                    "invalid Screen anchor must fail validation without partially moving content or changing the active space mode",
                    failures);

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    screenAnchor.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                Expect(
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Screen,
                        out _) &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        screenAnchor.transform),
                    "Screen space must use the explicit screen/Canvas anchor",
                    failures);

                Expect(
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Character,
                        out _) &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        characterAnchor.transform),
                    "Character space must follow the explicit character anchor",
                    failures);

                var dayBinding =
                    new EnvironmentStateBinding();
                dayBinding.Configure(
                    "day",
                    day);

                var nightBinding =
                    new EnvironmentStateBinding();
                nightBinding.Configure(
                    "night",
                    night);

                var configured =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            dayBinding,
                            nightBinding
                        },
                        out var bindingError);

                runtime.SetUpdateTargets(
                    target);

                Expect(
                    configured &&
                    string.IsNullOrEmpty(
                        bindingError),
                    "valid environment state bindings must configure",
                    failures);

                Expect(
                    day.activeSelf &&
                    !night.activeSelf,
                    "configuring bindings must apply the current state root immediately",
                    failures);

                var stateEvents = 0;
                runtime.StateChanged += _ =>
                {
                    stateEvents++;
                };

                var changed =
                    runtime.SetState(
                        "night",
                        out var stateError);

                Expect(
                    changed &&
                    string.IsNullOrEmpty(
                        stateError) &&
                    !day.activeSelf &&
                    night.activeSelf,
                    "SetState must atomically switch bound roots",
                    failures);

                Expect(
                    stateEvents == 1 &&
                    target.UpdateCount == 1 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.StateChanged &&
                    target.LastContext.StateId ==
                        "night",
                    "state change must emit one state event and one environment update dispatch",
                    failures);

                runtime.SetState(
                    "night",
                    out _);

                Expect(
                    stateEvents == 1 &&
                    target.UpdateCount == 1,
                    "repeating the active state must be idempotent",
                    failures);

                var unknown =
                    runtime.SetState(
                        "missing",
                        out var missingError);

                Expect(
                    !unknown &&
                    !string.IsNullOrEmpty(
                        missingError) &&
                    !day.activeSelf &&
                    night.activeSelf &&
                    target.UpdateCount == 1,
                    "unknown states must fail without partially toggling roots or dispatching updates",
                    failures);

                var unsupportedTransition =
                    runtime.SetState(
                        "day",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var unsupportedTransitionError);

                Expect(
                    !unsupportedTransition &&
                    !string.IsNullOrEmpty(
                        unsupportedTransitionError) &&
                    !day.activeSelf &&
                    night.activeSelf,
                    "non-Cut transition must be rejected when no transition target is configured",
                    failures);

                var transitionTarget =
                    root.AddComponent<
                        P6FakeEnvironmentTransitionTarget>();

                runtime.SetTransitionTargets(
                    transitionTarget,
                    transitionTarget);

                var transitionStarted =
                    runtime.SetState(
                        "day",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var transitionError);

                Expect(
                    transitionStarted &&
                    string.IsNullOrEmpty(
                        transitionError) &&
                    runtime.TransitionStatus.Active &&
                    runtime.TransitionTargetCount == 1 &&
                    day.activeSelf &&
                    night.activeSelf &&
                    transitionTarget.ApplyCount == 1 &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress) <
                    0.001f,
                    "Crossfade must keep both roots active, de-duplicate targets, and dispatch progress 0",
                    failures);

                var transitionStartUs =
                    runtime.TransitionStatus
                        .StartedAtTimestampUs;

                Expect(
                    runtime.TickTransition(
                        transitionStartUs +
                        250_000) &&
                    runtime.TransitionStatus.Active &&
                    Math.Abs(
                        runtime.TransitionStatus.Progress -
                        0.5f) <
                    0.01f &&
                    day.activeSelf &&
                    night.activeSelf &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress -
                        0.5f) <
                    0.01f,
                    "Crossfade midpoint must preserve both roots and dispatch progress 0.5",
                    failures);

                Expect(
                    runtime.TickTransition(
                        transitionStartUs +
                        500_000) &&
                    !runtime.TransitionStatus.Active &&
                    day.activeSelf &&
                    !night.activeSelf &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress -
                        1f) <
                    0.001f,
                    "Crossfade completion must keep only the new root active and dispatch progress 1",
                    failures);

                Expect(
                    runtime.RequestManualUpdate() &&
                    target.UpdateCount == 2 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.Manual,
                    "manual environment updates must dispatch explicitly without requiring a recurring driver",
                    failures);

                Expect(
                    root.GetComponent<
                        EnvironmentUpdateDriver>() ==
                    null,
                    "EventDriven environment must not allocate a recurring Update driver",
                    failures);

                runtime.Configure(
                    "environment.p6.test",
                    "night",
                    EnvironmentUpdatePolicy.Hz10,
                    EnvironmentSpaceMode.World);

                var driver =
                    root.GetComponent<
                        EnvironmentUpdateDriver>();

                Expect(
                    driver != null &&
                    driver.enabled &&
                    runtime.RecurringUpdatesActive,
                    "Hz10 policy must lazily create and enable the recurring update driver",
                    failures);

                var firstDue =
                    MonotonicClock
                        .NowMicroseconds();

                Expect(
                    runtime.TickScheduled(
                        firstDue),
                    "Hz10 runtime must dispatch the first due scheduled update",
                    failures);

                Expect(
                    !runtime.TickScheduled(
                        firstDue + 50_000),
                    "Hz10 runtime must reject early scheduled ticks",
                    failures);

                Expect(
                    runtime.TickScheduled(
                        firstDue + 100_000),
                    "Hz10 runtime must dispatch the next scheduled update after 100ms",
                    failures);

                Expect(
                    runtime.ScheduledDispatchCount == 2 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.Scheduled,
                    "scheduled dispatch count must include only due ticks",
                    failures);

                runtime.Configure(
                    "environment.p6.test",
                    "night",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                Expect(
                    driver != null &&
                    !driver.enabled &&
                    !runtime.RecurringUpdatesActive,
                    "returning to EventDriven must disable the recurring update driver",
                    failures);

                var duplicate =
                    new EnvironmentStateBinding();
                duplicate.Configure(
                    "night",
                    day);

                var duplicateAccepted =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            nightBinding,
                            duplicate
                        },
                        out var duplicateError);

                Expect(
                    !duplicateAccepted &&
                    !string.IsNullOrEmpty(
                        duplicateError) &&
                    night.activeSelf,
                    "duplicate binding ids must be rejected without replacing the active binding set",
                    failures);

                var unsafeBinding =
                    new EnvironmentStateBinding();
                unsafeBinding.Configure(
                    "night",
                    root);

                var unsafeAccepted =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            unsafeBinding
                        },
                        out var unsafeError);

                Expect(
                    !unsafeAccepted &&
                    !string.IsNullOrEmpty(
                        unsafeError),
                    "state roots that contain the runtime itself must be rejected",
                    failures);

                var throwingTarget =
                    root.AddComponent<
                        P6ThrowingEnvironmentUpdateTarget>();

                runtime.SetUpdateTargets(
                    target,
                    throwingTarget);

                var beforeFailureDispatch =
                    target.UpdateCount;

                Expect(
                    runtime.RequestManualUpdate() &&
                    target.UpdateCount ==
                        beforeFailureDispatch + 1 &&
                    runtime.UpdateFailureCount == 1,
                    "one failing update target must not stop healthy targets or the environment runtime",
                    failures);

                Expect(
                    runtime.UpdateTargetCount == 2,
                    "runtime must retain distinct healthy and failing update targets",
                    failures);

                runtime.UnregisterUpdateTarget(
                    throwingTarget);

                Expect(
                    runtime.UpdateTargetCount == 1,
                    "dynamic update target unregister must remove only the requested target",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                runtime.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.state_changes",
                        out var changes) &&
                    Math.Abs(changes - 2.0) <
                    0.001,
                    "environment diagnostics must expose state change count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.space_targets",
                        out var spaceTargetCount) &&
                    Math.Abs(
                        spaceTargetCount - 1.0) <
                    0.001,
                    "environment diagnostics must expose de-duplicated space target count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.space_mode",
                        out var spaceModeMetric) &&
                    Math.Abs(
                        spaceModeMetric -
                        (int)EnvironmentSpaceMode.Character) <
                    0.001,
                    "environment diagnostics must expose the active space mode",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_targets",
                        out var transitionTargets) &&
                    Math.Abs(
                        transitionTargets - 1.0) <
                    0.001,
                    "environment diagnostics must expose de-duplicated transition target count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_count",
                        out var transitionCount) &&
                    Math.Abs(
                        transitionCount - 1.0) <
                    0.001,
                    "environment diagnostics must expose non-Cut transition count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_ticks",
                        out var transitionTicks) &&
                    Math.Abs(
                        transitionTicks - 2.0) <
                    0.001,
                    "environment diagnostics must expose transition progress tick count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_failures",
                        out var transitionFailures) &&
                    transitionFailures < 0.5,
                    "valid transition target must not produce transition failures",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.state_dispatches",
                        out var stateDispatches) &&
                    Math.Abs(
                        stateDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose state dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.manual_dispatches",
                        out var manualDispatches) &&
                    Math.Abs(
                        manualDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose manual dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.scheduled_dispatches",
                        out var scheduledDispatches) &&
                    Math.Abs(
                        scheduledDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose scheduled dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.update_failures",
                        out var updateFailures) &&
                    Math.Abs(
                        updateFailures - 1.0) <
                    0.001,
                    "environment diagnostics must expose isolated update-target failures",
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
            }
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in metrics)
            {
                if (string.Equals(
                    metric.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    value = metric.Value;
                    return true;
                }
            }

            value = 0.0;
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

    internal sealed class P6FakeEnvironmentTransitionTarget :
        MonoBehaviour,
        IEnvironmentTransitionTarget
    {
        public int ApplyCount { get; private set; }
        public EnvironmentTransitionContext LastContext { get; private set; }

        public bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error)
        {
            error = null;
            return
                !transition.IsImmediate &&
                !string.IsNullOrEmpty(previousStateId) &&
                !string.IsNullOrEmpty(nextStateId);
        }

        public void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context)
        {
            ApplyCount++;
            LastContext = context;
        }
    }

    internal sealed class P6ThrowingEnvironmentUpdateTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget
    {
        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
            throw new InvalidOperationException(
                "P6 validation target failure");
        }
    }

    internal sealed class P6FakeEnvironmentUpdateTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget
    {
        public int UpdateCount { get; private set; }
        public EnvironmentUpdateContext LastContext { get; private set; }

        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
            UpdateCount++;
            LastContext = context;
        }
    }
}
