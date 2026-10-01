using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral local humanoid pose plus an optional model root transform.
    /// VMC bone transforms map directly to this domain.
    /// </summary>
    public sealed class NormalizedHumanoidPose
    {
        private readonly NormalizedBonePose[] _bones;
        private readonly bool[] _hasBone;

        public NormalizedHumanoidPose(
            TrackingVector3 rootPosition,
            TrackingQuaternion rootRotation,
            NormalizedBonePose[] bones,
            bool[] hasBone)
        {
            RootPosition = rootPosition;
            RootRotation = rootRotation;
            _bones = bones ?? throw new ArgumentNullException(nameof(bones));
            _hasBone = hasBone ?? throw new ArgumentNullException(nameof(hasBone));

            if (_bones.Length != (int)HumanoidBoneId.Count ||
                _hasBone.Length != (int)HumanoidBoneId.Count)
            {
                throw new ArgumentException(
                    "Humanoid pose arrays must match HumanoidBoneId.Count.");
            }
        }

        public TrackingVector3 RootPosition { get; }
        public TrackingQuaternion RootRotation { get; }

        public bool TryGet(
            HumanoidBoneId bone,
            out NormalizedBonePose pose)
        {
            var index = (int)bone;
            if (index < 0 ||
                index >= _bones.Length ||
                !_hasBone[index])
            {
                pose = default;
                return false;
            }

            pose = _bones[index];
            return true;
        }
    }
}
