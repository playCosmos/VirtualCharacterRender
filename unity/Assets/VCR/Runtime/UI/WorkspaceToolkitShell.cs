using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Opt-in UI Toolkit workspace prototype. Attach to a dedicated GameObject
    /// with a UIDocument/PanelSettings; it does not replace the existing uGUI
    /// controller or own the character/rendering lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorkspaceToolkitShell : MonoBehaviour
    {
        [SerializeField] private bool advancedWorkspace;
        [SerializeField] private SingleCharacterSceneRuntime sceneRuntime;
        [SerializeField] private ApplicationUiController applicationUiController;
        [SerializeField, Min(0.1f)] private float statusRefreshSeconds = 0.5f;
        private float _nextStatusRefresh;
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _workspaceRoot;
        private VisualElement _dockHost;
        private Label _status;
        private Label _sourcesStatus;
        private Label _inspectorStatus;
        private Label _trackingStatus;
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

        private void OnEnable()
        {
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
            _previewExplicitlyBound = previewRenderTexture != null;
            _root = _document.rootVisualElement;
            _workspaceRoot = new VisualElement { name = "vcr-workspace-shell" };
            _workspaceRoot.style.flexGrow = 1;
            _root.Add(_workspaceRoot);
            Build();
        }

        private void Update()
        {
            if (previewCamera == null && discoverCameraPreview && sceneRuntime != null)
                previewCamera = sceneRuntime.CameraController != null
                    ? sceneRuntime.CameraController.GetComponent<Camera>() : null;
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
                _status.text = "Scene runtime unavailable";
                if (_sourcesStatus != null) _sourcesStatus.text = "Model: unavailable";
                if (_inspectorStatus != null) _inspectorStatus.text = "Scene runtime unavailable";
                if (_trackingStatus != null) _trackingStatus.text = "Runtime unavailable";
                if (_previewStatus != null) _previewStatus.text = _previewTexture == null
                    ? "Preview unavailable: no scene runtime" : "External preview texture connected";
                return;
            }
            var state = sceneRuntime.Status;
            var path = sceneRuntime.CurrentCharacterPath;
            var modelName = string.IsNullOrWhiteSpace(path)
                ? "<none>" : System.IO.Path.GetFileName(path);
            _status.text = "Scene: " + state.State + " | Model: " + modelName +
                " | Loaded: " + state.HasCharacter;
            if (_sourcesStatus != null) _sourcesStatus.text = "Active model: " + modelName;
            if (_previewStatus != null)
                _previewStatus.text = state.HasCharacter
                    ? "Model loaded: " + modelName + (_previewTexture == null ? " (viewport not connected)" : " (preview connected)")
                    : "No active model" + (_previewTexture == null ? " (viewport not connected)" : " (preview connected)");
            if (_inspectorStatus != null)
                _inspectorStatus.text = "State: " + state.State + " | Loaded: " + state.HasCharacter;
            if (_trackingStatus != null)
                _trackingStatus.text = "Scene active: " + state.HasCharacter + " (tracking details not connected)";
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

        private void OnDisable()
        {
            if (_workspaceRoot != null)
                _workspaceRoot.RemoveFromHierarchy();
            _workspaceRoot = null;
            _root = null;
            _dockHost = null;
            _status = null;
            _sourcesStatus = null;
            _inspectorStatus = null;
            _trackingStatus = null;
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
            var width = Mathf.Clamp(Mathf.RoundToInt(availableWidth), 256, 1920);
            var height = Mathf.Clamp(Mathf.RoundToInt(availableHeight - 45f), 256, 1080);
            if (width == _previewWidth && height == _previewHeight && _ownedPreviewCamera != null)
                return;
            ReleaseOwnedPreview();
            _previewWidth = width;
            _previewHeight = height;
            _ownedPreviewTexture = new RenderTexture(width, height, 16)
            {
                name = "VCR Workspace Preview",
                hideFlags = HideFlags.DontSave
            };
            _ownedPreviewTexture.Create();
            var cameraObject = new GameObject("VCR Workspace Preview Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            _ownedPreviewCamera = cameraObject.AddComponent<Camera>();
            _ownedPreviewCamera.enabled = false;
            _ownedPreviewCamera.targetTexture = _ownedPreviewTexture;
            SetPreviewTexture(_ownedPreviewTexture);
        }

        private void LateUpdate()
        {
            if (_ownedPreviewCamera == null || previewCamera == null) return;
            _ownedPreviewCamera.CopyFrom(previewCamera);
            _ownedPreviewCamera.enabled = false;
            _ownedPreviewCamera.targetTexture = _ownedPreviewTexture;
            _ownedPreviewCamera.transform.SetPositionAndRotation(
                previewCamera.transform.position, previewCamera.transform.rotation);
            try
            {
                _ownedPreviewCamera.Render();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Workspace preview render unavailable: " + exception.Message, this);
                ReleaseOwnedPreview();
                _previewRenderUnsupported = true;
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
                if (applicationUiController == null)
                    applicationUiController = FindFirstObjectByType<ApplicationUiController>();
                if (applicationUiController != null)
                    applicationUiController.ShowNativeSettingsWindow();
            }) { text = "Settings" };
            settingsButton.SetEnabled(DesktopNativeSettingsWindow.Supported);
            settingsButton.tooltip = DesktopNativeSettingsWindow.Supported
                ? "Open the existing native settings window"
                : "Native settings are unavailable on this platform";
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
            left.Add(scenes);
            var scenesSplitter = Splitter(false, _scenesHeight, 100f, 400f, value =>
            {
                _scenesHeight = value;
                scenes.style.height = value;
            }, ScenesHeightKey);
            scenesSplitter.style.display = _tabMode ? DisplayStyle.None : DisplayStyle.Flex;
            left.Add(scenesSplitter);
            var sources = Panel(WorkspacePanelCatalog.Sources, "Sources / Models", 220);
            _sourcesStatus = new Label("Waiting for model state...");
            sources.Add(_sourcesStatus);
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
                inspectorSplitter.style.display = !_tabMode && inspector.parent == right
                    ? DisplayStyle.Flex : DisplayStyle.None;
                right.Add(inspectorSplitter);
                PlacePanel(Panel(WorkspacePanelCatalog.Materials, "Materials / Shaders", 140), WorkspacePanelCatalog.Materials, "right");
                PlacePanel(Panel(WorkspacePanelCatalog.Capabilities, "Modules", 120), WorkspacePanelCatalog.Capabilities, "right");
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
            PlacePanel(tracking, WorkspacePanelCatalog.Tracking, "bottom");
            PlacePanel(Panel(WorkspacePanelCatalog.Actions, "Actions", 150), WorkspacePanelCatalog.Actions, "bottom");
            PlacePanel(Panel(WorkspacePanelCatalog.Events, "Log / Events", 150), WorkspacePanelCatalog.Events, "bottom");
            _workspaceRoot.Add(bottom);
            SortDockPanels(left);
            SortDockPanels(right);
            SortDockPanels(bottom);
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
            panels.Sort((a, b) =>
            {
                var aId = a.name.Substring("vcr-panel-".Length);
                var bId = b.name.Substring("vcr-panel-".Length);
                var aOrder = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + aId + ".v1", int.MaxValue);
                var bOrder = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + bId + ".v1", int.MaxValue);
                return aOrder.CompareTo(bOrder);
            });
            foreach (var panel in panels)
            {
                dock.Remove(panel);
                dock.Add(panel);
            }
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
                    if ((evt.position - tabStart).sqrMagnitude > 64f)
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
                dragHandle.CapturePointer(pointerId);
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
            var orderKey = "VCR.UI.Workspace.Order." + id + ".v1";
            var savedOrder = PlayerPrefs.GetInt(orderKey, -1);
            if (savedOrder >= 0 && target.childCount > 1)
            {
                target.Remove(panel);
                target.Insert(Mathf.Clamp(savedOrder, 0, target.childCount), panel);
            }
            panel.Add(new Button(() => MovePanelOrder(panel, id, -1)) { text = "Up / Earlier" });
            panel.Add(new Button(() => MovePanelOrder(panel, id, 1)) { text = "Down / Later" });
        }

        private void MovePanelToDock(VisualElement panel, string id, string destination)
        {
            if (destination != "left" && destination != "right" && destination != "bottom") return;
            var previous = panel.parent;
            PlayerPrefs.SetString("VCR.UI.Workspace.Dock." + id + ".v1", destination);
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab." + destination + ".v1", panel.name);
            PlayerPrefs.DeleteKey("VCR.UI.Workspace.Order." + id + ".v1");
            if (destination == "left") { _leftVisible = true; PlayerPrefs.SetInt(LeftVisibleKey, 1); }
            if (destination == "right") { _rightVisible = true; PlayerPrefs.SetInt(RightVisibleKey, 1); }
            if (destination == "bottom") { _bottomVisible = true; PlayerPrefs.SetInt(BottomVisibleKey, 1); }
            if (previous != null)
            {
                previous.Remove(panel);
                SaveDockOrder(previous);
            }
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
