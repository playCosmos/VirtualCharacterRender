using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        // M1–M3: one registry for quick search and category browsing.
        // Never retain VisualElement references in the registry: Build()
        // destroys and recreates the entire U1 workspace.
        private readonly WorkspaceFeatureRegistry _featureRegistry =
            WorkspaceFeatureRegistry.CreateDefault();
        private readonly Dictionary<string, string> _featureTemporaryTabs =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Button> _featureResultButtons = new List<Button>();
        private List<WorkspaceFeature> _featureSearchResults = new List<WorkspaceFeature>();
        private VisualElement _featureSearchOverlay;
        private TextField _featureSearchInput;
        private ScrollView _featureSearchResultsHost;
        private Label _featureSearchStatus;
        private int _featureSearchSelectedIndex;
        private bool _featureSearchIsOpen;

        private void HandleWorkspaceFeatureShortcut(KeyDownEvent evt)
        {
            // Trickle-down sees Ctrl+K even if a nested TextField has focus.
            // Do not swallow ordinary typing or keyboard shortcuts elsewhere.
            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.K)
            {
                if (_featureSearchIsOpen) CloseWorkspaceFeatureSearch();
                else OpenWorkspaceFeatureSearch();
                evt.StopPropagation();
                evt.PreventDefault();
            }
            else if (_featureSearchIsOpen && evt.keyCode == KeyCode.Escape)
            {
                CloseWorkspaceFeatureSearch();
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        private void BuildWorkspaceFeatureSearchOverlay()
        {
            _featureSearchIsOpen = false;
            _featureSearchSelectedIndex = 0;
            _featureSearchResults = new List<WorkspaceFeature>();
            _featureResultButtons.Clear();

            var overlay = new VisualElement { name = "vcr-feature-search-overlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.flexDirection = FlexDirection.Column;
            overlay.style.alignItems = Align.Center;
            overlay.style.paddingTop = Mathf.Clamp(Screen.height * 0.08f, 24f, 64f);
            overlay.style.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 0.74f);
            overlay.style.display = DisplayStyle.None;
            overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == overlay) CloseWorkspaceFeatureSearch();
            });

            var card = new VisualElement { name = "vcr-feature-search-card" };
            card.style.width = Length.Percent(92);
            card.style.maxWidth = 690;
            card.style.maxHeight = Mathf.Max(185, Screen.height - 76);
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.backgroundColor = new Color(0.15f, 0.18f, 0.21f);
            var heading = new Label("Find a feature");
            heading.style.color = Color.white;
            heading.style.marginBottom = 6;
            card.Add(heading);

            var input = new TextField("Search features / 기능 검색")
                { name = "vcr-feature-search-input" };
            input.style.flexShrink = 0;
            input.style.color = new Color(0.10f, 0.14f, 0.18f);
            input.RegisterValueChangedCallback(evt =>
                RefreshWorkspaceFeatureSearchResults(evt.newValue));
            input.RegisterCallback<KeyDownEvent>(HandleWorkspaceFeatureSearchKeys,
                TrickleDown.TrickleDown);
            card.Add(input);

            var help = new Label("↑ ↓ select  ·  Enter navigate  ·  Esc close  ·  Ctrl+K search");
            help.style.color = new Color(0.75f, 0.80f, 0.86f);
            help.style.marginTop = 6;
            help.style.marginBottom = 8;
            card.Add(help);

            var results = new ScrollView(ScrollViewMode.Vertical)
                { name = "vcr-feature-search-results" };
            results.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            results.style.flexGrow = 1;
            results.style.flexShrink = 1;
            results.style.minHeight = 90;
            results.style.maxHeight = Mathf.Clamp(Screen.height * 0.63f, 130f, 470f);
            card.Add(results);
            var status = new Label("");
            status.style.color = new Color(0.75f, 0.80f, 0.86f);
            status.style.marginTop = 6;
            card.Add(status);
            overlay.Add(card);
            _workspaceRoot.Add(overlay);
            _featureSearchOverlay = overlay;
            _featureSearchInput = input;
            _featureSearchResultsHost = results;
            _featureSearchStatus = status;
        }

        private void OpenWorkspaceFeatureSearch()
        {
            if (_featureSearchOverlay == null || _featureSearchOverlay.panel == null)
                return;
            _featureSearchIsOpen = true;
            _featureSearchOverlay.style.display = DisplayStyle.Flex;
            _featureSearchInput.SetValueWithoutNotify("");
            RefreshWorkspaceFeatureSearchResults("");
            if (_guiProbeRequested)
            {
                var korean = _featureRegistry.Search("시야각", 3);
                var english = _featureRegistry.Search("fov", 3);
                var absent = _featureRegistry.Search("zqxyz_unknown_feature", 3);
                var valid = korean.Count > 0 && english.Count > 0 &&
                    korean[0].Id == "camera.fov" && english[0].Id == "camera.fov" &&
                    absent.Count == 0;
                Debug.Log("VCR U1 GUI probe: feature_search_registry_ready=" +
                    (valid ? "true" : "false") +
                    " indexed=" + _featureRegistry.All.Count, this);
                Debug.Log("VCR U1 GUI probe: feature_search_opened=true", this);
            }
            _featureSearchInput.schedule.Execute(() =>
            {
                if (_featureSearchIsOpen && _featureSearchInput.panel != null)
                    _featureSearchInput.Focus();
            }).ExecuteLater(1);
        }

        private void CloseWorkspaceFeatureSearch()
        {
            _featureSearchIsOpen = false;
            if (_featureSearchOverlay != null)
                _featureSearchOverlay.style.display = DisplayStyle.None;
        }

        private void HandleWorkspaceFeatureSearchKeys(KeyDownEvent evt)
        {
            if (!_featureSearchIsOpen) return;
            if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
            {
                if (_featureSearchResults.Count != 0)
                {
                    var direction = evt.keyCode == KeyCode.DownArrow ? 1 : -1;
                    _featureSearchSelectedIndex = Mathf.Clamp(
                        _featureSearchSelectedIndex + direction,
                        0, _featureSearchResults.Count - 1);
                    HighlightWorkspaceFeatureResult();
                    if (_featureSearchSelectedIndex < _featureResultButtons.Count)
                        _featureSearchResultsHost.ScrollTo(
                            _featureResultButtons[_featureSearchSelectedIndex]);
                }
                evt.StopPropagation();
                evt.PreventDefault();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (_featureSearchSelectedIndex < _featureSearchResults.Count)
                    NavigateToWorkspaceFeature(_featureSearchResults[_featureSearchSelectedIndex]);
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        private void RefreshWorkspaceFeatureSearchResults(string query)
        {
            if (_featureSearchResultsHost == null) return;
            // Empty query IS the browsable feature index; filtered queries
            // show ranked results. No additional index database is needed.
            var showIndex = string.IsNullOrWhiteSpace(query);
            _featureSearchResults = _featureRegistry.Search(query, showIndex ? 128 : 18);
            _featureSearchSelectedIndex = 0;
            _featureResultButtons.Clear();
            _featureSearchResultsHost.Clear();
            var currentCategory = "";
            foreach (var feature in _featureSearchResults)
            {
                if (showIndex && feature.Category != currentCategory)
                {
                    currentCategory = feature.Category;
                    var category = new Label(currentCategory);
                    category.style.color = new Color(0.72f, 0.84f, 0.94f);
                    category.style.marginTop = 8;
                    category.style.marginBottom = 3;
                    _featureSearchResultsHost.Add(category);
                }
                var selectedFeature = feature;
                var item = new Button(() => NavigateToWorkspaceFeature(selectedFeature))
                {
                    text = feature.Title +
                        (feature.RequiresAdvanced ? "  · Advanced" : "") +
                        (feature.NativeSettingsCategory >= 0 ? "  · Settings" : ""),
                    name = "vcr-feature-result-" + feature.Id
                };
                item.style.unityTextAlign = TextAnchor.MiddleLeft;
                item.style.color = new Color(0.10f, 0.15f, 0.20f);
                item.style.marginBottom = 2;
                item.tooltip = feature.Category + "  ›  " + feature.Description;
                _featureSearchResultsHost.Add(item);
                _featureResultButtons.Add(item);
            }
            if (_featureSearchStatus != null)
                _featureSearchStatus.text = showIndex
                    ? "All features · " + _featureSearchResults.Count + " indexed"
                    : _featureSearchResults.Count == 0
                        ? "No matching features. Try a shorter name or keyword."
                        : _featureSearchResults.Count + " matching features";
            HighlightWorkspaceFeatureResult();
            if (_guiProbeRequested)
            {
                var probeQuery = query == null ? "" :
                    query.Trim().ToLowerInvariant();
                if (probeQuery == "fov" || probeQuery == "materials")
                    Debug.Log("VCR U1 GUI probe: feature_search_query_ready=true " +
                        "query=" + probeQuery + " first=" +
                        (_featureSearchResults.Count != 0
                            ? _featureSearchResults[0].Id : "none"), this);
            }
        }

        private void HighlightWorkspaceFeatureResult()
        {
            for (var i = 0; i < _featureResultButtons.Count; ++i)
                _featureResultButtons[i].style.backgroundColor =
                    i == _featureSearchSelectedIndex
                        ? new Color(0.56f, 0.78f, 0.94f)
                        : new Color(0.81f, 0.85f, 0.89f);
        }

        private static string DefaultDockForFeature(string panelId)
        {
            switch (panelId)
            {
                case WorkspacePanelCatalog.Scenes:
                case WorkspacePanelCatalog.Sources: return "left";
                case WorkspacePanelCatalog.Inspector:
                case WorkspacePanelCatalog.Materials:
                case WorkspacePanelCatalog.Capabilities: return "right";
                case WorkspacePanelCatalog.Tracking:
                case WorkspacePanelCatalog.Actions:
                case WorkspacePanelCatalog.Events: return "bottom";
                default: return null;
            }
        }

        private void NavigateToWorkspaceFeature(WorkspaceFeature feature)
        {
            if (feature == null) return;
            CloseWorkspaceFeatureSearch();
            if (feature.NativeSettingsCategory >= 0)
            {
                // Native Settings is a separate top-level window. In Editor
                // and unsupported platforms, retain the existing fallback.
                OpenWorkspaceNativeSettings();
                if (_workspaceNativeSettings != null &&
                    _workspaceNativeSettings.IsVisible)
                {
                    if (feature.NativeSettingsCategory == 2)
                        RebuildWorkspaceNativeTrackingSources();
                    if (feature.NativeSettingsCategory == 3)
                        RefreshWorkspaceOverlayStatus();
                    _workspaceNativeSettings.SelectCategory(feature.NativeSettingsCategory);
                }
                return;
            }
            if (feature.RequiresAdvanced && !advancedWorkspace)
                advancedWorkspace = true; // Session-only; do not alter PlayerPrefs.

            if (!string.IsNullOrEmpty(feature.PanelId))
            {
                var defaultDock = DefaultDockForFeature(feature.PanelId);
                if (defaultDock != null)
                {
                    var dock = PlayerPrefs.GetString(
                        "VCR.UI.Workspace.Dock." + feature.PanelId + ".v1", defaultDock);
                    if (dock != "left" && dock != "right" && dock != "bottom")
                        dock = defaultDock;
                    if (dock == "left") _leftVisible = true;
                    if (dock == "right") _rightVisible = true;
                    if (dock == "bottom") _bottomVisible = true;
                    // Transient selection: do not change persisted ActiveTab
                    // or the operator's dock layout when searching.
                    _featureTemporaryTabs[dock] = "vcr-panel-" + feature.PanelId;
                }
                // Rebuild once to handle mode, hidden docks and auto-tabs
                // consistently; existing Build() preserves editor drafts.
                Build();
            }

            var targetName = !string.IsNullOrEmpty(feature.TargetName)
                ? feature.TargetName
                : !string.IsNullOrEmpty(feature.PanelId)
                    ? "vcr-panel-" + feature.PanelId : null;
            if (targetName == null) return;
            var target = _workspaceRoot.Q<VisualElement>(targetName);
            if (target == null)
            {
                // A removed optional capability may no longer have a visual
                // target: don't report a successful focus or mutate the layout.
                Debug.LogWarning("Feature search target missing: " + feature.Id, this);
                return;
            }
            target.schedule.Execute(() =>
            {
                if (target.panel == null) return;
                if (_guiProbeRequested)
                {
                    Debug.Log("VCR U1 GUI probe: feature_search_navigated=true id=" +
                        feature.Id + " target=" + targetName +
                        " mode=" + (advancedWorkspace ? "advanced" : "live"), this);
                    if (feature.Id == "materials")
                    {
                        var visible = target.resolvedStyle.display != DisplayStyle.None &&
                            target.worldBound.width > 120 && target.worldBound.height > 40;
                        var dockVisible = _rightDock != null &&
                            _rightDock.resolvedStyle.display != DisplayStyle.None &&
                            _rightDock.worldBound.width > 120;
                        Debug.Log("VCR U1 GUI probe: feature_search_panel_visible=true " +
                            "id=materials target_visible=" +
                            (visible ? "true" : "false") + " dock_visible=" +
                            (dockVisible ? "true" : "false") +
                            " mode=" + (advancedWorkspace ? "advanced" : "live"), this);
                    }
                    if (!string.IsNullOrEmpty(_guiProbeScreenshotPath))
                    {
                        var directory = System.IO.Path.GetDirectoryName(
                            _guiProbeScreenshotPath);
                        if (!string.IsNullOrEmpty(directory))
                            StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                                System.IO.Path.Combine(directory,
                                    feature.Id == "materials"
                                        ? "unity-feature-materials.png"
                                        : "unity-feature-searched.png")));
                    }
                }
                var scroll = target.GetFirstAncestorOfType<ScrollView>();
                if (scroll != null) scroll.ScrollTo(target);
                target.focusable = true;
                target.Focus();
                var previousWidth = target.style.borderLeftWidth;
                var previousColor = target.style.borderLeftColor;
                target.style.borderLeftWidth = 3;
                target.style.borderLeftColor = new Color(0.32f, 0.77f, 0.99f);
                target.schedule.Execute(() =>
                {
                    if (target.panel == null) return;
                    target.style.borderLeftWidth = previousWidth;
                    target.style.borderLeftColor = previousColor;
                }).ExecuteLater(1600);
            }).ExecuteLater(90);
        }
    }
}
