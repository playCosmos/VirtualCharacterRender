using System;

namespace VCR.Runtime.Tracking
{
    [Flags]
    public enum TrackingRegion
    {
        None = 0,
        Face = 1 << 0,
        Head = 1 << 1,
        LeftHand = 1 << 2,
        RightHand = 1 << 3,
        UpperBody = 1 << 4,
        FullBody = 1 << 5,

        Hands = LeftHand | RightHand,
        Baseline = Face | Head | Hands | UpperBody
    }
}
