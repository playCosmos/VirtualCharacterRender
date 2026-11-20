using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Output.Unity;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Editor.P0;
using VCR.Runtime.Application;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Tracking.Mixing;
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

            // Keep P11's application-policy tests while generating only
            // the UI Toolkit application shell.
            var mainCamera = bootstrap.GetComponentInChildren<Camera>(true);
            var output = bootstrap.GetComponent<UniWinCOverlayOutput>();
            if (mainCamera == null || output == null)
            {
                Debug.LogError("VCR P11: Toolkit workspace requires camera and output.");
                return;
            }
            mainCamera.backgroundColor = new Color(0.105f, 0.12f, 0.14f, 1f);
            output.ConfigureForP1Workspace(mainCamera);

            // P11 is now an interactive application scene, not the P0
            // transparent/webcam hardware probe. The operator opts in to
            // physical tracking after the normal window starts.
            var webcam = bootstrap.GetComponentInChildren<
                MediaPipeWebcamTrackingRunner>(true);
            var diagnostics = bootstrap.GetComponent<RuntimeDiagnostics>();
            if (webcam == null || diagnostics == null)
            {
                Debug.LogError("VCR P11: missing tracking or diagnostics base service.");
                return;
            }
            webcam.enabled = false;
            diagnostics.ConfigureForP1Workspace();

            // Inert on regular launches: only the explicit smoke-report
            // command-line option tests real Player startup and shutdown.
            if (bootstrap.GetComponent<PlayerStartupSmoke>() == null)
                bootstrap.gameObject.AddComponent<PlayerStartupSmoke>();

            const string panelPath =
                "Assets/VCR/P11/P11WorkspacePanelSettings.asset";
            var panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, panelPath);
            }
            if (!AssetDatabase.Contains(panelSettings))
            {
                Debug.LogError("VCR P11: Toolkit PanelSettings could not be persisted.");
                return;
            }
            var workspace = new GameObject("Workspace UI Toolkit");
            workspace.SetActive(false);
            workspace.transform.SetParent(bootstrap.transform, false);
            var document = workspace.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            workspace.AddComponent<WorkspaceToolkitShell>();
            workspace.SetActive(true);

            if (bootstrap.GetComponent<
                    DesktopCharacterFileSelectionAdapter>() ==
                null)
            {
                Undo.AddComponent<
                    DesktopCharacterFileSelectionAdapter>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    AppearanceTransitionActionExecutor>() ==
                null)
            {
                Undo.AddComponent<
                    AppearanceTransitionActionExecutor>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    EffectEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    EffectEventActionHandler>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    PropEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    PropEventActionHandler>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    SceneSequenceEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    SceneSequenceEventActionHandler>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    AppearanceEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    AppearanceEventActionHandler>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    AudioEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    AudioEventActionHandler>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    ProceduralMotionCueSource>() ==
                null)
            {
                Undo.AddComponent<
                    ProceduralMotionCueSource>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    BakedMotionCueSource>() ==
                null)
            {
                Undo.AddComponent<
                    BakedMotionCueSource>(
                        bootstrap.gameObject);
            }

            if (bootstrap.GetComponent<
                    MotionCueEventActionHandler>() ==
                null)
            {
                Undo.AddComponent<
                    MotionCueEventActionHandler>(
                        bootstrap.gameObject);
            }

            var manualExpression =
                bootstrap.GetComponent<
                    ManualExpressionLayerSource>();

            if (manualExpression == null)
            {
                manualExpression =
                    Undo.AddComponent<
                        ManualExpressionLayerSource>(
                        bootstrap.gameObject);
            }

            var expressionHandler =
                bootstrap.GetComponent<
                    ExpressionEventActionHandler>();

            if (expressionHandler == null)
            {
                expressionHandler =
                    Undo.AddComponent<
                        ExpressionEventActionHandler>(
                        bootstrap.gameObject);
            }

            expressionHandler
                .SetExpressionSource(
                    manualExpression);

            var motionMixer =
                Object.FindFirstObjectByType<
                    MotionExpressionMixer>();

            if (motionMixer != null &&
                !motionMixer
                    .IsExpressionLayerProvider(
                        manualExpression))
            {
                motionMixer
                    .SetExpressionLayerProvider(
                        manualExpression);
                motionMixer
                    .ConfigureExpressionLayer(
                        ExpressionBlendMode.Maximum,
                        1f,
                        0f,
                        0f);
                EditorUtility.SetDirty(
                    motionMixer);
            }

            var eventRuntime =
                Object.FindFirstObjectByType<
                    EventRuntimeHost>();

            if (eventRuntime != null)
            {
                var handlers =
                    new List<MonoBehaviour>();

                var behaviours =
                    Object.FindObjectsByType<
                        MonoBehaviour>(
                            FindObjectsInactive.Exclude,
                            FindObjectsSortMode.None);

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is
                        IEventActionHandler)
                    {
                        handlers.Add(
                            behaviour);
                    }
                }

                eventRuntime.SetActionHandlers(
                    handlers.ToArray());
            }

            AssetDatabase.SaveAssets();

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
