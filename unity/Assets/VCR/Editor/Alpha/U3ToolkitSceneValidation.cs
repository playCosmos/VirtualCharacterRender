using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VCR.Editor.P1;
using VCR.Editor.P11;
using VCR.Runtime.Application;
using VCR.Runtime.Output.Unity;
using VCR.Runtime.UI;

namespace VCR.Editor.Alpha
{
    /// <summary>
    /// Exact-source U3 integration test: generate and inspect P1, P11 and
    /// Alpha scenes in a clean CI checkout. Never replace authored scenes.
    /// Model/policy source-free tests remain a separate earlier CI gate.
    /// </summary>
    public static class U3ToolkitSceneValidation
    {
        private const string P1Panel =
            "Assets/VCR/P1/WorkspacePanelSettings.asset";
        private const string P11Panel =
            "Assets/VCR/P11/P11WorkspacePanelSettings.asset";
        private const string AlphaPanel =
            "Assets/VCR/Alpha/AlphaWorkspacePanelSettings.asset";

        public static void GenerateAndValidateAllScenesAndExit()
        {
            try
            {
                var paths = new[]
                {
                    P1ApplicationSceneBuilder.ScenePath,
                    P11ApplicationSceneBuilder.ScenePath,
                    AlphaApplicationSceneBuilder.ScenePath,
                    P1Panel,
                    P11Panel,
                    AlphaPanel
                };
                foreach (var path in paths)
                {
                    if (File.Exists(path) ||
                        AssetDatabase.LoadMainAssetAtPath(path) != null)
                    {
                        Debug.LogError("VCR U3: refusing to overwrite an authored scene/asset: " + path);
                        EditorApplication.Exit(1);
                        return;
                    }
                }

                P1ApplicationSceneBuilder.CreateApplicationRuntimeScene();
                if (!File.Exists(P1ApplicationSceneBuilder.ScenePath) ||
                    !P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene() ||
                    !ValidateGeneratedScene(P1ApplicationSceneBuilder.ScenePath,
                        P1Panel, false))
                {
                    Debug.LogError("VCR U3: generated P1 Toolkit workspace failed validation.");
                    EditorApplication.Exit(1);
                    return;
                }

                P11ApplicationSceneBuilder.CreateApplicationUiRuntimeScene();
                if (!ValidateGeneratedScene(P11ApplicationSceneBuilder.ScenePath,
                        P11Panel, false))
                {
                    Debug.LogError("VCR U3: generated P11 Toolkit workspace failed validation.");
                    EditorApplication.Exit(1);
                    return;
                }

                if (!AlphaApplicationSceneBuilder.CreateAlphaRuntimeScene() ||
                    !ValidateGeneratedScene(AlphaApplicationSceneBuilder.ScenePath,
                        AlphaPanel, true))
                {
                    Debug.LogError("VCR U3: generated Alpha Toolkit workspace failed validation.");
                    EditorApplication.Exit(1);
                    return;
                }

                // Re-open the saved P1 scene after both other generators. An
                // independent P1 Player build must not depend on active Editor
                // scene order or the Alpha/P11 workspace state.
                if (!ValidateGeneratedScene(P1ApplicationSceneBuilder.ScenePath,
                        P1Panel, false) ||
                    !P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene())
                {
                    Debug.LogError("VCR U3: P1 regression after Alpha/P11 scene generation.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log("VCR U3 exact-source generated P1/P11/Alpha Toolkit scene checks: PASS.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR U3 generated Toolkit scene checks: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static bool ValidateGeneratedScene(
            string scenePath, string panelPath, bool requireSmoke)
        {
            if (!File.Exists(scenePath) || !File.Exists(panelPath))
            {
                Debug.LogError("VCR U3: generated scene/PanelSettings missing: " + scenePath);
                return false;
            }
            var alreadyOpen = false;
            var scene = default(Scene);
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var current = SceneManager.GetSceneAt(i);
                if (current.path != scenePath)
                    continue;
                scene = current;
                alreadyOpen = true;
                break;
            }
            if (!alreadyOpen)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.path != scenePath ||
                    scene.isDirty)
                    return Fail(scenePath, "scene unavailable or contains unsaved changes");

                var shells = 0;
                var bootstraps = 0;
                var smokeComponents = 0;
                var outputs = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var item in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = item.gameObject;
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) != 0)
                            return Fail(scenePath, "missing MonoBehaviour script on " + go.name);
                        foreach (var shell in go.GetComponents<WorkspaceToolkitShell>())
                        {
                            shells++;
                            var document = shell.GetComponent<UIDocument>();
                            if (document == null || !document.enabled ||
                                !shell.enabled || !shell.gameObject.activeInHierarchy ||
                                document.panelSettings == null ||
                                !AssetDatabase.Contains(document.panelSettings) ||
                                AssetDatabase.GetAssetPath(document.panelSettings) != panelPath)
                                return Fail(scenePath, "shell has inactive or incorrect persistent UIDocument");
                        }
                        bootstraps += go.GetComponents<ApplicationRuntimeBootstrap>().Length;
                        smokeComponents += go.GetComponents<PlayerStartupSmoke>().Length;
                        foreach (var output in go.GetComponents<UniWinCOverlayOutput>())
                        {
                            outputs++;
                            if (!output.PreservesDesktopWindowFrame ||
                                output.Settings.Transparent || output.Settings.ClickThrough)
                                return Fail(scenePath, "interactive desktop output has transparent/frame-less defaults");
                        }
                    }
                }
                if (shells != 1 || bootstraps != 1 || outputs != 1 ||
                    (requireSmoke && smokeComponents != 1))
                    return Fail(scenePath, "expected exactly one Toolkit shell, bootstrap and output");
                return true;
            }
            finally
            {
                if (!alreadyOpen && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool Fail(string scene, string reason)
        {
            Debug.LogError("VCR U3: " + scene + ": " + reason);
            return false;
        }
    }
}
