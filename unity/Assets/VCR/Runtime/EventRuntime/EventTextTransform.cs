using System;

namespace VCR.Runtime.EventRuntime
{
    [Flags]
    public enum EventTextTransformFlags
    {
        None = 0,
        Trim = 1 << 0,
        ToLowerInvariant = 1 << 1,
        ToUpperInvariant = 1 << 2
    }

    public static class EventTextTransform
    {
        public static string Apply(
            string value,
            EventTextTransformFlags transforms,
            string prefix,
            string suffix)
        {
            var result = value;

            if ((transforms &
                 EventTextTransformFlags.Trim) != 0)
            {
                result = result?.Trim();
            }

            // If both casing flags are configured, upper-case wins
            // deterministically instead of depending on flag order.
            if ((transforms &
                 EventTextTransformFlags.ToUpperInvariant) != 0)
            {
                result = result?.ToUpperInvariant();
            }
            else if ((transforms &
                      EventTextTransformFlags.ToLowerInvariant) != 0)
            {
                result = result?.ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                result =
                    prefix +
                    (result ?? string.Empty);
            }

            if (!string.IsNullOrEmpty(suffix))
            {
                result =
                    (result ?? string.Empty) +
                    suffix;
            }

            return result;
        }
    }
}
