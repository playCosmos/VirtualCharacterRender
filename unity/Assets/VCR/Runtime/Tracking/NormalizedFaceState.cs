using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Engine-independent face/head state for one performer.
    /// </summary>
    public sealed class NormalizedFaceState
    {
        private readonly float[] _coefficients;

        public NormalizedFaceState(
            TrackingQuaternion headRotation,
            TrackingVector3 headPosition,
            float[] coefficients)
        {
            HeadRotation = headRotation;
            HeadPosition = headPosition;
            _coefficients = coefficients ?? throw new ArgumentNullException(nameof(coefficients));

            if (_coefficients.Length != (int)FaceCoefficient.Count)
            {
                throw new ArgumentException(
                    $"Expected {(int)FaceCoefficient.Count} coefficients, got {_coefficients.Length}.",
                    nameof(coefficients));
            }
        }

        public TrackingQuaternion HeadRotation { get; }
        public TrackingVector3 HeadPosition { get; }

        public float Get(FaceCoefficient coefficient)
        {
            var index = (int)coefficient;
            if (index < 0 || index >= (int)FaceCoefficient.Count)
            {
                return 0f;
            }

            return _coefficients[index];
        }

        public float EyeOpenLeft => 1f - Get(FaceCoefficient.EyeBlinkLeft);
        public float EyeOpenRight => 1f - Get(FaceCoefficient.EyeBlinkRight);
        public float MouthOpen => Get(FaceCoefficient.JawOpen);
    }
}
