using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class HumanoidBoneMask
    {
        [SerializeField] private bool includeAll = true;
        [SerializeField] private HumanoidBoneId[] includedBones =
            Array.Empty<HumanoidBoneId>();

        public bool IncludeAll => includeAll;

        public bool Includes(HumanoidBoneId bone)
        {
            if (includeAll)
            {
                return true;
            }

            if (includedBones == null)
            {
                return false;
            }

            for (var i = 0;
                 i < includedBones.Length;
                 i++)
            {
                if (includedBones[i] == bone)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetAll()
        {
            includeAll = true;
            includedBones =
                Array.Empty<HumanoidBoneId>();
        }

        public void SetIncluded(
            params HumanoidBoneId[] bones)
        {
            includeAll = false;
            includedBones =
                bones == null
                    ? Array.Empty<HumanoidBoneId>()
                    : (HumanoidBoneId[])bones.Clone();
        }
    }
}
