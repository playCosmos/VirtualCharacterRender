namespace VCR.Runtime.Tracking
{
    public enum HumanoidPoseSpace
    {
        /// <summary>
        /// Original/non-normalized avatar-local humanoid transforms.
        /// This is the VMC Protocol default for VRM1.
        /// </summary>
        OriginalLocal = 0,

        /// <summary>
        /// Normalized humanoid local transforms suitable for a ControlRig.
        /// </summary>
        NormalizedLocal = 1
    }
}
