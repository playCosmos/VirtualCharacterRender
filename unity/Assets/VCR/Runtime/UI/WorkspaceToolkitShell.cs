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
            if (!_previewExplicitlyBound && (_previewBoundFromCamera || _previewTexture == null))
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
            _previewBoundFromCamera = false;
        }

        private void Build()
        {
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
            left.Add(Splitter(false, _scenesHeight, 100f, 400f, value =>
            {
                _scenesHeight = value;
                scenes.style.height = value;
            }, ScenesHeightKey));
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
                right.Add(Splitter(false, _inspectorHeight, 140f, 520f, value =>
                {
                    _inspectorHeight = value;
                    inspector.style.height = value;
                }, RightInspectorHeightKey));
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

            _status = new Label("Waiting for scene runtime status...");
            _status.style.color = new Color(0.75f, 0.8f, 0.85f);
            _status.style.paddingLeft = 12;
            _status.style.paddingTop = 5;
            _status.style.paddingBottom = 5;
            _workspaceRoot.Add(_status);
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
                PlayerPrefs.SetString(key, next);
                if (next == "left") { _leftVisible = true; PlayerPrefs.SetInt(LeftVisibleKey, 1); }
                if (next == "right") { _rightVisible = true; PlayerPrefs.SetInt(RightVisibleKey, 1); }
                if (next == "bottom") { _bottomVisible = true; PlayerPrefs.SetInt(BottomVisibleKey, 1); }
                PlayerPrefs.Save();
                Build();
            }) { text = "Move: " + currentDock };
            panel.Add(move);
            var dragHandle = new Label("Drag panel to left / right / bottom");
            dragHandle.style.paddingTop = 5;
            dragHandle.style.paddingBottom = 5;
            dragHandle.style.color = new Color(0.68f, 0.77f, 0.85f);
            panel.Add(dragHandle);
            var dragging = false;
            var pointerId = -1;
            dragHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || dragging) return;
                dragging = true;
                pointerId = evt.pointerId;
                dragHandle.CapturePointer(pointerId);
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging || evt.pointerId != pointerId) return;
                dragging = false;
                pointerId = -1;
                var position = evt.position;
                if (dragHandle.HasPointerCapture(evt.pointerId))
                    dragHandle.ReleasePointer(evt.pointerId);
                var destination = _leftDock != null && _leftDock.worldBound.Contains(position)
                    ? "left" : _rightDock != null && _rightDock.worldBound.Contains(position)
                    ? "right" : _bottomDock != null && _bottomDock.worldBound.Contains(position)
                    ? "bottom" : null;
                if (destination != null && destination != currentDock)
                {
                    PlayerPrefs.SetString(key, destination);
                    if (destination == "left") { _leftVisible = true; PlayerPrefs.SetInt(LeftVisibleKey, 1); }
                    if (destination == "right") { _rightVisible = true; PlayerPrefs.SetInt(RightVisibleKey, 1); }
                    if (destination == "bottom") { _bottomVisible = true; PlayerPrefs.SetInt(BottomVisibleKey, 1); }
                    PlayerPrefs.Save();
                    Build();
                }
                evt.StopPropagation();
            });
            dragHandle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                dragging = false;
                pointerId = -1;
            });
            target.Add(panel);
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
            foreach (var id in WorkspacePanelCatalog.GetDefaultPanelIds(true))
                PlayerPrefs.DeleteKey("VCR.UI.Workspace.Dock." + id + ".v1");
            PlayerPrefs.DeleteKey(LeftVisibleKey);
            PlayerPrefs.DeleteKey(RightVisibleKey);
            PlayerPrefs.Save();
            _leftWidth = 220f;
            _scenesHeight = 180f;
            _inspectorHeight = 260f;
            _rightWidth = 260f;
            _bottomHeight = 150f;
            _bottomVisible = true;
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
