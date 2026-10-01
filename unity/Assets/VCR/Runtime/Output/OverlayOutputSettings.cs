namespace VCR.Runtime.Output
{
    public readonly struct OverlayOutputSettings
    {
        public OverlayOutputSettings(
            bool transparent,
            bool topmost,
            bool clickThrough,
            int width,
            int height)
        {
            Transparent = transparent;
            Topmost = topmost;
            ClickThrough = clickThrough;
            Width = width;
            Height = height;
        }

        public bool Transparent { get; }
        public bool Topmost { get; }
        public bool ClickThrough { get; }
        public int Width { get; }
        public int Height { get; }
    }
}
