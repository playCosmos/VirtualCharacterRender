using System;

namespace VCR.Runtime.Rendering
{
    [Serializable]
    public struct RenderRuntimeSettings
    {
        public RenderResolutionPreset ResolutionPreset;
        public int Width;
        public int Height;
        public float RenderScale;
        public int TargetFrameRate;
        public bool UseVSync;
        public bool RunInBackground;

        public static RenderRuntimeSettings Default1080p =>
            new()
            {
                ResolutionPreset =
                    RenderResolutionPreset.Recommended1080p,
                Width = 1920,
                Height = 1080,
                RenderScale = 1f,
                TargetFrameRate = 60,
                UseVSync = false,
                RunInBackground = true
            };
    }
}
