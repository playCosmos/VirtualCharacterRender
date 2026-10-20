using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral immutable humanoid-pose envelope.
    ///
    /// The transform space is explicit because VMC normally transports the
    /// sender avatar's original local bone rotations, while ControlRig consumes
    /// normalized local rotations. Bone presence is stored as one 64-bit mask
    /// (HumanoidBoneId.Count is currently below 64), avoiding a second array in
    /// immutable pose snapshots. Legacy bool[] construction remains supported.
    /// </summary>
    public sealed class HumanoidPoseState
    {
        private readonly NormalizedBonePose[] _bones;
        private readonly ulong _boneMask;

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
            : this(
                poseSpace,
                rootPosition,
                rootRotation,
                bones,
                BuildBoneMask(
                    hasBone,
                    ownership),
                ownership)
        {
        }

        public HumanoidPoseState(
            HumanoidPoseSpace poseSpace,
            TrackingVector3 rootPosition,
            TrackingQuaternion rootRotation,
            NormalizedBonePose[] bones,
            ulong boneMask,
            SnapshotArrayOwnership ownership =
                SnapshotArrayOwnership.Transfer)
        {
            if ((int)HumanoidBoneId.Count >
                64)
            {
                throw new InvalidOperationException(
                    "HumanoidPoseState bone-mask storage supports at most 64 humanoid bones.");
            }

            PoseSpace = poseSpace;
            RootPosition = rootPosition;
            RootRotation = rootRotation;

            _bones =
                SnapshotArrayOwnershipUtility.Acquire(
                    bones,
                    (int)HumanoidBoneId.Count,
                    ownership,
                    nameof(bones));

            _boneMask =
                boneMask &
                ValidBoneMask;
        }

        public HumanoidPoseSpace PoseSpace { get; }
        public TrackingVector3 RootPosition { get; }
        public TrackingQuaternion RootRotation { get; }

        public ulong BoneMask =>
            _boneMask;

        public bool TryGet(
            HumanoidBoneId bone,
            out NormalizedBonePose pose)
        {
            var index =
                (int)bone;

            if (index < 0 ||
                index >=
                    _bones.Length ||
                (_boneMask &
                 (1UL << index)) == 0)
            {
                pose = default;
                return false;
            }

            pose =
                _bones[index];
            return true;
        }

        public static ulong CreateBoneMask(
            bool[] hasBone)
        {
            return BuildBoneMask(
                hasBone,
                SnapshotArrayOwnership.Transfer);
        }

        public static ulong BoneBit(
            HumanoidBoneId bone)
        {
            var index =
                (int)bone;

            return index >= 0 &&
                   index <
                       (int)HumanoidBoneId.Count
                ? 1UL << index
                : 0UL;
        }

        private static ulong BuildBoneMask(
            bool[] hasBone,
            SnapshotArrayOwnership ownership)
        {
            if (hasBone == null)
            {
                throw new ArgumentNullException(
                    nameof(hasBone));
            }

            if (hasBone.Length !=
                (int)HumanoidBoneId.Count)
            {
                throw new ArgumentException(
                    $"Expected {(int)HumanoidBoneId.Count} items, got {hasBone.Length}.",
                    nameof(hasBone));
            }

            if (ownership !=
                    SnapshotArrayOwnership.Transfer &&
                ownership !=
                    SnapshotArrayOwnership.Copy)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ownership),
                    ownership,
                    "Unknown snapshot array ownership mode.");
            }

            if ((int)HumanoidBoneId.Count >
                64)
            {
                throw new InvalidOperationException(
                    "HumanoidPoseState bone-mask storage supports at most 64 humanoid bones.");
            }

            ulong mask = 0;

            for (var i = 0;
                 i < hasBone.Length;
                 i++)
            {
                if (hasBone[i])
                {
                    mask |=
                        1UL << i;
                }
            }

            return mask;
        }

        private static ulong ValidBoneMask =>
            (int)HumanoidBoneId.Count >= 64
                ? ulong.MaxValue
                : (1UL <<
                    (int)HumanoidBoneId.Count) -
                  1UL;
    }
}
