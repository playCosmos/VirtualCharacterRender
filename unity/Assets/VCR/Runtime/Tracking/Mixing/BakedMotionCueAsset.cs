using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class BakedMotionCueMarker
    {
        public string Name;
        [Min(0f)] public float TimeSeconds;

        public BakedMotionCueMarker Clone() =>
            new()
            {
                Name = Name,
                TimeSeconds = TimeSeconds
            };
    }

    [Serializable]
    public sealed class BakedBoneMotionCueTrack
    {
        public HumanoidBoneId Bone;
        public Vector3[] LocalPositionOffsets =
            Array.Empty<Vector3>();
        public Quaternion[] LocalRotationOffsets =
            Array.Empty<Quaternion>();

        public BakedBoneMotionCueTrack Clone() =>
            new()
            {
                Bone = Bone,
                LocalPositionOffsets =
                    LocalPositionOffsets == null
                        ? Array.Empty<Vector3>()
                        : (Vector3[])
                            LocalPositionOffsets.Clone(),
                LocalRotationOffsets =
                    LocalRotationOffsets == null
                        ? Array.Empty<Quaternion>()
                        : (Quaternion[])
                            LocalRotationOffsets.Clone()
            };
    }

    [Serializable]
    public sealed class BakedMotionCueDefinition
    {
        public string CueId;
        [Min(0.01f)] public float DurationSeconds = 1f;
        public bool Loop = false;
        public bool HoldLastPose = false;
        public BakedMotionCueMarker[] Markers =
            Array.Empty<BakedMotionCueMarker>();
        public HumanoidPoseSpace PoseSpace =
            HumanoidPoseSpace.NormalizedLocal;
        [Min(2)] public int FrameCount = 2;
        public Vector3[] RootPositionOffsets =
            Array.Empty<Vector3>();
        public Quaternion[] RootRotationOffsets =
            Array.Empty<Quaternion>();
        public BakedBoneMotionCueTrack[] Bones =
            Array.Empty<BakedBoneMotionCueTrack>();

        public BakedMotionCueDefinition Clone()
        {
            var markerClones =
                new BakedMotionCueMarker[
                    Markers?.Length ?? 0];
            var boneClones =
                new BakedBoneMotionCueTrack[
                    Bones?.Length ?? 0];

            for (var i = 0;
                 i < markerClones.Length;
                 i++)
            {
                markerClones[i] =
                    Markers[i]?.Clone();
            }

            for (var i = 0;
                 i < boneClones.Length;
                 i++)
            {
                boneClones[i] =
                    Bones[i]?.Clone();
            }

            return new BakedMotionCueDefinition
            {
                CueId = CueId,
                DurationSeconds =
                    DurationSeconds,
                Loop = Loop,
                HoldLastPose =
                    HoldLastPose,
                Markers =
                    markerClones,
                PoseSpace =
                    PoseSpace,
                FrameCount =
                    FrameCount,
                RootPositionOffsets =
                    RootPositionOffsets == null
                        ? Array.Empty<Vector3>()
                        : (Vector3[])
                            RootPositionOffsets.Clone(),
                RootRotationOffsets =
                    RootRotationOffsets == null
                        ? Array.Empty<Quaternion>()
                        : (Quaternion[])
                            RootRotationOffsets.Clone(),
                Bones =
                    boneClones
            };
        }
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
            cue?.Clone() ??
            new BakedMotionCueDefinition();

        public void SetCue(
            BakedMotionCueDefinition value)
        {
            cue =
                value?.Clone() ??
                new BakedMotionCueDefinition();
        }
    }
}
