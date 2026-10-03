using System;

namespace VCR.Runtime.Events
{
    /// <summary>
    /// Structural validation for untrusted/external event ingress.
    ///
    /// Main-thread dispatch assigns the final sequence. External ingress does
    /// not get to claim tracking-derived event types unless explicitly allowed.
    /// </summary>
    public static class NormalizedEventIngressValidator
    {
        public const int MaxTypeLength = 128;
        public const int MaxSourceIdLength = 128;
        public const int MaxActorIdLength = 256;
        public const int MaxActorNameLength = 256;
        public const int MaxTextLength = 4096;
        public const int MaxCurrencyLength = 32;

        public static bool TryValidate(
            NormalizedEvent value,
            bool allowTrackingEvents,
            out string error)
        {
            error = null;

            if (!IsSafeToken(
                    value.Type,
                    MaxTypeLength))
            {
                error =
                    "Normalized event type is missing or invalid.";
                return false;
            }

            if (!allowTrackingEvents &&
                value.Type.StartsWith(
                    "tracking.",
                    StringComparison.Ordinal))
            {
                error =
                    "External ingress cannot inject tracking-derived events.";
                return false;
            }

            if (!IsSafeToken(
                    value.SourceId,
                    MaxSourceIdLength))
            {
                error =
                    "Normalized event source id is missing or invalid.";
                return false;
            }

            if (value.TimestampUs < 0)
            {
                error =
                    "Normalized event timestamp cannot be negative.";
                return false;
            }

            if (value.Sequence != 0)
            {
                error =
                    "External ingress sequence must be zero; dispatch assigns the sequence.";
                return false;
            }

            if (!IsOptionalTextWithin(
                    value.ActorId,
                    MaxActorIdLength) ||
                !IsOptionalTextWithin(
                    value.ActorName,
                    MaxActorNameLength) ||
                !IsOptionalTextWithin(
                    value.Text,
                    MaxTextLength) ||
                !IsOptionalTextWithin(
                    value.Currency,
                    MaxCurrencyLength))
            {
                error =
                    "Normalized event payload exceeds ingress limits.";
                return false;
            }

            if (value.HasAmount)
            {
                if (double.IsNaN(
                        value.Amount) ||
                    double.IsInfinity(
                        value.Amount) ||
                    value.Amount < 0.0)
                {
                    error =
                        "Normalized event amount must be finite and non-negative.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(
                        value.Currency))
                {
                    error =
                        "Normalized event amount requires a currency/unit id.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsSafeToken(
            string value,
            int maxLength)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length > maxLength)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch) ||
                    ch == '.' ||
                    ch == '_' ||
                    ch == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsOptionalTextWithin(
            string value,
            int maxLength)
        {
            return
                value == null ||
                value.Length <= maxLength;
        }
    }
}
