namespace VCR.Runtime.Output
{
    public readonly struct OverlayOutputSettings
    {
        public OverlayOutputSettings(
            bool transparent,
            bool topmost,
            bool clickThrough)
        {
            Transparent = transparent;
            Topmost = topmost;
            ClickThrough = clickThrough;
        }

        public bool Transparent { get; }
        public bool Topmost { get; }
        public bool ClickThrough { get; }
    }
}
