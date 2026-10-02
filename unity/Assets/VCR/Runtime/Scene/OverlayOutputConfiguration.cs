using System;
using VCR.Runtime.Output;

namespace VCR.Runtime.Scene
{
    [Serializable]
    public struct OverlayOutputConfiguration
    {
        public bool Transparent;
        public bool Topmost;
        public bool ClickThrough;

        public OverlayOutputSettings ToSettings()
        {
            return new OverlayOutputSettings(
                Transparent,
                Topmost,
                ClickThrough);
        }

        public static OverlayOutputConfiguration FromSettings(
            OverlayOutputSettings settings)
        {
            return new OverlayOutputConfiguration
            {
                Transparent = settings.Transparent,
                Topmost = settings.Topmost,
                ClickThrough = settings.ClickThrough
            };
        }

        public static OverlayOutputConfiguration Default =>
            new()
            {
                Transparent = true,
                Topmost = true,
                ClickThrough = false
            };
    }
}
