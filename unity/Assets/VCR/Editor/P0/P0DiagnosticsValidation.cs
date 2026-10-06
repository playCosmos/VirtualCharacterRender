using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Diagnostics;

namespace VCR.Editor.P0
{
    public static class P0DiagnosticsValidation
    {
        [MenuItem("VCR/P0/Validate Diagnostics Math")]
        public static void Validate()
        {
            GameObject root = null;
            var runtimeBounds = false;

            try
            {
                root =
                    new GameObject(
                        "VCR P0 Diagnostics Bounds");
                var diagnostics =
                    root.AddComponent<
                        RuntimeDiagnostics>();
                root.AddComponent<
                    P0ThrowingMetricsSource>();

                SetPrivateField(
                    diagnostics,
                    "reportIntervalSeconds",
                    0f);
                SetPrivateField(
                    diagnostics,
                    "frameWindowFrames",
                    0);
                InvokeAwake(
                    diagnostics);

                runtimeBounds =
                    Mathf.Approximately(
                        diagnostics
                            .ReportIntervalSeconds,
                        1f) &&
                    diagnostics
                        .FrameWindowFrames ==
                        120 &&
                    GetPrivateArrayLength(
                        diagnostics,
                        "_frameMs") ==
                        120;

                SetPrivateField(
                    diagnostics,
                    "reportIntervalSeconds",
                    float.NaN);
                SetPrivateField(
                    diagnostics,
                    "frameWindowFrames",
                    int.MaxValue);

                runtimeBounds =
                    runtimeBounds &&
                    Mathf.Approximately(
                        diagnostics
                            .ReportIntervalSeconds,
                        5f) &&
                    diagnostics
                        .FrameWindowFrames ==
                        3600;

                var survivingSubscriberCount =
                    0;
                System.Action<RuntimeDiagnosticsSnapshot>
                    throwingSubscriber = _ =>
                        throw new System.InvalidOperationException(
                            "synthetic diagnostics subscriber failure");
                System.Action<RuntimeDiagnosticsSnapshot>
                    survivingSubscriber = _ =>
                    {
                        survivingSubscriberCount++;
                    };

                diagnostics.SnapshotUpdated +=
                    throwingSubscriber;
                diagnostics.SnapshotUpdated +=
                    survivingSubscriber;

                runtimeBounds =
                    runtimeBounds &&
                    GetPrivateArrayLength(
                        diagnostics,
                        "_snapshotSubscribers") == 2;

                InvokeSnapshotNotification(
                    diagnostics);

                diagnostics.SnapshotUpdated -=
                    survivingSubscriber;

                runtimeBounds =
                    runtimeBounds &&
                    GetPrivateArrayLength(
                        diagnostics,
                        "_snapshotSubscribers") == 1;

                InvokeSnapshotNotification(
                    diagnostics);

                var collectedMetrics =
                    new List<RuntimeMetric>();
                InvokeMetricCollection(
                    diagnostics,
                    collectedMetrics);

                runtimeBounds =
                    runtimeBounds &&
                    survivingSubscriberCount == 1 &&
                    TryGetMetric(
                        collectedMetrics,
                        "diagnostics.metric_source_failures",
                        out var metricFailures) &&
                    metricFailures >= 1.0 &&
                    TryGetMetric(
                        collectedMetrics,
                        "diagnostics.snapshot_subscriber_failures",
                        out var subscriberFailures) &&
                    subscriberFailures >= 2.0;
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(
                        root);
                }
            }

            var samples = new float[100];
            var scratch = new float[100];

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = i + 1;
            }

            FrameTimingStatistics.Compute(
                samples,
                samples.Length,
                scratch,
                out var average,
                out var p95,
                out var p99);

            var percentileClamp =
                FrameTimingStatistics.PercentileIndex(10, -1.0) == 0 &&
                FrameTimingStatistics.PercentileIndex(10, 2.0) == 9;

            var pass =
                Mathf.Approximately(average, 50.5f) &&
                Mathf.Approximately(p95, 95f) &&
                Mathf.Approximately(p99, 99f) &&
                percentileClamp &&
                runtimeBounds;

            if (pass)
            {
                Debug.Log(
                    "VCR P0 diagnostics math: PASS " +
                    $"avg={average:F1} p95={p95:F1} p99={p99:F1}");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 diagnostics math: FAIL " +
                    $"avg={average:F3} p95={p95:F3} p99={p99:F3}");
            }
        }

        private static void InvokeMetricCollection(
            RuntimeDiagnostics diagnostics,
            List<RuntimeMetric> output)
        {
            var method =
                typeof(RuntimeDiagnostics)
                    .GetMethod(
                        "CollectSubsystemMetrics",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new System.MissingMethodException(
                    typeof(RuntimeDiagnostics)
                        .FullName,
                    "CollectSubsystemMetrics");
            }

            method.Invoke(
                diagnostics,
                new object[]
                {
                    output
                });
        }

        private static void InvokeSnapshotNotification(
            RuntimeDiagnostics diagnostics)
        {
            var method =
                typeof(RuntimeDiagnostics)
                    .GetMethod(
                        "NotifySnapshotUpdated",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new System.MissingMethodException(
                    typeof(RuntimeDiagnostics)
                        .FullName,
                    "NotifySnapshotUpdated");
            }

            method.Invoke(
                diagnostics,
                new object[]
                {
                    diagnostics.LatestSnapshot
                });
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in metrics)
            {
                if (metric.Name == name)
                {
                    value = metric.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (field == null)
            {
                throw new System.MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            field.SetValue(
                target,
                value);
        }

        private static int GetPrivateArrayLength(
            object target,
            string fieldName)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (field?.GetValue(target) is
                System.Array array)
            {
                return array.Length;
            }

            return -1;
        }

        private static void InvokeAwake(
            RuntimeDiagnostics diagnostics)
        {
            var method =
                typeof(RuntimeDiagnostics)
                    .GetMethod(
                        "Awake",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new System.MissingMethodException(
                    typeof(RuntimeDiagnostics)
                        .FullName,
                    "Awake");
            }

            method.Invoke(
                diagnostics,
                null);
        }
    }

    internal sealed class P0ThrowingMetricsSource :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            throw new System.InvalidOperationException(
                "synthetic metrics failure");
        }
    }
}
