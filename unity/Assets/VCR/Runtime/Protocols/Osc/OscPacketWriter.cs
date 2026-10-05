using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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

            using var stream =
                new MemoryStream(256);
            WritePaddedString(
                stream,
                address);
            WriteTypeTags(
                stream,
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
                            stream,
                            argument.IntValue);
                        break;

                    case OscArgumentType.Float32:
                        WriteFloat32(
                            stream,
                            argument.FloatValue);
                        break;

                    case OscArgumentType.String:
                        WritePaddedString(
                            stream,
                            argument.StringValue ??
                            string.Empty);
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unsupported OSC argument type.");
                }
            }

            return stream.ToArray();
        }

        public static byte[] WriteBundle(
            IReadOnlyList<byte[]> messages)
        {
            using var stream = new MemoryStream(1024);
            WritePaddedString(stream, "#bundle");

            // OSC immediate timetag = 1.
            WriteUInt64(stream, 1UL);

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    if (message == null ||
                        message.Length == 0 ||
                        message.Length > OscPacketReader.MaxPacketBytes)
                    {
                        continue;
                    }

                    WriteInt32(stream, message.Length);
                    stream.Write(message, 0, message.Length);

                    if (stream.Length > OscPacketReader.MaxPacketBytes)
                    {
                        throw new InvalidOperationException(
                            "OSC bundle exceeds P0 packet limit.");
                    }
                }
            }

            return stream.ToArray();
        }

        private static void WriteTypeTags(
            Stream stream,
            OscArgument[] arguments,
            int argumentCount)
        {
            stream.WriteByte(
                (byte)',');

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                stream.WriteByte(
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
                    });
            }

            stream.WriteByte(0);

            while ((stream.Position & 3) != 0)
            {
                stream.WriteByte(0);
            }
        }

        private static void WritePaddedString(
            Stream stream,
            string value)
        {
            value ??= string.Empty;

            var byteCount =
                Encoding.UTF8.GetByteCount(
                    value);

            if (byteCount > 0)
            {
                var buffer =
                    ArrayPool<byte>
                        .Shared
                        .Rent(byteCount);

                try
                {
                    var written =
                        Encoding.UTF8.GetBytes(
                            value,
                            0,
                            value.Length,
                            buffer,
                            0);
                    stream.Write(
                        buffer,
                        0,
                        written);
                }
                finally
                {
                    ArrayPool<byte>
                        .Shared
                        .Return(buffer);
                }
            }

            stream.WriteByte(0);

            while ((stream.Position & 3) != 0)
            {
                stream.WriteByte(0);
            }
        }

        private static void WriteInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xff));
            stream.WriteByte((byte)((value >> 16) & 0xff));
            stream.WriteByte((byte)((value >> 8) & 0xff));
            stream.WriteByte((byte)(value & 0xff));
        }

        private static void WriteUInt64(Stream stream, ulong value)
        {
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                stream.WriteByte((byte)((value >> shift) & 0xff));
            }
        }

        private static void WriteFloat32(
            Stream stream,
            float value)
        {
            WriteInt32(
                stream,
                BitConverter.SingleToInt32Bits(
                    value));
        }
    }
}
