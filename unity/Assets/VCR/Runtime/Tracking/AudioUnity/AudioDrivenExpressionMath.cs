using System;

namespace VCR.Runtime.Tracking.AudioUnity
{
    public static class AudioDrivenExpressionMath
    {
        public static float ComputeRms(
            float[] samples)
        {
            if (samples == null ||
                samples.Length == 0)
            {
                return 0f;
            }

            double sum = 0.0;

            for (var i = 0;
                 i < samples.Length;
                 i++)
            {
                var sample =
                    samples[i];
                sum +=
                    sample *
                    sample;
            }

            return (float)Math.Sqrt(
                sum /
                samples.Length);
        }

        public static float NormalizeLevel(
            float rms,
            float threshold,
            float gain)
        {
            if (float.IsNaN(rms) ||
                float.IsInfinity(rms))
            {
                return 0f;
            }

            threshold =
                Math.Max(
                    0f,
                    threshold);
            gain =
                Math.Max(
                    0f,
                    gain);

            var value =
                Math.Max(
                    0f,
                    rms - threshold) *
                gain;

            return Math.Min(
                1f,
                value);
        }

        public static float Smooth(
            float current,
            float target,
            float deltaSeconds,
            float attackSeconds,
            float releaseSeconds)
        {
            var duration =
                target > current
                    ? Math.Max(
                        0.001f,
                        attackSeconds)
                    : Math.Max(
                        0.001f,
                        releaseSeconds);

            var alpha =
                1f -
                (float)Math.Exp(
                    -Math.Max(
                        0f,
                        deltaSeconds) /
                    duration);

            return
                current +
                (target - current) *
                alpha;
        }
    }
}
