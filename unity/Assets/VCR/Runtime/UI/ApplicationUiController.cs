using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VCR.Runtime.Application;
using VCR.Runtime.Appearance;
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

        private readonly List<ITrackingRuntimeControl>
            _trackingControls =
                new();

        private Canvas _canvas;
        private RectTransform _root;
        private Text _statusText;
        private Text _sectionTitle;
        private Text _contentText;
        private Button _saveButton;
        private Button _recoverOutputButton;

        private RectTransform _contextActions;
        private RectTransform _appearanceActions;
        private InputField _characterPathInput;
        private Button _loadCharacterButton;
        private Button _reloadCharacterButton;
        private Button _unloadCharacterButton;
        private Button _apply720p60Button;
        private Button _apply1080p60Button;
        private Button _trackingPreviousButton;
        private Button _trackingToggleButton;
        private Button _trackingRecoverButton;
        private Button _trackingNextButton;
        private Button _appearancePreviousButton;
        private Button _appearanceNextButton;
        private Button _appearanceTransitionButton;
        private Button _appearanceRestoreButton;

        private ITrackingPresenceProvider _trackingPresence;
        private IAppearanceRuntime _appearanceRuntime;
        private MotionExpressionMixer _mixer;
        private MaterialOverrideController _materialController;

        private float _nextRefreshTime;
        private string _lastActionMessage;
        private int _trackingControlIndex;
        private int _appearanceTransitionIndex;

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
            _contextActions = null;
            _appearanceActions = null;
            _characterPathInput = null;
            _loadCharacterButton = null;
            _reloadCharacterButton = null;
            _unloadCharacterButton = null;
            _apply720p60Button = null;
            _apply1080p60Button = null;
            _trackingPreviousButton = null;
            _trackingToggleButton = null;
            _trackingRecoverButton = null;
            _trackingNextButton = null;
            _appearancePreviousButton = null;
            _appearanceNextButton = null;
            _appearanceTransitionButton = null;
            _appearanceRestoreButton = null;

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

            ResolveTrackingControls();
            ResolveAppearanceRuntime();

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
                _trackingPresence != null ||
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
                    new Vector2(24f, 188f);
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
            pathLayout.preferredWidth = 440f;

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
            if (_appearanceRuntime == null ||
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
            if (_appearanceRuntime == null)
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
            if (_appearanceRuntime == null ||
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
            if (_appearanceRuntime == null)
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

            if (_appearanceActions != null &&
                _appearanceActions.gameObject.activeSelf !=
                    characterSelected)
            {
                _appearanceActions.gameObject.SetActive(
                    characterSelected);
            }

            SetActive(
                _characterPathInput,
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
                var appearanceAvailable =
                    _appearanceRuntime != null &&
                    _appearanceRuntime.Status.State !=
                        AppearanceRuntimeState.Unconfigured;

                if (_appearancePreviousButton != null)
                {
                    _appearancePreviousButton.interactable =
                        appearanceAvailable &&
                        _appearanceRuntime.PresetIds.Count > 0 &&
                        !_appearanceRuntime.Status.Busy;
                }

                if (_appearanceNextButton != null)
                {
                    _appearanceNextButton.interactable =
                        appearanceAvailable &&
                        _appearanceRuntime.PresetIds.Count > 0 &&
                        !_appearanceRuntime.Status.Busy;
                }

                if (_appearanceTransitionButton != null)
                {
                    _appearanceTransitionButton.interactable =
                        appearanceAvailable &&
                        !_appearanceRuntime.Status.Busy;

                    var transitionLabel =
                        GetSelectedAppearanceTransitionId();

                    SetButtonLabel(
                        _appearanceTransitionButton,
                        "Transition: " +
                        transitionLabel);
                }

                if (_appearanceRestoreButton != null)
                {
                    _appearanceRestoreButton.interactable =
                        appearanceAvailable &&
                        !_appearanceRuntime.Status.Busy;
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

            var text =
                $"Runtime state: {status.State}\n" +
                $"Character loaded: {status.HasCharacter}\n" +
                $"Model path: {status.CurrentCharacterPath ?? "<none>"}\n" +
                $"Last runtime error: {status.LastError ?? "<none>"}";

            if (_appearanceRuntime == null)
            {
                return text +
                    "\nAppearance: runtime unavailable";
            }

            var appearance =
                _appearanceRuntime.Status;

            return text +
                $"\nAppearance state: {appearance.State}" +
                $"\nAppearance preset: {appearance.CurrentPresetId ?? "<none>"}" +
                $"\nOutfit: {appearance.CurrentOutfitId ?? "<none>"}" +
                $"\nTransition: {appearance.ActiveTransitionId ?? "<none>"}" +
                $"\nAppearance error: {appearance.LastError ?? "<none>"}";
        }

        private string TrackingSummary()
        {
            if (_trackingPresence == null)
            {
                return "Tracking provider unavailable.";
            }

            var presence =
                _trackingPresence.Presence;

            var summary =
                $"Subject: {presence.SubjectState}\n" +
                $"Any source available: {presence.AnySourceAvailable}\n" +
                $"Face source: {presence.FaceSourceAvailable}\n" +
                $"Body/hands source: {presence.BodyHandsSourceAvailable}\n" +
                $"Full-body source: {presence.FullBodySourceAvailable}\n" +
                $"Events: {presence.Events}";

            if (_trackingControls.Count == 0)
            {
                return summary +
                    "\nSource controls: <none>";
            }

            var lines =
                new List<string>(
                    _trackingControls.Count);

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];
                var selected =
                    i == _trackingControlIndex
                        ? ">"
                        : " ";

                lines.Add(
                    $"{selected} {control.DisplayName}: " +
                    $"enabled={control.ControlEnabled}, " +
                    $"health={control.ControlHealthState}, " +
                    $"error={control.ControlError ?? "<none>"}");
            }

            return summary +
                "\nSource controls:\n" +
                string.Join(
                    "\n",
                    lines);
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

        private void ResolveAppearanceRuntime()
        {
            if (_appearanceRuntime is
                    MonoBehaviour current &&
                current != null)
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

        private static void SetButtonLabel(
            Button button,
            string label)
        {
            if (button != null &&
                button.GetComponentInChildren<Text>()
                    is Text text)
            {
                text.text = label;
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
                        ApplicationUiSection.Character)
                    {
                        RefreshContextActions();
                    }
                });

            return input;
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
