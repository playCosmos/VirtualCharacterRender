namespace VCR.Runtime.Tracking
{
    public sealed class NormalizedHandState
    {
        private readonly TrackingPoint[] _joints;

        public NormalizedHandState(
            bool isLeft,
            TrackingPoint[] joints)
            : this(
                isLeft,
                joints,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public NormalizedHandState(
            bool isLeft,
            TrackingPoint[] joints,
            SnapshotArrayOwnership ownership)
        {
            IsLeft = isLeft;
            _joints =
                SnapshotArrayOwnershipUtility.Acquire(
                    joints,
                    (int)HandJoint.Count,
                    ownership,
                    nameof(joints));
        }

        public bool IsLeft { get; }

        public TrackingPoint Get(
            HandJoint joint)
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
