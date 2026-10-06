using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    public static class HumanoidBoneNames
    {
        private readonly struct AsciiLookupEntry
        {
            public AsciiLookupEntry(
                string name,
                HumanoidBoneId bone)
            {
                Name = name;
                Bone = bone;
            }

            public string Name { get; }
            public HumanoidBoneId Bone { get; }
        }

        private static readonly string[] CanonicalNames =
            BuildCanonicalNames();

        private static readonly Dictionary<string, HumanoidBoneId> Lookup =
            BuildLookup();

        private static readonly Dictionary<uint, AsciiLookupEntry[]>
            AsciiLookup =
                BuildAsciiLookup();

        public static bool TryParse(
            string name,
            out HumanoidBoneId bone)
        {
            if (string.IsNullOrEmpty(name))
            {
                bone = default;
                return false;
            }

            return Lookup.TryGetValue(
                name,
                out bone);
        }

        /// <summary>
        /// Allocation-free VMC/OSC lookup for canonical humanoid bone names.
        /// VMC humanoid names are ASCII; non-ASCII input is rejected.
        /// </summary>
        public static bool TryParseAscii(
            ReadOnlySpan<byte> name,
            out HumanoidBoneId bone)
        {
            if (name.IsEmpty ||
                !TryComputeAsciiIgnoreCaseHash(
                    name,
                    out var hash) ||
                !AsciiLookup.TryGetValue(
                    hash,
                    out var entries))
            {
                bone = default;
                return false;
            }

            for (var i = 0;
                 i < entries.Length;
                 i++)
            {
                var entry =
                    entries[i];

                if (EqualsAsciiIgnoreCase(
                        name,
                        entry.Name))
                {
                    bone =
                        entry.Bone;
                    return true;
                }
            }

            bone = default;
            return false;
        }

        public static string GetCanonical(
            HumanoidBoneId bone)
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

        private static Dictionary<string, HumanoidBoneId>
            BuildLookup()
        {
            var lookup =
                new Dictionary<
                    string,
                    HumanoidBoneId>(
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

        private static Dictionary<uint, AsciiLookupEntry[]>
            BuildAsciiLookup()
        {
            var buckets =
                new Dictionary<
                    uint,
                    List<AsciiLookupEntry>>();

            for (var i = 0;
                 i < CanonicalNames.Length;
                 i++)
            {
                var name =
                    CanonicalNames[i];
                var hash =
                    ComputeAsciiIgnoreCaseHash(
                        name);

                if (!buckets.TryGetValue(
                        hash,
                        out var entries))
                {
                    entries =
                        new List<
                            AsciiLookupEntry>();
                    buckets.Add(
                        hash,
                        entries);
                }

                entries.Add(
                    new AsciiLookupEntry(
                        name,
                        (HumanoidBoneId)i));
            }

            var result =
                new Dictionary<
                    uint,
                    AsciiLookupEntry[]>(
                    buckets.Count);

            foreach (var pair in buckets)
            {
                result.Add(
                    pair.Key,
                    pair.Value.ToArray());
            }

            return result;
        }

        private static bool TryComputeAsciiIgnoreCaseHash(
            ReadOnlySpan<byte> value,
            out uint hash)
        {
            unchecked
            {
                hash =
                    2166136261u;

                for (var i = 0;
                     i < value.Length;
                     i++)
                {
                    var current =
                        value[i];

                    if (current > 0x7f)
                    {
                        hash = 0;
                        return false;
                    }

                    hash ^=
                        FoldAscii(
                            current);
                    hash *=
                        16777619u;
                }

                return true;
            }
        }

        private static uint ComputeAsciiIgnoreCaseHash(
            string value)
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
                        FoldAscii(
                            (byte)value[i]);
                    hash *=
                        16777619u;
                }

                return hash;
            }
        }

        private static bool EqualsAsciiIgnoreCase(
            ReadOnlySpan<byte> value,
            string candidate)
        {
            if (value.Length !=
                candidate.Length)
            {
                return false;
            }

            for (var i = 0;
                 i < value.Length;
                 i++)
            {
                var current =
                    value[i];

                if (current > 0x7f ||
                    FoldAscii(current) !=
                    FoldAscii(
                        (byte)candidate[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static byte FoldAscii(
            byte value)
        {
            return value >= (byte)'A' &&
                   value <= (byte)'Z'
                ? (byte)(
                    value +
                    ((byte)'a' -
                     (byte)'A'))
                : value;
        }
    }
}
