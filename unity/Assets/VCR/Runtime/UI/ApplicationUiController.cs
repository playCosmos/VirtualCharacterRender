using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VCR.Runtime.Application;
using VCR.Runtime.Appearance;
using VCR.Runtime.Capabilities;
using VCR.Runtime.Core;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Environment;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Materials.Unity;
using VCR.Runtime.Output;
using VCR.Runtime.Rendering;
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
        [SerializeField, Min(0.5f)] private float dependencyResolveIntervalSeconds = 2f;
        [SerializeField] private Font uiFont;

        private readonly ApplicationUiModel _model =
            new();

        private readonly Dictionary<ApplicationUiSection, Button>
            _sectionButtons =
                new();

        private readonly Dictionary<ApplicationUiSection, Text>
            _sectionLabels =
                new();

        private readonly Dictionary<Button, Text>
            _buttonLabels =
                new();

        private readonly List<ITrackingRuntimeControl>
            _trackingControls =
                new();

        private readonly StringBuilder _summaryBuilder =
            new(768);

        private RuntimeMetric[] _diagnosticsMetricSource;
        private RuntimeMetric[] _diagnosticsSortedMetrics =
            Array.Empty<RuntimeMetric>();

        private Canvas _canvas;
        private RectTransform _root;
        private Text _statusText;
        private Text _sectionTitle;
        private Text _contentText;
        private Button _saveButton;
        private Button _recoverOutputButton;

        private RectTransform _contextActions;
        private RectTransform _appearanceActions;
        private RectTransform _appearanceDirectActions;
        private RectTransform _appearancePersistenceActions;
        private RectTransform _appearancePresetManagementActions;
        private InputField _characterPathInput;
        private Button _characterBrowseButton;
        private Button _loadCharacterButton;
        private Button _reloadCharacterButton;
        private Button _unloadCharacterButton;
        private Button _apply720p60Button;
        private Button _apply1080p60Button;
        private Button _outputTransparentButton;
        private Button _outputTopmostButton;
        private Button _outputClickThroughButton;
        private Text _motionPoseWeightLabel;
        private Slider _motionPoseWeightSlider;
        private InputField _manualExpressionNameInput;
        private InputField _manualExpressionValueInput;
        private Button _manualExpressionApplyButton;
        private Button _manualExpressionClearButton;
        private Button _manualExpressionClearAllButton;
        private InputField _environmentStateInput;
        private Button _environmentTransitionModeButton;
        private InputField _environmentTransitionDurationInput;
        private Button _environmentApplyStateButton;
        private Button _materialPreviousSlotButton;
        private Button _materialNextSlotButton;
        private InputField _materialSlotInput;
        private InputField _materialShaderInput;
        private Button _materialApplyShaderButton;
        private InputField _materialPropertyInput;
        private InputField _materialValueInput;
        private Button _materialSetFloatButton;
        private Button _materialClearOverrideButton;
        private Button _materialRefreshSlotsButton;
        private Button _eventPreviousRuleButton;
        private Button _eventNextRuleButton;
        private InputField _eventRuleInput;
        private Button _eventToggleRuleButton;
        private Button _eventTraceButton;
        private InputField _eventMaxCommandsInput;
        private Button _eventApplyMaxCommandsButton;
        private Button _eventSaveRulesButton;
        private Button _eventReloadRulesButton;
        private Button _settingsPreviousCapabilityButton;
        private Button _settingsNextCapabilityButton;
        private Button _settingsToggleCapabilityButton;
        private InputField _settingsRenderScaleInput;
        private Button _settingsApplyRenderScaleButton;
        private InputField _settingsFpsInput;
        private Button _settingsApplyFpsButton;
        private Button _settingsVsyncButton;
        private Button _settingsRunInBackgroundButton;
        private Button _diagnosticsPreviousPageButton;
        private Button _diagnosticsNextPageButton;
        private Button _diagnosticsCaptureButton;
        private Button _diagnosticsSaveSnapshotButton;
        private Button _diagnosticsCsvButton;
        private Button _diagnosticsConsoleButton;
        private Button _trackingPreviousButton;
        private Button _trackingToggleButton;
        private Button _trackingRecoverButton;
        private Button _trackingNextButton;
        private Button _appearancePreviousButton;
        private Button _appearanceNextButton;
        private Button _appearanceTransitionButton;
        private Button _appearanceRestoreButton;
        private Button _appearancePreviewButton;
        private Button _appearanceCancelButton;
        private InputField _appearancePresetInput;
        private Button _appearanceApplyPresetButton;
        private InputField _appearanceOutfitInput;
        private Button _appearanceApplyOutfitButton;
        private InputField _appearanceAccessorySlotInput;
        private InputField _appearanceAccessoryInput;
        private Button _appearanceSetAccessoryButton;
        private Button _appearanceClearAccessoryButton;
        private InputField _appearanceUserPresetInput;
        private InputField _appearanceUserPresetTargetInput;
        private Button _appearanceSaveUserPresetButton;
        private Button _appearanceDeleteUserPresetButton;
        private Button _appearanceRenameUserPresetButton;
        private Button _appearanceDuplicateUserPresetButton;
        private Button _appearanceMoveUserPresetUpButton;
        private Button _appearanceMoveUserPresetDownButton;

        private ITrackingPresenceProvider _trackingPresence;
        private ICharacterFileSelectionAdapter _characterFileSelectionAdapter;
        private IAppearanceRuntime _appearanceRuntime;
        private AppearanceUserPresetStore _appearancePresetStore;
        private EventRuntimeConfigurationStore _eventRuleStore;
        private string _eventRuleStorePath;
        private EventRuntimeHost _loadedEventRuleHost;
        private bool _eventRuleStoreChecked;
        private IAppearanceUserPresetRegistry _loadedAppearancePresetRegistry;
        private string _loadedAppearanceProfilePath;
        private MotionExpressionMixer _mixer;
        private ManualExpressionLayerSource _manualExpressionSource;
        private MaterialOverrideController _materialController;

        private SingleCharacterSceneRuntime _subscribedSceneRuntime;
        private RuntimeDiagnostics _subscribedDiagnostics;
        private IAppearanceRuntime _subscribedAppearanceRuntime;
        private AppearanceStateSnapshot _currentAppearanceSnapshot;
        private bool _hasCurrentAppearanceSnapshot;

        private float _nextRefreshTime;
        private float _nextDependencyResolveTime;
        private string _lastActionMessage;
        private int _trackingControlIndex;
        private int _appearanceTransitionIndex;
        private EnvironmentTransitionMode _environmentTransitionMode =
            EnvironmentTransitionMode.Cut;
        private int _materialSlotIndex;
        private int _eventRuleIndex;
        private int _settingsCapabilityIndex;
        private int _diagnosticsMetricPage;

        public ApplicationUiModel Model => _model;

        private void Awake()
        {
            ResolveDependencies(
                force: true);
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
            _sectionLabels.Clear();
            _buttonLabels.Clear();
            _root = null;
            _statusText = null;
            _sectionTitle = null;
            _contentText = null;
            _saveButton = null;
            _recoverOutputButton = null;
            _contextActions = null;
            _appearanceActions = null;
            _appearanceDirectActions = null;
            _appearancePersistenceActions = null;
            _appearancePresetManagementActions = null;
            _characterPathInput = null;
            _characterBrowseButton = null;
            _loadCharacterButton = null;
            _reloadCharacterButton = null;
            _unloadCharacterButton = null;
            _apply720p60Button = null;
            _apply1080p60Button = null;
            _outputTransparentButton = null;
            _outputTopmostButton = null;
            _outputClickThroughButton = null;
            _motionPoseWeightLabel = null;
            _motionPoseWeightSlider = null;
            _manualExpressionNameInput = null;
            _manualExpressionValueInput = null;
            _manualExpressionApplyButton = null;
            _manualExpressionClearButton = null;
            _manualExpressionClearAllButton = null;
            _environmentStateInput = null;
            _environmentTransitionModeButton = null;
            _environmentTransitionDurationInput = null;
            _environmentApplyStateButton = null;
            _materialPreviousSlotButton = null;
            _materialNextSlotButton = null;
            _materialSlotInput = null;
            _materialShaderInput = null;
            _materialApplyShaderButton = null;
            _materialPropertyInput = null;
            _materialValueInput = null;
            _materialSetFloatButton = null;
            _materialClearOverrideButton = null;
            _materialRefreshSlotsButton = null;
            _eventPreviousRuleButton = null;
            _eventNextRuleButton = null;
            _eventRuleInput = null;
            _eventToggleRuleButton = null;
            _eventTraceButton = null;
            _eventMaxCommandsInput = null;
            _eventApplyMaxCommandsButton = null;
            _eventSaveRulesButton = null;
            _eventReloadRulesButton = null;
            _settingsPreviousCapabilityButton = null;
            _settingsNextCapabilityButton = null;
            _settingsToggleCapabilityButton = null;
            _settingsRenderScaleInput = null;
            _settingsApplyRenderScaleButton = null;
            _settingsFpsInput = null;
            _settingsApplyFpsButton = null;
            _settingsVsyncButton = null;
            _settingsRunInBackgroundButton = null;
            _diagnosticsPreviousPageButton = null;
            _diagnosticsNextPageButton = null;
            _diagnosticsCaptureButton = null;
            _diagnosticsSaveSnapshotButton = null;
            _diagnosticsCsvButton = null;
            _diagnosticsConsoleButton = null;
            _trackingPreviousButton = null;
            _trackingToggleButton = null;
            _trackingRecoverButton = null;
            _trackingNextButton = null;
            _appearancePreviousButton = null;
            _appearanceNextButton = null;
            _appearanceTransitionButton = null;
            _appearanceRestoreButton = null;
            _appearancePreviewButton = null;
            _appearanceCancelButton = null;
            _appearancePresetInput = null;
            _appearanceApplyPresetButton = null;
            _appearanceOutfitInput = null;
            _appearanceApplyOutfitButton = null;
            _appearanceAccessorySlotInput = null;
            _appearanceAccessoryInput = null;
            _appearanceSetAccessoryButton = null;
            _appearanceClearAccessoryButton = null;
            _appearanceUserPresetInput = null;
            _appearanceUserPresetTargetInput = null;
            _appearanceSaveUserPresetButton = null;
            _appearanceDeleteUserPresetButton = null;
            _appearanceRenameUserPresetButton = null;
            _appearanceDuplicateUserPresetButton = null;
            _appearanceMoveUserPresetUpButton = null;
            _appearanceMoveUserPresetDownButton = null;

            BuildUi();
            RefreshAll();
        }

        private void ResolveDependencies(
            bool force = false)
        {
            var now =
                Time.unscaledTime;
            var resolveMissing =
                force ||
                now >=
                _nextDependencyResolveTime;

            if (resolveMissing)
            {
                _nextDependencyResolveTime =
                    now +
                    Mathf.Max(
                        0.5f,
                        dependencyResolveIntervalSeconds);

                if (applicationBootstrap == null)
                {
                    applicationBootstrap =
                        FindFirstObjectByType<
                            ApplicationRuntimeBootstrap>(
                            FindObjectsInactive.Exclude);
                }

                if (sceneRuntime == null)
                {
                    sceneRuntime =
                        applicationBootstrap?.SceneRuntime ??
                        FindFirstObjectByType<
                            SingleCharacterSceneRuntime>(
                            FindObjectsInactive.Exclude);
                }

                if (diagnostics == null)
                {
                    diagnostics =
                        FindFirstObjectByType<
                            RuntimeDiagnostics>(
                            FindObjectsInactive.Exclude);
                }

                if (eventRuntime == null)
                {
                    eventRuntime =
                        FindFirstObjectByType<
                            EventRuntimeHost>(
                            FindObjectsInactive.Exclude);
                }

                if (_mixer == null)
                {
                    _mixer =
                        FindFirstObjectByType<
                            MotionExpressionMixer>(
                            FindObjectsInactive.Exclude);
                }

                if (_manualExpressionSource == null)
                {
                    _manualExpressionSource =
                        FindFirstObjectByType<
                            ManualExpressionLayerSource>(
                            FindObjectsInactive.Exclude);
                }

                if (_materialController == null)
                {
                    _materialController =
                        FindFirstObjectByType<
                            MaterialOverrideController>(
                            FindObjectsInactive.Exclude);
                }

                ResolveTrackingControls();
                ResolveCharacterFileSelectionAdapter();
                ResolveAppearanceRuntime();

                if (!IsServiceAlive(_trackingPresence))
                {
                    _trackingPresence = null;

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

            if (isActiveAndEnabled)
            {
                RebindSubscriptions();
            }

            EnsureAppearanceUserPresetsLoaded();
            EnsureEventRulesLoaded();
        }

        private void RefreshAvailability()
        {
            _model.SetAvailability(
                ApplicationUiSection.Character,
                sceneRuntime != null,
                "Scene runtime is unavailable.");

            _model.SetAvailability(
                ApplicationUiSection.Tracking,
                IsServiceAlive(_trackingPresence) ||
                _trackingControls.Count > 0,
                "No tracking runtime is configured.");

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
            RebindSubscriptions();
        }

        private void RebindSubscriptions()
        {
            if (!ReferenceEquals(
                    _subscribedSceneRuntime,
                    sceneRuntime))
            {
                if (_subscribedSceneRuntime != null)
                {
                    _subscribedSceneRuntime.StatusChanged -=
                        OnSceneStatusChanged;
                }

                _subscribedSceneRuntime =
                    sceneRuntime;

                if (_subscribedSceneRuntime != null)
                {
                    _subscribedSceneRuntime.StatusChanged +=
                        OnSceneStatusChanged;
                }
            }

            if (!ReferenceEquals(
                    _subscribedDiagnostics,
                    diagnostics))
            {
                if (_subscribedDiagnostics != null)
                {
                    _subscribedDiagnostics.SnapshotUpdated -=
                        OnDiagnosticsUpdated;
                }

                _subscribedDiagnostics =
                    diagnostics;
                _diagnosticsMetricSource = null;
                _diagnosticsSortedMetrics =
                    Array.Empty<RuntimeMetric>();
                _diagnosticsMetricPage = 0;

                if (_subscribedDiagnostics != null)
                {
                    _subscribedDiagnostics.SnapshotUpdated +=
                        OnDiagnosticsUpdated;
                }
            }

            var nextAppearanceRuntime =
                IsServiceAlive(_appearanceRuntime)
                    ? _appearanceRuntime
                    : null;

            if (!ReferenceEquals(
                    _subscribedAppearanceRuntime,
                    nextAppearanceRuntime))
            {
                if (_subscribedAppearanceRuntime != null)
                {
                    _subscribedAppearanceRuntime.AppearanceChanged -=
                        OnAppearanceChanged;
                }

                _subscribedAppearanceRuntime =
                    nextAppearanceRuntime;
                _hasCurrentAppearanceSnapshot =
                    false;

                if (_subscribedAppearanceRuntime != null)
                {
                    _currentAppearanceSnapshot =
                        _subscribedAppearanceRuntime.Current;
                    _hasCurrentAppearanceSnapshot =
                        true;
                    _subscribedAppearanceRuntime.AppearanceChanged +=
                        OnAppearanceChanged;
                }
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedSceneRuntime != null)
            {
                _subscribedSceneRuntime.StatusChanged -=
                    OnSceneStatusChanged;
            }

            if (_subscribedDiagnostics != null)
            {
                _subscribedDiagnostics.SnapshotUpdated -=
                    OnDiagnosticsUpdated;
            }

            if (_subscribedAppearanceRuntime != null)
            {
                _subscribedAppearanceRuntime.AppearanceChanged -=
                    OnAppearanceChanged;
            }

            _subscribedSceneRuntime = null;
            _subscribedDiagnostics = null;
            _subscribedAppearanceRuntime = null;
            _diagnosticsMetricSource = null;
            _diagnosticsSortedMetrics =
                Array.Empty<RuntimeMetric>();
            _hasCurrentAppearanceSnapshot = false;
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

        private void OnAppearanceChanged(
            AppearanceStateSnapshot snapshot)
        {
            _currentAppearanceSnapshot =
                snapshot;
            _hasCurrentAppearanceSnapshot =
                true;
        }

        private AppearanceStateSnapshot
            GetCurrentAppearanceSnapshot()
        {
            if (_hasCurrentAppearanceSnapshot)
            {
                return _currentAppearanceSnapshot;
            }

            if (!IsServiceAlive(_appearanceRuntime))
            {
                return default;
            }

            _currentAppearanceSnapshot =
                _appearanceRuntime.Current;
            _hasCurrentAppearanceSnapshot =
                true;
            return _currentAppearanceSnapshot;
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

            for (var i = 0;
                 i < (int)ApplicationUiSection.Count;
                 i++)
            {
                var section =
                    (ApplicationUiSection)i;

                var button =
                    CreateButton(
                        ApplicationUiModel.GetTitle(
                            section),
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
                _sectionLabels[section] =
                    button.GetComponentInChildren<Text>();
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
                    new Vector2(24f, 300f);
            _contentText.rectTransform
                .offsetMax =
                    new Vector2(-24f, -84f);

            _contextActions =
                CreateRect(
                    "Section Actions",
                    content);

            _contextActions.anchorMin =
                new Vector2(0f, 0f);
            _contextActions.anchorMax =
                new Vector2(1f, 0f);
            _contextActions.pivot =
                new Vector2(0.5f, 0f);
            _contextActions.offsetMin =
                new Vector2(24f, 72f);
            _contextActions.offsetMax =
                new Vector2(-24f, 122f);

            var contextLayout =
                _contextActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            contextLayout.spacing = 10f;
            contextLayout.childForceExpandWidth = false;
            contextLayout.childControlWidth = true;
            contextLayout.childControlHeight = true;

            _characterPathInput =
                CreateInputField(
                    "Character Path",
                    _contextActions,
                    "VRM path");

            var pathLayout =
                _characterPathInput.gameObject
                    .AddComponent<
                        LayoutElement>();
            pathLayout.preferredWidth = 360f;

            _characterBrowseButton =
                CreateButton(
                    "Browse…",
                    _contextActions,
                    BrowseCharacterFile);
            _characterBrowseButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 100f;

            _loadCharacterButton =
                CreateButton(
                    "Load Character",
                    _contextActions,
                    LoadCharacterFromPath);
            _loadCharacterButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _reloadCharacterButton =
                CreateButton(
                    "Reload Character",
                    _contextActions,
                    ReloadCharacter);
            _reloadCharacterButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 160f;

            _unloadCharacterButton =
                CreateButton(
                    "Unload Character",
                    _contextActions,
                    UnloadCharacter);
            _unloadCharacterButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 160f;

            _trackingPreviousButton =
                CreateButton(
                    "Prev Source",
                    _contextActions,
                    SelectPreviousTrackingControl);
            _trackingPreviousButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _trackingToggleButton =
                CreateButton(
                    "Toggle Tracking",
                    _contextActions,
                    ToggleSelectedTrackingControl);
            _trackingToggleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 220f;

            _trackingRecoverButton =
                CreateButton(
                    "Recover Source",
                    _contextActions,
                    RecoverSelectedTrackingControl);
            _trackingRecoverButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _trackingNextButton =
                CreateButton(
                    "Next Source",
                    _contextActions,
                    SelectNextTrackingControl);
            _trackingNextButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _apply720p60Button =
                CreateButton(
                    "Apply 720p60",
                    _contextActions,
                    Apply720p60);
            _apply720p60Button.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 140f;

            _apply1080p60Button =
                CreateButton(
                    "Apply 1080p60",
                    _contextActions,
                    Apply1080p60);
            _apply1080p60Button.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _outputTransparentButton =
                CreateButton(
                    "Transparent",
                    _contextActions,
                    ToggleOverlayTransparent);
            _outputTransparentButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _outputTopmostButton =
                CreateButton(
                    "Topmost",
                    _contextActions,
                    ToggleOverlayTopmost);
            _outputTopmostButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 135f;

            _outputClickThroughButton =
                CreateButton(
                    "Click-through",
                    _contextActions,
                    ToggleOverlayClickThrough);
            _outputClickThroughButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 165f;

            _motionPoseWeightLabel =
                CreateText(
                    "Pose Weight Label",
                    _contextActions,
                    15,
                    TextAnchor.MiddleLeft);
            _motionPoseWeightLabel.text =
                "Pose Weight";
            _motionPoseWeightLabel.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;

            _motionPoseWeightSlider =
                CreateSlider(
                    "Primary Pose Weight",
                    _contextActions,
                    0f,
                    1f,
                    1f,
                    SetPrimaryPoseLayerWeight);
            _motionPoseWeightSlider.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 180f;

            _manualExpressionNameInput =
                CreateInputField(
                    "Manual Expression Name",
                    _contextActions,
                    "Expression");
            _manualExpressionNameInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 135f;

            _manualExpressionValueInput =
                CreateInputField(
                    "Manual Expression Value",
                    _contextActions,
                    "0..1");
            _manualExpressionValueInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _manualExpressionApplyButton =
                CreateButton(
                    "Apply",
                    _contextActions,
                    ApplyManualExpression);
            _manualExpressionApplyButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _manualExpressionClearButton =
                CreateButton(
                    "Clear",
                    _contextActions,
                    ClearManualExpression);
            _manualExpressionClearButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _manualExpressionClearAllButton =
                CreateButton(
                    "Clear All",
                    _contextActions,
                    ClearAllManualExpressions);
            _manualExpressionClearAllButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 95f;

            _environmentStateInput =
                CreateInputField(
                    "Environment State",
                    _contextActions,
                    "State ID");
            _environmentStateInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 220f;

            _environmentTransitionModeButton =
                CreateButton(
                    "Transition: Cut",
                    _contextActions,
                    SelectNextEnvironmentTransitionMode);
            _environmentTransitionModeButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 190f;

            _environmentTransitionDurationInput =
                CreateInputField(
                    "Environment Transition Duration",
                    _contextActions,
                    "Duration s");
            _environmentTransitionDurationInput.text =
                "0";
            _environmentTransitionDurationInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;

            _environmentApplyStateButton =
                CreateButton(
                    "Apply State",
                    _contextActions,
                    ApplyEnvironmentState);
            _environmentApplyStateButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _materialPreviousSlotButton =
                CreateButton(
                    "Prev Slot",
                    _contextActions,
                    SelectPreviousMaterialSlot);
            _materialPreviousSlotButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 90f;

            _materialNextSlotButton =
                CreateButton(
                    "Next Slot",
                    _contextActions,
                    SelectNextMaterialSlot);
            _materialNextSlotButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 90f;

            _materialSlotInput =
                CreateInputField(
                    "Material Slot Id",
                    _contextActions,
                    "Slot ID");
            _materialSlotInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 190f;

            _materialShaderInput =
                CreateInputField(
                    "Material Shader Id",
                    _contextActions,
                    "Shader ID");
            _materialShaderInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _materialApplyShaderButton =
                CreateButton(
                    "Apply Shader",
                    _contextActions,
                    ApplyMaterialShader);
            _materialApplyShaderButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 115f;

            _materialPropertyInput =
                CreateInputField(
                    "Material Float Property",
                    _contextActions,
                    "Float property");
            _materialPropertyInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 130f;

            _materialValueInput =
                CreateInputField(
                    "Material Float Value",
                    _contextActions,
                    "Value");
            _materialValueInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _materialSetFloatButton =
                CreateButton(
                    "Set Float",
                    _contextActions,
                    SetMaterialFloat);
            _materialSetFloatButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 95f;

            _materialClearOverrideButton =
                CreateButton(
                    "Clear",
                    _contextActions,
                    ClearMaterialOverride);
            _materialClearOverrideButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 75f;

            _materialRefreshSlotsButton =
                CreateButton(
                    "Refresh Slots",
                    _contextActions,
                    RefreshMaterialSlots);
            _materialRefreshSlotsButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 115f;

            _eventPreviousRuleButton =
                CreateButton(
                    "Prev Rule",
                    _contextActions,
                    SelectPreviousEventRule);
            _eventPreviousRuleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 90f;

            _eventNextRuleButton =
                CreateButton(
                    "Next Rule",
                    _contextActions,
                    SelectNextEventRule);
            _eventNextRuleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 90f;

            _eventRuleInput =
                CreateInputField(
                    "Event Rule Id",
                    _contextActions,
                    "Rule ID");
            _eventRuleInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 170f;

            _eventToggleRuleButton =
                CreateButton(
                    "Toggle Rule",
                    _contextActions,
                    ToggleSelectedEventRule);
            _eventToggleRuleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 115f;

            _eventTraceButton =
                CreateButton(
                    "Trace: Off",
                    _contextActions,
                    ToggleEventRuleTracing);
            _eventTraceButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 105f;

            _eventMaxCommandsInput =
                CreateInputField(
                    "Event Max Commands",
                    _contextActions,
                    "1..256");
            _eventMaxCommandsInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _eventApplyMaxCommandsButton =
                CreateButton(
                    "Set Max",
                    _contextActions,
                    ApplyEventMaxCommands);
            _eventApplyMaxCommandsButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 85f;

            _eventSaveRulesButton =
                CreateButton(
                    "Save Rules",
                    _contextActions,
                    SaveEventRules);
            _eventSaveRulesButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 100f;

            _eventReloadRulesButton =
                CreateButton(
                    "Reload Rules",
                    _contextActions,
                    ReloadEventRules);
            _eventReloadRulesButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;

            _settingsPreviousCapabilityButton =
                CreateButton(
                    "Prev Capability",
                    _contextActions,
                    SelectPreviousCapability);
            _settingsPreviousCapabilityButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 125f;

            _settingsNextCapabilityButton =
                CreateButton(
                    "Next Capability",
                    _contextActions,
                    SelectNextCapability);
            _settingsNextCapabilityButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 125f;

            _settingsToggleCapabilityButton =
                CreateButton(
                    "Capability",
                    _contextActions,
                    ToggleSelectedCapability);
            _settingsToggleCapabilityButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 220f;

            _settingsRenderScaleInput =
                CreateInputField(
                    "Settings Render Scale",
                    _contextActions,
                    "Scale 0.5..2");
            _settingsRenderScaleInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 105f;

            _settingsApplyRenderScaleButton =
                CreateButton(
                    "Set Scale",
                    _contextActions,
                    ApplySettingsRenderScale);
            _settingsApplyRenderScaleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 95f;

            _settingsFpsInput =
                CreateInputField(
                    "Settings FPS",
                    _contextActions,
                    "FPS 30..240");
            _settingsFpsInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 105f;

            _settingsApplyFpsButton =
                CreateButton(
                    "Set FPS",
                    _contextActions,
                    ApplySettingsFps);
            _settingsApplyFpsButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 85f;

            _settingsVsyncButton =
                CreateButton(
                    "VSync",
                    _contextActions,
                    ToggleSettingsVsync);
            _settingsVsyncButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 105f;

            _settingsRunInBackgroundButton =
                CreateButton(
                    "Background",
                    _contextActions,
                    ToggleSettingsRunInBackground);
            _settingsRunInBackgroundButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 145f;

            _diagnosticsPreviousPageButton =
                CreateButton(
                    "Prev Metrics",
                    _contextActions,
                    SelectPreviousDiagnosticsMetricPage);
            _diagnosticsPreviousPageButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _diagnosticsNextPageButton =
                CreateButton(
                    "Next Metrics",
                    _contextActions,
                    SelectNextDiagnosticsMetricPage);
            _diagnosticsNextPageButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _diagnosticsCaptureButton =
                CreateButton(
                    "Capture Now",
                    _contextActions,
                    CaptureDiagnosticsNow);
            _diagnosticsCaptureButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _diagnosticsSaveSnapshotButton =
                CreateButton(
                    "Save Snapshot",
                    _contextActions,
                    SaveDiagnosticsSnapshot);
            _diagnosticsSaveSnapshotButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 135f;

            _diagnosticsCsvButton =
                CreateButton(
                    "CSV Evidence",
                    _contextActions,
                    ToggleDiagnosticsCsvEvidence);
            _diagnosticsCsvButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 135f;

            _diagnosticsConsoleButton =
                CreateButton(
                    "Console Log",
                    _contextActions,
                    ToggleDiagnosticsConsoleLogging);
            _diagnosticsConsoleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 130f;

            _appearanceActions =
                CreateRect(
                    "Appearance Actions",
                    content);

            _appearanceActions.anchorMin =
                new Vector2(0f, 0f);
            _appearanceActions.anchorMax =
                new Vector2(1f, 0f);
            _appearanceActions.pivot =
                new Vector2(0.5f, 0f);
            _appearanceActions.offsetMin =
                new Vector2(24f, 128f);
            _appearanceActions.offsetMax =
                new Vector2(-24f, 178f);

            var appearanceLayout =
                _appearanceActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearanceLayout.spacing = 10f;
            appearanceLayout.childForceExpandWidth = false;
            appearanceLayout.childControlWidth = true;
            appearanceLayout.childControlHeight = true;

            _appearancePreviousButton =
                CreateButton(
                    "Previous Look",
                    _appearanceActions,
                    ApplyPreviousAppearancePreset);
            _appearancePreviousButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _appearanceNextButton =
                CreateButton(
                    "Next Look",
                    _appearanceActions,
                    ApplyNextAppearancePreset);
            _appearanceNextButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _appearanceTransitionButton =
                CreateButton(
                    "Transition: Immediate",
                    _appearanceActions,
                    SelectNextAppearanceTransition);
            _appearanceTransitionButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 240f;

            _appearanceRestoreButton =
                CreateButton(
                    "Restore Default",
                    _appearanceActions,
                    RestoreDefaultAppearance);
            _appearanceRestoreButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 170f;

            _appearancePreviewButton =
                CreateButton(
                    "Preview Transition",
                    _appearanceActions,
                    PreviewSelectedAppearanceTransition);
            _appearancePreviewButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 170f;

            _appearanceCancelButton =
                CreateButton(
                    "Cancel Transition",
                    _appearanceActions,
                    CancelAppearanceTransition);
            _appearanceCancelButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 160f;

            _appearanceDirectActions =
                CreateRect(
                    "Appearance Direct Actions",
                    content);

            _appearanceDirectActions.anchorMin =
                new Vector2(0f, 0f);
            _appearanceDirectActions.anchorMax =
                new Vector2(1f, 0f);
            _appearanceDirectActions.pivot =
                new Vector2(0.5f, 0f);
            _appearanceDirectActions.offsetMin =
                new Vector2(24f, 184f);
            _appearanceDirectActions.offsetMax =
                new Vector2(-24f, 234f);

            var appearanceDirectLayout =
                _appearanceDirectActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearanceDirectLayout.spacing = 8f;
            appearanceDirectLayout.childForceExpandWidth = false;
            appearanceDirectLayout.childControlWidth = true;
            appearanceDirectLayout.childControlHeight = true;

            _appearancePresetInput =
                CreateInputField(
                    "Appearance Preset Id",
                    _appearanceDirectActions,
                    "Preset ID");
            _appearancePresetInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 170f;

            _appearanceApplyPresetButton =
                CreateButton(
                    "Apply Preset",
                    _appearanceDirectActions,
                    ApplyAppearancePresetFromInput);
            _appearanceApplyPresetButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _appearanceOutfitInput =
                CreateInputField(
                    "Appearance Outfit Id",
                    _appearanceDirectActions,
                    "Outfit ID");
            _appearanceOutfitInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 160f;

            _appearanceApplyOutfitButton =
                CreateButton(
                    "Apply Outfit",
                    _appearanceDirectActions,
                    ApplyAppearanceOutfitFromInput);
            _appearanceApplyOutfitButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _appearanceAccessorySlotInput =
                CreateInputField(
                    "Appearance Accessory Slot",
                    _appearanceDirectActions,
                    "Slot ID");
            _appearanceAccessorySlotInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _appearanceAccessoryInput =
                CreateInputField(
                    "Appearance Accessory Id",
                    _appearanceDirectActions,
                    "Accessory ID");
            _appearanceAccessoryInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _appearanceSetAccessoryButton =
                CreateButton(
                    "Set",
                    _appearanceDirectActions,
                    SetAppearanceAccessoryFromInput);
            _appearanceSetAccessoryButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _appearanceClearAccessoryButton =
                CreateButton(
                    "Clear",
                    _appearanceDirectActions,
                    ClearAppearanceAccessoryFromInput);
            _appearanceClearAccessoryButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 80f;

            _appearancePersistenceActions =
                CreateRect(
                    "Appearance Saved Presets",
                    content);

            _appearancePersistenceActions.anchorMin =
                new Vector2(0f, 0f);
            _appearancePersistenceActions.anchorMax =
                new Vector2(1f, 0f);
            _appearancePersistenceActions.pivot =
                new Vector2(0.5f, 0f);
            _appearancePersistenceActions.offsetMin =
                new Vector2(24f, 240f);
            _appearancePersistenceActions.offsetMax =
                new Vector2(-24f, 290f);

            var appearancePersistenceLayout =
                _appearancePersistenceActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearancePersistenceLayout.spacing = 8f;
            appearancePersistenceLayout.childForceExpandWidth = false;
            appearancePersistenceLayout.childControlWidth = true;
            appearancePersistenceLayout.childControlHeight = true;

            _appearanceUserPresetInput =
                CreateInputField(
                    "Appearance User Preset Id",
                    _appearancePersistenceActions,
                    "User Preset ID");
            _appearanceUserPresetInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 220f;

            _appearanceSaveUserPresetButton =
                CreateButton(
                    "Save Current",
                    _appearancePersistenceActions,
                    SaveCurrentAppearanceUserPreset);
            _appearanceSaveUserPresetButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 140f;

            _appearanceDeleteUserPresetButton =
                CreateButton(
                    "Delete User Preset",
                    _appearancePersistenceActions,
                    DeleteAppearanceUserPreset);
            _appearanceDeleteUserPresetButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 180f;

            _appearancePresetManagementActions =
                CreateRect(
                    "Appearance Preset Management",
                    content);

            _appearancePresetManagementActions.anchorMin =
                new Vector2(0f, 0f);
            _appearancePresetManagementActions.anchorMax =
                new Vector2(1f, 0f);
            _appearancePresetManagementActions.pivot =
                new Vector2(0.5f, 0f);
            _appearancePresetManagementActions.offsetMin =
                new Vector2(24f, 296f);
            _appearancePresetManagementActions.offsetMax =
                new Vector2(-24f, 346f);

            var appearancePresetManagementLayout =
                _appearancePresetManagementActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearancePresetManagementLayout.spacing = 8f;
            appearancePresetManagementLayout.childForceExpandWidth = false;
            appearancePresetManagementLayout.childControlWidth = true;
            appearancePresetManagementLayout.childControlHeight = true;

            _appearanceUserPresetTargetInput =
                CreateInputField(
                    "Appearance User Preset Target Id",
                    _appearancePresetManagementActions,
                    "New Preset ID");
            _appearanceUserPresetTargetInput.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 220f;

            _appearanceRenameUserPresetButton =
                CreateButton(
                    "Rename",
                    _appearancePresetManagementActions,
                    RenameAppearanceUserPreset);
            _appearanceRenameUserPresetButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;

            _appearanceDuplicateUserPresetButton =
                CreateButton(
                    "Duplicate",
                    _appearancePresetManagementActions,
                    DuplicateAppearanceUserPreset);
            _appearanceDuplicateUserPresetButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 120f;

            _appearanceMoveUserPresetUpButton =
                CreateButton(
                    "Move Up",
                    _appearancePresetManagementActions,
                    MoveAppearanceUserPresetUp);
            _appearanceMoveUserPresetUpButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 105f;

            _appearanceMoveUserPresetDownButton =
                CreateButton(
                    "Move Down",
                    _appearancePresetManagementActions,
                    MoveAppearanceUserPresetDown);
            _appearanceMoveUserPresetDownButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 115f;

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

        private void BrowseCharacterFile()
        {
            ResolveCharacterFileSelectionAdapter();

            if (sceneRuntime == null ||
                !IsServiceAlive(_characterFileSelectionAdapter) ||
                !_characterFileSelectionAdapter.IsSupported)
            {
                _lastActionMessage =
                    _characterFileSelectionAdapter?.UnavailableReason ??
                    "Character file selection adapter is unavailable.";
                RefreshAll();
                return;
            }

            if (!ApplicationUiActionPolicy
                .CanBrowseCharacterFile(
                    true,
                    sceneRuntime.State,
                    true))
            {
                _lastActionMessage =
                    "Character browsing is unavailable while the scene runtime is busy.";
                RefreshAll();
                return;
            }

            var result =
                _characterFileSelectionAdapter
                    .SelectCharacterFile(
                        _characterPathInput?.text);

            if (result.Cancelled)
            {
                _lastActionMessage =
                    "Character file selection cancelled.";
                RefreshAll();
                return;
            }

            if (!result.Selected ||
                string.IsNullOrWhiteSpace(
                    result.Path))
            {
                _lastActionMessage =
                    "Character file selection failed: " +
                    (result.Error ??
                     "unknown error");
                RefreshAll();
                return;
            }

            if (_characterPathInput != null)
            {
                _characterPathInput.text =
                    result.Path;
            }

            _lastActionMessage =
                $"Selected character file through '{_characterFileSelectionAdapter.AdapterId}'.";
            RefreshAll();
        }

        private async void LoadCharacterFromPath()
        {
            if (sceneRuntime == null ||
                _characterPathInput == null)
            {
                _lastActionMessage =
                    "Character load unavailable.";
                RefreshAll();
                return;
            }

            var path =
                _characterPathInput.text?.Trim();

            if (!ApplicationUiActionPolicy
                    .CanLoadCharacter(
                        true,
                        sceneRuntime.State,
                        path))
            {
                _lastActionMessage =
                    string.IsNullOrWhiteSpace(path)
                        ? "Enter a VRM path first."
                        : "Character load is unavailable in the current runtime state.";
                RefreshAll();
                return;
            }

            _lastActionMessage =
                "Loading character...";
            RefreshAll();

            try
            {
                var loaded =
                    await sceneRuntime
                        .LoadCharacterAsync(path);

                _lastActionMessage =
                    loaded != null
                        ? "Character loaded."
                        : "Character load completed without a model.";
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Character load failed: " +
                    exception.Message;
            }

            RefreshAll();
        }

        private async void ReloadCharacter()
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Character reload unavailable.";
                RefreshAll();
                return;
            }

            var status =
                sceneRuntime.Status;

            if (!ApplicationUiActionPolicy
                    .CanReloadCharacter(
                        true,
                        status.State,
                        status.HasCharacter,
                        status.CurrentCharacterPath))
            {
                _lastActionMessage =
                    "Character reload is unavailable.";
                RefreshAll();
                return;
            }

            _lastActionMessage =
                "Reloading character...";
            RefreshAll();

            try
            {
                var loaded =
                    await sceneRuntime
                        .ReloadCharacterAsync();

                _lastActionMessage =
                    loaded != null
                        ? "Character reloaded."
                        : "Character reload completed without a model.";
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Character reload failed: " +
                    exception.Message;
            }

            RefreshAll();
        }

        private void UnloadCharacter()
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Character unload unavailable.";
                RefreshAll();
                return;
            }

            var status =
                sceneRuntime.Status;

            if (!ApplicationUiActionPolicy
                    .CanUnloadCharacter(
                        true,
                        status.State,
                        status.HasCharacter))
            {
                _lastActionMessage =
                    "Character unload is unavailable.";
                RefreshAll();
                return;
            }

            try
            {
                sceneRuntime.UnloadCharacter();
                _lastActionMessage =
                    "Character unloaded.";
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Character unload failed: " +
                    exception.Message;
            }

            RefreshAll();
        }

        private void SelectPreviousTrackingControl()
        {
            if (_trackingControls.Count == 0)
            {
                return;
            }

            _trackingControlIndex =
                (_trackingControlIndex -
                 1 +
                 _trackingControls.Count) %
                _trackingControls.Count;

            RefreshAll();
        }

        private void SelectNextTrackingControl()
        {
            if (_trackingControls.Count == 0)
            {
                return;
            }

            _trackingControlIndex =
                (_trackingControlIndex + 1) %
                _trackingControls.Count;

            RefreshAll();
        }

        private void ToggleSelectedTrackingControl()
        {
            var control =
                GetSelectedTrackingControl();

            if (control == null)
            {
                _lastActionMessage =
                    "No tracking source is selected.";
                RefreshAll();
                return;
            }

            var nextEnabled =
                !control.ControlEnabled;

            if (control.TrySetControlEnabled(
                    nextEnabled,
                    out var error))
            {
                _lastActionMessage =
                    control.DisplayName +
                    (nextEnabled
                        ? " enabled."
                        : " disabled.");
            }
            else
            {
                _lastActionMessage =
                    control.DisplayName +
                    " toggle failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void RecoverSelectedTrackingControl()
        {
            var control =
                GetSelectedTrackingControl();

            if (control == null)
            {
                _lastActionMessage =
                    "No tracking source is selected.";
                RefreshAll();
                return;
            }

            if (control.TryRecover(
                    out var error))
            {
                _lastActionMessage =
                    control.DisplayName +
                    " recovery requested.";
            }
            else
            {
                _lastActionMessage =
                    control.DisplayName +
                    " recovery failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void ApplyPreviousAppearancePreset()
        {
            ApplyRelativeAppearancePreset(
                -1);
        }

        private void ApplyNextAppearancePreset()
        {
            ApplyRelativeAppearancePreset(
                1);
        }

        private void ApplyRelativeAppearancePreset(
            int direction)
        {
            if (!IsServiceAlive(_appearanceRuntime) ||
                _appearanceRuntime.PresetIds.Count == 0)
            {
                _lastActionMessage =
                    "No appearance presets are configured.";
                RefreshAll();
                return;
            }

            var ids =
                _appearanceRuntime.PresetIds;
            var currentIndex = -1;

            for (var i = 0;
                 i < ids.Count;
                 i++)
            {
                if (string.Equals(
                        ids[i],
                        _appearanceRuntime.Status
                            .CurrentPresetId,
                        StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                currentIndex =
                    direction >= 0
                        ? -1
                        : 0;
            }

            var nextIndex =
                (currentIndex +
                 direction +
                 ids.Count) %
                ids.Count;

            var presetId =
                ids[nextIndex];

            if (_appearanceRuntime.SetPreset(
                    presetId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    $"Appearance '{presetId}' requested.";
            }
            else
            {
                _lastActionMessage =
                    "Appearance change failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void SelectNextAppearanceTransition()
        {
            if (!IsServiceAlive(_appearanceRuntime))
            {
                return;
            }

            var count =
                _appearanceRuntime.TransitionIds.Count +
                1;

            _appearanceTransitionIndex =
                (_appearanceTransitionIndex + 1) %
                Mathf.Max(1, count);

            RefreshAll();
        }

        private string GetSelectedAppearanceTransitionId()
        {
            if (!IsServiceAlive(_appearanceRuntime) ||
                _appearanceTransitionIndex <= 0 ||
                _appearanceRuntime.TransitionIds.Count == 0)
            {
                return "Immediate";
            }

            var index =
                Mathf.Clamp(
                    _appearanceTransitionIndex - 1,
                    0,
                    _appearanceRuntime.TransitionIds.Count - 1);

            return
                _appearanceRuntime.TransitionIds[
                    index];
        }

        private void RestoreDefaultAppearance()
        {
            if (!IsServiceAlive(_appearanceRuntime))
            {
                _lastActionMessage =
                    "Appearance runtime unavailable.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.RestoreDefault(
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    "Default appearance requested.";
            }
            else
            {
                _lastActionMessage =
                    "Restore appearance failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void ApplyAppearancePresetFromInput()
        {
            var presetId =
                _appearancePresetInput?.text?.Trim();

            if (!IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanApplyAppearancePreset(
                        true,
                        _appearanceRuntime.Status.State,
                        presetId))
            {
                _lastActionMessage =
                    "Enter a preset ID while the appearance runtime is ready.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.SetPreset(
                    presetId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    $"Appearance preset '{presetId}' requested.";
            }
            else
            {
                _lastActionMessage =
                    "Preset apply failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void ApplyAppearanceOutfitFromInput()
        {
            var outfitId =
                _appearanceOutfitInput?.text?.Trim();

            if (!IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanApplyAppearanceOutfit(
                        true,
                        _appearanceRuntime.Status.State,
                        outfitId))
            {
                _lastActionMessage =
                    "Enter an outfit ID while the appearance runtime is ready.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.SetOutfit(
                    outfitId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    $"Outfit '{outfitId}' requested.";
            }
            else
            {
                _lastActionMessage =
                    "Outfit apply failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void SetAppearanceAccessoryFromInput()
        {
            var slotId =
                _appearanceAccessorySlotInput?.text?.Trim();
            var accessoryId =
                _appearanceAccessoryInput?.text?.Trim();

            if (!IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanSetAppearanceAccessory(
                        true,
                        _appearanceRuntime.Status.State,
                        slotId,
                        accessoryId))
            {
                _lastActionMessage =
                    "Enter both accessory slot and accessory IDs while the appearance runtime is ready.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.SetAccessory(
                    slotId,
                    accessoryId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    $"Accessory '{slotId}/{accessoryId}' requested.";
            }
            else
            {
                _lastActionMessage =
                    "Accessory apply failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void ClearAppearanceAccessoryFromInput()
        {
            var slotId =
                _appearanceAccessorySlotInput?.text?.Trim();

            if (!IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanClearAppearanceAccessory(
                        true,
                        _appearanceRuntime.Status.State,
                        slotId))
            {
                _lastActionMessage =
                    "Enter an accessory slot ID while the appearance runtime is ready.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.ClearAccessory(
                    slotId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                _lastActionMessage =
                    $"Accessory slot '{slotId}' clear requested.";
            }
            else
            {
                _lastActionMessage =
                    "Accessory clear failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void PreviewSelectedAppearanceTransition()
        {
            if (!IsServiceAlive(_appearanceRuntime))
            {
                _lastActionMessage =
                    "Appearance runtime unavailable.";
                RefreshAll();
                return;
            }

            var transitionId =
                GetSelectedAppearanceTransitionId();
            var appearanceStatus =
                _appearanceRuntime.Status;

            if (!ApplicationUiActionPolicy
                .CanPreviewAppearanceTransition(
                    true,
                    appearanceStatus.State,
                    transitionId,
                    appearanceStatus.CurrentOutfitId))
            {
                _lastActionMessage =
                    string.Equals(
                        transitionId,
                        "Immediate",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Select a transition preset before previewing."
                        : "Transition preview requires an active outfit and a ready appearance runtime.";
                RefreshAll();
                return;
            }

            bool requested;
            string error;

            if (!string.IsNullOrWhiteSpace(
                    current.PresetId))
            {
                requested =
                    _appearanceRuntime.SetPreset(
                        current.PresetId,
                        transitionId,
                        out error);
            }
            else
            {
                requested =
                    _appearanceRuntime.SetOutfit(
                        current.OutfitId,
                        transitionId,
                        out error);
            }

            _lastActionMessage =
                requested
                    ? $"Transition '{transitionId}' preview requested."
                    : "Transition preview failed: " +
                      (error ?? "unknown error");

            RefreshAll();
        }

        private void CancelAppearanceTransition()
        {
            if (!IsServiceAlive(_appearanceRuntime))
            {
                _lastActionMessage =
                    "Appearance runtime unavailable.";
                RefreshAll();
                return;
            }

            if (_appearanceRuntime.CancelTransition(
                    out var error))
            {
                _lastActionMessage =
                    "Appearance transition cancelled. Presentation cleanup was executed.";
            }
            else
            {
                _lastActionMessage =
                    "Appearance transition cancel failed: " +
                    (error ?? "unknown error");
            }

            RefreshAll();
        }

        private void SaveCurrentAppearanceUserPreset()
        {
            var registry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var presetId =
                _appearanceUserPresetInput?.text?.Trim();
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                !IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanSaveAppearanceUserPreset(
                        true,
                        _appearanceRuntime.Status.State,
                        presetId,
                        characterPath))
            {
                _lastActionMessage =
                    "Enter a user preset ID while a character appearance is ready.";
                RefreshAll();
                return;
            }

            var previous =
                registry.CaptureUserPresets();

            if (!registry.SaveCurrentAsUserPreset(
                    presetId,
                    GetSelectedAppearanceTransitionId(),
                    out var saved,
                    out var error))
            {
                _lastActionMessage =
                    "User preset save failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (!PersistAppearanceUserPresets(
                    registry,
                    characterPath,
                    out error))
            {
                registry.ReplaceUserPresets(
                    previous,
                    out _);

                _lastActionMessage =
                    "User preset persistence failed and the in-memory change was rolled back: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (_appearancePresetInput != null)
            {
                _appearancePresetInput.text =
                    saved.Id;
            }
            _lastActionMessage =
                $"User appearance preset '{saved.Id}' saved for this character.";
            RefreshAll();
        }

        private void DeleteAppearanceUserPreset()
        {
            var registry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var presetId =
                _appearanceUserPresetInput?.text?.Trim();
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                !IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanDeleteAppearanceUserPreset(
                        true,
                        _appearanceRuntime.Status.State,
                        presetId,
                        characterPath))
            {
                _lastActionMessage =
                    "Enter a saved user preset ID to delete.";
                RefreshAll();
                return;
            }

            var previous =
                registry.CaptureUserPresets();
            var previousCurrentPresetId =
                _appearanceRuntime.Status.CurrentPresetId;

            if (!registry.RemoveUserPreset(
                    presetId,
                    out var error))
            {
                _lastActionMessage =
                    "User preset delete failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (!PersistAppearanceUserPresets(
                    registry,
                    characterPath,
                    out error))
            {
                var restored =
                    registry.ReplaceUserPresets(
                        previous,
                        out _);

                if (restored &&
                    !string.IsNullOrWhiteSpace(
                        previousCurrentPresetId) &&
                    !string.Equals(
                        _appearanceRuntime.Status.CurrentPresetId,
                        previousCurrentPresetId,
                        StringComparison.Ordinal))
                {
                    _appearanceRuntime.SetPreset(
                        previousCurrentPresetId,
                        "Immediate",
                        out _);
                }

                _lastActionMessage =
                    "User preset delete persistence failed and the in-memory change was rolled back: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (_appearancePresetInput != null &&
                string.Equals(
                    _appearancePresetInput.text?.Trim(),
                    presetId,
                    StringComparison.Ordinal))
            {
                _appearancePresetInput.text =
                    string.Empty;
            }

            _lastActionMessage =
                $"User appearance preset '{presetId}' deleted.";
            RefreshAll();
        }

        private void RenameAppearanceUserPreset()
        {
            var registry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var sourceId =
                _appearanceUserPresetInput?.text?.Trim();
            var targetId =
                _appearanceUserPresetTargetInput?.text?.Trim();
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                !IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanRenameAppearanceUserPreset(
                        true,
                        _appearanceRuntime.Status.State,
                        sourceId,
                        targetId,
                        characterPath))
            {
                _lastActionMessage =
                    "Enter a saved user preset ID and a new preset ID to rename.";
                RefreshAll();
                return;
            }

            var previous =
                registry.CaptureUserPresets();
            var previousCurrentPresetId =
                _appearanceRuntime.Status.CurrentPresetId;

            if (!registry.RenameUserPreset(
                    sourceId,
                    targetId,
                    out var renamed,
                    out var error))
            {
                _lastActionMessage =
                    "User preset rename failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (!PersistAppearanceUserPresets(
                    registry,
                    characterPath,
                    out error))
            {
                RestoreAppearanceUserPresetMutation(
                    registry,
                    previous,
                    previousCurrentPresetId);

                _lastActionMessage =
                    "User preset rename persistence failed and the in-memory change was rolled back: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (_appearanceUserPresetInput != null)
            {
                _appearanceUserPresetInput.text =
                    renamed.Id;
            }

            if (_appearancePresetInput != null &&
                string.Equals(
                    _appearancePresetInput.text?.Trim(),
                    sourceId,
                    StringComparison.Ordinal))
            {
                _appearancePresetInput.text =
                    renamed.Id;
            }

            if (_appearanceUserPresetTargetInput != null)
            {
                _appearanceUserPresetTargetInput.text =
                    string.Empty;
            }

            _lastActionMessage =
                $"User appearance preset '{sourceId}' renamed to '{renamed.Id}'.";
            RefreshAll();
        }

        private void DuplicateAppearanceUserPreset()
        {
            var registry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var sourceId =
                _appearanceUserPresetInput?.text?.Trim();
            var targetId =
                _appearanceUserPresetTargetInput?.text?.Trim();
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                !IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanDuplicateAppearanceUserPreset(
                        true,
                        _appearanceRuntime.Status.State,
                        sourceId,
                        targetId,
                        characterPath))
            {
                _lastActionMessage =
                    "Enter a saved user preset ID and a new preset ID to duplicate.";
                RefreshAll();
                return;
            }

            var previous =
                registry.CaptureUserPresets();
            var previousCurrentPresetId =
                _appearanceRuntime.Status.CurrentPresetId;

            if (!registry.DuplicateUserPreset(
                    sourceId,
                    targetId,
                    out var duplicate,
                    out var error))
            {
                _lastActionMessage =
                    "User preset duplicate failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (!PersistAppearanceUserPresets(
                    registry,
                    characterPath,
                    out error))
            {
                RestoreAppearanceUserPresetMutation(
                    registry,
                    previous,
                    previousCurrentPresetId);

                _lastActionMessage =
                    "User preset duplicate persistence failed and the in-memory change was rolled back: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (_appearanceUserPresetInput != null)
            {
                _appearanceUserPresetInput.text =
                    duplicate.Id;
            }

            if (_appearancePresetInput != null)
            {
                _appearancePresetInput.text =
                    duplicate.Id;
            }

            if (_appearanceUserPresetTargetInput != null)
            {
                _appearanceUserPresetTargetInput.text =
                    string.Empty;
            }

            _lastActionMessage =
                $"User appearance preset '{sourceId}' duplicated as '{duplicate.Id}'.";
            RefreshAll();
        }

        private void MoveAppearanceUserPresetUp()
        {
            MoveAppearanceUserPreset(
                -1);
        }

        private void MoveAppearanceUserPresetDown()
        {
            MoveAppearanceUserPreset(
                1);
        }

        private void MoveAppearanceUserPreset(
            int offset)
        {
            var registry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var presetId =
                _appearanceUserPresetInput?.text?.Trim();
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                !IsServiceAlive(_appearanceRuntime) ||
                !ApplicationUiActionPolicy
                    .CanMoveAppearanceUserPreset(
                        true,
                        _appearanceRuntime.Status.State,
                        presetId,
                        characterPath))
            {
                _lastActionMessage =
                    "Enter a saved user preset ID to reorder.";
                RefreshAll();
                return;
            }

            var previous =
                registry.CaptureUserPresets();
            var previousCurrentPresetId =
                _appearanceRuntime.Status.CurrentPresetId;

            if (!registry.MoveUserPreset(
                    presetId,
                    offset,
                    out var error))
            {
                _lastActionMessage =
                    "User preset reorder failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            if (!PersistAppearanceUserPresets(
                    registry,
                    characterPath,
                    out error))
            {
                RestoreAppearanceUserPresetMutation(
                    registry,
                    previous,
                    previousCurrentPresetId);

                _lastActionMessage =
                    "User preset reorder persistence failed and the in-memory change was rolled back: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"User appearance preset '{presetId}' moved " +
                (offset < 0
                    ? "up."
                    : "down.");
            RefreshAll();
        }

        private void RestoreAppearanceUserPresetMutation(
            IAppearanceUserPresetRegistry registry,
            AppearancePreset[] previous,
            string previousCurrentPresetId)
        {
            if (registry == null)
            {
                return;
            }

            var restored =
                registry.ReplaceUserPresets(
                    previous,
                    out _);

            if (restored &&
                IsServiceAlive(_appearanceRuntime) &&
                !string.IsNullOrWhiteSpace(
                    previousCurrentPresetId) &&
                !string.Equals(
                    _appearanceRuntime.Status.CurrentPresetId,
                    previousCurrentPresetId,
                    StringComparison.Ordinal))
            {
                _appearanceRuntime.SetPreset(
                    previousCurrentPresetId,
                    "Immediate",
                    out _);
            }
        }

        private bool PersistAppearanceUserPresets(
            IAppearanceUserPresetRegistry registry,
            string characterPath,
            out string error)
        {
            error = null;

            if (registry == null)
            {
                error =
                    "Appearance user preset registry is unavailable.";
                return false;
            }

            try
            {
                _appearancePresetStore ??=
                    AppearanceUserPresetStore
                        .CreateDefault();
            }
            catch (Exception exception)
            {
                error =
                    "Appearance profile store initialization failed: " +
                    exception.Message;
                return false;
            }

            return _appearancePresetStore.TrySave(
                characterPath,
                registry.CaptureUserPresets(),
                out error);
        }

        private void EnsureAppearanceUserPresetsLoaded()
        {
            var registry =
                IsServiceAlive(_appearanceRuntime)
                    ? _appearanceRuntime as
                        IAppearanceUserPresetRegistry
                    : null;
            var characterPath =
                sceneRuntime?.CurrentCharacterPath;

            if (registry == null ||
                string.IsNullOrWhiteSpace(
                    characterPath))
            {
                _loadedAppearancePresetRegistry = null;
                _loadedAppearanceProfilePath = null;
                return;
            }

            if (ReferenceEquals(
                    registry,
                    _loadedAppearancePresetRegistry) &&
                string.Equals(
                    characterPath,
                    _loadedAppearanceProfilePath,
                    StringComparison.Ordinal))
            {
                return;
            }

            _loadedAppearancePresetRegistry =
                registry;
            _loadedAppearanceProfilePath =
                characterPath;

            try
            {
                _appearancePresetStore ??=
                    AppearanceUserPresetStore
                        .CreateDefault();
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Appearance profile store initialization failed: " +
                    exception.Message;
                return;
            }

            if (!_appearancePresetStore.TryLoad(
                    characterPath,
                    out var presets,
                    out var error))
            {
                _lastActionMessage =
                    "Saved appearance presets were not loaded: " +
                    (error ?? "unknown error");
                return;
            }

            if (!registry.ReplaceUserPresets(
                    presets,
                    out error))
            {
                _lastActionMessage =
                    "Saved appearance presets are incompatible with this character: " +
                    (error ?? "unknown error");
                return;
            }

            if (presets.Length > 0)
            {
                _lastActionMessage =
                    $"Loaded {presets.Length} saved appearance preset(s) for this character.";
            }
        }

        private void SetPrimaryPoseLayerWeight(
            float value)
        {
            if (_mixer == null)
            {
                _lastActionMessage =
                    "Motion/expression mixer is unavailable.";
                return;
            }

            if (!_mixer.TrySetPrimaryPoseLayerWeight(
                    Mathf.Clamp01(
                        value),
                    out var error))
            {
                _lastActionMessage =
                    "Pose layer weight update failed: " +
                    (error ?? "unknown error");
                RefreshMotionControlState();
                return;
            }

            if (_motionPoseWeightLabel != null)
            {
                _motionPoseWeightLabel.text =
                    "Pose Weight " +
                    _mixer.PrimaryPoseLayerWeight
                        .ToString("0.00");
            }
        }

        private void ApplyManualExpression()
        {
            if (!TryResolveManualExpressionInput(
                    requireValue:
                        true,
                    out var expression,
                    out var value,
                    out var error))
            {
                _lastActionMessage =
                    error;
                RefreshAll();
                return;
            }

            if (_manualExpressionSource == null ||
                !_manualExpressionSource
                    .SetExpression(
                        expression,
                        value))
            {
                _lastActionMessage =
                    "Manual expression source rejected the requested value.";
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Manual expression '{StandardExpressionNames.GetVrm1Name(expression)}' set to {value:0.00}.";
            RefreshAll();
        }

        private void ClearManualExpression()
        {
            if (!TryResolveManualExpressionInput(
                    requireValue:
                        false,
                    out var expression,
                    out _,
                    out var error))
            {
                _lastActionMessage =
                    error;
                RefreshAll();
                return;
            }

            if (_manualExpressionSource == null ||
                !_manualExpressionSource
                    .ClearExpression(
                        expression))
            {
                _lastActionMessage =
                    "Manual expression source rejected the clear request.";
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Manual expression '{StandardExpressionNames.GetVrm1Name(expression)}' cleared.";
            RefreshAll();
        }

        private void ClearAllManualExpressions()
        {
            if (_manualExpressionSource == null)
            {
                _lastActionMessage =
                    "Manual expression source is unavailable.";
                RefreshAll();
                return;
            }

            _manualExpressionSource
                .ClearAll();
            _lastActionMessage =
                "All manual expressions cleared.";
            RefreshAll();
        }

        private bool TryResolveManualExpressionInput(
            bool requireValue,
            out StandardExpression expression,
            out float value,
            out string error)
        {
            expression = default;
            value = 0f;
            error = null;

            if (_manualExpressionSource == null)
            {
                error =
                    "Manual expression source is unavailable.";
                return false;
            }

            var name =
                _manualExpressionNameInput?.text?.Trim();

            if (!StandardExpressionNames
                .TryParse(
                    name,
                    out expression))
            {
                error =
                    $"Unknown standard expression '{name ?? "<empty>"}'.";
                return false;
            }

            if (!requireValue)
            {
                return true;
            }

            var raw =
                _manualExpressionValueInput?.text?.Trim();

            if (!float.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) ||
                float.IsNaN(
                    value) ||
                float.IsInfinity(
                    value) ||
                value < 0f ||
                value > 1f)
            {
                error =
                    "Manual expression value must be a number in the 0..1 range.";
                return false;
            }

            return true;
        }

        private void SelectNextEnvironmentTransitionMode()
        {
            _environmentTransitionMode =
                (EnvironmentTransitionMode)(
                    ((int)_environmentTransitionMode +
                     1) %
                    ((int)EnvironmentTransitionMode.Dissolve +
                     1));

            if (_environmentTransitionMode <
                    EnvironmentTransitionMode.Cut ||
                _environmentTransitionMode >
                    EnvironmentTransitionMode.Dissolve)
            {
                _environmentTransitionMode =
                    EnvironmentTransitionMode.Cut;
            }

            RefreshContextActions();
        }

        private void ApplyEnvironmentState()
        {
            var runtime =
                sceneRuntime?.EnvironmentRuntime;
            var stateId =
                _environmentStateInput
                    ?.text
                    ?.Trim();

            if (runtime == null ||
                string.IsNullOrWhiteSpace(
                    stateId))
            {
                _lastActionMessage =
                    "Enter an environment state ID while the environment runtime is available.";
                RefreshAll();
                return;
            }

            var rawDuration =
                _environmentTransitionDurationInput
                    ?.text
                    ?.Trim();
            var duration =
                0f;

            if (!string.IsNullOrWhiteSpace(
                    rawDuration) &&
                (!float.TryParse(
                     rawDuration,
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out duration) ||
                 float.IsNaN(
                     duration) ||
                 float.IsInfinity(
                     duration) ||
                 duration < 0f))
            {
                _lastActionMessage =
                    "Environment transition duration must be a finite non-negative number.";
                RefreshAll();
                return;
            }

            var transition =
                new EnvironmentTransitionSpec(
                    _environmentTransitionMode,
                    duration);

            if (!runtime.SetState(
                    stateId,
                    transition,
                    out var error))
            {
                _lastActionMessage =
                    "Environment state apply failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Environment state '{stateId}' requested with {_environmentTransitionMode} ({duration:0.###}s).";
            RefreshAll();
        }

        private void SelectPreviousMaterialSlot()
        {
            SelectMaterialSlot(
                -1);
        }

        private void SelectNextMaterialSlot()
        {
            SelectMaterialSlot(
                1);
        }

        private void SelectMaterialSlot(
            int offset)
        {
            if (_materialController == null)
            {
                _lastActionMessage =
                    "Material controller unavailable.";
                RefreshAll();
                return;
            }

            var slotCount =
                _materialController.SlotCount;

            if (slotCount == 0)
            {
                _lastActionMessage =
                    "No material slots are available.";
                RefreshAll();
                return;
            }

            _materialSlotIndex =
                (_materialSlotIndex +
                 offset +
                 slotCount) %
                slotCount;

            if (_materialSlotInput != null &&
                _materialController.TryGetSlotAt(
                    _materialSlotIndex,
                    out var selectedSlot))
            {
                _materialSlotInput.text =
                    selectedSlot.Id;
            }

            RefreshAll();
        }

        private void ApplyMaterialShader()
        {
            if (_materialController == null)
            {
                _lastActionMessage =
                    "Material controller unavailable.";
                RefreshAll();
                return;
            }

            var slotId =
                _materialSlotInput?.text?.Trim();
            var shaderId =
                _materialShaderInput?.text?.Trim();

            if (string.IsNullOrWhiteSpace(
                    slotId) ||
                string.IsNullOrWhiteSpace(
                    shaderId))
            {
                _lastActionMessage =
                    "Material slot ID and shader ID are required.";
                RefreshAll();
                return;
            }

            if (!_materialController
                .TryApplyShaderId(
                    slotId,
                    shaderId,
                    out var error))
            {
                _lastActionMessage =
                    "Material shader apply failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Shader '{shaderId}' applied to material slot '{slotId}'.";
            RefreshAll();
        }

        private void SetMaterialFloat()
        {
            if (_materialController == null)
            {
                _lastActionMessage =
                    "Material controller unavailable.";
                RefreshAll();
                return;
            }

            var slotId =
                _materialSlotInput?.text?.Trim();
            var property =
                _materialPropertyInput?.text?.Trim();
            var rawValue =
                _materialValueInput?.text?.Trim();

            if (string.IsNullOrWhiteSpace(
                    slotId) ||
                string.IsNullOrWhiteSpace(
                    property) ||
                !float.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                float.IsNaN(
                    value) ||
                float.IsInfinity(
                    value))
            {
                _lastActionMessage =
                    "Material float update requires a slot ID, property name, and finite numeric value.";
                RefreshAll();
                return;
            }

            if (!_materialController
                .TrySetFloat(
                    slotId,
                    property,
                    value,
                    out var error))
            {
                _lastActionMessage =
                    "Material float update failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Material '{slotId}' property '{property}' set to {value:0.###}.";
            RefreshAll();
        }

        private void ClearMaterialOverride()
        {
            if (_materialController == null)
            {
                _lastActionMessage =
                    "Material controller unavailable.";
                RefreshAll();
                return;
            }

            var slotId =
                _materialSlotInput?.text?.Trim();

            if (string.IsNullOrWhiteSpace(
                    slotId) ||
                !_materialController
                    .ClearOverride(
                        slotId))
            {
                _lastActionMessage =
                    $"Material override clear failed for '{slotId ?? "<empty>"}'.";
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Material override cleared for '{slotId}'.";
            RefreshAll();
        }

        private void RefreshMaterialSlots()
        {
            if (_materialController == null)
            {
                _lastActionMessage =
                    "Material controller unavailable.";
                RefreshAll();
                return;
            }

            _materialController.RefreshSlots();
            _materialSlotIndex = 0;

            var slotCount =
                _materialController.SlotCount;

            if (_materialSlotInput != null)
            {
                _materialSlotInput.text =
                    _materialController.TryGetSlotAt(
                        0,
                        out var firstSlot)
                        ? firstSlot.Id
                        : string.Empty;
            }

            _lastActionMessage =
                $"Material slots refreshed: {slotCount}.";
            RefreshAll();
        }

        private void EnsureEventRulesLoaded()
        {
            if (eventRuntime == null)
            {
                _eventRuleStore = null;
                _eventRuleStorePath = null;
                _loadedEventRuleHost = null;
                _eventRuleStoreChecked = false;
                return;
            }

            if (_eventRuleStoreChecked &&
                ReferenceEquals(
                    _loadedEventRuleHost,
                    eventRuntime))
            {
                return;
            }

            _eventRuleStoreChecked = true;
            _loadedEventRuleHost =
                eventRuntime;
            _eventRuleStorePath =
                Path.Combine(
                    Application.persistentDataPath,
                    "VCR",
                    "event-rules.json");

            try
            {
                _eventRuleStore =
                    new EventRuntimeConfigurationStore(
                        _eventRuleStorePath);
            }
            catch (Exception exception)
            {
                _eventRuleStore = null;
                _lastActionMessage =
                    "Event rule store initialization failed: " +
                    exception.Message;
                return;
            }

            if (!File.Exists(
                    _eventRuleStorePath))
            {
                _eventRuleIndex = 0;
                return;
            }

            if (!_eventRuleStore.TryLoad(
                    out var rules,
                    out var maxCommands,
                    out var error))
            {
                _lastActionMessage =
                    "Event rule load failed: " +
                    (error ?? "unknown error");
                return;
            }

            eventRuntime.SetRules(
                rules);

            if (!eventRuntime.TrySetMaxCommandsPerEvent(
                    maxCommands,
                    out error))
            {
                _lastActionMessage =
                    "Event max-command restore failed: " +
                    (error ?? "unknown error");
            }

            _eventRuleIndex = 0;
        }

        private void SelectPreviousEventRule()
        {
            SelectEventRule(
                -1);
        }

        private void SelectNextEventRule()
        {
            SelectEventRule(
                1);
        }

        private void SelectEventRule(
            int offset)
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            var ruleCount =
                eventRuntime.RuleCount;

            if (ruleCount == 0)
            {
                _eventRuleIndex = 0;
                _lastActionMessage =
                    "No event rules are configured.";
                RefreshAll();
                return;
            }

            _eventRuleIndex =
                (_eventRuleIndex +
                 offset +
                 ruleCount) %
                ruleCount;

            if (_eventRuleInput != null)
            {
                _eventRuleInput.text =
                    eventRuntime.GetRuleAt(
                        _eventRuleIndex)?.Id ??
                    string.Empty;
            }

            RefreshAll();
        }

        private void ToggleSelectedEventRule()
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            var ruleCount =
                eventRuntime.RuleCount;

            if (ruleCount == 0)
            {
                _lastActionMessage =
                    "No event rules are configured.";
                RefreshAll();
                return;
            }

            var ruleId =
                _eventRuleInput?.text?.Trim();

            EventRuntimeRule selected =
                null;

            eventRuntime.TryGetRule(
                ruleId,
                out selected);

            if (selected == null)
            {
                _eventRuleIndex =
                    Mathf.Clamp(
                        _eventRuleIndex,
                        0,
                        ruleCount - 1);
                selected =
                    eventRuntime.GetRuleAt(
                        _eventRuleIndex);
                ruleId =
                    selected?.Id;
            }

            if (selected == null ||
                string.IsNullOrWhiteSpace(
                    ruleId))
            {
                _lastActionMessage =
                    "Select a valid event rule first.";
                RefreshAll();
                return;
            }

            var enable =
                !selected.Enabled;

            if (!eventRuntime.TrySetRuleEnabled(
                    ruleId,
                    enable,
                    out var error))
            {
                _lastActionMessage =
                    "Event rule toggle failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Event rule '{ruleId}' " +
                (enable
                    ? "enabled."
                    : "disabled.");
            RefreshAll();
        }

        private void ToggleEventRuleTracing()
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            var enabled =
                !eventRuntime.Engine.TraceEnabled;
            eventRuntime.SetRuleTracingEnabled(
                enabled);

            _lastActionMessage =
                "Event rule tracing " +
                (enabled
                    ? "enabled."
                    : "disabled.");
            RefreshAll();
        }

        private void ApplyEventMaxCommands()
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            if (!int.TryParse(
                    _eventMaxCommandsInput?.text?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                value < 1 ||
                value > 256)
            {
                _lastActionMessage =
                    "Event max commands must be an integer in the 1..256 range.";
                RefreshAll();
                return;
            }

            if (!eventRuntime.TrySetMaxCommandsPerEvent(
                    value,
                    out var error))
            {
                _lastActionMessage =
                    "Event max-command update failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Event max commands set to {value}.";
            RefreshAll();
        }

        private void SaveEventRules()
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            EnsureEventRulesLoaded();

            if (_eventRuleStore == null)
            {
                _lastActionMessage =
                    "Event rule store unavailable.";
                RefreshAll();
                return;
            }

            if (!_eventRuleStore.TrySave(
                    eventRuntime.CaptureRules(),
                    eventRuntime.MaxCommandsPerEvent,
                    out var error))
            {
                _lastActionMessage =
                    "Event rule save failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                "Event rules saved to " +
                _eventRuleStore.Path;
            RefreshAll();
        }

        private void ReloadEventRules()
        {
            if (eventRuntime == null)
            {
                _lastActionMessage =
                    "Event runtime unavailable.";
                RefreshAll();
                return;
            }

            EnsureEventRulesLoaded();

            if (_eventRuleStore == null ||
                string.IsNullOrWhiteSpace(
                    _eventRuleStorePath) ||
                !File.Exists(
                    _eventRuleStorePath))
            {
                _lastActionMessage =
                    "No saved event rule document is available to reload.";
                RefreshAll();
                return;
            }

            if (!_eventRuleStore.TryLoad(
                    out var rules,
                    out var maxCommands,
                    out var error))
            {
                _lastActionMessage =
                    "Event rule reload failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            eventRuntime.SetRules(
                rules);

            if (!eventRuntime.TrySetMaxCommandsPerEvent(
                    maxCommands,
                    out error))
            {
                _lastActionMessage =
                    "Event max-command reload failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _eventRuleIndex = 0;
            _lastActionMessage =
                $"Reloaded {rules.Length} event rule(s).";
            RefreshAll();
        }

        private void RefreshEventControlState()
        {
            var ruleCount =
                eventRuntime?.RuleCount ??
                0;
            var hasRules =
                ruleCount > 0;

            _eventRuleIndex =
                hasRules
                    ? Mathf.Clamp(
                        _eventRuleIndex,
                        0,
                        ruleCount - 1)
                    : 0;

            var selected =
                hasRules
                    ? eventRuntime.GetRuleAt(
                        _eventRuleIndex)
                    : null;

            if (_eventRuleInput != null &&
                !_eventRuleInput.isFocused)
            {
                SetInputTextIfChanged(
                    _eventRuleInput,
                    selected?.Id ??
                    string.Empty);
            }

            if (_eventPreviousRuleButton != null)
            {
                _eventPreviousRuleButton.interactable =
                    ruleCount > 1;
            }

            if (_eventNextRuleButton != null)
            {
                _eventNextRuleButton.interactable =
                    ruleCount > 1;
            }

            if (_eventToggleRuleButton != null)
            {
                _eventToggleRuleButton.interactable =
                    selected != null;
                SetButtonLabel(
                    _eventToggleRuleButton,
                    selected == null
                        ? "No Rule"
                        : selected.Enabled
                            ? "Disable Rule"
                            : "Enable Rule");
            }

            if (_eventTraceButton != null)
            {
                _eventTraceButton.interactable =
                    eventRuntime != null;
                SetButtonLabel(
                    _eventTraceButton,
                    "Trace: " +
                    (eventRuntime?.Engine.TraceEnabled ==
                        true
                        ? "On"
                        : "Off"));
            }

            if (_eventMaxCommandsInput != null &&
                !_eventMaxCommandsInput.isFocused &&
                eventRuntime != null &&
                (!int.TryParse(
                     _eventMaxCommandsInput.text,
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out var displayedMaxCommands) ||
                 displayedMaxCommands !=
                    eventRuntime.MaxCommandsPerEvent))
            {
                SetInputTextIfChanged(
                    _eventMaxCommandsInput,
                    eventRuntime.MaxCommandsPerEvent
                        .ToString(
                            CultureInfo.InvariantCulture));
            }

            if (_eventApplyMaxCommandsButton != null)
            {
                _eventApplyMaxCommandsButton.interactable =
                    eventRuntime != null;
            }

            if (_eventSaveRulesButton != null)
            {
                _eventSaveRulesButton.interactable =
                    eventRuntime != null;
            }

            if (_eventReloadRulesButton != null)
            {
                _eventReloadRulesButton.interactable =
                    eventRuntime != null &&
                    !string.IsNullOrWhiteSpace(
                        _eventRuleStorePath) &&
                    File.Exists(
                        _eventRuleStorePath);
            }
        }

        private void SelectPreviousCapability()
        {
            SelectCapability(
                -1);
        }

        private void SelectNextCapability()
        {
            SelectCapability(
                1);
        }

        private void SelectCapability(
            int offset)
        {
            var registry =
                sceneRuntime?.Capabilities;
            var statusCount =
                registry?.StatusCount ??
                0;

            if (statusCount == 0)
            {
                _settingsCapabilityIndex = 0;
                _lastActionMessage =
                    "No runtime capabilities are registered.";
                RefreshAll();
                return;
            }

            _settingsCapabilityIndex =
                (_settingsCapabilityIndex +
                 offset +
                 statusCount) %
                statusCount;

            RefreshAll();
        }

        private void ToggleSelectedCapability()
        {
            var registry =
                sceneRuntime?.Capabilities;
            var statusCount =
                registry?.StatusCount ??
                0;

            if (registry == null ||
                sceneRuntime == null ||
                !ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        true,
                        sceneRuntime.State) ||
                statusCount == 0)
            {
                _lastActionMessage =
                    "No runtime capabilities are registered.";
                RefreshAll();
                return;
            }

            _settingsCapabilityIndex =
                Mathf.Clamp(
                    _settingsCapabilityIndex,
                    0,
                    statusCount - 1);

            if (!registry.TryGetStatusAt(
                    _settingsCapabilityIndex,
                    out var selected))
            {
                _lastActionMessage =
                    "Selected runtime capability is unavailable.";
                RefreshAll();
                return;
            }

            if (selected.State ==
                CapabilityState.Enabled)
            {
                if (!registry.Disable(
                        selected.Id))
                {
                    _lastActionMessage =
                        $"Capability '{selected.Id}' could not be disabled.";
                    RefreshAll();
                    return;
                }

                _lastActionMessage =
                    $"Capability '{selected.Id}' disabled.";
                RefreshAll();
                return;
            }

            if (!registry.Enable(
                    selected.Id,
                    out var error))
            {
                _lastActionMessage =
                    $"Capability '{selected.Id}' enable failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Capability '{selected.Id}' enabled.";
            RefreshAll();
        }

        private void ApplySettingsRenderScale()
        {
            if (sceneRuntime == null ||
                !float.TryParse(
                    _settingsRenderScaleInput?.text?.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                float.IsNaN(
                    value) ||
                float.IsInfinity(
                    value) ||
                value < 0.5f ||
                value > 2.0f)
            {
                _lastActionMessage =
                    "Render scale must be a finite number in the 0.5..2.0 range.";
                RefreshAll();
                return;
            }

            var settings =
                sceneRuntime.CaptureRenderSettings();
            settings.RenderScale =
                value;

            ApplySettingsRender(
                settings,
                $"Render scale set to {value:0.###}.");
        }

        private void ApplySettingsFps()
        {
            if (sceneRuntime == null ||
                !int.TryParse(
                    _settingsFpsInput?.text?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                value < 30 ||
                value > 240)
            {
                _lastActionMessage =
                    "Target FPS must be an integer in the 30..240 range.";
                RefreshAll();
                return;
            }

            var settings =
                sceneRuntime.CaptureRenderSettings();
            settings.TargetFrameRate =
                value;

            ApplySettingsRender(
                settings,
                $"Target FPS set to {value}.");
        }

        private void ToggleSettingsVsync()
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Render settings unavailable.";
                RefreshAll();
                return;
            }

            var settings =
                sceneRuntime.CaptureRenderSettings();
            settings.UseVSync =
                !settings.UseVSync;

            ApplySettingsRender(
                settings,
                "VSync " +
                (settings.UseVSync
                    ? "enabled."
                    : "disabled."));
        }

        private void ToggleSettingsRunInBackground()
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Render settings unavailable.";
                RefreshAll();
                return;
            }

            var settings =
                sceneRuntime.CaptureRenderSettings();
            settings.RunInBackground =
                !settings.RunInBackground;

            ApplySettingsRender(
                settings,
                "Run in background " +
                (settings.RunInBackground
                    ? "enabled."
                    : "disabled."));
        }

        private void ApplySettingsRender(
            RenderRuntimeSettings settings,
            string successMessage)
        {
            if (sceneRuntime == null ||
                !ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        true,
                        sceneRuntime.State))
            {
                _lastActionMessage =
                    "Render settings cannot be changed while the scene runtime is busy.";
                RefreshAll();
                return;
            }

            try
            {
                sceneRuntime.ApplyRenderSettings(
                    settings);
                _lastActionMessage =
                    successMessage;
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Render settings apply failed: " +
                    exception.Message;
            }

            RefreshAll();
        }

        private void RefreshSettingsControlState()
        {
            var registry =
                sceneRuntime?.Capabilities;
            var statusCount =
                registry?.StatusCount ??
                0;
            var hasCapabilities =
                statusCount > 0;

            _settingsCapabilityIndex =
                hasCapabilities
                    ? Mathf.Clamp(
                        _settingsCapabilityIndex,
                        0,
                        statusCount - 1)
                    : 0;

            CapabilityStatusSnapshot selected =
                default;
            var hasSelected =
                hasCapabilities &&
                registry.TryGetStatusAt(
                    _settingsCapabilityIndex,
                    out selected);
            var canMutate =
                sceneRuntime != null &&
                ApplicationUiActionPolicy
                    .CanApplyRuntimeSettings(
                        true,
                        sceneRuntime.State);

            if (_settingsPreviousCapabilityButton != null)
            {
                _settingsPreviousCapabilityButton.interactable =
                    statusCount > 1 &&
                    canMutate;
            }

            if (_settingsNextCapabilityButton != null)
            {
                _settingsNextCapabilityButton.interactable =
                    statusCount > 1 &&
                    canMutate;
            }

            if (_settingsToggleCapabilityButton != null)
            {
                _settingsToggleCapabilityButton.interactable =
                    hasSelected &&
                    canMutate;
                SetButtonLabel(
                    _settingsToggleCapabilityButton,
                    hasSelected
                        ? (selected.State ==
                            CapabilityState.Enabled
                            ? "Disable "
                            : selected.State ==
                                CapabilityState.Faulted
                                ? "Retry "
                                : "Enable ") +
                          selected.Id
                        : "No Capabilities");
            }

            RenderRuntimeSettings renderSettings =
                default;

            if (sceneRuntime != null)
            {
                renderSettings =
                    sceneRuntime.CaptureRenderSettings();
            }

            if (_settingsRenderScaleInput != null &&
                !_settingsRenderScaleInput.isFocused &&
                sceneRuntime != null &&
                (!float.TryParse(
                     _settingsRenderScaleInput.text,
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out var displayedRenderScale) ||
                 !Mathf.Approximately(
                     displayedRenderScale,
                     renderSettings.RenderScale)))
            {
                SetInputTextIfChanged(
                    _settingsRenderScaleInput,
                    renderSettings.RenderScale
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture));
            }

            if (_settingsFpsInput != null &&
                !_settingsFpsInput.isFocused &&
                sceneRuntime != null &&
                (!int.TryParse(
                     _settingsFpsInput.text,
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out var displayedTargetFps) ||
                 displayedTargetFps !=
                    renderSettings.TargetFrameRate))
            {
                SetInputTextIfChanged(
                    _settingsFpsInput,
                    renderSettings.TargetFrameRate
                        .ToString(
                            CultureInfo.InvariantCulture));
            }

            if (_settingsApplyRenderScaleButton != null)
            {
                _settingsApplyRenderScaleButton.interactable =
                    canMutate;
            }

            if (_settingsApplyFpsButton != null)
            {
                _settingsApplyFpsButton.interactable =
                    canMutate;
            }

            if (_settingsVsyncButton != null)
            {
                _settingsVsyncButton.interactable =
                    canMutate;
                SetButtonLabel(
                    _settingsVsyncButton,
                    sceneRuntime != null
                        ? "VSync: " +
                          (renderSettings.UseVSync
                              ? "On"
                              : "Off")
                        : "VSync: n/a");
            }

            if (_settingsRunInBackgroundButton != null)
            {
                _settingsRunInBackgroundButton.interactable =
                    canMutate;
                SetButtonLabel(
                    _settingsRunInBackgroundButton,
                    sceneRuntime != null
                        ? "Background: " +
                          (renderSettings.RunInBackground
                              ? "On"
                              : "Off")
                        : "Background: n/a");
            }
        }

        private void ToggleOverlayTransparent()
        {
            ToggleOverlaySetting(
                transparent:
                    true,
                topmost:
                    false,
                clickThrough:
                    false);
        }

        private void ToggleOverlayTopmost()
        {
            ToggleOverlaySetting(
                transparent:
                    false,
                topmost:
                    true,
                clickThrough:
                    false);
        }

        private void ToggleOverlayClickThrough()
        {
            ToggleOverlaySetting(
                transparent:
                    false,
                topmost:
                    false,
                clickThrough:
                    true);
        }

        private void ToggleOverlaySetting(
            bool transparent,
            bool topmost,
            bool clickThrough)
        {
            if (sceneRuntime == null)
            {
                _lastActionMessage =
                    "Overlay output control is unavailable.";
                RefreshAll();
                return;
            }

            var output =
                sceneRuntime.OverlayOutput;

            if (output == null ||
                !ApplicationUiActionPolicy
                    .CanApplyOverlaySetting(
                        true,
                        sceneRuntime.State,
                        true))
            {
                _lastActionMessage =
                    output == null
                        ? "No overlay output adapter is configured."
                        : "Overlay output settings cannot be changed while the scene runtime is busy.";
                RefreshAll();
                return;
            }

            var current =
                output.Settings;
            var next =
                new OverlayOutputSettings(
                    transparent
                        ? !current.Transparent
                        : current.Transparent,
                    topmost
                        ? !current.Topmost
                        : current.Topmost,
                    clickThrough
                        ? !current.ClickThrough
                        : current.ClickThrough);

            try
            {
                sceneRuntime.ApplyOverlayOutput(
                    next);

                if (clickThrough &&
                    next.ClickThrough)
                {
                    _lastActionMessage =
                        "Click-through enabled. Mouse input will pass through the overlay window; use another control path or restart with click-through disabled if you need to regain mouse interaction.";
                }
                else
                {
                    _lastActionMessage =
                        $"Overlay settings applied: transparent={next.Transparent}, topmost={next.Topmost}, click-through={next.ClickThrough}.";
                }
            }
            catch (Exception exception)
            {
                _lastActionMessage =
                    "Overlay setting apply failed: " +
                    exception.Message;
            }

            RefreshAll();
        }

        private void Apply720p60()
        {
            ApplyBroadcastTarget(
                BroadcastCaptureTarget
                    .Minimum720p60,
                "720p60");
        }

        private void Apply1080p60()
        {
            ApplyBroadcastTarget(
                BroadcastCaptureTarget
                    .Recommended1080p60,
                "1080p60");
        }

        private void ApplyBroadcastTarget(
            BroadcastCaptureTarget target,
            string label)
        {
            if (sceneRuntime == null ||
                !ApplicationUiActionPolicy
                    .CanApplyBroadcastTarget(
                        true,
                        sceneRuntime.State))
            {
                _lastActionMessage =
                    label +
                    " target apply unavailable.";
                RefreshAll();
                return;
            }

            if (sceneRuntime
                .TryApplyBroadcastCaptureTarget(
                    target,
                    out var error))
            {
                _lastActionMessage =
                    label +
                    " target applied.";
            }
            else
            {
                _lastActionMessage =
                    label +
                    " target apply failed: " +
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

            for (var i = 0;
                 i < (int)ApplicationUiSection.Count;
                 i++)
            {
                var section =
                    (ApplicationUiSection)i;

                if (!_sectionButtons.TryGetValue(
                        section,
                        out var button) ||
                    button == null)
                {
                    continue;
                }

                var available =
                    _model.IsAvailable(
                        section);

                button.interactable =
                    available;

                if (_sectionLabels.TryGetValue(
                        section,
                        out var label) &&
                    label != null)
                {
                    var title =
                        ApplicationUiModel.GetTitle(
                            section);

                    SetSectionLabel(
                        label,
                        title,
                        available);
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

            RefreshContextActions();

            RefreshStatus();
            RefreshContent();
        }

        private void RefreshContextActions()
        {
            var selected =
                _model.SelectedSection;
            var characterSelected =
                selected ==
                ApplicationUiSection.Character;
            var outputSelected =
                selected ==
                ApplicationUiSection.CameraOutput;
            var trackingSelected =
                selected ==
                ApplicationUiSection.Tracking;
            var motionSelected =
                selected ==
                ApplicationUiSection.MotionExpression;
            var environmentSelected =
                selected ==
                ApplicationUiSection.Environment;
            var materialSelected =
                selected ==
                ApplicationUiSection.MaterialShader;
            var eventsSelected =
                selected ==
                ApplicationUiSection.Events;
            var settingsSelected =
                selected ==
                ApplicationUiSection.Settings;
            var diagnosticsSelected =
                selected ==
                ApplicationUiSection.Diagnostics;

            if (_appearanceActions != null &&
                _appearanceActions.gameObject.activeSelf !=
                    characterSelected)
            {
                _appearanceActions.gameObject.SetActive(
                    characterSelected);
            }

            if (_appearanceDirectActions != null &&
                _appearanceDirectActions.gameObject.activeSelf !=
                    characterSelected)
            {
                _appearanceDirectActions.gameObject.SetActive(
                    characterSelected);
            }

            if (_appearancePersistenceActions != null &&
                _appearancePersistenceActions.gameObject.activeSelf !=
                    characterSelected)
            {
                _appearancePersistenceActions.gameObject.SetActive(
                    characterSelected);
            }

            if (_appearancePresetManagementActions != null &&
                _appearancePresetManagementActions.gameObject.activeSelf !=
                    characterSelected)
            {
                _appearancePresetManagementActions.gameObject.SetActive(
                    characterSelected);
            }

            SetActive(
                _characterPathInput,
                characterSelected);
            SetActive(
                _characterBrowseButton,
                characterSelected);
            SetActive(
                _loadCharacterButton,
                characterSelected);
            SetActive(
                _reloadCharacterButton,
                characterSelected);
            SetActive(
                _unloadCharacterButton,
                characterSelected);
            SetActive(
                _trackingPreviousButton,
                trackingSelected);
            SetActive(
                _trackingToggleButton,
                trackingSelected);
            SetActive(
                _trackingRecoverButton,
                trackingSelected);
            SetActive(
                _trackingNextButton,
                trackingSelected);

            SetActive(
                _apply720p60Button,
                outputSelected);
            SetActive(
                _apply1080p60Button,
                outputSelected);
            SetActive(
                _outputTransparentButton,
                outputSelected);
            SetActive(
                _outputTopmostButton,
                outputSelected);
            SetActive(
                _outputClickThroughButton,
                outputSelected);

            SetActive(
                _motionPoseWeightLabel,
                motionSelected);
            SetActive(
                _motionPoseWeightSlider,
                motionSelected);
            SetActive(
                _manualExpressionNameInput,
                motionSelected);
            SetActive(
                _manualExpressionValueInput,
                motionSelected);
            SetActive(
                _manualExpressionApplyButton,
                motionSelected);
            SetActive(
                _manualExpressionClearButton,
                motionSelected);
            SetActive(
                _manualExpressionClearAllButton,
                motionSelected);

            SetActive(
                _environmentStateInput,
                environmentSelected);
            SetActive(
                _environmentTransitionModeButton,
                environmentSelected);
            SetActive(
                _environmentTransitionDurationInput,
                environmentSelected);
            SetActive(
                _environmentApplyStateButton,
                environmentSelected);

            SetActive(
                _materialPreviousSlotButton,
                materialSelected);
            SetActive(
                _materialNextSlotButton,
                materialSelected);
            SetActive(
                _materialSlotInput,
                materialSelected);
            SetActive(
                _materialShaderInput,
                materialSelected);
            SetActive(
                _materialApplyShaderButton,
                materialSelected);
            SetActive(
                _materialPropertyInput,
                materialSelected);
            SetActive(
                _materialValueInput,
                materialSelected);
            SetActive(
                _materialSetFloatButton,
                materialSelected);
            SetActive(
                _materialClearOverrideButton,
                materialSelected);
            SetActive(
                _materialRefreshSlotsButton,
                materialSelected);

            SetActive(
                _eventPreviousRuleButton,
                eventsSelected);
            SetActive(
                _eventNextRuleButton,
                eventsSelected);
            SetActive(
                _eventRuleInput,
                eventsSelected);
            SetActive(
                _eventToggleRuleButton,
                eventsSelected);
            SetActive(
                _eventTraceButton,
                eventsSelected);
            SetActive(
                _eventMaxCommandsInput,
                eventsSelected);
            SetActive(
                _eventApplyMaxCommandsButton,
                eventsSelected);
            SetActive(
                _eventSaveRulesButton,
                eventsSelected);
            SetActive(
                _eventReloadRulesButton,
                eventsSelected);

            SetActive(
                _settingsPreviousCapabilityButton,
                settingsSelected);
            SetActive(
                _settingsNextCapabilityButton,
                settingsSelected);
            SetActive(
                _settingsToggleCapabilityButton,
                settingsSelected);
            SetActive(
                _settingsRenderScaleInput,
                settingsSelected);
            SetActive(
                _settingsApplyRenderScaleButton,
                settingsSelected);
            SetActive(
                _settingsFpsInput,
                settingsSelected);
            SetActive(
                _settingsApplyFpsButton,
                settingsSelected);
            SetActive(
                _settingsVsyncButton,
                settingsSelected);
            SetActive(
                _settingsRunInBackgroundButton,
                settingsSelected);

            SetActive(
                _diagnosticsPreviousPageButton,
                diagnosticsSelected);
            SetActive(
                _diagnosticsNextPageButton,
                diagnosticsSelected);
            SetActive(
                _diagnosticsCaptureButton,
                diagnosticsSelected);
            SetActive(
                _diagnosticsSaveSnapshotButton,
                diagnosticsSelected);
            SetActive(
                _diagnosticsCsvButton,
                diagnosticsSelected);
            SetActive(
                _diagnosticsConsoleButton,
                diagnosticsSelected);

            if (motionSelected)
            {
                RefreshMotionControlState();
            }

            if (environmentSelected)
            {
                RefreshEnvironmentControlState();
            }

            if (materialSelected)
            {
                RefreshMaterialControlState();
            }

            if (eventsSelected)
            {
                RefreshEventControlState();
            }

            if (settingsSelected)
            {
                RefreshSettingsControlState();
            }

            if (diagnosticsSelected)
            {
                RefreshDiagnosticsControlState();
            }

            if (characterSelected &&
                sceneRuntime != null)
            {
                var status =
                    sceneRuntime.Status;

                if (_characterPathInput != null &&
                    !_characterPathInput.isFocused &&
                    string.IsNullOrWhiteSpace(
                        _characterPathInput.text) &&
                    !string.IsNullOrWhiteSpace(
                        status.CurrentCharacterPath))
                {
                    _characterPathInput.text =
                        status.CurrentCharacterPath;
                }

                if (_characterBrowseButton != null)
                {
                    _characterBrowseButton.interactable =
                        ApplicationUiActionPolicy
                            .CanBrowseCharacterFile(
                                true,
                                status.State,
                                _characterFileSelectionAdapter !=
                                    null &&
                                _characterFileSelectionAdapter
                                    .IsSupported);
                }

                if (_loadCharacterButton != null)
                {
                    _loadCharacterButton.interactable =
                        ApplicationUiActionPolicy
                            .CanLoadCharacter(
                                true,
                                status.State,
                                _characterPathInput?.text);
                }

                if (_reloadCharacterButton != null)
                {
                    _reloadCharacterButton.interactable =
                        ApplicationUiActionPolicy
                            .CanReloadCharacter(
                                true,
                                status.State,
                                status.HasCharacter,
                                status.CurrentCharacterPath);
                }

                if (_unloadCharacterButton != null)
                {
                    _unloadCharacterButton.interactable =
                        ApplicationUiActionPolicy
                            .CanUnloadCharacter(
                                true,
                                status.State,
                                status.HasCharacter);
                }
            }

            if (outputSelected &&
                sceneRuntime != null)
            {
                var status =
                    sceneRuntime.Status;
                var output =
                    sceneRuntime.OverlayOutput;
                var canApplyOverlay =
                    ApplicationUiActionPolicy
                        .CanApplyOverlaySetting(
                            true,
                            status.State,
                            output != null);

                if (_outputTransparentButton != null)
                {
                    _outputTransparentButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputTransparentButton,
                        output != null
                            ? "Transparent: " +
                              (output.Settings.Transparent
                                  ? "On"
                                  : "Off")
                            : "Transparent: n/a");
                }

                if (_outputTopmostButton != null)
                {
                    _outputTopmostButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputTopmostButton,
                        output != null
                            ? "Topmost: " +
                              (output.Settings.Topmost
                                  ? "On"
                                  : "Off")
                            : "Topmost: n/a");
                }

                if (_outputClickThroughButton != null)
                {
                    _outputClickThroughButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputClickThroughButton,
                        output != null
                            ? "Click-through: " +
                              (output.Settings.ClickThrough
                                  ? "On"
                                  : "Off")
                            : "Click-through: n/a");
                }
            }

            if (trackingSelected)
            {
                var control =
                    GetSelectedTrackingControl();
                var hasControl =
                    control != null;

                if (_trackingPreviousButton != null)
                {
                    _trackingPreviousButton.interactable =
                        _trackingControls.Count > 1;
                }

                if (_trackingNextButton != null)
                {
                    _trackingNextButton.interactable =
                        _trackingControls.Count > 1;
                }

                if (_trackingToggleButton != null)
                {
                    _trackingToggleButton.interactable =
                        hasControl;

                    SetButtonLabel(
                        _trackingToggleButton,
                        hasControl
                            ? (control.ControlEnabled
                                ? "Disable "
                                : "Enable ") +
                              control.DisplayName
                            : "No Tracking Source");
                }

                if (_trackingRecoverButton != null)
                {
                    _trackingRecoverButton.interactable =
                        hasControl;
                }
            }

            if (characterSelected)
            {
                var appearanceRuntimeAlive =
                    IsServiceAlive(
                        _appearanceRuntime);
                var appearanceStatus =
                    appearanceRuntimeAlive
                        ? _appearanceRuntime.Status
                        : default;
                var appearanceState =
                    appearanceRuntimeAlive
                        ? appearanceStatus.State
                        : AppearanceRuntimeState.Unconfigured;
                var currentAppearance =
                    appearanceRuntimeAlive
                        ? GetCurrentAppearanceSnapshot()
                        : default;
                var appearanceAvailable =
                    ApplicationUiActionPolicy
                        .CanMutateAppearance(
                            appearanceRuntimeAlive,
                            appearanceState);

                if (_appearancePreviousButton != null)
                {
                    _appearancePreviousButton.interactable =
                        appearanceAvailable &&
                        _appearanceRuntime.PresetIds.Count > 0 &&
                        !appearanceStatus.Busy;
                }

                if (_appearanceNextButton != null)
                {
                    _appearanceNextButton.interactable =
                        appearanceAvailable &&
                        _appearanceRuntime.PresetIds.Count > 0 &&
                        !appearanceStatus.Busy;
                }

                if (_appearanceTransitionButton != null)
                {
                    _appearanceTransitionButton.interactable =
                        appearanceAvailable &&
                        !appearanceStatus.Busy;

                    var transitionLabel =
                        appearanceStatus.Busy &&
                        !string.IsNullOrWhiteSpace(
                            appearanceStatus.ActiveTransitionId)
                            ? appearanceStatus.ActiveTransitionId +
                              " " +
                              Math.Round(
                                  appearanceStatus
                                      .TransitionProgress01 *
                                  100.0) +
                              "%"
                            : GetSelectedAppearanceTransitionId();

                    SetButtonLabel(
                        _appearanceTransitionButton,
                        "Transition: " +
                        transitionLabel);
                }

                if (_appearanceCancelButton != null)
                {
                    _appearanceCancelButton.interactable =
                        appearanceRuntimeAlive &&
                        ApplicationUiActionPolicy
                            .CanCancelAppearanceTransition(
                                true,
                                appearanceStatus);
                }

                if (_appearanceRestoreButton != null)
                {
                    _appearanceRestoreButton.interactable =
                        appearanceAvailable &&
                        !appearanceStatus.Busy;
                }

                if (appearanceRuntimeAlive)
                {
                    var current =
                        currentAppearance;

                    if (_appearancePresetInput != null &&
                        !_appearancePresetInput.isFocused &&
                        string.IsNullOrWhiteSpace(
                            _appearancePresetInput.text) &&
                        !string.IsNullOrWhiteSpace(
                            current.PresetId))
                    {
                        _appearancePresetInput.text =
                            current.PresetId;
                    }

                    var currentUserPresetRegistry =
                        _appearanceRuntime as
                            IAppearanceUserPresetRegistry;

                    if (_appearanceUserPresetInput != null &&
                        !_appearanceUserPresetInput.isFocused &&
                        string.IsNullOrWhiteSpace(
                            _appearanceUserPresetInput.text) &&
                        IsUserPresetId(
                            currentUserPresetRegistry,
                            current.PresetId))
                    {
                        _appearanceUserPresetInput.text =
                            current.PresetId;
                    }

                    if (_appearanceOutfitInput != null &&
                        !_appearanceOutfitInput.isFocused &&
                        string.IsNullOrWhiteSpace(
                            _appearanceOutfitInput.text) &&
                        !string.IsNullOrWhiteSpace(
                            current.OutfitId))
                    {
                        _appearanceOutfitInput.text =
                            current.OutfitId;
                    }

                    if (current.Accessories != null &&
                        current.Accessories.Length > 0)
                    {
                        var accessory =
                            current.Accessories[0];

                        if (_appearanceAccessorySlotInput != null &&
                            !_appearanceAccessorySlotInput.isFocused &&
                            string.IsNullOrWhiteSpace(
                                _appearanceAccessorySlotInput.text))
                        {
                            _appearanceAccessorySlotInput.text =
                                accessory?.SlotId ?? string.Empty;
                        }

                        if (_appearanceAccessoryInput != null &&
                            !_appearanceAccessoryInput.isFocused &&
                            string.IsNullOrWhiteSpace(
                                _appearanceAccessoryInput.text))
                        {
                            _appearanceAccessoryInput.text =
                                accessory?.AccessoryId ?? string.Empty;
                        }
                    }
                }

                if (_appearanceApplyPresetButton != null)
                {
                    _appearanceApplyPresetButton.interactable =
                        ApplicationUiActionPolicy
                            .CanApplyAppearancePreset(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearancePresetInput?.text);
                }

                if (_appearanceApplyOutfitButton != null)
                {
                    _appearanceApplyOutfitButton.interactable =
                        ApplicationUiActionPolicy
                            .CanApplyAppearanceOutfit(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceOutfitInput?.text);
                }

                if (_appearanceSetAccessoryButton != null)
                {
                    _appearanceSetAccessoryButton.interactable =
                        ApplicationUiActionPolicy
                            .CanSetAppearanceAccessory(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceAccessorySlotInput?.text,
                                _appearanceAccessoryInput?.text);
                }

                if (_appearanceClearAccessoryButton != null)
                {
                    _appearanceClearAccessoryButton.interactable =
                        ApplicationUiActionPolicy
                            .CanClearAppearanceAccessory(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceAccessorySlotInput?.text);
                }

                if (_appearancePreviewButton != null)
                {
                    _appearancePreviewButton.interactable =
                        ApplicationUiActionPolicy
                            .CanPreviewAppearanceTransition(
                                appearanceRuntimeAlive,
                                appearanceState,
                                GetSelectedAppearanceTransitionId(),
                                appearanceRuntimeAlive
                                    ? currentAppearance.OutfitId
                                    : null);
                }

                var presetRegistry =
                    IsServiceAlive(_appearanceRuntime)
                        ? _appearanceRuntime as
                            IAppearanceUserPresetRegistry
                        : null;
                var hasPresetRegistry =
                    presetRegistry != null;
                var selectedUserPresetId =
                    _appearanceUserPresetInput?.text?.Trim();
                var selectedUserPresetIndex =
                    -1;

                if (presetRegistry != null &&
                    !string.IsNullOrWhiteSpace(
                        selectedUserPresetId))
                {
                    for (var i = 0;
                         i <
                         presetRegistry.UserPresetIds.Count;
                         i++)
                    {
                        if (string.Equals(
                                presetRegistry.UserPresetIds[i],
                                selectedUserPresetId,
                                StringComparison.Ordinal))
                        {
                            selectedUserPresetIndex =
                                i;
                            break;
                        }
                    }
                }

                if (_appearanceSaveUserPresetButton != null)
                {
                    _appearanceSaveUserPresetButton.interactable =
                        hasPresetRegistry &&
                        ApplicationUiActionPolicy
                            .CanSaveAppearanceUserPreset(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceUserPresetInput?.text,
                                sceneRuntime?.CurrentCharacterPath);
                }

                if (_appearanceDeleteUserPresetButton != null)
                {
                    _appearanceDeleteUserPresetButton.interactable =
                        hasPresetRegistry &&
                        ApplicationUiActionPolicy
                            .CanDeleteAppearanceUserPreset(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceUserPresetInput?.text,
                                sceneRuntime?.CurrentCharacterPath);
                }

                if (_appearanceRenameUserPresetButton != null)
                {
                    _appearanceRenameUserPresetButton.interactable =
                        hasPresetRegistry &&
                        ApplicationUiActionPolicy
                            .CanRenameAppearanceUserPreset(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceUserPresetInput?.text,
                                _appearanceUserPresetTargetInput?.text,
                                sceneRuntime?.CurrentCharacterPath);
                }

                if (_appearanceDuplicateUserPresetButton != null)
                {
                    _appearanceDuplicateUserPresetButton.interactable =
                        hasPresetRegistry &&
                        ApplicationUiActionPolicy
                            .CanDuplicateAppearanceUserPreset(
                                IsServiceAlive(_appearanceRuntime),
                                appearanceState,
                                _appearanceUserPresetInput?.text,
                                _appearanceUserPresetTargetInput?.text,
                                sceneRuntime?.CurrentCharacterPath);
                }

                var canMoveUserPreset =
                    hasPresetRegistry &&
                    ApplicationUiActionPolicy
                        .CanMoveAppearanceUserPreset(
                            IsServiceAlive(_appearanceRuntime),
                            appearanceState,
                            _appearanceUserPresetInput?.text,
                            sceneRuntime?.CurrentCharacterPath);

                if (_appearanceMoveUserPresetUpButton != null)
                {
                    _appearanceMoveUserPresetUpButton.interactable =
                        canMoveUserPreset &&
                        selectedUserPresetIndex >
                            0;
                }

                if (_appearanceMoveUserPresetDownButton != null)
                {
                    _appearanceMoveUserPresetDownButton.interactable =
                        canMoveUserPreset &&
                        selectedUserPresetIndex >=
                            0 &&
                        presetRegistry != null &&
                        selectedUserPresetIndex <
                            presetRegistry.UserPresetIds.Count -
                            1;
                }
            }

            if (outputSelected &&
                sceneRuntime != null)
            {
                var canApply =
                    ApplicationUiActionPolicy
                        .CanApplyBroadcastTarget(
                            true,
                            sceneRuntime.State);

                if (_apply720p60Button != null)
                {
                    _apply720p60Button.interactable =
                        canApply;
                }

                if (_apply1080p60Button != null)
                {
                    _apply1080p60Button.interactable =
                        canApply;
                }
            }
        }

        private void RefreshMaterialControlState()
        {
            var slotCount =
                _materialController?.SlotCount ??
                0;

            _materialSlotIndex =
                Mathf.Clamp(
                    _materialSlotIndex,
                    0,
                    Mathf.Max(
                        0,
                        slotCount - 1));

            if (_materialSlotInput != null &&
                !_materialSlotInput.isFocused &&
                string.IsNullOrWhiteSpace(
                    _materialSlotInput.text) &&
                _materialController != null &&
                _materialController.TryGetSlotAt(
                    _materialSlotIndex,
                    out var selectedSlot))
            {
                _materialSlotInput.text =
                    selectedSlot.Id;
            }

            var slotId =
                _materialSlotInput
                    ?.text
                    ?.Trim();
            var slotValid =
                _materialController != null &&
                !string.IsNullOrWhiteSpace(
                    slotId) &&
                _materialController.TryGetStatus(
                    slotId,
                    out _);

            if (_materialPreviousSlotButton != null)
            {
                _materialPreviousSlotButton.interactable =
                    slotCount >
                    1;
            }

            if (_materialNextSlotButton != null)
            {
                _materialNextSlotButton.interactable =
                    slotCount >
                    1;
            }

            if (_materialApplyShaderButton != null)
            {
                _materialApplyShaderButton.interactable =
                    slotValid &&
                    !string.IsNullOrWhiteSpace(
                        _materialShaderInput
                            ?.text);
            }

            var floatValueValid =
                float.TryParse(
                    _materialValueInput
                        ?.text
                        ?.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var floatValue) &&
                !float.IsNaN(
                    floatValue) &&
                !float.IsInfinity(
                    floatValue);

            if (_materialSetFloatButton != null)
            {
                _materialSetFloatButton.interactable =
                    slotValid &&
                    !string.IsNullOrWhiteSpace(
                        _materialPropertyInput
                            ?.text) &&
                    floatValueValid;
            }

            if (_materialClearOverrideButton != null)
            {
                _materialClearOverrideButton.interactable =
                    slotValid;
            }

            if (_materialRefreshSlotsButton != null)
            {
                _materialRefreshSlotsButton.interactable =
                    _materialController != null;
            }
        }

        private void RefreshEnvironmentControlState()
        {
            var runtime =
                sceneRuntime?.EnvironmentRuntime;
            var status =
                runtime?.Status;

            if (_environmentStateInput != null &&
                !_environmentStateInput.isFocused &&
                string.IsNullOrWhiteSpace(
                    _environmentStateInput.text) &&
                status.HasValue &&
                !string.IsNullOrWhiteSpace(
                    status.Value.StateId))
            {
                _environmentStateInput.text =
                    status.Value.StateId;
            }

            if (_environmentTransitionModeButton != null)
            {
                SetButtonLabel(
                    _environmentTransitionModeButton,
                    "Transition: " +
                    _environmentTransitionMode);
                _environmentTransitionModeButton.interactable =
                    runtime != null &&
                    status.HasValue &&
                    status.Value.Active;
            }

            var durationValid =
                float.TryParse(
                    _environmentTransitionDurationInput
                        ?.text
                        ?.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var duration) &&
                !float.IsNaN(
                    duration) &&
                !float.IsInfinity(
                    duration) &&
                duration >= 0f;
            var stateValid =
                !string.IsNullOrWhiteSpace(
                    _environmentStateInput
                        ?.text);

            if (_environmentApplyStateButton != null)
            {
                _environmentApplyStateButton.interactable =
                    runtime != null &&
                    status.HasValue &&
                    status.Value.Active &&
                    stateValid &&
                    durationValid;
            }
        }

        private void RefreshMotionControlState()
        {
            var poseConfigured =
                _mixer != null &&
                _mixer.PrimaryPoseLayerConfigured;
            var poseWeight =
                _mixer?.PrimaryPoseLayerWeight ??
                0f;

            if (_motionPoseWeightLabel != null)
            {
                SetTextIfChanged(
                    _motionPoseWeightLabel,
                    poseConfigured
                        ? "Pose Weight " +
                          poseWeight.ToString(
                              "0.00")
                        : "Pose Weight n/a");
            }

            if (_motionPoseWeightSlider != null)
            {
                _motionPoseWeightSlider
                    .SetValueWithoutNotify(
                        Mathf.Clamp01(
                            poseWeight));
                _motionPoseWeightSlider.interactable =
                    poseConfigured;
            }

            var manualLayerReady =
                _mixer != null &&
                _manualExpressionSource != null &&
                _mixer.IsExpressionLayerProvider(
                    _manualExpressionSource);
            var expressionValid =
                StandardExpressionNames
                    .TryParse(
                        _manualExpressionNameInput
                            ?.text
                            ?.Trim(),
                        out _);
            var valueValid =
                float.TryParse(
                    _manualExpressionValueInput
                        ?.text
                        ?.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedValue) &&
                !float.IsNaN(
                    parsedValue) &&
                !float.IsInfinity(
                    parsedValue) &&
                parsedValue >= 0f &&
                parsedValue <= 1f;

            if (_manualExpressionApplyButton != null)
            {
                _manualExpressionApplyButton.interactable =
                    manualLayerReady &&
                    expressionValid &&
                    valueValid;
            }

            if (_manualExpressionClearButton != null)
            {
                _manualExpressionClearButton.interactable =
                    manualLayerReady &&
                    expressionValid;
            }

            if (_manualExpressionClearAllButton != null)
            {
                _manualExpressionClearAllButton.interactable =
                    manualLayerReady;
            }
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

            SetTextIfChanged(
                _statusText,
                $"VCR  |  {sceneText}  |  {characterText}  |  {outputText}{suffix}");
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

            SetTextIfChanged(
                _sectionTitle,
                ApplicationUiModel
                    .GetTitle(selected));

            if (!_model.IsAvailable(
                    selected))
            {
                SetTextIfChanged(
                    _contentText,
                    _model.GetUnavailableReason(
                        selected) ??
                    "Section unavailable.");
                return;
            }

            var content =
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

            SetTextIfChanged(
                _contentText,
                content);
        }

        private string CharacterSummary()
        {
            if (sceneRuntime == null)
            {
                return "Scene runtime unavailable.";
            }

            var status =
                sceneRuntime.Status;

            var text =
                $"Runtime state: {status.State}\n" +
                $"Character loaded: {status.HasCharacter}\n" +
                $"Model path: {status.CurrentCharacterPath ?? "<none>"}\n" +
                $"Last runtime error: {status.LastError ?? "<none>"}";

            if (!IsServiceAlive(_appearanceRuntime))
            {
                return text +
                    "\nAppearance: runtime unavailable";
            }

            var appearance =
                _appearanceRuntime.Status;

            var userPresetRegistry =
                _appearanceRuntime as
                    IAppearanceUserPresetRegistry;
            var userPresetCount =
                userPresetRegistry?.UserPresetIds.Count ??
                0;
            var userPresetOrder =
                FormatUserPresetOrder(
                    userPresetRegistry);

            var transitionProgress =
                appearance.Busy
                    ? Math.Round(
                        appearance.TransitionProgress01 *
                        100.0) +
                      "% (" +
                      appearance.TransitionElapsedSeconds
                          .ToString("0.00") +
                      "s / " +
                      appearance.TransitionDurationSeconds
                          .ToString("0.00") +
                      "s)"
                    : "<idle>";

            return text +
                $"\nAppearance state: {appearance.State}" +
                $"\nAppearance preset: {appearance.CurrentPresetId ?? "<none>"}" +
                $"\nOutfit: {appearance.CurrentOutfitId ?? "<none>"}" +
                $"\nUser presets: {userPresetCount}" +
                $"\nUser preset order: {userPresetOrder}" +
                $"\nTransition: {appearance.ActiveTransitionId ?? "<none>"}" +
                $"\nTransition progress: {transitionProgress}" +
                $"\nTransition committed: {appearance.TransitionCommitted}" +
                $"\nTransition cancelable: {appearance.CanCancelTransition}" +
                $"\nAppearance error: {appearance.LastError ?? "<none>"}";
        }

        private static bool IsUserPresetId(
            IAppearanceUserPresetRegistry registry,
            string presetId)
        {
            if (registry == null ||
                string.IsNullOrWhiteSpace(
                    presetId))
            {
                return false;
            }

            for (var i = 0;
                 i <
                 registry.UserPresetIds.Count;
                 i++)
            {
                if (string.Equals(
                        registry.UserPresetIds[i],
                        presetId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string FormatUserPresetOrder(
            IAppearanceUserPresetRegistry registry)
        {
            if (registry == null ||
                registry.UserPresetIds.Count == 0)
            {
                return "<none>";
            }

            const int visibleLimit = 8;
            var visibleCount =
                Math.Min(
                    visibleLimit,
                    registry.UserPresetIds.Count);
            var builder =
                _summaryBuilder;
            builder.Clear();

            for (var i = 0;
                 i < visibleCount;
                 i++)
            {
                if (i > 0)
                {
                    builder.Append(
                        " > ");
                }

                builder.Append(
                    registry.UserPresetIds[i]);
            }

            if (registry.UserPresetIds.Count >
                visibleLimit)
            {
                builder.Append(
                    " > +");
                builder.Append(
                    registry.UserPresetIds.Count -
                    visibleLimit);
            }

            return builder.ToString();
        }

        private string TrackingSummary()
        {
            if (!IsServiceAlive(_trackingPresence))
            {
                return "Tracking provider unavailable.";
            }

            var presence =
                _trackingPresence.Presence;
            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Subject: ");
            builder.Append(presence.SubjectState);
            builder.Append("\nAny source available: ");
            builder.Append(presence.AnySourceAvailable);
            builder.Append("\nFace source: ");
            builder.Append(presence.FaceSourceAvailable);
            builder.Append("\nBody/hands source: ");
            builder.Append(presence.BodyHandsSourceAvailable);
            builder.Append("\nFull-body source: ");
            builder.Append(presence.FullBodySourceAvailable);
            builder.Append("\nEvents: ");
            builder.Append(presence.Events);

            if (_trackingControls.Count == 0)
            {
                builder.Append(
                    "\nSource controls: <none>");
                return builder.ToString();
            }

            builder.Append(
                "\nSource controls:\n");

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                var control =
                    _trackingControls[i];

                builder.Append(
                    i == _trackingControlIndex
                        ? '>'
                        : ' ');
                builder.Append(' ');
                builder.Append(control.DisplayName);
                builder.Append(": enabled=");
                builder.Append(control.ControlEnabled);
                builder.Append(", health=");
                builder.Append(control.ControlHealthState);
                builder.Append(", error=");
                builder.Append(
                    control.ControlError ??
                    "<none>");
            }

            return builder.ToString();
        }

        private string MotionSummary()
        {
            if (_mixer == null)
            {
                return "Motion/expression mixer unavailable.";
            }

            var manualConnected =
                _manualExpressionSource != null &&
                _mixer.IsExpressionLayerProvider(
                    _manualExpressionSource);
            var selectedExpression =
                _manualExpressionNameInput
                    ?.text
                    ?.Trim();
            var selectedValue =
                "<none>";

            if (_manualExpressionSource != null &&
                StandardExpressionNames.TryParse(
                    selectedExpression,
                    out var expression))
            {
                selectedValue =
                    _manualExpressionSource
                        .GetExpression(
                            expression)
                        .ToString(
                            "0.00");
            }

            return
                $"Primary pose layer configured: {_mixer.PrimaryPoseLayerConfigured}\n" +
                $"Primary pose weight: {_mixer.PrimaryPoseLayerWeight:0.00}\n" +
                $"Manual expression source: {(_manualExpressionSource != null ? "available" : "missing")}\n" +
                $"Manual layer connected: {manualConnected}\n" +
                $"Expression blend: {_mixer.ExpressionLayerBlendMode} @ {_mixer.ExpressionLayerWeight:0.00}\n" +
                $"Selected manual expression: {(string.IsNullOrWhiteSpace(selectedExpression) ? "<none>" : selectedExpression)} = {selectedValue}\n" +
                "Standard expressions: neutral, happy, angry, sad, relaxed, surprised, aa, ih, ou, ee, oh, blink, blinkLeft, blinkRight, lookUp, lookDown, lookLeft, lookRight";
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
            var transition =
                runtime.TransitionStatus;
            var transitionText =
                transition.Active
                    ? transition.Mode +
                      " " +
                      Math.Round(
                          transition.Progress *
                          100f) +
                      "% (" +
                      transition.PreviousStateId +
                      " → " +
                      transition.StateId +
                      ", " +
                      transition.DurationSeconds
                          .ToString("0.###") +
                      "s)"
                    : "<idle>";

            return
                $"Environment: {status.EnvironmentId ?? "<none>"}\n" +
                $"State: {status.StateId ?? "<none>"}\n" +
                $"Space: {runtime.SpaceMode}\n" +
                $"Active: {status.Active}\n" +
                $"Transition: {transitionText}\n" +
                $"Selected transition mode: {_environmentTransitionMode}\n" +
                $"Error: {status.Error ?? "<none>"}";
        }

        private string MaterialSummary()
        {
            if (_materialController == null)
            {
                return "Material controller unavailable.";
            }

            var slotCount =
                _materialController.SlotCount;
            var slotId =
                _materialSlotInput
                    ?.text
                    ?.Trim();
            var slotSummary =
                "<none>";

            if (!string.IsNullOrWhiteSpace(
                    slotId) &&
                _materialController.TryGetStatus(
                    slotId,
                    out var status))
            {
                slotSummary =
                    $"{status.SlotId}: {status.Health}, shader={status.ShaderId ?? "<none>"}, preset={status.PresetId ?? "<none>"}, error={status.Error ?? "<none>"}";
            }
            else if (slotCount > 0 &&
                     _materialController.TryGetSlotAt(
                         Mathf.Clamp(
                             _materialSlotIndex,
                             0,
                             slotCount - 1),
                         out var slot))
            {
                slotSummary =
                    $"{slot.Id}: source={slot.SourceMaterialName}, shader={slot.SourceShaderName}";
            }

            return
                $"Material slots: {_materialController.SlotCount}\n" +
                $"Selected slot: {slotSummary}\n" +
                $"Override errors: {_materialController.ErrorCount}\n" +
                "Controls: cycle slot, apply registered shader ID, set a float property, clear override, or refresh discovered slots.";
        }

        private string EventsSummary()
        {
            if (eventRuntime == null)
            {
                return "Event runtime unavailable.";
            }

            var ruleCount =
                eventRuntime.RuleCount;
            var selectedRule =
                ruleCount > 0
                    ? eventRuntime.GetRuleAt(
                        Mathf.Clamp(
                            _eventRuleIndex,
                            0,
                            ruleCount - 1))
                    : null;

            return
                $"Rules: {ruleCount}\n" +
                $"Selected rule: {selectedRule?.Id ?? "<none>"}\n" +
                $"Selected enabled: {(selectedRule != null ? selectedRule.Enabled.ToString() : "n/a")}\n" +
                $"Trace enabled: {eventRuntime.Engine.TraceEnabled}\n" +
                $"Max commands/event: {eventRuntime.MaxCommandsPerEvent}\n" +
                $"Persisted rules: {_eventRuleStorePath ?? "<not resolved>"}\n" +
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
            var statusCount =
                capabilities?.StatusCount ??
                0;
            CapabilityStatusSnapshot selectedCapability =
                default;
            var hasSelectedCapability =
                statusCount > 0 &&
                capabilities.TryGetStatusAt(
                    Mathf.Clamp(
                        _settingsCapabilityIndex,
                        0,
                        statusCount - 1),
                    out selectedCapability);
            var render =
                sceneRuntime != null
                    ? sceneRuntime.CaptureRenderSettings()
                    : RenderRuntimeSettings.Default1080p;

            return
                $"Runtime started: {applicationBootstrap.IsStarted}\n" +
                $"Configuration: {applicationBootstrap.ConfigurationPath ?? "<default/not resolved>"}\n" +
                $"Capabilities registered: {capabilities?.RegisteredCount ?? 0}\n" +
                $"Capabilities enabled: {capabilities?.EnabledCount ?? 0}\n" +
                $"Selected capability: {(hasSelectedCapability ? selectedCapability.Id : "<none>")}\n" +
                $"Capability state: {(hasSelectedCapability ? selectedCapability.State.ToString() : "n/a")}\n" +
                $"Capability error: {(hasSelectedCapability ? selectedCapability.Error ?? "<none>" : "n/a")}\n" +
                $"Render scale: {render.RenderScale:0.###}\n" +
                $"Target FPS: {render.TargetFrameRate}\n" +
                $"VSync: {render.UseVSync}\n" +
                $"Run in background: {render.RunInBackground}";
        }

        private RuntimeMetric[]
            GetSortedDiagnosticMetrics(
                RuntimeDiagnosticsSnapshot snapshot)
        {
            var source =
                snapshot.Metrics ??
                Array.Empty<RuntimeMetric>();

            if (!ReferenceEquals(
                    source,
                    _diagnosticsMetricSource))
            {
                _diagnosticsMetricSource =
                    source;
                _diagnosticsSortedMetrics =
                    source;
            }

            return _diagnosticsSortedMetrics;
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

            var metrics =
                GetSortedDiagnosticMetrics(
                    snapshot);

            const int pageSize = 12;
            var pageCount =
                Math.Max(
                    1,
                    (metrics.Length +
                     pageSize -
                     1) /
                    pageSize);
            _diagnosticsMetricPage =
                Mathf.Clamp(
                    _diagnosticsMetricPage,
                    0,
                    pageCount - 1);
            var start =
                _diagnosticsMetricPage *
                pageSize;
            var end =
                Math.Min(
                    metrics.Length,
                    start +
                    pageSize);

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.AppendLine(
                $"Snapshot: {snapshot.Sequence} @ {snapshot.RealtimeSeconds:0.00}s");
            builder.AppendLine(
                $"Frame: avg {snapshot.FrameAverageMs:F2} ms | P95 {snapshot.FrameP95Ms:F2} ms | P99 {snapshot.FrameP99Ms:F2} ms");
            builder.AppendLine(
                $"Tracking Hz: face {snapshot.FaceHz:F1} | body/hands {snapshot.BodyHandsHz:F1} | full-body {snapshot.FullBodyHz:F1} | expressions {snapshot.ExpressionHz:F1}");
            builder.AppendLine(
                $"Tracking age ms: face {FormatDiagnosticValue(snapshot.FaceAgeMs)} | body/hands {FormatDiagnosticValue(snapshot.BodyHandsAgeMs)} | full-body {FormatDiagnosticValue(snapshot.FullBodyAgeMs)} | expressions {FormatDiagnosticValue(snapshot.ExpressionAgeMs)}");

            if (snapshot.Presence.HasValue)
            {
                builder.AppendLine(
                    $"Presence: {snapshot.Presence.Value.SubjectState} | source available: {snapshot.Presence.Value.AnySourceAvailable}");
            }
            else
            {
                builder.AppendLine(
                    "Presence: n/a");
            }

            builder.AppendLine();
            builder.AppendLine(
                $"Reporting: every {diagnostics.ReportIntervalSeconds:0.###}s | console {diagnostics.ConsoleLoggingEnabled} | CSV {diagnostics.CsvEvidenceEnabled}");
            builder.AppendLine(
                $"Evidence directory: {diagnostics.EvidenceDirectory}");
            builder.AppendLine();
            builder.AppendLine(
                $"Subsystem metrics: {metrics.Length} | page {_diagnosticsMetricPage + 1}/{pageCount}");

            if (metrics.Length == 0)
            {
                builder.Append(
                    "<no subsystem metrics>");
            }
            else
            {
                for (var i = start;
                     i < end;
                     i++)
                {
                    var metric =
                        metrics[i];
                    builder.Append(
                        metric.Name);
                    builder.Append(
                        " = ");
                    builder.Append(
                        metric.Value.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture));

                    if (!string.IsNullOrWhiteSpace(
                            metric.Unit))
                    {
                        builder.Append(
                            ' ');
                        builder.Append(
                            metric.Unit);
                    }

                    if (i + 1 < end)
                    {
                        builder.AppendLine();
                    }
                }
            }

            return builder.ToString();
        }

        private static string FormatDiagnosticValue(
            double value)
        {
            return double.IsNaN(
                    value) ||
                double.IsInfinity(
                    value)
                    ? "n/a"
                    : value.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture);
        }

        private void CaptureDiagnosticsNow()
        {
            if (diagnostics == null)
            {
                _lastActionMessage =
                    "Runtime diagnostics unavailable.";
                RefreshAll();
                return;
            }

            if (!diagnostics.TryCaptureNow(
                    out var snapshot,
                    out var error))
            {
                _lastActionMessage =
                    "Diagnostics capture skipped: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _diagnosticsMetricPage = 0;
            _lastActionMessage =
                $"Diagnostics snapshot {snapshot.Sequence} captured.";
            RefreshAll();
        }

        private void SaveDiagnosticsSnapshot()
        {
            if (diagnostics == null)
            {
                _lastActionMessage =
                    "Runtime diagnostics unavailable.";
                RefreshAll();
                return;
            }

            if (diagnostics.LatestSnapshot.Sequence <= 0)
            {
                if (!diagnostics.TryCaptureNow(
                        out _,
                        out var captureError))
                {
                    _lastActionMessage =
                        "Diagnostics snapshot save requires a captured report: " +
                        (captureError ?? "unknown error");
                    RefreshAll();
                    return;
                }
            }

            if (!diagnostics.TryWriteLatestSnapshotJson(
                    out var path,
                    out var error))
            {
                _lastActionMessage =
                    "Diagnostics snapshot save failed: " +
                    (error ?? "unknown error");
                RefreshAll();
                return;
            }

            _lastActionMessage =
                $"Diagnostics snapshot saved: {path}";
            RefreshAll();
        }

        private void ToggleDiagnosticsCsvEvidence()
        {
            if (diagnostics == null)
            {
                _lastActionMessage =
                    "Runtime diagnostics unavailable.";
                RefreshAll();
                return;
            }

            diagnostics.SetCsvEvidence(
                !diagnostics.CsvEvidenceEnabled);

            _lastActionMessage =
                "Diagnostics CSV evidence " +
                (diagnostics.CsvEvidenceEnabled
                    ? "enabled."
                    : "disabled.");
            RefreshAll();
        }

        private void ToggleDiagnosticsConsoleLogging()
        {
            if (diagnostics == null)
            {
                _lastActionMessage =
                    "Runtime diagnostics unavailable.";
                RefreshAll();
                return;
            }

            diagnostics.SetConsoleLogging(
                !diagnostics.ConsoleLoggingEnabled);

            _lastActionMessage =
                "Diagnostics console logging " +
                (diagnostics.ConsoleLoggingEnabled
                    ? "enabled."
                    : "disabled.");
            RefreshAll();
        }

        private void SelectPreviousDiagnosticsMetricPage()
        {
            SelectDiagnosticsMetricPage(
                -1);
        }

        private void SelectNextDiagnosticsMetricPage()
        {
            SelectDiagnosticsMetricPage(
                1);
        }

        private void SelectDiagnosticsMetricPage(
            int offset)
        {
            var metrics =
                diagnostics?.LatestSnapshot.Metrics ??
                Array.Empty<RuntimeMetric>();
            const int pageSize = 12;
            var pageCount =
                Math.Max(
                    1,
                    (metrics.Length +
                     pageSize -
                     1) /
                    pageSize);

            _diagnosticsMetricPage =
                (_diagnosticsMetricPage +
                 offset +
                 pageCount) %
                pageCount;
            RefreshAll();
        }

        private void RefreshDiagnosticsControlState()
        {
            var metrics =
                diagnostics?.LatestSnapshot.Metrics ??
                Array.Empty<RuntimeMetric>();
            const int pageSize = 12;
            var pageCount =
                Math.Max(
                    1,
                    (metrics.Length +
                     pageSize -
                     1) /
                    pageSize);
            _diagnosticsMetricPage =
                Mathf.Clamp(
                    _diagnosticsMetricPage,
                    0,
                    pageCount - 1);
            var canPage =
                diagnostics != null &&
                diagnostics.LatestSnapshot.Sequence >
                    0 &&
                pageCount > 1;

            if (_diagnosticsPreviousPageButton != null)
            {
                _diagnosticsPreviousPageButton.interactable =
                    canPage;
                SetButtonLabel(
                    _diagnosticsPreviousPageButton,
                    "Prev Metrics");
            }

            if (_diagnosticsNextPageButton != null)
            {
                _diagnosticsNextPageButton.interactable =
                    canPage;
                SetButtonLabel(
                    _diagnosticsNextPageButton,
                    pageCount > 1
                        ? $"Next Metrics ({_diagnosticsMetricPage + 1}/{pageCount})"
                        : "Next Metrics");
            }

            var available =
                diagnostics != null;

            if (_diagnosticsCaptureButton != null)
            {
                _diagnosticsCaptureButton.interactable =
                    available;
            }

            if (_diagnosticsSaveSnapshotButton != null)
            {
                _diagnosticsSaveSnapshotButton.interactable =
                    available;
            }

            if (_diagnosticsCsvButton != null)
            {
                _diagnosticsCsvButton.interactable =
                    available;
                SetButtonLabel(
                    _diagnosticsCsvButton,
                    available
                        ? "CSV Evidence: " +
                          (diagnostics.CsvEvidenceEnabled
                              ? "On"
                              : "Off")
                        : "CSV Evidence: n/a");
            }

            if (_diagnosticsConsoleButton != null)
            {
                _diagnosticsConsoleButton.interactable =
                    available;
                SetButtonLabel(
                    _diagnosticsConsoleButton,
                    available
                        ? "Console Log: " +
                          (diagnostics.ConsoleLoggingEnabled
                              ? "On"
                              : "Off")
                        : "Console Log: n/a");
            }
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
            _buttonLabels[
                button] =
                    text;

            Stretch(
                text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 2f),
                new Vector2(-8f, -2f));

            return button;
        }

        private void ResolveCharacterFileSelectionAdapter()
        {
            if (IsServiceAlive(_characterFileSelectionAdapter) &&
                _characterFileSelectionAdapter.IsSupported)
            {
                return;
            }

            ICharacterFileSelectionAdapter fallback =
                null;
            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in
                     behaviours)
            {
                if (behaviour is not
                    ICharacterFileSelectionAdapter candidate)
                {
                    continue;
                }

                fallback ??=
                    candidate;

                if (candidate.IsSupported)
                {
                    _characterFileSelectionAdapter =
                        candidate;
                    return;
                }
            }

            _characterFileSelectionAdapter =
                fallback;
        }

        private void ResolveAppearanceRuntime()
        {
            if (IsServiceAlive(
                    _appearanceRuntime))
            {
                return;
            }

            _appearanceRuntime = null;

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IAppearanceRuntime runtime)
                {
                    _appearanceRuntime =
                        runtime;
                    return;
                }
            }
        }

        private void ResolveTrackingControls()
        {
            var rebuild =
                _trackingControls.Count == 0;

            if (!rebuild)
            {
                foreach (var control in
                         _trackingControls)
                {
                    if (control is not
                            MonoBehaviour behaviour ||
                        behaviour == null)
                    {
                        rebuild = true;
                        break;
                    }
                }
            }

            if (!rebuild)
            {
                return;
            }

            _trackingControls.Clear();

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    ITrackingRuntimeControl control)
                {
                    _trackingControls.Add(
                        control);
                }
            }

            _trackingControls.Sort(
                (left, right) =>
                    string.CompareOrdinal(
                        left.ControlId,
                        right.ControlId));

            _trackingControlIndex =
                Mathf.Clamp(
                    _trackingControlIndex,
                    0,
                    Mathf.Max(
                        0,
                        _trackingControls.Count - 1));
        }

        private ITrackingRuntimeControl
            GetSelectedTrackingControl()
        {
            if (_trackingControls.Count == 0)
            {
                return null;
            }

            _trackingControlIndex =
                Mathf.Clamp(
                    _trackingControlIndex,
                    0,
                    _trackingControls.Count - 1);

            return
                _trackingControls[
                    _trackingControlIndex];
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private static void SetSectionLabel(
            Text label,
            string title,
            bool available)
        {
            if (label == null)
            {
                return;
            }

            title ??=
                string.Empty;

            if (available)
            {
                if (!string.Equals(
                        label.text,
                        title,
                        StringComparison.Ordinal))
                {
                    label.text =
                        title;
                }

                return;
            }

            const string suffix =
                "  — unavailable";
            var current =
                label.text ??
                string.Empty;
            var expectedLength =
                title.Length +
                suffix.Length;

            if (current.Length ==
                    expectedLength &&
                current.StartsWith(
                    title,
                    StringComparison.Ordinal) &&
                current.EndsWith(
                    suffix,
                    StringComparison.Ordinal))
            {
                return;
            }

            label.text =
                title +
                suffix;
        }

        private static void SetInputTextIfChanged(
            InputField input,
            string value)
        {
            if (input == null ||
                string.Equals(
                    input.text,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            input.text =
                value;
        }

        private static void SetTextIfChanged(
            Text text,
            string value)
        {
            if (text == null ||
                string.Equals(
                    text.text,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            text.text =
                value;
        }

        private void SetButtonLabel(
            Button button,
            string label)
        {
            if (button == null)
            {
                return;
            }

            if (!_buttonLabels.TryGetValue(
                    button,
                    out var text) ||
                text == null)
            {
                text =
                    button.GetComponentInChildren<
                        Text>();

                if (text == null)
                {
                    return;
                }

                _buttonLabels[
                    button] =
                        text;
            }

            if (!string.Equals(
                    text.text,
                    label,
                    StringComparison.Ordinal))
            {
                text.text =
                    label;
            }
        }

        private InputField CreateInputField(
            string name,
            Transform parent,
            string placeholder)
        {
            var rect =
                CreateRect(
                    name,
                    parent);

            var image =
                rect.gameObject
                    .AddComponent<Image>();
            image.color =
                new Color(
                    0.10f,
                    0.11f,
                    0.13f,
                    1f);

            var input =
                rect.gameObject
                    .AddComponent<InputField>();
            input.targetGraphic = image;

            var text =
                CreateText(
                    "Text",
                    rect,
                    16,
                    TextAnchor.MiddleLeft);
            text.raycastTarget = true;

            Stretch(
                text.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 2f),
                new Vector2(-10f, -2f));

            var placeholderText =
                CreateText(
                    "Placeholder",
                    rect,
                    16,
                    TextAnchor.MiddleLeft);
            placeholderText.text =
                placeholder;
            placeholderText.color =
                new Color(
                    0.55f,
                    0.58f,
                    0.63f,
                    1f);

            Stretch(
                placeholderText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 2f),
                new Vector2(-10f, -2f));

            input.textComponent = text;
            input.placeholder =
                placeholderText;
            input.lineType =
                InputField.LineType.SingleLine;

            input.onValueChanged.AddListener(
                _ =>
                {
                    if (_model.SelectedSection ==
                            ApplicationUiSection.Character ||
                        _model.SelectedSection ==
                            ApplicationUiSection.MotionExpression ||
                        _model.SelectedSection ==
                            ApplicationUiSection.Environment ||
                        _model.SelectedSection ==
                            ApplicationUiSection.MaterialShader)
                    {
                        RefreshContextActions();
                    }
                });

            return input;
        }

        private Slider CreateSlider(
            string name,
            Transform parent,
            float minimum,
            float maximum,
            float initialValue,
            Action<float> onValueChanged)
        {
            var rect =
                CreateRect(
                    name,
                    parent);

            var background =
                rect.gameObject
                    .AddComponent<Image>();
            background.color =
                new Color(
                    0.10f,
                    0.11f,
                    0.13f,
                    1f);

            var fill =
                CreateRect(
                    "Fill",
                    rect);
            fill.anchorMin =
                new Vector2(
                    0f,
                    0.3f);
            fill.anchorMax =
                new Vector2(
                    1f,
                    0.7f);
            fill.offsetMin =
                new Vector2(
                    6f,
                    0f);
            fill.offsetMax =
                new Vector2(
                    -6f,
                    0f);

            var fillImage =
                fill.gameObject
                    .AddComponent<Image>();
            fillImage.color =
                new Color(
                    0.45f,
                    0.62f,
                    0.86f,
                    1f);

            var handle =
                CreateRect(
                    "Handle",
                    rect);
            handle.anchorMin =
                new Vector2(
                    0f,
                    0.5f);
            handle.anchorMax =
                new Vector2(
                    0f,
                    0.5f);
            handle.sizeDelta =
                new Vector2(
                    16f,
                    28f);

            var handleImage =
                handle.gameObject
                    .AddComponent<Image>();
            handleImage.color =
                new Color(
                    0.92f,
                    0.94f,
                    0.97f,
                    1f);

            var slider =
                rect.gameObject
                    .AddComponent<Slider>();
            slider.minValue =
                minimum;
            slider.maxValue =
                maximum;
            slider.wholeNumbers =
                false;
            slider.direction =
                Slider.Direction.LeftToRight;
            slider.fillRect =
                fill;
            slider.handleRect =
                handle;
            slider.targetGraphic =
                handleImage;
            slider.SetValueWithoutNotify(
                Mathf.Clamp(
                    initialValue,
                    minimum,
                    maximum));

            if (onValueChanged != null)
            {
                slider.onValueChanged
                    .AddListener(
                        value =>
                            onValueChanged(
                                value));
            }

            return slider;
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

        private static void SetActive(
            Component component,
            bool active)
        {
            if (component != null &&
                component.gameObject.activeSelf != active)
            {
                component.gameObject.SetActive(
                    active);
            }
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
