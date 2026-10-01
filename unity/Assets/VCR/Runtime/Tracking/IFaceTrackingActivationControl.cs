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
}
