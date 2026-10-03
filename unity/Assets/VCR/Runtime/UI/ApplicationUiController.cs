using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VCR.Runtime.Application;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Materials.Unity;
using VCR.Runtime.Output;
using VCR.Runtime.Scene;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40000)]
    public sealed class ApplicationUiController :
        MonoBehaviour
    {
        [Header("Runtime")]
        [SerializeField] private ApplicationRuntimeBootstrap applicationBootstrap;
        [SerializeField] private SingleCharacterSceneRuntime sceneRuntime;
        [SerializeField] private RuntimeDiagnostics diagnostics;
        [SerializeField] private EventRuntimeHost eventRuntime;

        [Header("UI")]
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 0.5f;
        [SerializeField] private Font uiFont;

        private readonly ApplicationUiModel _model =
            new();

        private readonly Dictionary<ApplicationUiSection, Button>
            _sectionButtons =
                new();

        private Canvas _canvas;
        private RectTransform _root;
        private Text _statusText;
        private Text _sectionTitle;
        private Text _contentText;
        private Button _saveButton;
        private Button _recoverOutputButton;

        private ITrackingPresenceProvider _trackingPresence;
        private MotionExpressionMixer _mixer;
        private MaterialOverrideController _materialController;

        private float _nextRefreshTime;
        private string _lastActionMessage;

        public ApplicationUiModel Model => _model;

        private void Awake()
        {
            ResolveDependencies();
            RefreshAvailability();

            if (buildOnAwake)
            {
                BuildUi();
            }

            RefreshAll();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (Time.unscaledTime <
                _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime =
                Time.unscaledTime +
                Mathf.Max(
                    0.25f,
                    refreshIntervalSeconds);

            ResolveDependencies();
            RefreshAvailability();
            RefreshAll();
        }

        public bool SelectSection(
            ApplicationUiSection section)
        {
            var selected =
                _model.TrySelect(section);

            if (selected)
            {
                RefreshAll();
            }

            return selected;
        }

        [ContextMenu("Rebuild Application UI")]
        public void RebuildUi()
        {
            if (_root != null)
            {
                DestroyObject(
                    _root.gameObject);
            }

            _sectionButtons.Clear();
            _root = null;
            _statusText = null;
            _sectionTitle = null;
            _contentText = null;
            _saveButton = null;
            _recoverOutputButton = null;

            BuildUi();
            RefreshAll();
        }

        private void ResolveDependencies()
        {
            applicationBootstrap ??=
                FindFirstObjectByType<
                    ApplicationRuntimeBootstrap>(
                    FindObjectsInactive.Exclude);

            sceneRuntime ??=
                applicationBootstrap?.SceneRuntime ??
                FindFirstObjectByType<
                    SingleCharacterSceneRuntime>(
                    FindObjectsInactive.Exclude);

            diagnostics ??=
                FindFirstObjectByType<
                    RuntimeDiagnostics>(
                    FindObjectsInactive.Exclude);

            eventRuntime ??=
                FindFirstObjectByType<
                    EventRuntimeHost>(
                    FindObjectsInactive.Exclude);

            _mixer ??=
                FindFirstObjectByType<
                    MotionExpressionMixer>(
                    FindObjectsInactive.Exclude);

            _materialController ??=
                FindFirstObjectByType<
                    MaterialOverrideController>(
                    FindObjectsInactive.Exclude);

            if (_trackingPresence == null)
            {
                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                ITrackingPresenceProvider direct = null;

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is
                        ITrackingMixProvider mix)
                    {
                        _trackingPresence = mix;
                        break;
                    }

                    if (direct == null &&
                        behaviour is
                            ITrackingPresenceProvider presence)
                    {
                        direct = presence;
                    }
                }

                _trackingPresence ??=
                    direct;
            }
        }

        private void RefreshAvailability()
        {
            _model.SetAvailability(
                ApplicationUiSection.Character,
                sceneRuntime != null,
                "Scene runtime is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Tracking,
                _trackingPresence != null,
                "No tracking presence provider is active.");

            _model.SetAvailability(
                ApplicationUiSection.MotionExpression,
                _mixer != null,
                "Motion/expression mixer is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Environment,
                sceneRuntime?.EnvironmentRuntime != null,
                "Environment runtime is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.MaterialShader,
                _materialController != null,
                "Material override controller is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Events,
                eventRuntime != null,
                "Event runtime is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.CameraOutput,
                sceneRuntime != null,
                "Scene/output runtime is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Settings,
                applicationBootstrap != null,
                "Application bootstrap is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Diagnostics,
                diagnostics != null,
                "Runtime diagnostics are unavailable.");
        }

        private void Subscribe()
        {
            if (sceneRuntime != null)
            {
                sceneRuntime.StatusChanged -=
                    OnSceneStatusChanged;
                sceneRuntime.StatusChanged +=
                    OnSceneStatusChanged;
            }

            if (diagnostics != null)
            {
                diagnostics.SnapshotUpdated -=
                    OnDiagnosticsUpdated;
                diagnostics.SnapshotUpdated +=
                    OnDiagnosticsUpdated;
            }
        }

        private void Unsubscribe()
        {
            if (sceneRuntime != null)
            {
                sceneRuntime.StatusChanged -=
                    OnSceneStatusChanged;
            }

            if (diagnostics != null)
            {
                diagnostics.SnapshotUpdated -=
                    OnDiagnosticsUpdated;
            }
        }

        private void OnSceneStatusChanged(
            SceneRuntimeStatus status)
        {
            RefreshAll();
        }

        private void OnDiagnosticsUpdated(
            RuntimeDiagnosticsSnapshot snapshot)
        {
            if (_model.SelectedSection ==
                    ApplicationUiSection.Diagnostics ||
                _statusText != null)
            {
                RefreshAll();
            }
        }

        private void BuildUi()
        {
            EnsureEventSystem();

            _canvas =
                GetComponent<Canvas>() ??
                gameObject.AddComponent<Canvas>();

            _canvas.renderMode =
                RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder =
                1000;

            var scaler =
                GetComponent<CanvasScaler>() ??
                gameObject.AddComponent<
                    CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode
                    .ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(
                    1920f,
                    1080f);
            scaler.matchWidthOrHeight =
                0.5f;

            if (GetComponent<
                    GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<
                    GraphicRaycaster>();
            }

            uiFont ??=
                Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");

            _root =
                CreateRect(
                    "VCR Application UI",
                    transform);

            Stretch(
                _root,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            var background =
                _root.gameObject
                    .AddComponent<Image>();
            background.color =
                new Color(
                    0.055f,
                    0.06f,
                    0.07f,
                    0.96f);

            var top =
                CreateRect(
                    "Status Bar",
                    _root);

            AnchorTop(
                top,
                56f,
                left: 0f,
                right: 0f);

            var topImage =
                top.gameObject
                    .AddComponent<Image>();
            topImage.color =
                new Color(
                    0.09f,
                    0.10f,
                    0.12f,
                    1f);

            _statusText =
                CreateText(
                    "Status",
                    top,
                    18,
                    TextAnchor.MiddleLeft);

            Stretch(
                _statusText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(18f, 0f),
                new Vector2(-18f, 0f));

            var navigation =
                CreateRect(
                    "Navigation",
                    _root);

            navigation.anchorMin =
                new Vector2(0f, 0f);
            navigation.anchorMax =
                new Vector2(0f, 1f);
            navigation.pivot =
                new Vector2(0f, 1f);
            navigation.offsetMin =
                new Vector2(0f, 0f);
            navigation.offsetMax =
                new Vector2(248f, -56f);

            var navImage =
                navigation.gameObject
                    .AddComponent<Image>();
            navImage.color =
                new Color(
                    0.075f,
                    0.08f,
                    0.095f,
                    1f);

            var navLayout =
                navigation.gameObject
                    .AddComponent<
                        VerticalLayoutGroup>();
            navLayout.padding =
                new RectOffset(
                    10,
                    10,
                    12,
                    12);
            navLayout.spacing = 7f;
            navLayout.childControlHeight = true;
            navLayout.childForceExpandHeight = false;

            foreach (var state in
                     _model.CaptureSections())
            {
                var section =
                    state.Section;

                var button =
                    CreateButton(
                        state.Title,
                        navigation,
                        () =>
                        {
                            SelectSection(section);
                        });

                var layout =
                    button.gameObject
                        .AddComponent<
                            LayoutElement>();
                layout.preferredHeight = 42f;

                _sectionButtons[section] =
                    button;
            }

            var content =
                CreateRect(
                    "Content",
                    _root);

            content.anchorMin =
                Vector2.zero;
            content.anchorMax =
                Vector2.one;
            content.offsetMin =
                new Vector2(
                    248f,
                    0f);
            content.offsetMax =
                new Vector2(
                    0f,
                    -56f);

            var contentImage =
                content.gameObject
                    .AddComponent<Image>();
            contentImage.color =
                new Color(
                    0.045f,
                    0.05f,
                    0.06f,
                    0.96f);

            _sectionTitle =
                CreateText(
                    "Section Title",
                    content,
                    28,
                    TextAnchor.UpperLeft);

            _sectionTitle.rectTransform
                .anchorMin =
                    new Vector2(0f, 1f);
            _sectionTitle.rectTransform
                .anchorMax =
                    new Vector2(1f, 1f);
            _sectionTitle.rectTransform
                .pivot =
                    new Vector2(0.5f, 1f);
            _sectionTitle.rectTransform
                .offsetMin =
                    new Vector2(24f, -76f);
            _sectionTitle.rectTransform
                .offsetMax =
                    new Vector2(-24f, -18f);

            _contentText =
                CreateText(
                    "Content Text",
                    content,
                    18,
                    TextAnchor.UpperLeft);

            _contentText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _contentText.verticalOverflow =
                VerticalWrapMode.Overflow;

            _contentText.rectTransform
                .anchorMin =
                    new Vector2(0f, 0f);
            _contentText.rectTransform
                .anchorMax =
                    new Vector2(1f, 1f);
            _contentText.rectTransform
                .offsetMin =
                    new Vector2(24f, 78f);
            _contentText.rectTransform
                .offsetMax =
                    new Vector2(-24f, -84f);

            var actions =
                CreateRect(
                    "Actions",
                    content);

            actions.anchorMin =
                new Vector2(0f, 0f);
            actions.anchorMax =
                new Vector2(1f, 0f);
            actions.pivot =
                new Vector2(0.5f, 0f);
            actions.offsetMin =
                new Vector2(24f, 18f);
            actions.offsetMax =
                new Vector2(-24f, 62f);

            var actionLayout =
                actions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            actionLayout.spacing = 10f;
            actionLayout.childForceExpandWidth = false;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;

            _saveButton =
                CreateButton(
                    "Save Configuration",
                    actions,
                    SaveConfiguration);

            var saveLayout =
                _saveButton.gameObject
                    .AddComponent<
                        LayoutElement>();
            saveLayout.preferredWidth = 180f;

            _recoverOutputButton =
                CreateButton(
                    "Recover Output",
                    actions,
                    RecoverOutput);

            var recoveryLayout =
                _recoverOutputButton
                    .gameObject
                    .AddComponent<
                        LayoutElement>();
            recoveryLayout.preferredWidth = 160f;
        }

        private void SaveConfiguration()
        {
            if (applicationBootstrap == null)
            {
                _lastActionMessage =
                    "Configuration save unavailable.";
                RefreshAll();
                return;
            }

            if (applicationBootstrap
                .SaveConfiguration(
                    out var error))
            {
                _lastActionMessage =
                    "Configuration saved.";
            }
            else
            {
                _lastActionMessage =
                    "Save failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void RecoverOutput()
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Output recovery unavailable.";
                RefreshAll();
                return;
            }

            if (sceneRuntime
                .TryRecoverOverlayOutput(
                    out var error))
            {
                _lastActionMessage =
                    "Output recovery requested.";
            }
            else
            {
                _lastActionMessage =
                    "Output recovery failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_root == null)
            {
                return;
            }

            foreach (var state in
                     _model.CaptureSections())
            {
                if (!_sectionButtons.TryGetValue(
                        state.Section,
                        out var button) ||
                    button == null)
                {
                    continue;
                }

                button.interactable =
                    state.Available;

                if (button.GetComponentInChildren<Text>()
                    is Text label)
                {
                    label.text =
                        state.Available
                            ? state.Title
                            : state.Title +
                              "  — unavailable";
                }
            }

            if (_saveButton != null)
            {
                _saveButton.interactable =
                    applicationBootstrap != null;
            }

            if (_recoverOutputButton != null)
            {
                _recoverOutputButton.interactable =
                    sceneRuntime?.OverlayOutput != null;
            }

            RefreshStatus();
            RefreshContent();
        }

        private void RefreshStatus()
        {
            if (_statusText == null)
            {
                return;
            }

            var sceneStatus =
                sceneRuntime?.Status;

            var sceneText =
                sceneStatus.HasValue
                    ? sceneStatus.Value.State
                        .ToString()
                    : "No Scene Runtime";

            var characterText =
                sceneStatus.HasValue &&
                sceneStatus.Value.HasCharacter
                    ? "Character: loaded"
                    : "Character: none";

            var outputText =
                sceneRuntime?.OverlayOutput != null
                    ? "Output: " +
                      sceneRuntime.OverlayOutput
                          .Status.State
                    : "Output: none";

            var suffix =
                string.IsNullOrWhiteSpace(
                    _lastActionMessage)
                    ? string.Empty
                    : "  |  " +
                      _lastActionMessage;

            _statusText.text =
                $"VCR  |  {sceneText}  |  {characterText}  |  {outputText}{suffix}";
        }

        private void RefreshContent()
        {
            if (_sectionTitle == null ||
                _contentText == null)
            {
                return;
            }

            var selected =
                _model.SelectedSection;

            _sectionTitle.text =
                ApplicationUiModel
                    .GetTitle(selected);

            if (!_model.IsAvailable(
                    selected))
            {
                _contentText.text =
                    _model.GetUnavailableReason(
                        selected) ??
                    "Section unavailable.";
                return;
            }

            _contentText.text =
                selected switch
                {
                    ApplicationUiSection.Character =>
                        CharacterSummary(),
                    ApplicationUiSection.Tracking =>
                        TrackingSummary(),
                    ApplicationUiSection.MotionExpression =>
                        MotionSummary(),
                    ApplicationUiSection.Environment =>
                        EnvironmentSummary(),
                    ApplicationUiSection.MaterialShader =>
                        MaterialSummary(),
                    ApplicationUiSection.Events =>
                        EventsSummary(),
                    ApplicationUiSection.CameraOutput =>
                        OutputSummary(),
                    ApplicationUiSection.Settings =>
                        SettingsSummary(),
                    ApplicationUiSection.Diagnostics =>
                        DiagnosticsSummary(),
                    _ =>
                        "No section content."
                };
        }

        private string CharacterSummary()
        {
            if (sceneRuntime == null)
            {
                return "Scene runtime unavailable.";
            }

            var status =
                sceneRuntime.Status;

            return
                $"Runtime state: {status.State}\n" +
                $"Character loaded: {status.HasCharacter}\n" +
                $"Model path: {status.CurrentCharacterPath ?? "<none>"}\n" +
                $"Last runtime error: {status.LastError ?? "<none>"}";
        }

        private string TrackingSummary()
        {
            if (_trackingPresence == null)
            {
                return "Tracking provider unavailable.";
            }

            var presence =
                _trackingPresence.Presence;

            return
                $"Subject: {presence.SubjectState}\n" +
                $"Any source available: {presence.AnySourceAvailable}\n" +
                $"Face source: {presence.FaceSourceAvailable}\n" +
                $"Body/hands source: {presence.BodyHandsSourceAvailable}\n" +
                $"Full-body source: {presence.FullBodySourceAvailable}\n" +
                $"Events: {presence.Events}";
        }

        private string MotionSummary()
        {
            return _mixer == null
                ? "Motion/expression mixer unavailable."
                : "MotionExpressionMixer is active. " +
                  "Detailed layer controls are added in later P11 slices.";
        }

        private string EnvironmentSummary()
        {
            var runtime =
                sceneRuntime?.EnvironmentRuntime;

            if (runtime == null)
            {
                return "Environment runtime unavailable.";
            }

            var status =
                runtime.Status;

            return
                $"Environment: {status.EnvironmentId ?? "<none>"}\n" +
                $"State: {status.StateId ?? "<none>"}\n" +
                $"Space: {runtime.SpaceMode}\n" +
                $"Active: {status.Active}\n" +
                $"Error: {status.Error ?? "<none>"}";
        }

        private string MaterialSummary()
        {
            if (_materialController == null)
            {
                return "Material controller unavailable.";
            }

            return
                $"Material slots: {_materialController.SlotCount}\n" +
                $"Override errors: {_materialController.ErrorCount}\n" +
                "Detailed slot/preset controls are added in later P11 slices.";
        }

        private string EventsSummary()
        {
            if (eventRuntime == null)
            {
                return "Event runtime unavailable.";
            }

            return
                $"Processed events: {eventRuntime.Engine.ProcessedEvents}\n" +
                $"Matched rules: {eventRuntime.Engine.MatchedRules}\n" +
                $"Executed actions: {eventRuntime.ExecutedActions}\n" +
                $"Failed actions: {eventRuntime.FailedActions}\n" +
                $"Unhandled actions: {eventRuntime.UnhandledActions}\n" +
                $"Ambiguous actions: {eventRuntime.AmbiguousActions}";
        }

        private string OutputSummary()
        {
            if (sceneRuntime == null)
            {
                return "Scene/output runtime unavailable.";
            }

            var output =
                sceneRuntime.OverlayOutput;
            var readiness =
                sceneRuntime.OverlayCaptureReadiness;

            var minimum =
                sceneRuntime
                    .EvaluateBroadcastCaptureTarget(
                        BroadcastCaptureTarget
                            .Minimum720p60);

            var recommended =
                sceneRuntime
                    .EvaluateBroadcastCaptureTarget(
                        BroadcastCaptureTarget
                            .Recommended1080p60);

            var status =
                output?.Status;

            return
                $"Output state: {(status.HasValue ? status.Value.State.ToString() : "none")}\n" +
                $"Transparent: {output?.Settings.Transparent.ToString() ?? "n/a"}\n" +
                $"Topmost: {output?.Settings.Topmost.ToString() ?? "n/a"}\n" +
                $"Click-through: {output?.Settings.ClickThrough.ToString() ?? "n/a"}\n" +
                $"Capture ready: {readiness.Ready}\n" +
                $"Capture readiness: {readiness.Failure}\n" +
                $"720p60 configured: {minimum.Ready}\n" +
                $"1080p60 configured: {recommended.Ready}\n" +
                $"Output error: {status?.LastError ?? "<none>"}";
        }

        private string SettingsSummary()
        {
            if (applicationBootstrap == null)
            {
                return "Application bootstrap unavailable.";
            }

            var capabilities =
                sceneRuntime?.Capabilities;

            return
                $"Runtime started: {applicationBootstrap.IsStarted}\n" +
                $"Configuration: {applicationBootstrap.ConfigurationPath ?? "<default/not resolved>"}\n" +
                $"Capabilities registered: {capabilities?.RegisteredCount ?? 0}\n" +
                $"Capabilities enabled: {capabilities?.EnabledCount ?? 0}";
        }

        private string DiagnosticsSummary()
        {
            if (diagnostics == null)
            {
                return "Runtime diagnostics unavailable.";
            }

            var snapshot =
                diagnostics.LatestSnapshot;

            if (snapshot.Sequence <= 0)
            {
                return "Waiting for the first diagnostics report.";
            }

            return
                $"Frame average: {snapshot.FrameAverageMs:F2} ms\n" +
                $"Frame P95: {snapshot.FrameP95Ms:F2} ms\n" +
                $"Frame P99: {snapshot.FrameP99Ms:F2} ms\n" +
                $"Face updates: {snapshot.FaceHz:F1} Hz\n" +
                $"Body/hands updates: {snapshot.BodyHandsHz:F1} Hz\n" +
                $"Full-body updates: {snapshot.FullBodyHz:F1} Hz\n" +
                $"Expression updates: {snapshot.ExpressionHz:F1} Hz";
        }

        private Button CreateButton(
            string label,
            Transform parent,
            Action onClick)
        {
            var rect =
                CreateRect(
                    label + " Button",
                    parent);

            var image =
                rect.gameObject
                    .AddComponent<Image>();
            image.color =
                new Color(
                    0.14f,
                    0.16f,
                    0.19f,
                    1f);

            var button =
                rect.gameObject
                    .AddComponent<Button>();
            button.targetGraphic = image;

            if (onClick != null)
            {
                button.onClick.AddListener(
                    () => onClick());
            }

            var text =
                CreateText(
                    "Label",
                    rect,
                    16,
                    TextAnchor.MiddleCenter);
            text.text = label;

            Stretch(
                text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 2f),
                new Vector2(-8f, -2f));

            return button;
        }

        private Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            TextAnchor alignment)
        {
            var rect =
                CreateRect(
                    name,
                    parent);

            var text =
                rect.gameObject
                    .AddComponent<Text>();

            text.font = uiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color =
                new Color(
                    0.92f,
                    0.94f,
                    0.97f,
                    1f);
            text.raycastTarget = false;

            return text;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent)
        {
            var gameObject =
                new GameObject(
                    name,
                    typeof(RectTransform));

            var rect =
                gameObject.GetComponent<
                    RectTransform>();
            rect.SetParent(
                parent,
                false);
            return rect;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void AnchorTop(
            RectTransform rect,
            float height,
            float left,
            float right)
        {
            rect.anchorMin =
                new Vector2(0f, 1f);
            rect.anchorMax =
                new Vector2(1f, 1f);
            rect.pivot =
                new Vector2(0.5f, 1f);
            rect.offsetMin =
                new Vector2(left, -height);
            rect.offsetMax =
                new Vector2(-right, 0f);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<
                    EventSystem>(
                    FindObjectsInactive.Exclude) !=
                null)
            {
                return;
            }

            var eventSystem =
                new GameObject(
                    "VCR EventSystem");

            eventSystem.AddComponent<
                EventSystem>();
            eventSystem.AddComponent<
                StandaloneInputModule>();
        }

        private static void DestroyObject(
            UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
