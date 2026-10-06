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
            this.weight =
                SanitizeWeight(
                    weight);
        }

        public HumanoidBoneId Bone => bone;
        public float Weight =>
            SanitizeWeight(
                weight);

        private static float SanitizeWeight(
            float value)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp01(
                value);
        }
    }
}
