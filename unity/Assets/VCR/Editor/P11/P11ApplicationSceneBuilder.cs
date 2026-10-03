using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Runtime.Application;
using VCR.Runtime.UI;

namespace VCR.Editor.P11
{
    public static class P11ApplicationSceneBuilder
    {
        public const string ScenePath =
            "Assets/VCR/P11/P11Runtime.unity";

        [MenuItem("VCR/P11/Create Application UI Runtime Scene")]
        public static void CreateApplicationUiRuntimeScene()
        {
            if (!P0TrackingSceneBuilder
                .CreateRuntimeScene(
                    ScenePath,
                    "VCR P11 Runtime",
                    "environment.p11.basic",
                    "P11"))
            {
                return;
            }

            var bootstrap =
                Object.FindFirstObjectByType<
                    ApplicationRuntimeBootstrap>();

            if (bootstrap == null)
            {
                Debug.LogError(
                    "VCR P11: ApplicationRuntimeBootstrap was not created.");
                return;
            }

            if (bootstrap.GetComponent<
                    ApplicationUiController>() ==
                null)
            {
                Undo.AddComponent<
                    ApplicationUiController>(
                        bootstrap.gameObject);
            }

            if (!EditorSceneManager
                .SaveOpenScenes())
            {
                Debug.LogError(
                    "VCR P11: failed to save the application UI runtime scene.");
                return;
            }

            Selection.activeGameObject =
                bootstrap.gameObject;
            EditorGUIUtility.PingObject(
                bootstrap.gameObject);

            Debug.Log(
                "VCR P11 application UI runtime scene created: " +
                ScenePath);
        }
    }
}
