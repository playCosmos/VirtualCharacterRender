using System;
using System.Collections.Generic;
using System.Text;
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

    /// <summary>
    /// Allocation-minimized OSC packet reader specialized for /vcr/event.
    ///
    /// The packet is fully validated before any event is returned. A second
    /// pass maps valid event messages directly from the caller-owned datagram
    /// buffer, avoiding transient OscMessage/address/argument-array objects.
    /// Only strings retained by the resulting NormalizedEvent are decoded.
    /// </summary>
    public static class OscNormalizedEventPacketReader
    {
        private const int MaxBundleDepth = 4;

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        public static bool TryReadEvents(
            byte[] packet,
            int length,
            string sourceId,
            long receivedTimestampUs,
            List<NormalizedEvent> output,
            out int rejectedMessages)
        {
            rejectedMessages = 0;

            if (packet == null ||
                output == null ||
                length <= 0 ||
                length > packet.Length ||
                length >
                    OscPacketReader.MaxPacketBytes)
            {
                return false;
            }

            output.Clear();

            try
            {
                var messageCount = 0;

                if (!ValidatePacket(
                        packet,
                        0,
                        length,
                        depth: 0,
                        ref messageCount))
                {
                    return false;
                }

                if (!ReadPacketEvents(
                        packet,
                        0,
                        length,
                        sourceId,
                        receivedTimestampUs,
                        output,
                        depth: 0,
                        ref rejectedMessages))
                {
                    output.Clear();
                    rejectedMessages = 0;
                    return false;
                }

                return true;
            }
            catch
            {
                output.Clear();
                rejectedMessages = 0;
                return false;
            }
        }

        private static bool ValidatePacket(
            byte[] data,
            int offset,
            int length,
            int depth,
            ref int messageCount)
        {
            if (length <= 0 ||
                offset < 0 ||
                offset + length >
                    data.Length ||
                depth >
                    MaxBundleDepth)
            {
                return false;
            }

            if (StartsWithBundle(
                    data,
                    offset,
                    length))
            {
                return ValidateBundle(
                    data,
                    offset,
                    length,
                    depth,
                    ref messageCount);
            }

            messageCount++;

            if (messageCount >
                OscPacketReader.MaxMessagesPerPacket)
            {
                return false;
            }

            return ValidateMessage(
                data,
                offset,
                length);
        }

        private static bool ValidateBundle(
            byte[] data,
            int offset,
            int length,
            int depth,
            ref int messageCount)
        {
            var end =
                offset + length;
            var cursor =
                offset + 8;

            if (cursor + 8 > end)
            {
                return false;
            }

            cursor += 8;

            while (cursor < end)
            {
                if (!TryReadInt32(
                        data,
                        end,
                        ref cursor,
                        out var elementSize) ||
                    elementSize <= 0 ||
                    cursor + elementSize > end)
                {
                    return false;
                }

                if (!ValidatePacket(
                        data,
                        cursor,
                        elementSize,
                        depth + 1,
                        ref messageCount))
                {
                    return false;
                }

                cursor += elementSize;
            }

            return cursor == end;
        }

        private static bool ValidateMessage(
            byte[] data,
            int offset,
            int length)
        {
            var end =
                offset + length;
            var cursor =
                offset;

            if (!TryReadPaddedStringRange(
                    data,
                    end,
                    ref cursor,
                    validateUtf8: true,
                    out var addressStart,
                    out var addressLength) ||
                addressLength <= 0 ||
                data[addressStart] !=
                    (byte)'/')
            {
                return false;
            }

            if (!TryReadTypeTags(
                    data,
                    end,
                    ref cursor,
                    out var typeTagStart,
                    out var argumentCount))
            {
                return false;
            }

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                switch (data[
                    typeTagStart + i])
                {
                    case (byte)'i':
                    case (byte)'f':
                        if (cursor + 4 > end)
                        {
                            return false;
                        }

                        cursor += 4;
                        break;

                    case (byte)'s':
                        if (!TryReadPaddedStringRange(
                                data,
                                end,
                                ref cursor,
                                validateUtf8: true,
                                out _,
                                out _))
                        {
                            return false;
                        }

                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool ReadPacketEvents(
            byte[] data,
            int offset,
            int length,
            string sourceId,
            long receivedTimestampUs,
            List<NormalizedEvent> output,
            int depth,
            ref int rejectedMessages)
        {
            if (StartsWithBundle(
                    data,
                    offset,
                    length))
            {
                return ReadBundleEvents(
                    data,
                    offset,
                    length,
                    sourceId,
                    receivedTimestampUs,
                    output,
                    depth,
                    ref rejectedMessages);
            }

            return ReadMessageEvent(
                data,
                offset,
                length,
                sourceId,
                receivedTimestampUs,
                output,
                ref rejectedMessages);
        }

        private static bool ReadBundleEvents(
            byte[] data,
            int offset,
            int length,
            string sourceId,
            long receivedTimestampUs,
            List<NormalizedEvent> output,
            int depth,
            ref int rejectedMessages)
        {
            if (depth >
                MaxBundleDepth)
            {
                return false;
            }

            var end =
                offset + length;
            var cursor =
                offset + 16;

            while (cursor < end)
            {
                if (!TryReadInt32(
                        data,
                        end,
                        ref cursor,
                        out var elementSize) ||
                    elementSize <= 0 ||
                    cursor + elementSize > end)
                {
                    return false;
                }

                if (!ReadPacketEvents(
                        data,
                        cursor,
                        elementSize,
                        sourceId,
                        receivedTimestampUs,
                        output,
                        depth + 1,
                        ref rejectedMessages))
                {
                    return false;
                }

                cursor += elementSize;
            }

            return cursor == end;
        }

        private static bool ReadMessageEvent(
            byte[] data,
            int offset,
            int length,
            string sourceId,
            long receivedTimestampUs,
            List<NormalizedEvent> output,
            ref int rejectedMessages)
        {
            var end =
                offset + length;
            var cursor =
                offset;

            if (!TryReadPaddedStringRange(
                    data,
                    end,
                    ref cursor,
                    validateUtf8: false,
                    out var addressStart,
                    out var addressLength) ||
                !TryReadTypeTags(
                    data,
                    end,
                    ref cursor,
                    out var typeTagStart,
                    out var argumentCount))
            {
                return false;
            }

            if (!RangeEqualsAscii(
                    data,
                    addressStart,
                    addressLength,
                    OscNormalizedEventMapper
                        .EventAddress))
            {
                rejectedMessages++;
                return true;
            }

            if (!TryCreateEvent(
                    data,
                    end,
                    cursor,
                    typeTagStart,
                    argumentCount,
                    sourceId,
                    receivedTimestampUs,
                    out var value))
            {
                rejectedMessages++;
                return true;
            }

            output.Add(
                value);
            return true;
        }

        private static bool TryCreateEvent(
            byte[] data,
            int end,
            int cursor,
            int typeTagStart,
            int argumentCount,
            string sourceId,
            long receivedTimestampUs,
            out NormalizedEvent value)
        {
            value = default;

            if (argumentCount < 1 ||
                argumentCount > 6 ||
                data[typeTagStart] !=
                    (byte)'s' ||
                !TryReadRequiredString(
                    data,
                    end,
                    ref cursor,
                    out var type))
            {
                return false;
            }

            string actorId = null;
            string text = null;
            string actorName = null;
            string currency = null;
            var amount = 0.0;
            var hasAmount = false;

            if (argumentCount >= 2 &&
                (data[typeTagStart + 1] !=
                     (byte)'s' ||
                 !TryReadOptionalString(
                     data,
                     end,
                     ref cursor,
                     out actorId)))
            {
                return false;
            }

            if (argumentCount >= 3 &&
                (data[typeTagStart + 2] !=
                     (byte)'s' ||
                 !TryReadOptionalString(
                     data,
                     end,
                     ref cursor,
                     out text)))
            {
                return false;
            }

            if (argumentCount >= 4)
            {
                hasAmount = true;

                switch (data[
                    typeTagStart + 3])
                {
                    case (byte)'f':
                        if (!TryReadFloat32(
                                data,
                                end,
                                ref cursor,
                                out var floatAmount))
                        {
                            return false;
                        }

                        amount =
                            floatAmount;
                        break;

                    case (byte)'i':
                        if (!TryReadInt32(
                                data,
                                end,
                                ref cursor,
                                out var intAmount))
                        {
                            return false;
                        }

                        amount =
                            intAmount;
                        break;

                    default:
                        return false;
                }

                if (argumentCount < 5 ||
                    data[typeTagStart + 4] !=
                        (byte)'s' ||
                    !TryReadRequiredString(
                        data,
                        end,
                        ref cursor,
                        out currency))
                {
                    return false;
                }
            }

            if (argumentCount >= 6 &&
                (data[typeTagStart + 5] !=
                     (byte)'s' ||
                 !TryReadOptionalString(
                     data,
                     end,
                     ref cursor,
                     out actorName)))
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
                    out _))
            {
                value = default;
                return false;
            }

            return true;
        }

        private static bool TryReadRequiredString(
            byte[] data,
            int end,
            ref int cursor,
            out string value)
        {
            value = null;

            if (!TryReadPaddedStringRange(
                    data,
                    end,
                    ref cursor,
                    validateUtf8: false,
                    out var start,
                    out var length) ||
                length == 0)
            {
                return false;
            }

            value =
                StrictUtf8.GetString(
                    data,
                    start,
                    length);

            return
                !string.IsNullOrWhiteSpace(
                    value);
        }

        private static bool TryReadOptionalString(
            byte[] data,
            int end,
            ref int cursor,
            out string value)
        {
            value = null;

            if (!TryReadPaddedStringRange(
                    data,
                    end,
                    ref cursor,
                    validateUtf8: false,
                    out var start,
                    out var length))
            {
                return false;
            }

            if (length == 0)
            {
                return true;
            }

            value =
                StrictUtf8.GetString(
                    data,
                    start,
                    length);
            return true;
        }

        private static bool RangeEqualsAscii(
            byte[] data,
            int start,
            int length,
            string expected)
        {
            if (expected == null ||
                length != expected.Length)
            {
                return false;
            }

            for (var i = 0;
                 i < length;
                 i++)
            {
                if (expected[i] > 0x7f ||
                    data[start + i] !=
                        (byte)expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool StartsWithBundle(
            byte[] data,
            int offset,
            int length)
        {
            return
                length >= 8 &&
                data[offset + 0] == (byte)'#' &&
                data[offset + 1] == (byte)'b' &&
                data[offset + 2] == (byte)'u' &&
                data[offset + 3] == (byte)'n' &&
                data[offset + 4] == (byte)'d' &&
                data[offset + 5] == (byte)'l' &&
                data[offset + 6] == (byte)'e' &&
                data[offset + 7] == 0;
        }

        private static bool TryReadTypeTags(
            byte[] data,
            int end,
            ref int cursor,
            out int typeTagStart,
            out int argumentCount)
        {
            typeTagStart = 0;
            argumentCount = 0;

            if (cursor >= end ||
                data[cursor] !=
                    (byte)',')
            {
                return false;
            }

            cursor++;
            typeTagStart =
                cursor;

            while (cursor < end &&
                   data[cursor] != 0)
            {
                argumentCount++;

                if (argumentCount >
                    OscPacketReader
                        .MaxArgumentsPerMessage)
                {
                    return false;
                }

                cursor++;
            }

            if (cursor >= end)
            {
                return false;
            }

            cursor++;
            cursor =
                Align4(
                    cursor);

            return cursor <= end;
        }

        private static bool TryReadPaddedStringRange(
            byte[] data,
            int end,
            ref int cursor,
            bool validateUtf8,
            out int start,
            out int length)
        {
            start =
                cursor;
            length = 0;

            if (cursor >= end)
            {
                return false;
            }

            while (cursor < end &&
                   data[cursor] != 0)
            {
                cursor++;
            }

            if (cursor >= end)
            {
                return false;
            }

            length =
                cursor -
                start;

            if (validateUtf8 &&
                length > 0)
            {
                StrictUtf8.GetCharCount(
                    data,
                    start,
                    length);
            }

            cursor++;
            cursor =
                Align4(
                    cursor);

            return cursor <= end;
        }

        private static bool TryReadInt32(
            byte[] data,
            int end,
            ref int cursor,
            out int value)
        {
            value = 0;

            if (cursor + 4 > end)
            {
                return false;
            }

            value =
                (data[cursor + 0] << 24) |
                (data[cursor + 1] << 16) |
                (data[cursor + 2] << 8) |
                data[cursor + 3];

            cursor += 4;
            return true;
        }

        private static bool TryReadFloat32(
            byte[] data,
            int end,
            ref int cursor,
            out float value)
        {
            value = 0f;

            if (!TryReadInt32(
                    data,
                    end,
                    ref cursor,
                    out var bits))
            {
                return false;
            }

            value =
                BitConverter.Int32BitsToSingle(
                    bits);
            return true;
        }

        private static int Align4(
            int value)
        {
            return
                (value + 3) &
                ~3;
        }
    }
}
