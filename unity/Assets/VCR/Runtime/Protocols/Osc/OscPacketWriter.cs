using System;
using System.Collections.Generic;
using System.Text;

namespace VCR.Runtime.Protocols.Osc
{
    public static class OscPacketWriter
    {
        public static byte[] WriteMessage(
            string address,
            params OscArgument[] arguments)
        {
            return WriteMessage(
                address,
                arguments,
                arguments?.Length ?? 0);
        }

        public static byte[] WriteMessage(
            string address,
            OscArgument[] arguments,
            int argumentCount)
        {
            if (argumentCount < 0 ||
                argumentCount >
                    (arguments?.Length ?? 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(argumentCount));
            }

            var size =
                GetMessageSize(
                    address,
                    arguments,
                    argumentCount);
            var packet =
                new byte[size];
            var cursor = 0;

            WritePaddedString(
                packet,
                ref cursor,
                address);
            WriteTypeTags(
                packet,
                ref cursor,
                arguments,
                argumentCount);

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                var argument =
                    arguments[i];

                switch (argument.Type)
                {
                    case OscArgumentType.Int32:
                        WriteInt32(
                            packet,
                            ref cursor,
                            argument.IntValue);
                        break;

                    case OscArgumentType.Float32:
                        WriteFloat32(
                            packet,
                            ref cursor,
                            argument.FloatValue);
                        break;

                    case OscArgumentType.String:
                        WritePaddedString(
                            packet,
                            ref cursor,
                            argument.StringValue ??
                            string.Empty);
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unsupported OSC argument type.");
                }
            }

            return packet;
        }

        public static byte[] WriteBundle(
            IReadOnlyList<byte[]> messages)
        {
            var size = 16;

            if (messages != null)
            {
                foreach (var message in
                         messages)
                {
                    if (message == null ||
                        message.Length == 0 ||
                        message.Length >
                            OscPacketReader
                                .MaxPacketBytes)
                    {
                        continue;
                    }

                    size =
                        checked(
                            size +
                            4 +
                            message.Length);

                    if (size >
                        OscPacketReader
                            .MaxPacketBytes)
                    {
                        throw new InvalidOperationException(
                            "OSC bundle exceeds P0 packet limit.");
                    }
                }
            }

            var packet =
                new byte[size];
            var cursor = 0;

            WritePaddedString(
                packet,
                ref cursor,
                "#bundle");

            // OSC immediate timetag = 1.
            WriteUInt64(
                packet,
                ref cursor,
                1UL);

            if (messages != null)
            {
                foreach (var message in
                         messages)
                {
                    if (message == null ||
                        message.Length == 0 ||
                        message.Length >
                            OscPacketReader
                                .MaxPacketBytes)
                    {
                        continue;
                    }

                    WriteInt32(
                        packet,
                        ref cursor,
                        message.Length);
                    Buffer.BlockCopy(
                        message,
                        0,
                        packet,
                        cursor,
                        message.Length);
                    cursor +=
                        message.Length;
                }
            }

            return packet;
        }

        private static int GetMessageSize(
            string address,
            OscArgument[] arguments,
            int argumentCount)
        {
            var size =
                checked(
                    GetPaddedStringSize(
                        address) +
                    Align4(
                        argumentCount + 2));

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                var argument =
                    arguments[i];

                size =
                    argument.Type switch
                    {
                        OscArgumentType.Int32 =>
                            checked(
                                size + 4),
                        OscArgumentType.Float32 =>
                            checked(
                                size + 4),
                        OscArgumentType.String =>
                            checked(
                                size +
                                GetPaddedStringSize(
                                    argument.StringValue ??
                                    string.Empty)),
                        _ =>
                            throw new InvalidOperationException(
                                "Unsupported OSC argument type.")
                    };
            }

            return size;
        }

        private static int GetPaddedStringSize(
            string value)
        {
            value ??=
                string.Empty;

            var byteCount =
                Encoding.UTF8.GetByteCount(
                    value);

            return
                Align4(
                    checked(
                        byteCount + 1));
        }

        private static void WriteTypeTags(
            byte[] packet,
            ref int cursor,
            OscArgument[] arguments,
            int argumentCount)
        {
            packet[cursor++] =
                (byte)',';

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                packet[cursor++] =
                    arguments[i].Type switch
                    {
                        OscArgumentType.Int32 =>
                            (byte)'i',
                        OscArgumentType.Float32 =>
                            (byte)'f',
                        OscArgumentType.String =>
                            (byte)'s',
                        _ =>
                            throw new InvalidOperationException(
                                "Unsupported OSC argument type.")
                    };
            }

            // The packet was zero-initialized, so the NUL terminator and
            // alignment padding only require advancing the cursor.
            cursor++;
            cursor =
                Align4(
                    cursor);
        }

        private static void WritePaddedString(
            byte[] packet,
            ref int cursor,
            string value)
        {
            value ??=
                string.Empty;

            if (value.Length > 0)
            {
                cursor +=
                    Encoding.UTF8.GetBytes(
                        value,
                        0,
                        value.Length,
                        packet,
                        cursor);
            }

            // The packet was zero-initialized, so the NUL terminator and
            // alignment padding only require advancing the cursor.
            cursor++;
            cursor =
                Align4(
                    cursor);
        }

        private static void WriteInt32(
            byte[] packet,
            ref int cursor,
            int value)
        {
            packet[cursor++] =
                (byte)(
                    (value >> 24) &
                    0xff);
            packet[cursor++] =
                (byte)(
                    (value >> 16) &
                    0xff);
            packet[cursor++] =
                (byte)(
                    (value >> 8) &
                    0xff);
            packet[cursor++] =
                (byte)(
                    value &
                    0xff);
        }

        private static void WriteUInt64(
            byte[] packet,
            ref int cursor,
            ulong value)
        {
            for (var shift = 56;
                 shift >= 0;
                 shift -= 8)
            {
                packet[cursor++] =
                    (byte)(
                        (value >> shift) &
                        0xff);
            }
        }

        private static void WriteFloat32(
            byte[] packet,
            ref int cursor,
            float value)
        {
            WriteInt32(
                packet,
                ref cursor,
                BitConverter
                    .SingleToInt32Bits(
                        value));
        }

        private static int Align4(
            int value)
        {
            return
                checked(
                    value + 3) &
                ~3;
        }
    }
}
