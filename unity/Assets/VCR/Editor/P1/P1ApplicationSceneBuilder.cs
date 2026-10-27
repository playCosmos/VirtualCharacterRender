using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Editor.P0;
using VCR.Runtime.UI;

namespace VCR.Editor.P1
{
    public static class P1ApplicationSceneBuilder
    {
        public const string ScenePath =
            "Assets/VCR/P1/P1Runtime.unity";
        private const string PanelSettingsPath =
            "Assets/VCR/P1/WorkspacePanelSettings.asset";

        // Upgrade an existing P1 scene without replacing user-authored objects.
        // The P0 scene generator is intentionally not invoked here.
        public static bool EnsureWorkspaceInExistingScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogError("VCR P1: runtime scene is missing.");
                return false;
            }

            // OpenScene(Single) replaces the currently edited scene. Never
            // discard unrelated unsaved work during an upgrade or build.
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError("VCR P1: active scene has unsaved changes; refusing batch upgrade.");
                    return false;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("VCR P1: workspace upgrade cancelled.");
                    return false;
                }
            }

            var scene = activeScene.IsValid() && activeScene.path == ScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(ScenePath);
            if (!scene.IsValid())
                return false;

            foreach (var root in scene.GetRootGameObjects())
            {
                var existingShell = root.GetComponentInChildren<WorkspaceToolkitShell>(true);
                if (existingShell == null)
                    continue;
                var existingDocument = existingShell.GetComponent<UIDocument>();
                if (existingDocument == null || existingDocument.panelSettings == null)
                {
                    Debug.LogError("VCR P1: existing workspace has no configured UIDocument/PanelSettings.");
                    return false;
                }
                return true;
            }

            var panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            var workspaceObject = new GameObject("Workspace UI Toolkit");
            workspaceObject.SetActive(false);
            var document = workspaceObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            workspaceObject.AddComponent<WorkspaceToolkitShell>();
            workspaceObject.SetActive(true);

            if (!EditorSceneManager.SaveScene(scene))
            {
                Object.DestroyImmediate(workspaceObject);
                Debug.LogError("VCR P1: could not save workspace into existing scene.");
                return false;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("VCR P1: existing runtime scene upgraded with UI Toolkit workspace.");
            return true;
        }

        [MenuItem("VCR/P1/Upgrade Existing Runtime Scene With Workspace")]
        public static void UpgradeExistingRuntimeScene()
        {
            EnsureWorkspaceInExistingScene();
        }

        [MenuItem("VCR/P1/Create Application Runtime Scene")]
        public static void CreateApplicationRuntimeScene()
        {
            if (!P0TrackingSceneBuilder.CreateRuntimeScene(
                    ScenePath,
                    "VCR P1 Runtime",
                    "environment.p1.basic",
                    "P1"))
                return;

            var panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                Debug.LogError("VCR P1: expected runtime scene is not active; workspace was not created.");
                return;
            }

            var workspaceObject = new GameObject("Workspace UI Toolkit");
            workspaceObject.SetActive(false);
            var document = workspaceObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            workspaceObject.AddComponent<WorkspaceToolkitShell>();
            workspaceObject.SetActive(true);

            if (!EditorSceneManager.SaveScene(scene))
            {
                Object.DestroyImmediate(workspaceObject);
                Debug.LogError("VCR P1: failed to save runtime scene with workspace UI.");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("VCR P1: UI Toolkit workspace added to the application runtime scene.");
        }
    }
}
