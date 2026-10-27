using System;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Scene;
using VCR.Runtime.Output;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Opt-in UI Toolkit workspace prototype. Attach to a dedicated GameObject
    /// with a UIDocument/PanelSettings; it does not replace the existing uGUI
    /// controller or own the character/rendering lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class WorkspaceToolkitShell : MonoBehaviour
    {
        [SerializeField] private bool advancedWorkspace;
        [SerializeField] private SingleCharacterSceneRuntime sceneRuntime;
        private DesktopNativeSettingsWindow _workspaceNativeSettings;
        private bool _nativeSettingsWasVisible;
        private bool _runInBackgroundBeforeSettings;
        private string _nativeModelPathOverride;
        [SerializeField, Min(0.1f)] private float statusRefreshSeconds = 0.5f;
        private float _nextStatusRefresh;
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _workspaceRoot;
        private VisualElement _dockHost;
        private Label _status;
        private Label _sourcesStatus;
        private Label _sourcesFeedback;
        private TextField _modelPathInput;
        private Button _loadModelButton;
        private Label _inspectorStatus;
        private Label _inspectorModelPath;
        private Label _inspectorCameraStatus;
        private Button _applySceneViewButton;
        private Button _applyRenderButton;
        private IntegerField _targetFrameRateInput;
        private Toggle _vsyncInput;
        private SingleCharacterSceneRuntime _renderInputsRuntime;
        private Label _inspectorFeedback;
        private Label _trackingStatus;
        private Button _reloadCharacterButton;
        private Button _unloadCharacterButton;
        private Label _actionResult;
        private bool _characterActionPending;
        private bool _characterRestoreAttempted;
        private bool _characterRestoreInProgress;
        private const string LastSuccessfulCharacterPathKey = "VCR.Character.LastSuccessfulPath.v1";
        private Label _previewStatus;
        private VisualElement _leftDock;
        private VisualElement _rightDock;
        private VisualElement _bottomDock;
        private Texture _previewTexture;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private bool discoverCameraPreview = true;
        [SerializeField] private RenderTexture previewRenderTexture;
        private Image _previewImage;
        private bool _previewBoundFromCamera;
        private bool _discoveredPreviewCamera;
        private bool _previewExplicitlyBound;
        private VisualElement _dragOverlay;
        private Label _dockTargetLabel;
        private Camera _ownedPreviewCamera;
        private RenderTexture _ownedPreviewTexture;
        private int _previewWidth;
        private int _previewHeight;
        private const string TabModeKey = "VCR.UI.Workspace.TabMode.v1";
        private bool _tabMode;
        private bool _previewRenderUnsupported;
        private float _nextPreviewRetryAt;
        private const string WorkspaceModeKey = "VCR.UI.Workspace.Advanced.v1";
        private const string LeftWidthKey = "VCR.UI.Workspace.LeftWidth.v1";
        private const string RightWidthKey = "VCR.UI.Workspace.RightWidth.v1";
        private const string BottomHeightKey = "VCR.UI.Workspace.BottomHeight.v1";
        private const string BottomVisibleKey = "VCR.UI.Workspace.BottomVisible.v1";
        private const string LeftVisibleKey = "VCR.UI.Workspace.LeftVisible.v1";
        private const string ScenesHeightKey = "VCR.UI.Workspace.ScenesHeight.v1";
        private const string RightInspectorHeightKey = "VCR.UI.Workspace.InspectorHeight.v1";
        private float _inspectorHeight = 260f;
        private float _scenesHeight = 180f;
        private const string RightVisibleKey = "VCR.UI.Workspace.RightVisible.v1";
        private bool _leftVisible = true;
        private bool _rightVisible = true;
        private bool _bottomVisible = true;
        private float _leftWidth = 220f;
        private float _rightWidth = 260f;
        private float _bottomHeight = 150f;

        private string _lastPersistedLayoutSignature;
        private float _nextLayoutPersistenceCheck;

        private string CaptureLayoutSignature()
        {
            var keys = new[]
            {
                WorkspaceModeKey, LeftWidthKey, ScenesHeightKey, RightInspectorHeightKey,
                RightWidthKey, BottomHeightKey, BottomVisibleKey, LeftVisibleKey,
                RightVisibleKey, TabModeKey
            };
            var signature = new System.Text.StringBuilder(512);
            foreach (var key in keys)
                signature.Append(key).Append('=').Append(PlayerPrefs.GetString(key, "#"))
                    .Append('/').Append(PlayerPrefs.GetInt(key, -9999))
                    .Append('/').Append(PlayerPrefs.GetFloat(key, -9999f)).Append(';');
            foreach (var dock in new[] { "left", "right", "bottom" })
                signature.Append(PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab." + dock + ".v1", "")).Append(';');
            foreach (var id in WorkspacePanelCatalog.GetDefaultPanelIds(true))
                signature.Append(id).Append(':')
                    .Append(PlayerPrefs.GetString("VCR.UI.Workspace.Dock." + id + ".v1", ""))
                    .Append(':').Append(PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + id + ".v1", -1)).Append(';');
            return signature.ToString();
        }

        private void PersistLayoutIfChanged()
        {
            if (_workspaceRoot == null || Time.unscaledTime < _nextLayoutPersistenceCheck)
                return;
            _nextLayoutPersistenceCheck = Time.unscaledTime + 2f;
            var signature = CaptureLayoutSignature();
            if (signature == _lastPersistedLayoutSignature)
                return;
            if (WorkspaceLayoutFile.Save())
                _lastPersistedLayoutSignature = signature;
        }

        private void OnEnable()
        {
            InitializeWorkspaceTrackingRestore();
            ResetWorkspaceTrackingPanel();
            ResetWorkspaceCapabilitiesPanel();
            ResetWorkspaceLogPanel();
            SubscribeWorkspaceLog();
            _characterRestoreAttempted = false;
            _characterRestoreInProgress = false;
            // Existing PlayerPrefs layouts remain the migration fallback.
            var restoredSnapshot = WorkspaceLayoutFile.Load();
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
                return;

            advancedWorkspace = PlayerPrefs.GetInt(WorkspaceModeKey, advancedWorkspace ? 1 : 0) == 1;
            _leftWidth = ReadSize(LeftWidthKey, 220f, 140f, 520f);
            _scenesHeight = ReadSize(ScenesHeightKey, 180f, 100f, 400f);
            _inspectorHeight = ReadSize(RightInspectorHeightKey, 260f, 140f, 520f);
            _rightWidth = ReadSize(RightWidthKey, 260f, 160f, 520f);
            _bottomHeight = ReadSize(BottomHeightKey, 150f, 100f, 400f);
            _tabMode = PlayerPrefs.GetInt(TabModeKey, 0) != 0;
            _bottomVisible = PlayerPrefs.GetInt(BottomVisibleKey, 1) != 0;
            _leftVisible = PlayerPrefs.GetInt(LeftVisibleKey, 1) != 0;
            _rightVisible = PlayerPrefs.GetInt(RightVisibleKey, 1) != 0;
            if (previewRenderTexture != null)
                _previewTexture = previewRenderTexture;
            _previewBoundFromCamera = false;
            _discoveredPreviewCamera = false;
            _previewExplicitlyBound = previewRenderTexture != null;
            _root = _document.rootVisualElement;
            _workspaceRoot = new VisualElement { name = "vcr-workspace-shell" };
            _workspaceRoot.style.flexGrow = 1;
            _root.Add(_workspaceRoot);
            Build();
            // First run and legacy PlayerPrefs migration need an initial JSON snapshot.
            _lastPersistedLayoutSignature = restoredSnapshot ? CaptureLayoutSignature() : null;
            _nextLayoutPersistenceCheck = Time.unscaledTime + 2f;
        }

        private void Update()
        {
            PumpWorkspaceNativeSettings();
            RestoreWorkspaceTrackingPreferences();
            RefreshWorkspaceTrackingPanel();
            RefreshWorkspaceCapabilitiesPanel();
            DrainWorkspaceLog();
            PersistLayoutIfChanged();
            // Resolve the runtime before camera discovery. The runtime may be
            // created after the shell, including on a delayed startup path.
            if (sceneRuntime == null && Time.unscaledTime >= _nextStatusRefresh)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (discoverCameraPreview && sceneRuntime != null)
            {
                var runtimeCamera = sceneRuntime.CameraController != null
                    ? sceneRuntime.CameraController.GetComponent<Camera>() : null;
                if (runtimeCamera != previewCamera)
                {
                    // Never replace an explicitly assigned camera with discovery.
                    if (_discoveredPreviewCamera || previewCamera == null)
                    {
                        previewCamera = runtimeCamera;
                        _discoveredPreviewCamera = runtimeCamera != null;
                        ReleaseOwnedPreview();
                    }
                }
            }
            if (_previewRenderUnsupported && Time.unscaledTime >= _nextPreviewRetryAt)
                _previewRenderUnsupported = false;
            if (!_previewRenderUnsupported) UpdateOwnedPreview();
            if (!_previewExplicitlyBound && _ownedPreviewTexture == null && (_previewBoundFromCamera || _previewTexture == null))
            {
                var cameraTexture = previewCamera != null ? previewCamera.targetTexture : null;
                if (cameraTexture != _previewTexture)
                {
                    _previewBoundFromCamera = cameraTexture != null;
                    SetPreviewTexture(cameraTexture);
                }
            }
            if (_status == null || Time.unscaledTime < _nextStatusRefresh)
                return;
            _nextStatusRefresh = Time.unscaledTime + statusRefreshSeconds;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
            {
                _renderInputsRuntime = null;
                _status.text = "Scene runtime unavailable";
                if (_sourcesStatus != null) _sourcesStatus.text = "Model: unavailable";
                if (_inspectorStatus != null) _inspectorStatus.text = "Scene runtime unavailable";
                if (_inspectorModelPath != null) _inspectorModelPath.text = "Model path: unavailable";
                if (_inspectorCameraStatus != null) _inspectorCameraStatus.text = "Camera: unavailable";
                if (_loadModelButton != null) _loadModelButton.SetEnabled(false);
                if (_reloadCharacterButton != null) _reloadCharacterButton.SetEnabled(false);
                if (_unloadCharacterButton != null) _unloadCharacterButton.SetEnabled(false);
                if (_applySceneViewButton != null) _applySceneViewButton.SetEnabled(false);
                if (_applyRenderButton != null) _applyRenderButton.SetEnabled(false);
                if (_targetFrameRateInput != null) _targetFrameRateInput.SetEnabled(false);
                if (_vsyncInput != null) _vsyncInput.SetEnabled(false);
                if (_trackingStatus != null) _trackingStatus.text = "Runtime unavailable";
                if (_previewStatus != null) _previewStatus.text = _previewTexture == null
                    ? "Preview unavailable: no scene runtime" : "External preview texture connected";
                return;
            }
            TryRestoreWorkspaceCharacter();
            SynchronizeRenderInputs();
            var state = sceneRuntime.Status;
            var path = sceneRuntime.CurrentCharacterPath;
            var modelName = string.IsNullOrWhiteSpace(path)
                ? "<none>" : System.IO.Path.GetFileName(path);
            _status.text = "Scene: " + state.State + " | Model: " + modelName +
                " | Loaded: " + state.HasCharacter;
            if (_sourcesStatus != null) _sourcesStatus.text = "Active model: " + modelName;
            if (_loadModelButton != null) _loadModelButton.SetEnabled(!_characterActionPending && sceneRuntime.State != SceneRuntimeState.Faulted);
            if (_targetFrameRateInput != null) _targetFrameRateInput.SetEnabled(!_characterActionPending && sceneRuntime.State != SceneRuntimeState.Faulted);
            if (_vsyncInput != null) _vsyncInput.SetEnabled(!_characterActionPending && sceneRuntime.State != SceneRuntimeState.Faulted);
            if (_previewStatus != null)
                _previewStatus.text = state.HasCharacter
                    ? "Model loaded: " + modelName + (_previewTexture == null ? " (viewport not connected)" : " (preview connected)")
                    : "No active model" + (_previewTexture == null ? " (viewport not connected)" : " (preview connected)");
            if (_inspectorStatus != null)
                _inspectorStatus.text = "State: " + state.State + " | Loaded: " + state.HasCharacter;
            if (_trackingStatus != null)
                _trackingStatus.text = GetWorkspaceTrackingSummary();
            if (_inspectorModelPath != null)
                _inspectorModelPath.text = "Model path: " + (string.IsNullOrWhiteSpace(path) ? "<none>" : path);
            if (_inspectorCameraStatus != null)
                _inspectorCameraStatus.text = "Camera: " + (sceneRuntime.CameraController == null ? "unavailable" : "connected");
            if (_applySceneViewButton != null) _applySceneViewButton.SetEnabled(sceneRuntime.CameraController != null && !_characterActionPending && sceneRuntime.State != SceneRuntimeState.Faulted);
            if (_applyRenderButton != null) _applyRenderButton.SetEnabled(!_characterActionPending && sceneRuntime.State != SceneRuntimeState.Faulted);
            if (_reloadCharacterButton != null)
                _reloadCharacterButton.SetEnabled(state.HasCharacter && !_characterActionPending);
            if (_unloadCharacterButton != null)
                _unloadCharacterButton.SetEnabled(state.HasCharacter && !_characterActionPending);
            if (!string.IsNullOrWhiteSpace(state.LastError))
                _status.text += " | Error: " + state.LastError;
        }

        /// <summary>Bind an externally owned runtime viewport texture. This shell never creates or disposes the texture.</summary>
        public void BindPreviewTexture(Texture texture)
        {
            _previewExplicitlyBound = texture != null;
            _previewBoundFromCamera = false;
            if (texture != null) _previewRenderUnsupported = false;
            if (_ownedPreviewTexture != null) ReleaseOwnedPreview();
            SetPreviewTexture(texture);
        }

        private void SetPreviewTexture(Texture texture)
        {
            _previewTexture = texture;
            if (_previewImage == null) return;
            _previewImage.image = texture;
            _previewImage.style.display = texture == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _workspaceRoot != null) WorkspaceLayoutFile.Save();
        }

        private void OnApplicationQuit()
        {
            if (_workspaceRoot != null) WorkspaceLayoutFile.Save();
        }

        private void OnDisable()
        {
            UnsubscribeWorkspaceLog();
            ResetWorkspaceCapabilitiesPanel();
            ResetWorkspaceLogPanel();
            _workspaceNativeSettings?.Dispose();
            _workspaceNativeSettings = null;
            if (_nativeSettingsWasVisible)
                UnityEngine.Application.runInBackground = _runInBackgroundBeforeSettings;
            _nativeSettingsWasVisible = false;
            if (_workspaceRoot != null) WorkspaceLayoutFile.Save();
            if (_workspaceRoot != null)
                _workspaceRoot.RemoveFromHierarchy();
            _workspaceRoot = null;
            _root = null;
            _dockHost = null;
            _status = null;
            _sourcesStatus = null;
            _sourcesFeedback = null;
            _modelPathInput = null;
            _loadModelButton = null;
            _inspectorStatus = null;
            _inspectorModelPath = null;
            _inspectorCameraStatus = null;
            _applySceneViewButton = null;
            _applyRenderButton = null;
            _targetFrameRateInput = null;
            _vsyncInput = null;
            _renderInputsRuntime = null;
            _inspectorFeedback = null;
            _trackingStatus = null;
            _reloadCharacterButton = null;
            _unloadCharacterButton = null;
            _actionResult = null;
            _previewStatus = null;
            _leftDock = null;
            _rightDock = null;
            _bottomDock = null;
            _previewImage = null;
            _dragOverlay = null;
            _dockTargetLabel = null;
            _previewBoundFromCamera = false;
            ReleaseOwnedPreview();
        }

        private void UpdateOwnedPreview()
        {
            if (_previewExplicitlyBound || previewCamera == null || _previewImage == null ||
                previewCamera.targetTexture != null)
            {
                ReleaseOwnedPreview();
                return;
            }
            var previewContainer = _previewImage.parent;
            if (previewContainer == null) return;
            var availableWidth = previewContainer.resolvedStyle.width;
            var availableHeight = previewContainer.resolvedStyle.height;
            if (float.IsNaN(availableWidth) || float.IsNaN(availableHeight) ||
                availableWidth <= 0 || availableHeight <= 0) return;
            // Quantize dimensions to avoid reallocating a render target for
            // every subpixel layout adjustment during splitter dragging.
            var width = Mathf.Clamp(Mathf.RoundToInt(availableWidth / 16f) * 16, 256, 1920);
            var height = Mathf.Clamp(Mathf.RoundToInt((availableHeight - 45f) / 16f) * 16, 256, 1080);
            if (width == _previewWidth && height == _previewHeight &&
                _ownedPreviewCamera != null && _ownedPreviewTexture != null)
                return;
            ReleaseOwnedPreview();
            _previewWidth = width;
            _previewHeight = height;
            _ownedPreviewTexture = new RenderTexture(width, height, 16)
            {
                name = "VCR Workspace Preview",
                hideFlags = HideFlags.DontSave
            };
            if (!_ownedPreviewTexture.Create() || !_ownedPreviewTexture.IsCreated())
            {
                Debug.LogWarning("Workspace preview render texture allocation failed.", this);
                ReleaseOwnedPreview();
                _previewRenderUnsupported = true;
                _nextPreviewRetryAt = Time.unscaledTime + 10f;
                if (_previewStatus != null)
                    _previewStatus.text = "Preview allocation failed; retrying in 10 seconds";
                return;
            }
            var cameraObject = new GameObject("VCR Workspace Preview Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            _ownedPreviewCamera = cameraObject.AddComponent<Camera>();
            _ownedPreviewCamera.enabled = false;
            _ownedPreviewCamera.targetTexture = _ownedPreviewTexture;
            SetPreviewTexture(_ownedPreviewTexture);
        }

        private void LateUpdate()
        {
            if (_ownedPreviewCamera == null || previewCamera == null ||
                _ownedPreviewTexture == null || !_ownedPreviewTexture.IsCreated())
                return;
            // A hidden or detached preview should not issue an extra GPU render.
            if (_previewImage == null || _previewImage.panel == null ||
                _previewImage.resolvedStyle.display == DisplayStyle.None)
                return;
            try
            {
                _ownedPreviewCamera.CopyFrom(previewCamera);
                _ownedPreviewCamera.enabled = false;
                _ownedPreviewCamera.targetTexture = _ownedPreviewTexture;
                // The preview is mono and explicitly rendered, not part of the
                // scene camera's normal frame submission.
                _ownedPreviewCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _ownedPreviewCamera.transform.SetPositionAndRotation(
                    previewCamera.transform.position, previewCamera.transform.rotation);
                _ownedPreviewCamera.Render();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Workspace preview render unavailable: " + exception.Message, this);
                ReleaseOwnedPreview();
                _previewRenderUnsupported = true;
                _nextPreviewRetryAt = Time.unscaledTime + 10f;
                if (_previewStatus != null)
                    _previewStatus.text = "Preview render unsupported; runtime controls remain available";
            }
        }

        private void ReleaseOwnedPreview()
        {
            if (_ownedPreviewCamera != null)
                Destroy(_ownedPreviewCamera.gameObject);
            _ownedPreviewCamera = null;
            if (_ownedPreviewTexture != null)
            {
                if (_previewTexture == _ownedPreviewTexture) SetPreviewTexture(null);
                _ownedPreviewTexture.Release();
                Destroy(_ownedPreviewTexture);
            }
            _ownedPreviewTexture = null;
            _previewWidth = 0;
            _previewHeight = 0;
        }

        // P1 scenes generated from P0 do not contain the legacy uGUI controller.
        // Keep the Settings action usable without creating a second UI controller.
        // U1 owns its native settings window independently of the legacy uGUI controller.
        // Render settings are the first migrated category; remaining categories must
        // be migrated before the old controller can be retired.
        private void OpenWorkspaceNativeSettings()
        {
            if (!DesktopNativeSettingsWindow.Supported)
            {
                ShowWorkspaceSettingsFallback();
                return;
            }
            try
            {
                if (_workspaceNativeSettings == null)
                {
                    _workspaceNativeSettings = new DesktopNativeSettingsWindow();
                    _workspaceNativeSettings.Add(0, 1, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Render settings");
                    _workspaceNativeSettings.Add(0, 10, DesktopNativeSettingsWindow.ControlKind.Edit,
                        "Render scale (0.5-2.0)", "1");
                    _workspaceNativeSettings.Add(0, 200, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Apply render scale");
                    _workspaceNativeSettings.Add(0, 11, DesktopNativeSettingsWindow.ControlKind.Edit,
                        "Target FPS (30-120)", "60");
                    _workspaceNativeSettings.Add(0, 201, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Apply FPS");
                    _workspaceNativeSettings.Add(0, 202, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Toggle VSync");
                    _workspaceNativeSettings.Add(0, 203, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Toggle background execution");
                    _workspaceNativeSettings.Add(0, 204, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Restore render defaults");
                    _workspaceNativeSettings.Add(0, 90, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Changes apply to the active scene.");
                    _workspaceNativeSettings.Add(1, 2, DesktopNativeSettingsWindow.ControlKind.Label,
                        "VRM model management");
                    _workspaceNativeSettings.Add(1, 12, DesktopNativeSettingsWindow.ControlKind.Edit,
                        "VRM file path", "");
                    _workspaceNativeSettings.Add(1, 300, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Load VRM from path");
                    _workspaceNativeSettings.Add(1, 302, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Reload active VRM");
                    _workspaceNativeSettings.Add(1, 303, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Unload active VRM");
                    _workspaceNativeSettings.Add(1, 91, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Only existing .vrm files are supported.");
                    _workspaceNativeSettings.Add(3, 3, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Transparent overlay output");
                    _workspaceNativeSettings.Add(3, 401, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Toggle transparent background");
                    _workspaceNativeSettings.Add(3, 402, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Toggle click-through (may disable mouse input)");
                    _workspaceNativeSettings.Add(3, 403, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Recover output");
                    _workspaceNativeSettings.Add(3, 93, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Output controls require a compatible active adapter.");
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SelectCategory(0);
                }
                if (sceneRuntime == null)
                    sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
                if (sceneRuntime != null && sceneRuntime.TryCaptureRenderSettings(out var settings))
                {
                    _workspaceNativeSettings.SetText(10,
                        settings.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    _workspaceNativeSettings.SetText(11, settings.TargetFrameRate.ToString());
                }
                _workspaceNativeSettings.SetText(12,
                    _modelPathInput != null ? _modelPathInput.value ?? "" :
                    PlayerPrefs.GetString("VCR.Character.LastSuccessfulPath.v1", ""));
                RefreshWorkspaceOverlayStatus();
                if (!_workspaceNativeSettings.IsVisible)
                {
                    _runInBackgroundBeforeSettings = UnityEngine.Application.runInBackground;
                    UnityEngine.Application.runInBackground = true;
                }
                _workspaceNativeSettings.Open();
                _nativeSettingsWasVisible = _workspaceNativeSettings.IsVisible;
            }
            catch (Exception exception)
            {
                Debug.LogError("U1 native Settings unavailable: " + exception);
                _workspaceNativeSettings?.Dispose();
                _workspaceNativeSettings = null;
                if (_nativeSettingsWasVisible)
                    UnityEngine.Application.runInBackground = _runInBackgroundBeforeSettings;
                _nativeSettingsWasVisible = false;
                ShowWorkspaceSettingsFallback();
            }
        }

        private void PumpWorkspaceNativeSettings()
        {
            if (_workspaceNativeSettings == null) return;
            _workspaceNativeSettings.PumpMessages();
            if (!_workspaceNativeSettings.IsVisible && _nativeSettingsWasVisible)
            {
                UnityEngine.Application.runInBackground = _runInBackgroundBeforeSettings;
                _nativeSettingsWasVisible = false;
            }
            for (var i = 0; i < 48; ++i)
            {
                var action = _workspaceNativeSettings.PollAction();
                if (action == 0) break;
                // Native navigation emits 100-103; without this, model/output
                // pages exist but cannot be reached from the sidebar.
                if (action >= 100 && action <= 103)
                {
                    _workspaceNativeSettings.SelectCategory(action - 100);
                    if (action == 102) RebuildWorkspaceNativeTrackingSources();
                    if (action == 103) RefreshWorkspaceOverlayStatus();
                    continue;
                }
                // A third-party tracking source can throw while toggling or applying
                // its own configuration. Keep the independent Settings UI responsive.
                var handledTracking = false;
                try
                {
                    handledTracking = HandleWorkspaceNativeTrackingAction(action);
                }
                catch (Exception exception)
                {
                    handledTracking = true;
                    _workspaceNativeSettings.SetText(92,
                        "Tracking action failed: " + exception.Message);
                    Debug.LogException(exception, this);
                }
                if (handledTracking)
                    continue;
                if (action >= 401 && action <= 403)
                {
                    ApplyWorkspaceOverlayAction(action);
                    continue;
                }
                if (action == 300)
                {
                    var path = _workspaceNativeSettings.GetText(12)?.Trim();
                    if (string.IsNullOrEmpty(path) ||
                        !string.Equals(System.IO.Path.GetExtension(path), ".vrm",
                            StringComparison.OrdinalIgnoreCase) ||
                        !System.IO.File.Exists(path))
                    {
                        _workspaceNativeSettings.SetText(91, "Choose an existing .vrm file.");
                        continue;
                    }
                    if (_characterActionPending)
                    {
                        _workspaceNativeSettings.SetText(91, "A model operation is in progress.");
                        continue;
                    }
                    _nativeModelPathOverride = path;
                    if (_modelPathInput != null) _modelPathInput.value = path;
                    LoadModelFromPath();
                    continue;
                }
                if (action == 302)
                {
                    ReloadActiveCharacter();
                    continue;
                }
                if (action == 303)
                {
                    UnloadActiveCharacter();
                    continue;
                }
                if (action != 200 && action != 201 && action != 202 && action != 203 && action != 204) continue;
                if (sceneRuntime == null)
                    sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
                if (sceneRuntime == null || !sceneRuntime.TryCaptureRenderSettings(out var settings))
                {
                    _workspaceNativeSettings.SetText(90, "Render runtime unavailable.");
                    continue;
                }
                if (action == 200)
                {
                    if (!float.TryParse(_workspaceNativeSettings.GetText(10),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var scale) ||
                        float.IsNaN(scale) || float.IsInfinity(scale) ||
                        scale < 0.5f || scale > 2.0f)
                    {
                        _workspaceNativeSettings.SetText(90, "Render scale must be 0.5-2.0.");
                        continue;
                    }
                    settings.RenderScale = scale;
                }
                else if (action == 201)
                {
                    if (!int.TryParse(_workspaceNativeSettings.GetText(11), out var fps) ||
                        fps < 30 || fps > 120)
                    {
                        _workspaceNativeSettings.SetText(90, "FPS must be between 30 and 120.");
                        continue;
                    }
                    settings.TargetFrameRate = fps;
                }
                else if (action == 202)
                {
                    settings.UseVSync = !settings.UseVSync;
                }
                else if (action == 203)
                {
                    settings.RunInBackground = !settings.RunInBackground;
                }
                else
                {
                    settings.RenderScale = 1f;
                    settings.TargetFrameRate = 60;
                    settings.UseVSync = false;
                }
                sceneRuntime.ApplyRenderSettings(settings);
                if (action == 203)
                {
                    // The window temporarily forces background execution while open.
                    // Preserve the user's newly selected value for window close.
                    _runInBackgroundBeforeSettings = settings.RunInBackground;
                    UnityEngine.Application.runInBackground = true;
                }
                if (sceneRuntime.TryCaptureRenderSettings(out var applied))
                {
                    _workspaceNativeSettings.SetText(10,
                        applied.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    _workspaceNativeSettings.SetText(11, applied.TargetFrameRate.ToString());
                    _workspaceNativeSettings.SetText(90,
                        "FPS: " + applied.TargetFrameRate + ", VSync: " +
                        (applied.UseVSync ? "on" : "off") + ", Background: " +
                        (applied.RunInBackground ? "on" : "off"));
                }
            }
        }

        private void RefreshWorkspaceOverlayStatus()
        {
            if (_workspaceNativeSettings == null) return;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
            {
                _workspaceNativeSettings.SetText(93, "Output runtime unavailable.");
                return;
            }
            if (!sceneRuntime.TryCaptureOverlayOutput(out _, out var settings, out var error))
            {
                _workspaceNativeSettings.SetText(93,
                    "Output unavailable: " + (error ?? "unknown error"));
                return;
            }
            _workspaceNativeSettings.SetText(93,
                "Transparent: " + (settings.Transparent ? "on" : "off") +
                ", click-through: " + (settings.ClickThrough ? "on" : "off"));
        }

        private void ApplyWorkspaceOverlayAction(int action)
        {
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
            {
                _workspaceNativeSettings.SetText(93, "Output runtime unavailable.");
                return;
            }
            if (action == 403)
            {
                var recovered = sceneRuntime.TryRecoverOverlayOutput(out var recoverError);
                _workspaceNativeSettings.SetText(93, recovered
                    ? "Output recovery requested."
                    : "Output recovery failed: " + (recoverError ?? "unknown error"));
                return;
            }
            if (!sceneRuntime.TryCaptureOverlayOutput(
                    out _, out var current, out var error))
            {
                _workspaceNativeSettings.SetText(93,
                    "Output unavailable: " + (error ?? "unknown error"));
                return;
            }
            if (!ApplicationUiActionPolicy.CanApplyOverlaySetting(
                    true, sceneRuntime.State, true))
            {
                _workspaceNativeSettings.SetText(93,
                    "Output settings cannot be changed while the scene is busy.");
                return;
            }

            var next = new OverlayOutputSettings(
                action == 401 ? !current.Transparent : current.Transparent,
                false,
                action == 402 ? !current.ClickThrough : current.ClickThrough);
            try
            {
                sceneRuntime.ApplyOverlayOutput(next);
                RefreshWorkspaceOverlayStatus();
                if (action == 402 && next.ClickThrough)
                    _workspaceNativeSettings.SetText(93,
                        "Click-through ON: mouse input passes through the overlay. " +
                        "Use this Settings window to disable it.");
            }
            catch (Exception exception)
            {
                _workspaceNativeSettings.SetText(93,
                    "Output setting failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        private void ShowWorkspaceSettingsFallback()
        {
            if (_workspaceRoot == null) return;
            var existing = _workspaceRoot.Q<VisualElement>("vcr-settings-fallback");
            if (existing != null)
            {
                existing.RemoveFromHierarchy();
                return;
            }

            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();

            var panel = new VisualElement { name = "vcr-settings-fallback" };
            panel.style.position = Position.Absolute;
            panel.style.top = 48;
            panel.style.right = 16;
            panel.style.width = 310;
            panel.style.paddingTop = 12;
            panel.style.paddingBottom = 12;
            panel.style.paddingLeft = 12;
            panel.style.paddingRight = 12;
            panel.style.backgroundColor = new Color(0.17f, 0.19f, 0.22f);
            panel.style.borderTopWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = Color.gray;
            panel.style.borderBottomColor = Color.gray;
            panel.style.borderLeftColor = Color.gray;
            panel.style.borderRightColor = Color.gray;

            var title = new Label("Render settings");
            title.style.color = Color.white;
            panel.Add(title);
            var fps = new IntegerField("Target FPS (30–120)");
            var vsync = new Toggle("VSync");
            var available = false;
            var initialFps = 60;
            var initialVsync = false;
            if (sceneRuntime != null &&
                sceneRuntime.TryCaptureRenderSettings(out var current))
            {
                available = true;
                initialFps = Mathf.Clamp(current.TargetFrameRate, 30, 120);
                initialVsync = current.UseVSync;
            }
            fps.value = initialFps;
            vsync.value = initialVsync;
            panel.Add(fps);
            panel.Add(vsync);
            var feedback = new Label(available ? "" : "Runtime not ready; Apply will retry discovery.");
            feedback.style.color = Color.white;
            panel.Add(feedback);
            var apply = new Button(() =>
            {
                if (sceneRuntime == null)
                    sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
                if (sceneRuntime == null ||
                    !sceneRuntime.TryCaptureRenderSettings(out var settings))
                {
                    feedback.text = "Render settings unavailable.";
                    return;
                }
                try
                {
                    settings.TargetFrameRate = Mathf.Clamp(fps.value, 30, 120);
                    settings.UseVSync = vsync.value;
                    sceneRuntime.ApplyRenderSettings(settings);
                    if (!sceneRuntime.TryCaptureRenderSettings(out var applied))
                        throw new System.InvalidOperationException("Unable to read back render settings.");
                    fps.SetValueWithoutNotify(Mathf.Clamp(applied.TargetFrameRate, 30, 120));
                    vsync.SetValueWithoutNotify(applied.UseVSync);
                    feedback.text = "Applied: " + applied.TargetFrameRate +
                        " FPS, VSync " + (applied.UseVSync ? "on" : "off") + ".";
                    SynchronizeRenderInputs();
                }
                catch (System.Exception exception)
                {
                    feedback.text = "Apply failed: " + exception.Message;
                    Debug.LogException(exception, this);
                }
            }) { text = "Apply" };
            panel.Add(apply);
            panel.Add(new Button(() => panel.RemoveFromHierarchy()) { text = "Close" });
            _workspaceRoot.Add(panel);
        }

        private void Build()
        {
            ReleaseOwnedPreview();
            _workspaceRoot.Clear();
            _workspaceRoot.style.flexGrow = 1;
            _workspaceRoot.style.backgroundColor = new Color(0.105f, 0.12f, 0.14f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingLeft = 12;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;
            header.style.backgroundColor = new Color(0.15f, 0.17f, 0.19f);
            var title = new Label(advancedWorkspace
                ? "VirtualCharacterRender  /  Advanced"
                : "VirtualCharacterRender  /  Live");
            title.style.flexGrow = 1;
            title.style.color = Color.white;
            header.Add(title);

            var toggle = new Button(() =>
            {
                advancedWorkspace = !advancedWorkspace;
                PlayerPrefs.SetInt(WorkspaceModeKey, advancedWorkspace ? 1 : 0);
                PlayerPrefs.Save();
                Build();
            }) { text = advancedWorkspace ? "Live workspace" : "Advanced workspace" };
            header.Add(toggle);
            var settingsButton = new Button(() =>
            {
                if (DesktopNativeSettingsWindow.Supported)
                    OpenWorkspaceNativeSettings();
                else
                    ShowWorkspaceSettingsFallback();
            }) { text = "Settings" };
            settingsButton.tooltip = DesktopNativeSettingsWindow.Supported
                ? "Open native settings or the workspace render settings"
                : "Open workspace render settings";
            header.Add(settingsButton);
            header.Add(new Button(() =>
            {
                _tabMode = !_tabMode;
                PlayerPrefs.SetInt(TabModeKey, _tabMode ? 1 : 0);
                PlayerPrefs.Save();
                Build();
            }) { text = _tabMode ? "Stack panels" : "Tab panels" });
            header.Add(new Button(() =>
            {
                _bottomVisible = !_bottomVisible;
                PlayerPrefs.SetInt(BottomVisibleKey, _bottomVisible ? 1 : 0);
                PlayerPrefs.Save();
                Build();
            }) { text = _bottomVisible ? "Hide bottom" : "Show bottom" });
            header.Add(new Button(() =>
            {
                _leftVisible = !_leftVisible;
                PlayerPrefs.SetInt(LeftVisibleKey, _leftVisible ? 1 : 0);
                PlayerPrefs.Save();
                Build();
            }) { text = _leftVisible ? "Hide left" : "Show left" });
            header.Add(new Button(() =>
            {
                _rightVisible = !_rightVisible;
                PlayerPrefs.SetInt(RightVisibleKey, _rightVisible ? 1 : 0);
                PlayerPrefs.Save();
                Build();
            }) { text = _rightVisible ? "Hide right" : "Show right" });
            header.Add(new Button(() =>
            {
                if (!WorkspaceLayoutFile.Save())
                    Debug.LogWarning("Workspace layout snapshot could not be saved.");
            }) { text = "Save layout" });
            header.Add(new Button(() =>
            {
                if (!WorkspaceLayoutFile.Load()) return;
                advancedWorkspace = PlayerPrefs.GetInt(WorkspaceModeKey, 0) != 0;
                _tabMode = PlayerPrefs.GetInt(TabModeKey, 0) != 0;
                _leftVisible = PlayerPrefs.GetInt(LeftVisibleKey, 1) != 0;
                _rightVisible = PlayerPrefs.GetInt(RightVisibleKey, 1) != 0;
                _bottomVisible = PlayerPrefs.GetInt(BottomVisibleKey, 1) != 0;
                _leftWidth = ReadSize(LeftWidthKey, 220f, 140f, 520f);
                _rightWidth = ReadSize(RightWidthKey, 260f, 160f, 520f);
                _bottomHeight = ReadSize(BottomHeightKey, 150f, 100f, 400f);
                _scenesHeight = ReadSize(ScenesHeightKey, 180f, 100f, 400f);
                _inspectorHeight = ReadSize(RightInspectorHeightKey, 260f, 140f, 520f);
                Build();
            }) { text = "Restore layout" });
            header.Add(new Button(ResetLayout) { text = "Reset layout" });
            _workspaceRoot.Add(header);

            var bottom = new VisualElement { name = "vcr-bottom" };
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.height = _bottomHeight;
            bottom.style.display = _bottomVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _bottomDock = bottom;

            _dockHost = new VisualElement();
            _dockHost.style.flexGrow = 1;
            _dockHost.style.flexDirection = FlexDirection.Row;
            _workspaceRoot.Add(_dockHost);

            var left = Column(_leftWidth);
            var center = Column(0);
            center.style.flexGrow = 1;
            var right = Column(_rightWidth);
            _leftDock = left;
            _rightDock = right;
            left.style.display = _leftVisible ? DisplayStyle.Flex : DisplayStyle.None;
            right.style.display = _rightVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _dockHost.Add(left);
            var leftSplitter = Splitter(true, _leftWidth, 140f, 520f, value =>
            {
                _leftWidth = value;
                left.style.width = value;
            }, LeftWidthKey);
            leftSplitter.style.display = _leftVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _dockHost.Add(leftSplitter);
            _dockHost.Add(center);
            var rightSplitter = Splitter(true, _rightWidth, 160f, 520f, value =>
            {
                _rightWidth = value;
                right.style.width = value;
            }, RightWidthKey, true);
            rightSplitter.style.display = _rightVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _dockHost.Add(rightSplitter);
            _dockHost.Add(right);

            var scenes = Panel(WorkspacePanelCatalog.Scenes, "Scenes", 100);
            scenes.style.height = _scenesHeight;
            scenes.style.flexGrow = 0;
            PlacePanel(scenes, WorkspacePanelCatalog.Scenes, "left");
            var scenesSplitter = Splitter(false, _scenesHeight, 100f, 400f, value =>
            {
                _scenesHeight = value;
                scenes.style.height = value;
            }, ScenesHeightKey);
            scenesSplitter.name = "vcr-splitter-scenes";
            scenesSplitter.style.display = !_tabMode && scenes.parent == left ? DisplayStyle.Flex : DisplayStyle.None;
            left.Add(scenesSplitter);
            var sources = Panel(WorkspacePanelCatalog.Sources, "Sources / Models", 220);
            _sourcesStatus = new Label("Waiting for model state...");
            sources.Add(_sourcesStatus);
            _modelPathInput = new TextField("VRM file path");
            _modelPathInput.value = PlayerPrefs.GetString("VCR.Character.LastSuccessfulPath.v1", "");
            _modelPathInput.tooltip = "Absolute path to a local .vrm file. FBX runtime import is not available.";
            sources.Add(_modelPathInput);
            _loadModelButton = new Button(LoadModelFromPath) { text = "Load VRM" };
            sources.Add(_loadModelButton);
            _sourcesFeedback = new Label("");
            sources.Add(_sourcesFeedback);
            PlacePanel(sources, WorkspacePanelCatalog.Sources, "left");
            var preview = Panel(WorkspacePanelCatalog.Preview,
                "Preview — runtime viewport integration pending", 360);
            _previewStatus = new Label("Waiting for model state...");
            preview.Add(_previewStatus);
            _previewImage = new Image();
            _previewImage.scaleMode = ScaleMode.ScaleToFit;
            _previewImage.style.flexGrow = 1;
            _previewImage.style.display = _previewTexture == null ? DisplayStyle.None : DisplayStyle.Flex;
            _previewImage.image = _previewTexture;
            preview.Add(_previewImage);
            center.Add(preview);
            var inspector = Panel(WorkspacePanelCatalog.Inspector, "Inspector", 260);
            _inspectorStatus = new Label("Waiting for scene state...");
            inspector.Add(_inspectorStatus);
            _inspectorModelPath = new Label("Model path: none");
            inspector.Add(_inspectorModelPath);
            _inspectorCameraStatus = new Label("Camera: unavailable");
            inspector.Add(_inspectorCameraStatus);
            _applySceneViewButton = new Button(ApplySceneViewFromInspector) { text = "Apply scene camera settings" };
            _applyRenderButton = new Button(ApplyRenderFromInspector) { text = "Apply current render settings" };
            _targetFrameRateInput = new IntegerField("Target FPS (30–120)");
            _targetFrameRateInput.tooltip = "Supported target: 30–120 FPS. Default: 60 FPS.";
            _vsyncInput = new Toggle("VSync");
            _targetFrameRateInput.RegisterCallback<FocusOutEvent>(_ =>
            {
                _targetFrameRateInput.SetValueWithoutNotify(Mathf.Clamp(_targetFrameRateInput.value, 30, 120));
            });
            _targetFrameRateInput.SetValueWithoutNotify(60);
            inspector.Add(_targetFrameRateInput);
            inspector.Add(_vsyncInput);
            _renderInputsRuntime = null;
            SynchronizeRenderInputs();
            inspector.Add(_applySceneViewButton);
            inspector.Add(_applyRenderButton);
            _inspectorFeedback = new Label("");
            inspector.Add(_inspectorFeedback);
            if (advancedWorkspace)
            {
                inspector.style.height = _inspectorHeight;
                inspector.style.flexGrow = 0;
            }
            PlacePanel(inspector, WorkspacePanelCatalog.Inspector, "right");
            if (advancedWorkspace)
            {
                var inspectorSplitter = Splitter(false, _inspectorHeight, 140f, 520f, value =>
                {
                    _inspectorHeight = value;
                    inspector.style.height = value;
                }, RightInspectorHeightKey);
                inspectorSplitter.name = "vcr-splitter-inspector";
                inspectorSplitter.style.display = !_tabMode && inspector.parent == right
                    ? DisplayStyle.Flex : DisplayStyle.None;
                right.Add(inspectorSplitter);
                PlacePanel(Panel(WorkspacePanelCatalog.Materials, "Materials / Shaders", 140), WorkspacePanelCatalog.Materials, "right");
                var modules = Panel(WorkspacePanelCatalog.Capabilities, "Modules", 120);
                BuildWorkspaceCapabilitiesPanel(modules);
                PlacePanel(modules, WorkspacePanelCatalog.Capabilities, "right");
            }

            var bottomSplitter = Splitter(false, _bottomHeight, 100f, 400f, value =>
            {
                _bottomHeight = value;
                var target = _workspaceRoot.Q<VisualElement>("vcr-bottom");
                if (target != null) target.style.height = value;
            }, BottomHeightKey, true);
            bottomSplitter.style.display = _bottomVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _workspaceRoot.Add(bottomSplitter);
            var tracking = Panel(WorkspacePanelCatalog.Tracking, "Tracking", 150);
            _trackingStatus = new Label("Waiting for runtime...");
            tracking.Add(_trackingStatus);
            BuildWorkspaceTrackingPanel(tracking);
            PlacePanel(tracking, WorkspacePanelCatalog.Tracking, "bottom");
            var actions = Panel(WorkspacePanelCatalog.Actions, "Actions", 150);
            _reloadCharacterButton = new Button(ReloadActiveCharacter) { text = "Reload active VRM" };
            _unloadCharacterButton = new Button(UnloadActiveCharacter) { text = "Unload active VRM" };
            _reloadCharacterButton.SetEnabled(false);
            _unloadCharacterButton.SetEnabled(false);
            actions.Add(_reloadCharacterButton);
            actions.Add(_unloadCharacterButton);
            _actionResult = new Label("Actions operate on the current scene runtime.");
            actions.Add(_actionResult);
            PlacePanel(actions, WorkspacePanelCatalog.Actions, "bottom");
            var events = Panel(WorkspacePanelCatalog.Events, "Log / Events", 150);
            BuildWorkspaceLogPanel(events);
            PlacePanel(events, WorkspacePanelCatalog.Events, "bottom");
            _workspaceRoot.Add(bottom);
            SortDockPanels(left);
            SortDockPanels(right);
            SortDockPanels(bottom);
            PositionPanelSplitter(left, WorkspacePanelCatalog.Scenes, "vcr-splitter-scenes", _tabMode);
            PositionPanelSplitter(right, WorkspacePanelCatalog.Inspector, "vcr-splitter-inspector", _tabMode);
            if (_tabMode)
            {
                ApplyDockTabs(left, "left");
                ApplyDockTabs(right, "right");
                ApplyDockTabs(bottom, "bottom");
            }

            _status = new Label("Waiting for scene runtime status...");
            _status.style.color = new Color(0.75f, 0.8f, 0.85f);
            _status.style.paddingLeft = 12;
            _status.style.paddingTop = 5;
            _status.style.paddingBottom = 5;
            _workspaceRoot.Add(_status);
            _dragOverlay = new Label("Drop onto left, right or bottom dock");
            _dragOverlay.style.position = Position.Absolute;
            _dragOverlay.style.left = 12;
            _dragOverlay.style.top = 50;
            _dragOverlay.style.paddingLeft = 12;
            _dragOverlay.style.paddingRight = 12;
            _dragOverlay.style.paddingTop = 8;
            _dragOverlay.style.paddingBottom = 8;
            _dragOverlay.style.backgroundColor = new Color(0.12f, 0.35f, 0.48f);
            _dragOverlay.style.color = Color.white;
            _dragOverlay.style.display = DisplayStyle.None;
            _dragOverlay.pickingMode = PickingMode.Ignore;
            _workspaceRoot.Add(_dragOverlay);
            _dockTargetLabel = new Label();
            _dockTargetLabel.style.position = Position.Absolute;
            _dockTargetLabel.style.right = 12;
            _dockTargetLabel.style.top = 50;
            _dockTargetLabel.style.backgroundColor = new Color(0.15f, 0.38f, 0.50f);
            _dockTargetLabel.style.color = Color.white;
            _dockTargetLabel.style.paddingLeft = 12;
            _dockTargetLabel.style.paddingRight = 12;
            _dockTargetLabel.style.paddingTop = 8;
            _dockTargetLabel.style.paddingBottom = 8;
            _dockTargetLabel.style.display = DisplayStyle.None;
            _dockTargetLabel.pickingMode = PickingMode.Ignore;
            _workspaceRoot.Add(_dockTargetLabel);
        }

        private static void SortDockPanels(VisualElement dock)
        {
            var panels = new System.Collections.Generic.List<VisualElement>();
            foreach (var child in dock.Children())
                if (child.name != null && child.name.StartsWith("vcr-panel-"))
                    panels.Add(child);
            var creationOrder = new System.Collections.Generic.Dictionary<VisualElement, int>();
            for (var i = 0; i < panels.Count; i++) creationOrder[panels[i]] = i;
            panels.Sort((a, b) =>
            {
                var aId = a.name.Substring("vcr-panel-".Length);
                var bId = b.name.Substring("vcr-panel-".Length);
                var aOrder = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + aId + ".v1", int.MaxValue);
                var bOrder = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + bId + ".v1", int.MaxValue);
                var comparison = aOrder.CompareTo(bOrder);
                return comparison != 0 ? comparison : creationOrder[a].CompareTo(creationOrder[b]);
            });
            foreach (var panel in panels)
            {
                dock.Remove(panel);
                dock.Add(panel);
            }
        }

        private static void PositionPanelSplitter(
            VisualElement dock, string panelId, string splitterName, bool tabMode)
        {
            var splitter = dock.Q<VisualElement>(splitterName);
            if (splitter == null) return;
            var panel = dock.Q<VisualElement>("vcr-panel-" + panelId);
            var visible = !tabMode && panel != null && panel.parent == dock &&
                          dock.childCount > 2;
            splitter.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            // The sorted panels must stay adjacent to their resize handle.
            // Inserting the handle after its panel avoids a detached splitter
            // at the top of a dock when saved panel order is restored.
            dock.Remove(splitter);
            dock.Insert(dock.IndexOf(panel) + 1, splitter);
        }

        private void ApplyDockTabs(VisualElement dock, string dockId)
        {
            var panels = new System.Collections.Generic.List<VisualElement>();
            foreach (var child in dock.Children())
                if (child.name != null && child.name.StartsWith("vcr-panel-"))
                    panels.Add(child);
            if (panels.Count < 2) return;
            // Tabbed panels share the same dock; drag onto a tab to reorder.
            var key = "VCR.UI.Workspace.ActiveTab." + dockId + ".v1";
            var selectedId = PlayerPrefs.GetString(key, panels[0].name);
            var active = panels.FindIndex(p => p.name == selectedId);
            if (active < 0) active = 0;
            foreach (var panel in panels)
            {
                panel.style.height = StyleKeyword.Auto;
                panel.style.flexGrow = 1;
            }
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexShrink = 0;
            for (var i = 0; i < panels.Count; i++)
            {
                var index = i;
                var panel = panels[i];
                panel.style.display = i == active ? DisplayStyle.Flex : DisplayStyle.None;
                var name = panel.name.Substring("vcr-panel-".Length);
                var suppressClick = false;
                var tab = new Button(() =>
                {
                    if (suppressClick) { suppressClick = false; return; }
                    for (var j = 0; j < panels.Count; j++)
                        panels[j].style.display = j == index ? DisplayStyle.Flex : DisplayStyle.None;
                    PlayerPrefs.SetString(key, panel.name);
                    PlayerPrefs.Save();
                }) { text = name };
                tab.name = "vcr-tab-" + name;
                tab.tooltip = "Click to activate. Drag to reorder or move to another dock.";
                tab.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (!evt.ctrlKey || (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow)) return;
                    MovePanelOrder(panel, name, evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
                    evt.StopPropagation();
                });
                // Tab drag works even when its panel body is hidden.
                var draggingTab = false;
                var tabPointer = -1;
                var tabStart = Vector2.zero;
                var tabMoved = false;
                tab.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Escape || !draggingTab) return;
                    draggingTab = false;
                    suppressClick = true;
                    if (tab.HasPointerCapture(tabPointer)) tab.ReleasePointer(tabPointer);
                    tabPointer = -1;
                    evt.StopPropagation();
                });
                tab.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    draggingTab = true;
                    tabPointer = evt.pointerId;
                    tabStart = evt.position;
                    tabMoved = false;
                    suppressClick = false;
                    tab.CapturePointer(tabPointer);
                });
                tab.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!draggingTab || evt.pointerId != tabPointer) return;
                    if (((Vector2)evt.position - tabStart).sqrMagnitude > 64f)
                        tabMoved = true;
                });
                tab.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (!draggingTab || evt.pointerId != tabPointer) return;
                    draggingTab = false;
                    var drop = evt.position;
                    var moved = tabMoved;
                    suppressClick = moved;
                    if (tab.HasPointerCapture(evt.pointerId))
                        tab.ReleasePointer(evt.pointerId);
                    if (!moved) return;
                    var destination = DockAt(drop);
                    if (destination != null && destination != dockId)
                    {
                        MovePanelToDock(panel, name, destination);
                    }
                    else if (destination == dockId)
                    {
                        var tabStrip = dock.Q<VisualElement>("vcr-dock-tabs-" + dockId);
                        if (tabStrip != null)
                        {
                            foreach (var candidate in tabStrip.Children())
                            {
                                if (candidate == tab || !candidate.worldBound.Contains(drop)) continue;
                                var other = dock.Q<VisualElement>("vcr-panel-" +
                                    candidate.name.Substring("vcr-tab-".Length));
                                if (other == null) break;
                                var oldIndex = dock.IndexOf(panel);
                                var otherIndex = dock.IndexOf(other);
                                if (oldIndex == otherIndex) break;
                                dock.Remove(panel);
                                dock.Insert(dock.IndexOf(other), panel);
                                SaveDockOrder(dock);
                                Build();
                                break;
                            }
                        }
                    }
                    evt.StopPropagation();
                });
                tab.RegisterCallback<PointerCaptureOutEvent>(_ =>
                {
                    draggingTab = false;
                    tabPointer = -1;
                });
                tabs.Add(tab);
            }
            tabs.name = "vcr-dock-tabs-" + dockId;
            dock.Insert(0, tabs);
        }

        // Move controls provide deterministic, persistent dock relocation.
        // Preview stays anchored to the central viewport region.
        private void PlacePanel(VisualElement panel, string id, string defaultDock)
        {
            var key = "VCR.UI.Workspace.Dock." + id + ".v1";
            var dock = PlayerPrefs.GetString(key, defaultDock);
            if (dock != "left" && dock != "right" && dock != "bottom")
                dock = defaultDock;
            var target = dock == "left" ? _leftDock :
                dock == "right" ? _rightDock : _bottomDock;
            if (target == null)
            {
                dock = defaultDock;
                target = dock == "left" ? _leftDock :
                    dock == "right" ? _rightDock : _bottomDock;
            }
            if (target == null) return;
            var currentDock = dock;
            var move = new Button(() =>
            {
                var next = currentDock == "left" ? "right" :
                    currentDock == "right" ? "bottom" : "left";
                MovePanelToDock(panel, id, next);
            }) { text = "Move: " + currentDock };
            panel.Add(move);
            var dragHandle = new Label("Drag panel to left / right / bottom");
            dragHandle.style.paddingTop = 5;
            dragHandle.style.paddingBottom = 5;
            dragHandle.style.color = new Color(0.68f, 0.77f, 0.85f);
            panel.Add(dragHandle);
            var dragging = false;
            var activePointerId = -1;
            dragHandle.focusable = true;
            dragHandle.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape || !dragging) return;
                dragging = false;
                if (dragHandle.HasPointerCapture(activePointerId)) dragHandle.ReleasePointer(activePointerId);
                activePointerId = -1;
                if (_dragOverlay != null) _dragOverlay.style.display = DisplayStyle.None;
                if (_dockTargetLabel != null) _dockTargetLabel.style.display = DisplayStyle.None;
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || dragging) return;
                dragging = true;
                activePointerId = evt.pointerId;
                dragHandle.CapturePointer(activePointerId);
                dragHandle.Focus();
                if (_dragOverlay != null) _dragOverlay.style.display = DisplayStyle.Flex;
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || evt.pointerId != activePointerId) return;
                if (_dockTargetLabel != null)
                {
                    var destination = DockAt(evt.position);
                    var sibling = destination == currentDock ? PanelAt(evt.position, target, panel) : null;
                    _dockTargetLabel.text = destination == null ? "No dock target" :
                        sibling == null ? "Drop: " + destination :
                        "Reorder before: " + sibling.name.Substring("vcr-panel-".Length);
                    _dockTargetLabel.style.display = DisplayStyle.Flex;
                }
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging || evt.pointerId != activePointerId) return;
                dragging = false;
                activePointerId = -1;
                if (_dragOverlay != null) _dragOverlay.style.display = DisplayStyle.None;
                if (_dockTargetLabel != null) _dockTargetLabel.style.display = DisplayStyle.None;
                var position = evt.position;
                if (dragHandle.HasPointerCapture(evt.pointerId))
                    dragHandle.ReleasePointer(evt.pointerId);
                var destination = DockAt(position);
                if (destination == currentDock)
                {
                    var sibling = PanelAt(position, target, panel);
                    if (sibling != null)
                    {
                        target.Remove(panel);
                        target.Insert(target.IndexOf(sibling), panel);
                        SaveDockOrder(target);
                        Build();
                    }
                }
                else if (destination != null)
                {
                    MovePanelToDock(panel, id, destination);
                }
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_dragOverlay != null) _dragOverlay.style.display = DisplayStyle.None;
                if (_dockTargetLabel != null) _dockTargetLabel.style.display = DisplayStyle.None;
                dragging = false;
                activePointerId = -1;
            });
            target.Add(panel);
            // The final SortDockPanels pass restores the persisted order.
            panel.Add(new Button(() => MovePanelOrder(panel, id, -1)) { text = "Up / Earlier" });
            panel.Add(new Button(() => MovePanelOrder(panel, id, 1)) { text = "Down / Later" });
        }

        private void MovePanelToDock(VisualElement panel, string id, string destination)
        {
            if (destination != "left" && destination != "right" && destination != "bottom") return;
            var previous = panel.parent;
            PlayerPrefs.SetString("VCR.UI.Workspace.Dock." + id + ".v1", destination);
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab." + destination + ".v1", panel.name);
            if (destination == "left") { _leftVisible = true; PlayerPrefs.SetInt(LeftVisibleKey, 1); }
            if (destination == "right") { _rightVisible = true; PlayerPrefs.SetInt(RightVisibleKey, 1); }
            if (destination == "bottom") { _bottomVisible = true; PlayerPrefs.SetInt(BottomVisibleKey, 1); }
            if (previous != null)
            {
                previous.Remove(panel);
                SaveDockOrder(previous);
            }
            // Persist a deterministic position after existing destination panels.
            var destinationDock = destination == "left" ? _leftDock :
                destination == "right" ? _rightDock : _bottomDock;
            var destinationCount = 0;
            if (destinationDock != null)
                foreach (var child in destinationDock.Children())
                    if (child.name != null && child.name.StartsWith("vcr-panel-")) destinationCount++;
            var lastOrder = -1;
            if (destinationDock != null)
                foreach (var child in destinationDock.Children())
                    if (child.name != null && child.name.StartsWith("vcr-panel-"))
                        lastOrder = Mathf.Max(lastOrder, PlayerPrefs.GetInt(
                            "VCR.UI.Workspace.Order." + child.name.Substring("vcr-panel-".Length) + ".v1",
                            destinationCount));
            PlayerPrefs.SetInt("VCR.UI.Workspace.Order." + id + ".v1", lastOrder + 1);
            PlayerPrefs.Save();
            Build();
        }

        private static VisualElement PanelAt(Vector2 position, VisualElement dock, VisualElement dragged)
        {
            if (dock == null) return null;
            foreach (var child in dock.Children())
            {
                if (child == dragged || child.name == null ||
                    !child.name.StartsWith("vcr-panel-")) continue;
                if (child.worldBound.Contains(position)) return child;
            }
            return null;
        }

        private static void SaveDockOrder(VisualElement dock)
        {
            var order = 0;
            foreach (var child in dock.Children())
            {
                if (child.name == null || !child.name.StartsWith("vcr-panel-")) continue;
                PlayerPrefs.SetInt("VCR.UI.Workspace.Order." +
                    child.name.Substring("vcr-panel-".Length) + ".v1", order++);
            }
            PlayerPrefs.Save();
        }

        private string DockAt(Vector2 position)
        {
            if (_leftDock != null && _leftDock.worldBound.Contains(position)) return "left";
            if (_rightDock != null && _rightDock.worldBound.Contains(position)) return "right";
            if (_bottomDock != null && _bottomDock.worldBound.Contains(position)) return "bottom";
            return null;
        }

        private void MovePanelOrder(VisualElement panel, string id, int direction)
        {
            var parent = panel.parent;
            if (parent == null) return;
            var panels = new System.Collections.Generic.List<VisualElement>();
            foreach (var child in parent.Children())
                if (child.name != null && child.name.StartsWith("vcr-panel-")) panels.Add(child);
            var index = panels.IndexOf(panel);
            if (index < 0) return;
            var next = Mathf.Clamp(index + direction, 0, panels.Count - 1);
            if (index == next) return;
            var neighbor = panels[next];
            parent.Remove(panel);
            var insertAt = parent.IndexOf(neighbor);
            if (direction > 0) insertAt++;
            parent.Insert(Mathf.Clamp(insertAt, 0, parent.childCount), panel);
            SaveDockOrder(parent);
            if (_tabMode) Build();
        }

        private static float ReadSize(string key, float fallback, float min, float max)
        {
            var value = PlayerPrefs.GetFloat(key, fallback);
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback : Mathf.Clamp(value, min, max);
        }

        private void ApplySceneViewFromInspector()
        {
            if (sceneRuntime == null || _characterActionPending) return;
            try
            {
                sceneRuntime.ApplySceneViewSettings();
                if (_inspectorFeedback != null) _inspectorFeedback.text = "Scene camera settings applied.";
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
                if (_inspectorFeedback != null) _inspectorFeedback.text = "Camera settings failed: " + ex.Message;
            }
            _nextStatusRefresh = Time.unscaledTime + statusRefreshSeconds;
        }

        private void SynchronizeRenderInputs()
        {
            if (sceneRuntime == null || _renderInputsRuntime == sceneRuntime ||
                _targetFrameRateInput == null || _vsyncInput == null)
                return;
            if (!sceneRuntime.TryCaptureRenderSettings(out var currentRender))
                return;
            _targetFrameRateInput.SetValueWithoutNotify(Mathf.Clamp(currentRender.TargetFrameRate, 30, 120));
            _vsyncInput.SetValueWithoutNotify(currentRender.UseVSync);
            _renderInputsRuntime = sceneRuntime;
        }

        private void ApplyRenderFromInspector()
        {
            if (sceneRuntime == null || _characterActionPending) return;
            SynchronizeRenderInputs();
            if (_renderInputsRuntime != sceneRuntime)
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Render settings unavailable: runtime not initialized.";
                return;
            }
            try
            {
                if (!sceneRuntime.TryCaptureRenderSettings(out var settings))
                    throw new System.InvalidOperationException("Render settings are unavailable.");
                settings.TargetFrameRate = Mathf.Clamp(_targetFrameRateInput.value, 30, 120);
                settings.UseVSync = _vsyncInput.value;
                sceneRuntime.ApplyRenderSettings(settings);
                if (!sceneRuntime.TryCaptureRenderSettings(out var appliedSettings))
                    throw new System.InvalidOperationException("Unable to read back applied render settings.");
                if (_targetFrameRateInput != null)
                    _targetFrameRateInput.SetValueWithoutNotify(Mathf.Clamp(appliedSettings.TargetFrameRate, 30, 120));
                if (_vsyncInput != null)
                    _vsyncInput.SetValueWithoutNotify(appliedSettings.UseVSync);
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Render settings applied: " +
                        appliedSettings.TargetFrameRate + " FPS, VSync " +
                        (appliedSettings.UseVSync ? "on" : "off") + ".";
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
                if (_inspectorFeedback != null) _inspectorFeedback.text = "Render settings failed: " + ex.Message;
            }
            _nextStatusRefresh = Time.unscaledTime + statusRefreshSeconds;
        }

        // The new U1 scene does not include the old uGUI controller that
        // previously restored the last successfully loaded character.
        private void TryRestoreWorkspaceCharacter()
        {
            if (_characterRestoreAttempted || _characterActionPending || sceneRuntime == null)
                return;
            if (sceneRuntime.Status.HasCharacter)
            {
                _characterRestoreAttempted = true;
                return;
            }
            var path = PlayerPrefs.GetString(LastSuccessfulCharacterPathKey, "");
            if (string.IsNullOrWhiteSpace(path))
            {
                _characterRestoreAttempted = true;
                return;
            }
            if (!string.Equals(System.IO.Path.GetExtension(path), ".vrm",
                    StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            {
                _characterRestoreAttempted = true;
                if (_sourcesFeedback != null)
                    _sourcesFeedback.text = "Previous VRM unavailable. Select an existing .vrm file.";
                Debug.LogWarning("VCR inventory: stage=restore category=model status=missing filename=" +
                    System.IO.Path.GetFileName(path), this);
                return;
            }
            if (!ApplicationUiActionPolicy.CanLoadCharacter(true, sceneRuntime.State, path))
                return;

            _characterRestoreAttempted = true;
            _characterRestoreInProgress = true;
            _nativeModelPathOverride = path;
            if (_modelPathInput != null) _modelPathInput.SetValueWithoutNotify(path);
            Debug.Log("VCR inventory: stage=restore category=model status=loading filename=" +
                System.IO.Path.GetFileName(path), this);
            LoadModelFromPath();
        }

        private async void LoadModelFromPath()
        {
            if (_characterActionPending) return;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
            {
                if (_sourcesFeedback != null) _sourcesFeedback.text = "Scene runtime unavailable.";
                return;
            }
            var path = !string.IsNullOrWhiteSpace(_nativeModelPathOverride)
                ? _nativeModelPathOverride
                : _modelPathInput?.value?.Trim();
            _nativeModelPathOverride = null;
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(System.IO.Path.GetExtension(path), ".vrm",
                    System.StringComparison.OrdinalIgnoreCase) ||
                !System.IO.File.Exists(path))
            {
                if (_sourcesFeedback != null) _sourcesFeedback.text = "Select an existing .vrm file path.";
                return;
            }
            var restoringPreviousCharacter = _characterRestoreInProgress;
            _characterActionPending = true;
            if (_loadModelButton != null) _loadModelButton.SetEnabled(false);
            if (_reloadCharacterButton != null) _reloadCharacterButton.SetEnabled(false);
            if (_unloadCharacterButton != null) _unloadCharacterButton.SetEnabled(false);
            try
            {
                var loaded = await sceneRuntime.LoadCharacterAsync(path);
                if (loaded == null)
                {
                    if (_sourcesFeedback != null) _sourcesFeedback.text = "VRM load returned no model.";
                    if (restoringPreviousCharacter)
                        Debug.LogWarning("VCR inventory: stage=restore category=model status=not_loaded filename=" +
                            System.IO.Path.GetFileName(path), this);
                    return;
                }
                if (restoringPreviousCharacter)
                    Debug.Log("VCR inventory: stage=restore category=model status=restored filename=" +
                        System.IO.Path.GetFileName(path), this);
                PlayerPrefs.SetString(LastSuccessfulCharacterPathKey, path);
                PlayerPrefs.Save();
                if (_sourcesFeedback != null) _sourcesFeedback.text = "Loaded: " + System.IO.Path.GetFileName(path);
            }
            catch (System.OperationCanceledException)
            {
                if (_sourcesFeedback != null) _sourcesFeedback.text = "VRM load canceled.";
                if (restoringPreviousCharacter)
                    Debug.LogWarning("VCR inventory: stage=restore category=model status=canceled filename=" +
                        System.IO.Path.GetFileName(path), this);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
                if (_sourcesFeedback != null) _sourcesFeedback.text = "VRM load failed: " + ex.Message;
                if (restoringPreviousCharacter)
                    Debug.LogWarning("VCR inventory: stage=restore category=model status=failed filename=" +
                        System.IO.Path.GetFileName(path) + " exception=" + ex.GetType().Name, this);
            }
            finally
            {
                _characterRestoreInProgress = false;
                _characterActionPending = false;
                _nextStatusRefresh = Time.unscaledTime + statusRefreshSeconds;
            }
        }

        private async void ReloadActiveCharacter()
        {
            if (_characterActionPending || sceneRuntime == null || !sceneRuntime.Status.HasCharacter) return;
            _characterActionPending = true;
            if (_reloadCharacterButton != null) _reloadCharacterButton.SetEnabled(false);
            if (_unloadCharacterButton != null) _unloadCharacterButton.SetEnabled(false);
            try
            {
                var loaded = await sceneRuntime.ReloadCharacterAsync();
                if (_actionResult != null)
                    _actionResult.text = loaded != null ? "Active VRM reloaded." : "VRM reload returned no model.";
            }
            catch (System.OperationCanceledException)
            {
                if (_actionResult != null) _actionResult.text = "VRM reload canceled.";
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
                if (_actionResult != null) _actionResult.text = "VRM reload failed: " + ex.Message;
            }
            finally
            {
                _characterActionPending = false;
                _nextStatusRefresh = 0f;
            }
        }

        private void UnloadActiveCharacter()
        {
            if (_characterActionPending || sceneRuntime == null || !sceneRuntime.Status.HasCharacter) return;
            try
            {
                sceneRuntime.UnloadCharacter();
                // Unloading intentionally clears the next-start restoration target.
                // Do not restore a model that the user explicitly removed.
                PlayerPrefs.DeleteKey(LastSuccessfulCharacterPathKey);
                PlayerPrefs.Save();
                _characterRestoreAttempted = true;
                _nativeModelPathOverride = null;
                if (_modelPathInput != null) _modelPathInput.SetValueWithoutNotify("");
                if (_workspaceNativeSettings != null)
                    _workspaceNativeSettings.SetText(12, "");
                if (_actionResult != null) _actionResult.text = "Active VRM unloaded; auto-restore cleared.";
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
                if (_actionResult != null) _actionResult.text = "VRM unload failed: " + ex.Message;
            }
            _nextStatusRefresh = 0f;
        }

        private void ResetLayout()
        {
            PlayerPrefs.DeleteKey(LeftWidthKey);
            PlayerPrefs.DeleteKey(ScenesHeightKey);
            PlayerPrefs.DeleteKey(RightInspectorHeightKey);
            PlayerPrefs.DeleteKey(RightWidthKey);
            PlayerPrefs.DeleteKey(BottomHeightKey);
            PlayerPrefs.DeleteKey(BottomVisibleKey);
            PlayerPrefs.DeleteKey(TabModeKey);
            PlayerPrefs.DeleteKey("VCR.UI.Workspace.ActiveTab.left.v1");
            PlayerPrefs.DeleteKey("VCR.UI.Workspace.ActiveTab.right.v1");
            PlayerPrefs.DeleteKey("VCR.UI.Workspace.ActiveTab.bottom.v1");
            foreach (var id in WorkspacePanelCatalog.GetDefaultPanelIds(true))
            {
                PlayerPrefs.DeleteKey("VCR.UI.Workspace.Dock." + id + ".v1");
                PlayerPrefs.DeleteKey("VCR.UI.Workspace.Order." + id + ".v1");
            }
            PlayerPrefs.DeleteKey(LeftVisibleKey);
            PlayerPrefs.DeleteKey(RightVisibleKey);
            PlayerPrefs.Save();
            _leftWidth = 220f;
            _scenesHeight = 180f;
            _inspectorHeight = 260f;
            _rightWidth = 260f;
            _bottomHeight = 150f;
            _bottomVisible = true;
            _tabMode = false;
            _leftVisible = true;
            _rightVisible = true;
            WorkspaceLayoutFile.ClearSnapshots();
            // Replace the previous snapshot immediately: reset must survive a crash
            // or forced shutdown without restoring the old panel arrangement.
            if (WorkspaceLayoutFile.Save())
                _lastPersistedLayoutSignature = CaptureLayoutSignature();
            else
                _lastPersistedLayoutSignature = null;
            Build();
        }

        // Splitters use pointer capture, so releasing outside the handle still
        // ends a drag. Only a completed drag writes the persisted dimension.
        private static VisualElement Splitter(
            bool horizontal, float initial, float min, float max,
            System.Action<float> apply, string key, bool reverse = false)
        {
            var handle = new VisualElement();
            handle.style.flexShrink = 0;
            handle.style.backgroundColor = new Color(0.27f, 0.31f, 0.35f);
            if (horizontal)
                handle.style.width = 6;
            else
                handle.style.height = 6;

            var dragging = false;
            var activePointerId = -1;
            var startPosition = 0f;
            var startValue = initial;
            var currentValue = initial;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || dragging) return;
                dragging = true;
                activePointerId = evt.pointerId;
                startPosition = horizontal ? evt.position.x : evt.position.y;
                startValue = currentValue;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || evt.pointerId != activePointerId) return;
                var position = horizontal ? evt.position.x : evt.position.y;
                var delta = (position - startPosition) * (reverse ? -1f : 1f);
                currentValue = Mathf.Clamp(startValue + delta, min, max);
                apply(currentValue);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging || evt.pointerId != activePointerId) return;
                dragging = false;
                activePointerId = -1;
                if (handle.HasPointerCapture(evt.pointerId))
                    handle.ReleasePointer(evt.pointerId);
                PlayerPrefs.SetFloat(key, currentValue);
                PlayerPrefs.Save();
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!dragging) return;
                dragging = false;
                activePointerId = -1;
                PlayerPrefs.SetFloat(key, currentValue);
                PlayerPrefs.Save();
            });
            return handle;
        }

        private static VisualElement Column(float width)
        {
            var column = new VisualElement();
            if (width > 0)
                column.style.width = width;
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        private static VisualElement Panel(string id, string caption, float minHeight)
        {
            var panel = new VisualElement { name = "vcr-panel-" + id };
            panel.style.flexGrow = 1;
            panel.style.minHeight = minHeight;
            panel.style.marginLeft = 3;
            panel.style.marginRight = 3;
            panel.style.marginTop = 3;
            panel.style.marginBottom = 3;
            panel.style.paddingLeft = 10;
            panel.style.paddingTop = 8;
            panel.style.backgroundColor = new Color(0.18f, 0.20f, 0.23f);
            var label = new Label(caption);
            label.style.color = Color.white;
            panel.Add(label);
            return panel;
        }
    }
}
