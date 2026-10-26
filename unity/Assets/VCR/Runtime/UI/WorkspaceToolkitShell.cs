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
        private VisualElement _dockHost;
        private Label _status;
        private Label _sourcesStatus;
        private Label _inspectorStatus;
        private Label _trackingStatus;
        private const string WorkspaceModeKey = "VCR.UI.Workspace.Advanced.v1";
        private const string LeftWidthKey = "VCR.UI.Workspace.LeftWidth.v1";
        private const string RightWidthKey = "VCR.UI.Workspace.RightWidth.v1";
        private const string BottomHeightKey = "VCR.UI.Workspace.BottomHeight.v1";
        private const string BottomVisibleKey = "VCR.UI.Workspace.BottomVisible.v1";
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
            _rightWidth = ReadSize(RightWidthKey, 260f, 160f, 520f);
            _bottomHeight = ReadSize(BottomHeightKey, 150f, 100f, 400f);
            _bottomVisible = PlayerPrefs.GetInt(BottomVisibleKey, 1) != 0;
            _root = _document.rootVisualElement;
            Build();
        }

        private void Update()
        {
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
                return;
            }
            var state = sceneRuntime.Status;
            var path = sceneRuntime.CurrentCharacterPath;
            var modelName = string.IsNullOrWhiteSpace(path)
                ? "<none>" : System.IO.Path.GetFileName(path);
            _status.text = "Scene: " + state.State + " | Model: " + modelName +
                " | Loaded: " + state.HasCharacter;
            if (_sourcesStatus != null) _sourcesStatus.text = "Active model: " + modelName;
            if (_inspectorStatus != null)
                _inspectorStatus.text = "State: " + state.State + " | Loaded: " + state.HasCharacter;
            if (_trackingStatus != null)
                _trackingStatus.text = "Scene active: " + state.HasCharacter + " (tracking details not connected)";
        }

        private void OnDisable()
        {
            if (_root != null)
                _root.Clear();
            _root = null;
            _dockHost = null;
            _status = null;
            _sourcesStatus = null;
            _inspectorStatus = null;
            _trackingStatus = null;
        }

        private void Build()
        {
            _root.Clear();
            _root.style.flexGrow = 1;
            _root.style.backgroundColor = new Color(0.105f, 0.12f, 0.14f);

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
            header.Add(new Button(ResetLayout) { text = "Reset layout" });
            _root.Add(header);

            _dockHost = new VisualElement();
            _dockHost.style.flexGrow = 1;
            _dockHost.style.flexDirection = FlexDirection.Row;
            _root.Add(_dockHost);

            var left = Column(_leftWidth);
            var center = Column(0);
            center.style.flexGrow = 1;
            var right = Column(_rightWidth);
            _dockHost.Add(left);
            _dockHost.Add(Splitter(true, _leftWidth, 140f, 520f, value =>
            {
                _leftWidth = value;
                left.style.width = value;
            }, LeftWidthKey));
            _dockHost.Add(center);
            _dockHost.Add(Splitter(true, _rightWidth, 160f, 520f, value =>
            {
                _rightWidth = value;
                right.style.width = value;
            }, RightWidthKey, true));
            _dockHost.Add(right);

            left.Add(Panel(WorkspacePanelCatalog.Scenes, "Scenes", 140));
            var sources = Panel(WorkspacePanelCatalog.Sources, "Sources / Models", 220);
            _sourcesStatus = new Label("Waiting for model state...");
            sources.Add(_sourcesStatus);
            left.Add(sources);
            center.Add(Panel(WorkspacePanelCatalog.Preview,
                "Preview — runtime viewport integration pending", 360));
            var inspector = Panel(WorkspacePanelCatalog.Inspector, "Inspector", 260);
            _inspectorStatus = new Label("Waiting for scene state...");
            inspector.Add(_inspectorStatus);
            right.Add(inspector);
            if (advancedWorkspace)
            {
                right.Add(Panel(WorkspacePanelCatalog.Materials, "Materials / Shaders", 140));
                right.Add(Panel(WorkspacePanelCatalog.Capabilities, "Modules", 120));
            }

            var bottomSplitter = Splitter(false, _bottomHeight, 100f, 400f, value =>
            {
                _bottomHeight = value;
                // The bottom element is assigned after the splitter is created.
                var target = _root.Q<VisualElement>("vcr-bottom");
                if (target != null) target.style.height = value;
            }, BottomHeightKey, true);
            bottomSplitter.style.display = _bottomVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Add(bottomSplitter);
            var bottom = new VisualElement { name = "vcr-bottom" };
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.height = _bottomHeight;
            bottom.style.display = _bottomVisible ? DisplayStyle.Flex : DisplayStyle.None;
            var tracking = Panel(WorkspacePanelCatalog.Tracking, "Tracking", 150);
            _trackingStatus = new Label("Waiting for runtime...");
            tracking.Add(_trackingStatus);
            bottom.Add(tracking);
            bottom.Add(Panel(WorkspacePanelCatalog.Actions, "Actions", 150));
            bottom.Add(Panel(WorkspacePanelCatalog.Events, "Log / Events", 150));
            _root.Add(bottom);

            _status = new Label("Waiting for scene runtime status...");
            _status.style.color = new Color(0.75f, 0.8f, 0.85f);
            _status.style.paddingLeft = 12;
            _status.style.paddingTop = 5;
            _status.style.paddingBottom = 5;
            _root.Add(_status);
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
            PlayerPrefs.DeleteKey(RightWidthKey);
            PlayerPrefs.DeleteKey(BottomHeightKey);
            PlayerPrefs.DeleteKey(BottomVisibleKey);
            PlayerPrefs.Save();
            _leftWidth = 220f;
            _rightWidth = 260f;
            _bottomHeight = 150f;
            _bottomVisible = true;
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
