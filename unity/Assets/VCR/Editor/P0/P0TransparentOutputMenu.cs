using System.IO;
using System.Linq;
using Kirurobo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VCR.Runtime.Output.Unity;

namespace VCR.Editor.P0
{
    public static class P0TransparentOutputMenu
    {
        public const string AlphaTestMaterialPath =
            "Assets/VCR/P0/Generated/P0AlphaTestBase.mat";

        [MenuItem("VCR/P0/Configure Transparent Output Baseline")]
        public static void Configure()
        {
            var camera = FindOrCreateMainCamera();
            ConfigureCamera(camera);

            var output =
                Object.FindFirstObjectByType<UniWinCOverlayOutput>();

            if (output == null)
            {
                var root = new GameObject("P0 Output");
                Undo.RegisterCreatedObjectUndo(
                    root,
                    "Create P0 Output");

                // RequiredComponent adds UniWindowController.
                output =
                    Undo.AddComponent<UniWinCOverlayOutput>(root);
            }

            output.ConfigureForP0(camera);
            EditorUtility.SetDirty(output);

            var controller =
                output.GetComponent<UniWindowController>();
            if (controller != null)
            {
                controller.isHitTestEnabled = false;
                controller.hitTestType =
                    UniWindowController.HitTestType.None;
                controller.autoSwitchCameraBackground = false;
                controller.transparentType =
                    UniWindowController.TransparentType.Alpha;
                controller.SetCamera(camera);
                EditorUtility.SetDirty(controller);
            }

            ConfigureProjectBaseline();

            Selection.activeGameObject =
                output.gameObject;
            EditorGUIUtility.PingObject(output);

            Debug.Log(
                "VCR P0: transparent-output baseline configured. " +
                "Run 'Validate Transparent Output Baseline', then validate the built player on Windows and macOS.");
        }

        public static void ConfigureProjectBaseline()
        {
            ConfigurePlayerSettings();
            ConfigureUrpAlpha();
            EnsureAlphaTestMaterialAsset();
            AssetDatabase.SaveAssets();
        }

        public static Material EnsureAlphaTestMaterialAsset()
        {
            var shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                Debug.LogError(
                    "VCR P0: URP Unlit shader was not found; alpha-test material cannot be created.");
                return null;
            }

            var directory =
                Path.GetDirectoryName(
                    AlphaTestMaterialPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    AlphaTestMaterialPath);

            if (material == null)
            {
                material =
                    new Material(shader)
                    {
                        name = "P0AlphaTestBase"
                    };

                AssetDatabase.CreateAsset(
                    material,
                    AlphaTestMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat(
                "_SrcBlend",
                (float)BlendMode.SrcAlpha);
            material.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue =
                (int)RenderQueue.Transparent;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    Color.white);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        [MenuItem("VCR/P0/Validate Transparent Output Baseline")]
        public static void Validate()
        {
            var failures = 0;

            var camera = Camera.main ??
                Object.FindFirstObjectByType<Camera>();

            if (camera == null)
            {
                Fail("No camera exists.", ref failures);
            }
            else
            {
                Check(
                    camera.clearFlags ==
                        CameraClearFlags.SolidColor,
                    "Camera clear mode is Solid Color.",
                    "Camera clear mode must be Solid Color.",
                    ref failures);

                Check(
                    camera.backgroundColor.a <= 0.001f,
                    "Camera background alpha is zero.",
                    "Camera background alpha must be zero.",
                    ref failures);

                Check(
                    !camera.allowHDR,
                    "Camera HDR is disabled for the lowest-complexity SDR alpha path.",
                    "Camera HDR must be disabled for the P0 SDR alpha path.",
                    ref failures);
            }

            var output =
                Object.FindFirstObjectByType<UniWinCOverlayOutput>();

            Check(
                output != null,
                "UniWinC overlay adapter exists.",
                "UniWinC overlay adapter is missing.",
                ref failures);

            if (output != null)
            {
                var controller =
                    output.GetComponent<UniWindowController>();

                Check(
                    controller != null,
                    "UniWindowController component exists.",
                    "UniWindowController component is missing.",
                    ref failures);

                if (controller != null)
                {
                    Check(
                        !controller.isHitTestEnabled &&
                        controller.hitTestType ==
                            UniWindowController.HitTestType.None,
                        "Automatic per-pixel hit testing is disabled.",
                        "Automatic hit testing must be disabled for the lightweight baseline.",
                        ref failures);

                    Check(
                        controller.transparentType ==
                            UniWindowController.TransparentType.Alpha,
                        "Alpha transparency mode selected.",
                        "Windows transparency mode must be Alpha, not ColorKey.",
                        ref failures);
                }
            }

            var alphaTestMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    AlphaTestMaterialPath);

            Check(
                alphaTestMaterial != null &&
                alphaTestMaterial.shader != null,
                "Alpha-test material asset is referenced and build-preservable.",
                "Alpha-test material asset is missing. Reconfigure the transparent-output baseline.",
                ref failures);

            var urp =
                UniversalRenderPipeline.asset;

            Check(
                urp != null,
                "URP asset is active.",
                "No active URP asset was found.",
                ref failures);

            if (urp != null)
            {
                Check(
                    urp.allowPostProcessAlphaOutput,
                    "URP Alpha Processing is enabled.",
                    "URP Alpha Processing must be enabled.",
                    ref failures);
            }

            var useDefaultWindowsApis =
                PlayerSettings.GetUseDefaultGraphicsAPIs(
                    BuildTarget.StandaloneWindows64);
            var windowsApis =
                PlayerSettings.GetGraphicsAPIs(
                    BuildTarget.StandaloneWindows64);

            Check(
                !useDefaultWindowsApis &&
                windowsApis.Length > 0 &&
                windowsApis[0] ==
                    GraphicsDeviceType.Direct3D11 &&
                !windowsApis.Contains(
                    GraphicsDeviceType.Direct3D12),
                "Windows graphics baseline is D3D11 only.",
                "Windows transparent-window P0 requires explicit D3D11; D3D12 must not be in the baseline list.",
                ref failures);

            Check(
                !PlayerSettings.useFlipModelSwapchain,
                "D3D11 flip-model swapchain is disabled.",
                "Disable D3D11 flip-model swapchain for DWM alpha transparency.",
                ref failures);

            Check(
                PlayerSettings.runInBackground,
                "Run In Background is enabled.",
                "Run In Background should be enabled for broadcast overlay use.",
                ref failures);

            Check(
                !string.IsNullOrWhiteSpace(
                    PlayerSettings.macOS
                        .cameraUsageDescription),
                "macOS camera usage description is configured.",
                "macOS Camera Usage Description is required for standalone webcam tracking.",
                ref failures);

            var macBuildTargetName =
                BuildPipeline.GetBuildTargetName(
                    BuildTarget.StandaloneOSX);
            var macArchitecture =
                EditorUserBuildSettings.GetPlatformSettings(
                    macBuildTargetName,
                    "Architecture");

            Check(
                string.Equals(
                    macArchitecture,
                    "arm64",
                    System.StringComparison.OrdinalIgnoreCase),
                "macOS P0 build architecture is Apple Silicon arm64.",
                $"macOS P0 baseline requires arm64, got '{macArchitecture}'.",
                ref failures);

            if (failures == 0)
            {
                Debug.Log(
                    "VCR P0 transparent output STATIC CHECK: PASS. " +
                    "Native transparency/OBS capture still require standalone-player validation on Windows and macOS.");
            }
            else
            {
                Debug.LogError(
                    $"VCR P0 transparent output STATIC CHECK: FAIL ({failures} issue(s)).");
            }
        }

        private static Camera FindOrCreateMainCamera()
        {
            var camera = Camera.main ??
                Object.FindFirstObjectByType<Camera>();

            if (camera != null)
            {
                if (!camera.CompareTag("MainCamera"))
                {
                    camera.tag = "MainCamera";
                    EditorUtility.SetDirty(camera);
                }

                return camera;
            }

            var gameObject =
                new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(
                gameObject,
                "Create Main Camera");

            gameObject.tag = "MainCamera";
            camera = Undo.AddComponent<Camera>(gameObject);
            gameObject.transform.position =
                new Vector3(0f, 1.4f, -3f);

            return camera;
        }

        private static void ConfigureCamera(Camera camera)
        {
            camera.clearFlags =
                CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            EditorUtility.SetDirty(camera);
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode =
                FullScreenMode.Windowed;

            PlayerSettings.macOS.cameraUsageDescription =
                "VirtualCharacterRender uses the camera for face, hand, and upper-body tracking.";

            var macBuildTargetName =
                BuildPipeline.GetBuildTargetName(
                    BuildTarget.StandaloneOSX);
            EditorUserBuildSettings.SetPlatformSettings(
                macBuildTargetName,
                "Architecture",
                "arm64");

            PlayerSettings.SetUseDefaultGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[]
                {
                    GraphicsDeviceType.Direct3D11
                });

            // Unity documents that DWM transparency via
            // DwmExtendFrameIntoClientArea is incompatible with D3D11 flip
            // model; BitBlt is therefore required for this P0 path.
            PlayerSettings.useFlipModelSwapchain = false;
        }

        private static void ConfigureUrpAlpha()
        {
            var urp =
                UniversalRenderPipeline.asset;

            if (urp == null)
            {
                Debug.LogWarning(
                    "VCR P0: no active URP asset. Alpha Processing could not be configured.");
                return;
            }

            var serialized =
                new SerializedObject(urp);
            var alpha =
                serialized.FindProperty(
                    "m_AllowPostProcessAlphaOutput");

            if (alpha == null)
            {
                Debug.LogError(
                    "VCR P0: URP Alpha Processing serialized property was not found.");
                return;
            }

            alpha.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        private static void Check(
            bool condition,
            string pass,
            string fail,
            ref int failures)
        {
            if (condition)
            {
                Debug.Log(
                    "VCR P0 output: PASS - " + pass);
            }
            else
            {
                Fail(fail, ref failures);
            }
        }

        private static void Fail(
            string message,
            ref int failures)
        {
            failures++;
            Debug.LogError(
                "VCR P0 output: FAIL - " + message);
        }
    }
}
