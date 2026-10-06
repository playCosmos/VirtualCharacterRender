using System;

namespace VCR.Runtime.Tracking
{
    public readonly struct TrackingSourceHealthSnapshot
    {
        public TrackingSourceHealthSnapshot(
            string sourceId,
            TrackingSourceKind kind,
            TrackingRegion regions,
            TrackingSourceHealth health,
            long lastFrameRuntimeTimestampUs)
        {
            SourceId = sourceId;
            Kind = kind;
            Regions = regions;
            Health = health;
            LastFrameRuntimeTimestampUs =
                lastFrameRuntimeTimestampUs;
        }

        public string SourceId { get; }
        public TrackingSourceKind Kind { get; }
        public TrackingRegion Regions { get; }
        public TrackingSourceHealth Health { get; }

        /// <summary>
        /// Runtime-monotonic timestamp of the newest normalized frame exposed
        /// by the provider. This is deliberately separate from
        /// Health.LastUpdateTimestampUs because device/source timestamps can
        /// use a different epoch.
        /// </summary>
        public long LastFrameRuntimeTimestampUs { get; }

        public bool IsUsable => Health.IsUsable;

        public bool Supports(
            TrackingRegion region)
        {
            return
                region != TrackingRegion.None &&
                (Regions & region) != 0;
        }

        public double FrameAgeMs(
            long nowRuntimeTimestampUs)
        {
            return TrackingTimestampMath
                .AgeMillisecondsOrNaN(
                    nowRuntimeTimestampUs,
                    LastFrameRuntimeTimestampUs);
        }
    }

    internal static class TrackingTimestampMath
    {
        public static bool TryElapsedMicroseconds(
            long nowUs,
            long sinceUs,
            out ulong elapsedUs)
        {
            elapsedUs = 0UL;

            if (nowUs < sinceUs)
            {
                return false;
            }

            elapsedUs =
                unchecked(
                    (ulong)(
                        nowUs -
                        sinceUs));
            return true;
        }

        public static double AgeMillisecondsOrNaN(
            long nowUs,
            long timestampUs)
        {
            if (timestampUs <= 0)
            {
                return double.NaN;
            }

            if (!TryElapsedMicroseconds(
                    nowUs,
                    timestampUs,
                    out var elapsedUs))
            {
                return 0.0;
            }

            return
                elapsedUs /
                1000.0;
        }
    }
}
