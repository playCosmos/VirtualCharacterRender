using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    public static class StandardExpressionNames
    {
        private readonly struct AsciiLookupEntry
        {
            public AsciiLookupEntry(
                string name,
                StandardExpression expression)
            {
                Name = name;
                Expression = expression;
            }

            public string Name { get; }
            public StandardExpression Expression { get; }
        }

        private static readonly Dictionary<string, StandardExpression> Lookup =
            BuildLookup();

        private static readonly Dictionary<uint, AsciiLookupEntry[]>
            AsciiLookup =
                BuildAsciiLookup(
                    Lookup);

        public static bool TryParse(
            string name,
            out StandardExpression expression)
        {
            if (string.IsNullOrEmpty(name))
            {
                expression = default;
                return false;
            }

            return Lookup.TryGetValue(
                name,
                out expression);
        }

        /// <summary>
        /// Allocation-free lookup for the ASCII VRM/VMC expression aliases.
        /// Custom/non-ASCII names are intentionally left to the caller.
        /// </summary>
        public static bool TryParseAscii(
            ReadOnlySpan<byte> name,
            out StandardExpression expression)
        {
            if (name.IsEmpty ||
                !TryComputeAsciiIgnoreCaseHash(
                    name,
                    out var hash) ||
                !AsciiLookup.TryGetValue(
                    hash,
                    out var entries))
            {
                expression = default;
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
                    expression =
                        entry.Expression;
                    return true;
                }
            }

            expression = default;
            return false;
        }

        public static string GetVrm1Name(
            StandardExpression expression)
        {
            return expression switch
            {
                StandardExpression.Neutral => "neutral",
                StandardExpression.Happy => "happy",
                StandardExpression.Angry => "angry",
                StandardExpression.Sad => "sad",
                StandardExpression.Relaxed => "relaxed",
                StandardExpression.Surprised => "surprised",
                StandardExpression.Aa => "aa",
                StandardExpression.Ih => "ih",
                StandardExpression.Ou => "ou",
                StandardExpression.Ee => "ee",
                StandardExpression.Oh => "oh",
                StandardExpression.Blink => "blink",
                StandardExpression.BlinkLeft => "blinkLeft",
                StandardExpression.BlinkRight => "blinkRight",
                StandardExpression.LookUp => "lookUp",
                StandardExpression.LookDown => "lookDown",
                StandardExpression.LookLeft => "lookLeft",
                StandardExpression.LookRight => "lookRight",
                _ => string.Empty
            };
        }

        public static string GetVmcVrm0Name(
            StandardExpression expression)
        {
            return expression switch
            {
                StandardExpression.Neutral => "Neutral",
                StandardExpression.Happy => "Joy",
                StandardExpression.Angry => "Angry",
                StandardExpression.Sad => "Sorrow",
                StandardExpression.Relaxed => "Fun",
                StandardExpression.Aa => "A",
                StandardExpression.Ih => "I",
                StandardExpression.Ou => "U",
                StandardExpression.Ee => "E",
                StandardExpression.Oh => "O",
                StandardExpression.Blink => "Blink",
                StandardExpression.BlinkLeft => "Blink_L",
                StandardExpression.BlinkRight => "Blink_R",
                StandardExpression.LookUp => "LookUp",
                StandardExpression.LookDown => "LookDown",
                StandardExpression.LookLeft => "LookLeft",
                StandardExpression.LookRight => "LookRight",
                _ => string.Empty
            };
        }

        private static Dictionary<string, StandardExpression>
            BuildLookup()
        {
            var lookup =
                new Dictionary<
                    string,
                    StandardExpression>(
                    StringComparer.OrdinalIgnoreCase);

            Add(
                lookup,
                StandardExpression.Neutral,
                "Neutral",
                "neutral");
            Add(
                lookup,
                StandardExpression.Happy,
                "Joy",
                "happy");
            Add(
                lookup,
                StandardExpression.Angry,
                "Angry",
                "angry");
            Add(
                lookup,
                StandardExpression.Sad,
                "Sorrow",
                "sad");
            Add(
                lookup,
                StandardExpression.Relaxed,
                "Fun",
                "relaxed");
            Add(
                lookup,
                StandardExpression.Surprised,
                "Surprised",
                "surprised");

            Add(
                lookup,
                StandardExpression.Aa,
                "A",
                "aa");
            Add(
                lookup,
                StandardExpression.Ih,
                "I",
                "ih");
            Add(
                lookup,
                StandardExpression.Ou,
                "U",
                "ou");
            Add(
                lookup,
                StandardExpression.Ee,
                "E",
                "ee");
            Add(
                lookup,
                StandardExpression.Oh,
                "O",
                "oh");

            Add(
                lookup,
                StandardExpression.Blink,
                "Blink",
                "blink");
            Add(
                lookup,
                StandardExpression.BlinkLeft,
                "Blink_L",
                "blinkLeft");
            Add(
                lookup,
                StandardExpression.BlinkRight,
                "Blink_R",
                "blinkRight");

            Add(
                lookup,
                StandardExpression.LookUp,
                "LookUp",
                "lookUp");
            Add(
                lookup,
                StandardExpression.LookDown,
                "LookDown",
                "lookDown");
            Add(
                lookup,
                StandardExpression.LookLeft,
                "LookLeft",
                "lookLeft");
            Add(
                lookup,
                StandardExpression.LookRight,
                "LookRight",
                "lookRight");

            return lookup;
        }

        private static void Add(
            Dictionary<string, StandardExpression> lookup,
            StandardExpression expression,
            params string[] names)
        {
            foreach (var name in names)
            {
                lookup[name] =
                    expression;
            }
        }

        private static Dictionary<uint, AsciiLookupEntry[]>
            BuildAsciiLookup(
                Dictionary<string, StandardExpression> lookup)
        {
            var buckets =
                new Dictionary<
                    uint,
                    List<AsciiLookupEntry>>();

            foreach (var pair in lookup)
            {
                var hash =
                    ComputeAsciiIgnoreCaseHash(
                        pair.Key);

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
                        pair.Key,
                        pair.Value));
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
