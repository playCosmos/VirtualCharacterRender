using UnityEditor;
using UnityEngine;
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
}
