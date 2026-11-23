using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Editor.P11;

namespace VCR.Editor.Alpha
{
    /// <summary>
    /// U3-only Linux-hosted Windows Mono integration evidence. These are
    /// development Players, never IL2CPP release or operator acceptance.
    /// Creates only missing fixture scenes in a clean CI checkout and refuses
    /// to rewrite user-authored scenes/PanelSettings.
    /// </summary>
    public static class U3AlphaP11PlayerBuild
    {
        private const string AlphaOutput =
            "Builds/U3/Alpha/Windows-Development/VirtualCharacterRender.exe";
        private const string P11Output =
            "Builds/U3/P11/Windows-Development/VirtualCharacterRender.exe";

        public static void BuildWindowsMonoAndExit()
        {
            BuildBothPlayersAndExit(
                BuildTarget.StandaloneWindows64, AlphaOutput, P11Output,
                BuildOptions.Development | BuildOptions.AllowDebugging,
                ScriptingImplementation.Mono2x);
        }

        // Native Windows host with the pinned IL2CPP Editor module only.
        // This nondevelopment evidence is not a signed release.
        public static void BuildWindowsIl2CppAndExit()
        {
            BuildBothPlayersAndExit(
                BuildTarget.StandaloneWindows64,
                "Builds/U3/Alpha/Windows-IL2CPP/VirtualCharacterRender.exe",
                "Builds/U3/P11/Windows-IL2CPP/VirtualCharacterRender.exe",
                BuildOptions.None, ScriptingImplementation.IL2CPP);
        }

        // macOS Mono Players exercise the exact Toolkit scenes without
        // claiming signed/notarized distribution or IL2CPP certification.
        public static void BuildMacMonoAndExit()
        {
            BuildBothPlayersAndExit(
                BuildTarget.StandaloneOSX,
                "Builds/U3/Alpha/macOS/VirtualCharacterRender.app",
                "Builds/U3/P11/macOS/VirtualCharacterRender.app",
                BuildOptions.None, ScriptingImplementation.Mono2x);
        }

        private static void BuildBothPlayersAndExit(
            BuildTarget buildTarget, string alphaOutput, string p11Output,
            BuildOptions options, ScriptingImplementation backend)
        {
            bool passed = false;
            var target = NamedBuildTarget.Standalone;
            var originalBackend = PlayerSettings.GetScriptingBackend(target);
            try
            {
                if (backend == ScriptingImplementation.IL2CPP &&
                    (buildTarget != BuildTarget.StandaloneWindows64 ||
                     originalBackend != ScriptingImplementation.IL2CPP ||
                     options != BuildOptions.None))
                    throw new InvalidOperationException(
                        "U3 IL2CPP evidence requires pinned native Windows " +
                        "IL2CPP baseline and nondevelopment build.");
                // Never overwrite authored scenes or persisted PanelSettings.
                // The exact-source AppKit plugin is restored by CI before
                // native macOS packaging, not created in this builder.
                if (buildTarget == BuildTarget.StandaloneOSX &&
                    !File.Exists("Assets/Plugins/macOS/VcrSettingsWindow.dylib"))
                    throw new InvalidOperationException(
                        "Missing source-bound macOS AppKit Settings dylib.");
                var inputs = new[]
                {
                    AlphaApplicationSceneBuilder.ScenePath,
                    P11ApplicationSceneBuilder.ScenePath,
                    "Assets/VCR/Alpha/AlphaWorkspacePanelSettings.asset",
                    "Assets/VCR/P11/P11WorkspacePanelSettings.asset"
                };
                foreach (var path in inputs)
                {
                    if (File.Exists(path) ||
                        AssetDatabase.LoadMainAssetAtPath(path) != null)
                        throw new InvalidOperationException(
                            "U3 refuses to replace authored scene/PanelSettings: " + path);
                }

                if (!AlphaApplicationSceneBuilder.CreateAlphaRuntimeScene())
                    throw new InvalidOperationException(
                        "U3 Alpha scene generator failed.");
                P11ApplicationSceneBuilder.CreateApplicationUiRuntimeScene();
                if (!U3ToolkitSceneValidation.ValidateExistingAlphaAndP11Scenes())
                    throw new InvalidOperationException(
                        "U3 generated Alpha/P11 scenes failed read-only acceptance.");

                // Restore the committed IL2CPP baseline after every build.
                PlayerSettings.SetScriptingBackend(target, backend);
                P0TransparentOutputMenu.ConfigureProjectBaseline();
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 800;
                if (!BuildOne(AlphaApplicationSceneBuilder.ScenePath,
                        alphaOutput, "Alpha", buildTarget, options) ||
                    !BuildOne(P11ApplicationSceneBuilder.ScenePath,
                        p11Output, "P11", buildTarget, options))
                    throw new InvalidOperationException(
                        "U3 Alpha/P11 standalone " + backend + " Player build failed.");
                passed = true;
                Debug.Log("VCR U3 Alpha/P11 " + buildTarget +
                    " " + backend + " Players: PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR U3 Alpha/P11 " + buildTarget +
                    " " + backend + " Players: FAIL");
                Debug.LogException(exception);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, originalBackend);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static bool BuildOne(
            string scenePath, string output, string label,
            BuildTarget buildTarget, BuildOptions options)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogError("VCR U3: " + label + " saved scene missing.");
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = output,
                    target = buildTarget,
                    options = options
                });
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("VCR U3: " + label + " " + buildTarget + " build failed.");
                return false;
            }
            var found = buildTarget == BuildTarget.StandaloneOSX
                ? Directory.Exists(output) &&
                    File.Exists(Path.Combine(output, "Contents", "Info.plist"))
                : File.Exists(output);
            if (!found)
            {
                Debug.LogError("VCR U3: " + label + " Player output missing.");
                return false;
            }
            Debug.Log("VCR U3: " + label + " " + buildTarget + " build PASS.");
            return true;
        }
    }
}
