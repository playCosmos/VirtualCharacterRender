using System.Diagnostics;

namespace VCR.Runtime.Core
{
    public static class MonotonicClock
    {
        private static readonly double TickToMicroseconds =
            1_000_000.0 / Stopwatch.Frequency;

        public static long NowMicroseconds()
        {
            return (long)(
                Stopwatch.GetTimestamp() *
                TickToMicroseconds);
        }
    }
}
