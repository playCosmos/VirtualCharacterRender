using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P0;

namespace VCR.Editor.P1
{
    public static class P1BatchValidation
    {
        // Generates the missing P1 scene in a clean batch workspace, then validates
        // the installed UI Toolkit shell. Never overwrites an existing scene.
        public static void GenerateWorkspaceAndExit()
        {
            try
            {
                if (System.IO.File.Exists(P1ApplicationSceneBuilder.ScenePath))
                {
                    Debug.LogError("VCR P1: refusing to regenerate an existing scene.");
                    EditorApplication.Exit(1);
                    return;
                }

                P1ApplicationSceneBuilder.CreateApplicationRuntimeScene();
                var passed =
                    System.IO.File.Exists(P1ApplicationSceneBuilder.ScenePath) &&
                    P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene();
                Debug.Log(passed
                    ? "VCR P1 workspace generation: PASS."
                    : "VCR P1 workspace generation: FAIL.");
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace generation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // Invoke with -batchmode -executeMethod VCR.Editor.P1.P1BatchValidation.RunWorkspaceAndExit.
        // This checks the actual P1 scene and upgrades legacy scenes before a player build.
        // RunSourceFreeAndExit remains usable without a generated scene asset.
        public static void RunWorkspaceAndExit()
        {
            try
            {
                var passed = P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene();
                Debug.Log(passed
                    ? "VCR P1 workspace scene validation: PASS."
                    : "VCR P1 workspace scene validation: FAIL.");
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace scene validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // Runs an actual development player build in Unity batch mode.
        // Supply -vcrBuildOutput <path> to override the default output location.
        public static void BuildWindowsDevelopmentAndExit()
        {
            BuildDevelopmentAndExit(
                BuildTarget.StandaloneWindows64,
                "Builds/P1/Windows-Development/VirtualCharacterRender.exe");
        }

        public static void BuildMacDevelopmentAndExit()
        {
            BuildDevelopmentAndExit(
                BuildTarget.StandaloneOSX,
                "Builds/P1/macOS-Development/VirtualCharacterRender.app");
        }

        private static void BuildDevelopmentAndExit(BuildTarget target, string defaultOutput)
        {
            try
            {
                var output = defaultOutput;
                var args = Environment.GetCommandLineArgs();
                for (var index = 0; index < args.Length; index++)
                {
                    if (args[index] != "-vcrBuildOutput")
                        continue;
                    if (index + 1 >= args.Length ||
                        string.IsNullOrWhiteSpace(args[index + 1]) ||
                        args[index + 1].StartsWith("-"))
                    {
                        Debug.LogError("VCR P1 batch build: -vcrBuildOutput requires a path.");
                        EditorApplication.Exit(1);
                        return;
                    }
                    output = args[index + 1];
                    break;
                }

                var passed = P1PlayerBuildMenu.Build(target, output, development: true);
                Debug.Log(passed
                    ? "VCR P1 development player build: PASS."
                    : "VCR P1 development player build: FAIL.");
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 development player build: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RunSourceFreeAndExit()
        {
            try
            {
                var p0Passed =
                    P0SourceFreeValidationSuite
                        .RunAllChecks();

                var p1Passed =
                    P1RendererCoreValidation
                        .RunChecks();

                EditorApplication.Exit(
                    p0Passed && p1Passed
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P1 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
