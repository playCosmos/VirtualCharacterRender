using System;

namespace VCR.Runtime.Environment
{
    /// <summary>
    /// Drop-only scheduler for explicit environment update classes.
    ///
    /// It never catches up with bursts after a stall. Static/EventDriven never
    /// request recurring ticks; Hz10/Hz30 schedule from the latest dispatch.
    /// </summary>
    public sealed class EnvironmentUpdateScheduler
    {
        private EnvironmentUpdatePolicy _policy;
        private long _nextDueUs;
        private long _lastDispatchUs = -1;

        public EnvironmentUpdatePolicy Policy =>
            _policy;

        public bool HasRecurringUpdates =>
            _policy ==
                EnvironmentUpdatePolicy.Hz10 ||
            _policy ==
                EnvironmentUpdatePolicy.Hz30 ||
            _policy ==
                EnvironmentUpdatePolicy.EveryFrame;

        public void Configure(
            EnvironmentUpdatePolicy policy,
            long nowUs)
        {
            _policy = policy;
            _lastDispatchUs = -1;
            _nextDueUs =
                HasRecurringUpdates
                    ? nowUs
                    : long.MaxValue;
        }

        public bool IsDue(long nowUs)
        {
            if (!HasRecurringUpdates)
            {
                return false;
            }

            if (_policy ==
                EnvironmentUpdatePolicy.EveryFrame)
            {
                return true;
            }

            return nowUs >= _nextDueUs;
        }

        public float MarkDispatched(long nowUs)
        {
            var deltaSeconds =
                _lastDispatchUs < 0
                    ? 0f
                    : (float)Math.Max(
                        0.0,
                        (nowUs -
                         _lastDispatchUs) /
                        1_000_000.0);

            _lastDispatchUs = nowUs;

            var intervalUs =
                IntervalMicroseconds(
                    _policy);

            _nextDueUs =
                intervalUs > 0
                    ? nowUs + intervalUs
                    : nowUs;

            return deltaSeconds;
        }

        public static long IntervalMicroseconds(
            EnvironmentUpdatePolicy policy)
        {
            return policy switch
            {
                EnvironmentUpdatePolicy.Hz10 =>
                    100_000,
                EnvironmentUpdatePolicy.Hz30 =>
                    33_333,
                _ => 0
            };
        }
    }
}
