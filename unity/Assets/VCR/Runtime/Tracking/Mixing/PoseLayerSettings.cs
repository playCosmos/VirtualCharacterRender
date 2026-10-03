using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class PoseLayerSettings
    {
        [SerializeField]
        private PoseLayerRole role =
            PoseLayerRole.Additive;

        [SerializeField]
        private PoseBlendMode blendMode =
            PoseBlendMode.Additive;

        [SerializeField, Range(0f, 1f)]
        private float weight = 1f;

        [SerializeField]
        private HumanoidPoseLayerMask mask =
            new HumanoidPoseLayerMask();

        public PoseLayerRole Role => role;
        public PoseBlendMode BlendMode => blendMode;
        public float Weight => Mathf.Clamp01(weight);
        public HumanoidPoseLayerMask Mask =>
            mask ??=
                new HumanoidPoseLayerMask();

        public void Configure(
            PoseLayerRole layerRole,
            PoseBlendMode mode,
            float layerWeight)
        {
            role = layerRole;
            blendMode = mode;
            weight = Mathf.Clamp01(layerWeight);
        }
    }
}
