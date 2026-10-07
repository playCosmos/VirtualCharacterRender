using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Runtime.Application;
using VCR.Runtime.Character;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Events.Unity;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;
using VCR.Runtime.Output.Unity;
using VCR.Runtime.Rendering;
using VCR.Runtime.Scene;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Runtime.Tracking.Routing;

namespace VCR.Editor.P0
{
    public static class P0TrackingSceneBuilder
    {
        public const string P0ScenePath =
            "Assets/VCR/P0/P0Runtime.unity";

        private const string Vrm10MToonShaderPath =
            "Packages/com.vrmc.vrm/MToon10/Shaders/vrmc_materials_mtoon_urp.shader";
        private const string UrpLitShaderPath =
            "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";
        private const string UniUnlitShaderPath =
            "Packages/com.vrmc.gltf/UniUnlit/Shaders/UniUnlit.shader";

        [MenuItem("VCR/P0/Create Runtime Test Scene")]
        public static void CreateRuntimeTestScene()
        {
            CreateRuntimeScene(
                P0ScenePath,
                "VCR P0 Runtime",
                "environment.p0.basic",
                "P0");
        }

        public static bool CreateRuntimeScene(
            string scenePath,
            string rootName,
            string environmentId,
            string logContext)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                Debug.LogError(
                    $"VCR {logContext}: scene path is required.");
                return false;
            }

            var sceneDirectory =
                Path.GetDirectoryName(scenePath);

            if (string.IsNullOrWhiteSpace(sceneDirectory))
            {
                Debug.LogError(
                    $"VCR {logContext}: invalid scene path '{scenePath}'.");
                return false;
            }

            Directory.CreateDirectory(sceneDirectory);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var runtimeRoot =
                new GameObject(
                    string.IsNullOrWhiteSpace(rootName)
                        ? "VCR Runtime"
                        : rootName);

            var cameraObject =
                new GameObject("Main Camera");
            cameraObject.transform.SetParent(
                runtimeRoot.transform,
                false);
            cameraObject.tag = "MainCamera";

            var camera =
                cameraObject.AddComponent<Camera>();
            camera.clearFlags =
                CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;

            var cameraController =
                cameraObject.AddComponent<
                    PrimaryCameraController>();
            cameraController.Configure(
                camera,
                SceneCameraSettings.Default);

            var environmentRoot =
                new GameObject("Environment");
            environmentRoot.transform.SetParent(
                runtimeRoot.transform,
                false);

            var environment =
                environmentRoot.AddComponent<
                    BasicEnvironmentRuntime>();
            environment.Configure(
                string.IsNullOrWhiteSpace(environmentId)
                    ? "environment.basic"
                    : environmentId,
                "default",
                EnvironmentUpdatePolicy.Static,
                EnvironmentSpaceMode.World);

            var lightObject =
                new GameObject("Main Directional Light");
            lightObject.transform.SetParent(
                environmentRoot.transform,
                false);

            var light =
                lightObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var lightController =
                lightObject.AddComponent<
                    PrimaryLightController>();
            lightController.Configure(
                light,
                SceneLightSettings.DefaultDirectional);

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

            var vrm10MToonShader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    Vrm10MToonShaderPath);
            var urpLitShader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    UrpLitShaderPath);
            var uniUnlitShader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    UniUnlitShaderPath);

            if (vrm10MToonShader == null ||
                urpLitShader == null ||
                uniUnlitShader == null)
            {
                Object.DestroyImmediate(runtimeRoot);
                Debug.LogError(
                    $"VCR {logContext}: required VRM runtime shaders could not be resolved. " +
                    $"MToon={(vrm10MToonShader != null)}, " +
                    $"URP Lit={(urpLitShader != null)}, " +
                    $"UniUnlit={(uniUnlitShader != null)}. " +
                    "Check the pinned UniVRM/URP package contents before building.");
                return false;
            }

            loader.ConfigureRuntimeImportShaders(
                vrm10MToonShader,
                urpLitShader,
                uniUnlitShader);
            loader.SetTrackingProvider(router);

            var renderBootstrap =
                runtimeRoot.AddComponent<
                    DesktopRenderBootstrap>();

            runtimeRoot.AddComponent<
                SingleCharacterSceneRuntime>();

            runtimeRoot.AddComponent<
                ApplicationRuntimeBootstrap>();

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
                    RuntimeDiagnostics>();
            diagnostics.SetTrackingProvider(router);

            P0TransparentOutputMenu.ConfigureProjectBaseline();

            if (!EditorSceneManager.SaveScene(
                    scene,
                    scenePath))
            {
                Object.DestroyImmediate(runtimeRoot);
                Debug.LogError(
                    $"VCR {logContext}: failed to save runtime scene at {scenePath}");
                return false;
            }

            Selection.activeGameObject =
                runtimeRoot;
            EditorGUIUtility.PingObject(runtimeRoot);

            Debug.Log(
                $"VCR {logContext} runtime scene created: {scenePath}. " +
                "Transparent-output project settings were applied automatically.");

            return true;
        }
    }
}
