using System.Collections.Generic;
using Leap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.Application;
using VCR.Runtime.Character;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Events.Unity;
using VCR.Runtime.Materials.Unity;
using VCR.Runtime.Presentation2D;
using VCR.Runtime.Protocols.VmcUnity;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.ArKitUnity;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Runtime.Tracking.Mixing;
using VCR.Runtime.Tracking.Routing;
using VCR.Runtime.Tracking.Ultraleap;
using VCR.Runtime.UI;

namespace VCR.Editor.Alpha
{
    public static class AlphaApplicationSceneBuilder
    {
        public const string ScenePath =
            "Assets/VCR/Alpha/AlphaRuntime.unity";

        public const string RootName =
            "VCR Alpha Runtime";

        [MenuItem("VCR/Alpha/Create Integrated Runtime Scene")]
        public static void CreateFromMenu()
        {
            CreateAlphaRuntimeScene();
        }

        public static bool CreateAlphaRuntimeScene()
        {
            if (!P0TrackingSceneBuilder.CreateRuntimeScene(
                    ScenePath,
                    RootName,
                    "environment.alpha.basic",
                    "Alpha"))
            {
                return false;
            }

            var root =
                GameObject.Find(RootName);

            if (root == null)
            {
                Debug.LogError(
                    "VCR Alpha: generated runtime root was not found.");
                return false;
            }

            var tracking =
                root.transform.Find("Tracking");
            var eventsRoot =
                root.transform.Find("Events");
            var character =
                root.transform.Find("Character");

            if (tracking == null ||
                eventsRoot == null ||
                character == null)
            {
                Debug.LogError(
                    "VCR Alpha: base runtime scene is missing Tracking, Events, or Character roots.");
                return false;
            }

            var mediaPipe =
                tracking.GetComponent<
                    MediaPipeWebcamTrackingRunner>();
            var router =
                tracking.GetComponent<
                    PriorityTrackingRouter>();
            var loader =
                character.GetComponent<
                    Vrm10CharacterLoader>();
            var diagnostics =
                root.GetComponent<
                    RuntimeDiagnostics>();
            var eventHub =
                eventsRoot.GetComponent<
                    NormalizedEventHub>();

            if (mediaPipe == null ||
                router == null ||
                loader == null ||
                diagnostics == null ||
                eventHub == null)
            {
                Debug.LogError(
                    "VCR Alpha: base runtime scene is missing required runtime components.");
                return false;
            }

            // Alpha includes every tracking implementation, but starts physical
            // input sources disabled so non-hardware validation can run first.
            mediaPipe.enabled = false;

            var arkit =
                tracking.gameObject.AddComponent<
                    IFacialMocapUdpReceiver>();
            arkit.enabled = false;

            var vmcReceiver =
                tracking.gameObject.AddComponent<
                    VmcUdpReceiver>();
            vmcReceiver.enabled = false;

            var vmcSender =
                tracking.gameObject.AddComponent<
                    VmcUdpSender>();
            vmcSender.enabled = false;

            var ultraleapObject =
                new GameObject(
                    "Ultraleap");
            ultraleapObject.transform.SetParent(
                tracking,
                false);

            var ultraleapProvider =
                ultraleapObject.AddComponent<
                    LeapServiceProvider>();
            var ultraleap =
                ultraleapObject.AddComponent<
                    UltraleapTrackingRunner>();
            ultraleap.ConfigureProvider(
                ultraleapProvider);

            // Hardware inputs remain opt-in. The generic tracking-source UI
            // enables the adapter and its provider together after the user
            // explicitly adds/enables Ultraleap.
            ultraleap.enabled = false;
            ultraleapProvider.enabled = false;

            router.SetPreferredFaceProvider(
                arkit);
            router.SetPreferredHandsProvider(
                ultraleap);
            router.SetFallbackProvider(
                mediaPipe);
            router.SetExternalPoseProvider(
                vmcReceiver);
            router.SetExpressionFallbackProvider(
                vmcReceiver);

            var manualExpression =
                tracking.gameObject.AddComponent<
                    ManualExpressionLayerSource>();
            var mixer =
                tracking.gameObject.AddComponent<
                    MotionExpressionMixer>();

            mixer.SetRoutedProvider(
                router);
            mixer.SetExpressionLayerProvider(
                manualExpression);

            loader.SetTrackingProvider(
                mixer);
            diagnostics.SetTrackingProvider(
                mixer);

            character.gameObject.AddComponent<
                MaterialOverrideController>();
            character.gameObject.AddComponent<
                BasicCharacterAppearanceRuntime>();
            character.gameObject.AddComponent<
                AppearanceTransitionActionExecutor>();

            var eventRuntime =
                eventsRoot.gameObject.AddComponent<
                    EventRuntimeHost>();
            eventRuntime.SetEventHub(
                eventHub);

            var handlers =
                new List<MonoBehaviour>
                {
                    eventsRoot.gameObject.AddComponent<
                        AppearanceEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        AudioEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        CameraFieldOfViewEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        EffectEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        EnvironmentStateEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        ExpressionEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        MaterialFloatEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        MaterialPresetEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        MaterialPropertyEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        MotionCueEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        MotionPoseWeightEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        PropEventActionHandler>(),
                    eventsRoot.gameObject.AddComponent<
                        SceneSequenceEventActionHandler>()
                };

            eventRuntime.SetActionHandlers(
                handlers.ToArray());

            root.AddComponent<
                DesktopCharacterFileSelectionAdapter>();
            root.AddComponent<
                ApplicationUiController>();

            // Inert during normal launches; activated only by the explicit
            // --vcr-smoke-report=... standalone CI command-line option.
            root.AddComponent<PlayerStartupSmoke>();

            var presentation2D =
                new GameObject(
                    "Presentation2D");
            presentation2D.transform.SetParent(
                root.transform,
                false);

            var character2D =
                presentation2D.AddComponent<
                    Character2DRuntime>();
            character2D.Configure(
                backend: null,
                trackingProvider: mixer,
                inputs:
                    Character2DInputDomain.Face |
                    Character2DInputDomain.Expressions);
            character2D.enabled = false;

            var scene =
                EditorSceneManager.GetActiveScene();

            EditorSceneManager.MarkSceneDirty(
                scene);

            if (!EditorSceneManager.SaveScene(
                    scene,
                    ScenePath))
            {
                Debug.LogError(
                    "VCR Alpha: failed to save integrated runtime scene.");
                return false;
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "VCR Alpha integrated runtime scene created. " +
                "All current runtime feature families remain included; " +
                "MediaPipe webcam, ARKit/iFacialMocap, Ultraleap hands, VMC receive/send, and 2D host start disabled for equipment-free alpha validation.");

            return true;
        }
    }
}
