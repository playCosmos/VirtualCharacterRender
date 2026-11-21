using System;
using UnityEditor;
using UnityEditor.Build;
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

        // Linux Unity CI has the Windows Mono module, not Windows IL2CPP.
        // This explicitly produces a Mono smoke-test player without changing
        // the checked-in IL2CPP release baseline.
        public static void BuildWindowsMonoCiAndExit()
        {
            var target = NamedBuildTarget.Standalone;
            var original = PlayerSettings.GetScriptingBackend(target);
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                BuildDevelopmentAndExit(
                    BuildTarget.StandaloneWindows64,
                    "Builds/P1/Windows-Development/VirtualCharacterRender.exe");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, original);
            }
        }

        public static void BuildMacDevelopmentAndExit()
        {
            BuildDevelopmentAndExit(
                BuildTarget.StandaloneOSX,
                "Builds/P1/macOS-Development/VirtualCharacterRender.app");
        }

        // Real U1/P1 macOS non-development path. Unlike the Alpha CI
        // scene, this builds the generated UI Toolkit workspace.
        public static void BuildMacReleaseAndExit()
        {
            BuildDevelopmentAndExit(
                BuildTarget.StandaloneOSX,
                "Builds/P1/macOS/VirtualCharacterRender.app",
                development: false);
        }

        // The Linux Unity editor has the macOS Mono module but not Xcode
        // for macOS IL2CPP. Build an actual non-development U1/P1 macOS
        // Player for native Mac smoke, without modifying the committed
        // standalone scripting backend used by Windows IL2CPP builds.
        public static void BuildMacMonoCiAndExit()
        {
            var target = NamedBuildTarget.Standalone;
            var original = PlayerSettings.GetScriptingBackend(target);
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                BuildDevelopmentAndExit(
                    BuildTarget.StandaloneOSX,
                    "Builds/P1/macOS/VirtualCharacterRender.app",
                    development: false);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, original);
                AssetDatabase.SaveAssets();
            }
        }

        // P1 U1 production-path build: full UI Toolkit application scene with
        // IL2CPP, not the P0 alpha scene or Linux-hosted Windows Mono Player.
        // Requires a Windows host with the matching Unity IL2CPP module.
        public static void BuildWindowsIl2CppAndExit()
        {
            try
            {
                if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) !=
                    ScriptingImplementation.IL2CPP)
                {
                    Debug.LogError("VCR P1 IL2CPP build: Windows standalone " +
                        "backend is not IL2CPP.");
                    EditorApplication.Exit(1);
                    return;
                }
                BuildDevelopmentAndExit(
                    BuildTarget.StandaloneWindows64,
                    "Builds/P1/Windows-IL2CPP/VirtualCharacterRender.exe",
                    development: false);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 IL2CPP build: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void BuildDevelopmentAndExit(
            BuildTarget target, string defaultOutput, bool development = true)
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

                var passed = P1PlayerBuildMenu.Build(target, output, development);
                var variant = development
                    ? "development"
                    : target == BuildTarget.StandaloneWindows64 ? "IL2CPP" : "release";
                Debug.Log(passed
                    ? "VCR P1 " + variant + " player build: PASS."
                    : "VCR P1 " + variant + " player build: FAIL.");
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
