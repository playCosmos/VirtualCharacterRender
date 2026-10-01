using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VCR.Editor.P0
{
    public static class P0PlayerBuildMenu
    {
        private const string ScenePath =
            "Assets/VCR/P0/P0Runtime.unity";

        [MenuItem("VCR/P0/Build/Windows x64 Evidence Player")]
        public static void BuildWindowsEvidence()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                "Builds/P0/Windows-Evidence/VirtualCharacterRender-P0.exe",
                development: true);
        }

        [MenuItem("VCR/P0/Build/Windows x64 Performance Player")]
        public static void BuildWindowsPerformance()
        {
            Build(
                BuildTarget.StandaloneWindows64,
                "Builds/P0/Windows-Performance/VirtualCharacterRender-P0.exe",
                development: false);
        }

        [MenuItem("VCR/P0/Build/macOS Evidence Player")]
        public static void BuildMacEvidence()
        {
            Build(
                BuildTarget.StandaloneOSX,
                "Builds/P0/macOS-Evidence/VirtualCharacterRender-P0.app",
                development: true);
        }

        [MenuItem("VCR/P0/Build/macOS Performance Player")]
        public static void BuildMacPerformance()
        {
            Build(
                BuildTarget.StandaloneOSX,
                "Builds/P0/macOS-Performance/VirtualCharacterRender-P0.app",
                development: false);
        }

        private static void Build(
            BuildTarget target,
            string location,
            bool development)
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError(
                    "VCR P0 build: runtime scene is missing. " +
                    "Run 'VCR > P0 > Create Runtime Test Scene' first.");
                return;
            }

            P0TransparentOutputMenu.ConfigureProjectBaseline();
            AssetDatabase.SaveAssets();

            var directory =
                Path.GetDirectoryName(location);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = development
                ? BuildOptions.Development |
                  BuildOptions.AllowDebugging
                : BuildOptions.None;

            var build = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = location,
                target = target,
                options = options
            };

            var report =
                BuildPipeline.BuildPlayer(build);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    "VCR P0 build: PASS " +
                    $"target={target}, " +
                    $"mode={(development ? "evidence-development" : "performance")}, " +
                    $"size={summary.totalSize} bytes, " +
                    $"time={summary.totalTime}, " +
                    $"output='{location}'. " +
                    "This is a build PASS only; transparent alpha, OBS capture, " +
                    "tracking quality, and 60 FPS still require player/hardware validation.");
                return;
            }

            Debug.LogError(
                "VCR P0 build: FAIL " +
                $"target={target}, result={summary.result}, " +
                $"errors={summary.totalErrors}, warnings={summary.totalWarnings}.");
        }
    }
}
