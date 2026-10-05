using System;
using System.Globalization;

namespace VCR.Runtime.Tracking.ArKit
{
    /// <summary>
    /// Parser for the documented iFacialMocap / FaceMotion3D stream format.
    ///
    /// v2 ("name&value") is preferred because it remains unambiguous when a
    /// sender emits negative values. Legacy "name-value" is accepted for
    /// compatibility with ordinary iFacialMocap 0..100 coefficients.
    /// </summary>
    public static class IFacialMocapFrameParser
    {
        public const int DefaultPort = 49983;
        public const int MaxTextCharacters =
            16 * 1024;
        public const int MaxParts = 128;
        public const int MaxPartCharacters =
            256;

        private const string HeadPrefix =
            "=head#";
        private const string RightEyePrefix =
            "rightEye#";
        private const string LeftEyePrefix =
            "leftEye#";
        private const string MetadataPrefix =
            "___iFacialMocap";

        public const string StartStreamingV2Command =
            "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719|sendDataVersion=v2";

        public static bool TryParse(
            string text,
            out IFacialMocapFrame frame)
        {
            if (text == null)
            {
                frame = null;
                return false;
            }

            return TryParse(
                text.AsSpan(),
                out frame);
        }

        public static bool TryParse(
            ReadOnlySpan<char> text,
            out IFacialMocapFrame frame)
        {
            frame = null;

            if (text.IsEmpty ||
                text.Length >
                    MaxTextCharacters)
            {
                return false;
            }

            var coefficients =
                new float[
                    (int)FaceCoefficient.Count];

            var hasCoefficient = false;
            var hasHead = false;
            var headEulerX = 0f;
            var headEulerY = 0f;
            var headEulerZ = 0f;
            var headPositionX = 0f;
            var headPositionY = 0f;
            var headPositionZ = 0f;

            var partStart = 0;
            var partCount = 0;

            while (partStart <
                   text.Length)
            {
                if (++partCount >
                    MaxParts)
                {
                    return false;
                }

                var relativeSeparator =
                    text.Slice(
                            partStart)
                        .IndexOf('|');
                var separator =
                    relativeSeparator >= 0
                        ? partStart +
                          relativeSeparator
                        : -1;
                var partEnd =
                    separator >= 0
                        ? separator
                        : text.Length;
                var partLength =
                    partEnd -
                    partStart;

                if (partLength >
                    MaxPartCharacters)
                {
                    return false;
                }

                var part =
                    TrimWhitespace(
                        text.Slice(
                            partStart,
                            partLength));

                if (!part.IsEmpty)
                {
                    if (StartsWithOrdinal(
                            part,
                            HeadPrefix))
                    {
                        if (TryParseHead(
                                part,
                                out headEulerX,
                                out headEulerY,
                                out headEulerZ,
                                out headPositionX,
                                out headPositionY,
                                out headPositionZ))
                        {
                            hasHead =
                                true;
                        }
                    }
                    else if (!StartsWithOrdinal(
                                 part,
                                 RightEyePrefix) &&
                             !StartsWithOrdinal(
                                 part,
                                 LeftEyePrefix) &&
                             !StartsWithOrdinal(
                                 part,
                                 MetadataPrefix) &&
                             TryParseCoefficientPart(
                                 part,
                                 out var name,
                                 out var value) &&
                             FaceCoefficientNames
                                 .TryParse(
                                     name,
                                     out var coefficient))
                    {
                        coefficients[
                            (int)coefficient] =
                                Clamp01(
                                    value /
                                    100f);
                        hasCoefficient =
                            true;
                    }
                }

                if (separator < 0)
                {
                    break;
                }

                partStart =
                    separator + 1;
            }

            if (!hasCoefficient &&
                !hasHead)
            {
                return false;
            }

            frame =
                new IFacialMocapFrame(
                    coefficients,
                    hasHead,
                    headEulerX,
                    headEulerY,
                    headEulerZ,
                    headPositionX,
                    headPositionY,
                    headPositionZ);

            return true;
        }

        private static bool
            TryParseCoefficientPart(
                ReadOnlySpan<char> part,
                out ReadOnlySpan<char> name,
                out float value)
        {
            var separator =
                part.IndexOf('&');

            if (separator > 0)
            {
                name =
                    part.Slice(
                        0,
                        separator);

                return TryFloat(
                    part.Slice(
                        separator + 1),
                    out value);
            }

            // Legacy v1. Blendshape values are documented as non-negative in
            // iFacialMocap; FaceMotion3D negative values require v2.
            separator =
                part.IndexOf('-');

            if (separator > 0)
            {
                name =
                    part.Slice(
                        0,
                        separator);

                return TryFloat(
                    part.Slice(
                        separator + 1),
                    out value);
            }

            name = default;
            value = 0f;
            return false;
        }

        private static bool TryParseHead(
            ReadOnlySpan<char> part,
            out float eulerX,
            out float eulerY,
            out float eulerZ,
            out float positionX,
            out float positionY,
            out float positionZ)
        {
            eulerX = 0f;
            eulerY = 0f;
            eulerZ = 0f;
            positionX = 0f;
            positionY = 0f;
            positionZ = 0f;

            if (part.Length >
                    MaxPartCharacters ||
                !StartsWithOrdinal(
                    part,
                    HeadPrefix))
            {
                return false;
            }

            var values =
                part.Slice(
                    HeadPrefix.Length);
            var cursor = 0;

            return
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: true,
                    out eulerX) &&
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: true,
                    out eulerY) &&
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: true,
                    out eulerZ) &&
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: true,
                    out positionX) &&
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: true,
                    out positionY) &&
                TryReadFloatComponent(
                    values,
                    ref cursor,
                    requireSeparator: false,
                    out positionZ) &&
                cursor ==
                    values.Length;
        }

        private static bool
            TryReadFloatComponent(
                ReadOnlySpan<char> values,
                ref int cursor,
                bool requireSeparator,
                out float value)
        {
            value = 0f;

            if (cursor < 0 ||
                cursor >=
                    values.Length)
            {
                return false;
            }

            var remaining =
                values.Slice(
                    cursor);
            var separator =
                remaining.IndexOf(',');

            if (requireSeparator)
            {
                if (separator <= 0)
                {
                    return false;
                }

                if (!TryFloat(
                        remaining.Slice(
                            0,
                            separator),
                        out value))
                {
                    return false;
                }

                cursor +=
                    separator + 1;
                return true;
            }

            if (separator >= 0 ||
                remaining.IsEmpty ||
                !TryFloat(
                    remaining,
                    out value))
            {
                return false;
            }

            cursor =
                values.Length;
            return true;
        }

        private static bool TryFloat(
            ReadOnlySpan<char> text,
            out float value)
        {
            if (!float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return false;
            }

            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private static bool StartsWithOrdinal(
            ReadOnlySpan<char> value,
            string prefix)
        {
            return
                value.Length >=
                    prefix.Length &&
                value.Slice(
                        0,
                        prefix.Length)
                    .SequenceEqual(
                        prefix.AsSpan());
        }

        private static ReadOnlySpan<char>
            TrimWhitespace(
                ReadOnlySpan<char> value)
        {
            var start = 0;
            var end =
                value.Length - 1;

            while (start <= end &&
                   char.IsWhiteSpace(
                       value[start]))
            {
                start++;
            }

            while (end >= start &&
                   char.IsWhiteSpace(
                       value[end]))
            {
                end--;
            }

            return
                start > end
                    ? ReadOnlySpan<char>.Empty
                    : value.Slice(
                        start,
                        end - start + 1);
        }

        private static float Clamp01(
            float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
