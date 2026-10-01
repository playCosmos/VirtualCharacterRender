namespace VCR.Runtime.Output
{
    public readonly struct OutputWindowRequest
    {
        public OutputWindowRequest(
            OutputWindowMode mode,
            bool clickThrough,
            bool alwaysOnTop)
        {
            Mode = mode;
            ClickThrough = clickThrough;
            AlwaysOnTop = alwaysOnTop;
        }

        public OutputWindowMode Mode { get; }
        public bool ClickThrough { get; }
        public bool AlwaysOnTop { get; }
    }
}
