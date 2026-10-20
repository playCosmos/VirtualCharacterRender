namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// UI/application-facing control boundary for one configured tracking
    /// source runtime. Implementations keep device/protocol details private.
    /// </summary>
    public interface ITrackingRuntimeControl
    {
        string ControlId { get; }
        string DisplayName { get; }
        bool ControlEnabled { get; }
        TrackingSourceHealthState ControlHealthState { get; }
        string ControlError { get; }

        bool TrySetControlEnabled(
            bool enabled,
            out string error);

        bool TryRecover(
            out string error);
    }
}
