namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Optional capability used by a router to suspend an expensive fallback
    /// face tracker while a preferred face source (for example ARKit) is healthy.
    /// </summary>
    public interface IFaceTrackingActivationControl
    {
        bool FaceTrackingEnabled { get; }
        void SetFaceTrackingEnabled(bool enabled);
    }

    /// <summary>
    /// Optional capability used by a router to suspend an expensive expression
    /// fallback while a higher-priority expression source is healthy.
    /// </summary>
    public interface IExpressionTrackingActivationControl
    {
        bool ExpressionTrackingEnabled { get; }
        void SetExpressionTrackingEnabled(bool enabled);
    }
}
