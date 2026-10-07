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
using VCR.Runtime.Materials;
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

        private IReadOnlyList<RuntimeMetric> _diagnosticsMetricSource;
        private IReadOnlyList<RuntimeMetric> _diagnosticsSortedMetrics =
            Array.Empty<RuntimeMetric>();

        private Canvas _canvas;
        private RectTransform _root;
        private Text _statusText;
        private Text _sectionTitle;
        private Text _contentText;
        private RectTransform _renderViewportFrame;
        private RectTransform _trackingCameraPreviewPanel;
        private RawImage _trackingCameraPreviewImage;
        private Text _trackingCameraPreviewPrivacyText;
        private Button _trackingCameraPreviewButton;
        private bool _trackingCameraPreviewRequested;
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
        private IEnvironmentRuntime _environmentRuntime;
        private SingleCharacterSceneRuntime _environmentRuntimeOwner;
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

        private const float MissingTrackingControlDiscoveryRetrySeconds = 5f;

        private float _nextRefreshTime;
        private float _nextDependencyResolveTime;
        private float _nextTrackingControlResolveTime;
        private string _lastActionMessage;
        private int _characterUiOperationGeneration;
        private int _trackingControlIndex;
        private int _appearanceTransitionIndex;
        private EnvironmentTransitionMode _environmentTransitionMode =
            EnvironmentTransitionMode.Cut;
        private int _materialSlotIndex;
        private int _eventRuleIndex;
        private int _settingsCapabilityIndex;
        private int _diagnosticsMetricPage;

        private long _diagnosticsSummarySequence = -1;
        private int _diagnosticsSummaryPage = -1;
        private float _diagnosticsSummaryReportInterval =
            float.NaN;
        private bool _diagnosticsSummaryConsoleLogging;
        private bool _diagnosticsSummaryCsvEvidence;
        private string _diagnosticsSummaryEvidenceDirectory;
        private string _diagnosticsSummaryCache;

        private bool _trackingSummaryStateValid;
        private SubjectPresenceState _trackingSummarySubjectState;
        private bool _trackingSummaryAnySourceAvailable;
        private bool _trackingSummaryFaceSourceAvailable;
        private bool _trackingSummaryBodyHandsSourceAvailable;
        private bool _trackingSummaryFullBodySourceAvailable;
        private TrackingPresenceEvents _trackingSummaryEvents;
        private int _trackingSummarySelectedControlIndex = -1;
        private TrackingSummaryControlState[]
            _trackingSummaryControlStates =
                Array.Empty<TrackingSummaryControlState>();
        private string _trackingSummaryCache;

        private bool _environmentSummaryStateValid;
        private string _environmentSummaryEnvironmentId;
        private string _environmentSummaryStateId;
        private EnvironmentSpaceMode _environmentSummarySpaceMode;
        private bool _environmentSummaryActive;
        private bool _environmentSummaryTransitionActive;
        private EnvironmentTransitionMode _environmentSummaryTransitionMode;
        private int _environmentSummaryTransitionPercent;
        private string _environmentSummaryPreviousStateId;
        private string _environmentSummaryTransitionStateId;
        private float _environmentSummaryTransitionDuration;
        private EnvironmentTransitionMode _environmentSummarySelectedMode;
        private string _environmentSummaryError;
        private string _environmentSummaryCache;

        private bool _settingsSummaryStateValid;
        private bool _settingsSummaryRuntimeStarted;
        private string _settingsSummaryConfigurationPath;
        private int _settingsSummaryRegisteredCapabilities;
        private int _settingsSummaryEnabledCapabilities;
        private bool _settingsSummaryHasSelectedCapability;
        private string _settingsSummaryCapabilityId;
        private CapabilityState _settingsSummaryCapabilityState;
        private string _settingsSummaryCapabilityError;
        private float _settingsSummaryRenderScale;
        private int _settingsSummaryTargetFrameRate;
        private bool _settingsSummaryUseVSync;
        private bool _settingsSummaryRunInBackground;
        private string _settingsSummaryCache;

        private bool _eventsSummaryStateValid;
        private int _eventsSummaryRuleCount;
        private string _eventsSummarySelectedRuleId;
        private bool _eventsSummaryHasSelectedRule;
        private bool _eventsSummarySelectedRuleEnabled;
        private bool _eventsSummaryTraceEnabled;
        private int _eventsSummaryMaxCommandsPerEvent;
        private string _eventsSummaryRuleStorePath;
        private long _eventsSummaryProcessedEvents;
        private long _eventsSummaryMatchedRules;
        private long _eventsSummaryExecutedActions;
        private long _eventsSummaryFailedActions;
        private long _eventsSummaryUnhandledActions;
        private long _eventsSummaryAmbiguousActions;
        private string _eventsSummaryCache;

        private bool _outputSummaryStateValid;
        private bool _outputSummaryHasAdapter;
        private OverlayOutputState _outputSummaryState;
        private string _outputSummaryError;
        private int _outputSummaryClientWidth;
        private int _outputSummaryClientHeight;
        private bool _outputSummaryTransparent;
        private bool _outputSummaryTopmost;
        private bool _outputSummaryClickThrough;
        private bool _outputSummaryRenderAvailable;
        private int _outputSummaryRenderWidth;
        private int _outputSummaryRenderHeight;
        private int _outputSummaryTargetFrameRate;
        private bool _outputSummaryRunInBackground;
        private string _outputSummaryCache;

        private bool _materialSummaryStateValid;
        private int _materialSummarySlotCount;
        private string _materialSummaryRequestedSlotId;
        private bool _materialSummaryHasStatus;
        private string _materialSummarySlotId;
        private MaterialOverrideHealth _materialSummaryHealth;
        private string _materialSummaryShaderId;
        private string _materialSummaryPresetId;
        private string _materialSummaryError;
        private bool _materialSummaryHasDescriptor;
        private string _materialSummarySourceMaterialName;
        private string _materialSummarySourceShaderName;
        private int _materialSummaryErrorCount;
        private string _materialSummaryCache;

        private const int CharacterSummaryPresetVisibleLimit =
            8;
        private bool _characterSummaryStateValid;
        private SceneRuntimeState _characterSummaryRuntimeState;
        private bool _characterSummaryHasCharacter;
        private string _characterSummaryModelPath;
        private string _characterSummaryRuntimeError;
        private bool _characterSummaryAppearanceAvailable;
        private AppearanceRuntimeState _characterSummaryAppearanceState;
        private string _characterSummaryPresetId;
        private string _characterSummaryOutfitId;
        private int _characterSummaryUserPresetCount;
        private readonly string[] _characterSummaryPresetIds =
            new string[CharacterSummaryPresetVisibleLimit];
        private string _characterSummaryTransitionId;
        private bool _characterSummaryAppearanceBusy;
        private int _characterSummaryTransitionPercent;
        private long _characterSummaryElapsedCentiseconds;
        private long _characterSummaryDurationCentiseconds;
        private bool _characterSummaryTransitionCommitted;
        private bool _characterSummaryCanCancelTransition;
        private string _characterSummaryAppearanceError;
        private string _characterSummaryCache;

        private bool _motionSummaryStateValid;
        private bool _motionSummaryPoseConfigured;
        private float _motionSummaryPoseWeight;
        private bool _motionSummaryManualAvailable;
        private long _motionSummaryManualSequence;
        private bool _motionSummaryManualConnected;
        private ExpressionBlendMode _motionSummaryBlendMode;
        private float _motionSummaryLayerWeight;
        private string _motionSummaryExpressionInput;
        private string _motionSummaryCache;
        private bool _motionPoseWeightLabelStateValid;
        private bool _motionPoseWeightLabelConfigured;
        private float _motionPoseWeightLabelValue;
        private string _motionPoseWeightLabelCache;

        private bool _trackingToggleLabelHasControl;
        private string _trackingToggleLabelDisplayName;
        private bool _trackingToggleLabelEnabled;
        private string _trackingToggleLabelCache;
        private bool _settingsCapabilityLabelHasSelection;
        private string _settingsCapabilityLabelId;
        private CapabilityState _settingsCapabilityLabelState;
        private string _settingsCapabilityLabelCache;
        private bool _appearanceTransitionLabelBusy;
        private string _appearanceTransitionLabelId;
        private int _appearanceTransitionLabelPercent = -1;
        private string _appearanceTransitionLabelCache;
        private int _diagnosticsNextLabelPage = -1;
        private int _diagnosticsNextLabelPageCount = -1;
        private string _diagnosticsNextLabelCache;

        private bool _statusBarStateValid;
        private bool _statusBarSceneAvailable;
        private SceneRuntimeState _statusBarSceneState;
        private bool _statusBarHasCharacter;
        private bool _statusBarOutputAvailable;
        private OverlayOutputState _statusBarOutputState;
        private string _statusBarActionMessage;
        private string _statusBarCache;

        private ApplicationUiSection _contextVisibilitySection =
            ApplicationUiSection.Count;
        private readonly byte[] _sectionAvailabilityCache =
            new byte[(int)ApplicationUiSection.Count];

        private bool _uiRefreshPassActive;
        private bool _refreshOverlayOutputSampled;
        private bool _refreshOverlayOutputPresent;
        private bool _refreshOverlayOutputReadable;
        private OverlayOutputStatus _refreshOverlayOutputStatus;
        private OverlayOutputSettings _refreshOverlayOutputSettings;
        private string _refreshOverlayOutputError;
        private bool _refreshRenderSettingsSampled;
        private bool _refreshRenderSettingsAvailable;
        private RenderRuntimeSettings _refreshRenderSettings;

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
                // A raw camera preview is privacy-sensitive. Navigating away
                // from Tracking always returns it to the hidden state.
                if (section != ApplicationUiSection.Tracking)
                {
                    _trackingCameraPreviewRequested = false;
                }

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
            _motionPoseWeightLabelStateValid =
                false;
            _motionPoseWeightLabelCache =
                null;
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
            _contextVisibilitySection =
                ApplicationUiSection.Count;
            Array.Clear(
                _sectionAvailabilityCache,
                0,
                _sectionAvailabilityCache.Length);

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

                MonoBehaviour[] activeDependencyBehaviours =
                    null;

                ResolveConcreteDependencies(
                    ref activeDependencyBehaviours);

                if (!ReferenceEquals(
                        _environmentRuntimeOwner,
                        sceneRuntime) ||
                    !IsServiceAlive(_environmentRuntime))
                {
                    _environmentRuntime =
                        sceneRuntime?.EnvironmentRuntime;
                    _environmentRuntimeOwner =
                        sceneRuntime;
                }

                ResolveTrackingControls(
                    force);
                ResolveCharacterFileSelectionAdapter(
                    ref activeDependencyBehaviours);
                ResolveAppearanceRuntime(
                    ref activeDependencyBehaviours);

                if (!IsServiceAlive(_trackingPresence))
                {
                    _trackingPresence = null;

                    var behaviours =
                        GetActiveDependencyBehaviours(
                            ref activeDependencyBehaviours);

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
                GetCachedEnvironmentRuntime() != null,
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
                InvalidateDiagnosticsSummaryCache();

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
            InvalidateDiagnosticsSummaryCache();
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
                    0.018f,
                    0.022f,
                    0.03f,
                    0.08f);
            background.raycastTarget = false;

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

            var applicationTitle =
                CreateText(
                    "Application Title",
                    top,
                    18,
                    TextAnchor.MiddleLeft);
            applicationTitle.text =
                "Virtual Character Renderer";
            applicationTitle.fontStyle =
                FontStyle.Bold;
            applicationTitle.raycastTarget =
                false;
            Stretch(
                applicationTitle.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(18f, 0f),
                new Vector2(-520f, 0f));

            _statusText =
                CreateText(
                    "Status",
                    top,
                    15,
                    TextAnchor.MiddleRight);
            _statusText.raycastTarget =
                false;

            Stretch(
                _statusText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(520f, 0f),
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
                new Vector2(8f, 276f);
            navigation.offsetMax =
                new Vector2(220f, -64f);

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
                    12,
                    12,
                    16,
                    16);
            navLayout.spacing = 8f;
            navLayout.childControlHeight = true;
            navLayout.childForceExpandHeight = false;
            navLayout.childAlignment =
                TextAnchor.UpperLeft;

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

                if (_sectionLabels[section] !=
                    null)
                {
                    _sectionLabels[section]
                        .alignment =
                            TextAnchor.MiddleLeft;
                }
            }

            _renderViewportFrame =
                CreateRect(
                    "Render Viewport",
                    _root);
            _renderViewportFrame.anchorMin =
                Vector2.zero;
            _renderViewportFrame.anchorMax =
                Vector2.one;
            _renderViewportFrame.offsetMin =
                new Vector2(228f, 276f);
            _renderViewportFrame.offsetMax =
                new Vector2(-442f, -64f);

            var viewportBackground =
                _renderViewportFrame.gameObject
                    .AddComponent<Image>();
            viewportBackground.color =
                new Color(
                    0.01f,
                    0.015f,
                    0.025f,
                    0.06f);
            viewportBackground.raycastTarget =
                false;

            var viewportLabel =
                CreateText(
                    "Render Viewport Label",
                    _renderViewportFrame,
                    13,
                    TextAnchor.UpperLeft);
            viewportLabel.text =
                "RENDER VIEW";
            viewportLabel.color =
                new Color(
                    0.60f,
                    0.66f,
                    0.76f,
                    0.82f);
            viewportLabel.raycastTarget =
                false;
            Stretch(
                viewportLabel.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12f, 8f),
                new Vector2(-12f, -8f));

            var content =
                CreateRect(
                    "Inspector",
                    _root);

            content.anchorMin =
                new Vector2(1f, 0f);
            content.anchorMax =
                new Vector2(1f, 1f);
            content.pivot =
                new Vector2(1f, 1f);
            content.offsetMin =
                new Vector2(
                    -434f,
                    276f);
            content.offsetMax =
                new Vector2(
                    -8f,
                    -64f);

            var contentImage =
                content.gameObject
                    .AddComponent<Image>();
            contentImage.color =
                new Color(
                    0.05f,
                    0.055f,
                    0.07f,
                    0.985f);

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
            _sectionTitle.fontStyle =
                FontStyle.Bold;

            _contentText =
                CreateText(
                    "Content Text",
                    content,
                    15,
                    TextAnchor.UpperLeft);

            _contentText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _contentText.verticalOverflow =
                VerticalWrapMode.Truncate;

            _contentText.rectTransform
                .anchorMin =
                    new Vector2(0f, 0f);
            _contentText.rectTransform
                .anchorMax =
                    new Vector2(1f, 1f);
            _contentText.rectTransform
                .offsetMin =
                    new Vector2(20f, 84f);
            _contentText.rectTransform
                .offsetMax =
                    new Vector2(-20f, -82f);

            _trackingCameraPreviewPanel =
                CreateRect(
                    "Tracking Camera Preview",
                    content);
            _trackingCameraPreviewPanel.anchorMin =
                new Vector2(0f, 0f);
            _trackingCameraPreviewPanel.anchorMax =
                new Vector2(1f, 0f);
            _trackingCameraPreviewPanel.pivot =
                new Vector2(0.5f, 0f);
            _trackingCameraPreviewPanel.offsetMin =
                new Vector2(16f, 84f);
            _trackingCameraPreviewPanel.offsetMax =
                new Vector2(-16f, 260f);

            var trackingPreviewBackground =
                _trackingCameraPreviewPanel.gameObject
                    .AddComponent<Image>();
            trackingPreviewBackground.color =
                new Color(
                    0.025f,
                    0.03f,
                    0.04f,
                    1f);
            trackingPreviewBackground.raycastTarget =
                false;

            _trackingCameraPreviewImage =
                _trackingCameraPreviewPanel.gameObject
                    .AddComponent<RawImage>();
            _trackingCameraPreviewImage.color =
                Color.white;
            _trackingCameraPreviewImage.raycastTarget =
                false;

            _trackingCameraPreviewPrivacyText =
                CreateText(
                    "Camera Preview Privacy",
                    _trackingCameraPreviewPanel,
                    15,
                    TextAnchor.MiddleCenter);
            _trackingCameraPreviewPrivacyText.text =
                "Camera preview is hidden by default.\nUse Show Camera Preview to reveal it.";
            _trackingCameraPreviewPrivacyText.raycastTarget =
                false;
            Stretch(
                _trackingCameraPreviewPrivacyText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(18f, 18f),
                new Vector2(-18f, -18f));
            _trackingCameraPreviewPanel.gameObject.SetActive(
                false);

            _contextActions =
                CreateRect(
                    "Section Actions",
                    _root);

            _contextActions.anchorMin =
                new Vector2(0f, 0f);
            _contextActions.anchorMax =
                new Vector2(1f, 0f);
            _contextActions.pivot =
                new Vector2(0.5f, 0f);
            _contextActions.offsetMin =
                new Vector2(228f, 196f);
            _contextActions.offsetMax =
                new Vector2(-8f, 268f);

            AddActionPanelBackground(
                _contextActions);

            var contextLayout =
                _contextActions.gameObject
                    .AddComponent<
                        GridLayoutGroup>();
            contextLayout.padding =
                new RectOffset(
                    8,
                    8,
                    8,
                    8);
            contextLayout.spacing =
                new Vector2(
                    8f,
                    8f);
            contextLayout.cellSize =
                new Vector2(
                    220f,
                    41f);
            contextLayout.constraint =
                GridLayoutGroup.Constraint
                    .FixedColumnCount;
            contextLayout.constraintCount = 5;
            contextLayout.childAlignment =
                TextAnchor.UpperLeft;

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
                    "Browse VRM…",
                    _contextActions,
                    BrowseCharacterFile);
            _characterBrowseButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 100f;

            _loadCharacterButton =
                CreateButton(
                    "Load VRM",
                    _contextActions,
                    LoadCharacterFromPath);
            _loadCharacterButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 150f;

            _reloadCharacterButton =
                CreateButton(
                    "Reload",
                    _contextActions,
                    ReloadCharacter);
            _reloadCharacterButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 160f;

            _unloadCharacterButton =
                CreateButton(
                    "Unload",
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

            _trackingCameraPreviewButton =
                CreateButton(
                    "Show Camera Preview",
                    _contextActions,
                    ToggleTrackingCameraPreview);
            _trackingCameraPreviewButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 190f;

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
                    _root);

            _appearanceActions.anchorMin =
                new Vector2(0f, 0f);
            _appearanceActions.anchorMax =
                new Vector2(1f, 0f);
            _appearanceActions.pivot =
                new Vector2(0.5f, 0f);
            _appearanceActions.offsetMin =
                new Vector2(228f, 148f);
            _appearanceActions.offsetMax =
                new Vector2(-8f, 192f);

            AddActionPanelBackground(
                _appearanceActions);

            var appearanceLayout =
                _appearanceActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearanceLayout.padding =
                new RectOffset(8, 8, 6, 6);
            appearanceLayout.spacing = 8f;
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
                    _root);

            _appearanceDirectActions.anchorMin =
                new Vector2(0f, 0f);
            _appearanceDirectActions.anchorMax =
                new Vector2(1f, 0f);
            _appearanceDirectActions.pivot =
                new Vector2(0.5f, 0f);
            _appearanceDirectActions.offsetMin =
                new Vector2(228f, 100f);
            _appearanceDirectActions.offsetMax =
                new Vector2(-8f, 144f);

            AddActionPanelBackground(
                _appearanceDirectActions);

            var appearanceDirectLayout =
                _appearanceDirectActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearanceDirectLayout.padding =
                new RectOffset(8, 8, 6, 6);
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
                    _root);

            _appearancePersistenceActions.anchorMin =
                new Vector2(0f, 0f);
            _appearancePersistenceActions.anchorMax =
                new Vector2(1f, 0f);
            _appearancePersistenceActions.pivot =
                new Vector2(0.5f, 0f);
            _appearancePersistenceActions.offsetMin =
                new Vector2(228f, 52f);
            _appearancePersistenceActions.offsetMax =
                new Vector2(-8f, 96f);

            AddActionPanelBackground(
                _appearancePersistenceActions);

            var appearancePersistenceLayout =
                _appearancePersistenceActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearancePersistenceLayout.padding =
                new RectOffset(8, 8, 6, 6);
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
                    _root);

            _appearancePresetManagementActions.anchorMin =
                new Vector2(0f, 0f);
            _appearancePresetManagementActions.anchorMax =
                new Vector2(1f, 0f);
            _appearancePresetManagementActions.pivot =
                new Vector2(0.5f, 0f);
            _appearancePresetManagementActions.offsetMin =
                new Vector2(228f, 8f);
            _appearancePresetManagementActions.offsetMax =
                new Vector2(-8f, 48f);

            AddActionPanelBackground(
                _appearancePresetManagementActions);

            var appearancePresetManagementLayout =
                _appearancePresetManagementActions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            appearancePresetManagementLayout.padding =
                new RectOffset(8, 8, 6, 6);
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

            AddActionPanelBackground(
                actions);

            var actionLayout =
                actions.gameObject
                    .AddComponent<
                        HorizontalLayoutGroup>();
            actionLayout.padding =
                new RectOffset(8, 8, 5, 5);
            actionLayout.spacing = 8f;
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

            var runtime =
                sceneRuntime;
            var path =
                _characterPathInput.text?.Trim();

            if (!ApplicationUiActionPolicy
                    .CanLoadCharacter(
                        true,
                        runtime.State,
                        path))
            {
                _lastActionMessage =
                    string.IsNullOrWhiteSpace(path)
                        ? "Enter a VRM path first."
                        : "Character load is unavailable in the current runtime state.";
                RefreshAll();
                return;
            }

            var operationGeneration =
                ++_characterUiOperationGeneration;

            _lastActionMessage =
                "Loading character...";
            RefreshAll();

            try
            {
                var loaded =
                    await runtime
                        .LoadCharacterAsync(path);

                if (!IsCurrentCharacterUiOperation(
                        operationGeneration,
                        runtime))
                {
                    return;
                }

                _lastActionMessage =
                    loaded != null
                        ? "Character loaded."
                        : "Character load completed without a model.";
            }
            catch (Exception exception)
            {
                if (!IsCurrentCharacterUiOperation(
                        operationGeneration,
                        runtime))
                {
                    return;
                }

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

            var runtime =
                sceneRuntime;
            var status =
                runtime.Status;

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

            var operationGeneration =
                ++_characterUiOperationGeneration;

            _lastActionMessage =
                "Reloading character...";
            RefreshAll();

            try
            {
                var loaded =
                    await runtime
                        .ReloadCharacterAsync();

                if (!IsCurrentCharacterUiOperation(
                        operationGeneration,
                        runtime))
                {
                    return;
                }

                _lastActionMessage =
                    loaded != null
                        ? "Character reloaded."
                        : "Character reload completed without a model.";
            }
            catch (Exception exception)
            {
                if (!IsCurrentCharacterUiOperation(
                        operationGeneration,
                        runtime))
                {
                    return;
                }

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

            _characterUiOperationGeneration++;

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

        private bool IsCurrentCharacterUiOperation(
            int generation,
            SingleCharacterSceneRuntime runtime)
        {
            return
                this != null &&
                generation ==
                    _characterUiOperationGeneration &&
                ReferenceEquals(
                    sceneRuntime,
                    runtime);
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
            var current =
                GetCurrentAppearanceSnapshot();
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

            RefreshMotionPoseWeightLabel(
                configured: true,
                _mixer.PrimaryPoseLayerWeight);
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
                    UnityEngine.Application.persistentDataPath,
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
                    eventRuntime.TryGetRuleSummaryAt(
                        _eventRuleIndex,
                        out var selectedRule)
                        ? selectedRule.Id ??
                          string.Empty
                        : string.Empty;
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

            var hasSelected =
                eventRuntime.TryGetRuleSummary(
                    ruleId,
                    out var selected);

            if (!hasSelected)
            {
                _eventRuleIndex =
                    Mathf.Clamp(
                        _eventRuleIndex,
                        0,
                        ruleCount - 1);
                hasSelected =
                    eventRuntime.TryGetRuleSummaryAt(
                        _eventRuleIndex,
                        out selected);
                ruleId =
                    hasSelected
                        ? selected.Id
                        : null;
            }

            if (!hasSelected ||
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
                !eventRuntime.RuleTracingEnabled;
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

            EventRuntimeRuleSummary selected =
                default;
            var hasSelected =
                hasRules &&
                eventRuntime.TryGetRuleSummaryAt(
                    _eventRuleIndex,
                    out selected);

            if (_eventRuleInput != null &&
                !_eventRuleInput.isFocused)
            {
                SetInputTextIfChanged(
                    _eventRuleInput,
                    hasSelected
                        ? selected.Id ??
                          string.Empty
                        : string.Empty);
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
                    hasSelected;
                SetButtonLabel(
                    _eventToggleRuleButton,
                    !hasSelected
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
                    eventRuntime?.RuleTracingEnabled ==
                        true
                        ? "Trace: On"
                        : "Trace: Off");
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
                ResolveTrackingControls(
                    force: true);
                RefreshAvailability();
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
            ResolveTrackingControls(
                force: true);
            RefreshAvailability();
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
                    GetSettingsCapabilityLabel(
                        hasSelected,
                        selected));
            }

            TryGetRenderSettingsForUiRefresh(
                out var renderSettings);

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
                    sceneRuntime == null
                        ? "VSync: n/a"
                        : renderSettings.UseVSync
                            ? "VSync: On"
                            : "VSync: Off");
            }

            if (_settingsRunInBackgroundButton != null)
            {
                _settingsRunInBackgroundButton.interactable =
                    canMutate;
                SetButtonLabel(
                    _settingsRunInBackgroundButton,
                    sceneRuntime == null
                        ? "Background: n/a"
                        : renderSettings.RunInBackground
                            ? "Background: On"
                            : "Background: Off");
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

            var outputReadable =
                TryGetOverlayOutputForUiRefresh(
                    out var outputPresent,
                    out _,
                    out var current,
                    out var outputError);

            if (!outputPresent ||
                !outputReadable ||
                !ApplicationUiActionPolicy
                    .CanApplyOverlaySetting(
                        true,
                        sceneRuntime.State,
                        true))
            {
                _lastActionMessage =
                    !outputPresent
                        ? "No overlay output adapter is configured."
                        : !outputReadable
                            ? "Overlay output state is unavailable: " +
                              (outputError ?? "unknown error")
                            : "Overlay output settings cannot be changed while the scene runtime is busy.";
                RefreshAll();
                return;
            }
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

            _uiRefreshPassActive =
                true;
            _refreshOverlayOutputSampled =
                false;
            _refreshRenderSettingsSampled =
                false;

            try
            {
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
                var selectedSection =
                    section ==
                    _model.SelectedSection;
                var availabilityState =
                    !available
                        ? (byte)1
                        : selectedSection
                            ? (byte)3
                            : (byte)2;

                if (_sectionAvailabilityCache[i] ==
                    availabilityState)
                {
                    continue;
                }

                _sectionAvailabilityCache[i] =
                    availabilityState;
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
                        available,
                        selectedSection);
                }
            }

            if (_saveButton != null)
            {
                _saveButton.interactable =
                    applicationBootstrap != null;
            }

            if (_recoverOutputButton != null)
            {
                TryGetOverlayOutputForUiRefresh(
                    out var recoverOutputPresent,
                    out _,
                    out _,
                    out _);
                _recoverOutputButton.interactable =
                    recoverOutputPresent;
            }

                RefreshContextActions();

                RefreshStatus();
                RefreshContent();
            }
            finally
            {
                _uiRefreshPassActive =
                    false;
                _refreshOverlayOutputSampled =
                    false;
                _refreshOverlayOutputPresent =
                    false;
                _refreshOverlayOutputReadable =
                    false;
                _refreshOverlayOutputStatus =
                    default;
                _refreshOverlayOutputSettings =
                    default;
                _refreshOverlayOutputError =
                    null;
                _refreshRenderSettingsSampled =
                    false;
            }
        }

        private bool TryGetOverlayOutputForUiRefresh(
            out bool present,
            out OverlayOutputStatus status,
            out OverlayOutputSettings settings,
            out string error)
        {
            if (!_uiRefreshPassActive)
            {
                present =
                    sceneRuntime?.HasOverlayOutput ==
                    true;

                if (!present)
                {
                    status = default;
                    settings = default;
                    error =
                        "No overlay output adapter is configured.";
                    return false;
                }

                return sceneRuntime.TryCaptureOverlayOutput(
                    out status,
                    out settings,
                    out error);
            }

            if (!_refreshOverlayOutputSampled)
            {
                _refreshOverlayOutputPresent =
                    sceneRuntime?.HasOverlayOutput ==
                    true;

                if (_refreshOverlayOutputPresent)
                {
                    _refreshOverlayOutputReadable =
                        sceneRuntime.TryCaptureOverlayOutput(
                            out _refreshOverlayOutputStatus,
                            out _refreshOverlayOutputSettings,
                            out _refreshOverlayOutputError);
                }
                else
                {
                    _refreshOverlayOutputReadable =
                        false;
                    _refreshOverlayOutputStatus =
                        default;
                    _refreshOverlayOutputSettings =
                        default;
                    _refreshOverlayOutputError =
                        "No overlay output adapter is configured.";
                }

                _refreshOverlayOutputSampled =
                    true;
            }

            present =
                _refreshOverlayOutputPresent;
            status =
                _refreshOverlayOutputStatus;
            settings =
                _refreshOverlayOutputSettings;
            error =
                _refreshOverlayOutputError;
            return _refreshOverlayOutputReadable;
        }

        private bool TryGetRenderSettingsForUiRefresh(
            out RenderRuntimeSettings settings)
        {
            if (!_uiRefreshPassActive)
            {
                if (sceneRuntime == null)
                {
                    settings =
                        RenderRuntimeSettings
                            .Default1080p;
                    return false;
                }

                return
                    sceneRuntime.TryCaptureRenderSettings(
                        out settings);
            }

            if (!_refreshRenderSettingsSampled)
            {
                _refreshRenderSettingsAvailable =
                    sceneRuntime != null &&
                    sceneRuntime.TryCaptureRenderSettings(
                        out _refreshRenderSettings);

                if (sceneRuntime == null)
                {
                    _refreshRenderSettings =
                        RenderRuntimeSettings
                            .Default1080p;
                }

                _refreshRenderSettingsSampled =
                    true;
            }

            settings =
                _refreshRenderSettings;
            return
                _refreshRenderSettingsAvailable;
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

            if (_contextVisibilitySection !=
                selected)
            {
                _contextVisibilitySection =
                    selected;
                RefreshContextActionVisibility(
                    selected);
            }

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
                var outputReadable =
                    TryGetOverlayOutputForUiRefresh(
                        out var outputPresent,
                        out _,
                        out var outputSettings,
                        out _);
                var canApplyOverlay =
                    ApplicationUiActionPolicy
                        .CanApplyOverlaySetting(
                            true,
                            status.State,
                            outputPresent &&
                            outputReadable);

                if (_outputTransparentButton != null)
                {
                    _outputTransparentButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputTransparentButton,
                        !outputReadable
                            ? "Transparent: n/a"
                            : outputSettings.Transparent
                                ? "Transparent: On"
                                : "Transparent: Off");
                }

                if (_outputTopmostButton != null)
                {
                    _outputTopmostButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputTopmostButton,
                        !outputReadable
                            ? "Topmost: n/a"
                            : outputSettings.Topmost
                                ? "Topmost: On"
                                : "Topmost: Off");
                }

                if (_outputClickThroughButton != null)
                {
                    _outputClickThroughButton.interactable =
                        canApplyOverlay;
                    SetButtonLabel(
                        _outputClickThroughButton,
                        !outputReadable
                            ? "Click-through: n/a"
                            : outputSettings.ClickThrough
                                ? "Click-through: On"
                                : "Click-through: Off");
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
                        GetTrackingToggleLabel(
                            control));
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

                    SetButtonLabel(
                        _appearanceTransitionButton,
                        GetAppearanceTransitionButtonLabel(
                            appearanceStatus));
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

        private void RefreshContextActionVisibility(
            ApplicationUiSection selected)
        {
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
                _trackingCameraPreviewButton,
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

            RefreshTrackingCameraPreviewPrivacy(
                trackingSelected);
        }

        private void ToggleTrackingCameraPreview()
        {
            _trackingCameraPreviewRequested =
                !_trackingCameraPreviewRequested;

            RefreshTrackingCameraPreviewPrivacy(
                _model.SelectedSection ==
                ApplicationUiSection.Tracking);
        }

        public void BindTrackingCameraPreviewTexture(
            Texture texture)
        {
            if (_trackingCameraPreviewImage != null)
            {
                _trackingCameraPreviewImage.texture =
                    texture;
            }

            RefreshTrackingCameraPreviewPrivacy(
                _model.SelectedSection ==
                ApplicationUiSection.Tracking);
        }

        private void RefreshTrackingCameraPreviewPrivacy(
            bool trackingSelected)
        {
            var shouldReveal =
                trackingSelected &&
                _trackingCameraPreviewRequested;

            if (_trackingCameraPreviewPanel != null &&
                _trackingCameraPreviewPanel.gameObject.activeSelf !=
                    shouldReveal)
            {
                _trackingCameraPreviewPanel.gameObject.SetActive(
                    shouldReveal);
            }

            var hasTexture =
                _trackingCameraPreviewImage != null &&
                _trackingCameraPreviewImage.texture != null;

            if (_trackingCameraPreviewImage != null)
            {
                _trackingCameraPreviewImage.enabled =
                    shouldReveal &&
                    hasTexture;
            }

            if (_trackingCameraPreviewPrivacyText != null)
            {
                _trackingCameraPreviewPrivacyText.gameObject.SetActive(
                    shouldReveal &&
                    !hasTexture);

                if (shouldReveal &&
                    !hasTexture)
                {
                    SetTextIfChanged(
                        _trackingCameraPreviewPrivacyText,
                        "Camera preview was explicitly requested, but no video surface is connected in this alpha.");
                }
            }

            SetButtonLabel(
                _trackingCameraPreviewButton,
                _trackingCameraPreviewRequested
                    ? "Hide Camera Preview"
                    : "Show Camera Preview");

            if (_contentText != null)
            {
                var offset =
                    _contentText.rectTransform.offsetMin;
                offset.y =
                    shouldReveal
                        ? 276f
                        : 84f;
                _contentText.rectTransform.offsetMin =
                    offset;
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

        private void RefreshMotionPoseWeightLabel(
            bool configured,
            float value)
        {
            if (_motionPoseWeightLabel == null)
            {
                return;
            }

            if (_motionPoseWeightLabelStateValid &&
                _motionPoseWeightLabelConfigured ==
                    configured &&
                (!configured ||
                 _motionPoseWeightLabelValue ==
                    value))
            {
                return;
            }

            _motionPoseWeightLabelConfigured =
                configured;
            _motionPoseWeightLabelValue =
                value;
            _motionPoseWeightLabelStateValid =
                true;
            _motionPoseWeightLabelCache =
                configured
                    ? "Pose Weight " +
                      value.ToString(
                          "0.00",
                          CultureInfo.InvariantCulture)
                    : "Pose Weight n/a";

            SetTextIfChanged(
                _motionPoseWeightLabel,
                _motionPoseWeightLabelCache);
        }

        private void RefreshEnvironmentControlState()
        {
            var runtime =
                GetCachedEnvironmentRuntime();
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
                    GetEnvironmentTransitionModeLabel(
                        _environmentTransitionMode));
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

            RefreshMotionPoseWeightLabel(
                poseConfigured,
                poseWeight);

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

            var sceneAvailable =
                sceneRuntime != null;
            var sceneStatus =
                sceneAvailable
                    ? sceneRuntime.Status
                    : default;
            OverlayOutputStatus outputStatus =
                default;
            var outputAvailable =
                sceneAvailable &&
                TryGetOverlayOutputForUiRefresh(
                    out var outputPresent,
                    out outputStatus,
                    out _,
                    out _) &&
                outputPresent;
            var outputState =
                outputAvailable
                    ? outputStatus.State
                    : default;

            if (_statusBarStateValid &&
                _statusBarCache != null &&
                _statusBarSceneAvailable ==
                    sceneAvailable &&
                (!sceneAvailable ||
                 (_statusBarSceneState ==
                      sceneStatus.State &&
                  _statusBarHasCharacter ==
                      sceneStatus.HasCharacter)) &&
                _statusBarOutputAvailable ==
                    outputAvailable &&
                (!outputAvailable ||
                 _statusBarOutputState ==
                    outputState) &&
                string.Equals(
                    _statusBarActionMessage,
                    _lastActionMessage,
                    StringComparison.Ordinal))
            {
                SetTextIfChanged(
                    _statusText,
                    _statusBarCache);
                return;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("VCR  |  ");

            if (sceneAvailable)
            {
                builder.Append(
                    sceneStatus.State);
            }
            else
            {
                builder.Append(
                    "No Scene Runtime");
            }

            builder.Append("  |  ");
            builder.Append(
                sceneAvailable &&
                sceneStatus.HasCharacter
                    ? "Character: loaded"
                    : "Character: none");
            builder.Append("  |  ");

            if (outputAvailable)
            {
                builder.Append("Output: ");
                builder.Append(
                    outputState);
            }
            else
            {
                builder.Append(
                    "Output: none");
            }

            if (!string.IsNullOrWhiteSpace(
                    _lastActionMessage))
            {
                builder.Append("  |  ");
                builder.Append(
                    _lastActionMessage);
            }

            _statusBarSceneAvailable =
                sceneAvailable;
            _statusBarSceneState =
                sceneStatus.State;
            _statusBarHasCharacter =
                sceneStatus.HasCharacter;
            _statusBarOutputAvailable =
                outputAvailable;
            _statusBarOutputState =
                outputState;
            _statusBarActionMessage =
                _lastActionMessage;
            _statusBarStateValid =
                true;
            _statusBarCache =
                builder.ToString();

            SetTextIfChanged(
                _statusText,
                _statusBarCache);
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
            var appearanceAvailable =
                IsServiceAlive(
                    _appearanceRuntime);
            var appearance =
                appearanceAvailable
                    ? _appearanceRuntime.Status
                    : default;
            var userPresetRegistry =
                appearanceAvailable
                    ? _appearanceRuntime as
                        IAppearanceUserPresetRegistry
                    : null;
            var userPresetCount =
                userPresetRegistry?.UserPresetIds.Count ??
                0;
            var transitionPercent =
                appearanceAvailable &&
                appearance.Busy
                    ? (int)Math.Round(
                        appearance.TransitionProgress01 *
                        100.0)
                    : 0;
            var elapsedCentiseconds =
                appearanceAvailable &&
                appearance.Busy
                    ? (long)Math.Round(
                        appearance.TransitionElapsedSeconds *
                        100.0)
                    : 0L;
            var durationCentiseconds =
                appearanceAvailable &&
                appearance.Busy
                    ? (long)Math.Round(
                        appearance.TransitionDurationSeconds *
                        100.0)
                    : 0L;

            if (CharacterSummaryCacheMatches(
                    status,
                    appearanceAvailable,
                    appearance,
                    userPresetRegistry,
                    userPresetCount,
                    transitionPercent,
                    elapsedCentiseconds,
                    durationCentiseconds))
            {
                return _characterSummaryCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Runtime state: ");
            builder.Append(status.State);
            builder.Append(
                "\nCharacter loaded: ");
            builder.Append(
                status.HasCharacter);
            builder.Append("\nModel path: ");
            builder.Append(
                status.CurrentCharacterPath ??
                "<none>");
            builder.Append(
                "\nLast runtime error: ");
            builder.Append(
                status.LastError ??
                "<none>");

            if (!appearanceAvailable)
            {
                builder.Append(
                    "\nAppearance: runtime unavailable");
            }
            else
            {
                builder.Append(
                    "\nAppearance state: ");
                builder.Append(
                    appearance.State);
                builder.Append(
                    "\nAppearance preset: ");
                builder.Append(
                    appearance.CurrentPresetId ??
                    "<none>");
                builder.Append("\nOutfit: ");
                builder.Append(
                    appearance.CurrentOutfitId ??
                    "<none>");
                builder.Append(
                    "\nUser presets: ");
                builder.Append(
                    userPresetCount);
                builder.Append(
                    "\nUser preset order: ");
                AppendUserPresetOrder(
                    builder,
                    userPresetRegistry);
                builder.Append(
                    "\nTransition: ");
                builder.Append(
                    appearance.ActiveTransitionId ??
                    "<none>");
                builder.Append(
                    "\nTransition progress: ");

                if (appearance.Busy)
                {
                    builder.Append(
                        transitionPercent);
                    builder.Append("% (");
                    builder.Append(
                        appearance
                            .TransitionElapsedSeconds
                            .ToString(
                                "0.00",
                                CultureInfo.InvariantCulture));
                    builder.Append("s / ");
                    builder.Append(
                        appearance
                            .TransitionDurationSeconds
                            .ToString(
                                "0.00",
                                CultureInfo.InvariantCulture));
                    builder.Append("s)");
                }
                else
                {
                    builder.Append(
                        "<idle>");
                }

                builder.Append(
                    "\nTransition committed: ");
                builder.Append(
                    appearance
                        .TransitionCommitted);
                builder.Append(
                    "\nTransition cancelable: ");
                builder.Append(
                    appearance
                        .CanCancelTransition);
                builder.Append(
                    "\nAppearance error: ");
                builder.Append(
                    appearance.LastError ??
                    "<none>");
            }

            CaptureCharacterSummaryState(
                status,
                appearanceAvailable,
                appearance,
                userPresetRegistry,
                userPresetCount,
                transitionPercent,
                elapsedCentiseconds,
                durationCentiseconds);
            _characterSummaryCache =
                builder.ToString();
            return _characterSummaryCache;
        }

        private bool CharacterSummaryCacheMatches(
            SceneRuntimeStatus status,
            bool appearanceAvailable,
            AppearanceRuntimeStatus appearance,
            IAppearanceUserPresetRegistry registry,
            int userPresetCount,
            int transitionPercent,
            long elapsedCentiseconds,
            long durationCentiseconds)
        {
            if (!_characterSummaryStateValid ||
                _characterSummaryCache == null ||
                _characterSummaryRuntimeState !=
                    status.State ||
                _characterSummaryHasCharacter !=
                    status.HasCharacter ||
                !string.Equals(
                    _characterSummaryModelPath,
                    status.CurrentCharacterPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _characterSummaryRuntimeError,
                    status.LastError,
                    StringComparison.Ordinal) ||
                _characterSummaryAppearanceAvailable !=
                    appearanceAvailable)
            {
                return false;
            }

            if (!appearanceAvailable)
            {
                return true;
            }

            if (_characterSummaryAppearanceState !=
                    appearance.State ||
                !string.Equals(
                    _characterSummaryPresetId,
                    appearance.CurrentPresetId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _characterSummaryOutfitId,
                    appearance.CurrentOutfitId,
                    StringComparison.Ordinal) ||
                _characterSummaryUserPresetCount !=
                    userPresetCount ||
                !string.Equals(
                    _characterSummaryTransitionId,
                    appearance.ActiveTransitionId,
                    StringComparison.Ordinal) ||
                _characterSummaryAppearanceBusy !=
                    appearance.Busy ||
                _characterSummaryTransitionPercent !=
                    transitionPercent ||
                _characterSummaryElapsedCentiseconds !=
                    elapsedCentiseconds ||
                _characterSummaryDurationCentiseconds !=
                    durationCentiseconds ||
                _characterSummaryTransitionCommitted !=
                    appearance.TransitionCommitted ||
                _characterSummaryCanCancelTransition !=
                    appearance.CanCancelTransition ||
                !string.Equals(
                    _characterSummaryAppearanceError,
                    appearance.LastError,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var visibleCount =
                Math.Min(
                    CharacterSummaryPresetVisibleLimit,
                    userPresetCount);

            for (var i = 0;
                 i < visibleCount;
                 i++)
            {
                if (!string.Equals(
                        _characterSummaryPresetIds[i],
                        registry.UserPresetIds[i],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void CaptureCharacterSummaryState(
            SceneRuntimeStatus status,
            bool appearanceAvailable,
            AppearanceRuntimeStatus appearance,
            IAppearanceUserPresetRegistry registry,
            int userPresetCount,
            int transitionPercent,
            long elapsedCentiseconds,
            long durationCentiseconds)
        {
            _characterSummaryRuntimeState =
                status.State;
            _characterSummaryHasCharacter =
                status.HasCharacter;
            _characterSummaryModelPath =
                status.CurrentCharacterPath;
            _characterSummaryRuntimeError =
                status.LastError;
            _characterSummaryAppearanceAvailable =
                appearanceAvailable;

            if (appearanceAvailable)
            {
                _characterSummaryAppearanceState =
                    appearance.State;
                _characterSummaryPresetId =
                    appearance.CurrentPresetId;
                _characterSummaryOutfitId =
                    appearance.CurrentOutfitId;
                _characterSummaryUserPresetCount =
                    userPresetCount;
                _characterSummaryTransitionId =
                    appearance.ActiveTransitionId;
                _characterSummaryAppearanceBusy =
                    appearance.Busy;
                _characterSummaryTransitionPercent =
                    transitionPercent;
                _characterSummaryElapsedCentiseconds =
                    elapsedCentiseconds;
                _characterSummaryDurationCentiseconds =
                    durationCentiseconds;
                _characterSummaryTransitionCommitted =
                    appearance.TransitionCommitted;
                _characterSummaryCanCancelTransition =
                    appearance.CanCancelTransition;
                _characterSummaryAppearanceError =
                    appearance.LastError;

                var visibleCount =
                    Math.Min(
                        CharacterSummaryPresetVisibleLimit,
                        userPresetCount);

                for (var i = 0;
                     i < visibleCount;
                     i++)
                {
                    _characterSummaryPresetIds[i] =
                        registry.UserPresetIds[i];
                }

                for (var i = visibleCount;
                     i <
                     CharacterSummaryPresetVisibleLimit;
                     i++)
                {
                    _characterSummaryPresetIds[i] =
                        null;
                }
            }
            else
            {
                _characterSummaryUserPresetCount =
                    0;
                Array.Clear(
                    _characterSummaryPresetIds,
                    0,
                    _characterSummaryPresetIds.Length);
            }

            _characterSummaryStateValid =
                true;
        }

        private static void AppendUserPresetOrder(
            StringBuilder builder,
            IAppearanceUserPresetRegistry registry)
        {
            if (registry == null ||
                registry.UserPresetIds.Count == 0)
            {
                builder.Append(
                    "<none>");
                return;
            }

            var visibleCount =
                Math.Min(
                    CharacterSummaryPresetVisibleLimit,
                    registry.UserPresetIds.Count);

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
                CharacterSummaryPresetVisibleLimit)
            {
                builder.Append(
                    " > +");
                builder.Append(
                    registry.UserPresetIds.Count -
                    CharacterSummaryPresetVisibleLimit);
            }
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

        private string TrackingSummary()
        {
            if (!IsServiceAlive(_trackingPresence))
            {
                return "Tracking provider unavailable.";
            }

            var presence =
                _trackingPresence.Presence;

            if (TrackingSummaryCacheMatches(
                    presence))
            {
                return _trackingSummaryCache;
            }

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
            }
            else
            {
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
            }

            CaptureTrackingSummaryState(
                presence);
            _trackingSummaryCache =
                builder.ToString();
            return _trackingSummaryCache;
        }

        private bool TrackingSummaryCacheMatches(
            TrackingPresenceSnapshot presence)
        {
            if (!_trackingSummaryStateValid ||
                _trackingSummaryCache == null ||
                _trackingSummarySubjectState !=
                    presence.SubjectState ||
                _trackingSummaryAnySourceAvailable !=
                    presence.AnySourceAvailable ||
                _trackingSummaryFaceSourceAvailable !=
                    presence.FaceSourceAvailable ||
                _trackingSummaryBodyHandsSourceAvailable !=
                    presence.BodyHandsSourceAvailable ||
                _trackingSummaryFullBodySourceAvailable !=
                    presence.FullBodySourceAvailable ||
                _trackingSummaryEvents !=
                    presence.Events ||
                _trackingSummarySelectedControlIndex !=
                    _trackingControlIndex ||
                _trackingSummaryControlStates.Length !=
                    _trackingControls.Count)
            {
                return false;
            }

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];
                var cached =
                    _trackingSummaryControlStates[i];

                if (!string.Equals(
                        cached.DisplayName,
                        control.DisplayName,
                        StringComparison.Ordinal) ||
                    cached.Enabled !=
                        control.ControlEnabled ||
                    cached.Health !=
                        control.ControlHealthState ||
                    !string.Equals(
                        cached.Error,
                        control.ControlError,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void CaptureTrackingSummaryState(
            TrackingPresenceSnapshot presence)
        {
            _trackingSummarySubjectState =
                presence.SubjectState;
            _trackingSummaryAnySourceAvailable =
                presence.AnySourceAvailable;
            _trackingSummaryFaceSourceAvailable =
                presence.FaceSourceAvailable;
            _trackingSummaryBodyHandsSourceAvailable =
                presence.BodyHandsSourceAvailable;
            _trackingSummaryFullBodySourceAvailable =
                presence.FullBodySourceAvailable;
            _trackingSummaryEvents =
                presence.Events;
            _trackingSummarySelectedControlIndex =
                _trackingControlIndex;

            if (_trackingSummaryControlStates.Length !=
                _trackingControls.Count)
            {
                _trackingSummaryControlStates =
                    _trackingControls.Count == 0
                        ? Array.Empty<
                            TrackingSummaryControlState>()
                        : new TrackingSummaryControlState[
                            _trackingControls.Count];
            }

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];

                _trackingSummaryControlStates[i] =
                    new TrackingSummaryControlState(
                        control.DisplayName,
                        control.ControlEnabled,
                        control.ControlHealthState,
                        control.ControlError);
            }

            _trackingSummaryStateValid =
                true;
        }

        private readonly struct TrackingSummaryControlState
        {
            public TrackingSummaryControlState(
                string displayName,
                bool enabled,
                TrackingSourceHealthState health,
                string error)
            {
                DisplayName = displayName;
                Enabled = enabled;
                Health = health;
                Error = error;
            }

            public string DisplayName { get; }
            public bool Enabled { get; }
            public TrackingSourceHealthState Health { get; }
            public string Error { get; }
        }

        private string MotionSummary()
        {
            if (_mixer == null)
            {
                return "Motion/expression mixer unavailable.";
            }

            var poseConfigured =
                _mixer.PrimaryPoseLayerConfigured;
            var poseWeight =
                _mixer.PrimaryPoseLayerWeight;
            var manualAvailable =
                _manualExpressionSource != null;
            var manualSequence =
                manualAvailable
                    ? _manualExpressionSource.Sequence
                    : 0L;
            var manualConnected =
                manualAvailable &&
                _mixer.IsExpressionLayerProvider(
                    _manualExpressionSource);
            var blendMode =
                _mixer.ExpressionLayerBlendMode;
            var layerWeight =
                _mixer.ExpressionLayerWeight;
            var expressionInput =
                _manualExpressionNameInput?.text;

            if (_motionSummaryStateValid &&
                _motionSummaryCache != null &&
                _motionSummaryPoseConfigured ==
                    poseConfigured &&
                _motionSummaryPoseWeight ==
                    poseWeight &&
                _motionSummaryManualAvailable ==
                    manualAvailable &&
                _motionSummaryManualSequence ==
                    manualSequence &&
                _motionSummaryManualConnected ==
                    manualConnected &&
                _motionSummaryBlendMode ==
                    blendMode &&
                _motionSummaryLayerWeight ==
                    layerWeight &&
                string.Equals(
                    _motionSummaryExpressionInput,
                    expressionInput,
                    StringComparison.Ordinal))
            {
                return _motionSummaryCache;
            }

            var selectedExpression =
                expressionInput?.Trim();
            StandardExpression expression =
                default;
            var hasSelectedValue =
                manualAvailable &&
                StandardExpressionNames.TryParse(
                    selectedExpression,
                    out expression);
            var selectedValue =
                hasSelectedValue
                    ? _manualExpressionSource
                        .GetExpression(
                            expression)
                    : 0f;

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append(
                "Primary pose layer configured: ");
            builder.Append(
                poseConfigured);
            builder.Append(
                "\nPrimary pose weight: ");
            builder.Append(
                poseWeight.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture));
            builder.Append(
                "\nManual expression source: ");
            builder.Append(
                manualAvailable
                    ? "available"
                    : "missing");
            builder.Append(
                "\nManual layer connected: ");
            builder.Append(
                manualConnected);
            builder.Append(
                "\nExpression blend: ");
            builder.Append(
                blendMode);
            builder.Append(" @ ");
            builder.Append(
                layerWeight.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture));
            builder.Append(
                "\nSelected manual expression: ");
            builder.Append(
                string.IsNullOrWhiteSpace(
                    selectedExpression)
                    ? "<none>"
                    : selectedExpression);
            builder.Append(" = ");

            if (hasSelectedValue)
            {
                builder.Append(
                    selectedValue.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(
                    "<none>");
            }

            builder.Append(
                "\nStandard expressions: neutral, happy, angry, sad, relaxed, surprised, aa, ih, ou, ee, oh, blink, blinkLeft, blinkRight, lookUp, lookDown, lookLeft, lookRight");

            _motionSummaryPoseConfigured =
                poseConfigured;
            _motionSummaryPoseWeight =
                poseWeight;
            _motionSummaryManualAvailable =
                manualAvailable;
            _motionSummaryManualSequence =
                manualSequence;
            _motionSummaryManualConnected =
                manualConnected;
            _motionSummaryBlendMode =
                blendMode;
            _motionSummaryLayerWeight =
                layerWeight;
            _motionSummaryExpressionInput =
                expressionInput;
            _motionSummaryStateValid =
                true;
            _motionSummaryCache =
                builder.ToString();
            return _motionSummaryCache;
        }

        private string EnvironmentSummary()
        {
            var runtime =
                GetCachedEnvironmentRuntime();

            if (runtime == null)
            {
                return "Environment runtime unavailable.";
            }

            var status =
                runtime.Status;
            var transition =
                runtime.TransitionStatus;
            var spaceMode =
                runtime.SpaceMode;
            var transitionPercent =
                transition.Active
                    ? (int)Math.Round(
                        transition.Progress *
                        100f)
                    : 0;

            if (EnvironmentSummaryCacheMatches(
                    status,
                    transition,
                    spaceMode,
                    transitionPercent))
            {
                return _environmentSummaryCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Environment: ");
            builder.Append(
                status.EnvironmentId ??
                "<none>");
            builder.Append("\nState: ");
            builder.Append(
                status.StateId ??
                "<none>");
            builder.Append("\nSpace: ");
            builder.Append(spaceMode);
            builder.Append("\nActive: ");
            builder.Append(status.Active);
            builder.Append("\nTransition: ");

            if (transition.Active)
            {
                builder.Append(
                    transition.Mode);
                builder.Append(' ');
                builder.Append(
                    transitionPercent);
                builder.Append("% (");
                builder.Append(
                    transition.PreviousStateId);
                builder.Append(" → ");
                builder.Append(
                    transition.StateId);
                builder.Append(", ");
                builder.Append(
                    transition.DurationSeconds
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture));
                builder.Append("s)");
            }
            else
            {
                builder.Append(
                    "<idle>");
            }

            builder.Append(
                "\nSelected transition mode: ");
            builder.Append(
                _environmentTransitionMode);
            builder.Append("\nError: ");
            builder.Append(
                status.Error ??
                "<none>");

            CaptureEnvironmentSummaryState(
                status,
                transition,
                spaceMode,
                transitionPercent);
            _environmentSummaryCache =
                builder.ToString();
            return _environmentSummaryCache;
        }

        private bool EnvironmentSummaryCacheMatches(
            EnvironmentRuntimeStatus status,
            EnvironmentTransitionStatus transition,
            EnvironmentSpaceMode spaceMode,
            int transitionPercent)
        {
            return
                _environmentSummaryStateValid &&
                _environmentSummaryCache != null &&
                string.Equals(
                    _environmentSummaryEnvironmentId,
                    status.EnvironmentId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _environmentSummaryStateId,
                    status.StateId,
                    StringComparison.Ordinal) &&
                _environmentSummarySpaceMode ==
                    spaceMode &&
                _environmentSummaryActive ==
                    status.Active &&
                _environmentSummaryTransitionActive ==
                    transition.Active &&
                _environmentSummaryTransitionMode ==
                    transition.Mode &&
                _environmentSummaryTransitionPercent ==
                    transitionPercent &&
                string.Equals(
                    _environmentSummaryPreviousStateId,
                    transition.PreviousStateId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    _environmentSummaryTransitionStateId,
                    transition.StateId,
                    StringComparison.Ordinal) &&
                _environmentSummaryTransitionDuration ==
                    transition.DurationSeconds &&
                _environmentSummarySelectedMode ==
                    _environmentTransitionMode &&
                string.Equals(
                    _environmentSummaryError,
                    status.Error,
                    StringComparison.Ordinal);
        }

        private void CaptureEnvironmentSummaryState(
            EnvironmentRuntimeStatus status,
            EnvironmentTransitionStatus transition,
            EnvironmentSpaceMode spaceMode,
            int transitionPercent)
        {
            _environmentSummaryEnvironmentId =
                status.EnvironmentId;
            _environmentSummaryStateId =
                status.StateId;
            _environmentSummarySpaceMode =
                spaceMode;
            _environmentSummaryActive =
                status.Active;
            _environmentSummaryTransitionActive =
                transition.Active;
            _environmentSummaryTransitionMode =
                transition.Mode;
            _environmentSummaryTransitionPercent =
                transitionPercent;
            _environmentSummaryPreviousStateId =
                transition.PreviousStateId;
            _environmentSummaryTransitionStateId =
                transition.StateId;
            _environmentSummaryTransitionDuration =
                transition.DurationSeconds;
            _environmentSummarySelectedMode =
                _environmentTransitionMode;
            _environmentSummaryError =
                status.Error;
            _environmentSummaryStateValid =
                true;
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
            MaterialOverrideStatus status =
                default;
            var hasStatus =
                !string.IsNullOrWhiteSpace(
                    slotId) &&
                _materialController.TryGetStatus(
                    slotId,
                    out status);
            MaterialSlotDescriptor slot =
                default;
            var hasDescriptor =
                !hasStatus &&
                slotCount > 0 &&
                _materialController.TryGetSlotAt(
                    Mathf.Clamp(
                        _materialSlotIndex,
                        0,
                        slotCount - 1),
                    out slot);
            var errorCount =
                _materialController.ErrorCount;

            if (MaterialSummaryCacheMatches(
                    slotCount,
                    slotId,
                    hasStatus,
                    status,
                    hasDescriptor,
                    slot,
                    errorCount))
            {
                return _materialSummaryCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Material slots: ");
            builder.Append(slotCount);
            builder.Append("\nSelected slot: ");

            if (hasStatus)
            {
                builder.Append(status.SlotId);
                builder.Append(": ");
                builder.Append(status.Health);
                builder.Append(", shader=");
                builder.Append(
                    status.ShaderId ??
                    "<none>");
                builder.Append(", preset=");
                builder.Append(
                    status.PresetId ??
                    "<none>");
                builder.Append(", error=");
                builder.Append(
                    status.Error ??
                    "<none>");
            }
            else if (hasDescriptor)
            {
                builder.Append(slot.Id);
                builder.Append(": source=");
                builder.Append(
                    slot.SourceMaterialName);
                builder.Append(", shader=");
                builder.Append(
                    slot.SourceShaderName);
            }
            else
            {
                builder.Append("<none>");
            }

            builder.Append(
                "\nOverride errors: ");
            builder.Append(errorCount);
            builder.Append(
                "\nControls: cycle slot, apply registered shader ID, set a float property, clear override, or refresh discovered slots.");

            CaptureMaterialSummaryState(
                slotCount,
                slotId,
                hasStatus,
                status,
                hasDescriptor,
                slot,
                errorCount);
            _materialSummaryCache =
                builder.ToString();
            return _materialSummaryCache;
        }

        private bool MaterialSummaryCacheMatches(
            int slotCount,
            string requestedSlotId,
            bool hasStatus,
            MaterialOverrideStatus status,
            bool hasDescriptor,
            MaterialSlotDescriptor descriptor,
            int errorCount)
        {
            return
                _materialSummaryStateValid &&
                _materialSummaryCache != null &&
                _materialSummarySlotCount ==
                    slotCount &&
                string.Equals(
                    _materialSummaryRequestedSlotId,
                    requestedSlotId,
                    StringComparison.Ordinal) &&
                _materialSummaryHasStatus ==
                    hasStatus &&
                (!hasStatus ||
                 (string.Equals(
                      _materialSummarySlotId,
                      status.SlotId,
                      StringComparison.Ordinal) &&
                  _materialSummaryHealth ==
                      status.Health &&
                  string.Equals(
                      _materialSummaryShaderId,
                      status.ShaderId,
                      StringComparison.Ordinal) &&
                  string.Equals(
                      _materialSummaryPresetId,
                      status.PresetId,
                      StringComparison.Ordinal) &&
                  string.Equals(
                      _materialSummaryError,
                      status.Error,
                      StringComparison.Ordinal))) &&
                _materialSummaryHasDescriptor ==
                    hasDescriptor &&
                (!hasDescriptor ||
                 (string.Equals(
                      _materialSummarySlotId,
                      descriptor.Id,
                      StringComparison.Ordinal) &&
                  string.Equals(
                      _materialSummarySourceMaterialName,
                      descriptor.SourceMaterialName,
                      StringComparison.Ordinal) &&
                  string.Equals(
                      _materialSummarySourceShaderName,
                      descriptor.SourceShaderName,
                      StringComparison.Ordinal))) &&
                _materialSummaryErrorCount ==
                    errorCount;
        }

        private void CaptureMaterialSummaryState(
            int slotCount,
            string requestedSlotId,
            bool hasStatus,
            MaterialOverrideStatus status,
            bool hasDescriptor,
            MaterialSlotDescriptor descriptor,
            int errorCount)
        {
            _materialSummarySlotCount =
                slotCount;
            _materialSummaryRequestedSlotId =
                requestedSlotId;
            _materialSummaryHasStatus =
                hasStatus;
            _materialSummaryHasDescriptor =
                hasDescriptor;
            _materialSummarySlotId =
                hasStatus
                    ? status.SlotId
                    : hasDescriptor
                        ? descriptor.Id
                        : null;
            _materialSummaryHealth =
                hasStatus
                    ? status.Health
                    : default;
            _materialSummaryShaderId =
                hasStatus
                    ? status.ShaderId
                    : null;
            _materialSummaryPresetId =
                hasStatus
                    ? status.PresetId
                    : null;
            _materialSummaryError =
                hasStatus
                    ? status.Error
                    : null;
            _materialSummarySourceMaterialName =
                hasDescriptor
                    ? descriptor.SourceMaterialName
                    : null;
            _materialSummarySourceShaderName =
                hasDescriptor
                    ? descriptor.SourceShaderName
                    : null;
            _materialSummaryErrorCount =
                errorCount;
            _materialSummaryStateValid =
                true;
        }

        private string EventsSummary()
        {
            if (eventRuntime == null)
            {
                return "Event runtime unavailable.";
            }

            var ruleCount =
                eventRuntime.RuleCount;
            EventRuntimeRuleSummary selectedRule =
                default;
            var hasSelectedRule =
                ruleCount > 0 &&
                eventRuntime.TryGetRuleSummaryAt(
                    Mathf.Clamp(
                        _eventRuleIndex,
                        0,
                        ruleCount - 1),
                    out selectedRule);
            var traceEnabled =
                eventRuntime.RuleTracingEnabled;
            var maxCommandsPerEvent =
                eventRuntime.MaxCommandsPerEvent;
            var processedEvents =
                eventRuntime.ProcessedEvents;
            var matchedRules =
                eventRuntime.MatchedRules;
            var executedActions =
                eventRuntime.ExecutedActions;
            var failedActions =
                eventRuntime.FailedActions;
            var unhandledActions =
                eventRuntime.UnhandledActions;
            var ambiguousActions =
                eventRuntime.AmbiguousActions;

            if (EventsSummaryCacheMatches(
                    ruleCount,
                    hasSelectedRule,
                    selectedRule,
                    traceEnabled,
                    maxCommandsPerEvent,
                    processedEvents,
                    matchedRules,
                    executedActions,
                    failedActions,
                    unhandledActions,
                    ambiguousActions))
            {
                return _eventsSummaryCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Rules: ");
            builder.Append(ruleCount);
            builder.Append("\nSelected rule: ");
            builder.Append(
                hasSelectedRule
                    ? selectedRule.Id
                    : "<none>");
            builder.Append(
                "\nSelected enabled: ");
            if (hasSelectedRule)
            {
                builder.Append(
                    selectedRule.Enabled);
            }
            else
            {
                builder.Append("n/a");
            }

            builder.Append("\nTrace enabled: ");
            builder.Append(traceEnabled);
            builder.Append(
                "\nMax commands/event: ");
            builder.Append(
                maxCommandsPerEvent);
            builder.Append("\nPersisted rules: ");
            builder.Append(
                _eventRuleStorePath ??
                "<not resolved>");
            builder.Append("\nProcessed events: ");
            builder.Append(processedEvents);
            builder.Append("\nMatched rules: ");
            builder.Append(matchedRules);
            builder.Append(
                "\nExecuted actions: ");
            builder.Append(executedActions);
            builder.Append("\nFailed actions: ");
            builder.Append(failedActions);
            builder.Append(
                "\nUnhandled actions: ");
            builder.Append(unhandledActions);
            builder.Append(
                "\nAmbiguous actions: ");
            builder.Append(ambiguousActions);

            CaptureEventsSummaryState(
                ruleCount,
                hasSelectedRule,
                selectedRule,
                traceEnabled,
                maxCommandsPerEvent,
                processedEvents,
                matchedRules,
                executedActions,
                failedActions,
                unhandledActions,
                ambiguousActions);
            _eventsSummaryCache =
                builder.ToString();
            return _eventsSummaryCache;
        }

        private bool EventsSummaryCacheMatches(
            int ruleCount,
            bool hasSelectedRule,
            EventRuntimeRuleSummary selectedRule,
            bool traceEnabled,
            int maxCommandsPerEvent,
            long processedEvents,
            long matchedRules,
            long executedActions,
            long failedActions,
            long unhandledActions,
            long ambiguousActions)
        {
            return
                _eventsSummaryStateValid &&
                _eventsSummaryCache != null &&
                _eventsSummaryRuleCount ==
                    ruleCount &&
                _eventsSummaryHasSelectedRule ==
                    hasSelectedRule &&
                string.Equals(
                    _eventsSummarySelectedRuleId,
                    hasSelectedRule
                        ? selectedRule.Id
                        : null,
                    StringComparison.Ordinal) &&
                (!hasSelectedRule ||
                 _eventsSummarySelectedRuleEnabled ==
                    selectedRule.Enabled) &&
                _eventsSummaryTraceEnabled ==
                    traceEnabled &&
                _eventsSummaryMaxCommandsPerEvent ==
                    maxCommandsPerEvent &&
                string.Equals(
                    _eventsSummaryRuleStorePath,
                    _eventRuleStorePath,
                    StringComparison.Ordinal) &&
                _eventsSummaryProcessedEvents ==
                    processedEvents &&
                _eventsSummaryMatchedRules ==
                    matchedRules &&
                _eventsSummaryExecutedActions ==
                    executedActions &&
                _eventsSummaryFailedActions ==
                    failedActions &&
                _eventsSummaryUnhandledActions ==
                    unhandledActions &&
                _eventsSummaryAmbiguousActions ==
                    ambiguousActions;
        }

        private void CaptureEventsSummaryState(
            int ruleCount,
            bool hasSelectedRule,
            EventRuntimeRuleSummary selectedRule,
            bool traceEnabled,
            int maxCommandsPerEvent,
            long processedEvents,
            long matchedRules,
            long executedActions,
            long failedActions,
            long unhandledActions,
            long ambiguousActions)
        {
            _eventsSummaryRuleCount =
                ruleCount;
            _eventsSummaryHasSelectedRule =
                hasSelectedRule;
            _eventsSummarySelectedRuleId =
                hasSelectedRule
                    ? selectedRule.Id
                    : null;
            _eventsSummarySelectedRuleEnabled =
                hasSelectedRule &&
                selectedRule.Enabled;
            _eventsSummaryTraceEnabled =
                traceEnabled;
            _eventsSummaryMaxCommandsPerEvent =
                maxCommandsPerEvent;
            _eventsSummaryRuleStorePath =
                _eventRuleStorePath;
            _eventsSummaryProcessedEvents =
                processedEvents;
            _eventsSummaryMatchedRules =
                matchedRules;
            _eventsSummaryExecutedActions =
                executedActions;
            _eventsSummaryFailedActions =
                failedActions;
            _eventsSummaryUnhandledActions =
                unhandledActions;
            _eventsSummaryAmbiguousActions =
                ambiguousActions;
            _eventsSummaryStateValid =
                true;
        }

        private string OutputSummary()
        {
            if (sceneRuntime == null)
            {
                return "Scene/output runtime unavailable.";
            }

            var outputReadable =
                TryGetOverlayOutputForUiRefresh(
                    out var hasAdapter,
                    out var status,
                    out var settings,
                    out var outputError);
            var renderAvailable =
                TryGetRenderSettingsForUiRefresh(
                    out var render);

            if (OutputSummaryCacheMatches(
                    hasAdapter,
                    status,
                    settings,
                    renderAvailable,
                    render))
            {
                return _outputSummaryCache;
            }

            var readiness =
                hasAdapter &&
                outputReadable
                    ? OverlayCaptureReadinessEvaluator
                        .Evaluate(
                            status,
                            settings)
                    : new OverlayCaptureReadiness(
                        false,
                        hasAdapter
                            ? OverlayCaptureReadinessFailure.Faulted
                            : OverlayCaptureReadinessFailure.NotActive,
                        hasAdapter
                            ? outputError ??
                              "Overlay output state is unavailable."
                            : "No overlay output adapter is configured.");
            var minimum =
                renderAvailable
                    ? BroadcastCaptureReadinessEvaluator
                        .Evaluate(
                            BroadcastCaptureTarget
                                .Minimum720p60,
                            readiness,
                            render.Width,
                            render.Height,
                            render.TargetFrameRate,
                            render.RunInBackground)
                    : new BroadcastCaptureReadiness(
                        false,
                        BroadcastCaptureReadinessFailure
                            .InvalidTarget,
                        "Render bootstrap is unavailable.");
            var recommended =
                renderAvailable
                    ? BroadcastCaptureReadinessEvaluator
                        .Evaluate(
                            BroadcastCaptureTarget
                                .Recommended1080p60,
                            readiness,
                            render.Width,
                            render.Height,
                            render.TargetFrameRate,
                            render.RunInBackground)
                    : new BroadcastCaptureReadiness(
                        false,
                        BroadcastCaptureReadinessFailure
                            .InvalidTarget,
                        "Render bootstrap is unavailable.");

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Output state: ");
            if (hasAdapter)
            {
                builder.Append(
                    status.State);
            }
            else
            {
                builder.Append("none");
            }

            builder.Append("\nTransparent: ");
            if (hasAdapter)
            {
                builder.Append(
                    settings.Transparent);
            }
            else
            {
                builder.Append("n/a");
            }

            builder.Append("\nTopmost: ");
            if (hasAdapter)
            {
                builder.Append(
                    settings.Topmost);
            }
            else
            {
                builder.Append("n/a");
            }

            builder.Append(
                "\nClick-through: ");
            if (hasAdapter)
            {
                builder.Append(
                    settings.ClickThrough);
            }
            else
            {
                builder.Append("n/a");
            }

            builder.Append(
                "\nCapture ready: ");
            builder.Append(readiness.Ready);
            builder.Append(
                "\nCapture readiness: ");
            builder.Append(readiness.Failure);
            builder.Append(
                "\n720p60 configured: ");
            builder.Append(minimum.Ready);
            builder.Append(
                "\n1080p60 configured: ");
            builder.Append(recommended.Ready);
            builder.Append(
                "\nOutput error: ");
            builder.Append(
                hasAdapter
                    ? status.LastError ??
                      "<none>"
                    : "<none>");

            CaptureOutputSummaryState(
                hasAdapter,
                status,
                settings,
                renderAvailable,
                render);
            _outputSummaryCache =
                builder.ToString();
            return _outputSummaryCache;
        }

        private bool OutputSummaryCacheMatches(
            bool hasAdapter,
            OverlayOutputStatus status,
            OverlayOutputSettings settings,
            bool renderAvailable,
            RenderRuntimeSettings render)
        {
            return
                _outputSummaryStateValid &&
                _outputSummaryCache != null &&
                _outputSummaryHasAdapter ==
                    hasAdapter &&
                (!hasAdapter ||
                 (_outputSummaryState ==
                      status.State &&
                  string.Equals(
                      _outputSummaryError,
                      status.LastError,
                      StringComparison.Ordinal) &&
                  _outputSummaryClientWidth ==
                      status.ClientWidth &&
                  _outputSummaryClientHeight ==
                      status.ClientHeight &&
                  _outputSummaryTransparent ==
                      settings.Transparent &&
                  _outputSummaryTopmost ==
                      settings.Topmost &&
                  _outputSummaryClickThrough ==
                      settings.ClickThrough)) &&
                _outputSummaryRenderAvailable ==
                    renderAvailable &&
                _outputSummaryRenderWidth ==
                    render.Width &&
                _outputSummaryRenderHeight ==
                    render.Height &&
                _outputSummaryTargetFrameRate ==
                    render.TargetFrameRate &&
                _outputSummaryRunInBackground ==
                    render.RunInBackground;
        }

        private void CaptureOutputSummaryState(
            bool hasAdapter,
            OverlayOutputStatus status,
            OverlayOutputSettings settings,
            bool renderAvailable,
            RenderRuntimeSettings render)
        {
            _outputSummaryHasAdapter =
                hasAdapter;
            _outputSummaryState =
                hasAdapter
                    ? status.State
                    : default;
            _outputSummaryError =
                hasAdapter
                    ? status.LastError
                    : null;
            _outputSummaryClientWidth =
                hasAdapter
                    ? status.ClientWidth
                    : 0;
            _outputSummaryClientHeight =
                hasAdapter
                    ? status.ClientHeight
                    : 0;
            _outputSummaryTransparent =
                hasAdapter &&
                settings.Transparent;
            _outputSummaryTopmost =
                hasAdapter &&
                settings.Topmost;
            _outputSummaryClickThrough =
                hasAdapter &&
                settings.ClickThrough;
            _outputSummaryRenderAvailable =
                renderAvailable;
            _outputSummaryRenderWidth =
                render.Width;
            _outputSummaryRenderHeight =
                render.Height;
            _outputSummaryTargetFrameRate =
                render.TargetFrameRate;
            _outputSummaryRunInBackground =
                render.RunInBackground;
            _outputSummaryStateValid =
                true;
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
            TryGetRenderSettingsForUiRefresh(
                out var render);
            var runtimeStarted =
                applicationBootstrap.IsStarted;
            var configurationPath =
                applicationBootstrap.ConfigurationPath;
            var registeredCapabilities =
                capabilities?.RegisteredCount ??
                0;
            var enabledCapabilities =
                capabilities?.EnabledCount ??
                0;

            if (SettingsSummaryCacheMatches(
                    runtimeStarted,
                    configurationPath,
                    registeredCapabilities,
                    enabledCapabilities,
                    hasSelectedCapability,
                    selectedCapability,
                    render))
            {
                return _settingsSummaryCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append("Runtime started: ");
            builder.Append(runtimeStarted);
            builder.Append("\nConfiguration: ");
            builder.Append(
                configurationPath ??
                "<default/not resolved>");
            builder.Append(
                "\nCapabilities registered: ");
            builder.Append(
                registeredCapabilities);
            builder.Append(
                "\nCapabilities enabled: ");
            builder.Append(
                enabledCapabilities);
            builder.Append(
                "\nSelected capability: ");
            builder.Append(
                hasSelectedCapability
                    ? selectedCapability.Id
                    : "<none>");
            builder.Append(
                "\nCapability state: ");
            if (hasSelectedCapability)
            {
                builder.Append(
                    selectedCapability.State);
            }
            else
            {
                builder.Append(
                    "n/a");
            }

            builder.Append(
                "\nCapability error: ");
            builder.Append(
                hasSelectedCapability
                    ? selectedCapability.Error ??
                      "<none>"
                    : "n/a");
            builder.Append(
                "\nRender scale: ");
            builder.Append(
                render.RenderScale.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            builder.Append(
                "\nTarget FPS: ");
            builder.Append(
                render.TargetFrameRate);
            builder.Append(
                "\nVSync: ");
            builder.Append(
                render.UseVSync);
            builder.Append(
                "\nRun in background: ");
            builder.Append(
                render.RunInBackground);

            CaptureSettingsSummaryState(
                runtimeStarted,
                configurationPath,
                registeredCapabilities,
                enabledCapabilities,
                hasSelectedCapability,
                selectedCapability,
                render);
            _settingsSummaryCache =
                builder.ToString();
            return _settingsSummaryCache;
        }

        private bool SettingsSummaryCacheMatches(
            bool runtimeStarted,
            string configurationPath,
            int registeredCapabilities,
            int enabledCapabilities,
            bool hasSelectedCapability,
            CapabilityStatusSnapshot selectedCapability,
            RenderRuntimeSettings render)
        {
            return
                _settingsSummaryStateValid &&
                _settingsSummaryCache != null &&
                _settingsSummaryRuntimeStarted ==
                    runtimeStarted &&
                string.Equals(
                    _settingsSummaryConfigurationPath,
                    configurationPath,
                    StringComparison.Ordinal) &&
                _settingsSummaryRegisteredCapabilities ==
                    registeredCapabilities &&
                _settingsSummaryEnabledCapabilities ==
                    enabledCapabilities &&
                _settingsSummaryHasSelectedCapability ==
                    hasSelectedCapability &&
                string.Equals(
                    _settingsSummaryCapabilityId,
                    hasSelectedCapability
                        ? selectedCapability.Id
                        : null,
                    StringComparison.Ordinal) &&
                (!hasSelectedCapability ||
                 _settingsSummaryCapabilityState ==
                    selectedCapability.State) &&
                string.Equals(
                    _settingsSummaryCapabilityError,
                    hasSelectedCapability
                        ? selectedCapability.Error
                        : null,
                    StringComparison.Ordinal) &&
                _settingsSummaryRenderScale ==
                    render.RenderScale &&
                _settingsSummaryTargetFrameRate ==
                    render.TargetFrameRate &&
                _settingsSummaryUseVSync ==
                    render.UseVSync &&
                _settingsSummaryRunInBackground ==
                    render.RunInBackground;
        }

        private void CaptureSettingsSummaryState(
            bool runtimeStarted,
            string configurationPath,
            int registeredCapabilities,
            int enabledCapabilities,
            bool hasSelectedCapability,
            CapabilityStatusSnapshot selectedCapability,
            RenderRuntimeSettings render)
        {
            _settingsSummaryRuntimeStarted =
                runtimeStarted;
            _settingsSummaryConfigurationPath =
                configurationPath;
            _settingsSummaryRegisteredCapabilities =
                registeredCapabilities;
            _settingsSummaryEnabledCapabilities =
                enabledCapabilities;
            _settingsSummaryHasSelectedCapability =
                hasSelectedCapability;
            _settingsSummaryCapabilityId =
                hasSelectedCapability
                    ? selectedCapability.Id
                    : null;
            _settingsSummaryCapabilityState =
                hasSelectedCapability
                    ? selectedCapability.State
                    : default;
            _settingsSummaryCapabilityError =
                hasSelectedCapability
                    ? selectedCapability.Error
                    : null;
            _settingsSummaryRenderScale =
                render.RenderScale;
            _settingsSummaryTargetFrameRate =
                render.TargetFrameRate;
            _settingsSummaryUseVSync =
                render.UseVSync;
            _settingsSummaryRunInBackground =
                render.RunInBackground;
            _settingsSummaryStateValid =
                true;
        }

        private IReadOnlyList<RuntimeMetric>
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
                    (metrics.Count +
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
                    metrics.Count,
                    start +
                    pageSize);

            var reportInterval =
                diagnostics.ReportIntervalSeconds;
            var consoleLogging =
                diagnostics.ConsoleLoggingEnabled;
            var csvEvidence =
                diagnostics.CsvEvidenceEnabled;
            var evidenceDirectory =
                diagnostics.EvidenceDirectory;

            if (_diagnosticsSummaryCache != null &&
                _diagnosticsSummarySequence ==
                    snapshot.Sequence &&
                _diagnosticsSummaryPage ==
                    _diagnosticsMetricPage &&
                _diagnosticsSummaryReportInterval ==
                    reportInterval &&
                _diagnosticsSummaryConsoleLogging ==
                    consoleLogging &&
                _diagnosticsSummaryCsvEvidence ==
                    csvEvidence &&
                string.Equals(
                    _diagnosticsSummaryEvidenceDirectory,
                    evidenceDirectory,
                    StringComparison.Ordinal))
            {
                return _diagnosticsSummaryCache;
            }

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
                $"Reporting: every {reportInterval:0.###}s | console {consoleLogging} | CSV {csvEvidence}");
            builder.AppendLine(
                $"Evidence directory: {evidenceDirectory}");
            builder.AppendLine();
            builder.AppendLine(
                $"Subsystem metrics: {metrics.Count} | page {_diagnosticsMetricPage + 1}/{pageCount}");

            if (metrics.Count == 0)
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

            _diagnosticsSummarySequence =
                snapshot.Sequence;
            _diagnosticsSummaryPage =
                _diagnosticsMetricPage;
            _diagnosticsSummaryReportInterval =
                reportInterval;
            _diagnosticsSummaryConsoleLogging =
                consoleLogging;
            _diagnosticsSummaryCsvEvidence =
                csvEvidence;
            _diagnosticsSummaryEvidenceDirectory =
                evidenceDirectory;
            _diagnosticsSummaryCache =
                builder.ToString();

            return _diagnosticsSummaryCache;
        }

        private void InvalidateDiagnosticsSummaryCache()
        {
            _diagnosticsSummarySequence = -1;
            _diagnosticsSummaryPage = -1;
            _diagnosticsSummaryReportInterval =
                float.NaN;
            _diagnosticsSummaryConsoleLogging = false;
            _diagnosticsSummaryCsvEvidence = false;
            _diagnosticsSummaryEvidenceDirectory = null;
            _diagnosticsSummaryCache = null;
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
                    (metrics.Count +
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
                    (metrics.Count +
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
                    GetDiagnosticsNextPageLabel(
                        _diagnosticsMetricPage,
                        pageCount));
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
                    !available
                        ? "CSV Evidence: n/a"
                        : diagnostics.CsvEvidenceEnabled
                            ? "CSV Evidence: On"
                            : "CSV Evidence: Off");
            }

            if (_diagnosticsConsoleButton != null)
            {
                _diagnosticsConsoleButton.interactable =
                    available;
                SetButtonLabel(
                    _diagnosticsConsoleButton,
                    !available
                        ? "Console Log: n/a"
                        : diagnostics.ConsoleLoggingEnabled
                            ? "Console Log: On"
                            : "Console Log: Off");
            }
        }

        private static void AddActionPanelBackground(
            RectTransform panel)
        {
            if (panel == null)
            {
                return;
            }

            var image =
                panel.gameObject
                    .AddComponent<Image>();
            image.color =
                new Color(
                    0.075f,
                    0.085f,
                    0.105f,
                    0.96f);
            image.raycastTarget =
                false;
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

            var colors =
                button.colors;
            colors.normalColor =
                Color.white;
            colors.highlightedColor =
                new Color(
                    1.18f,
                    1.18f,
                    1.18f,
                    1f);
            colors.pressedColor =
                new Color(
                    0.78f,
                    0.82f,
                    0.88f,
                    1f);
            colors.selectedColor =
                colors.highlightedColor;
            colors.disabledColor =
                new Color(
                    0.55f,
                    0.55f,
                    0.55f,
                    0.55f);
            colors.fadeDuration =
                0.08f;
            button.colors =
                colors;

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
            MonoBehaviour[] activeDependencyBehaviours =
                null;
            ResolveCharacterFileSelectionAdapter(
                ref activeDependencyBehaviours);
        }

        private void ResolveCharacterFileSelectionAdapter(
            ref MonoBehaviour[] activeDependencyBehaviours)
        {
            if (IsServiceAlive(_characterFileSelectionAdapter) &&
                _characterFileSelectionAdapter.IsSupported)
            {
                return;
            }

            ICharacterFileSelectionAdapter fallback =
                null;
            var behaviours =
                GetActiveDependencyBehaviours(
                    ref activeDependencyBehaviours);

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

        private void ResolveAppearanceRuntime(
            ref MonoBehaviour[] activeDependencyBehaviours)
        {
            if (IsServiceAlive(
                    _appearanceRuntime))
            {
                return;
            }

            _appearanceRuntime = null;

            var behaviours =
                GetActiveDependencyBehaviours(
                    ref activeDependencyBehaviours);

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

        private void ResolveConcreteDependencies(
            ref MonoBehaviour[] activeDependencyBehaviours)
        {
            if (sceneRuntime == null &&
                applicationBootstrap != null)
            {
                sceneRuntime =
                    applicationBootstrap.SceneRuntime;
            }

            var needsScan =
                applicationBootstrap == null ||
                sceneRuntime == null ||
                diagnostics == null ||
                eventRuntime == null ||
                _mixer == null ||
                _manualExpressionSource == null ||
                _materialController == null;

            if (!needsScan)
            {
                return;
            }

            var behaviours =
                GetActiveDependencyBehaviours(
                    ref activeDependencyBehaviours);
            SingleCharacterSceneRuntime sceneCandidate =
                null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                {
                    continue;
                }

                if (applicationBootstrap == null &&
                    behaviour is
                        ApplicationRuntimeBootstrap bootstrap)
                {
                    applicationBootstrap =
                        bootstrap;
                }

                if (sceneRuntime == null &&
                    sceneCandidate == null &&
                    behaviour is
                        SingleCharacterSceneRuntime candidate)
                {
                    sceneCandidate =
                        candidate;
                }

                if (diagnostics == null &&
                    behaviour is
                        RuntimeDiagnostics runtimeDiagnostics)
                {
                    diagnostics =
                        runtimeDiagnostics;
                }

                if (eventRuntime == null &&
                    behaviour is
                        EventRuntimeHost runtimeHost)
                {
                    eventRuntime =
                        runtimeHost;
                }

                if (_mixer == null &&
                    behaviour is
                        MotionExpressionMixer mixer)
                {
                    _mixer =
                        mixer;
                }

                if (_manualExpressionSource == null &&
                    behaviour is
                        ManualExpressionLayerSource
                            manualExpressionSource)
                {
                    _manualExpressionSource =
                        manualExpressionSource;
                }

                if (_materialController == null &&
                    behaviour is
                        MaterialOverrideController
                            materialController)
                {
                    _materialController =
                        materialController;
                }
            }

            if (sceneRuntime == null)
            {
                sceneRuntime =
                    applicationBootstrap?.SceneRuntime;

                if (sceneRuntime == null)
                {
                    sceneRuntime =
                        sceneCandidate;
                }
            }
        }

        private IEnvironmentRuntime
            GetCachedEnvironmentRuntime()
        {
            if (sceneRuntime == null ||
                !ReferenceEquals(
                    _environmentRuntimeOwner,
                    sceneRuntime) ||
                !IsServiceAlive(_environmentRuntime))
            {
                return null;
            }

            return _environmentRuntime;
        }

        private static MonoBehaviour[]
            GetActiveDependencyBehaviours(
                ref MonoBehaviour[] behaviours)
        {
            behaviours ??=
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            return behaviours;
        }

        private void ResolveTrackingControls(
            bool force = false)
        {
            var missingOnly =
                _trackingControls.Count == 0;
            var rebuild =
                missingOnly;

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
                _nextTrackingControlResolveTime = 0f;
                return;
            }

            var now =
                Time.unscaledTime;

            if (missingOnly &&
                !force &&
                now <
                    _nextTrackingControlResolveTime)
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

            _nextTrackingControlResolveTime =
                _trackingControls.Count == 0
                    ? now +
                      MissingTrackingControlDiscoveryRetrySeconds
                    : 0f;
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
            bool available,
            bool selected)
        {
            if (label == null)
            {
                return;
            }

            title ??=
                string.Empty;

            var expected =
                selected && available
                    ? "> " + title
                    : "  " + title;

            if (!string.Equals(
                    label.text,
                    expected,
                    StringComparison.Ordinal))
            {
                label.text =
                    expected;
            }

            label.fontStyle =
                selected && available
                    ? FontStyle.Bold
                    : FontStyle.Normal;
            label.color =
                !available
                    ? new Color(
                        0.43f,
                        0.46f,
                        0.52f,
                        1f)
                    : selected
                        ? new Color(
                            0.58f,
                            0.76f,
                            1f,
                            1f)
                        : new Color(
                            0.90f,
                            0.92f,
                            0.95f,
                            1f);
        }

        private string GetAppearanceTransitionButtonLabel(
            AppearanceRuntimeStatus status)
        {
            var busy =
                status.Busy &&
                !string.IsNullOrWhiteSpace(
                    status.ActiveTransitionId);
            var id =
                busy
                    ? status.ActiveTransitionId
                    : GetSelectedAppearanceTransitionId();
            var percent =
                busy
                    ? (int)Math.Round(
                        status.TransitionProgress01 *
                        100.0)
                    : -1;

            if (_appearanceTransitionLabelCache != null &&
                _appearanceTransitionLabelBusy ==
                    busy &&
                _appearanceTransitionLabelPercent ==
                    percent &&
                string.Equals(
                    _appearanceTransitionLabelId,
                    id,
                    StringComparison.Ordinal))
            {
                return _appearanceTransitionLabelCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append(
                "Transition: ");
            builder.Append(
                id ??
                "<none>");

            if (busy)
            {
                builder.Append(' ');
                builder.Append(
                    percent);
                builder.Append('%');
            }

            _appearanceTransitionLabelBusy =
                busy;
            _appearanceTransitionLabelId =
                id;
            _appearanceTransitionLabelPercent =
                percent;
            _appearanceTransitionLabelCache =
                builder.ToString();
            return _appearanceTransitionLabelCache;
        }

        private static string GetEnvironmentTransitionModeLabel(
            EnvironmentTransitionMode mode)
        {
            return mode switch
            {
                EnvironmentTransitionMode.Cut =>
                    "Transition: Cut",
                EnvironmentTransitionMode.Fade =>
                    "Transition: Fade",
                EnvironmentTransitionMode.Crossfade =>
                    "Transition: Crossfade",
                EnvironmentTransitionMode.Dissolve =>
                    "Transition: Dissolve",
                _ =>
                    "Transition: Unknown"
            };
        }

        private string GetDiagnosticsNextPageLabel(
            int page,
            int pageCount)
        {
            if (pageCount <= 1)
            {
                _diagnosticsNextLabelPage =
                    page;
                _diagnosticsNextLabelPageCount =
                    pageCount;
                _diagnosticsNextLabelCache =
                    "Next Metrics";
                return _diagnosticsNextLabelCache;
            }

            if (_diagnosticsNextLabelCache != null &&
                _diagnosticsNextLabelPage ==
                    page &&
                _diagnosticsNextLabelPageCount ==
                    pageCount)
            {
                return _diagnosticsNextLabelCache;
            }

            var builder =
                _summaryBuilder;
            builder.Clear();
            builder.Append(
                "Next Metrics (");
            builder.Append(
                page + 1);
            builder.Append('/');
            builder.Append(
                pageCount);
            builder.Append(')');

            _diagnosticsNextLabelPage =
                page;
            _diagnosticsNextLabelPageCount =
                pageCount;
            _diagnosticsNextLabelCache =
                builder.ToString();
            return _diagnosticsNextLabelCache;
        }

        private string GetTrackingToggleLabel(
            ITrackingRuntimeControl control)
        {
            if (control == null)
            {
                _trackingToggleLabelHasControl =
                    false;
                _trackingToggleLabelDisplayName =
                    null;
                _trackingToggleLabelCache =
                    "No Tracking Source";
                return _trackingToggleLabelCache;
            }

            var displayName =
                control.DisplayName;
            var enabled =
                control.ControlEnabled;

            if (_trackingToggleLabelCache != null &&
                _trackingToggleLabelHasControl &&
                _trackingToggleLabelEnabled ==
                    enabled &&
                string.Equals(
                    _trackingToggleLabelDisplayName,
                    displayName,
                    StringComparison.Ordinal))
            {
                return _trackingToggleLabelCache;
            }

            _trackingToggleLabelHasControl =
                true;
            _trackingToggleLabelDisplayName =
                displayName;
            _trackingToggleLabelEnabled =
                enabled;
            _trackingToggleLabelCache =
                string.Concat(
                    enabled
                        ? "Disable "
                        : "Enable ",
                    displayName);
            return _trackingToggleLabelCache;
        }

        private string GetSettingsCapabilityLabel(
            bool hasSelection,
            CapabilityStatusSnapshot selected)
        {
            if (!hasSelection)
            {
                _settingsCapabilityLabelHasSelection =
                    false;
                _settingsCapabilityLabelId = null;
                _settingsCapabilityLabelCache =
                    "No Capabilities";
                return _settingsCapabilityLabelCache;
            }

            if (_settingsCapabilityLabelCache != null &&
                _settingsCapabilityLabelHasSelection &&
                _settingsCapabilityLabelState ==
                    selected.State &&
                string.Equals(
                    _settingsCapabilityLabelId,
                    selected.Id,
                    StringComparison.Ordinal))
            {
                return _settingsCapabilityLabelCache;
            }

            _settingsCapabilityLabelHasSelection =
                true;
            _settingsCapabilityLabelState =
                selected.State;
            _settingsCapabilityLabelId =
                selected.Id;
            _settingsCapabilityLabelCache =
                string.Concat(
                    selected.State ==
                        CapabilityState.Enabled
                        ? "Disable "
                        : selected.State ==
                            CapabilityState.Faulted
                            ? "Retry "
                            : "Enable ",
                    selected.Id);
            return _settingsCapabilityLabelCache;
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
            input.customCaretColor =
                true;
            input.caretColor =
                new Color(
                    0.58f,
                    0.76f,
                    1f,
                    1f);
            input.selectionColor =
                new Color(
                    0.32f,
                    0.48f,
                    0.72f,
                    0.55f);

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

            if (UnityEngine.Application.isPlaying)
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
