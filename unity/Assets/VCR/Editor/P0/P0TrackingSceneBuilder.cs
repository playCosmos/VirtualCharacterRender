using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Events.Unity;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;
using VCR.Runtime.Output.Unity;
using VCR.Runtime.P0;
using VCR.Runtime.Rendering;
using VCR.Runtime.Scene;
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
            camera.allowHDR = false;

            var environmentRoot =
                new GameObject("Environment");
            environmentRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var environment =
                environmentRoot.AddComponent<
                    BasicEnvironmentRuntime>();
            environment.Configure(
                "environment.p0.basic",
                "default",
                EnvironmentUpdatePolicy.Static,
                EnvironmentSpaceMode.World);

            var lightObject =
                new GameObject("Main Directional Light");
            lightObject.transform.SetParent(
                environmentRoot.transform,
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

            var eventRoot =
                new GameObject("Events");
            eventRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var eventHub =
                eventRoot.AddComponent<
                    NormalizedEventHub>();

            var presenceEvents =
                eventRoot.AddComponent<
                    TrackingPresenceEventAdapter>();
            presenceEvents.SetProvider(router);
            presenceEvents.SetSink(eventHub);

            var characterRoot =
                new GameObject("Character");
            characterRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var loader =
                characterRoot.AddComponent<
                    Vrm10CharacterLoader>();
            loader.SetTrackingProvider(router);

            var standaloneBootstrap =
                runtimeRoot.AddComponent<
                    P0StandaloneBootstrap>();
            standaloneBootstrap.Configure(loader);

            var renderBootstrap =
                runtimeRoot.AddComponent<
                    DesktopRenderBootstrap>();

            runtimeRoot.AddComponent<
                SingleCharacterSceneRuntime>();

            var output =
                runtimeRoot.AddComponent<
                    UniWinCOverlayOutput>();
            output.ConfigureForP0(camera);

            var alphaPattern =
                runtimeRoot.AddComponent<
                    P0AlphaTestPattern>();
            alphaPattern.Configure(
                P0TransparentOutputMenu
                    .EnsureAlphaTestMaterialAsset());

            var diagnostics =
                runtimeRoot.AddComponent<
                    P0RuntimeDiagnostics>();
            diagnostics.SetTrackingProvider(router);

            P0TransparentOutputMenu.ConfigureProjectBaseline();

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
                "Transparent-output project settings were applied automatically. " +
                "Run 'VCR > P0 > Validate Transparent Output Baseline', then enter Play mode. " +
                "Use 'VCR > P0 > Load VRM Into Runtime Scene' and inspect the alpha pattern plus five-second diagnostics.");
        }
    }
}
