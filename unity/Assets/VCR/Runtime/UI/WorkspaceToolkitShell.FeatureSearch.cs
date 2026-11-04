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
        // Remember only the current modal session's focus, not a persisted
        // feature target. A dock rebuild invalidates this reference.
        private VisualElement _featureSearchPreviousFocus;
        private bool _featureSearchProbeReturnToLive;
        private bool _featureSearchProbeHiddenReturnToLive;

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
                if (_guiProbeRequested)
                    Debug.Log("VCR U1 GUI probe: feature_search_esc_closed=true", this);
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        private void BuildWorkspaceFeatureSearchOverlay()
        {
            _featureSearchIsOpen = false;
            _featureSearchPreviousFocus = null;
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
            _featureSearchPreviousFocus =
                _workspaceRoot.panel.focusController.focusedElement as VisualElement;
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
            // Avoid leaving keyboard input on an invisible TextField after Esc,
            // or the operator may appear to lose keyboard shortcuts.
            if (_featureSearchInput != null)
                _featureSearchInput.Blur();
            var previous = _featureSearchPreviousFocus;
            _featureSearchPreviousFocus = null;
            if (previous != null && previous.panel != null)
                previous.Focus();
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
                if (probeQuery == "fov" || probeQuery == "materials" ||
                    probeQuery == "scene management")
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

        // Clear transient search tabs and reveal-only visibility on explicit
        // layout actions without modifying persisted preferences.
        private void DiscardTransientFeatureSearchNavigation()
        {
            _featureTemporaryTabs.Clear();
            _leftVisible = PlayerPrefs.GetInt(LeftVisibleKey, 1) != 0;
            _rightVisible = PlayerPrefs.GetInt(RightVisibleKey, 1) != 0;
            _bottomVisible = PlayerPrefs.GetInt(BottomVisibleKey, 1) != 0;
        }

        private void NavigateToWorkspaceFeature(WorkspaceFeature feature)
        {
            if (feature == null) return;
            var savedLayoutBeforeSearch = _guiProbeRequested
                ? CaptureLayoutSignature() : null;
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
            var autoAdvanced = feature.RequiresAdvanced && !advancedWorkspace;
            if (autoAdvanced)
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
                if (_guiProbeRequested &&
                    (feature.Id == "materials" || feature.Id == "scene.list"))
                {
                    var layoutPreserved = string.Equals(
                        savedLayoutBeforeSearch, CaptureLayoutSignature(),
                        StringComparison.Ordinal);
                    Debug.Log("VCR U1 GUI probe: feature_search_layout_preserved=" +
                        (layoutPreserved ? "true" : "false") +
                        " id=" + feature.Id, this);
                }
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
                    if (autoAdvanced)
                    {
                        var persisted = PlayerPrefs.GetInt(WorkspaceModeKey, -1);
                        Debug.Log("VCR U1 GUI probe: feature_search_mode_preserved=" +
                            (persisted == 0 ? "true" : "false") +
                            " id=" + feature.Id +
                            " from=live to=advanced persisted=" +
                            (persisted == 0 ? "live" : persisted == 1 ? "advanced" : "missing"),
                            this);
                    }
                    Debug.Log("VCR U1 GUI probe: feature_search_navigated=true id=" +
                        feature.Id + " target=" + targetName +
                        " mode=" + (advancedWorkspace ? "advanced" : "live"), this);
                    if (feature.Id == "materials")
                    {
                        if (autoAdvanced)
                            StartCoroutine(ReportFeatureSearchHiddenLeftToggleTarget());
                        else
                            StartCoroutine(ReportFeatureSearchReturnToLiveTarget());
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
                        var contents = target.Q<ScrollView>("vcr-materials-content");
                        var viewport = contents != null ? contents.contentViewport : null;
                        var panelBounds = target.worldBound;
                        var clipped = viewport != null &&
                            viewport.resolvedStyle.display != DisplayStyle.None &&
                            viewport.worldBound.height >= 35f &&
                            viewport.worldBound.yMin >= panelBounds.yMin - 1f &&
                            viewport.worldBound.yMax <= panelBounds.yMax + 1f;
                        var aboveBottom = _bottomDock != null &&
                            panelBounds.yMax <= _bottomDock.worldBound.yMin + 1f;
                        Debug.Log("VCR U1 GUI probe: feature_search_materials_layout_ready=" +
                            (visible && dockVisible && clipped && aboveBottom
                                ? "true" : "false") +
                            " viewport_clipped=" + (clipped ? "true" : "false") +
                            " panel_above_bottom=" + (aboveBottom ? "true" : "false") +
                            " scroll_height=" + (viewport != null
                                ? viewport.worldBound.height.ToString("0.##",
                                    System.Globalization.CultureInfo.InvariantCulture)
                                : "0"), this);
                    }
                    if (feature.Id == "scene.list")
                    {
                        var visible = target.resolvedStyle.display != DisplayStyle.None &&
                            target.worldBound.width > 120f &&
                            target.worldBound.height > 40f &&
                            _leftDock != null &&
                            _leftDock.resolvedStyle.display == DisplayStyle.Flex &&
                            _leftDock.worldBound.width > 120f;
                        var persistedHidden = PlayerPrefs.GetInt(LeftVisibleKey, 1) == 0;
                        Debug.Log("VCR U1 GUI probe: feature_search_hidden_left_revealed=" +
                            (visible && persistedHidden ? "true" : "false") +
                            " visual_visible=" + (visible ? "true" : "false") +
                            " persisted_hidden=" + (persistedHidden ? "true" : "false"),
                            this);
                        StartCoroutine(ReportFeatureSearchReturnToLiveTarget(true));
                    }
                    if (!string.IsNullOrEmpty(_guiProbeScreenshotPath))
                    {
                        var directory = System.IO.Path.GetDirectoryName(
                            _guiProbeScreenshotPath);
                        if (!string.IsNullOrEmpty(directory))
                            StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                                System.IO.Path.Combine(directory,
                                    feature.Id == "materials"
                                        ? autoAdvanced
                                            ? "unity-feature-materials-from-live.png"
                                            : "unity-feature-materials.png"
                                        : feature.Id == "scene.list"
                                            ? "unity-feature-scenes-revealed.png"
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
        private System.Collections.IEnumerator ReportFeatureSearchReturnToLiveTarget(
            bool afterHiddenDock = false)
        {
            yield return new WaitForEndOfFrame();
            if (_workspaceRoot == null || _workspaceRoot.panel == null ||
                !advancedWorkspace) yield break;
            var toggle = _workspaceRoot.Q<Button>("vcr-header-workspace");
            if (!WorkspaceButtonReceivesPointer(toggle))
            {
                Debug.Log("VCR U1 GUI probe: feature_search_workspace_target_rejected=true", this);
                yield break;
            }
            var center = toggle.worldBound.center;
            var root = _workspaceRoot.worldBound;
            if (afterHiddenDock)
                _featureSearchProbeHiddenReturnToLive = true;
            else
                _featureSearchProbeReturnToLive = true;
            Debug.Log("VCR U1 GUI probe: feature_search_workspace_target_ready=true " +
                "x=" + center.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " y=" + center.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_w=" + root.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_h=" + root.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " current_mode=advanced", this);
        }
        private System.Collections.IEnumerator ReportFeatureSearchHiddenDockReset()
        {
            yield return new WaitForEndOfFrame();
            var visualHidden = _leftDock != null && !_leftVisible &&
                _leftDock.resolvedStyle.display == DisplayStyle.None;
            var persistedHidden = PlayerPrefs.GetInt(LeftVisibleKey, 1) == 0;
            var clearedTabs = _featureTemporaryTabs.Count == 0;
            var live = !advancedWorkspace;
            var persistedLive = PlayerPrefs.GetInt(WorkspaceModeKey, 1) == 0;
            Debug.Log("VCR U1 GUI probe: feature_search_hidden_reset=" +
                (visualHidden && persistedHidden && clearedTabs &&
                 live && persistedLive ? "true" : "false") +
                " visual_hidden=" + (visualHidden ? "true" : "false") +
                " persisted_hidden=" + (persistedHidden ? "true" : "false") +
                " temporary_tabs=" + _featureTemporaryTabs.Count +
                " mode=" + (live ? "live" : "advanced") +
                " persisted_mode=" + (persistedLive ? "live" : "advanced"), this);
            if (!visualHidden || !persistedHidden || !clearedTabs ||
                !live || !persistedLive) yield break;
            var move = _workspaceRoot.Q<Button>("vcr-dock-move-inspector");
            if (!WorkspaceButtonReceivesPointer(move))
            {
                Debug.Log("VCR U1 GUI probe: feature_search_inspector_move_target_rejected=true", this);
                yield break;
            }
            var center = move.worldBound.center;
            var root = _workspaceRoot.worldBound;
            Debug.Log("VCR U1 GUI probe: feature_search_inspector_move_target_ready=true " +
                "x=" + center.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " y=" + center.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_w=" + root.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_h=" + root.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " mode=live", this);
        }

        private System.Collections.IEnumerator ReportFeatureSearchInspectorMoved()
        {
            yield return new WaitForEndOfFrame();
            var panel = _workspaceRoot.Q<VisualElement>("vcr-panel-inspector");
            var panelVisible = panel != null && panel.parent == _bottomDock &&
                panel.resolvedStyle.display != DisplayStyle.None &&
                panel.worldBound.width > 120f && panel.worldBound.height > 32f;
            var persistedDock = PlayerPrefs.GetString(
                "VCR.UI.Workspace.Dock.inspector.v1", "") == "bottom";
            var selectedTab = PlayerPrefs.GetString(
                "VCR.UI.Workspace.ActiveTab.bottom.v1", "") == "vcr-panel-inspector";
            var leftStillHidden = !_leftVisible && _leftDock != null &&
                _leftDock.resolvedStyle.display == DisplayStyle.None &&
                PlayerPrefs.GetInt(LeftVisibleKey, 1) == 0;
            // The old full-width empty right dock must not waste Preview space.
            // It remains a narrow drag-and-drop target, not an invisible dock.
            var rightRail = _rightDock != null && _rightVisible &&
                _rightDock.Q<Label>("vcr-empty-right-dock-drop-target") != null &&
                _rightDock.resolvedStyle.display != DisplayStyle.None &&
                _rightDock.worldBound.width >= 22f &&
                _rightDock.worldBound.width <= 32f;
            var preview = _workspaceRoot.Q<VisualElement>("vcr-panel-preview");
            var previewExpanded = preview != null &&
                preview.worldBound.width >= _workspaceRoot.worldBound.width * 0.75f;
            var passed = panelVisible && persistedDock && selectedTab &&
                _featureTemporaryTabs.Count == 0 && leftStillHidden &&
                rightRail && previewExpanded;
            Debug.Log("VCR U1 GUI probe: feature_search_inspector_moved=" +
                (passed ? "true" : "false") +
                " destination=bottom panel_visible=" + (panelVisible ? "true" : "false") +
                " persisted_dock=" + (persistedDock ? "true" : "false") +
                " selected_tab=" + (selectedTab ? "true" : "false") +
                " temporary_tabs=" + _featureTemporaryTabs.Count +
                " hidden_left_preserved=" + (leftStillHidden ? "true" : "false") +
                " empty_right_rail=" + (rightRail ? "true" : "false") +
                " preview_expanded=" + (previewExpanded ? "true" : "false"), this);
            if (!passed) yield break;
            var trackingTab = _workspaceRoot.Q<Button>("vcr-tab-tracking");
            if (!WorkspaceButtonReceivesPointer(trackingTab))
            {
                Debug.Log("VCR U1 GUI probe: feature_search_tracking_tab_target_rejected=true", this);
                yield break;
            }
            var center = trackingTab.worldBound.center;
            var root = _workspaceRoot.worldBound;
            Debug.Log("VCR U1 GUI probe: feature_search_tracking_tab_target_ready=true " +
                "x=" + center.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " y=" + center.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_w=" + root.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_h=" + root.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " mode=live", this);
            if (string.IsNullOrEmpty(_guiProbeScreenshotPath)) yield break;
            var directory = System.IO.Path.GetDirectoryName(_guiProbeScreenshotPath);
            if (!string.IsNullOrEmpty(directory))
                StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                    System.IO.Path.Combine(directory, "unity-feature-inspector-moved.png")));
        }

        private System.Collections.IEnumerator ReportFeatureSearchTrackingTabClicked()
        {
            yield return new WaitForEndOfFrame();
            var tracking = _workspaceRoot.Q<VisualElement>("vcr-panel-tracking");
            var inspector = _workspaceRoot.Q<VisualElement>("vcr-panel-inspector");
            var visible = tracking != null && tracking.parent == _bottomDock &&
                tracking.resolvedStyle.display != DisplayStyle.None &&
                tracking.worldBound.width > 120f && tracking.worldBound.height > 32f;
            var inspectorHidden = inspector != null &&
                inspector.resolvedStyle.display == DisplayStyle.None;
            var persisted = PlayerPrefs.GetString(
                "VCR.UI.Workspace.ActiveTab.bottom.v1", "") == "vcr-panel-tracking";
            var hiddenLeft = !_leftVisible && _leftDock != null &&
                _leftDock.resolvedStyle.display == DisplayStyle.None &&
                PlayerPrefs.GetInt(LeftVisibleKey, 1) == 0;
            var passed = visible && inspectorHidden && persisted && hiddenLeft &&
                _featureTemporaryTabs.Count == 0;
            Debug.Log("VCR U1 GUI probe: feature_search_tracking_tab_clicked=" +
                (passed ? "true" : "false") +
                " visible=" + (visible ? "true" : "false") +
                " inspector_hidden=" + (inspectorHidden ? "true" : "false") +
                " persisted=" + (persisted ? "true" : "false") +
                " temporary_tabs=" + _featureTemporaryTabs.Count +
                " hidden_left_preserved=" + (hiddenLeft ? "true" : "false"), this);
            if (!passed) yield break;
            // The hidden Inspector body still has a visible, draggable tab.
            // The right rail is narrow but must accept real pointer drops.
            var inspectorGrip = _workspaceRoot.Q<Label>("vcr-tab-drag-inspector");
            var picked = inspectorGrip != null && inspectorGrip.panel != null
                ? inspectorGrip.panel.Pick(inspectorGrip.worldBound.center) : null;
            while (picked != null && picked != inspectorGrip)
                picked = picked.parent;
            var gripReady = picked == inspectorGrip;
            var rightRail = _rightDock != null && _rightVisible &&
                _rightDock.Q<Label>("vcr-empty-right-dock-drop-target") != null &&
                _rightDock.worldBound.width >= 22f &&
                _rightDock.worldBound.width <= 32f;
            if (!gripReady || !rightRail)
            {
                Debug.Log("VCR U1 GUI probe: feature_search_inspector_drag_target_rejected=true", this);
                yield break;
            }
            var source = inspectorGrip.worldBound.center;
            var destination = _rightDock.worldBound.center;
            var root = _workspaceRoot.worldBound;
            Debug.Log("VCR U1 GUI probe: feature_search_inspector_drag_target_ready=true " +
                "sx=" + source.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " sy=" + source.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " dx=" + destination.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " dy=" + destination.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_w=" + root.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_h=" + root.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " from=bottom to=right", this);
            if (string.IsNullOrEmpty(_guiProbeScreenshotPath)) yield break;
            var directory = System.IO.Path.GetDirectoryName(_guiProbeScreenshotPath);
            if (!string.IsNullOrEmpty(directory))
                StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                    System.IO.Path.Combine(directory, "unity-feature-tracking-tab.png")));
        }

        private System.Collections.IEnumerator ReportFeatureSearchInspectorReturnedToRight()
        {
            yield return new WaitForEndOfFrame();
            var inspector = _workspaceRoot.Q<VisualElement>("vcr-panel-inspector");
            var inspectorVisible = inspector != null && inspector.parent == _rightDock &&
                inspector.resolvedStyle.display != DisplayStyle.None &&
                inspector.worldBound.width > 120f && inspector.worldBound.height > 60f;
            var restoredWidth = _rightDock != null &&
                _rightDock.worldBound.width >= 160f &&
                _rightDock.Q<Label>("vcr-empty-right-dock-drop-target") == null;
            var persistedDock = PlayerPrefs.GetString(
                "VCR.UI.Workspace.Dock.inspector.v1", "") == "right";
            var selectedTab = PlayerPrefs.GetString(
                "VCR.UI.Workspace.ActiveTab.right.v1", "") == "vcr-panel-inspector";
            var trackingSelected = PlayerPrefs.GetString(
                "VCR.UI.Workspace.ActiveTab.bottom.v1", "") == "vcr-panel-tracking";
            var leftStillHidden = !_leftVisible && _leftDock != null &&
                _leftDock.resolvedStyle.display == DisplayStyle.None &&
                PlayerPrefs.GetInt(LeftVisibleKey, 1) == 0;
            var passed = inspectorVisible && restoredWidth && persistedDock &&
                selectedTab && trackingSelected && leftStillHidden &&
                _featureTemporaryTabs.Count == 0;
            Debug.Log("VCR U1 GUI probe: feature_search_inspector_drag_restored=" +
                (passed ? "true" : "false") +
                " inspector_visible=" + (inspectorVisible ? "true" : "false") +
                " right_width_restored=" + (restoredWidth ? "true" : "false") +
                " persisted_dock=" + (persistedDock ? "true" : "false") +
                " selected_tab=" + (selectedTab ? "true" : "false") +
                " tracking_selected=" + (trackingSelected ? "true" : "false") +
                " temporary_tabs=" + _featureTemporaryTabs.Count +
                " hidden_left_preserved=" + (leftStillHidden ? "true" : "false"), this);
            if (!passed || string.IsNullOrEmpty(_guiProbeScreenshotPath)) yield break;
            var directory = System.IO.Path.GetDirectoryName(_guiProbeScreenshotPath);
            if (!string.IsNullOrEmpty(directory))
                StartCoroutine(CaptureWorkspaceGuiScreenshotAtEndOfFrame(
                    System.IO.Path.Combine(directory, "unity-feature-inspector-drag-restored.png")));
        }

        private System.Collections.IEnumerator ReportFeatureSearchHiddenLeftToggleTarget()
        {
            yield return new WaitForEndOfFrame();
            if (_workspaceRoot == null || _workspaceRoot.panel == null ||
                !advancedWorkspace || !_leftVisible) yield break;
            var toggle = _workspaceRoot.Q<Button>("vcr-header-toggle-left");
            if (!WorkspaceButtonReceivesPointer(toggle))
            {
                Debug.Log("VCR U1 GUI probe: feature_search_hide_left_target_rejected=true",
                    this);
                yield break;
            }
            var center = toggle.worldBound.center;
            var bounds = _workspaceRoot.worldBound;
            Debug.Log("VCR U1 GUI probe: feature_search_hide_left_target_ready=true " +
                "x=" + center.x.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " y=" + center.y.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_w=" + bounds.width.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " root_h=" + bounds.height.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " mode=advanced", this);
        }

    }
}
