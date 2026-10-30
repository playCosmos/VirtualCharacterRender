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
            if (target != BuildTarget.StandaloneWindows64 &&
                target != BuildTarget.StandaloneOSX)
            {
                Debug.LogError("VCR P1 build: unsupported target " + target + ".");
                return false;
            }
            if (string.IsNullOrWhiteSpace(location))
            {
                Debug.LogError("VCR P1 build: output location is required.");
                return false;
            }

            if (!File.Exists(
                    P1ApplicationSceneBuilder.ScenePath))
            {
                Debug.LogError(
                    "VCR P1 build: runtime scene is missing. " +
                    "Run 'VCR > P1 > Create Application Runtime Scene' first.");
                return false;
            }

            // Existing P1 scenes may predate the UI Toolkit workspace.
            // Upgrade in place before packaging, rather than shipping a
            // successfully built player without the U1 application shell.
            if (!P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene())
            {
                Debug.LogError("VCR P1 build: workspace scene upgrade failed.");
                return false;
            }

            P0TransparentOutputMenu.ConfigureProjectBaseline();
            // P0 transparency is a separate output mode. The P1 editor
            // starts as a standard decorated, freely resizable desktop
            // window; never package it as fullscreen/popup-only.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed ||
                !PlayerSettings.resizableWindow)
            {
                Debug.LogError("VCR P1: native window must be windowed and resizable.");
                return false;
            }
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

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(
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
            }
            catch (System.Exception exception)
            {
                Debug.LogError(
                    "VCR P1 build: BuildPipeline exception for " + target + ".");
                Debug.LogException(exception);
                return false;
            }

            if (report == null)
            {
                Debug.LogError("VCR P1 build: BuildPipeline returned no report.");
                return false;
            }
            // BuildReport.summary is a BuildSummary value type, not a nullable report.
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
