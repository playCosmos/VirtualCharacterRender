using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VCR.Editor.P0;

namespace VCR.Editor.P1
{
    public static class P1PlayerBuildMenu
    {
        [MenuItem("VCR/P1/Build/Windows x64 Development Player")]
        public static void BuildWindowsDevelopment()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                "Builds/P1/Windows-Development/VirtualCharacterRender.exe",
                development: true);
        }

        [MenuItem("VCR/P1/Build/Windows x64 Player")]
        public static void BuildWindows()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                "Builds/P1/Windows/VirtualCharacterRender.exe",
                development: false);
        }

        [MenuItem("VCR/P1/Build/macOS Development Player")]
        public static void BuildMacDevelopment()
        {
            Build(
                BuildTarget.StandaloneOSX,
                "Builds/P1/macOS-Development/VirtualCharacterRender.app",
                development: true);
        }

        [MenuItem("VCR/P1/Build/macOS Player")]
        public static void BuildMac()
        {
            Build(
                BuildTarget.StandaloneOSX,
                "Builds/P1/macOS/VirtualCharacterRender.app",
                development: false);
        }

        public static bool Build(
            BuildTarget target,
            string location,
            bool development)
        {
            if (!File.Exists(
                    P1ApplicationSceneBuilder.ScenePath))
            {
                Debug.LogError(
                    "VCR P1 build: runtime scene is missing. " +
                    "Run 'VCR > P1 > Create Application Runtime Scene' first.");
                return false;
            }

            P0TransparentOutputMenu.ConfigureProjectBaseline();
            AssetDatabase.SaveAssets();

            var directory =
                Path.GetDirectoryName(location);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options =
                development
                    ? BuildOptions.Development |
                      BuildOptions.AllowDebugging
                    : BuildOptions.None;

            var report =
                BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes =
                            new[]
                            {
                                P1ApplicationSceneBuilder.ScenePath
                            },
                        locationPathName =
                            location,
                        target =
                            target,
                        options =
                            options
                    });

            var summary = report.summary;

            if (summary.result !=
                BuildResult.Succeeded)
            {
                Debug.LogError(
                    "VCR P1 build: FAIL " +
                    $"target={target}, " +
                    $"result={summary.result}, " +
                    $"errors={summary.totalErrors}, " +
                    $"warnings={summary.totalWarnings}.");
                return false;
            }

            Debug.Log(
                "VCR P1 build: PASS " +
                $"target={target}, " +
                $"mode={(development ? "development" : "release")}, " +
                $"size={summary.totalSize} bytes, " +
                $"time={summary.totalTime}, " +
                $"output='{location}'. " +
                "This is a build PASS only; hardware-dependent P0 evidence remains deferred.");

            return true;
        }
    }
}
