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

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
                return;

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
                return;
            }
            var state = sceneRuntime.Status;
            var path = sceneRuntime.CurrentCharacterPath;
            var modelName = string.IsNullOrWhiteSpace(path)
                ? "<none>" : System.IO.Path.GetFileName(path);
            _status.text = "Scene: " + state.State + " | Model: " + modelName +
                " | Loaded: " + state.HasCharacter;
        }

        private void OnDisable()
        {
            if (_root != null)
                _root.Clear();
            _root = null;
            _dockHost = null;
            _status = null;
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
            var title = new Label("VirtualCharacterRender  /  Live");
            title.style.flexGrow = 1;
            title.style.color = Color.white;
            header.Add(title);

            var toggle = new Button(() =>
            {
                advancedWorkspace = !advancedWorkspace;
                Build();
            }) { text = advancedWorkspace ? "Live workspace" : "Advanced workspace" };
            header.Add(toggle);
            _root.Add(header);

            _dockHost = new VisualElement();
            _dockHost.style.flexGrow = 1;
            _dockHost.style.flexDirection = FlexDirection.Row;
            _root.Add(_dockHost);

            var left = Column(220);
            var center = Column(0);
            center.style.flexGrow = 1;
            var right = Column(260);
            _dockHost.Add(left);
            _dockHost.Add(center);
            _dockHost.Add(right);

            left.Add(Panel(WorkspacePanelCatalog.Scenes, "Scenes", 140));
            left.Add(Panel(WorkspacePanelCatalog.Sources, "Sources / Models", 220));
            center.Add(Panel(WorkspacePanelCatalog.Preview,
                "Preview — runtime viewport integration pending", 360));
            right.Add(Panel(WorkspacePanelCatalog.Inspector, "Inspector", 260));
            if (advancedWorkspace)
            {
                right.Add(Panel(WorkspacePanelCatalog.Materials, "Materials / Shaders", 140));
                right.Add(Panel(WorkspacePanelCatalog.Capabilities, "Modules", 120));
            }

            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.height = 150;
            bottom.Add(Panel(WorkspacePanelCatalog.Tracking, "Tracking", 150));
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
