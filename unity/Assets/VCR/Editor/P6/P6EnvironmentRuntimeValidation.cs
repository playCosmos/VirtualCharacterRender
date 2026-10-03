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
                    "(state roots, binding guards, event/manual dispatch, recurring-policy driver, drop-only scheduler)");
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

                runtime.Configure(
                    "environment.p6.test",
                    "day",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

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

                runtime.ConfigureUpdateTargets(
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

                runtime.RequestUpdate();

                Expect(
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
                    driver.enabled,
                    "Hz10 policy must lazily create and enable the recurring update driver",
                    failures);

                runtime.Configure(
                    "environment.p6.test",
                    "night",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                Expect(
                    driver != null &&
                    !driver.enabled,
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

                var metrics =
                    new List<RuntimeMetric>();
                runtime.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.state_changes",
                        out var changes) &&
                    Math.Abs(changes - 1.0) <
                    0.001,
                    "environment diagnostics must expose state change count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.manual_updates",
                        out var manualUpdates) &&
                    Math.Abs(
                        manualUpdates - 1.0) <
                    0.001,
                    "environment diagnostics must expose manual update count",
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
