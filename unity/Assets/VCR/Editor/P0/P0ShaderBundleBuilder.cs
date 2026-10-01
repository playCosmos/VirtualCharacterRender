using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Materials.Unity;

namespace VCR.Editor.P0
{
    public static class P0ShaderBundleBuilder
    {
        private const string ShaderAssetPath =
            "Assets/VCR/P0/Shaders/P0TintUnlit.shader";

        [MenuItem("VCR/P0/Build Shader Bundle/Windows x64")]
        public static void BuildWindows()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                "windows");
        }

        [MenuItem("VCR/P0/Build Shader Bundle/macOS")]
        public static void BuildMac()
        {
            Build(
                BuildTarget.StandaloneOSX,
                "macos");
        }

        [MenuItem("VCR/P0/Validate Current Platform Shader Bundle")]
        public static void ValidateCurrentPlatformBundle()
        {
#if UNITY_EDITOR_WIN
            var platformFolder = "windows";
#elif UNITY_EDITOR_OSX
            var platformFolder = "macos";
#else
            Debug.LogError(
                "VCR P0 shader bundle: current Editor platform is not a supported P0 desktop target.");
            return;
#endif

            var bundlePath =
                Path.GetFullPath(
                    Path.Combine(
                        "Assets",
                        "VCR",
                        "P0",
                        "BuiltShaderBundles",
                        platformFolder,
                        "vcr-p0-shaders.bundle"));

            var root = new GameObject(
                "VCR P0 Shader Bundle Self-Test");
            root.hideFlags =
                HideFlags.HideAndDontSave;

            try
            {
                var loader =
                    root.AddComponent<
                        RuntimeShaderBundleLoader>();

                if (!loader.TryLoadFromFile(
                        bundlePath,
                        out var registeredCount,
                        out var loadError))
                {
                    Debug.LogError(
                        "VCR P0 shader bundle: FAIL - " +
                        loadError);
                    return;
                }

                if (registeredCount <= 0)
                {
                    Debug.LogError(
                        "VCR P0 shader bundle: FAIL - no Shader assets were registered.");
                    return;
                }

                if (!RuntimeShaderRegistry.TryResolve(
                        "VCR/P0/TintUnlit",
                        out var shader) ||
                    shader == null)
                {
                    Debug.LogError(
                        "VCR P0 shader bundle: FAIL - loaded Shader was not registered.");
                    return;
                }

                Debug.Log(
                    $"VCR P0 shader bundle SELF-TEST: PASS - '{shader.name}' loaded and registered from {bundlePath}.");
            }
            finally
            {
                RuntimeShaderRegistry.Unregister(
                    "VCR/P0/TintUnlit");

                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void Build(
            BuildTarget target,
            string platformFolder)
        {
            var shader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    ShaderAssetPath);

            if (shader == null)
            {
                Debug.LogError(
                    "VCR P0 shader bundle: validation Shader asset is missing.");
                return;
            }

            var outputDirectory =
                Path.Combine(
                    "Assets",
                    "VCR",
                    "P0",
                    "BuiltShaderBundles",
                    platformFolder);

            Directory.CreateDirectory(
                outputDirectory);

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName =
                        "vcr-p0-shaders.bundle",
                    assetNames =
                        new[]
                        {
                            ShaderAssetPath
                        }
                }
            };

            var manifest =
                BuildPipeline.BuildAssetBundles(
                    outputDirectory,
                    builds,
                    BuildAssetBundleOptions
                        .ForceRebuildAssetBundle,
                    target);

            AssetDatabase.Refresh();

            if (manifest == null)
            {
                Debug.LogError(
                    $"VCR P0 shader bundle: FAIL for {target}.");
                return;
            }

            var bundlePath =
                Path.Combine(
                    outputDirectory,
                    "vcr-p0-shaders.bundle");

            Debug.Log(
                $"VCR P0 shader bundle: PASS - {target} -> {bundlePath}. " +
                "Build the other target separately; bundles are platform-specific.");
        }
    }
}
