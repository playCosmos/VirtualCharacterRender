using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class HumanoidPoseLayerMask
    {
        [SerializeField, Range(0f, 1f)]
        private float defaultBoneWeight = 1f;

        [SerializeField, Range(0f, 1f)]
        private float rootPositionWeight = 1f;

        [SerializeField, Range(0f, 1f)]
        private float rootRotationWeight = 1f;

        [SerializeField]
        private HumanoidBoneWeight[] boneOverrides =
            Array.Empty<HumanoidBoneWeight>();

        public float DefaultBoneWeight =>
            Mathf.Clamp01(defaultBoneWeight);

        public float RootPositionWeight =>
            Mathf.Clamp01(rootPositionWeight);

        public float RootRotationWeight =>
            Mathf.Clamp01(rootRotationWeight);

        public bool HasFullWeight
        {
            get
            {
                if (RootPositionWeight < 1f ||
                    RootRotationWeight < 1f)
                {
                    return false;
                }

                for (var i = 0;
                     i < (int)HumanoidBoneId.Count;
                     i++)
                {
                    if (GetBoneWeight(
                            (HumanoidBoneId)i) <
                        1f)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool HasAnyWeight
        {
            get
            {
                if (RootPositionWeight > 0f ||
                    RootRotationWeight > 0f ||
                    DefaultBoneWeight > 0f)
                {
                    return true;
                }

                if (boneOverrides == null)
                {
                    return false;
                }

                for (var i = 0;
                     i < boneOverrides.Length;
                     i++)
                {
                    if (boneOverrides[i].Weight >
                        0f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public float GetBoneWeight(
            HumanoidBoneId bone)
        {
            if (boneOverrides != null)
            {
                for (var i =
                         boneOverrides.Length - 1;
                     i >= 0;
                     i--)
                {
                    if (boneOverrides[i].Bone == bone)
                    {
                        return boneOverrides[i].Weight;
                    }
                }
            }

            return DefaultBoneWeight;
        }

        public void SetDefaultBoneWeight(
            float weight)
        {
            defaultBoneWeight =
                Mathf.Clamp01(weight);
        }

        public void SetRootWeights(
            float positionWeight,
            float rotationWeight)
        {
            rootPositionWeight =
                Mathf.Clamp01(positionWeight);
            rootRotationWeight =
                Mathf.Clamp01(rotationWeight);
        }

        public void SetBoneWeight(
            HumanoidBoneId bone,
            float weight)
        {
            var clamped =
                Mathf.Clamp01(weight);

            boneOverrides ??=
                Array.Empty<HumanoidBoneWeight>();

            for (var i =
                     boneOverrides.Length - 1;
                 i >= 0;
                 i--)
            {
                if (boneOverrides[i].Bone !=
                    bone)
                {
                    continue;
                }

                boneOverrides[i] =
                    new HumanoidBoneWeight(
                        bone,
                        clamped);
                return;
            }

            Array.Resize(
                ref boneOverrides,
                boneOverrides.Length + 1);

            boneOverrides[
                boneOverrides.Length - 1] =
                new HumanoidBoneWeight(
                    bone,
                    clamped);
        }
    }
}
