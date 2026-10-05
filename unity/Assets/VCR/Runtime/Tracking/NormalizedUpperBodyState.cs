namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Immutable upper-body joint positions in normalized tracking
    /// coordinates: +X right, +Y up, +Z forward.
    /// </summary>
    public sealed class NormalizedUpperBodyState
    {
        private readonly TrackingPoint[] _joints;

        public NormalizedUpperBodyState(
            TrackingPoint[] joints)
            : this(
                joints,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public NormalizedUpperBodyState(
            TrackingPoint[] joints,
            SnapshotArrayOwnership ownership)
        {
            _joints =
                SnapshotArrayOwnershipUtility.Acquire(
                    joints,
                    (int)UpperBodyJoint.Count,
                    ownership,
                    nameof(joints));
        }

        public TrackingPoint Get(
            UpperBodyJoint joint)
        {
            var index =
                (int)joint;

            return
                index >= 0 &&
                index < _joints.Length
                    ? _joints[index]
                    : default;
        }
    }
}
