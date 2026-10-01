using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Canonical ARKit-style coefficient names plus known streaming aliases.
    /// Alias lookup is built once so per-frame adapters do not construct names.
    /// </summary>
    public static class FaceCoefficientNames
    {
        private static readonly Dictionary<string, FaceCoefficient> Lookup =
            BuildLookup();

        public static bool TryParse(string name, out FaceCoefficient coefficient)
        {
            if (string.IsNullOrEmpty(name))
            {
                coefficient = default;
                return false;
            }

            return Lookup.TryGetValue(name, out coefficient);
        }

        public static string GetCanonical(FaceCoefficient coefficient)
        {
            if (coefficient < FaceCoefficient.Neutral ||
                coefficient >= FaceCoefficient.Count)
            {
                return string.Empty;
            }

            if (coefficient == FaceCoefficient.Neutral)
            {
                return "_neutral";
            }

            var enumName = coefficient.ToString();
            return char.ToLowerInvariant(enumName[0]) + enumName.Substring(1);
        }

        private static Dictionary<string, FaceCoefficient> BuildLookup()
        {
            var lookup = new Dictionary<string, FaceCoefficient>(
                (int)FaceCoefficient.Count * 2,
                StringComparer.Ordinal);

            for (var i = 0; i < (int)FaceCoefficient.Count; i++)
            {
                var coefficient = (FaceCoefficient)i;
                var canonical = GetCanonical(coefficient);

                if (!string.IsNullOrEmpty(canonical))
                {
                    lookup[canonical] = coefficient;
                }

                if (coefficient == FaceCoefficient.Neutral)
                {
                    lookup["neutral"] = coefficient;
                    continue;
                }

                if (canonical.EndsWith("Left", StringComparison.Ordinal))
                {
                    lookup[canonical.Substring(0, canonical.Length - 4) + "_L"] =
                        coefficient;
                }
                else if (canonical.EndsWith("Right", StringComparison.Ordinal))
                {
                    lookup[canonical.Substring(0, canonical.Length - 5) + "_R"] =
                        coefficient;
                }
            }

            return lookup;
        }
    }
}
