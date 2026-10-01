using System;

namespace VCR.Runtime.Diagnostics
{
    public static class FrameTimingStatistics
    {
        public static void Compute(
            float[] samples,
            int count,
            float[] scratch,
            out float averageMs,
            out float p95Ms,
            out float p99Ms)
        {
            averageMs = 0f;
            p95Ms = 0f;
            p99Ms = 0f;

            if (samples == null ||
                scratch == null ||
                count <= 0)
            {
                return;
            }

            count = Math.Min(
                count,
                Math.Min(
                    samples.Length,
                    scratch.Length));

            if (count <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                scratch[i] = samples[i];
                averageMs += scratch[i];
            }

            averageMs /= count;

            Array.Sort(
                scratch,
                0,
                count);

            p95Ms =
                scratch[
                    PercentileIndex(count, 0.95)];

            p99Ms =
                scratch[
                    PercentileIndex(count, 0.99)];
        }

        public static int PercentileIndex(
            int count,
            double percentile)
        {
            if (count <= 0)
            {
                return 0;
            }

            percentile =
                Math.Max(
                    0.0,
                    Math.Min(1.0, percentile));

            return Math.Min(
                count - 1,
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        percentile * count) - 1));
        }
    }
}
