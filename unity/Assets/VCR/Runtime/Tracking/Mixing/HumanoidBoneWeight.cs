using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public struct HumanoidBoneWeight
    {
        [SerializeField] private HumanoidBoneId bone;
        [SerializeField, Range(0f, 1f)] private float weight;

        public HumanoidBoneWeight(
            HumanoidBoneId bone,
            float weight)
        {
            this.bone = bone;
            this.weight = Mathf.Clamp01(weight);
        }

        public HumanoidBoneId Bone => bone;
        public float Weight => Mathf.Clamp01(weight);
    }
}
