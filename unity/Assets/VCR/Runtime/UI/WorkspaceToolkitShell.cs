using System;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;
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
        // Opt-in, read-only evidence for CI's real visible Player. This never
        // creates a synthetic UI or changes runtime configuration.
        private bool _guiProbeRequested;
        private bool _guiProbeReported;
        private string _guiProbeScreenshotPath;
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
        private Button _revertCameraDraftButton;
        private Button _applyRenderButton;
        private IntegerField _targetFrameRateInput;
        private FloatField _cameraFieldOfViewInput;
        private Vector3Field _cameraPositionInput;
        private Vector3Field _cameraRotationInput;
        private FloatField _cameraNearClipInput;
        private FloatField _cameraFarClipInput;
        private FloatField _cameraFieldOfViewBoundField;
        private PrimaryCameraController _cameraDraftOwner;
        private Camera _cameraDraftTarget;
        private SingleCharacterSceneRuntime _cameraDraftRuntime;
        private SceneCameraSettings _cameraDraft;
        private bool _cameraDraftValid;
        private FloatField _primaryLightIntensityInput;
        private Toggle _primaryLightEnabledInput;
        private Vector3Field _primaryLightRotationInput;
        private Vector3Field _primaryLightRgbInput;
        private FloatField _primaryLightAlphaInput;
        private DropdownField _primaryLightShadowsInput;
        private Button _applyPrimaryLightButton;
        private Button _revertLightDraftButton;
        private PrimaryLightController _primaryLightDraftOwner;
        private Light _primaryLightDraftTarget;
        private SingleCharacterSceneRuntime _primaryLightDraftRuntime;
        private FloatField _primaryLightBoundIntensity;
        private Toggle _primaryLightBoundEnabled;
        private SceneLightSettings _primaryLightDraft;
        private bool _primaryLightDraftValid;
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
            var launchArgs = System.Environment.GetCommandLineArgs();
            _guiProbeRequested = Array.Exists(launchArgs,
                arg => string.Equals(arg, "--vcr-ui-probe", StringComparison.Ordinal));
            _guiProbeScreenshotPath = null;
            if (_guiProbeRequested)
            {
                const string screenshotArg = "--vcr-ui-probe-image=";
                foreach (var arg in launchArgs)
                    if (arg.StartsWith(screenshotArg, StringComparison.Ordinal))
                    {
                        var candidate = arg.Substring(screenshotArg.Length);
                        if (System.IO.Path.IsPathRooted(candidate) &&
                            string.Equals(System.IO.Path.GetExtension(candidate),
                                ".png", StringComparison.OrdinalIgnoreCase))
                            _guiProbeScreenshotPath = candidate;
                        break;
                    }
            }
            _guiProbeReported = false;
            InitializeWorkspaceTrackingRestore();
            ResetWorkspaceTrackingPanel();
            ResetWorkspaceTrackingCameraPreview();
            ResetWorkspaceCapabilitiesPanel();
            ResetWorkspaceLogPanel();
            ResetWorkspaceMaterialsPanel();
            ResetWorkspaceConfigurationActions();
            ResetWorkspaceScenesPanel();
            ResetWorkspaceBroadcastPanel();
            ResetWorkspaceEnvironmentPanel();
            ResetWorkspaceEventRules();
            ResetWorkspaceAppearancePanel();
            ResetWorkspaceMotionPanel();
            SubscribeWorkspaceLog();
            _cameraDraftValid = false;
            _cameraDraftOwner = null;
            _cameraDraftTarget = null;
            _cameraDraftRuntime = null;
            _primaryLightDraftValid = false;
            _primaryLightDraftOwner = null;
            _primaryLightDraftTarget = null;
            _primaryLightDraftRuntime = null;
            _primaryLightBoundIntensity = null;
            _primaryLightBoundEnabled = null;
            _cameraFieldOfViewBoundField = null;
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

        private void ReportWorkspaceGuiProbeIfReady()
        {
            if (!_guiProbeRequested || _guiProbeReported ||
                _workspaceRoot == null || _workspaceRoot.panel == null ||
                _applySceneViewButton == null || _applyPrimaryLightButton == null ||
                _modelPathInput == null)
                return;

            // Require a laid-out, attached UI tree with actual Inspector and
            // Sources controls, not only a UIDocument or GameObject reference.
            var bounds = _workspaceRoot.worldBound;
            if (float.IsNaN(bounds.width) || float.IsInfinity(bounds.width) ||
                float.IsNaN(bounds.height) || float.IsInfinity(bounds.height) ||
                bounds.width < 256f || bounds.height < 180f ||
                _applySceneViewButton.panel != _workspaceRoot.panel ||
                _applyPrimaryLightButton.panel != _workspaceRoot.panel ||
                _modelPathInput.panel != _workspaceRoot.panel)
                return;

            _guiProbeReported = true;
            Debug.Log("VCR U1 GUI probe: workspace_tree_ready=true " +
                "width=" + bounds.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " height=" + bounds.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " camera_apply_attached=true light_apply_attached=true " +
                "model_path_attached=true", this);
            if (!string.IsNullOrEmpty(_guiProbeScreenshotPath))
                StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                    _guiProbeScreenshotPath));
        }

        // ScreenCapture reads Unity's own frame before the native window's
        // desktop transparency/BitBlt path. It is an independent diagnostic
        // of a real Player and is never run outside the explicit CLI probe.
        private System.Collections.IEnumerator CaptureWorkspaceGuiScreenshotAtEndOfFrame(
            string outputPath)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                ScreenCapture.CaptureScreenshot(outputPath);
                Debug.Log("VCR U1 GUI probe: unity_screenshot_requested=true", this);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("VCR U1 GUI probe: unity_screenshot_request_failed=" +
                    exception.Message, this);
            }
        }

        private void Update()
        {
            ReportWorkspaceGuiProbeIfReady();
            PumpWorkspaceNativeSettings();
            RestoreWorkspaceTrackingPreferences();
            RefreshWorkspaceTrackingPanel();
            RefreshWorkspaceCapabilitiesPanel();
            RefreshWorkspaceMaterialsPanel();
            DrainWorkspaceLog();
            RefreshWorkspaceDiagnosticsPanel();
            RefreshWorkspaceScenesPanel();
            RefreshWorkspaceBroadcastStatus();
            RefreshWorkspaceEnvironmentPanel();
            RefreshWorkspaceEventRulesPanel();
            RefreshWorkspaceAppearancePanel();
            RefreshWorkspaceMotionPanel();
            PersistLayoutIfChanged();
            // Resolve the runtime before camera discovery. The runtime may be
            // created after the shell, including on a delayed startup path.
            if (sceneRuntime == null && Time.unscaledTime >= _nextStatusRefresh)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (discoverCameraPreview && sceneRuntime != null)
            {
                // PrimaryCameraController may be on a parent of its Camera.
                // Use the adapter's resolved target, not a same-GameObject guess.
                var runtimeCamera = sceneRuntime.CameraController != null
                    ? sceneRuntime.CameraController.TargetCamera : null;
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
                _cameraDraftOwner = null;
                _cameraDraftTarget = null;
                _cameraDraftRuntime = null;
                _cameraFieldOfViewBoundField = null;
                _primaryLightDraftOwner = null;
                _primaryLightDraftTarget = null;
                _primaryLightDraftRuntime = null;
                _primaryLightBoundIntensity = null;
                _primaryLightBoundEnabled = null;
                _status.text = "Scene runtime unavailable";
                if (_sourcesStatus != null) _sourcesStatus.text = "Model: unavailable";
                if (_inspectorStatus != null) _inspectorStatus.text = "Scene runtime unavailable";
                if (_inspectorModelPath != null) _inspectorModelPath.text = "Model path: unavailable";
                if (_inspectorCameraStatus != null) _inspectorCameraStatus.text = "Camera: unavailable";
                if (_loadModelButton != null) _loadModelButton.SetEnabled(false);
                if (_reloadCharacterButton != null) _reloadCharacterButton.SetEnabled(false);
                if (_unloadCharacterButton != null) _unloadCharacterButton.SetEnabled(false);
                if (_applySceneViewButton != null) _applySceneViewButton.SetEnabled(false);
                if (_revertCameraDraftButton != null) _revertCameraDraftButton.SetEnabled(false);
                if (_cameraFieldOfViewInput != null) _cameraFieldOfViewInput.SetEnabled(false);
                if (_cameraPositionInput != null) _cameraPositionInput.SetEnabled(false);
                if (_cameraRotationInput != null) _cameraRotationInput.SetEnabled(false);
                if (_cameraNearClipInput != null) _cameraNearClipInput.SetEnabled(false);
                if (_cameraFarClipInput != null) _cameraFarClipInput.SetEnabled(false);
                if (_primaryLightIntensityInput != null) _primaryLightIntensityInput.SetEnabled(false);
                if (_primaryLightEnabledInput != null) _primaryLightEnabledInput.SetEnabled(false);
                if (_primaryLightRotationInput != null) _primaryLightRotationInput.SetEnabled(false);
                if (_primaryLightRgbInput != null) _primaryLightRgbInput.SetEnabled(false);
                if (_primaryLightAlphaInput != null) _primaryLightAlphaInput.SetEnabled(false);
                if (_primaryLightShadowsInput != null) _primaryLightShadowsInput.SetEnabled(false);
                if (_applyPrimaryLightButton != null) _applyPrimaryLightButton.SetEnabled(false);
                if (_revertLightDraftButton != null) _revertLightDraftButton.SetEnabled(false);
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
            SynchronizeCameraInspectorInputs();
            SynchronizePrimaryLightInputs();
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
            var canEditCamera = sceneRuntime.CameraController != null &&
                sceneRuntime.CameraController.TargetCamera != null &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State) &&
                !_characterActionPending;
            if (_cameraFieldOfViewInput != null) _cameraFieldOfViewInput.SetEnabled(canEditCamera);
            if (_cameraPositionInput != null) _cameraPositionInput.SetEnabled(canEditCamera);
            if (_cameraRotationInput != null) _cameraRotationInput.SetEnabled(canEditCamera);
            if (_cameraNearClipInput != null) _cameraNearClipInput.SetEnabled(canEditCamera);
            if (_cameraFarClipInput != null) _cameraFarClipInput.SetEnabled(canEditCamera);
            if (_revertCameraDraftButton != null) _revertCameraDraftButton.SetEnabled(canEditCamera);
            var hasPrimaryLight = sceneRuntime.LightController != null &&
                sceneRuntime.LightController.TargetLight != null;
            if (_primaryLightIntensityInput != null)
                _primaryLightIntensityInput.SetEnabled(hasPrimaryLight &&
                    !_characterActionPending &&
                    ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State));
            if (_primaryLightEnabledInput != null)
                _primaryLightEnabledInput.SetEnabled(hasPrimaryLight &&
                    !_characterActionPending &&
                    ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State));
            if (_applyPrimaryLightButton != null)
                _applyPrimaryLightButton.SetEnabled(hasPrimaryLight &&
                    !_characterActionPending &&
                    ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State));
            var lightSettingsEditable = hasPrimaryLight && !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            if (_primaryLightRotationInput != null) _primaryLightRotationInput.SetEnabled(lightSettingsEditable);
            if (_primaryLightRgbInput != null) _primaryLightRgbInput.SetEnabled(lightSettingsEditable);
            if (_primaryLightAlphaInput != null) _primaryLightAlphaInput.SetEnabled(lightSettingsEditable);
            if (_primaryLightShadowsInput != null) _primaryLightShadowsInput.SetEnabled(lightSettingsEditable);
            if (_revertLightDraftButton != null) _revertLightDraftButton.SetEnabled(lightSettingsEditable);
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
            var canApplySettings = !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            if (_applySceneViewButton != null)
                _applySceneViewButton.SetEnabled(sceneRuntime.CameraController != null &&
                    sceneRuntime.CameraController.TargetCamera != null && canApplySettings);
            if (_applyRenderButton != null)
                _applyRenderButton.SetEnabled(canApplySettings);
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
            ResetWorkspaceTrackingCameraPreview();
            ResetWorkspaceCapabilitiesPanel();
            ResetWorkspaceLogPanel();
            ResetWorkspaceMaterialsPanel();
            ResetWorkspaceConfigurationActions();
            ResetWorkspaceScenesPanel();
            ResetWorkspaceBroadcastPanel();
            ResetWorkspaceEnvironmentPanel();
            ResetWorkspaceEventRules();
            ResetWorkspaceAppearancePanel();
            ResetWorkspaceMotionPanel();
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
            _revertCameraDraftButton = null;
            _applyRenderButton = null;
            _targetFrameRateInput = null;
            _cameraFieldOfViewInput = null;
            _cameraPositionInput = null;
            _cameraRotationInput = null;
            _cameraNearClipInput = null;
            _cameraFarClipInput = null;
            _cameraDraftOwner = null;
            _cameraDraftTarget = null;
            _cameraDraftRuntime = null;
            _cameraFieldOfViewBoundField = null;
            _cameraDraftValid = false;
            _primaryLightIntensityInput = null;
            _primaryLightEnabledInput = null;
            _primaryLightRotationInput = null;
            _primaryLightRgbInput = null;
            _primaryLightAlphaInput = null;
            _primaryLightShadowsInput = null;
            _applyPrimaryLightButton = null;
            _revertLightDraftButton = null;
            _primaryLightDraftOwner = null;
            _primaryLightDraftTarget = null;
            _primaryLightDraftRuntime = null;
            _primaryLightBoundIntensity = null;
            _primaryLightBoundEnabled = null;
            _primaryLightDraftValid = false;
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
                    _workspaceNativeSettings.Add(0, 205, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Save runtime configuration");
                    _workspaceNativeSettings.Add(0, 206, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Reload saved configuration");
                    _workspaceNativeSettings.Add(0, 90, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Changes apply to the active scene.");
                    _workspaceNativeSettings.Add(0, 94, DesktopNativeSettingsWindow.ControlKind.Label,
                        "Configuration status unavailable.");
                    _workspaceNativeSettings.Add(1, 2, DesktopNativeSettingsWindow.ControlKind.Label,
                        "VRM model management");
                    _workspaceNativeSettings.Add(1, 12, DesktopNativeSettingsWindow.ControlKind.Edit,
                        "VRM file path", "");
                    _workspaceNativeSettings.Add(1, 300, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Load VRM from path");
                    _workspaceNativeSettings.Add(1, 301, DesktopNativeSettingsWindow.ControlKind.Button,
                        "Browse VRM file...");
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
                // Update fields only when opening a previously closed window.
                // Re-invoking Settings while it is already open must not discard
                // a partially edited model path, FPS or render scale.
                if (!_workspaceNativeSettings.IsVisible)
                {
                    if (sceneRuntime != null && sceneRuntime.TryCaptureRenderSettings(out var settings))
                    {
                        _workspaceNativeSettings.SetText(10,
                            settings.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        _workspaceNativeSettings.SetText(11,
                            settings.TargetFrameRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    _workspaceNativeSettings.SetText(12,
                        _modelPathInput != null ? _modelPathInput.value ?? "" :
                        PlayerPrefs.GetString("VCR.Character.LastSuccessfulPath.v1", ""));
                    RefreshWorkspaceConfigurationStatus();
                    RefreshWorkspaceOverlayStatus();
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
                if (action == 205 || action == 206)
                {
                    ApplyWorkspaceConfigurationActionWithNativeFeedback(action);
                    continue;
                }
                if (action == 301)
                {
                    BrowseWorkspaceCharacterFile();
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
                if (!ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
                {
                    _workspaceNativeSettings.SetText(90, "Render settings unavailable while scene is busy.");
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
                try
                {
                    sceneRuntime.ApplyRenderSettings(settings);
                    // Applying *any* render setting writes runInBackground
                    // through DesktopRenderBootstrap. Keep native Settings
                    // responsive even when that preference is false.
                    if (_nativeSettingsWasVisible)
                    {
                        _runInBackgroundBeforeSettings = settings.RunInBackground;
                        UnityEngine.Application.runInBackground = true;
                    }
                    if (!sceneRuntime.TryCaptureRenderSettings(out var applied))
                        throw new InvalidOperationException("Unable to read applied render settings.");
                    _workspaceNativeSettings.SetText(10,
                        applied.RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    _workspaceNativeSettings.SetText(11,
                        applied.TargetFrameRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _workspaceNativeSettings.SetText(90,
                        "FPS: " + applied.TargetFrameRate + ", VSync: " +
                        (applied.UseVSync ? "on" : "off") + ", Background: " +
                        (applied.RunInBackground ? "on" : "off"));
                    // A native settings change must not leave the Inspector
                    // with stale values that could be applied over this edit.
                    _renderInputsRuntime = null;
                    SynchronizeRenderInputs();
                }
                catch (Exception exception)
                {
                    _workspaceNativeSettings.SetText(90, "Render settings failed: " + exception.Message);
                    Debug.LogException(exception, this);
                }
            }
        }

        private string GetWorkspaceOverlayStatus()
        {
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
                return "Output runtime unavailable.";
            try
            {
                if (!sceneRuntime.TryCaptureOverlayOutput(
                        out _, out var settings, out var error))
                    return "Output unavailable: " + (error ?? "unknown error");
                return "Transparent: " + (settings.Transparent ? "on" : "off") +
                    ", click-through: " + (settings.ClickThrough ? "on" : "off");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return "Output status failed: " + exception.Message;
            }
        }

        private void RefreshWorkspaceOverlayStatus()
        {
            if (_workspaceNativeSettings != null)
                _workspaceNativeSettings.SetText(93, GetWorkspaceOverlayStatus());
        }

        // The same output service backs both native Settings and the
        // non-native editor fallback. The latter can safely turn
        // click-through OFF, but never enable it without an independent
        // native Settings window to recover mouse input.
        private string ExecuteWorkspaceOverlayAction(int action, bool nativeWindow)
        {
            if (action != 401 && action != 402 && action != 403 && action != 404)
                return "Unsupported output action.";
            if (action == 402 && !nativeWindow)
                return "Enabling click-through requires the independent native Settings window.";
            if (action == 404 && nativeWindow)
                return "Unsupported native output action.";
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null)
                return "Output runtime unavailable.";
            try
            {
                if (action == 403)
                    return sceneRuntime.TryRecoverOverlayOutput(out var recoverError)
                        ? "Output recovery requested."
                        : "Output recovery failed: " + (recoverError ?? "unknown error");

                if (!sceneRuntime.TryCaptureOverlayOutput(
                        out _, out var current, out var error))
                    return "Output unavailable: " + (error ?? "unknown error");
                if (_characterActionPending ||
                    !ApplicationUiActionPolicy.CanApplyOverlaySetting(
                        true, sceneRuntime.State, true))
                    return "Output settings cannot be changed while the scene is busy.";

                if (action == 404 && !current.ClickThrough)
                    return "Click-through is already disabled.";

                var next = new OverlayOutputSettings(
                    action == 401 ? !current.Transparent : current.Transparent,
                    false,
                    action == 402 ? !current.ClickThrough :
                        action == 404 ? false : current.ClickThrough);
                sceneRuntime.ApplyOverlayOutput(next);
                return action == 402 && next.ClickThrough
                    ? "Click-through ON: mouse input passes through the overlay. " +
                      "Use this independent Settings window to disable it."
                    : "Output updated. " + GetWorkspaceOverlayStatus();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return "Output action failed: " + exception.Message;
            }
        }

        private void ApplyWorkspaceOverlayAction(int action)
        {
            var result = ExecuteWorkspaceOverlayAction(action, nativeWindow: true);
            if (_workspaceNativeSettings != null)
                _workspaceNativeSettings.SetText(93, result);
        }

        private void BuildWorkspaceOverlayFallback(VisualElement content)
        {
            var title = new Label("Output / transparent overlay");
            title.style.marginTop = 12;
            content.Add(title);
            var state = new Label(GetWorkspaceOverlayStatus());
            state.style.whiteSpace = WhiteSpace.Normal;
            content.Add(state);
            var feedback = new Label(
                "Click-through can only be enabled from independent native Settings.");
            feedback.style.whiteSpace = WhiteSpace.Normal;
            content.Add(feedback);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            void Run(int action)
            {
                feedback.text = ExecuteWorkspaceOverlayAction(action, nativeWindow: false);
                state.text = GetWorkspaceOverlayStatus();
            }
            row.Add(new Button(() => Run(401)) { text = "Toggle transparency" });
            row.Add(new Button(() => Run(404)) { text = "Disable click-through" });
            row.Add(new Button(() => Run(403)) { text = "Recover output" });
            row.Add(new Button(() => state.text = GetWorkspaceOverlayStatus())
                { text = "Refresh output status" });
            content.Add(row);
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
            panel.style.width = 330;
            // The Editor/non-native fallback must stay reachable on small
            // screens even after adding render scale and background controls.
            panel.style.height = Length.Percent(75);
            panel.style.minHeight = 220;
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
            var content = new ScrollView();
            content.style.flexGrow = 1;
            content.style.minHeight = 80;
            panel.Add(content);

            var scale = new FloatField("Render scale (0.5–2.0)");
            var fps = new IntegerField("Target FPS (30–120)");
            var vsync = new Toggle("VSync");
            var background = new Toggle("Run in background");
            var available = false;
            var initialScale = 1f;
            var initialFps = 60;
            var initialVsync = false;
            var initialBackground = true;
            if (sceneRuntime != null &&
                sceneRuntime.TryCaptureRenderSettings(out var current))
            {
                available = true;
                initialScale = current.RenderScale;
                initialFps = current.TargetFrameRate;
                initialVsync = current.UseVSync;
                initialBackground = current.RunInBackground;
            }
            scale.SetValueWithoutNotify(initialScale);
            fps.SetValueWithoutNotify(initialFps);
            vsync.SetValueWithoutNotify(initialVsync);
            background.SetValueWithoutNotify(initialBackground);
            content.Add(scale);
            content.Add(fps);
            content.Add(vsync);
            content.Add(background);

            var feedback = new Label(available ? "" :
                "Runtime not ready; Apply will retry discovery.");
            feedback.style.color = Color.white;
            feedback.style.whiteSpace = WhiteSpace.Normal;
            content.Add(feedback);
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
                if (_characterActionPending ||
                    !ApplicationUiActionPolicy.CanApplyRuntimeSettings(
                        true, sceneRuntime.State))
                {
                    feedback.text = "Render settings unavailable while the scene is busy.";
                    return;
                }
                var requestedScale = scale.value;
                if (float.IsNaN(requestedScale) ||
                    float.IsInfinity(requestedScale) ||
                    requestedScale < 0.5f || requestedScale > 2f)
                {
                    feedback.text = "Render scale must be finite and between 0.5 and 2.0.";
                    return;
                }
                if (fps.value < 30 || fps.value > 120)
                {
                    feedback.text = "Target FPS must be between 30 and 120.";
                    return;
                }
                try
                {
                    settings.RenderScale = requestedScale;
                    settings.TargetFrameRate = fps.value;
                    settings.UseVSync = vsync.value;
                    settings.RunInBackground = background.value;
                    sceneRuntime.ApplyRenderSettings(settings);
                    if (!sceneRuntime.TryCaptureRenderSettings(out var applied))
                        throw new InvalidOperationException(
                            "Unable to read back render settings.");
                    scale.SetValueWithoutNotify(applied.RenderScale);
                    fps.SetValueWithoutNotify(applied.TargetFrameRate);
                    vsync.SetValueWithoutNotify(applied.UseVSync);
                    background.SetValueWithoutNotify(applied.RunInBackground);
                    feedback.text = "Applied: scale " +
                        applied.RenderScale.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        ", " + applied.TargetFrameRate +
                        " FPS, VSync " + (applied.UseVSync ? "on" : "off") +
                        ", background " + (applied.RunInBackground ? "on" : "off") + ".";
                    _renderInputsRuntime = null;
                    SynchronizeRenderInputs();
                }
                catch (Exception exception)
                {
                    feedback.text = "Apply failed: " + exception.Message;
                    Debug.LogException(exception, this);
                }
            }) { text = "Apply render settings" };
            content.Add(apply);
            BuildWorkspaceConfigurationFallback(content, () =>
            {
                if (sceneRuntime != null &&
                    sceneRuntime.TryCaptureRenderSettings(out var loaded))
                {
                    scale.SetValueWithoutNotify(loaded.RenderScale);
                    fps.SetValueWithoutNotify(loaded.TargetFrameRate);
                    vsync.SetValueWithoutNotify(loaded.UseVSync);
                    background.SetValueWithoutNotify(loaded.RunInBackground);
                }
            });
            BuildWorkspaceOverlayFallback(content);
            panel.Add(new Button(() => panel.RemoveFromHierarchy()) { text = "Close" });
            _workspaceRoot.Add(panel);
        }

        private void Build()
        {
            // Layout/mode changes rebuild the whole UI tree. Keep the
            // operator's not-yet-loaded VRM path instead of replacing it
            // with the last successful model from PlayerPrefs.
            var modelPathDraft = _modelPathInput != null
                ? _modelPathInput.value
                : PlayerPrefs.GetString(LastSuccessfulCharacterPathKey, "");
            CaptureWorkspaceMaterialEditorDrafts();
            if (_cameraFieldOfViewInput != null && _cameraDraftOwner != null &&
                sceneRuntime != null && _cameraDraftRuntime == sceneRuntime &&
                _cameraDraftOwner == sceneRuntime.CameraController &&
                _cameraDraftTarget == sceneRuntime.CameraController.TargetCamera)
            {
                _cameraDraft.LocalPosition = _cameraPositionInput.value;
                _cameraDraft.LocalEulerAngles = _cameraRotationInput.value;
                _cameraDraft.FieldOfView = _cameraFieldOfViewInput.value;
                _cameraDraft.NearClipPlane = _cameraNearClipInput.value;
                _cameraDraft.FarClipPlane = _cameraFarClipInput.value;
                _cameraDraftValid = true;
            }
            if (_primaryLightIntensityInput != null && _primaryLightEnabledInput != null &&
                _primaryLightRotationInput != null && _primaryLightRgbInput != null &&
                _primaryLightAlphaInput != null && _primaryLightShadowsInput != null &&
                _primaryLightDraftOwner != null && sceneRuntime != null &&
                _primaryLightDraftRuntime == sceneRuntime &&
                _primaryLightDraftOwner == sceneRuntime.LightController &&
                _primaryLightDraftTarget == sceneRuntime.LightController.TargetLight)
            {
                _primaryLightDraft.Enabled = _primaryLightEnabledInput.value;
                _primaryLightDraft.Intensity = _primaryLightIntensityInput.value;
                _primaryLightDraft.LocalEulerAngles = _primaryLightRotationInput.value;
                var rgb = _primaryLightRgbInput.value;
                _primaryLightDraft.Color = new Color(
                    rgb.x, rgb.y, rgb.z, _primaryLightAlphaInput.value);
                _primaryLightDraft.Shadows = ParseWorkspaceLightShadows(
                    _primaryLightShadowsInput.value);
                _primaryLightDraftValid = true;
            }
            CaptureWorkspaceEnvironmentDraft();
            CaptureWorkspaceEventRulesDraft();
            CaptureWorkspaceAppearanceDraft();
            CaptureWorkspaceMotionDrafts();
            ReleaseOwnedPreview();
            _workspaceRoot.Clear();
            _workspaceRoot.style.flexGrow = 1;
            _workspaceRoot.style.backgroundColor = new Color(0.105f, 0.12f, 0.14f);
            // The workspace backgrounds are dark. Runtime UI Toolkit's default
            // label color can otherwise remain black without a custom theme.
            _workspaceRoot.style.color = new Color(0.91f, 0.94f, 0.97f);

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

            // Short player windows cannot display stacked fixed-height docks.
            // Adapt only the presentation; never overwrite the operator's
            // saved tab/stack preference.
            var compactHeight = Screen.height < 640;
            var effectiveTabMode = _tabMode || compactHeight;
            var bottom = new VisualElement { name = "vcr-bottom" };
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.height = compactHeight
                ? Mathf.Min(_bottomHeight, Mathf.Max(85f, Screen.height * 0.22f))
                : _bottomHeight;
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
            var scenesContent = new ScrollView { name = "vcr-scenes-content" };
            scenesContent.style.flexGrow = 1;
            scenesContent.style.minHeight = 0;
            scenes.Add(scenesContent);
            BuildWorkspaceScenesPanel(scenesContent);
            scenes.style.height = _scenesHeight;
            scenes.style.flexGrow = 0;
            PlacePanel(scenes, WorkspacePanelCatalog.Scenes, "left");
            var scenesSplitter = Splitter(false, _scenesHeight, 100f, 400f, value =>
            {
                _scenesHeight = value;
                scenes.style.height = value;
            }, ScenesHeightKey);
            scenesSplitter.name = "vcr-splitter-scenes";
            scenesSplitter.style.display = !effectiveTabMode && scenes.parent == left ? DisplayStyle.Flex : DisplayStyle.None;
            left.Add(scenesSplitter);
            var sources = Panel(WorkspacePanelCatalog.Sources, "Sources / Models", 220);
            var sourcesContent = new ScrollView { name = "vcr-sources-content" };
            sourcesContent.style.flexGrow = 1;
            sourcesContent.style.minHeight = 0;
            sources.Add(sourcesContent);
            _sourcesStatus = new Label("Waiting for model state...");
            sourcesContent.Add(_sourcesStatus);
            _modelPathInput = new TextField("VRM file path");
            _modelPathInput.value = modelPathDraft;
            _modelPathInput.tooltip = "Absolute path to a local .vrm file. FBX runtime import is not available.";
            sourcesContent.Add(_modelPathInput);
            var sourceActions = new VisualElement();
            sourceActions.style.flexDirection = FlexDirection.Row;
            sourceActions.style.flexWrap = Wrap.Wrap;
            sourceActions.Add(new Button(BrowseWorkspaceCharacterFile) { text = "Browse VRM..." });
            _loadModelButton = new Button(LoadModelFromPath) { text = "Load VRM" };
            sourceActions.Add(_loadModelButton);
            sourcesContent.Add(sourceActions);
            _sourcesFeedback = new Label("");
            sourcesContent.Add(_sourcesFeedback);
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
            // Camera, light and render controls exceed the 260 px advanced
            // Inspector height. Keep every action keyboard/scroll reachable.
            var inspectorContent = new ScrollView { name = "vcr-inspector-content" };
            inspectorContent.style.flexGrow = 1;
            inspectorContent.style.minHeight = 60;
            inspector.Add(inspectorContent);
            _inspectorStatus = new Label("Waiting for scene state...");
            inspectorContent.Add(_inspectorStatus);
            _inspectorModelPath = new Label("Model path: none");
            inspectorContent.Add(_inspectorModelPath);
            _inspectorCameraStatus = new Label("Camera: unavailable");
            inspectorContent.Add(_inspectorCameraStatus);
            _applySceneViewButton = new Button(ApplySceneViewFromInspector) { text = "Apply camera" };
            _revertCameraDraftButton = new Button(RevertCameraInspectorDraft)
                { text = "Discard camera edits" };
            _cameraPositionInput = new Vector3Field("Camera local position");
            _cameraRotationInput = new Vector3Field("Camera local Euler angles");
            _cameraFieldOfViewInput = new FloatField("Camera FOV (1-179)");
            _cameraNearClipInput = new FloatField("Near clip (>=0.001)");
            _cameraFarClipInput = new FloatField("Far clip (>=near + 0.01)");
            _cameraFieldOfViewInput.tooltip = "Camera vertical field of view in degrees (1–179).";
            inspectorContent.Add(_cameraPositionInput);
            inspectorContent.Add(_cameraRotationInput);
            inspectorContent.Add(_cameraFieldOfViewInput);
            inspectorContent.Add(_cameraNearClipInput);
            inspectorContent.Add(_cameraFarClipInput);
            inspectorContent.Add(_applySceneViewButton);
            inspectorContent.Add(_revertCameraDraftButton);
            SynchronizeCameraInspectorInputs();
            _primaryLightEnabledInput = new Toggle("Primary light enabled");
            _primaryLightIntensityInput = new FloatField("Light intensity (0-20)");
            _primaryLightRotationInput = new Vector3Field("Light local Euler angles");
            _primaryLightRgbInput = new Vector3Field("Light RGB (0-1)");
            _primaryLightAlphaInput = new FloatField("Light alpha (0-1)");
            _primaryLightShadowsInput = new DropdownField("Light shadows")
            {
                choices = new System.Collections.Generic.List<string>
                    { "None", "Hard", "Soft" }
            };
            _primaryLightIntensityInput.tooltip = "Finite nonnegative intensity, maximum 20.";
            _primaryLightRgbInput.tooltip = "Normalized RGB channels. This editor does not expose HDR colors.";
            _primaryLightAlphaInput.tooltip = "Normalized alpha, 0 to 1.";
            _applyPrimaryLightButton = new Button(ApplyPrimaryLightFromInspector)
                { text = "Apply primary light" };
            _revertLightDraftButton = new Button(RevertLightInspectorDraft)
                { text = "Discard light edits" };
            inspectorContent.Add(_primaryLightEnabledInput);
            inspectorContent.Add(_primaryLightIntensityInput);
            inspectorContent.Add(_primaryLightRotationInput);
            inspectorContent.Add(_primaryLightRgbInput);
            inspectorContent.Add(_primaryLightAlphaInput);
            inspectorContent.Add(_primaryLightShadowsInput);
            inspectorContent.Add(_applyPrimaryLightButton);
            inspectorContent.Add(_revertLightDraftButton);
            SynchronizePrimaryLightInputs();
            _applyRenderButton = new Button(ApplyRenderFromInspector) { text = "Apply current render settings" };
            _targetFrameRateInput = new IntegerField("Target FPS (30–120)");
            _targetFrameRateInput.tooltip = "Supported target: 30–120 FPS. Default: 60 FPS.";
            _vsyncInput = new Toggle("VSync");
            // Keep typed input intact for validation; silently clamping it
            // on blur hides invalid values and makes the Apply result unclear.
            _targetFrameRateInput.SetValueWithoutNotify(60);
            inspectorContent.Add(_targetFrameRateInput);
            inspectorContent.Add(_vsyncInput);
            _renderInputsRuntime = null;
            SynchronizeRenderInputs();
            inspectorContent.Add(_applyRenderButton);
            _inspectorFeedback = new Label("");
            inspectorContent.Add(_inspectorFeedback);
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
                inspectorSplitter.style.display = !effectiveTabMode && inspector.parent == right
                    ? DisplayStyle.Flex : DisplayStyle.None;
                right.Add(inspectorSplitter);
                var materials = Panel(WorkspacePanelCatalog.Materials, "Materials / Shaders", 200);
                BuildWorkspaceMaterialsPanel(materials);
                PlacePanel(materials, WorkspacePanelCatalog.Materials, "right");
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
            // Both the opt-in video surface and the device controls need to
            // remain reachable at the compact 150 px bottom-dock height.
            var trackingScroll = new ScrollView { name = "vcr-tracking-content" };
            trackingScroll.style.flexGrow = 1;
            trackingScroll.style.minHeight = 60;
            tracking.Add(trackingScroll);
            BuildWorkspaceTrackingCameraPreview(trackingScroll);
            BuildWorkspaceTrackingPanel(trackingScroll);
            PlacePanel(tracking, WorkspacePanelCatalog.Tracking, "bottom");
            var actions = Panel(WorkspacePanelCatalog.Actions, "Actions", 150);
            // The bottom dock defaults to 150 px. Keep both model actions
            // and broadcast controls reachable instead of clipping the latter.
            var actionsScroll = new ScrollView { name = "vcr-actions-content" };
            actionsScroll.style.flexGrow = 1;
            actionsScroll.style.minHeight = 60;
            _reloadCharacterButton = new Button(ReloadActiveCharacter) { text = "Reload active VRM" };
            _unloadCharacterButton = new Button(UnloadActiveCharacter) { text = "Unload active VRM" };
            _reloadCharacterButton.SetEnabled(false);
            _unloadCharacterButton.SetEnabled(false);
            actionsScroll.Add(_reloadCharacterButton);
            actionsScroll.Add(_unloadCharacterButton);
            _actionResult = new Label("Actions operate on the current scene runtime.");
            actionsScroll.Add(_actionResult);
            BuildWorkspaceBroadcastPanel(actionsScroll);
            BuildWorkspaceEnvironmentPanel(actionsScroll);
            BuildWorkspaceAppearancePanel(actionsScroll);
            BuildWorkspaceMotionPanel(actionsScroll);
            actions.Add(actionsScroll);
            PlacePanel(actions, WorkspacePanelCatalog.Actions, "bottom");
            var events = Panel(WorkspacePanelCatalog.Events, "Log / Events", 150);
            // The bottom dock is compact: scroll rule controls and logs as
            // one content area instead of clipping the controls below it.
            var eventsContent = new ScrollView { name = "vcr-events-content" };
            eventsContent.style.flexGrow = 1;
            eventsContent.style.minHeight = 60;
            BuildWorkspaceEventRulesPanel(eventsContent);
            BuildWorkspaceLogPanel(eventsContent);
            events.Add(eventsContent);
            PlacePanel(events, WorkspacePanelCatalog.Events, "bottom");
            _workspaceRoot.Add(bottom);
            SortDockPanels(left);
            SortDockPanels(right);
            SortDockPanels(bottom);
            PositionPanelSplitter(left, WorkspacePanelCatalog.Scenes, "vcr-splitter-scenes", effectiveTabMode);
            PositionPanelSplitter(right, WorkspacePanelCatalog.Inspector, "vcr-splitter-inspector", effectiveTabMode);
            if (effectiveTabMode)
            {
                ApplyDockTabs(left, "left");
                ApplyDockTabs(right, "right");
                ApplyDockTabs(bottom, "bottom");
                if (compactHeight)
                {
                    // The old minimum panel heights are for the stacked view;
                    // retaining them in a 400px-high window makes panels
                    // overflow the bottom dock. All substantive content has
                    // scrollable containers.
                    foreach (var dock in new[] { left, right, bottom })
                        foreach (var child in dock.Children())
                            if (child.name != null &&
                                child.name.StartsWith("vcr-panel-", StringComparison.Ordinal))
                                child.style.minHeight = 0;
                }
            }

            // Apply a readable default to all otherwise unstyled text labels,
            // including nested BaseField labels. Explicit warning/drag/title
            // colors remain untouched. Buttons keep their own contrast.
            _workspaceRoot.Query<Label>().ForEach(label =>
            {
                if (label.style.color.keyword == StyleKeyword.Null)
                    label.style.color = new Color(0.91f, 0.94f, 0.97f);
            });
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
            // One compact toolbar prevents four full-width dock controls
            // from covering the panel's actual content on short windows.
            var dockActions = new VisualElement { name = "vcr-dock-actions-" + id };
            dockActions.style.flexDirection = FlexDirection.Row;
            dockActions.style.flexShrink = 0;
            dockActions.style.flexWrap = Wrap.Wrap;
            var move = new Button(() =>
            {
                var next = currentDock == "left" ? "right" :
                    currentDock == "right" ? "bottom" : "left";
                MovePanelToDock(panel, id, next);
            }) { text = "Move: " + currentDock };
            move.tooltip = "Move this panel to the next dock.";
            dockActions.Add(move);
            var dragHandle = new Label("Drag");
            dragHandle.tooltip = "Drag this label to move or reorder the panel.";
            dragHandle.style.paddingLeft = 5;
            dragHandle.style.paddingRight = 5;
            dragHandle.style.paddingTop = 5;
            dragHandle.style.paddingBottom = 5;
            dragHandle.style.color = new Color(0.68f, 0.77f, 0.85f);
            dockActions.Add(dragHandle);
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
            var up = new Button(() => MovePanelOrder(panel, id, -1)) { text = "Up" };
            up.tooltip = "Move this panel earlier in the dock.";
            dockActions.Add(up);
            var down = new Button(() => MovePanelOrder(panel, id, 1)) { text = "Down" };
            down.tooltip = "Move this panel later in the dock.";
            dockActions.Add(down);
            panel.Add(dockActions);
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

        private void SynchronizeCameraInspectorInputs()
        {
            if (sceneRuntime == null || _cameraFieldOfViewInput == null ||
                _cameraPositionInput == null || _cameraRotationInput == null ||
                _cameraNearClipInput == null || _cameraFarClipInput == null)
                return;
            var camera = sceneRuntime.CameraController;
            if (camera == null || camera.TargetCamera == null)
                return;
            // Retain an unsaved draft when changing layouts for the same camera,
            // but refresh from the actual camera when ownership/config changes.
            var rebound = _cameraDraftOwner != camera ||
                _cameraDraftTarget != camera.TargetCamera ||
                _cameraDraftRuntime != sceneRuntime;
            if (rebound)
            {
                _cameraDraft = camera.Settings;
                _cameraDraftValid = true;
                _cameraDraftOwner = camera;
                _cameraDraftTarget = camera.TargetCamera;
                _cameraDraftRuntime = sceneRuntime;
            }
            if (_cameraFieldOfViewBoundField == _cameraFieldOfViewInput && !rebound)
                return;
            if (_cameraDraftValid)
            {
                _cameraPositionInput.SetValueWithoutNotify(_cameraDraft.LocalPosition);
                _cameraRotationInput.SetValueWithoutNotify(_cameraDraft.LocalEulerAngles);
                _cameraFieldOfViewInput.SetValueWithoutNotify(_cameraDraft.FieldOfView);
                _cameraNearClipInput.SetValueWithoutNotify(_cameraDraft.NearClipPlane);
                _cameraFarClipInput.SetValueWithoutNotify(_cameraDraft.FarClipPlane);
            }
            _cameraFieldOfViewBoundField = _cameraFieldOfViewInput;
        }

        private void RevertCameraInspectorDraft()
        {
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State) ||
                sceneRuntime.CameraController == null ||
                sceneRuntime.CameraController.TargetCamera == null ||
                _cameraFieldOfViewInput == null)
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Camera edits cannot be discarded: camera unavailable.";
                return;
            }

            // Deliberately read only the live adapter state. Discard is not a
            // scene mutation or configuration Reload, and must leave light
            // drafts and runtime settings untouched.
            _cameraDraftOwner = null;
            SynchronizeCameraInspectorInputs();
            if (_inspectorFeedback != null)
                _inspectorFeedback.text = "Camera edits discarded; live settings restored.";
        }

        private void ApplySceneViewFromInspector()
        {
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Camera settings unavailable while runtime is busy.";
                return;
            }
            try
            {
                var camera = sceneRuntime.CameraController;
                if (camera == null || camera.TargetCamera == null ||
                    _cameraPositionInput == null || _cameraRotationInput == null ||
                    _cameraFieldOfViewInput == null || _cameraNearClipInput == null ||
                    _cameraFarClipInput == null)
                    throw new InvalidOperationException("Primary camera inputs are unavailable.");

                // The operator can click Apply before the next periodic UI
                // refresh notices a retargeted adapter. Never submit fields
                // from a previous scene/camera to the new live target.
                if (_cameraDraftRuntime != sceneRuntime ||
                    _cameraDraftOwner != camera ||
                    _cameraDraftTarget != camera.TargetCamera)
                {
                    SynchronizeCameraInspectorInputs();
                    if (_inspectorFeedback != null)
                        _inspectorFeedback.text =
                            "Primary camera changed. Fields refreshed; review and apply again.";
                    return;
                }

                // The scene runtime validates the complete snapshot atomically
                // before changing the camera. Never silently clamp a draft.
                var next = camera.Settings;
                next.LocalPosition = _cameraPositionInput.value;
                next.LocalEulerAngles = _cameraRotationInput.value;
                next.FieldOfView = _cameraFieldOfViewInput.value;
                next.NearClipPlane = _cameraNearClipInput.value;
                next.FarClipPlane = _cameraFarClipInput.value;
                sceneRuntime.SetPrimaryCameraSettings(next);

                _cameraDraft = camera.Settings;
                _cameraDraftValid = true;
                _cameraDraftOwner = camera;
                _cameraDraftTarget = camera.TargetCamera;
                _cameraDraftRuntime = sceneRuntime;
                _cameraPositionInput.SetValueWithoutNotify(_cameraDraft.LocalPosition);
                _cameraRotationInput.SetValueWithoutNotify(_cameraDraft.LocalEulerAngles);
                _cameraFieldOfViewInput.SetValueWithoutNotify(_cameraDraft.FieldOfView);
                _cameraNearClipInput.SetValueWithoutNotify(_cameraDraft.NearClipPlane);
                _cameraFarClipInput.SetValueWithoutNotify(_cameraDraft.FarClipPlane);
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Primary camera settings applied.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Camera settings rejected: " + exception.Message;
            }
            _nextStatusRefresh = Time.unscaledTime + statusRefreshSeconds;
        }

        private static string FormatWorkspaceLightShadows(LightShadows value)
        {
            return value == LightShadows.Hard ? "Hard" :
                value == LightShadows.Soft ? "Soft" : "None";
        }

        private static LightShadows ParseWorkspaceLightShadows(string value)
        {
            return value == "Hard" ? LightShadows.Hard :
                value == "Soft" ? LightShadows.Soft : LightShadows.None;
        }

        private void SynchronizePrimaryLightInputs()
        {
            if (sceneRuntime == null || _primaryLightIntensityInput == null ||
                _primaryLightEnabledInput == null || _primaryLightRotationInput == null ||
                _primaryLightRgbInput == null || _primaryLightAlphaInput == null ||
                _primaryLightShadowsInput == null)
                return;
            var light = sceneRuntime.LightController;
            if (light == null || light.TargetLight == null)
                return;
            // Rebuilding the same light's dock retains all five pending edits.
            // Changing light owner or reloading config refreshes live state.
            var lightRebound = _primaryLightDraftOwner != light ||
                _primaryLightDraftTarget != light.TargetLight ||
                _primaryLightDraftRuntime != sceneRuntime;
            if (lightRebound)
            {
                _primaryLightDraft = light.Settings;
                _primaryLightDraftValid = true;
                _primaryLightDraftOwner = light;
                _primaryLightDraftTarget = light.TargetLight;
                _primaryLightDraftRuntime = sceneRuntime;
            }
            if (_primaryLightBoundIntensity == _primaryLightIntensityInput &&
                _primaryLightBoundEnabled == _primaryLightEnabledInput &&
                !lightRebound)
                return;
            if (_primaryLightDraftValid)
            {
                _primaryLightEnabledInput.SetValueWithoutNotify(_primaryLightDraft.Enabled);
                _primaryLightIntensityInput.SetValueWithoutNotify(_primaryLightDraft.Intensity);
                _primaryLightRotationInput.SetValueWithoutNotify(_primaryLightDraft.LocalEulerAngles);
                _primaryLightRgbInput.SetValueWithoutNotify(new Vector3(
                    _primaryLightDraft.Color.r, _primaryLightDraft.Color.g,
                    _primaryLightDraft.Color.b));
                _primaryLightAlphaInput.SetValueWithoutNotify(_primaryLightDraft.Color.a);
                _primaryLightShadowsInput.SetValueWithoutNotify(
                    FormatWorkspaceLightShadows(_primaryLightDraft.Shadows));
            }
            _primaryLightBoundIntensity = _primaryLightIntensityInput;
            _primaryLightBoundEnabled = _primaryLightEnabledInput;
        }

        private void RevertLightInspectorDraft()
        {
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State) ||
                sceneRuntime.LightController == null ||
                sceneRuntime.LightController.TargetLight == null ||
                _primaryLightIntensityInput == null)
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Light edits cannot be discarded: light unavailable.";
                return;
            }

            _primaryLightDraftOwner = null;
            SynchronizePrimaryLightInputs();
            if (_inspectorFeedback != null)
                _inspectorFeedback.text = "Light edits discarded; live settings restored.";
        }

        private void ApplyPrimaryLightFromInspector()
        {
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Light settings unavailable while runtime is busy.";
                return;
            }
            try
            {
                var light = sceneRuntime.LightController;
                if (light == null || light.TargetLight == null ||
                    _primaryLightIntensityInput == null || _primaryLightEnabledInput == null ||
                    _primaryLightRotationInput == null || _primaryLightRgbInput == null ||
                    _primaryLightAlphaInput == null || _primaryLightShadowsInput == null)
                    throw new InvalidOperationException("Primary light inputs are unavailable.");
                // A rapid Apply must not commit old light values to a new
                // target before the scheduled Inspector refresh runs.
                if (_primaryLightDraftRuntime != sceneRuntime ||
                    _primaryLightDraftOwner != light ||
                    _primaryLightDraftTarget != light.TargetLight)
                {
                    SynchronizePrimaryLightInputs();
                    if (_inspectorFeedback != null)
                        _inspectorFeedback.text =
                            "Primary light changed. Fields refreshed; review and apply again.";
                    return;
                }
                var rgb = _primaryLightRgbInput.value;
                var next = light.Settings;
                next.Enabled = _primaryLightEnabledInput.value;
                next.Intensity = _primaryLightIntensityInput.value;
                next.LocalEulerAngles = _primaryLightRotationInput.value;
                next.Color = new Color(rgb.x, rgb.y, rgb.z, _primaryLightAlphaInput.value);
                // Explicitly refuse unknown dropdown values rather than
                // interpreting them as 'None' during an Apply operation.
                if (_primaryLightShadowsInput.value != "None" &&
                    _primaryLightShadowsInput.value != "Hard" &&
                    _primaryLightShadowsInput.value != "Soft")
                    throw new ArgumentOutOfRangeException(nameof(next.Shadows),
                        "Choose None, Hard or Soft shadows.");
                next.Shadows = ParseWorkspaceLightShadows(_primaryLightShadowsInput.value);
                sceneRuntime.SetPrimaryLightConfiguration(next);

                _primaryLightDraft = light.Settings;
                _primaryLightDraftValid = true;
                _primaryLightDraftOwner = light;
                _primaryLightDraftTarget = light.TargetLight;
                _primaryLightDraftRuntime = sceneRuntime;
                _primaryLightEnabledInput.SetValueWithoutNotify(_primaryLightDraft.Enabled);
                _primaryLightIntensityInput.SetValueWithoutNotify(_primaryLightDraft.Intensity);
                _primaryLightRotationInput.SetValueWithoutNotify(_primaryLightDraft.LocalEulerAngles);
                _primaryLightRgbInput.SetValueWithoutNotify(new Vector3(
                    _primaryLightDraft.Color.r, _primaryLightDraft.Color.g,
                    _primaryLightDraft.Color.b));
                _primaryLightAlphaInput.SetValueWithoutNotify(_primaryLightDraft.Color.a);
                _primaryLightShadowsInput.SetValueWithoutNotify(
                    FormatWorkspaceLightShadows(_primaryLightDraft.Shadows));
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Primary light settings applied.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Primary light rejected: " + exception.Message;
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
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Render settings unavailable while runtime is busy.";
                return;
            }
            SynchronizeRenderInputs();
            if (_renderInputsRuntime != sceneRuntime)
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Render settings unavailable: runtime not initialized.";
                return;
            }
            if (_targetFrameRateInput == null || _targetFrameRateInput.value < 30 ||
                _targetFrameRateInput.value > 120)
            {
                if (_inspectorFeedback != null)
                    _inspectorFeedback.text = "Target FPS must be between 30 and 120.";
                return;
            }
            try
            {
                if (!sceneRuntime.TryCaptureRenderSettings(out var settings))
                    throw new System.InvalidOperationException("Render settings are unavailable.");
                settings.TargetFrameRate = _targetFrameRateInput.value;
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
            // The application bootstrap may be restoring configuration or a
            // command-line VRM asynchronously. Never race that startup load.
            if (!UnityEngine.Application.isEditor)
            {
                var bootstrap = FindFirstObjectByType<ApplicationRuntimeBootstrap>();
                if (bootstrap != null && bootstrap.isActiveAndEnabled && !bootstrap.IsStarted)
                    return;
            }
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
