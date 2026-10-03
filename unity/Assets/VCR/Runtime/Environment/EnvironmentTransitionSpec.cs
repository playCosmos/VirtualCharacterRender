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
            Mode = mode;
            DurationSeconds =
                Math.Max(
                    0f,
                    durationSeconds);
        }

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
