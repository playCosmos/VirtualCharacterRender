using System;

namespace VCR.Runtime.Tracking
{
    [Serializable]
    public readonly struct NormalizedBonePose
    {
        public NormalizedBonePose(
            TrackingVector3 localPosition,
            TrackingQuaternion localRotation)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public TrackingVector3 LocalPosition { get; }
        public TrackingQuaternion LocalRotation { get; }
    }
}
