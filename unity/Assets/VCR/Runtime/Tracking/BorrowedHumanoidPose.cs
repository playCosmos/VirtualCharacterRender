using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Allocation-free, non-owning view of one humanoid pose sample.
    ///
    /// The backing arrays remain owned by the provider and may be overwritten
    /// by a later pose/motion borrow. Consumers must finish reading this value
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
    /// Non-owning expression view backed by provider-owned reusable arrays.
    /// </summary>
    public readonly struct BorrowedExpressionState
    {
        private readonly float[] _standard;
        private readonly NamedExpressionValue[] _custom;
        private readonly int _customCount;

        public BorrowedExpressionState(
            float[] standard,
            NamedExpressionValue[] custom,
            int customCount)
        {
            if (standard == null)
            {
                throw new ArgumentNullException(
                    nameof(standard));
            }

            if (standard.Length !=
                (int)StandardExpression.Count)
            {
                throw new ArgumentException(
                    "Borrowed standard expression array has an invalid length.",
                    nameof(standard));
            }

            if (custom == null)
            {
                throw new ArgumentNullException(
                    nameof(custom));
            }

            if (customCount < 0 ||
                customCount > custom.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(customCount));
            }

            _standard = standard;
            _custom = custom;
            _customCount = customCount;
        }

        public bool IsValid =>
            _standard != null &&
            _custom != null;

        public float Get(
            StandardExpression expression)
        {
            var index =
                (int)expression;

            if (_standard == null ||
                index < 0 ||
                index >= _standard.Length)
            {
                return 0f;
            }

            return _standard[index];
        }

        public ReadOnlySpan<
            NamedExpressionValue> Custom =>
                _custom == null
                    ? ReadOnlySpan<
                        NamedExpressionValue>.Empty
                    : _custom.AsSpan(
                        0,
                        _customCount);
    }

    /// <summary>
    /// One synchronous borrowed motion sample. Every included domain is sampled
    /// together so a consumer never has to keep one borrowed domain alive while
    /// invoking the provider again for another.
    /// </summary>
    public readonly struct BorrowedMotionSample
    {
        public BorrowedMotionSample(
            TrackingRegion validRegions,
            BorrowedHumanoidPose humanoidPose,
            BorrowedExpressionState expressions)
        {
            ValidRegions = validRegions;
            HumanoidPose = humanoidPose;
            Expressions = expressions;
        }

        public TrackingRegion ValidRegions { get; }
        public BorrowedHumanoidPose HumanoidPose { get; }
        public BorrowedExpressionState Expressions { get; }

        public bool HasHumanoidPose =>
            HumanoidPose.IsValid;

        public bool HasExpressions =>
            Expressions.IsValid;
    }

    /// <summary>
    /// Preferred synchronous zero-allocation contract for protocol/output
    /// consumers. The returned sample remains valid only until the provider's
    /// next borrowed pose/motion sampling call.
    /// </summary>
    public interface IBorrowedNormalizedMotionProvider
    {
        bool TryBorrowMotion(
            in NormalizedMotionSnapshotRequest request,
            out BorrowedMotionSample sample);
    }

    /// <summary>
    /// Pose-only compatibility contract for synchronous consumers.
    /// </summary>
    public interface IBorrowedHumanoidPoseProvider
    {
        bool TryBorrowHumanoidPose(
            out BorrowedHumanoidPose pose);
    }
}
