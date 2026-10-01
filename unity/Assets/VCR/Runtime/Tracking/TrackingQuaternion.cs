using System;

namespace VCR.Runtime.Tracking
{
    [Serializable]
    public readonly struct TrackingQuaternion
    {
        public TrackingQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public static TrackingQuaternion Identity => new(0f, 0f, 0f, 1f);
    }
}
