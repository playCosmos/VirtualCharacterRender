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
        private const string DefaultVersion = "0.1.0-alpha.1";

        // CI sets this from the immutable tag. The legacy local default remains
        // available to run existing alpha.1 evidence builds.
        public static string Version
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable(
                    "VCR_RELEASE_VERSION");
                if (string.IsNullOrEmpty(configured))
                {
                    // game-ci may start Unity in a nested container without
                    // forwarding custom job environment variables.
                    var versionFile = ResolveRepositoryOutputPath(
                        "Builds/Alpha/release-version.txt");
                    if (File.Exists(versionFile))
                    {
                        configured = File.ReadAllText(versionFile).Trim();
                    }
                }

                if (string.IsNullOrEmpty(configured))
                {
                    return DefaultVersion;
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(
                    configured,
                    @"^\d+\.\d+\.\d+-alpha\.\d+$"))
                {
                    throw new InvalidOperationException(
                        "Invalid VCR_RELEASE_VERSION: " + configured);
                }

                return configured;
            }
        }

        public static string WindowsOutput =>
            "Builds/Alpha/" + Version + "/Windows/VirtualCharacterRender.exe";

        public static string MacOutput =>
            "Builds/Alpha/" + Version + "/macOS/VirtualCharacterRender.app";

        [MenuItem("VCR/Alpha/Validate Source-Free (No Physical Camera or ARKit Required)")]
        public static void ValidateSourceFreeFromMenu()
        {
            ValidateSourceFree();
        }

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
        public static void BuildWindowsFromMenu()
        {
            if (!BuildWindows())
            {
                throw new InvalidOperationException(
                    "VCR Windows alpha build failed.");
            }
        }

        public static bool BuildWindows()
        {
            return Build(
                BuildTarget.StandaloneWindows64,
                WindowsOutput);
        }

        [MenuItem("VCR/Alpha/Build/macOS Development Alpha")]
        public static void BuildMacFromMenu()
        {
            if (!BuildMac())
            {
                throw new InvalidOperationException(
                    "VCR macOS alpha build failed.");
            }
        }

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
            Debug.Log(
                "VCR Alpha build: source-free validation is a separate evidence pass and does not block alpha binary generation. " +
                "Current runtime features remain included; physical webcam/ARKit evidence is deferred.");

            if (!AlphaApplicationSceneBuilder
                .CreateAlphaRuntimeScene())
            {
                Debug.LogError(
                    "VCR Alpha build aborted because the integrated runtime scene could not be created.");
                return false;
            }

            P0TransparentOutputMenu.ConfigureProjectBaseline();
            AssetDatabase.SaveAssets();

            var absoluteLocation =
                ResolveRepositoryOutputPath(
                    location);

            var directory =
                Path.GetDirectoryName(
                    absoluteLocation);

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
                            absoluteLocation,
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
                $"output='{absoluteLocation}'. " +
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

        private static string ResolveRepositoryOutputPath(
            string relativePath)
        {
            var projectRoot =
                Directory.GetParent(
                    Application.dataPath)?.FullName;

            var repositoryRoot =
                projectRoot == null
                    ? null
                    : Directory.GetParent(
                        projectRoot)?.FullName;

            if (string.IsNullOrWhiteSpace(
                    repositoryRoot))
            {
                throw new InvalidOperationException(
                    "VCR Alpha build could not resolve the repository root from Application.dataPath.");
            }

            return Path.GetFullPath(
                Path.Combine(
                    repositoryRoot,
                    relativePath));
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
