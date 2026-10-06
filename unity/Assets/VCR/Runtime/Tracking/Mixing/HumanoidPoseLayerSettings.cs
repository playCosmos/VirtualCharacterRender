using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class HumanoidPoseLayerSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private MotionLayerRole role =
            MotionLayerRole.Additive;
        [SerializeField] private HumanoidPoseBlendMode blendMode =
            HumanoidPoseBlendMode.Additive;
        [SerializeField, Range(0f, 1f)] private float weight = 1f;
        [SerializeField] private HumanoidPoseLayerMask mask = new();

        public bool Enabled => enabled;
        public MotionLayerRole Role => role;
        public HumanoidPoseBlendMode BlendMode => blendMode;
        public float Weight => Mathf.Clamp01(weight);
        public HumanoidPoseLayerMask Mask =>
            RuntimeMask.Clone();

        internal HumanoidPoseLayerMask RuntimeMask =>
            mask ??= new HumanoidPoseLayerMask();

        public HumanoidPoseLayerSettings Clone()
        {
            var clone =
                new HumanoidPoseLayerSettings();

            clone.Configure(
                Enabled,
                Role,
                BlendMode,
                Weight,
                RuntimeMask);

            return clone;
        }

        public void Configure(
            bool layerEnabled,
            MotionLayerRole layerRole,
            HumanoidPoseBlendMode mode,
            float layerWeight,
            HumanoidPoseLayerMask layerMask = null)
        {
            enabled = layerEnabled;
            role = layerRole;
            blendMode = mode;
            weight = Mathf.Clamp01(layerWeight);
            mask =
                layerMask?.Clone() ??
                new HumanoidPoseLayerMask();
        }
    }
}
