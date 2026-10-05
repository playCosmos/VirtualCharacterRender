using System;
using System.Collections.Generic;
using System.Text;

namespace VCR.Runtime.Protocols.Osc
{
    public static class OscPacketReader
    {
        public const int MaxPacketBytes = 16 * 1024;
        public const int MaxMessagesPerPacket = 256;
        public const int MaxArgumentsPerMessage = 64;
        private const int MaxBundleDepth = 4;

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        public static bool TryReadMessages(
            byte[] packet,
            int length,
            List<OscMessage> output)
        {
            if (packet == null ||
                output == null ||
                length <= 0 ||
                length > packet.Length ||
                length > MaxPacketBytes)
            {
                return false;
            }

            output.Clear();

            try
            {
                var success =
                    ReadPacket(
                        packet,
                        0,
                        length,
                        output,
                        depth: 0);

                if (!success)
                {
                    output.Clear();
                }

                return success;
            }
            catch
            {
                output.Clear();
                return false;
            }
        }

        private static bool ReadPacket(
            byte[] data,
            int offset,
            int length,
            List<OscMessage> output,
            int depth)
        {
            if (length <= 0 ||
                offset < 0 ||
                offset + length > data.Length ||
                depth > MaxBundleDepth)
            {
                return false;
            }

            if (StartsWithBundle(data, offset, length))
            {
                return ReadBundle(
                    data,
                    offset,
                    length,
                    output,
                    depth);
            }

            return ReadMessage(data, offset, length, output);
        }

        private static bool ReadBundle(
            byte[] data,
            int offset,
            int length,
            List<OscMessage> output,
            int depth)
        {
            var end = offset + length;

            if (!StartsWithBundle(
                    data,
                    offset,
                    length))
            {
                return false;
            }

            // "#bundle\0" is exactly 8 bytes and already 4-byte aligned.
            // Avoid decoding a transient string for every bundle.
            var cursor =
                offset + 8;

            // 64-bit OSC timetag. P0 does not schedule future bundles; arrival
            // order is the runtime order.
            if (cursor + 8 > end)
            {
                return false;
            }

            cursor += 8;

            while (cursor < end)
            {
                if (!TryReadInt32(data, end, ref cursor, out var elementSize) ||
                    elementSize <= 0 ||
                    cursor + elementSize > end)
                {
                    return false;
                }

                if (!ReadPacket(
                    data,
                    cursor,
                    elementSize,
                    output,
                    depth + 1))
                {
                    return false;
                }

                cursor += elementSize;
            }

            return cursor == end;
        }

        private static bool ReadMessage(
            byte[] data,
            int offset,
            int length,
            List<OscMessage> output)
        {
            if (output.Count >=
                MaxMessagesPerPacket)
            {
                return false;
            }

            var end = offset + length;
            var cursor = offset;

            if (!TryReadPaddedString(
                data,
                end,
                ref cursor,
                out var address) ||
                string.IsNullOrEmpty(address) ||
                address[0] != '/')
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

            var arguments =
                new OscArgument[
                    argumentCount];

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                switch (data[
                    typeTagStart + i])
                {
                    case (byte)'i':
                        if (!TryReadInt32(
                            data,
                            end,
                            ref cursor,
                            out var intValue))
                        {
                            return false;
                        }

                        arguments[i] =
                            OscArgument.FromInt(intValue);
                        break;

                    case (byte)'f':
                        if (!TryReadFloat32(
                            data,
                            end,
                            ref cursor,
                            out var floatValue))
                        {
                            return false;
                        }

                        arguments[i] =
                            OscArgument.FromFloat(floatValue);
                        break;

                    case (byte)'s':
                        if (!TryReadPaddedString(
                            data,
                            end,
                            ref cursor,
                            out var stringValue))
                        {
                            return false;
                        }

                        arguments[i] =
                            OscArgument.FromString(stringValue);
                        break;

                    default:
                        // VMC requires recipients to ignore packets with
                        // unsupported argument types rather than guessing.
                        return false;
                }
            }

            output.Add(new OscMessage(address, arguments));
            return true;
        }

        private static bool StartsWithBundle(
            byte[] data,
            int offset,
            int length)
        {
            if (length < 8)
            {
                return false;
            }

            return
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
            typeTagStart = cursor;

            while (cursor < end &&
                   data[cursor] != 0)
            {
                argumentCount++;

                if (argumentCount >
                    MaxArgumentsPerMessage)
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

        private static bool TryReadPaddedString(
            byte[] data,
            int end,
            ref int cursor,
            out string value)
        {
            value = null;

            if (cursor >= end)
            {
                return false;
            }

            var start = cursor;
            while (cursor < end && data[cursor] != 0)
            {
                cursor++;
            }

            if (cursor >= end)
            {
                return false;
            }

            value = StrictUtf8.GetString(
                data,
                start,
                cursor - start);

            cursor++;
            cursor = Align4(cursor);

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

            value = Int32BitsToSingle(bits);
            return true;
        }

        private static float Int32BitsToSingle(int bits)
        {
            return BitConverter.Int32BitsToSingle(bits);
        }

        private static int Align4(int value)
        {
            return (value + 3) & ~3;
        }
    }
}
