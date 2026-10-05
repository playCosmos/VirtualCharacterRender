using System;
using VCR.Runtime.Events;

namespace VCR.Runtime.Broadcast.Soop
{
    public static class SoopBridgeEventMapper
    {
        public const int CurrentVersion = 1;
        public const string ChatType = "chat";
        public const string DonationType = "donation";
        public const string SourceId = "broadcast.soop";
        public const string StarBalloonUnit =
            "SOOP_STAR_BALLOON";
        public const int MaxEventIdLength = 256;

        public static bool TryCreateEvent(
            SoopBridgeMessage message,
            long receivedTimestampUs,
            out NormalizedEvent value,
            out string error)
        {
            value = default;
            error = null;

            if (message == null)
            {
                error =
                    "SOOP bridge message is required.";
                return false;
            }

            if (message.version !=
                CurrentVersion)
            {
                error =
                    $"SOOP bridge version {message.version} is unsupported; expected {CurrentVersion}.";
                return false;
            }

            if (!TryValidateEventId(
                    message.eventId,
                    out error))
            {
                return false;
            }

            if (receivedTimestampUs < 0)
            {
                error =
                    "SOOP bridge receive timestamp cannot be negative.";
                return false;
            }

            var actorId =
                !string.IsNullOrWhiteSpace(
                    message.userId)
                    ? message.userId
                    : message.nickname;

            if (string.Equals(
                    message.type,
                    ChatType,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(
                        message.text))
                {
                    error =
                        "SOOP chat message text is required.";
                    return false;
                }

                value =
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .BroadcastChatMessage,
                        SourceId,
                        receivedTimestampUs,
                        actorId:
                            actorId,
                        text:
                            message.text,
                        actorName:
                            message.nickname);
            }
            else if (string.Equals(
                         message.type,
                         DonationType,
                         StringComparison.Ordinal))
            {
                if (message.count <= 0 ||
                    message.count >
                    1_000_000_000L)
                {
                    error =
                        "SOOP donation count must be between 1 and 1,000,000,000.";
                    return false;
                }

                value =
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .BroadcastDonation,
                        SourceId,
                        receivedTimestampUs,
                        actorId:
                            actorId,
                        text:
                            message.text,
                        amount:
                            message.count,
                        currency:
                            StarBalloonUnit,
                        hasAmount:
                            true,
                        actorName:
                            message.nickname);
            }
            else
            {
                error =
                    $"SOOP bridge message type '{message.type}' is unsupported.";
                return false;
            }

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
        public static bool TryValidateEventId(
            string eventId,
            out string error)
        {
            error = null;

            if (eventId == null)
            {
                return true;
            }

            if (eventId.Length >
                MaxEventIdLength)
            {
                error =
                    $"SOOP bridge event id exceeds the {MaxEventIdLength} character limit.";
                return false;
            }

            return true;
        }

    }
}
