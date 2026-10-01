namespace VCR.Runtime.Tracking.ArKit
{
    /// <summary>
    /// Parsed iFacialMocap/FaceMotion3D frame before transport-specific
    /// head-axis conversion.
    /// </summary>
    public sealed class IFacialMocapFrame
    {
        public IFacialMocapFrame(
            float[] coefficients,
            bool hasHead,
            float headEulerXDegrees,
            float headEulerYDegrees,
            float headEulerZDegrees,
            float headPositionX,
            float headPositionY,
            float headPositionZ)
        {
            Coefficients = coefficients;
            HasHead = hasHead;
            HeadEulerXDegrees = headEulerXDegrees;
            HeadEulerYDegrees = headEulerYDegrees;
            HeadEulerZDegrees = headEulerZDegrees;
            HeadPositionX = headPositionX;
            HeadPositionY = headPositionY;
            HeadPositionZ = headPositionZ;
        }

        public float[] Coefficients { get; }
        public bool HasHead { get; }

        public float HeadEulerXDegrees { get; }
        public float HeadEulerYDegrees { get; }
        public float HeadEulerZDegrees { get; }

        public float HeadPositionX { get; }
        public float HeadPositionY { get; }
        public float HeadPositionZ { get; }
    }
}
