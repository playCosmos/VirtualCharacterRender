using System;
using VCR.Runtime.Rendering;

namespace VCR.Runtime.Scene
{
    [Serializable]
    public struct SceneRuntimeConfiguration
    {
        public RenderRuntimeSettings Rendering;
        public SceneCameraSettings Camera;
        public SceneLightSettings Light;
        public OverlayOutputConfiguration Overlay;
        public string EnvironmentStateId;

        public static SceneRuntimeConfiguration Default =>
            new()
            {
                Rendering =
                    RenderRuntimeSettings.Default1080p,
                Camera =
                    SceneCameraSettings.Default,
                Light =
                    SceneLightSettings.DefaultDirectional,
                Overlay =
                    OverlayOutputConfiguration.Default,
                EnvironmentStateId =
                    "default"
            };
    }
}
