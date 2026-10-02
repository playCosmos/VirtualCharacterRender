using UnityEditor;
using VCR.Editor.P0;

namespace VCR.Editor.P1
{
    public static class P1ApplicationSceneBuilder
    {
        public const string ScenePath =
            "Assets/VCR/P1/P1Runtime.unity";

        [MenuItem("VCR/P1/Create Application Runtime Scene")]
        public static void CreateApplicationRuntimeScene()
        {
            P0TrackingSceneBuilder.CreateRuntimeScene(
                ScenePath,
                "VCR P1 Runtime",
                "environment.p1.basic",
                "P1");
        }
    }
}
