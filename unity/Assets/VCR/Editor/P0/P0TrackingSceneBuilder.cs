using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Output.Unity;
using VCR.Runtime.Rendering;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Runtime.Tracking.Routing;

namespace VCR.Editor.P0
{
    public static class P0TrackingSceneBuilder
    {
        private const string SceneDirectory = "Assets/VCR/P0";
        private const string ScenePath =
            SceneDirectory + "/P0Runtime.unity";

        [MenuItem("VCR/P0/Create Runtime Test Scene")]
        public static void CreateRuntimeTestScene()
        {
            Directory.CreateDirectory(SceneDirectory);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var runtimeRoot =
                new GameObject("VCR P0 Runtime");

            var cameraObject =
                new GameObject("Main Camera");
            cameraObject.transform.SetParent(
                runtimeRoot.transform,
                false);
            cameraObject.transform.localPosition =
                new Vector3(0f, 1.35f, -3f);
            cameraObject.transform.localRotation =
                Quaternion.identity;
            cameraObject.tag = "MainCamera";

            var camera =
                cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags =
                CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0f, 0f, 0f, 0f);

            var lightObject =
                new GameObject("Main Directional Light");
            lightObject.transform.SetParent(
                runtimeRoot.transform,
                false);
            lightObject.transform.localRotation =
                Quaternion.Euler(45f, -30f, 0f);

            var light =
                lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            var trackingRoot =
                new GameObject("Tracking");
            trackingRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var mediaPipe =
                trackingRoot.AddComponent<
                    MediaPipeWebcamTrackingRunner>();

            var router =
                trackingRoot.AddComponent<
                    PriorityTrackingRouter>();
            router.SetFallbackProvider(mediaPipe);

            var characterRoot =
                new GameObject("Character");
            characterRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var loader =
                characterRoot.AddComponent<
                    Vrm10CharacterLoader>();
            loader.SetTrackingProvider(router);

            var renderBootstrap =
                runtimeRoot.AddComponent<
                    DesktopRenderBootstrap>();

            var output =
                runtimeRoot.AddComponent<
                    UniWinCOverlayOutput>();
            output.ConfigureForP0(camera);

            var alphaPattern =
                runtimeRoot.AddComponent<
                    P0AlphaTestPattern>();

            var diagnostics =
                runtimeRoot.AddComponent<
                    P0RuntimeDiagnostics>();
            diagnostics.SetTrackingProvider(router);

            if (!EditorSceneManager.SaveScene(
                scene,
                ScenePath))
            {
                Object.DestroyImmediate(runtimeRoot);
                Debug.LogError(
                    $"VCR P0: failed to save runtime test scene at {ScenePath}");
                return;
            }

            Selection.activeGameObject =
                runtimeRoot;
            EditorGUIUtility.PingObject(runtimeRoot);

            Debug.Log(
                $"VCR P0 runtime scene created: {ScenePath}. " +
                "Run 'VCR > P0 > Configure Transparent Output Baseline', then enter Play mode. " +
                "Use 'VCR > P0 > Load VRM Into Runtime Scene' and inspect the alpha pattern plus five-second diagnostics.");
        }
    }
}
