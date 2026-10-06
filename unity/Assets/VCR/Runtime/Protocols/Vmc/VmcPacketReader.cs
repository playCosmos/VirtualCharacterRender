using System;
using System.Text;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Protocols.Vmc
{
    /// <summary>
    /// Allocation-minimized OSC reader specialized for the VMC messages used by
    /// the runtime receiver.
    ///
    /// The packet is validated completely before any accumulator state is
    /// mutated. A second pass applies known VMC messages directly from the
    /// caller-owned datagram buffer, avoiding transient OscMessage/address/
    /// argument-array allocations. Standard bone/expression names are resolved
    /// from UTF-8 byte spans; only custom expression names materialize strings.
    /// </summary>
    internal static class VmcPacketReader
    {
        private const int MaxBundleDepth =
            4;

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        private enum AddressKind
        {
            Unknown = 0,
            Available,
            Time,
            Root,
            Bone,
            BlendValue,
            BlendApply
        }

        public static bool TryProcess(
            byte[] packet,
            int length,
            VmcFrameAccumulator accumulator,
            long arrivalTimestampUs,
            out TrackingFrame frame)
        {
            frame = null;

            if (packet == null ||
                accumulator == null ||
                length <= 0 ||
                length > packet.Length ||
                length >
                    OscPacketReader.MaxPacketBytes)
            {
                return false;
            }

            try
            {
                var messageCount = 0;

                if (!ValidatePacket(
                        packet,
                        offset: 0,
                        length,
                        depth: 0,
                        ref messageCount))
                {
                    return false;
                }

                var stateChanged = false;
                var poseChanged = false;
                var expressionApply = false;

                if (!ApplyPacket(
                        packet,
                        offset: 0,
                        length,
                        accumulator,
                        depth: 0,
                        ref stateChanged,
                        ref poseChanged,
                        ref expressionApply))
                {
                    // Validation succeeded, so this should only be reachable if
                    // the implementation's two parsing passes diverge.
                    return false;
                }

                frame =
                    accumulator.CompletePacket(
                        stateChanged,
                        poseChanged,
                        expressionApply,
                        arrivalTimestampUs);

                return true;
            }
            catch
            {
                frame = null;
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
                offset +
                length;
            var cursor =
                offset +
                8;

            if (cursor + 8 >
                end)
            {
                return false;
            }

            cursor +=
                8;

            while (cursor < end)
            {
                if (!TryReadInt32(
                        data,
                        end,
                        ref cursor,
                        out var elementSize) ||
                    elementSize <= 0 ||
                    cursor + elementSize >
                        end)
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

                cursor +=
                    elementSize;
            }

            return cursor ==
                end;
        }

        private static bool ValidateMessage(
            byte[] data,
            int offset,
            int length)
        {
            var end =
                offset +
                length;
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
                    typeTagStart +
                    i])
                {
                    case (byte)'i':
                    case (byte)'f':
                        if (cursor + 4 >
                            end)
                        {
                            return false;
                        }

                        cursor +=
                            4;
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

        private static bool ApplyPacket(
            byte[] data,
            int offset,
            int length,
            VmcFrameAccumulator accumulator,
            int depth,
            ref bool stateChanged,
            ref bool poseChanged,
            ref bool expressionApply)
        {
            if (StartsWithBundle(
                    data,
                    offset,
                    length))
            {
                var end =
                    offset +
                    length;
                var cursor =
                    offset +
                    16;

                while (cursor < end)
                {
                    if (!TryReadInt32(
                            data,
                            end,
                            ref cursor,
                            out var elementSize))
                    {
                        return false;
                    }

                    if (!ApplyPacket(
                            data,
                            cursor,
                            elementSize,
                            accumulator,
                            depth + 1,
                            ref stateChanged,
                            ref poseChanged,
                            ref expressionApply))
                    {
                        return false;
                    }

                    cursor +=
                        elementSize;
                }

                return cursor ==
                    end;
            }

            return ApplyMessage(
                data,
                offset,
                length,
                accumulator,
                ref stateChanged,
                ref poseChanged,
                ref expressionApply);
        }

        private static bool ApplyMessage(
            byte[] data,
            int offset,
            int length,
            VmcFrameAccumulator accumulator,
            ref bool stateChanged,
            ref bool poseChanged,
            ref bool expressionApply)
        {
            var end =
                offset +
                length;
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

            var kind =
                GetAddressKind(
                    data.AsSpan(
                        addressStart,
                        addressLength));

            var hasInt0 = false;
            var int0 = 0;
            var hasInt3 = false;
            var int3 = 0;
            var hasFloat0 = false;
            var float0 = 0f;
            var hasString0 = false;
            var string0Start = 0;
            var string0Length = 0;
            var hasFloat1 = false;
            var float1 = 0f;
            var transformMask = 0;
            var px = 0f;
            var py = 0f;
            var pz = 0f;
            var qx = 0f;
            var qy = 0f;
            var qz = 0f;
            var qw = 0f;

            for (var i = 0;
                 i < argumentCount;
                 i++)
            {
                switch (data[
                    typeTagStart +
                    i])
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

                        if (i == 0)
                        {
                            hasInt0 = true;
                            int0 =
                                intValue;
                        }
                        else if (i == 3)
                        {
                            hasInt3 = true;
                            int3 =
                                intValue;
                        }

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

                        if (i == 0)
                        {
                            hasFloat0 = true;
                            float0 =
                                floatValue;
                        }

                        if (i == 1)
                        {
                            hasFloat1 = true;
                            float1 =
                                floatValue;
                        }

                        if (i >= 1 &&
                            i <= 7)
                        {
                            transformMask |=
                                1 <<
                                (i - 1);

                            switch (i)
                            {
                                case 1:
                                    px =
                                        floatValue;
                                    break;
                                case 2:
                                    py =
                                        floatValue;
                                    break;
                                case 3:
                                    pz =
                                        floatValue;
                                    break;
                                case 4:
                                    qx =
                                        floatValue;
                                    break;
                                case 5:
                                    qy =
                                        floatValue;
                                    break;
                                case 6:
                                    qz =
                                        floatValue;
                                    break;
                                case 7:
                                    qw =
                                        floatValue;
                                    break;
                            }
                        }

                        break;

                    case (byte)'s':
                        if (!TryReadPaddedStringRange(
                                data,
                                end,
                                ref cursor,
                                validateUtf8: false,
                                out var stringStart,
                                out var stringLength))
                        {
                            return false;
                        }

                        if (i == 0)
                        {
                            hasString0 =
                                true;
                            string0Start =
                                stringStart;
                            string0Length =
                                stringLength;
                        }

                        break;

                    default:
                        return false;
                }
            }

            switch (kind)
            {
                case AddressKind.Available:
                    if (argumentCount >= 1 &&
                        hasInt0)
                    {
                        stateChanged |=
                            accumulator.ApplyAvailable(
                                int0,
                                argumentCount >= 4 &&
                                hasInt3,
                                int3);
                    }

                    break;

                case AddressKind.Time:
                    if (argumentCount >= 1 &&
                        hasFloat0)
                    {
                        accumulator.ApplyTime(
                            float0);
                    }

                    break;

                case AddressKind.Root:
                    if (argumentCount >= 8 &&
                        hasString0 &&
                        transformMask == 0x7f)
                    {
                        poseChanged |=
                            accumulator.ApplyRoot(
                                px,
                                py,
                                pz,
                                qx,
                                qy,
                                qz,
                                qw);
                    }

                    break;

                case AddressKind.Bone:
                    if (argumentCount >= 8 &&
                        hasString0 &&
                        transformMask == 0x7f &&
                        HumanoidBoneNames
                            .TryParseAscii(
                                data.AsSpan(
                                    string0Start,
                                    string0Length),
                                out var bone))
                    {
                        poseChanged |=
                            accumulator.ApplyBone(
                                bone,
                                px,
                                py,
                                pz,
                                qx,
                                qy,
                                qz,
                                qw);
                    }

                    break;

                case AddressKind.BlendValue:
                    if (argumentCount >= 2 &&
                        hasString0 &&
                        hasFloat1)
                    {
                        var nameBytes =
                            data.AsSpan(
                                string0Start,
                                string0Length);

                        if (StandardExpressionNames
                            .TryParseAscii(
                                nameBytes,
                                out var expression))
                        {
                            accumulator.ApplyStandardBlend(
                                expression,
                                float1);
                        }
                        else
                        {
                            accumulator.ApplyCustomBlendUtf8(
                                data,
                                string0Start,
                                string0Length,
                                float1);
                        }
                    }

                    break;

                case AddressKind.BlendApply:
                    expressionApply =
                        true;
                    break;
            }

            return true;
        }

        private static AddressKind GetAddressKind(
            ReadOnlySpan<byte> address)
        {
            if (MatchesAscii(
                    address,
                    "/VMC/Ext/OK"))
            {
                return AddressKind.Available;
            }

            if (MatchesAscii(
                    address,
                    "/VMC/Ext/T"))
            {
                return AddressKind.Time;
            }

            if (MatchesAscii(
                    address,
                    "/VMC/Ext/Root/Pos"))
            {
                return AddressKind.Root;
            }

            if (MatchesAscii(
                    address,
                    "/VMC/Ext/Bone/Pos"))
            {
                return AddressKind.Bone;
            }

            if (MatchesAscii(
                    address,
                    "/VMC/Ext/Blend/Val"))
            {
                return AddressKind.BlendValue;
            }

            if (MatchesAscii(
                    address,
                    "/VMC/Ext/Blend/Apply"))
            {
                return AddressKind.BlendApply;
            }

            return AddressKind.Unknown;
        }

        private static bool MatchesAscii(
            ReadOnlySpan<byte> value,
            string expected)
        {
            if (value.Length !=
                expected.Length)
            {
                return false;
            }

            for (var i = 0;
                 i < value.Length;
                 i++)
            {
                if (value[i] !=
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

            return cursor <=
                end;
        }

        private static bool TryReadPaddedStringRange(
            byte[] data,
            int end,
            ref int cursor,
            bool validateUtf8,
            out int start,
            out int length)
        {
            start = 0;
            length = 0;

            if (cursor >= end)
            {
                return false;
            }

            start =
                cursor;

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

            if (validateUtf8)
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

            return cursor <=
                end;
        }

        private static bool TryReadInt32(
            byte[] data,
            int end,
            ref int cursor,
            out int value)
        {
            value = 0;

            if (cursor + 4 >
                end)
            {
                return false;
            }

            value =
                (data[cursor + 0] << 24) |
                (data[cursor + 1] << 16) |
                (data[cursor + 2] << 8) |
                data[cursor + 3];

            cursor +=
                4;
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
