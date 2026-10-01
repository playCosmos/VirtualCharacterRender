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

        public const string StartStreamingV2Command =
            "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719|sendDataVersion=v2";

        public static bool TryParse(
            string text,
            out IFacialMocapFrame frame)
        {
            frame = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var coefficients = new float[(int)FaceCoefficient.Count];

            var hasCoefficient = false;
            var hasHead = false;
            var headEulerX = 0f;
            var headEulerY = 0f;
            var headEulerZ = 0f;
            var headPositionX = 0f;
            var headPositionY = 0f;
            var headPositionZ = 0f;

            var parts = text.Split('|');
            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (part.StartsWith("=head#", StringComparison.Ordinal))
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
                        hasHead = true;
                    }

                    continue;
                }

                if (part.StartsWith("rightEye#", StringComparison.Ordinal) ||
                    part.StartsWith("leftEye#", StringComparison.Ordinal) ||
                    part.StartsWith("___iFacialMocap", StringComparison.Ordinal))
                {
                    // Eye Euler channels are intentionally ignored because the
                    // normalized face contract already carries ARKit eye-look
                    // coefficients.
                    continue;
                }

                if (!TryParseCoefficientPart(
                    part,
                    out var name,
                    out var value))
                {
                    continue;
                }

                if (!FaceCoefficientNames.TryParse(name, out var coefficient))
                {
                    continue;
                }

                coefficients[(int)coefficient] = Clamp01(value / 100f);
                hasCoefficient = true;
            }

            if (!hasCoefficient && !hasHead)
            {
                return false;
            }

            frame = new IFacialMocapFrame(
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

        private static bool TryParseCoefficientPart(
            string part,
            out string name,
            out float value)
        {
            var separator = part.IndexOf('&');
            if (separator > 0)
            {
                name = part.Substring(0, separator);
                return TryFloat(
                    part.Substring(separator + 1),
                    out value);
            }

            // Legacy v1. Blendshape values are documented as non-negative in
            // iFacialMocap; FaceMotion3D negative values require v2.
            separator = part.IndexOf('-');
            if (separator > 0)
            {
                name = part.Substring(0, separator);
                return TryFloat(
                    part.Substring(separator + 1),
                    out value);
            }

            name = null;
            value = 0f;
            return false;
        }

        private static bool TryParseHead(
            string part,
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

            var values = part.Substring("=head#".Length).Split(',');
            if (values.Length != 6)
            {
                return false;
            }

            return
                TryFloat(values[0], out eulerX) &&
                TryFloat(values[1], out eulerY) &&
                TryFloat(values[2], out eulerZ) &&
                TryFloat(values[3], out positionX) &&
                TryFloat(values[4], out positionY) &&
                TryFloat(values[5], out positionZ);
        }

        private static bool TryFloat(
            string text,
            out float value)
        {
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
