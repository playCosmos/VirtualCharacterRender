using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Application;
using VCR.Runtime.Character;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;
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
            GameObject applicationRoot = null;

            var originalRunInBackground =
                Application.runInBackground;
            var originalVSync =
                QualitySettings.vSyncCount;
            var originalTargetFrameRate =
                Application.targetFrameRate;


            string configurationTestDirectory = null;

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

                var environmentRoot =
                    new GameObject("Environment");
                environmentRoot.transform.SetParent(
                    root.transform,
                    false);

                var environment =
                    environmentRoot.AddComponent<
                        BasicEnvironmentRuntime>();
                environment.Configure(
                    "environment.p1.validation",
                    "default",
                    EnvironmentUpdatePolicy.Static,
                    EnvironmentSpaceMode.World);

                var outputAdapter =
                    root.AddComponent<
                        P1TestOverlayOutputAdapter>();

                var launchOptions =
                    ApplicationLaunchOptions.Parse(
                        new[]
                        {
                            "player",
                            "--vcr-config=/tmp/vcr-config.json",
                            "--vcr-vrm",
                            "/tmp/avatar.vrm",
                            "--flag"
                        });

                Expect(
                    launchOptions.GetOrDefault(
                        "vcr-config") ==
                    "/tmp/vcr-config.json" &&
                    launchOptions.GetOrDefault(
                        "vcr-vrm") ==
                    "/tmp/avatar.vrm" &&
                    launchOptions.TryGet(
                        "flag",
                        out var flagValue) &&
                    flagValue == string.Empty,
                    "application launch options must support equals, spaced values, and flag options",
                    failures);

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

                var capabilityCreateCount = 0;
                var capabilityDisposeCount = 0;
                var capabilityThrowingDisposeCount = 0;
                var capabilityAfterThrowDisposeCount = 0;
                var capabilities =
                    scene.Capabilities;

                Expect(
                    capabilities != null,
                    "scene runtime must create a capability registry",
                    failures);

                if (capabilities != null)
                {
                    Expect(
                        capabilities.Register(
                            "p1.validation",
                            () =>
                            {
                                capabilityCreateCount++;
                                return new ProbeDisposable(
                                    () =>
                                        capabilityDisposeCount++);
                            }),
                        "capability registration must succeed",
                        failures);

                    Expect(
                        capabilityCreateCount == 0,
                        "capability registration must remain lazy",
                        failures);

                    Expect(
                        capabilities.Enable(
                            "p1.validation",
                            out var capabilityError) &&
                        string.IsNullOrEmpty(
                            capabilityError) &&
                        capabilityCreateCount == 1,
                        "capability enable must instantiate exactly once",
                        failures);

                    Expect(
                        capabilities.Register(
                            "p1.validation.throwing-dispose",
                            () =>
                                new ProbeDisposable(
                                    () =>
                                    {
                                        capabilityThrowingDisposeCount++;
                                        throw new InvalidOperationException(
                                            "expected validation dispose failure");
                                    })) &&
                        capabilities.Register(
                            "p1.validation.after-throw",
                            () =>
                                new ProbeDisposable(
                                    () =>
                                        capabilityAfterThrowDisposeCount++)),
                        "capability registry must accept disposal-isolation validation entries",
                        failures);

                    Expect(
                        capabilities.Enable(
                            "p1.validation.throwing-dispose",
                            out var throwingCapabilityError) &&
                        string.IsNullOrEmpty(
                            throwingCapabilityError) &&
                        capabilities.Enable(
                            "p1.validation.after-throw",
                            out var afterThrowCapabilityError) &&
                        string.IsNullOrEmpty(
                            afterThrowCapabilityError),
                        "disposal-isolation validation capabilities must enable successfully",
                        failures);
                }

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

                Expect(
                    ReferenceEquals(
                        scene.EnvironmentRuntime,
                        environment),
                    "scene runtime must resolve the environment runtime",
                    failures);

                var configuration =
                    SceneRuntimeConfiguration.Default;
                configuration.Rendering.ResolutionPreset =
                    RenderResolutionPreset.Custom;
                configuration.Rendering.Width = 800;
                configuration.Rendering.Height = 600;
                configuration.Rendering.RenderScale = 0.75f;
                configuration.Rendering.TargetFrameRate = 75;
                configuration.Rendering.UseVSync = true;
                configuration.Rendering.RunInBackground = false;
                configuration.Camera =
                    new SceneCameraSettings
                    {
                        LocalPosition =
                            new Vector3(0.2f, 1.5f, -2.5f),
                        LocalEulerAngles =
                            new Vector3(5f, 25f, 0f),
                        FieldOfView = 50f,
                        NearClipPlane = 0.1f,
                        FarClipPlane = 200f
                    };
                configuration.Light =
                    SceneLightSettings.DefaultDirectional;
                configuration.Light.Intensity = 0.6f;
                configuration.Overlay =
                    new OverlayOutputConfiguration
                    {
                        Transparent = true,
                        Topmost = true,
                        ClickThrough = false
                    };
                configuration.EnvironmentStateId =
                    "configured";

                scene.ApplyConfiguration(
                    configuration);

                var captured =
                    scene.CaptureConfiguration();

                Expect(
                    captured.Rendering.ResolutionPreset ==
                    RenderResolutionPreset.Custom &&
                    captured.Rendering.Width == 800 &&
                    captured.Rendering.Height == 600 &&
                    Math.Abs(
                        captured.Rendering.RenderScale -
                        0.75f) <
                    0.0001f &&
                    captured.Rendering.TargetFrameRate == 75 &&
                    captured.Rendering.UseVSync &&
                    !captured.Rendering.RunInBackground,
                    "scene configuration must round-trip rendering settings",
                    failures);

                Expect(
                    Math.Abs(
                        captured.Camera.FieldOfView -
                        50f) <
                    0.0001f &&
                    Approximately(
                        captured.Camera.LocalPosition,
                        configuration.Camera.LocalPosition),
                    "scene configuration must round-trip camera settings",
                    failures);

                Expect(
                    Math.Abs(
                        captured.Light.Intensity -
                        0.6f) <
                    0.0001f,
                    "scene configuration must round-trip light settings",
                    failures);

                Expect(
                    captured.EnvironmentStateId ==
                    "configured" &&
                    environment.Status.StateId ==
                    "configured",
                    "scene configuration must apply and capture environment state",
                    failures);

                Expect(
                    outputAdapter.ApplyCount == 1 &&
                    outputAdapter.LastSettings.Transparent &&
                    outputAdapter.LastSettings.Topmost &&
                    !outputAdapter.LastSettings.ClickThrough,
                    "scene configuration must apply overlay settings through the adapter",
                    failures);

                Expect(
                    scene.SetEnvironmentState(
                        "reactive",
                        out var environmentError) &&
                    string.IsNullOrEmpty(
                        environmentError) &&
                    scene.CaptureConfiguration()
                        .EnvironmentStateId ==
                    "reactive",
                    "direct environment state changes must flow through the scene runtime and snapshot",
                    failures);

                var outputSettings =
                    new OverlayOutputSettings(
                        transparent: true,
                        topmost: false,
                        clickThrough: true);

                scene.ApplyOverlayOutput(
                    outputSettings);

                var overlayCapture =
                    scene.CaptureConfiguration().Overlay;

                Expect(
                    outputAdapter.ApplyCount == 2 &&
                    outputAdapter.LastSettings.Transparent &&
                    !outputAdapter.LastSettings.Topmost &&
                    outputAdapter.LastSettings.ClickThrough &&
                    overlayCapture.Transparent &&
                    !overlayCapture.Topmost &&
                    overlayCapture.ClickThrough,
                    "individual overlay changes must update both adapter and configuration snapshot",
                    failures);

                configurationTestDirectory =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-p1-validation-" +
                        Guid.NewGuid().ToString("N"));

                var configurationPath =
                    Path.Combine(
                        configurationTestDirectory,
                        "runtime-config.json");

                var configurationStore =
                    new RuntimeConfigurationStore(
                        configurationPath);

                Expect(
                    configurationStore.TrySave(
                        scene.CaptureConfiguration(),
                        out var configurationSaveError) &&
                    string.IsNullOrEmpty(
                        configurationSaveError) &&
                    File.Exists(
                        configurationPath),
                    "configuration store must persist the scene snapshot",
                    failures);

                Expect(
                    configurationStore.TryLoad(
                        out var loadedConfiguration,
                        out var configurationLoadError) &&
                    string.IsNullOrEmpty(
                        configurationLoadError) &&
                    loadedConfiguration.Rendering.Width ==
                        captured.Rendering.Width &&
                    loadedConfiguration.Rendering.Height ==
                        captured.Rendering.Height &&
                    loadedConfiguration.EnvironmentStateId ==
                        "reactive" &&
                    loadedConfiguration.Overlay.ClickThrough,
                    "configuration store must round-trip the current version",
                    failures);

                var futureEnvelope =
                    new RuntimeConfigurationEnvelope
                    {
                        Version =
                            RuntimeConfigurationStore.CurrentVersion +
                            1,
                        Scene =
                            SceneRuntimeConfiguration.Default
                    };

                File.WriteAllText(
                    configurationPath,
                    JsonUtility.ToJson(
                        futureEnvelope,
                        prettyPrint: true));

                Expect(
                    !configurationStore.TryLoad(
                        out _,
                        out var futureVersionError) &&
                    !string.IsNullOrWhiteSpace(
                        futureVersionError),
                    "configuration store must reject unsupported future versions",
                    failures);

                Expect(
                    scene.Suspend() &&
                    scene.State ==
                    SceneRuntimeState.Suspended &&
                    outputAdapter.ShutdownCount == 1 &&
                    capabilityDisposeCount == 0 &&
                    capabilities != null &&
                    capabilities.EnabledCount == 3,
                    "suspend must stop overlay output without disposing active capabilities",
                    failures);

                Expect(
                    scene.Resume() &&
                    scene.State ==
                    SceneRuntimeState.Ready &&
                    outputAdapter.ApplyCount == 3 &&
                    capabilityDisposeCount == 0,
                    "resume must restore scene presentation without recreating capabilities",
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
                ExpectNoUpdate<
                    BasicEnvironmentRuntime>(
                    "basic environment runtime",
                    failures);

                scene.Shutdown();

                Expect(
                    scene.State ==
                    SceneRuntimeState.Stopped,
                    "scene shutdown must end in Stopped state",
                    failures);

                Expect(
                    outputAdapter.ShutdownCount == 2,
                    "scene shutdown must shut down the overlay output adapter after the earlier suspend cycle",
                    failures);

                Expect(
                    capabilityDisposeCount == 1 &&
                    capabilityThrowingDisposeCount == 1 &&
                    capabilityAfterThrowDisposeCount == 1 &&
                    scene.Capabilities == null &&
                    (capabilities == null ||
                     capabilities.RegisteredCount == 0),
                    "scene shutdown must isolate capability disposal failures, dispose remaining capabilities, and release the registry",
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


                applicationRoot =
                    new GameObject(
                        "VCR P1 Application Bootstrap Validation");

                var applicationCharacter =
                    new GameObject("Character");
                applicationCharacter.transform.SetParent(
                    applicationRoot.transform,
                    false);
                applicationCharacter.AddComponent<
                    Vrm10CharacterLoader>();

                applicationRoot.AddComponent<
                    DesktopRenderBootstrap>();

                var applicationScene =
                    applicationRoot.AddComponent<
                        SingleCharacterSceneRuntime>();

                var applicationBootstrap =
                    applicationRoot.AddComponent<
                        ApplicationRuntimeBootstrap>();

                var applicationConfiguration =
                    SceneRuntimeConfiguration.Default;
                applicationConfiguration.Rendering.ResolutionPreset =
                    RenderResolutionPreset.Custom;
                applicationConfiguration.Rendering.Width = 960;
                applicationConfiguration.Rendering.Height = 540;

                var applicationConfigurationPath =
                    Path.Combine(
                        configurationTestDirectory,
                        "application-config.json");

                var applicationStore =
                    new RuntimeConfigurationStore(
                        applicationConfigurationPath);

                Expect(
                    applicationStore.TrySave(
                        applicationConfiguration,
                        out var applicationSeedError) &&
                    string.IsNullOrEmpty(
                        applicationSeedError),
                    "application bootstrap validation config must be seeded",
                    failures);

                var applicationOptions =
                    ApplicationLaunchOptions.Parse(
                        new[]
                        {
                            "--vcr-config=" +
                            applicationConfigurationPath
                        });

                var applicationStarted =
                    applicationBootstrap
                        .StartRuntimeAsync(
                            applicationOptions)
                        .GetAwaiter()
                        .GetResult();

                Expect(
                    applicationStarted &&
                    applicationBootstrap.IsStarted &&
                    applicationBootstrap.ConfigurationPath ==
                        Path.GetFullPath(
                            applicationConfigurationPath) &&
                    applicationScene.CaptureConfiguration()
                        .Rendering.Width ==
                        960 &&
                    applicationScene.CaptureConfiguration()
                        .Rendering.Height ==
                        540,
                    "application bootstrap must load persisted configuration before normal runtime use",
                    failures);

                applicationBootstrap.Suspend();

                Expect(
                    applicationScene.State ==
                    SceneRuntimeState.Suspended,
                    "application bootstrap suspend must suspend the scene runtime",
                    failures);

                applicationBootstrap.Resume();

                Expect(
                    applicationScene.State ==
                    SceneRuntimeState.Ready,
                    "application bootstrap resume must restore the scene runtime",
                    failures);

                Expect(
                    applicationBootstrap.SaveConfiguration(
                        out var applicationSaveError) &&
                    string.IsNullOrEmpty(
                        applicationSaveError),
                    "application bootstrap must save the current scene configuration",
                    failures);

                Expect(
                    applicationBootstrap.Shutdown(
                        saveConfiguration: false,
                        out var applicationShutdownError) &&
                    string.IsNullOrEmpty(
                        applicationShutdownError) &&
                    !applicationBootstrap.IsStarted &&
                    applicationScene.State ==
                        SceneRuntimeState.Stopped,
                    "application bootstrap explicit shutdown must stop the scene runtime",
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

                if (applicationRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        applicationRoot);
                }

                if (!string.IsNullOrWhiteSpace(
                        configurationTestDirectory) &&
                    Directory.Exists(
                        configurationTestDirectory))
                {
                    Directory.Delete(
                        configurationTestDirectory,
                        recursive: true);
                }
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P1 renderer core validation: PASS " +
                    "(graphics clamps, scene lifecycle, camera/light adapters, overlay lifecycle, environment hook, capability lifecycle, application bootstrap, suspend/resume, configuration persistence/versioning, runtime restore, no per-frame coordinator loops)");
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

        private sealed class ProbeDisposable :
            IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public ProbeDisposable(
                Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _onDispose?.Invoke();
            }
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
