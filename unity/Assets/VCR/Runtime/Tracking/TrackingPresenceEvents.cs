using System;

namespace VCR.Runtime.Tracking
{
    [Flags]
    public enum TrackingPresenceEvents
    {
        None = 0,
        SubjectLost = 1 << 0,
        SubjectRestored = 1 << 1,
        TrackingSourceLost = 1 << 2,
        TrackingSourceRestored = 1 << 3
    }
}
