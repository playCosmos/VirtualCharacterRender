using System;
using VCR.Runtime.Events;

namespace VCR.Runtime.Protocols.WebSocket
{
    public static class WebSocketEventProtocol
    {
        public const int CurrentVersion = 1;
        public const string InjectOperation =
            "event.inject";

        public static bool TryCreateEvent(
            WebSocketEventMessage message,
            string gatewaySourceId,
            long receivedTimestampUs,
            out NormalizedEvent value,
            out string error)
        {
            value = default;
            error = null;

            if (message == null)
            {
                error =
                    "WebSocket event message is required.";
                return false;
            }

            if (message.version !=
                CurrentVersion)
            {
                error =
                    $"WebSocket event protocol version {message.version} is unsupported; expected {CurrentVersion}.";
                return false;
            }

            if (!string.Equals(
                    message.op,
                    InjectOperation,
                    StringComparison.Ordinal))
            {
                error =
                    $"WebSocket operation '{message.op}' is unsupported.";
                return false;
            }

            if (receivedTimestampUs < 0)
            {
                error =
                    "WebSocket receive timestamp cannot be negative.";
                return false;
            }

            value =
                new NormalizedEvent(
                    message.type,
                    gatewaySourceId,
                    receivedTimestampUs,
                    actorId:
                        message.actorId,
                    text:
                        message.text,
                    amount:
                        message.amount,
                    currency:
                        message.currency,
                    hasAmount:
                        message.hasAmount,
                    actorName:
                        message.actorName);

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
    }
}
