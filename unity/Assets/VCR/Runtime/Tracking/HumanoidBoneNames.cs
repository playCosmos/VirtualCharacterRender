using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    public static class HumanoidBoneNames
    {
        private static readonly string[] CanonicalNames =
            BuildCanonicalNames();

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
            return index >= 0 &&
                   index < CanonicalNames.Length
                ? CanonicalNames[index]
                : string.Empty;
        }

        private static string[] BuildCanonicalNames()
        {
            var names =
                new string[
                    (int)HumanoidBoneId.Count];

            for (var i = 0;
                 i < names.Length;
                 i++)
            {
                names[i] =
                    ((HumanoidBoneId)i)
                    .ToString();
            }

            return names;
        }

        private static Dictionary<string, HumanoidBoneId> BuildLookup()
        {
            var lookup = new Dictionary<string, HumanoidBoneId>(
                (int)HumanoidBoneId.Count,
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0;
                 i < CanonicalNames.Length;
                 i++)
            {
                lookup[
                    CanonicalNames[i]] =
                        (HumanoidBoneId)i;
            }

            return lookup;
        }
    }
}
