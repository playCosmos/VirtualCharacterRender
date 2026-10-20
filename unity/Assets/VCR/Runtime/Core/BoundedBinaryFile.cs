using System;
using System.IO;

namespace VCR.Runtime.Core
{
    public static class BoundedBinaryFile
    {
        public static bool TryRead(
            string path,
            long maxBytes,
            out byte[] bytes,
            out string error)
        {
            bytes = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                error =
                    "Binary file path is required.";
                return false;
            }

            if (maxBytes <= 0)
            {
                error =
                    "Binary file byte limit must be positive.";
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
                        $"Binary file is too large ({length} bytes; limit {maxBytes} bytes).";
                    return false;
                }

                bytes =
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
                        bytes = null;
                        error =
                            "Binary file changed while it was being read.";
                        return false;
                    }

                    read +=
                        count;
                }

                if (stream.ReadByte() !=
                    -1)
                {
                    bytes = null;
                    error =
                        "Binary file changed while it was being read.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                bytes = null;
                error =
                    "Binary file read failed: " +
                    exception.Message;
                return false;
            }
        }
    }
}
