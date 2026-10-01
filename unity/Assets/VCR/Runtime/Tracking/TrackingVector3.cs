using System;

namespace VCR.Runtime.Tracking
{
    [Serializable]
    public readonly struct TrackingVector3
    {
        public TrackingVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static TrackingVector3 Zero => new(0f, 0f, 0f);
    }
}
