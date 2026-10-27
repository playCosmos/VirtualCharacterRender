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
