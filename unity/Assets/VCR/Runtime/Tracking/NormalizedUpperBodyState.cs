using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Upper-body joint positions in normalized tracking coordinates:
    /// +X right, +Y up, +Z forward.
    /// </summary>
    public sealed class NormalizedUpperBodyState
    {
        private readonly TrackingPoint[] _joints;

        public NormalizedUpperBodyState(TrackingPoint[] joints)
        {
            _joints = joints ?? throw new ArgumentNullException(nameof(joints));

            if (_joints.Length != (int)UpperBodyJoint.Count)
            {
                throw new ArgumentException(
                    $"Expected {(int)UpperBodyJoint.Count} joints, got {_joints.Length}.",
                    nameof(joints));
            }
        }

        public TrackingPoint Get(UpperBodyJoint joint)
        {
            return _joints[(int)joint];
        }
    }
}
