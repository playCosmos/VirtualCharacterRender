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
        private readonly struct SpanLookupEntry
        {
            public SpanLookupEntry(
                string name,
                FaceCoefficient coefficient)
            {
                Name = name;
                Coefficient = coefficient;
            }

            public string Name { get; }
            public FaceCoefficient Coefficient { get; }
        }

        private static readonly Dictionary<string, FaceCoefficient> Lookup =
            BuildLookup();

        private static readonly Dictionary<uint, SpanLookupEntry[]> SpanLookup =
            BuildSpanLookup(
                Lookup);

        public static bool TryParse(
            string name,
            out FaceCoefficient coefficient)
        {
            if (string.IsNullOrEmpty(name))
            {
                coefficient = default;
                return false;
            }

            return Lookup.TryGetValue(
                name,
                out coefficient);
        }

        public static bool TryParse(
            ReadOnlySpan<char> name,
            out FaceCoefficient coefficient)
        {
            if (name.IsEmpty)
            {
                coefficient = default;
                return false;
            }

            var hash =
                ComputeOrdinalHash(
                    name);

            if (!SpanLookup.TryGetValue(
                    hash,
                    out var entries))
            {
                coefficient = default;
                return false;
            }

            for (var i = 0;
                 i < entries.Length;
                 i++)
            {
                var entry =
                    entries[i];

                if (name.SequenceEqual(
                        entry.Name.AsSpan()))
                {
                    coefficient =
                        entry.Coefficient;
                    return true;
                }
            }

            coefficient = default;
            return false;
        }

        public static string GetCanonical(
            FaceCoefficient coefficient)
        {
            var index = (int)coefficient;
            if (index < 0 ||
                index >=
                    (int)FaceCoefficient.Count)
            {
                return string.Empty;
            }

            if (coefficient ==
                FaceCoefficient.Neutral)
            {
                return "_neutral";
            }

            var enumName =
                coefficient.ToString();

            return
                char.ToLowerInvariant(
                    enumName[0]) +
                enumName.Substring(1);
        }

        private static Dictionary<string, FaceCoefficient>
            BuildLookup()
        {
            var lookup =
                new Dictionary<
                    string,
                    FaceCoefficient>(
                    (int)FaceCoefficient.Count *
                    2,
                    StringComparer.Ordinal);

            for (var i = 0;
                 i <
                 (int)FaceCoefficient.Count;
                 i++)
            {
                var coefficient =
                    (FaceCoefficient)i;
                var canonical =
                    GetCanonical(
                        coefficient);

                if (!string.IsNullOrEmpty(
                        canonical))
                {
                    lookup[canonical] =
                        coefficient;
                }

                if (coefficient ==
                    FaceCoefficient.Neutral)
                {
                    lookup["neutral"] =
                        coefficient;
                    continue;
                }

                if (canonical.EndsWith(
                        "Left",
                        StringComparison.Ordinal))
                {
                    lookup[
                        canonical.Substring(
                            0,
                            canonical.Length - 4) +
                        "_L"] =
                        coefficient;
                }
                else if (canonical.EndsWith(
                             "Right",
                             StringComparison.Ordinal))
                {
                    lookup[
                        canonical.Substring(
                            0,
                            canonical.Length - 5) +
                        "_R"] =
                        coefficient;
                }
            }

            return lookup;
        }

        private static Dictionary<uint, SpanLookupEntry[]>
            BuildSpanLookup(
                Dictionary<
                    string,
                    FaceCoefficient> lookup)
        {
            var buckets =
                new Dictionary<
                    uint,
                    List<SpanLookupEntry>>();

            foreach (var pair in lookup)
            {
                var hash =
                    ComputeOrdinalHash(
                        pair.Key.AsSpan());

                if (!buckets.TryGetValue(
                        hash,
                        out var entries))
                {
                    entries =
                        new List<
                            SpanLookupEntry>();
                    buckets.Add(
                        hash,
                        entries);
                }

                entries.Add(
                    new SpanLookupEntry(
                        pair.Key,
                        pair.Value));
            }

            var result =
                new Dictionary<
                    uint,
                    SpanLookupEntry[]>(
                    buckets.Count);

            foreach (var pair in buckets)
            {
                result.Add(
                    pair.Key,
                    pair.Value.ToArray());
            }

            return result;
        }

        private static uint ComputeOrdinalHash(
            ReadOnlySpan<char> value)
        {
            unchecked
            {
                var hash =
                    2166136261u;

                for (var i = 0;
                     i < value.Length;
                     i++)
                {
                    hash ^=
                        value[i];
                    hash *=
                        16777619u;
                }

                return hash;
            }
        }
    }
}
