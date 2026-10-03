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
        [SerializeField] private HumanoidBoneMask boneMask = new();
        [SerializeField] private bool affectRootPosition = false;
        [SerializeField] private bool affectRootRotation = false;

        public bool Enabled => enabled;
        public MotionLayerRole Role => role;
        public HumanoidPoseBlendMode BlendMode => blendMode;
        public float Weight => Mathf.Clamp01(weight);
        public HumanoidBoneMask BoneMask =>
            boneMask ??= new HumanoidBoneMask();
        public bool AffectRootPosition => affectRootPosition;
        public bool AffectRootRotation => affectRootRotation;

        public void Configure(
            bool layerEnabled,
            MotionLayerRole layerRole,
            HumanoidPoseBlendMode mode,
            float layerWeight,
            HumanoidBoneMask mask = null,
            bool rootPosition = false,
            bool rootRotation = false)
        {
            enabled = layerEnabled;
            role = layerRole;
            blendMode = mode;
            weight = Mathf.Clamp01(layerWeight);
            boneMask =
                mask ?? new HumanoidBoneMask();
            affectRootPosition = rootPosition;
            affectRootRotation = rootRotation;
        }
    }
}
