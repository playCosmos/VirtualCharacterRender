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
            Red = Clamp01(red);
            Green = Clamp01(green);
            Blue = Clamp01(blue);
            IntensityMultiplier =
                Math.Max(
                    0f,
                    intensityMultiplier);
            Weight = Clamp01(weight);
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
            float value)
        {
            return Math.Max(
                0f,
                Math.Min(
                    1f,
                    value));
        }
    }
}
