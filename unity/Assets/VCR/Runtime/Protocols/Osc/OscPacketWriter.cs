using System;
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
            using var stream = new MemoryStream(256);
            WritePaddedString(stream, address);

            var tags = new StringBuilder(",");
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    tags.Append(argument.Type switch
                    {
                        OscArgumentType.Int32 => 'i',
                        OscArgumentType.Float32 => 'f',
                        OscArgumentType.String => 's',
                        _ => throw new InvalidOperationException(
                            "Unsupported OSC argument type.")
                    });
                }
            }

            WritePaddedString(stream, tags.ToString());

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    switch (argument.Type)
                    {
                        case OscArgumentType.Int32:
                            WriteInt32(stream, argument.IntValue);
                            break;
                        case OscArgumentType.Float32:
                            WriteFloat32(stream, argument.FloatValue);
                            break;
                        case OscArgumentType.String:
                            WritePaddedString(
                                stream,
                                argument.StringValue ?? string.Empty);
                            break;
                    }
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

        private static void WritePaddedString(
            Stream stream,
            string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            stream.Write(bytes, 0, bytes.Length);
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
