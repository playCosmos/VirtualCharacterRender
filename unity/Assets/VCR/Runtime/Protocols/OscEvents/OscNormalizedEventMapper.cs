using System;
using VCR.Runtime.Events;
using VCR.Runtime.Protocols.Osc;

namespace VCR.Runtime.Protocols.OscEvents
{
    /// <summary>
    /// Maps the VCR generic OSC event address to NormalizedEvent.
    ///
    /// Wire signature:
    /// /vcr/event type [actorId] [text] [amount] [currency] [actorName]
    /// </summary>
    public static class OscNormalizedEventMapper
    {
        public const string EventAddress =
            "/vcr/event";

        public static bool TryCreateEvent(
            OscMessage message,
            string sourceId,
            long receivedTimestampUs,
            out NormalizedEvent value,
            out string error)
        {
            value = default;
            error = null;

            if (message == null)
            {
                error =
                    "OSC event message is required.";
                return false;
            }

            if (!string.Equals(
                    message.Address,
                    EventAddress,
                    StringComparison.Ordinal))
            {
                error =
                    $"OSC address '{message.Address}' is not a VCR normalized-event address.";
                return false;
            }

            var args =
                message.Arguments ??
                Array.Empty<OscArgument>();

            if (args.Length < 1 ||
                args.Length > 6)
            {
                error =
                    "OSC /vcr/event requires 1 to 6 arguments.";
                return false;
            }

            if (!args[0].TryGetString(
                    out var type) ||
                string.IsNullOrWhiteSpace(
                    type))
            {
                error =
                    "OSC /vcr/event argument 0 must be a non-empty event type string.";
                return false;
            }

            if (!TryOptionalString(
                    args,
                    1,
                    out var actorId,
                    out error) ||
                !TryOptionalString(
                    args,
                    2,
                    out var text,
                    out error))
            {
                return false;
            }

            var hasAmount =
                args.Length >= 4;
            var amount = 0.0;
            string currency = null;

            if (hasAmount)
            {
                if (args[3].TryGetFloat(
                        out var floatAmount))
                {
                    amount =
                        floatAmount;
                }
                else if (args[3].TryGetInt(
                             out var intAmount))
                {
                    amount =
                        intAmount;
                }
                else
                {
                    error =
                        "OSC /vcr/event amount must be float32 or int32.";
                    return false;
                }

                if (args.Length < 5 ||
                    !args[4].TryGetString(
                        out currency) ||
                    string.IsNullOrWhiteSpace(
                        currency))
                {
                    error =
                        "OSC /vcr/event amount requires a currency/unit string.";
                    return false;
                }
            }
            else if (args.Length >= 5)
            {
                error =
                    "OSC /vcr/event currency cannot be supplied without an amount.";
                return false;
            }

            if (!TryOptionalString(
                    args,
                    5,
                    out var actorName,
                    out error))
            {
                return false;
            }

            value =
                new NormalizedEvent(
                    type,
                    sourceId,
                    receivedTimestampUs,
                    actorId:
                        actorId,
                    text:
                        text,
                    amount:
                        amount,
                    currency:
                        currency,
                    hasAmount:
                        hasAmount,
                    actorName:
                        actorName);

            if (!NormalizedEventIngressValidator
                .TryValidate(
                    value,
                    allowTrackingEvents: false,
                    out error))
            {
                value = default;
                return false;
            }

            return true;
        }

        private static bool TryOptionalString(
            OscArgument[] args,
            int index,
            out string value,
            out string error)
        {
            value = null;
            error = null;

            if (args.Length <= index)
            {
                return true;
            }

            if (!args[index].TryGetString(
                    out value))
            {
                error =
                    $"OSC /vcr/event argument {index} must be a string.";
                return false;
            }

            if (string.IsNullOrEmpty(
                    value))
            {
                value = null;
            }

            return true;
        }
    }
}
