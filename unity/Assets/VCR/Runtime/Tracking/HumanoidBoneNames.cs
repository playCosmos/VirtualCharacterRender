using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    public static class HumanoidBoneNames
    {
        private static readonly Dictionary<string, HumanoidBoneId> Lookup =
            BuildLookup();

        public static bool TryParse(string name, out HumanoidBoneId bone)
        {
            if (string.IsNullOrEmpty(name))
            {
                bone = default;
                return false;
            }

            return Lookup.TryGetValue(name, out bone);
        }

        public static string GetCanonical(HumanoidBoneId bone)
        {
            var index = (int)bone;
            return index >= 0 && index < (int)HumanoidBoneId.Count
                ? bone.ToString()
                : string.Empty;
        }

        private static Dictionary<string, HumanoidBoneId> BuildLookup()
        {
            var lookup = new Dictionary<string, HumanoidBoneId>(
                (int)HumanoidBoneId.Count,
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var bone = (HumanoidBoneId)i;
                lookup[bone.ToString()] = bone;
            }

            return lookup;
        }
    }
}
