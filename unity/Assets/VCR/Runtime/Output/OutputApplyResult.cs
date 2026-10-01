namespace VCR.Runtime.Output
{
    public readonly struct OutputApplyResult
    {
        public OutputApplyResult(
            bool supported,
            bool success,
            string message)
        {
            Supported = supported;
            Success = success;
            Message = message;
        }

        public bool Supported { get; }
        public bool Success { get; }
        public string Message { get; }

        public static OutputApplyResult Unsupported(string message) =>
            new(false, false, message);

        public static OutputApplyResult Failed(string message) =>
            new(true, false, message);

        public static OutputApplyResult Applied(string message = null) =>
            new(true, true, message);
    }
}
