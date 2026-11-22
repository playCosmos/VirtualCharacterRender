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
                BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        // macOS Mono Players exercise the exact Toolkit scenes without
        // claiming signed/notarized distribution or IL2CPP certification.
        public static void BuildMacMonoAndExit()
        {
            BuildBothPlayersAndExit(
                BuildTarget.StandaloneOSX,
                "Builds/U3/Alpha/macOS/VirtualCharacterRender.app",
                "Builds/U3/P11/macOS/VirtualCharacterRender.app",
                BuildOptions.None);
        }

        private static void BuildBothPlayersAndExit(
            BuildTarget buildTarget, string alphaOutput, string p11Output,
            BuildOptions options)
        {
            bool passed = false;
            var target = NamedBuildTarget.Standalone;
            var originalBackend = PlayerSettings.GetScriptingBackend(target);
            try
            {
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

                // The pinned repository release baseline stays IL2CPP.
                // Both development Windows and nondevelopment macOS Players are diagnostic.
                PlayerSettings.SetScriptingBackend(
                    target, ScriptingImplementation.Mono2x);
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
                        "U3 Alpha/P11 standalone Mono Player build failed.");
                passed = true;
                Debug.Log("VCR U3 Alpha/P11 " + buildTarget + " Mono Players: PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR U3 Alpha/P11 " + buildTarget + " Mono Players: FAIL");
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
                Debug.LogError("VCR U3: " + label + " " + buildTarget + " Mono build failed.");
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
