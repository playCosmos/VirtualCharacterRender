namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral immutable humanoid-pose envelope.
    ///
    /// The transform space is explicit because VMC normally transports the
    /// sender avatar's original local bone rotations, while ControlRig consumes
    /// normalized local rotations. Transfer ownership is allocation-free and
    /// requires the producer to stop mutating both pose arrays after creation.
    /// </summary>
    public sealed class HumanoidPoseState
    {
        private readonly NormalizedBonePose[] _bones;
        private readonly bool[] _hasBone;

        public HumanoidPoseState(
            HumanoidPoseSpace poseSpace,
            TrackingVector3 rootPosition,
            TrackingQuaternion rootRotation,
            NormalizedBonePose[] bones,
            bool[] hasBone)
            : this(
                poseSpace,
                rootPosition,
                rootRotation,
                bones,
                hasBone,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public HumanoidPoseState(
            HumanoidPoseSpace poseSpace,
            TrackingVector3 rootPosition,
            TrackingQuaternion rootRotation,
            NormalizedBonePose[] bones,
            bool[] hasBone,
            SnapshotArrayOwnership ownership)
        {
            PoseSpace = poseSpace;
            RootPosition = rootPosition;
            RootRotation = rootRotation;

            _bones =
                SnapshotArrayOwnershipUtility.Acquire(
                    bones,
                    (int)HumanoidBoneId.Count,
                    ownership,
                    nameof(bones));

            _hasBone =
                SnapshotArrayOwnershipUtility.Acquire(
                    hasBone,
                    (int)HumanoidBoneId.Count,
                    ownership,
                    nameof(hasBone));
        }

        public HumanoidPoseSpace PoseSpace { get; }
        public TrackingVector3 RootPosition { get; }
        public TrackingQuaternion RootRotation { get; }

        public bool TryGet(
            HumanoidBoneId bone,
            out NormalizedBonePose pose)
        {
            var index =
                (int)bone;

            if (index < 0 ||
                index >=
                    _bones.Length ||
                !_hasBone[index])
            {
                pose = default;
                return false;
            }

            pose =
                _bones[index];
            return true;
        }
    }
}
