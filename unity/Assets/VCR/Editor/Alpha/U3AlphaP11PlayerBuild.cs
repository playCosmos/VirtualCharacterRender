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
            bool passed = false;
            var target = NamedBuildTarget.Standalone;
            var originalBackend = PlayerSettings.GetScriptingBackend(target);
            try
            {
                // This builder is deliberately CI-only. It must never
                // replace an existing scene or persisted UIToolkit panel.
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
                // This job verifies actual standalone Windows startup only.
                PlayerSettings.SetScriptingBackend(
                    target, ScriptingImplementation.Mono2x);
                P0TransparentOutputMenu.ConfigureProjectBaseline();
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 800;
                if (!BuildOne(AlphaApplicationSceneBuilder.ScenePath, AlphaOutput,
                        "Alpha") ||
                    !BuildOne(P11ApplicationSceneBuilder.ScenePath, P11Output,
                        "P11"))
                    throw new InvalidOperationException(
                        "U3 Alpha/P11 Windows Mono Player build failed.");
                passed = true;
                Debug.Log("VCR U3 Alpha/P11 Windows Mono Players: PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR U3 Alpha/P11 Windows Mono Players: FAIL");
                Debug.LogException(exception);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, originalBackend);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static bool BuildOne(string scenePath, string output, string label)
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
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                });
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("VCR U3: " + label + " Windows Mono build failed.");
                return false;
            }
            if (!File.Exists(output))
            {
                Debug.LogError("VCR U3: " + label + " build returned no executable.");
                return false;
            }
            Debug.Log("VCR U3: " + label + " standalone Windows build PASS.");
            return true;
        }
    }
}
