using System;

namespace VCR.Runtime.Tracking
{
    public sealed class NormalizedHandState
    {
        private readonly TrackingPoint[] _joints;

        public NormalizedHandState(bool isLeft, TrackingPoint[] joints)
        {
            IsLeft = isLeft;
            _joints = joints ?? throw new ArgumentNullException(nameof(joints));

            if (_joints.Length != (int)HandJoint.Count)
            {
                throw new ArgumentException(
                    $"Expected {(int)HandJoint.Count} joints, got {_joints.Length}.",
                    nameof(joints));
            }
        }

        public bool IsLeft { get; }

        public TrackingPoint Get(HandJoint joint)
        {
            return _joints[(int)joint];
        }
    }
}
