using System;

namespace VCR.Runtime.Tracking
{
    [Serializable]
    public readonly struct TrackingPoint
    {
        public TrackingPoint(TrackingVector3 position, float confidence)
        {
            Position = position;
            Confidence = confidence;
        }

        public TrackingVector3 Position { get; }
        public float Confidence { get; }
    }
}
