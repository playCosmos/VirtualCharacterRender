using System;

namespace VCR.Runtime.Environment
{
    [Serializable]
    public readonly struct EnvironmentTransitionSpec
    {
        public EnvironmentTransitionSpec(
            EnvironmentTransitionMode mode,
            float durationSeconds)
        {
            Mode =
                IsSupportedMode(mode)
                    ? mode
                    : EnvironmentTransitionMode.Cut;
            DurationSeconds =
                IsFinite(durationSeconds)
                    ? Math.Max(
                        0f,
                        durationSeconds)
                    : 0f;
        }

        private static bool IsSupportedMode(
            EnvironmentTransitionMode mode) =>
                (int)mode >=
                    (int)EnvironmentTransitionMode.Cut &&
                (int)mode <=
                    (int)EnvironmentTransitionMode.Dissolve;

        private static bool IsFinite(
            float value) =>
                !float.IsNaN(value) &&
                !float.IsInfinity(value);

        public EnvironmentTransitionMode Mode { get; }
        public float DurationSeconds { get; }

        public bool IsImmediate =>
            Mode ==
                EnvironmentTransitionMode.Cut ||
            DurationSeconds <= 0f;

        public static EnvironmentTransitionSpec Cut =>
            new(
                EnvironmentTransitionMode.Cut,
                0f);
    }
}
