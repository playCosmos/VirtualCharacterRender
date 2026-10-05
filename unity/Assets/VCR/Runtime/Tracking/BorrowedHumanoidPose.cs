using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Allocation-free, non-owning view of one humanoid pose sample.
    ///
    /// The backing arrays remain owned by the provider and may be overwritten
    /// by the next borrow/capture call. Consumers must finish reading this value
    /// synchronously and must never publish or retain it as an immutable
    /// TrackingFrame payload.
    /// </summary>
    public readonly struct BorrowedHumanoidPose
    {
        private readonly NormalizedBonePose[] _bones;
        private readonly bool[] _hasBone;

        public BorrowedHumanoidPose(
            HumanoidPoseSpace poseSpace,
            TrackingVector3 rootPosition,
            TrackingQuaternion rootRotation,
            NormalizedBonePose[] bones,
            bool[] hasBone)
        {
            if (bones == null)
            {
                throw new ArgumentNullException(
                    nameof(bones));
            }

            if (hasBone == null)
            {
                throw new ArgumentNullException(
                    nameof(hasBone));
            }

            if (bones.Length !=
                    (int)HumanoidBoneId.Count ||
                hasBone.Length !=
                    (int)HumanoidBoneId.Count)
            {
                throw new ArgumentException(
                    "Borrowed humanoid pose arrays must match HumanoidBoneId.Count.");
            }

            PoseSpace = poseSpace;
            RootPosition = rootPosition;
            RootRotation = rootRotation;
            _bones = bones;
            _hasBone = hasBone;
        }

        public HumanoidPoseSpace PoseSpace { get; }
        public TrackingVector3 RootPosition { get; }
        public TrackingQuaternion RootRotation { get; }

        public bool IsValid =>
            _bones != null &&
            _hasBone != null;

        public bool TryGet(
            HumanoidBoneId bone,
            out NormalizedBonePose pose)
        {
            var index =
                (int)bone;

            if (_bones == null ||
                _hasBone == null ||
                index < 0 ||
                index >= _bones.Length ||
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

    /// <summary>
    /// Optional synchronous zero-allocation pose path for consumers such as
    /// protocol senders that serialize a pose immediately and do not retain it.
    /// </summary>
    public interface IBorrowedHumanoidPoseProvider
    {
        bool TryBorrowHumanoidPose(
            out BorrowedHumanoidPose pose);
    }
}
