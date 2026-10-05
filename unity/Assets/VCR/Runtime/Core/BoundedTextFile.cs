using System;
using System.IO;
using System.Text;

namespace VCR.Runtime.Core
{
    public static class BoundedTextFile
    {
        private static readonly UTF8Encoding Utf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        public static bool TryReadUtf8(
            string path,
            long maxBytes,
            out string text,
            out string error)
        {
            text = null;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error =
                    "Text file path is required.";
                return false;
            }

            if (maxBytes <= 0)
            {
                error =
                    "Text file byte limit must be positive.";
                return false;
            }

            try
            {
                using var stream =
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);

                var length =
                    stream.Length;

                if (length >
                    maxBytes ||
                    length >
                    int.MaxValue)
                {
                    error =
                        $"Text file is too large ({length} bytes; limit {maxBytes} bytes).";
                    return false;
                }

                var bytes =
                    new byte[
                        checked((int)length)];
                var read = 0;

                while (read <
                       bytes.Length)
                {
                    var count =
                        stream.Read(
                            bytes,
                            read,
                            bytes.Length -
                            read);

                    if (count == 0)
                    {
                        error =
                            "Text file changed while it was being read.";
                        return false;
                    }

                    read +=
                        count;
                }

                if (stream.ReadByte() !=
                    -1)
                {
                    error =
                        "Text file changed while it was being read.";
                    return false;
                }

                var offset =
                    bytes.Length >= 3 &&
                    bytes[0] == 0xef &&
                    bytes[1] == 0xbb &&
                    bytes[2] == 0xbf
                        ? 3
                        : 0;

                text =
                    Utf8.GetString(
                        bytes,
                        offset,
                        bytes.Length -
                        offset);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Text file read failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryValidateUtf8Size(
            string text,
            long maxBytes,
            out string error)
        {
            error = null;

            if (text == null)
            {
                error =
                    "Text content is required.";
                return false;
            }

            if (maxBytes <= 0)
            {
                error =
                    "Text byte limit must be positive.";
                return false;
            }

            try
            {
                var byteCount =
                    Utf8.GetByteCount(
                        text);

                if (byteCount >
                    maxBytes)
                {
                    error =
                        $"Text content is too large ({byteCount} UTF-8 bytes; limit {maxBytes} bytes).";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Text size validation failed: " +
                    exception.Message;
                return false;
            }
        }
    }
}
