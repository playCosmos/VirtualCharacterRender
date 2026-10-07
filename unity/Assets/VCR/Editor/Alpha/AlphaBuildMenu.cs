using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Editor.P13;

namespace VCR.Editor.Alpha
{
    public static class AlphaBuildMenu
    {
        public const string Version =
            "0.1.0-alpha.1";

        public const string WindowsOutput =
            "Builds/Alpha/0.1.0-alpha.1/Windows/VirtualCharacterRender.exe";

        public const string MacOutput =
            "Builds/Alpha/0.1.0-alpha.1/macOS/VirtualCharacterRender.app";

        [MenuItem("VCR/Alpha/Validate Source-Free (No Physical Camera or ARKit Required)")]
        public static bool ValidateSourceFree()
        {
            Debug.Log(
                "VCR Alpha validation: running the complete P0-P13 source-free chain. " +
                "Camera and ARKit implementations remain included in the product; " +
                "this gate does not require physical webcam/iOS hardware.");

            var passed =
                P13BatchValidation.RunChecks();

            Debug.Log(
                passed
                    ? "VCR Alpha source-free validation: PASS."
                    : "VCR Alpha source-free validation: FAIL.");

            return passed;
        }

        [MenuItem("VCR/Alpha/Build/Windows x64 Development Alpha")]
        public static bool BuildWindows()
        {
            return Build(
                BuildTarget.StandaloneWindows64,
                WindowsOutput);
        }

        [MenuItem("VCR/Alpha/Build/macOS Development Alpha")]
        public static bool BuildMac()
        {
            return Build(
                BuildTarget.StandaloneOSX,
                MacOutput);
        }

        public static bool Build(
            BuildTarget target,
            string location)
        {
            if (!ValidateSourceFree())
            {
                Debug.LogError(
                    "VCR Alpha build aborted because source-free validation failed.");
                return false;
            }

            if (!AlphaApplicationSceneBuilder
                .CreateAlphaRuntimeScene())
            {
                Debug.LogError(
                    "VCR Alpha build aborted because the integrated runtime scene could not be created.");
                return false;
            }

            P0TransparentOutputMenu.ConfigureProjectBaseline();
            AssetDatabase.SaveAssets();

            var directory =
                Path.GetDirectoryName(
                    location);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            var report =
                BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes =
                            new[]
                            {
                                AlphaApplicationSceneBuilder
                                    .ScenePath
                            },
                        locationPathName =
                            location,
                        target =
                            target,
                        options =
                            BuildOptions.Development |
                            BuildOptions.AllowDebugging
                    });

            var summary =
                report.summary;

            if (summary.result !=
                BuildResult.Succeeded)
            {
                Debug.LogError(
                    "VCR Alpha build: FAIL " +
                    $"version={Version}, " +
                    $"target={target}, " +
                    $"result={summary.result}, " +
                    $"errors={summary.totalErrors}, " +
                    $"warnings={summary.totalWarnings}.");
                return false;
            }

            Debug.Log(
                "VCR Alpha build: PASS " +
                $"version={Version}, " +
                $"target={target}, " +
                $"size={summary.totalSize} bytes, " +
                $"time={summary.totalTime}, " +
                $"output='{location}'. " +
                "The build contains current runtime features including webcam/ARKit/VMC integrations, " +
                "but those physical-input paths start disabled in the alpha scene.");

            return true;
        }

        public static void ValidateAndExit()
        {
            ExitWithResult(
                ValidateSourceFree());
        }

        public static void BuildWindowsAndExit()
        {
            ExitWithResult(
                BuildWindows());
        }

        public static void BuildMacAndExit()
        {
            ExitWithResult(
                BuildMac());
        }

        private static void ExitWithResult(
            bool passed)
        {
            EditorApplication.Exit(
                passed
                    ? 0
                    : 1);
        }
    }
}
