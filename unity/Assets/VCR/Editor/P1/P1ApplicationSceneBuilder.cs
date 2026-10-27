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

            // Additive loading preserves unrelated open scenes and their edits.
            var targetAlreadyLoaded = false;
            var scene = default(UnityEngine.SceneManagement.Scene);
            for (var index = 0; index < EditorSceneManager.sceneCount; index++)
            {
                var loaded = EditorSceneManager.GetSceneAt(index);
                if (loaded.path != ScenePath) continue;
                scene = loaded;
                targetAlreadyLoaded = true;
                break;
            }
            if (!targetAlreadyLoaded)
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
                return false;

            try
            {
                var workspaceCount = 0;
                WorkspaceToolkitShell existingShell = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var shells = root.GetComponentsInChildren<WorkspaceToolkitShell>(true);
                    workspaceCount += shells.Length;
                    if (shells.Length > 0)
                        existingShell = shells[0];
                }
                if (workspaceCount > 1)
                {
                    Debug.LogError("VCR P1: multiple workspace shells found; refusing ambiguous UI setup.");
                    return false;
                }
                if (existingShell != null)
                {
                    // A dirty target scene may contain edits not yet serialized.
                    // Validate it, but do not silently package a different disk state.
                    if (scene.isDirty)
                    {
                        Debug.LogError("VCR P1: runtime scene has unsaved changes; save it before building.");
                        return false;
                    }
                    var existingDocument = existingShell.GetComponent<UIDocument>();
                    if (existingDocument == null || existingDocument.panelSettings == null)
                    {
                        Debug.LogError("VCR P1: existing workspace has no configured UIDocument/PanelSettings.");
                        return false;
                    }
                    if (!existingShell.gameObject.activeInHierarchy || !existingShell.enabled ||
                        !existingDocument.enabled)
                    {
                        Debug.LogError("VCR P1: existing workspace is disabled; refusing to build a player with no visible shell.");
                        return false;
                    }
                    return true;
                }
    
                if (scene.isDirty)
                {
                    Debug.LogError("VCR P1: runtime scene has unsaved changes; save it before adding workspace.");
                    return false;
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
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    workspaceObject, scene);
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
            finally
            {
                // Only close scenes opened by this method. Preserve scenes
                // that were already part of the user's editing session.
                if (!targetAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
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
