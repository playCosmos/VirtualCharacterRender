using System;

namespace VCR.Runtime.Environment
{
    [Serializable]
    public readonly struct EnvironmentLightingProfile
    {
        public EnvironmentLightingProfile(
            float red,
            float green,
            float blue,
            float intensityMultiplier,
            float weight)
        {
            Red =
                Clamp01(
                    red,
                    fallback:
                        1f);
            Green =
                Clamp01(
                    green,
                    fallback:
                        1f);
            Blue =
                Clamp01(
                    blue,
                    fallback:
                        1f);
            IntensityMultiplier =
                IsFinite(
                    intensityMultiplier)
                    ? Math.Max(
                        0f,
                        intensityMultiplier)
                    : 1f;
            Weight =
                Clamp01(
                    weight,
                    fallback:
                        0f);
        }

        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public float IntensityMultiplier { get; }
        public float Weight { get; }

        public static EnvironmentLightingProfile Neutral =>
            new(
                1f,
                1f,
                1f,
                1f,
                0f);

        private static float Clamp01(
            float value,
            float fallback)
        {
            if (!IsFinite(value))
            {
                return fallback;
            }

            return Math.Max(
                0f,
                Math.Min(
                    1f,
                    value));
        }

        private static bool IsFinite(
            float value) =>
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
    }
}
