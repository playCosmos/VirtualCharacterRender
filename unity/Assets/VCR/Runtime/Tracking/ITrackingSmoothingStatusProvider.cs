namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Declares whether the final provider already applied temporal smoothing
    /// to character-facing pose/expression channels.
    ///
    /// Character targets use this to avoid smoothing the same channel twice.
    /// Neutral-return smoothing remains target-owned.
    /// </summary>
    public interface ITrackingSmoothingStatusProvider
    {
        bool HumanoidPosePreSmoothed { get; }
        bool ExpressionsPreSmoothed { get; }
    }
}
