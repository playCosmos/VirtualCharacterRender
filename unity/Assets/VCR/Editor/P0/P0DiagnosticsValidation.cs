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
                percentileClamp;

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
    }
}
