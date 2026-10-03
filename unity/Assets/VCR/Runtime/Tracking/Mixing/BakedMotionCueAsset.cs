using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class BakedBoneMotionCueTrack
    {
        public HumanoidBoneId Bone;
        public Vector3[] LocalPositionOffsets =
            Array.Empty<Vector3>();
        public Quaternion[] LocalRotationOffsets =
            Array.Empty<Quaternion>();
    }

    [Serializable]
    public sealed class BakedMotionCueDefinition
    {
        public string CueId;
        [Min(0.01f)] public float DurationSeconds = 1f;
        public bool Loop = false;
        public bool HoldLastPose = false;
        public HumanoidPoseSpace PoseSpace =
            HumanoidPoseSpace.NormalizedLocal;
        [Min(2)] public int FrameCount = 2;
        public Vector3[] RootPositionOffsets =
            Array.Empty<Vector3>();
        public Quaternion[] RootRotationOffsets =
            Array.Empty<Quaternion>();
        public BakedBoneMotionCueTrack[] Bones =
            Array.Empty<BakedBoneMotionCueTrack>();
    }

    [CreateAssetMenu(
        fileName = "BakedMotionCue",
        menuName = "VCR/Motion/Baked Motion Cue")]
    public sealed class BakedMotionCueAsset :
        ScriptableObject
    {
        [SerializeField] private BakedMotionCueDefinition cue =
            new();

        public BakedMotionCueDefinition Cue =>
            cue;

        public void SetCue(
            BakedMotionCueDefinition value)
        {
            cue =
                value ??
                new BakedMotionCueDefinition();
        }
    }
}
