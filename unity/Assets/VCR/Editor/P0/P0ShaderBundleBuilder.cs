using System.IO;
using UnityEditor;
using UnityEngine;

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
