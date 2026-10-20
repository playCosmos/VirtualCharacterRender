using System;
using System.Threading;

namespace VCR.Runtime.Tracking.ArKit
{
    /// <summary>
    /// Parsed iFacialMocap/FaceMotion3D frame before transport-specific
    /// head-axis conversion.
    ///
    /// Coefficients are exposed read-only. The transport adapter may detach the
    /// backing array once to transfer it into the normalized immutable snapshot
    /// without a second per-frame copy.
    /// </summary>
    public sealed class IFacialMocapFrame
    {
        private float[] _coefficients;

        public IFacialMocapFrame(
            float[] coefficients,
            bool hasHead,
            float headEulerXDegrees,
            float headEulerYDegrees,
            float headEulerZDegrees,
            float headPositionX,
            float headPositionY,
            float headPositionZ)
            : this(
                coefficients,
                hasHead,
                headEulerXDegrees,
                headEulerYDegrees,
                headEulerZDegrees,
                headPositionX,
                headPositionY,
                headPositionZ,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public IFacialMocapFrame(
            float[] coefficients,
            bool hasHead,
            float headEulerXDegrees,
            float headEulerYDegrees,
            float headEulerZDegrees,
            float headPositionX,
            float headPositionY,
            float headPositionZ,
            SnapshotArrayOwnership ownership)
        {
            _coefficients =
                SnapshotArrayOwnershipUtility.Acquire(
                    coefficients,
                    (int)FaceCoefficient.Count,
                    ownership,
                    nameof(coefficients));

            HasHead = hasHead;
            HeadEulerXDegrees = headEulerXDegrees;
            HeadEulerYDegrees = headEulerYDegrees;
            HeadEulerZDegrees = headEulerZDegrees;
            HeadPositionX = headPositionX;
            HeadPositionY = headPositionY;
            HeadPositionZ = headPositionZ;
        }

        public ReadOnlySpan<float> Coefficients =>
            Volatile.Read(
                ref _coefficients) ??
            Array.Empty<float>();

        public bool HasHead { get; }

        public float HeadEulerXDegrees { get; }
        public float HeadEulerYDegrees { get; }
        public float HeadEulerZDegrees { get; }

        public float HeadPositionX { get; }
        public float HeadPositionY { get; }
        public float HeadPositionZ { get; }

        public float[] DetachCoefficientOwnership()
        {
            var coefficients =
                Interlocked.Exchange(
                    ref _coefficients,
                    null);

            if (coefficients == null)
            {
                throw new InvalidOperationException(
                    "iFacialMocap coefficient ownership was already detached.");
            }

            return coefficients;
        }
    }
}
