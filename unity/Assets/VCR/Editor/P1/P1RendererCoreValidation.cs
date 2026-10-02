using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Output;
using VCR.Runtime.Rendering;
using VCR.Runtime.Scene;

namespace VCR.Editor.P1
{
    public static class P1RendererCoreValidation
    {
        [MenuItem("VCR/P1/Validate Renderer Core")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures = new List<string>();
            GameObject root = null;

            var originalRunInBackground =
                Application.runInBackground;
            var originalVSync =
                QualitySettings.vSyncCount;
            var originalTargetFrameRate =
                Application.targetFrameRate;

            try
            {
                root = new GameObject(
                    "VCR P1 Renderer Core Validation");

                var characterRoot =
                    new GameObject("Character");
                characterRoot.transform.SetParent(
                    root.transform,
                    false);
                characterRoot.AddComponent<
                    Vrm10CharacterLoader>();

                var cameraObject =
                    new GameObject("Main Camera");
                cameraObject.transform.SetParent(
                    root.transform,
                    false);
                cameraObject.tag = "MainCamera";
                cameraObject.transform.localPosition =
                    new Vector3(2f, 3f, -5f);
                cameraObject.transform.localRotation =
                    Quaternion.Euler(10f, 20f, 30f);

                var camera =
                    cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 500f;
                camera.clearFlags =
                    CameraClearFlags.Skybox;
                camera.backgroundColor =
                    new Color(0.2f, 0.3f, 0.4f, 1f);
                camera.allowHDR = true;
                camera.allowMSAA = false;

                var originalCameraPosition =
                    camera.transform.localPosition;
                var originalCameraRotation =
                    camera.transform.localRotation;
                var originalCameraFov =
                    camera.fieldOfView;
                var originalCameraNear =
                    camera.nearClipPlane;
                var originalCameraFar =
                    camera.farClipPlane;
                var originalCameraClearFlags =
                    camera.clearFlags;
                var originalCameraBackground =
                    camera.backgroundColor;
                var originalCameraHdr =
                    camera.allowHDR;
                var originalCameraMsaa =
                    camera.allowMSAA;

                var cameraController =
                    cameraObject.AddComponent<
                        PrimaryCameraController>();

                cameraController.Configure(
                    camera,
                    new SceneCameraSettings
                    {
                        LocalPosition =
                            new Vector3(0f, 1f, -2f),
                        LocalEulerAngles =
                            new Vector3(0f, 15f, 0f),
                        FieldOfView = 200f,
                        NearClipPlane = -1f,
                        FarClipPlane = 0f
                    });

                Expect(
                    Math.Abs(
                        cameraController.Settings.FieldOfView -
                        179f) <
                    0.0001f,
                    "camera FOV must clamp to 179 degrees maximum",
                    failures);

                Expect(
                    cameraController.Settings.NearClipPlane >=
                    0.001f,
                    "camera near clip must be positive",
                    failures);

                Expect(
                    cameraController.Settings.FarClipPlane >
                    cameraController.Settings.NearClipPlane,
                    "camera far clip must remain beyond near clip",
                    failures);

                var lightObject =
                    new GameObject(
                        "Main Directional Light");
                lightObject.transform.SetParent(
                    root.transform,
                    false);
                lightObject.transform.localRotation =
                    Quaternion.Euler(5f, 10f, 15f);

                var light =
                    lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.enabled = false;
                light.color = Color.red;
                light.intensity = 2.5f;
                light.shadows = LightShadows.Hard;

                var originalLightEnabled =
                    light.enabled;
                var originalLightRotation =
                    light.transform.localRotation;
                var originalLightColor =
                    light.color;
                var originalLightIntensity =
                    light.intensity;
                var originalLightShadows =
                    light.shadows;

                var lightController =
                    lightObject.AddComponent<
                        PrimaryLightController>();

                var lightSettings =
                    SceneLightSettings.DefaultDirectional;
                lightSettings.Intensity = -3f;
                lightController.Configure(
                    light,
                    lightSettings);

                Expect(
                    Math.Abs(
                        lightController.Settings.Intensity) <
                    0.0001f,
                    "light intensity must clamp to zero minimum",
                    failures);

                var render =
                    root.AddComponent<
                        DesktopRenderBootstrap>();

                render.SetCustomResolution(100, 100);
                Expect(
                    render.RequestedWidth == 320 &&
                    render.RequestedHeight == 240,
                    "custom resolution must clamp to 320x240 minimum",
                    failures);

                render.SetRenderScale(0.1f);
                Expect(
                    Math.Abs(
                        render.RenderScale - 0.5f) <
                    0.0001f,
                    "render scale must clamp to 0.5 minimum",
                    failures);

                render.SetRenderScale(3.0f);
                Expect(
                    Math.Abs(
                        render.RenderScale - 2.0f) <
                    0.0001f,
                    "render scale must clamp to 2.0 maximum",
                    failures);

                render.SetFramePacing(
                    1,
                    vSync: false);
                Expect(
                    render.TargetFrameRate == 30,
                    "target frame rate must clamp to 30 FPS minimum",
                    failures);

                var outputAdapter =
                    root.AddComponent<
                        P1TestOverlayOutputAdapter>();

                var scene =
                    root.AddComponent<
                        SingleCharacterSceneRuntime>();

                Expect(
                    scene.Initialize(),
                    "scene runtime must initialize from local renderer/character dependencies",
                    failures);

                Expect(
                    scene.State ==
                    SceneRuntimeState.Ready,
                    "empty initialized scene must be Ready",
                    failures);

                Expect(
                    ReferenceEquals(
                        scene.CameraController,
                        cameraController),
                    "scene runtime must resolve the primary camera adapter",
                    failures);

                Expect(
                    ReferenceEquals(
                        scene.LightController,
                        lightController),
                    "scene runtime must resolve the primary light adapter",
                    failures);

                Expect(
                    ReferenceEquals(
                        scene.OverlayOutput,
                        outputAdapter),
                    "scene runtime must resolve the overlay output adapter",
                    failures);

                var outputSettings =
                    new OverlayOutputSettings(
                        transparent: true,
                        topmost: false,
                        clickThrough: true);

                scene.ApplyOverlayOutput(
                    outputSettings);

                Expect(
                    outputAdapter.ApplyCount == 1 &&
                    outputAdapter.LastSettings.Transparent &&
                    !outputAdapter.LastSettings.Topmost &&
                    outputAdapter.LastSettings.ClickThrough,
                    "scene runtime must route overlay output settings through the adapter",
                    failures);

                scene.UnloadCharacter();

                Expect(
                    scene.State ==
                    SceneRuntimeState.Ready,
                    "unloading an empty scene must remain Ready",
                    failures);

                ExpectNoUpdate<
                    SingleCharacterSceneRuntime>(
                    "scene runtime",
                    failures);
                ExpectNoUpdate<
                    DesktopRenderBootstrap>(
                    "render bootstrap",
                    failures);
                ExpectNoUpdate<
                    PrimaryCameraController>(
                    "camera controller",
                    failures);
                ExpectNoUpdate<
                    PrimaryLightController>(
                    "light controller",
                    failures);

                scene.Shutdown();

                Expect(
                    scene.State ==
                    SceneRuntimeState.Stopped,
                    "scene shutdown must end in Stopped state",
                    failures);

                Expect(
                    outputAdapter.ShutdownCount == 1,
                    "scene shutdown must shut down the overlay output adapter exactly once",
                    failures);

                Expect(
                    Application.runInBackground ==
                    originalRunInBackground,
                    "shutdown must restore Application.runInBackground",
                    failures);

                Expect(
                    QualitySettings.vSyncCount ==
                    originalVSync,
                    "shutdown must restore vSyncCount",
                    failures);

                Expect(
                    Application.targetFrameRate ==
                    originalTargetFrameRate,
                    "shutdown must restore targetFrameRate",
                    failures);

                Expect(
                    Approximately(
                        camera.transform.localPosition,
                        originalCameraPosition),
                    "shutdown must restore camera local position",
                    failures);

                Expect(
                    Approximately(
                        camera.transform.localRotation,
                        originalCameraRotation),
                    "shutdown must restore camera local rotation",
                    failures);

                Expect(
                    Math.Abs(
                        camera.fieldOfView -
                        originalCameraFov) <
                    0.0001f &&
                    Math.Abs(
                        camera.nearClipPlane -
                        originalCameraNear) <
                    0.0001f &&
                    Math.Abs(
                        camera.farClipPlane -
                        originalCameraFar) <
                    0.0001f,
                    "shutdown must restore camera projection",
                    failures);

                Expect(
                    camera.clearFlags ==
                    originalCameraClearFlags &&
                    Approximately(
                        camera.backgroundColor,
                        originalCameraBackground) &&
                    camera.allowHDR ==
                    originalCameraHdr &&
                    camera.allowMSAA ==
                    originalCameraMsaa,
                    "shutdown must restore camera render-surface settings",
                    failures);

                Expect(
                    light.enabled ==
                    originalLightEnabled &&
                    Approximately(
                        light.transform.localRotation,
                        originalLightRotation) &&
                    Approximately(
                        light.color,
                        originalLightColor) &&
                    Math.Abs(
                        light.intensity -
                        originalLightIntensity) <
                    0.0001f &&
                    light.shadows ==
                    originalLightShadows,
                    "shutdown must restore primary light settings",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                Application.runInBackground =
                    originalRunInBackground;
                QualitySettings.vSyncCount =
                    originalVSync;
                Application.targetFrameRate =
                    originalTargetFrameRate;

                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P1 renderer core validation: PASS " +
                    "(graphics clamps, scene lifecycle, camera/light adapters, overlay lifecycle, runtime restore, no per-frame coordinator loops)");
                return true;
            }

            Debug.LogError(
                "VCR P1 renderer core validation: FAIL\n" +
                string.Join("\n", failures));
            return false;
        }

        private static void ExpectNoUpdate<T>(
            string name,
            List<string> failures)
        {
            Expect(
                !DeclaresPerFrameUpdate(
                    typeof(T)),
                name +
                " must not add a per-frame Update loop",
                failures);
        }

        private static bool DeclaresPerFrameUpdate(
            Type type)
        {
            return type.GetMethod(
                       "Update",
                       BindingFlags.Instance |
                       BindingFlags.Public |
                       BindingFlags.NonPublic |
                       BindingFlags.DeclaredOnly) !=
                   null;
        }

        private static bool Approximately(
            Vector3 a,
            Vector3 b)
        {
            return
                (a - b).sqrMagnitude <
                0.000001f;
        }

        private static bool Approximately(
            Quaternion a,
            Quaternion b)
        {
            return
                Math.Abs(
                    Quaternion.Dot(a, b)) >
                0.999999f;
        }

        private static bool Approximately(
            Color a,
            Color b)
        {
            return
                Math.Abs(a.r - b.r) < 0.0001f &&
                Math.Abs(a.g - b.g) < 0.0001f &&
                Math.Abs(a.b - b.b) < 0.0001f &&
                Math.Abs(a.a - b.a) < 0.0001f;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }
}
