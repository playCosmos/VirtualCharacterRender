using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Engine-independent immutable face/head state for one performer.
    ///
    /// The default constructor preserves the historical allocation-free
    /// ownership-transfer behavior. Prefer the explicit ownership overload at
    /// producer boundaries so callers state whether they relinquish or copy
    /// their coefficient buffer.
    /// </summary>
    public sealed class NormalizedFaceState
    {
        private readonly float[] _coefficients;

        public NormalizedFaceState(
            TrackingQuaternion headRotation,
            TrackingVector3 headPosition,
            float[] coefficients)
            : this(
                headRotation,
                headPosition,
                coefficients,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public NormalizedFaceState(
            TrackingQuaternion headRotation,
            TrackingVector3 headPosition,
            float[] coefficients,
            SnapshotArrayOwnership ownership)
        {
            HeadRotation = headRotation;
            HeadPosition = headPosition;
            _coefficients =
                SnapshotArrayOwnershipUtility.Acquire(
                    coefficients,
                    (int)FaceCoefficient.Count,
                    ownership,
                    nameof(coefficients));
        }

        public TrackingQuaternion HeadRotation { get; }
        public TrackingVector3 HeadPosition { get; }

        public float Get(FaceCoefficient coefficient)
        {
            var index = (int)coefficient;
            if (index < 0 ||
                index >=
                    (int)FaceCoefficient.Count)
            {
                return 0f;
            }

            return _coefficients[index];
        }

        public float EyeOpenLeft =>
            1f -
            Get(
                FaceCoefficient.EyeBlinkLeft);

        public float EyeOpenRight =>
            1f -
            Get(
                FaceCoefficient.EyeBlinkRight);

        public float MouthOpen =>
            Get(
                FaceCoefficient.JawOpen);
    }
}
