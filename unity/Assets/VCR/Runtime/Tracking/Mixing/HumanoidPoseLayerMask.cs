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
            SanitizeWeight(
                defaultBoneWeight,
                fallback:
                    1f);

        public float RootPositionWeight =>
            SanitizeWeight(
                rootPositionWeight,
                fallback:
                    1f);

        public float RootRotationWeight =>
            SanitizeWeight(
                rootRotationWeight,
                fallback:
                    1f);

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
                SanitizeWeight(
                    weight,
                    fallback:
                        1f);
        }

        public void SetRootWeights(
            float positionWeight,
            float rotationWeight)
        {
            rootPositionWeight =
                SanitizeWeight(
                    positionWeight,
                    fallback:
                        1f);
            rootRotationWeight =
                SanitizeWeight(
                    rotationWeight,
                    fallback:
                        1f);
        }

        public HumanoidPoseLayerMask Clone()
        {
            var clone =
                new HumanoidPoseLayerMask
                {
                    defaultBoneWeight =
                        defaultBoneWeight,
                    rootPositionWeight =
                        rootPositionWeight,
                    rootRotationWeight =
                        rootRotationWeight,
                    boneOverrides =
                        boneOverrides == null
                            ? Array.Empty<HumanoidBoneWeight>()
                            : (HumanoidBoneWeight[])
                                boneOverrides.Clone()
                };

            return clone;
        }

        private static float SanitizeWeight(
            float value,
            float fallback)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return fallback;
            }

            return Mathf.Clamp01(
                value);
        }

        public void SetBoneWeight(
            HumanoidBoneId bone,
            float weight)
        {
            var clamped =
                SanitizeWeight(
                    weight,
                    fallback:
                        DefaultBoneWeight);

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
