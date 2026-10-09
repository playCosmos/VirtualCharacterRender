using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VCR.Runtime.Rendering;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.UI
{
    public sealed partial class ApplicationUiController
    {
        private Font _dashboardFontMedium;
        private Font _dashboardFontSemiBold;
        private RectTransform _trackingCameraPrivacyPlaceholder;
        private RectTransform _dashboardPresetPanel;
        private RectTransform _dashboardPresetGrid;
        private Text _dashboardPresetEmptyText;
        private string _dashboardPresetSignature;
        private RectTransform _dashboardSettingsModal;
        private RectTransform _settingsCategoryHost;
        private RectTransform _settingsGeneralBody;
        private readonly Dictionary<string, RectTransform> _settingsPages =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _settingsSidebarButtons =
            new(StringComparer.OrdinalIgnoreCase);
        private RectTransform _settingsTrackingDevices;
        private InputField _settingsCharacterPathInput;

        private RectTransform _dashboardCharacterInspector;
        private Text _dashboardModelNameText;
        private Text _dashboardTrackingStatusText;
        private Text _dashboardTrackingRouteText;

        private const string TrackingInputsConfiguredKey =
            "VCR.TrackingInputs.Configured.v1";
        private const string TrackingInputEnabledKeyPrefix =
            "VCR.TrackingInputs.Enabled.v1.";
        private const string TrackingInputSettingKeyPrefix =
            "VCR.TrackingInputs.Setting.v1.";

        private RectTransform _dashboardTrackingSourceList;
        private RectTransform _dashboardTrackingSourcePicker;
        private Text _dashboardTrackingSourcePickerEmptyText;
        private Button _dashboardTrackingSourceAddButton;
        private string _dashboardTrackingSourceSignature;
        private bool _dashboardTrackingSourcePickerOpen;
        private bool _dashboardTrackingConfigurationLoaded;
        private readonly HashSet<string>
            _dashboardConfiguredTrackingSources =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Text>
            _dashboardTrackingSourceStatusTexts =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button>
            _dashboardTrackingSourceToggleButtons =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float>
            _dashboardTrackingSourceLastClickTimes =
                new(StringComparer.OrdinalIgnoreCase);

        private RectTransform _dashboardTrackingSourceSettingsModal;
        private Text _dashboardTrackingSourceSettingsTitle;
        private RectTransform _dashboardTrackingSourceSettingsBody;
        private string _dashboardTrackingSourceSettingsControlId;
        private readonly Dictionary<string, InputField>
            _dashboardTrackingSourceSettingsInputs =
                new(StringComparer.OrdinalIgnoreCase);

        private const string DashboardPanelVisibleKeyPrefix =
            "VCR.Dashboard.Panel.Visible.v1.";
        private const string DashboardPanelFloatingKeyPrefix =
            "VCR.Dashboard.Panel.Floating.v1.";

        private RectTransform _dashboardMenuBar;
        private RectTransform _dashboardMenuPopup;
        private string _dashboardOpenMenuId;
        private RectTransform _dashboardCameraPreviewContent;
        private readonly Dictionary<string, RectTransform>
            _dashboardPanels =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string>
            _dashboardPanelTitles =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int>
            _dashboardPanelHomeOrder =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string>
            _dashboardFloatingPanels =
                new(StringComparer.OrdinalIgnoreCase);
        private string _dashboardDraggingPanelId;

        private Text _dashboardEnvironmentStatusText;
        private Dropdown _dashboardResolutionDropdown;
        private Dropdown _dashboardFpsDropdown;
        private Button _dashboardOutputApplyButton;
        private bool _dashboardOutputSelectionDirty;
        private bool _dashboardOutputDropdownSyncing;
        private Button _dashboardBackgroundModeButton;
        private Button _dashboardTopmostButton;

        private void FinalizeMockupDashboard(
            RectTransform titleBar,
            RectTransform navigation,
            RectTransform inspector,
            DesktopWindowChromeController windowChrome)
        {
            _dashboardFontMedium =
                Resources.Load<Font>(
                    "Fonts/Pretendard-Medium") ??
                uiFont;
            _dashboardFontSemiBold =
                Resources.Load<Font>(
                    "Fonts/Pretendard-SemiBold") ??
                _dashboardFontMedium ??
                uiFont;

            UseNativeDesktopChrome(
                titleBar,
                windowChrome);
            BuildObsMenuBar(
                navigation);
            ApplyObsWorkspaceGeometry(
                navigation,
                inspector);
            BuildMockupViewportToolbar();
            BuildMockupInspector(
                inspector);
            _dashboardCharacterInspector = inspector;
            if (_dashboardCharacterInspector != null)
            {
                _dashboardCharacterInspector.gameObject.SetActive(false);
            }
            BuildMockupTrackingCard();
            BuildMockupMotionCard();
            BuildMockupControlCard();
            BuildMockupEnvironmentCard();
            BuildMockupOutputCard();
            BuildStandaloneCameraPreviewCard();
            ConfigureObsDockablePanels();
            BuildSettingsModal();
            BuildTrackingSourceSettingsModal();
            ApplyPretendardTypography();
        }

        private void StyleMockupTitleBar(
            RectTransform titleBar)
        {
            if (titleBar == null)
            {
                return;
            }

            var mark =
                titleBar.Find(
                    "Application Mark");
            if (mark != null)
            {
                var image =
                    mark.GetComponent<Image>();
                if (image != null)
                {
                    image.color =
                        new Color(
                            0.78f,
                            0.26f,
                            0.78f,
                            1f);
                }

                var label =
                    mark.GetComponentInChildren<Text>(
                        true);
                if (label != null)
                {
                    label.text = "A";
                    label.font =
                        _dashboardFontSemiBold;
                    label.fontStyle =
                        FontStyle.Normal;
                }
            }

            var title =
                titleBar.Find(
                    "Application Title")
                    ?.GetComponent<Text>();
            if (title != null)
            {
                title.font =
                    _dashboardFontSemiBold;
                title.fontStyle =
                    FontStyle.Normal;
            }
        }

        private void BuildMockupViewportToolbar()
        {
            if (_renderViewportFrame == null)
            {
                return;
            }

            var oldLabel =
                _renderViewportFrame.Find(
                    "Render Viewport Label");
            if (oldLabel != null)
            {
                oldLabel.gameObject.SetActive(
                    false);
            }

            var toolbar =
                CreateRect(
                    "Viewport Toolbar",
                    _renderViewportFrame);
            toolbar.anchorMin =
                new Vector2(0f, 1f);
            toolbar.anchorMax =
                new Vector2(1f, 1f);
            toolbar.pivot =
                new Vector2(0.5f, 1f);
            toolbar.offsetMin =
                new Vector2(8f, -48f);
            toolbar.offsetMax =
                new Vector2(-8f, -8f);

            var toolbarImage =
                toolbar.gameObject
                    .AddComponent<Image>();
            toolbarImage.color =
                new Color(
                    0.035f,
                    0.045f,
                    0.06f,
                    0.92f);
            toolbarImage.raycastTarget =
                false;

            var left =
                CreateRect(
                    "Viewport Toolbar Left",
                    toolbar);
            left.anchorMin =
                new Vector2(0f, 0f);
            left.anchorMax =
                new Vector2(0.72f, 1f);
            left.offsetMin =
                new Vector2(8f, 4f);
            left.offsetMax =
                new Vector2(-4f, -4f);

            var leftLayout =
                left.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            leftLayout.spacing = 12f;
            leftLayout.childAlignment =
                TextAnchor.MiddleLeft;
            leftLayout.childControlWidth = true;
            leftLayout.childControlHeight = true;
            leftLayout.childForceExpandWidth = false;
            leftLayout.childForceExpandHeight = true;

            AddToolbarLabel(
                left,
                "미리보기",
                52f);
            AddToolbarLabel(
                left,
                "16:9",
                42f);
            AddToolbarLabel(
                left,
                "카메라 뷰",
                68f);

        }

        private void AddToolbarLabel(
            Transform parent,
            string label,
            float width)
        {
            var text =
                CreateText(
                    label + " Toolbar Label",
                    parent,
                    12,
                    TextAnchor.MiddleCenter);
            text.text = label;
            text.color =
                new Color(
                    0.78f,
                    0.82f,
                    0.88f,
                    1f);
            text.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = width;
        }

        private void BuildMockupInspector(
            RectTransform inspector)
        {
            if (inspector == null ||
                _characterModelPanel == null)
            {
                return;
            }

            _characterModelPanel.offsetMin =
                new Vector2(
                    16f,
                    -252f);
            _characterModelPanel.offsetMax =
                new Vector2(
                    -16f,
                    -54f);

            var fileRow =
                _characterModelPanel.Find(
                    "Character File Row") as
                    RectTransform;
            if (fileRow != null)
            {
                fileRow.offsetMin =
                    new Vector2(
                        126f,
                        74f);
                fileRow.offsetMax =
                    new Vector2(
                        -10f,
                        112f);
            }

            var loadRow =
                _characterModelPanel.Find(
                    "Character Load Row") as
                    RectTransform;
            if (loadRow != null)
            {
                loadRow.offsetMin =
                    new Vector2(
                        126f,
                        22f);
                loadRow.offsetMax =
                    new Vector2(
                        -10f,
                        62f);
            }

            var thumbnail =
                CreateRect(
                    "VRM Thumbnail",
                    _characterModelPanel);
            thumbnail.anchorMin =
                new Vector2(0f, 0f);
            thumbnail.anchorMax =
                new Vector2(0f, 1f);
            thumbnail.offsetMin =
                new Vector2(
                    10f,
                    18f);
            thumbnail.offsetMax =
                new Vector2(
                    116f,
                    -38f);

            var thumbImage =
                thumbnail.gameObject
                    .AddComponent<Image>();
            thumbImage.color =
                new Color(
                    0.11f,
                    0.13f,
                    0.17f,
                    1f);

            var thumbOutline =
                thumbnail.gameObject
                    .AddComponent<Outline>();
            thumbOutline.effectColor =
                new Color(
                    0.34f,
                    0.42f,
                    0.54f,
                    1f);
            thumbOutline.effectDistance =
                new Vector2(1f, -1f);

            var thumbText =
                CreateText(
                    "VRM Thumbnail Label",
                    thumbnail,
                    22,
                    TextAnchor.MiddleCenter);
            thumbText.text =
                "VRM";
            thumbText.font =
                _dashboardFontSemiBold;
            thumbText.color =
                new Color(
                    0.70f,
                    0.79f,
                    0.95f,
                    1f);
            Stretch(
                thumbText.rectTransform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            _dashboardModelNameText =
                CreateText(
                    "Dashboard Model Name",
                    _characterModelPanel,
                    13,
                    TextAnchor.MiddleLeft);
            _dashboardModelNameText.font =
                _dashboardFontSemiBold;
            _dashboardModelNameText.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            _dashboardModelNameText.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            _dashboardModelNameText.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            _dashboardModelNameText.rectTransform.offsetMin =
                new Vector2(
                    126f,
                    -72f);
            _dashboardModelNameText.rectTransform.offsetMax =
                new Vector2(
                    -10f,
                    -40f);
            _dashboardModelNameText.text =
                "모델이 로드되지 않음";

            if (_inspectorSummaryPanel != null)
            {
                _inspectorSummaryPanel.offsetMin =
                    new Vector2(
                        16f,
                        168f);
                _inspectorSummaryPanel.offsetMax =
                    new Vector2(
                        -16f,
                        -262f);
            }

            _dashboardPresetPanel =
                CreateRect(
                    "Preset Gallery",
                    inspector);
            _dashboardPresetPanel.anchorMin =
                new Vector2(0f, 0f);
            _dashboardPresetPanel.anchorMax =
                new Vector2(1f, 0f);
            _dashboardPresetPanel.pivot =
                new Vector2(0.5f, 0f);
            _dashboardPresetPanel.offsetMin =
                new Vector2(
                    16f,
                    16f);
            _dashboardPresetPanel.offsetMax =
                new Vector2(
                    -16f,
                    158f);

            AddActionPanelBackground(
                _dashboardPresetPanel);

            var presetTitle =
                CreateText(
                    "Preset Gallery Title",
                    _dashboardPresetPanel,
                    13,
                    TextAnchor.MiddleLeft);
            presetTitle.text =
                "⚑ 프리셋";
            presetTitle.font =
                _dashboardFontSemiBold;
            presetTitle.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            presetTitle.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            presetTitle.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            presetTitle.rectTransform.offsetMin =
                new Vector2(
                    10f,
                    -30f);
            presetTitle.rectTransform.offsetMax =
                new Vector2(
                    -10f,
                    -6f);

            _dashboardPresetGrid =
                CreateRect(
                    "Preset Grid",
                    _dashboardPresetPanel);
            Stretch(
                _dashboardPresetGrid,
                Vector2.zero,
                Vector2.one,
                new Vector2(
                    10f,
                    8f),
                new Vector2(
                    -10f,
                    -36f));

            var grid =
                _dashboardPresetGrid.gameObject
                    .AddComponent<GridLayoutGroup>();
            grid.padding =
                new RectOffset(
                    0,
                    0,
                    0,
                    0);
            grid.spacing =
                new Vector2(
                    6f,
                    0f);
            grid.cellSize =
                new Vector2(
                    92f,
                    88f);
            grid.constraint =
                GridLayoutGroup.Constraint
                    .FixedRowCount;
            grid.constraintCount = 1;
            grid.childAlignment =
                TextAnchor.MiddleLeft;

            _dashboardPresetEmptyText =
                CreateText(
                    "Preset Empty State",
                    _dashboardPresetPanel,
                    12,
                    TextAnchor.MiddleCenter);
            _dashboardPresetEmptyText.text =
                "모델에 등록된 프리셋이 없습니다.";
            _dashboardPresetEmptyText.color =
                new Color(
                    0.58f,
                    0.64f,
                    0.72f,
                    1f);
            Stretch(
                _dashboardPresetEmptyText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(14f, 12f),
                new Vector2(-14f, -38f));

            RefreshDashboardPresetGallery();
        }

        private void AddPresetTile(
            Transform parent,
            string label,
            string presetId)
        {
            var button =
                CreateButton(
                    "◇\n" + label,
                    parent,
                    () =>
                        ApplyDashboardAppearancePreset(
                            presetId));

            if (button.targetGraphic is
                Image image)
            {
                image.color =
                    new Color(
                        0.09f,
                        0.11f,
                        0.14f,
                        1f);
            }
        }

        private void UseNativeDesktopChrome(
            RectTransform titleBar,
            DesktopWindowChromeController windowChrome)
        {
            windowChrome?.UseNativeChrome();

            if (titleBar != null)
            {
                titleBar.gameObject.SetActive(
                    false);
            }
        }

        private void BuildSettingsLauncher(
            RectTransform navigation)
        {
            if (navigation == null)
            {
                return;
            }

            var button =
                CreateButton(
                    "⚙  환경설정…",
                    navigation,
                    OpenSettingsModal);
            button.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 34f;

            var label =
                button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.alignment =
                    TextAnchor.MiddleLeft;
            }
        }

        private void BuildSettingsModal()
        {
            if (_root == null ||
                _dashboardSettingsModal != null)
            {
                return;
            }

            _dashboardSettingsModal =
                CreateRect(
                    "Settings Modal Overlay",
                    _root);
            Stretch(
                _dashboardSettingsModal,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            var blocker =
                _dashboardSettingsModal.gameObject
                    .AddComponent<Image>();
            blocker.color =
                new Color(
                    0.01f,
                    0.015f,
                    0.025f,
                    0.78f);
            blocker.raycastTarget = true;

            var panel =
                CreateRect(
                    "Settings Modal",
                    _dashboardSettingsModal);
            panel.anchorMin =
                new Vector2(0.5f, 0.5f);
            panel.anchorMax =
                new Vector2(0.5f, 0.5f);
            panel.pivot =
                new Vector2(0.5f, 0.5f);
            panel.sizeDelta =
                new Vector2(900f, 620f);

            var panelImage =
                panel.gameObject
                    .AddComponent<Image>();
            panelImage.color =
                new Color(
                    0.055f,
                    0.065f,
                    0.085f,
                    1f);
            var panelOutline =
                panel.gameObject
                    .AddComponent<Outline>();
            panelOutline.effectColor =
                new Color(
                    0.20f,
                    0.28f,
                    0.40f,
                    0.9f);
            panelOutline.effectDistance =
                new Vector2(1f, -1f);

            var title =
                CreateText(
                    "Settings Modal Title",
                    panel,
                    18,
                    TextAnchor.MiddleLeft);
            title.text = "환경설정";
            title.font =
                _dashboardFontSemiBold;
            title.rectTransform.anchorMin =
                new Vector2(0f, 1f);
            title.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            title.rectTransform.pivot =
                new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin =
                new Vector2(20f, -52f);
            title.rectTransform.offsetMax =
                new Vector2(-70f, -14f);

            var close =
                CreateButton(
                    "×",
                    panel,
                    CloseSettingsModal);
            close.GetComponent<RectTransform>()
                .anchorMin =
                    new Vector2(1f, 1f);
            close.GetComponent<RectTransform>()
                .anchorMax =
                    new Vector2(1f, 1f);
            close.GetComponent<RectTransform>()
                .pivot =
                    new Vector2(1f, 1f);
            close.GetComponent<RectTransform>()
                .anchoredPosition =
                    new Vector2(-14f, -14f);
            close.GetComponent<RectTransform>()
                .sizeDelta =
                    new Vector2(38f, 34f);

            var body =
                CreateRect(
                    "Settings Modal Body",
                    panel);
            Stretch(
                body,
                Vector2.zero,
                Vector2.one,
                new Vector2(20f, 20f),
                new Vector2(-20f, -66f));

            var categoryRow = CreateRect("Settings Sidebar", body);
            categoryRow.anchorMin = new Vector2(0f, 0f);
            categoryRow.anchorMax = new Vector2(0f, 1f);
            categoryRow.pivot = new Vector2(0f, 1f);
            categoryRow.offsetMin = new Vector2(0f, 0f);
            categoryRow.offsetMax = new Vector2(180f, 0f);
            var sidebarBackground = categoryRow.gameObject.AddComponent<Image>();
            sidebarBackground.color = new Color(0.11f, 0.13f, 0.17f, 1f);
            var sidebarLayout = categoryRow.gameObject.AddComponent<VerticalLayoutGroup>();
            sidebarLayout.padding = new RectOffset(8, 8, 8, 8);
            sidebarLayout.spacing = 6f;
            sidebarLayout.childAlignment = TextAnchor.UpperLeft;
            sidebarLayout.childControlWidth = true;
            sidebarLayout.childControlHeight = true;
            sidebarLayout.childForceExpandWidth = true;
            sidebarLayout.childForceExpandHeight = false;

            AddSettingsCategoryButton(categoryRow, "general", "일반");
            AddSettingsCategoryButton(categoryRow, "character", "캐릭터 / 모델");
            AddSettingsCategoryButton(categoryRow, "tracking", "트래킹");
            AddSettingsCategoryButton(categoryRow, "output", "출력");

            _settingsCategoryHost = CreateRect(
                "Settings Category Content", body);
            Stretch(_settingsCategoryHost, Vector2.zero, Vector2.one,
                new Vector2(192f, 0f), Vector2.zero);
            _settingsGeneralBody = CreateRect(
                "General Settings", _settingsCategoryHost);
            Stretch(_settingsGeneralBody, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            body = _settingsGeneralBody;

            var layout =
                body.gameObject
                    .AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var help =
                CreateText(
                    "Settings Help",
                    body,
                    12,
                    TextAnchor.MiddleLeft);
            help.text =
                "프로그램 전체 동작에 영향을 주는 항목만 모았습니다. 고급 진단/개발 도구는 왼쪽 ‘고급 도구’에서 확인합니다.";
            help.color =
                new Color(
                    0.62f,
                    0.68f,
                    0.76f,
                    1f);
            help.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 42f;

            var renderScaleRow =
                CreateDashboardRow(
                    body,
                    "Render Scale Settings Row",
                    38f);
            var renderScaleLabel =
                CreateText(
                    "Render Scale Label",
                    renderScaleRow,
                    12,
                    TextAnchor.MiddleLeft);
            renderScaleLabel.text =
                "렌더 스케일";
            renderScaleLabel.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;
            _settingsRenderScaleInput =
                CreateInputField(
                    "Settings Modal Render Scale",
                    renderScaleRow,
                    "0.5 ~ 2.0");
            _settingsRenderScaleInput.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;
            _settingsApplyRenderScaleButton =
                CreateButton(
                    "적용",
                    renderScaleRow,
                    ApplySettingsRenderScale);
            _settingsApplyRenderScaleButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 72f;

            var fpsRow =
                CreateDashboardRow(
                    body,
                    "FPS Settings Row",
                    38f);
            var fpsLabel =
                CreateText(
                    "FPS Label",
                    fpsRow,
                    12,
                    TextAnchor.MiddleLeft);
            fpsLabel.text =
                "목표 프레임";
            fpsLabel.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 110f;
            _settingsFpsInput =
                CreateInputField(
                    "Settings Modal FPS",
                    fpsRow,
                    "30 ~ 240");
            _settingsFpsInput.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;
            _settingsApplyFpsButton =
                CreateButton(
                    "적용",
                    fpsRow,
                    ApplySettingsFps);
            _settingsApplyFpsButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 72f;

            _settingsVsyncButton =
                CreateButton(
                    "VSync",
                    body,
                    ToggleSettingsVsync);
            AddPreferredHeight(
                _settingsVsyncButton.gameObject,
                36f);

            _settingsRunInBackgroundButton =
                CreateButton(
                    "백그라운드 실행",
                    body,
                    ToggleSettingsRunInBackground);
            AddPreferredHeight(
                _settingsRunInBackgroundButton.gameObject,
                36f);

            var persistenceRow =
                CreateDashboardRow(
                    body,
                    "Configuration Persistence Row",
                    38f);
            var load =
                CreateButton(
                    "저장된 설정 불러오기",
                    persistenceRow,
                    ShowProfileLoadUnavailable);
            load.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;
            var save =
                CreateButton(
                    "현재 설정 저장",
                    persistenceRow,
                    SaveConfiguration);
            save.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            BuildSettingsDetailPages();
            _dashboardSettingsModal.gameObject.SetActive(
                false);
        }

        private void AddSettingsCategoryButton(Transform parent,
            string category, string label)
        {
            var button = CreateButton(label, parent,
                () => ShowSettingsCategory(category));
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 164f;
            element.preferredHeight = 38f;
            _settingsSidebarButtons[category] = button;
        }

        private void BuildSettingsDetailPages()
        {
            AddSettingsDetailPage("character", "캐릭터 / 모델",
                "성공적으로 불러온 모델은 다음 실행에서 자동 복원됩니다. 모델 해제를 누르면 자동 복원 대상도 지워집니다.",
                "선택한 VRM 불러오기",
                ApplySettingsCharacterPath);

            var characterPage = _settingsPages["character"];
            var characterFileRow = CreateDashboardRow(
                characterPage, "Settings VRM Path Row", 42f);
            // Show the path before the load action, not beneath it.
            characterFileRow.SetSiblingIndex(2);
            _settingsCharacterPathInput = CreateInputField(
                "Settings VRM Path", characterFileRow, "VRM 파일 경로");
            _settingsCharacterPathInput.gameObject
                .AddComponent<LayoutElement>().flexibleWidth = 1f;
            AddSettingsAction(characterPage, "파일 선택…",
                BrowseCharacterFile);
            AddSettingsAction(characterPage, "현재 모델 다시 불러오기",
                ReloadCharacter);
            AddSettingsAction(characterPage, "모델 해제 및 자동 복원 해제",
                UnloadCharacter);

            AddSettingsDetailPage("tracking", "트래킹",
                "입력 장치별 상세 설정입니다. 퀵패널의 연결 상태 및 소스 전환과 별도로 관리합니다.",
                "사용 가능한 장치 다시 확인",
                RebuildSettingsTrackingDevices);

            AddSettingsDetailPage("output", "출력",
                "출력 프리셋은 퀵패널에서 빠르게 전환합니다. 창 표시 방식은 이 설정 페이지에서 제어합니다.",
                "항상 위 켜기 / 끄기",
                ToggleOverlayTopmost);

            var outputPage = _settingsPages["output"];
            AddSettingsAction(outputPage, "투명 배경 켜기 / 끄기",
                ToggleOverlayTransparent);
            AddSettingsAction(outputPage, "클릭 통과 켜기 / 끄기",
                ToggleOverlayClickThrough);
            var trackingPage = _settingsPages["tracking"];
            _settingsTrackingDevices = CreateRect(
                "Tracking Device Settings", trackingPage);
            var devicesLayout = _settingsTrackingDevices.gameObject
                .AddComponent<VerticalLayoutGroup>();
            devicesLayout.spacing = 5f;
            devicesLayout.childControlWidth = true;
            devicesLayout.childControlHeight = true;
            devicesLayout.childForceExpandWidth = true;
            devicesLayout.childForceExpandHeight = false;
            _settingsTrackingDevices.gameObject
                .AddComponent<LayoutElement>().preferredHeight = 260f;
            RebuildSettingsTrackingDevices();
        }

        private void AddSettingsDetailPage(string id, string title,
            string description, string actionLabel, Action action)
        {
            var page = CreateRect("Settings " + title, _settingsCategoryHost);
            Stretch(page, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var heading = CreateText(title + " Heading", page, 17,
                TextAnchor.MiddleLeft);
            heading.text = title;
            AddPreferredHeight(heading.gameObject, 36f);
            var help = CreateText(title + " Help", page, 13,
                TextAnchor.UpperLeft);
            help.text = description;
            AddPreferredHeight(help.gameObject, 70f);
            var button = CreateButton(actionLabel, page, action);
            AddPreferredHeight(button.gameObject, 38f);
            _settingsPages[id] = page;
            page.gameObject.SetActive(false);
        }

        private void ApplySettingsCharacterPath()
        {
            if (_settingsCharacterPathInput == null ||
                _characterPathInput == null)
            {
                return;
            }

            _characterPathInput.text =
                _settingsCharacterPathInput.text;
            LoadCharacterFromPath();
        }

        private void AddSettingsAction(
            Transform parent, string title, Action action)
        {
            var button = CreateButton(title, parent, action);
            AddPreferredHeight(button.gameObject, 38f);
        }

        private void RebuildSettingsTrackingDevices()
        {
            if (_settingsTrackingDevices == null)
            {
                return;
            }

            for (var index = _settingsTrackingDevices.childCount - 1;
                 index >= 0; index--)
            {
                Destroy(_settingsTrackingDevices.GetChild(index).gameObject);
            }

            var count = 0;
            foreach (var control in _trackingControls)
            {
                if (control == null ||
                    control is not ITrackingRuntimeConfigurable)
                {
                    continue;
                }

                var sourceId = control.ControlId;
                AddSettingsAction(_settingsTrackingDevices,
                    control.DisplayName + " 설정",
                    () => OpenDashboardTrackingSourceSettings(sourceId));
                count++;
            }

            if (count == 0)
            {
                var empty = CreateText("Tracking Empty",
                    _settingsTrackingDevices, 12,
                    TextAnchor.MiddleLeft);
                empty.text = "설정 가능한 트래킹 장치가 없습니다.";
                AddPreferredHeight(empty.gameObject, 30f);
            }
        }

        private void ShowSettingsCategory(string category)
        {
            if (_settingsGeneralBody == null)
            {
                return;
            }

            if (category == "tracking")
            {
                RebuildSettingsTrackingDevices();
            }
            else if (category == "character" &&
                     _settingsCharacterPathInput != null)
            {
                _settingsCharacterPathInput.SetTextWithoutNotify(
                    _characterPathInput != null
                        ? _characterPathInput.text
                        : PlayerPrefs.GetString(
                            "VCR.Character.LastSuccessfulPath.v1",
                            string.Empty));
            }
            _settingsGeneralBody.gameObject.SetActive(category == "general");
            foreach (var entry in _settingsSidebarButtons)
            {
                if (entry.Value != null &&
                    entry.Value.targetGraphic is Image image)
                {
                    image.color = string.Equals(entry.Key, category,
                        StringComparison.OrdinalIgnoreCase)
                        ? new Color(0.16f, 0.32f, 0.56f, 1f)
                        : new Color(0.12f, 0.15f, 0.20f, 1f);
                }
            }
            foreach (var entry in _settingsPages)
            {
                if (entry.Value != null)
                {
                    entry.Value.gameObject.SetActive(entry.Key == category);
                }
            }
        }

        private void ToggleDashboardCharacterInspector()
        {
            if (_dashboardCharacterInspector == null)
            {
                return;
            }

            var visible = !_dashboardCharacterInspector.gameObject.activeSelf;
            _dashboardCharacterInspector.gameObject.SetActive(visible);
            if (visible)
            {
                SelectSection(ApplicationUiSection.Character);
                _dashboardCharacterInspector.SetAsLastSibling();
            }

            RefreshAll();
        }

        private void ToggleDashboardPanelFromSettings(string panelId)
        {
            if (!_dashboardPanels.TryGetValue(panelId, out var panel) ||
                panel == null)
            {
                return;
            }

            SetDashboardPanelVisible(panelId, !panel.gameObject.activeSelf);
        }

        private void OpenSettingsModal()
        {
            if (_dashboardSettingsModal == null)
            {
                return;
            }

            _dashboardSettingsModal.gameObject.SetActive(
                true);
            _dashboardSettingsModal.SetAsLastSibling();
            ShowSettingsCategory("general");
            RefreshAll();
        }

        private void CloseSettingsModal()
        {
            if (_dashboardSettingsModal != null)
            {
                _dashboardSettingsModal.gameObject.SetActive(
                    false);
                RefreshAll();
            }
        }

        private void RefreshDashboardPresetGallery()
        {
            if (_dashboardPresetGrid == null)
            {
                return;
            }

            var presetIds =
                IsServiceAlive(_appearanceRuntime)
                    ? _appearanceRuntime.PresetIds
                    : null;

            var signature =
                presetIds == null ||
                presetIds.Count == 0
                    ? string.Empty
                    : string.Join(
                        "\u001f",
                        presetIds);

            if (string.Equals(
                    signature,
                    _dashboardPresetSignature,
                    StringComparison.Ordinal))
            {
                if (_dashboardPresetEmptyText != null)
                {
                    _dashboardPresetEmptyText.gameObject.SetActive(
                        string.IsNullOrEmpty(
                            signature));
                }
                return;
            }

            _dashboardPresetSignature =
                signature;

            for (var index =
                     _dashboardPresetGrid.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(
                    _dashboardPresetGrid
                        .GetChild(index)
                        .gameObject);
            }

            var hasPresets =
                presetIds != null &&
                presetIds.Count > 0;

            if (_dashboardPresetEmptyText != null)
            {
                _dashboardPresetEmptyText.gameObject.SetActive(
                    !hasPresets);
            }

            if (!hasPresets)
            {
                return;
            }

            for (var i = 0;
                 i < presetIds.Count;
                 i++)
            {
                var presetId =
                    presetIds[i];
                if (string.IsNullOrWhiteSpace(
                        presetId))
                {
                    continue;
                }

                AddPresetTile(
                    _dashboardPresetGrid,
                    presetId,
                    presetId);
            }
        }

        private void BuildObsMenuBar(
            RectTransform navigation)
        {
            if (_root == null ||
                _dashboardMenuBar != null)
            {
                return;
            }

            if (navigation != null)
            {
                navigation.gameObject.SetActive(
                    false);
            }

            _dashboardMenuBar =
                CreateRect(
                    "OBS Style Menu Bar",
                    _root);
            _dashboardMenuBar.anchorMin =
                new Vector2(0f, 1f);
            _dashboardMenuBar.anchorMax =
                new Vector2(1f, 1f);
            _dashboardMenuBar.pivot =
                new Vector2(0.5f, 1f);
            _dashboardMenuBar.offsetMin =
                new Vector2(0f, -36f);
            _dashboardMenuBar.offsetMax =
                Vector2.zero;

            var background =
                _dashboardMenuBar.gameObject
                    .AddComponent<Image>();
            background.color =
                new Color(
                    0.045f,
                    0.05f,
                    0.062f,
                    1f);

            var layout =
                _dashboardMenuBar.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            layout.padding =
                new RectOffset(
                    8,
                    8,
                    3,
                    3);
            layout.spacing = 2f;
            layout.childAlignment =
                TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            AddObsMenuButton(
                "파일",
                54f,
                () =>
                    OpenObsMenu(
                        "file",
                        8f));
            AddObsMenuButton(
                "보기",
                54f,
                () =>
                    OpenObsMenu(
                        "view",
                        64f));
            AddObsMenuButton(
                "패널",
                54f,
                () =>
                    OpenObsMenu(
                        "panels",
                        120f));
            AddObsMenuButton(
                "설정",
                54f,
                () =>
                    OpenObsMenu(
                        "settings",
                        176f));
            AddObsMenuButton(
                "도구",
                54f,
                () =>
                    OpenObsMenu(
                        "tools",
                        232f));
        }

        private void AddObsMenuButton(
            string label,
            float width,
            Action action)
        {
            var button =
                CreateButton(
                    label,
                    _dashboardMenuBar,
                    action);
            var layout =
                button.gameObject
                    .AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 28f;

            if (button.targetGraphic is
                Image image)
            {
                image.color =
                    new Color(
                        0.045f,
                        0.05f,
                        0.062f,
                        1f);
            }
        }

        private void OpenObsMenu(
            string menuId,
            float left)
        {
            if (_dashboardMenuPopup != null)
            {
                Destroy(
                    _dashboardMenuPopup.gameObject);
                _dashboardMenuPopup = null;

                if (string.Equals(
                        _dashboardOpenMenuId,
                        menuId,
                        StringComparison.Ordinal))
                {
                    _dashboardOpenMenuId = null;
                    return;
                }
            }

            _dashboardOpenMenuId = menuId;

            _dashboardMenuPopup =
                CreateRect(
                    "OBS Menu Popup " +
                    menuId,
                    _root);
            _dashboardMenuPopup.anchorMin =
                new Vector2(0f, 1f);
            _dashboardMenuPopup.anchorMax =
                new Vector2(0f, 1f);
            _dashboardMenuPopup.pivot =
                new Vector2(0f, 1f);
            _dashboardMenuPopup.anchoredPosition =
                new Vector2(
                    left,
                    -36f);
            _dashboardMenuPopup.sizeDelta =
                new Vector2(
                    menuId == "panels"
                        ? 260f
                        : 230f,
                    320f);
            _dashboardMenuPopup.SetAsLastSibling();

            var image =
                _dashboardMenuPopup.gameObject
                    .AddComponent<Image>();
            image.color =
                new Color(
                    0.055f,
                    0.06f,
                    0.075f,
                    1f);

            var outline =
                _dashboardMenuPopup.gameObject
                    .AddComponent<Outline>();
            outline.effectColor =
                new Color(
                    0.18f,
                    0.22f,
                    0.29f,
                    1f);
            outline.effectDistance =
                new Vector2(1f, -1f);

            var layout =
                _dashboardMenuPopup.gameObject
                    .AddComponent<VerticalLayoutGroup>();
            layout.padding =
                new RectOffset(
                    6,
                    6,
                    6,
                    6);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            switch (menuId)
            {
                case "file":
                    AddObsMenuItem(
                        "VRM 모델 불러오기…",
                        () =>
                        {
                            SelectSection(
                                ApplicationUiSection.Character);
                            BrowseCharacterFile();
                        });
                    AddObsMenuItem(
                        "현재 설정 저장",
                        SaveConfiguration);
                    AddObsMenuItem(
                        "종료",
                        () =>
                            UnityEngine.Application.Quit());
                    break;

                case "view":
                    AddObsMenuItem(
                        "캐릭터 인스펙터",
                        () =>
                            SelectSection(
                                ApplicationUiSection.Character));
                    AddObsMenuItem(
                        "카메라 미리보기 패널",
                        () =>
                            SetDashboardPanelVisible(
                                "camera",
                                true));
                    AddObsMenuItem(
                        "기본 도킹 배치로 복원",
                        RestoreDefaultDashboardPanelLayout);
                    break;

                case "panels":
                    AddObsPanelMenuItem(
                        "tracking",
                        "트래킹");
                    AddObsPanelMenuItem(
                        "motion",
                        "모션 & 표정");
                    AddObsPanelMenuItem(
                        "control",
                        "조작");
                    AddObsPanelMenuItem(
                        "environment",
                        "배경 & 카메라");
                    AddObsPanelMenuItem(
                        "output",
                        "출력");
                    AddObsPanelMenuItem(
                        "camera",
                        "카메라 미리보기");
                    break;

                case "settings":
                    AddObsMenuItem(
                        "환경설정…",
                        OpenSettingsModal);
                    break;

                case "tools":
                    AddObsMenuItem(
                        "재질 / 셰이더",
                        () =>
                            SelectSection(
                                ApplicationUiSection.MaterialShader));
                    AddObsMenuItem(
                        "이벤트",
                        () =>
                            SelectSection(
                                ApplicationUiSection.Events));
                    AddObsMenuItem(
                        "진단",
                        () =>
                            SelectSection(
                                ApplicationUiSection.Diagnostics));
                    break;
            }

            var popupLayout =
                _dashboardMenuPopup
                    .GetComponent<LayoutElement>();
            if (popupLayout == null)
            {
                popupLayout =
                    _dashboardMenuPopup.gameObject
                        .AddComponent<LayoutElement>();
            }
        }

        private void AddObsMenuItem(
            string label,
            Action action)
        {
            var button =
                CreateButton(
                    label,
                    _dashboardMenuPopup,
                    () =>
                    {
                        CloseObsMenu();
                        action?.Invoke();
                    });
            AddPreferredHeight(
                button.gameObject,
                30f);
            var text =
                button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.alignment =
                    TextAnchor.MiddleLeft;
                text.fontSize = 11;
            }
        }

        private void AddObsPanelMenuItem(
            string panelId,
            string title)
        {
            var panel =
                _dashboardPanels.TryGetValue(
                    panelId,
                    out var value)
                    ? value
                    : null;
            var visible =
                panel != null &&
                panel.gameObject.activeSelf;

            AddObsMenuItem(
                (visible
                    ? "✓  "
                    : "    ") +
                title,
                () =>
                    ToggleDashboardPanelVisibility(
                        panelId));
        }

        private void CloseObsMenu()
        {
            if (_dashboardMenuPopup != null)
            {
                Destroy(
                    _dashboardMenuPopup.gameObject);
                _dashboardMenuPopup = null;
            }

            _dashboardOpenMenuId = null;
        }

        private void ApplyObsWorkspaceGeometry(
            RectTransform navigation,
            RectTransform inspector)
        {
            if (navigation != null)
            {
                navigation.gameObject.SetActive(
                    false);
            }

            if (_contextActions != null)
            {
                _contextActions.gameObject.SetActive(
                    false);
            }

            if (_renderViewportFrame != null)
            {
                var min =
                    _renderViewportFrame.offsetMin;
                var max =
                    _renderViewportFrame.offsetMax;
                min.x = 8f;
                max.x = -8f;
                max.y = -44f;
                _renderViewportFrame.offsetMin =
                    min;
                _renderViewportFrame.offsetMax =
                    max;
            }

            if (inspector != null)
            {
                var max =
                    inspector.offsetMax;
                max.y = -44f;
                inspector.offsetMax =
                    max;
            }
        }

        private void BuildStandaloneCameraPreviewCard()
        {
            if (_bottomDashboard == null ||
                _trackingCameraPreviewPanel == null ||
                _dashboardCameraPreviewContent != null)
            {
                return;
            }

            CreateDashboardCard(
                _bottomDashboard,
                "카메라 미리보기",
                300f,
                out _dashboardCameraPreviewContent);

            SetDashboardVerticalLayout(
                _dashboardCameraPreviewContent);

            _trackingCameraPrivacyPlaceholder =
                CreateDashboardPanel(
                    _dashboardCameraPreviewContent,
                    "Camera Privacy Placeholder",
                    150f);

            var privacyText =
                CreateText(
                    "Camera Preview Privacy Message",
                    _trackingCameraPrivacyPlaceholder,
                    13,
                    TextAnchor.MiddleCenter);
            privacyText.text =
                "◉̸\n카메라 미리보기 숨김\n" +
                "<size=11><color=#A8B1C0>명시적으로 보기 버튼을 눌러야 영상이 표시됩니다.</color></size>";
            privacyText.supportRichText = true;
            Stretch(
                privacyText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 8f),
                new Vector2(-10f, -8f));

            _trackingCameraPreviewPanel.SetParent(
                _dashboardCameraPreviewContent,
                false);
            var previewLayout =
                _trackingCameraPreviewPanel
                    .GetComponent<LayoutElement>() ??
                _trackingCameraPreviewPanel
                    .gameObject
                    .AddComponent<LayoutElement>();
            previewLayout.preferredHeight = 150f;
            _trackingCameraPreviewPanel.gameObject.SetActive(
                false);

            _trackingCameraPreviewButton =
                CreateButton(
                    "카메라 미리보기 보기",
                    _dashboardCameraPreviewContent,
                    ToggleTrackingCameraPreview);
            AddPreferredHeight(
                _trackingCameraPreviewButton.gameObject,
                32f);
            SetPrimaryButtonStyle(
                _trackingCameraPreviewButton);
        }

        private void ConfigureObsDockablePanels()
        {
            _dashboardPanels.Clear();
            _dashboardPanelTitles.Clear();
            _dashboardPanelHomeOrder.Clear();
            _dashboardFloatingPanels.Clear();

            RegisterObsDockablePanel(
                "tracking",
                "트래킹",
                _trackingDashboardContent,
                defaultVisible: false);
            RegisterObsDockablePanel(
                "motion",
                "모션 & 표정",
                _motionDashboardContent,
                defaultVisible: false);
            RegisterObsDockablePanel(
                "control",
                "조작",
                _controlDashboardContent,
                defaultVisible: false);
            RegisterObsDockablePanel(
                "environment",
                "배경 & 카메라",
                _environmentDashboardContent,
                defaultVisible: false);
            RegisterObsDockablePanel(
                "output",
                "출력",
                _outputDashboardContent,
                defaultVisible: false);
            RegisterObsDockablePanel(
                "camera",
                "카메라 미리보기",
                _dashboardCameraPreviewContent,
                defaultVisible: false);

            RefreshDashboardDockHost();
        }

        private void RegisterObsDockablePanel(
            string panelId,
            string title,
            RectTransform content,
            bool defaultVisible)
        {
            var panel =
                content?.parent as
                    RectTransform;

            if (panel == null)
            {
                return;
            }

            _dashboardPanels[
                panelId] =
                    panel;
            _dashboardPanelTitles[
                panelId] =
                    title;
            _dashboardPanelHomeOrder[
                panelId] =
                    panel.GetSiblingIndex();

            var visibilityKey =
                DashboardPanelVisibleKeyPrefix +
                panelId;
            var visible =
                PlayerPrefs.HasKey(
                    visibilityKey)
                    ? PlayerPrefs.GetInt(
                          visibilityKey,
                          defaultVisible
                              ? 1
                              : 0) != 0
                    : defaultVisible;

            panel.gameObject.SetActive(
                visible);

            AddObsPanelChrome(
                panelId,
                panel);

            var shouldFloat =
                PlayerPrefs.GetInt(
                    DashboardPanelFloatingKeyPrefix +
                    panelId,
                    0) != 0;

            if (visible &&
                shouldFloat)
            {
                FloatDashboardPanel(
                    panelId,
                    null);
            }
        }

        private void AddObsPanelChrome(
            string panelId,
            RectTransform panel)
        {
            var dragHandle =
                CreateRect(
                    "Dock Drag Handle " +
                    panelId,
                    panel);
            dragHandle.anchorMin =
                new Vector2(0f, 1f);
            dragHandle.anchorMax =
                new Vector2(1f, 1f);
            dragHandle.pivot =
                new Vector2(0.5f, 1f);
            dragHandle.offsetMin =
                new Vector2(0f, -36f);
            dragHandle.offsetMax =
                new Vector2(-72f, 0f);

            var dragImage =
                dragHandle.gameObject
                    .AddComponent<Image>();
            dragImage.color =
                new Color(
                    0f,
                    0f,
                    0f,
                    0f);
            dragImage.raycastTarget = true;

            var trigger =
                dragHandle.gameObject
                    .AddComponent<EventTrigger>();

            AddObsDragTrigger(
                trigger,
                EventTriggerType.BeginDrag,
                data =>
                    BeginDashboardPanelDrag(
                        panelId,
                        data as PointerEventData));
            AddObsDragTrigger(
                trigger,
                EventTriggerType.Drag,
                data =>
                    DragDashboardPanel(
                        panelId,
                        data as PointerEventData));
            AddObsDragTrigger(
                trigger,
                EventTriggerType.EndDrag,
                data =>
                    EndDashboardPanelDrag(
                        panelId,
                        data as PointerEventData));

            var floatButton =
                CreateButton(
                    "◇",
                    panel,
                    () =>
                        ToggleDashboardPanelFloating(
                            panelId));
            var floatRect =
                floatButton.GetComponent<RectTransform>();
            floatRect.anchorMin =
                new Vector2(1f, 1f);
            floatRect.anchorMax =
                new Vector2(1f, 1f);
            floatRect.pivot =
                new Vector2(1f, 1f);
            floatRect.offsetMin =
                new Vector2(-68f, -32f);
            floatRect.offsetMax =
                new Vector2(-38f, -4f);

            var close =
                CreateButton(
                    "×",
                    panel,
                    () =>
                        SetDashboardPanelVisible(
                            panelId,
                            false));
            var closeRect =
                close.GetComponent<RectTransform>();
            closeRect.anchorMin =
                new Vector2(1f, 1f);
            closeRect.anchorMax =
                new Vector2(1f, 1f);
            closeRect.pivot =
                new Vector2(1f, 1f);
            closeRect.offsetMin =
                new Vector2(-34f, -32f);
            closeRect.offsetMax =
                new Vector2(-4f, -4f);
        }

        private static void AddObsDragTrigger(
            EventTrigger trigger,
            EventTriggerType type,
            Action<BaseEventData> callback)
        {
            var entry =
                new EventTrigger.Entry
                {
                    eventID = type
                };
            entry.callback.AddListener(
                data =>
                    callback?.Invoke(data));
            trigger.triggers.Add(
                entry);
        }

        private void BeginDashboardPanelDrag(
            string panelId,
            PointerEventData eventData)
        {
            if (eventData == null ||
                !_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null)
            {
                return;
            }

            _dashboardDraggingPanelId =
                panelId;

            if (!_dashboardFloatingPanels.Contains(
                    panelId))
            {
                FloatDashboardPanel(
                    panelId,
                    eventData.position);
            }

            DragDashboardPanel(
                panelId,
                eventData);
        }

        private void DragDashboardPanel(
            string panelId,
            PointerEventData eventData)
        {
            if (eventData == null ||
                !_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null ||
                !_dashboardFloatingPanels.Contains(
                    panelId))
            {
                return;
            }

            if (RectTransformUtility
                .ScreenPointToLocalPointInRectangle(
                    _root,
                    eventData.position,
                    _canvas != null
                        ? _canvas.worldCamera
                        : null,
                    out var local))
            {
                panel.anchoredPosition =
                    local;
            }
        }

        private void EndDashboardPanelDrag(
            string panelId,
            PointerEventData eventData)
        {
            _dashboardDraggingPanelId =
                null;

            if (eventData != null &&
                eventData.position.y <=
                    Screen.height * 0.34f)
            {
                DockDashboardPanel(
                    panelId,
                    eventData.position.x);
            }
        }

        private void ToggleDashboardPanelFloating(
            string panelId)
        {
            if (_dashboardFloatingPanels.Contains(
                    panelId))
            {
                DockDashboardPanel(
                    panelId,
                    null);
            }
            else
            {
                FloatDashboardPanel(
                    panelId,
                    null);
            }
        }

        private void FloatDashboardPanel(
            string panelId,
            Vector2? screenPosition)
        {
            if (!_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null ||
                _root == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();

            var size =
                panel.rect.size;
            if (size.x < 240f)
            {
                size.x = 320f;
            }
            if (size.y < 160f)
            {
                size.y = 280f;
            }

            panel.SetParent(
                _root,
                false);
            panel.SetAsLastSibling();
            panel.anchorMin =
                new Vector2(0.5f, 0.5f);
            panel.anchorMax =
                new Vector2(0.5f, 0.5f);
            panel.pivot =
                new Vector2(0.5f, 0.5f);
            panel.sizeDelta =
                new Vector2(
                    Mathf.Clamp(
                        size.x,
                        300f,
                        520f),
                    Mathf.Clamp(
                        size.y,
                        220f,
                        420f));

            var layout =
                panel.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.ignoreLayout = true;
            }

            if (screenPosition.HasValue &&
                RectTransformUtility
                    .ScreenPointToLocalPointInRectangle(
                        _root,
                        screenPosition.Value,
                        _canvas != null
                            ? _canvas.worldCamera
                            : null,
                        out var local))
            {
                panel.anchoredPosition =
                    local;
            }
            else
            {
                panel.anchoredPosition =
                    Vector2.zero;
            }

            _dashboardFloatingPanels.Add(
                panelId);
            PlayerPrefs.SetInt(
                DashboardPanelFloatingKeyPrefix +
                panelId,
                1);
            PlayerPrefs.Save();

            RefreshDashboardDockHost();
        }

        private void DockDashboardPanel(
            string panelId,
            float? screenX)
        {
            if (!_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null ||
                _bottomDashboard == null)
            {
                return;
            }

            panel.SetParent(
                _bottomDashboard,
                false);

            var layout =
                panel.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.ignoreLayout = false;
            }

            var order =
                _dashboardPanelHomeOrder.TryGetValue(
                    panelId,
                    out var home)
                    ? home
                    : _bottomDashboard.childCount - 1;

            if (screenX.HasValue)
            {
                var normalized =
                    Mathf.Clamp01(
                        screenX.Value /
                        Mathf.Max(
                            1f,
                            Screen.width));
                order =
                    Mathf.RoundToInt(
                        normalized *
                        Mathf.Max(
                            0,
                            _bottomDashboard.childCount - 1));
            }

            panel.SetSiblingIndex(
                Mathf.Clamp(
                    order,
                    0,
                    Mathf.Max(
                        0,
                        _bottomDashboard.childCount - 1)));

            _dashboardFloatingPanels.Remove(
                panelId);
            PlayerPrefs.SetInt(
                DashboardPanelFloatingKeyPrefix +
                panelId,
                0);
            PlayerPrefs.Save();

            RefreshDashboardDockHost();
        }

        private void ToggleDashboardPanelVisibility(
            string panelId)
        {
            if (!_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null)
            {
                return;
            }

            SetDashboardPanelVisible(
                panelId,
                !panel.gameObject.activeSelf);
        }

        private void SetDashboardPanelVisible(
            string panelId,
            bool visible)
        {
            if (!_dashboardPanels.TryGetValue(
                    panelId,
                    out var panel) ||
                panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(
                visible);
            PlayerPrefs.SetInt(
                DashboardPanelVisibleKeyPrefix +
                panelId,
                visible
                    ? 1
                    : 0);
            PlayerPrefs.Save();

            if (!visible &&
                string.Equals(
                    panelId,
                    "camera",
                    StringComparison.OrdinalIgnoreCase))
            {
                _trackingCameraPreviewRequested =
                    false;
                RefreshTrackingCameraPreviewPrivacy(
                    false);
            }

            RefreshDashboardDockHost();
        }

        private void RestoreDefaultDashboardPanelLayout()
        {
            foreach (var pair in
                     _dashboardPanels)
            {
                if (_dashboardFloatingPanels.Contains(
                        pair.Key))
                {
                    DockDashboardPanel(
                        pair.Key,
                        null);
                }

                var visible = false;

                pair.Value.gameObject.SetActive(
                    visible);
                PlayerPrefs.SetInt(
                    DashboardPanelVisibleKeyPrefix +
                    pair.Key,
                    visible
                        ? 1
                        : 0);
            }

            PlayerPrefs.Save();
            RefreshDashboardDockHost();
        }

        private void RefreshDashboardDockHost()
        {
            if (_bottomDashboard == null)
            {
                return;
            }

            var hasDockedVisible = false;

            foreach (var pair in
                     _dashboardPanels)
            {
                if (pair.Value != null &&
                    pair.Value.gameObject.activeSelf &&
                    pair.Value.parent ==
                        _bottomDashboard)
                {
                    hasDockedVisible = true;
                    break;
                }
            }

            if (_bottomDashboard.gameObject.activeSelf !=
                hasDockedVisible)
            {
                _bottomDashboard.gameObject.SetActive(
                    hasDockedVisible);
            }

            RefreshDashboardGeometry(
                _model.SelectedSection);
        }

        private float GetDashboardWorkspaceBottomInset()
        {
            return
                _bottomDashboard != null &&
                _bottomDashboard.gameObject.activeSelf
                    ? 318f
                    : 8f;
        }

        private void BuildMockupTrackingCard()
        {
            if (_trackingDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _trackingDashboardContent);

            HideDashboardLegacyControl(
                _trackingPreviousButton);
            HideDashboardLegacyControl(
                _trackingToggleButton);
            HideDashboardLegacyControl(
                _trackingRecoverButton);
            HideDashboardLegacyControl(
                _trackingNextButton);
            HideDashboardLegacyControl(
                _trackingCameraPreviewButton);

            var routePanel =
                CreateDashboardPanel(
                    _trackingDashboardContent,
                    "Tracking Route Summary",
                    58f);

            _dashboardTrackingRouteText =
                CreateText(
                    "Tracking Route Summary Text",
                    routePanel,
                    12,
                    TextAnchor.MiddleLeft);
            _dashboardTrackingRouteText.text =
                "<b>자동 혼합 (권장)</b>\n" +
                "<size=11><color=#9EABBC>얼굴·표정: ARKit 우선 / 손·손가락: Ultraleap 우선 / 상체: MediaPipe</color></size>";
            _dashboardTrackingRouteText.supportRichText =
                true;
            Stretch(
                _dashboardTrackingRouteText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12f, 8f),
                new Vector2(-12f, -8f));

            EnsureDashboardTrackingConfigurationLoaded();

            var sourceHeader =
                CreateDashboardRow(
                    _trackingDashboardContent,
                    "Tracking Input Source Header",
                    30f);

            var sourceHeaderLabel =
                CreateText(
                    "Tracking Input Source Header Label",
                    sourceHeader,
                    12,
                    TextAnchor.MiddleLeft);
            sourceHeaderLabel.text =
                "<b>입력 소스</b>";
            sourceHeaderLabel.supportRichText = true;
            sourceHeaderLabel.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            _dashboardTrackingSourceAddButton =
                CreateButton(
                    "+",
                    sourceHeader,
                    ToggleDashboardTrackingSourcePicker);
            _dashboardTrackingSourceAddButton.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 34f;

            _dashboardTrackingSourcePicker =
                CreateRect(
                    "Tracking Input Source Picker",
                    _trackingDashboardContent);
            var pickerLayoutElement =
                _dashboardTrackingSourcePicker.gameObject
                    .AddComponent<LayoutElement>();
            pickerLayoutElement.preferredHeight = 0f;

            var pickerLayout =
                _dashboardTrackingSourcePicker.gameObject
                    .AddComponent<VerticalLayoutGroup>();
            pickerLayout.spacing = 3f;
            pickerLayout.childControlHeight = true;
            pickerLayout.childForceExpandHeight = false;
            _dashboardTrackingSourcePicker.gameObject.SetActive(
                false);

            _dashboardTrackingSourceList =
                CreateRect(
                    "Tracking Input Sources",
                    _trackingDashboardContent);
            var sourceListLayoutElement =
                _dashboardTrackingSourceList.gameObject
                    .AddComponent<LayoutElement>();
            sourceListLayoutElement.preferredHeight = 34f;

            var sourceListLayout =
                _dashboardTrackingSourceList.gameObject
                    .AddComponent<VerticalLayoutGroup>();
            sourceListLayout.spacing = 3f;
            sourceListLayout.childControlHeight = true;
            sourceListLayout.childForceExpandHeight = false;

            RefreshDashboardTrackingSources();

            _dashboardTrackingStatusText =
                CreateText(
                    "Tracking Status",
                    _trackingDashboardContent,
                    12,
                    TextAnchor.MiddleLeft);
            _dashboardTrackingStatusText.text =
                "트래킹 상태   ● 비활성화";
            _dashboardTrackingStatusText.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 24f;

            var sourceHelp =
                CreateText(
                    "Tracking Source Help",
                    _trackingDashboardContent,
                    11,
                    TextAnchor.MiddleLeft);
            sourceHelp.text =
                "+ 버튼으로 필요한 입력 소스를 추가합니다. 여러 소스를 동시에 사용할 수 있으며, Ultraleap이 꺼지거나 손 입력을 잃으면 MediaPipe 손 추적으로 폴백합니다.";
            sourceHelp.color =
                new Color(
                    0.58f,
                    0.64f,
                    0.72f,
                    1f);
            sourceHelp.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 34f;
        }

        private void BuildMockupMotionCard()
        {
            if (_motionDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _motionDashboardContent);

            HideDashboardLegacyControl(
                _motionPoseWeightLabel);
            HideDashboardLegacyControl(
                _motionPoseWeightSlider);
            HideDashboardLegacyControl(
                _manualExpressionNameInput);
            HideDashboardLegacyControl(
                _manualExpressionValueInput);
            HideDashboardLegacyControl(
                _manualExpressionApplyButton);
            HideDashboardLegacyControl(
                _manualExpressionClearButton);
            HideDashboardLegacyControl(
                _manualExpressionClearAllButton);

            var tabs =
                CreateDashboardRow(
                    _motionDashboardContent,
                    "Motion Tabs",
                    34f);
            AddDashboardTab(
                tabs,
                "모션",
                true,
                () =>
                    SelectSection(
                        ApplicationUiSection.MotionExpression));
            AddDashboardTab(
                tabs,
                "표정",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Expression));

            AddDashboardEmptyState(
                _motionDashboardContent,
                "등록된 모션이 없습니다.",
                "실제 모션/애니메이션이 등록되면 이 영역에만 표시합니다. 예시 모션은 만들지 않습니다.",
                116f);
        }

        private void BuildMockupControlCard()
        {
            if (_controlDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _controlDashboardContent);

            AddDashboardEmptyState(
                _controlDashboardContent,
                "모델 조작값 대기 중",
                "모델에서 실제로 제어 가능한 파라미터가 확인된 뒤에만 슬라이더와 토글을 표시합니다.",
                118f);

            var help =
                CreateText(
                    "Control Availability Help",
                    _controlDashboardContent,
                    11,
                    TextAnchor.MiddleLeft);
            help.text =
                "현재 알파 빌드의 렌더링·트래킹·외형 런타임은 유지되며, 동작하지 않는 장식용 컨트롤만 숨깁니다.";
            help.color =
                new Color(
                    0.56f,
                    0.62f,
                    0.70f,
                    1f);
            help.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 44f;
        }

        private void BuildMockupEnvironmentCard()
        {
            if (_environmentDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _environmentDashboardContent);

            HideDashboardLegacyControl(
                _environmentStateInput);
            HideDashboardLegacyControl(
                _environmentTransitionModeButton);
            HideDashboardLegacyControl(
                _environmentTransitionDurationInput);
            HideDashboardLegacyControl(
                _environmentApplyStateButton);

            _dashboardEnvironmentStatusText =
                CreateText(
                    "Environment Runtime Status",
                    _environmentDashboardContent,
                    12,
                    TextAnchor.MiddleLeft);
            _dashboardEnvironmentStatusText.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 34f;

            AddDashboardEmptyState(
                _environmentDashboardContent,
                "등록된 배경 프리셋이 없습니다.",
                "현재 런타임 상태는 위에 표시합니다. 스튜디오·방·야외 같은 예시 항목은 실제 데이터가 없으면 노출하지 않습니다.",
                132f);
        }

        private void BuildMockupOutputCard()
        {
            if (_outputDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _outputDashboardContent);

            HideDashboardLegacyControl(
                _apply720p60Button);
            HideDashboardLegacyControl(
                _apply1080p60Button);
            HideDashboardLegacyControl(
                _outputTransparentButton);
            HideDashboardLegacyControl(
                _outputTopmostButton);
            HideDashboardLegacyControl(
                _outputClickThroughButton);

            _dashboardResolutionDropdown =
                AddOutputDropdownRow(
                    "해상도",
                    new[]
                    {
                        "720p  ·  1280 × 720",
                        "1080p  ·  1920 × 1080",
                        "1440p  ·  2560 × 1440",
                        "4K UHD  ·  3840 × 2160"
                    },
                    OnDashboardOutputSelectionChanged);

            _dashboardFpsDropdown =
                AddOutputDropdownRow(
                    "프레임",
                    new[]
                    {
                        "30 FPS",
                        "60 FPS",
                        "120 FPS"
                    },
                    OnDashboardOutputSelectionChanged);

            _dashboardOutputApplyButton =
                CreateButton(
                    "출력 설정 적용",
                    _outputDashboardContent,
                    ApplyDashboardOutputSelection);
            AddPreferredHeight(
                _dashboardOutputApplyButton.gameObject,
                34f);
            SetPrimaryButtonStyle(
                _dashboardOutputApplyButton);
            _dashboardOutputApplyButton.interactable =
                false;

            _dashboardBackgroundModeButton =
                AddOutputValueRow(
                    "배경 모드",
                    "확인 중…",
                    ToggleOverlayTransparent);
            _dashboardTopmostButton =
                AddOutputValueRow(
                    "창 표시",
                    "확인 중…",
                    ToggleOverlayTopmost);

            var help =
                CreateText(
                    "Output Help",
                    _outputDashboardContent,
                    11,
                    TextAnchor.MiddleLeft);
            help.text =
                "해상도와 프레임을 선택한 뒤 ‘출력 설정 적용’을 눌러 반영합니다. " +
                "1440p/4K 항목도 미리 선택할 수 있으며 고해상도 성능 검증은 추후 진행합니다.";
            help.color =
                new Color(
                    0.56f,
                    0.62f,
                    0.70f,
                    1f);
            help.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 52f;
        }

        private Dropdown AddOutputDropdownRow(
            string label,
            IReadOnlyList<string> options,
            Action<int> onChanged)
        {
            var row =
                CreateDashboardRow(
                    _outputDashboardContent,
                    label + " Output Dropdown Row",
                    36f);

            var labelText =
                CreateText(
                    label + " Label",
                    row,
                    12,
                    TextAnchor.MiddleLeft);
            labelText.text = label;
            labelText.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 88f;

            var dropdown =
                CreateDashboardDropdown(
                    label + " Dropdown",
                    row,
                    options,
                    onChanged);

            dropdown.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            return dropdown;
        }

        private Dropdown CreateDashboardDropdown(
            string name,
            Transform parent,
            IReadOnlyList<string> options,
            Action<int> onChanged)
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
                    0.095f,
                    0.115f,
                    0.145f,
                    1f);

            var dropdown =
                rect.gameObject
                    .AddComponent<Dropdown>();
            dropdown.targetGraphic =
                background;

            var caption =
                CreateText(
                    "Caption",
                    rect,
                    11,
                    TextAnchor.MiddleLeft);
            caption.color =
                new Color(
                    0.90f,
                    0.92f,
                    0.96f,
                    1f);
            caption.rectTransform.anchorMin =
                Vector2.zero;
            caption.rectTransform.anchorMax =
                Vector2.one;
            caption.rectTransform.offsetMin =
                new Vector2(10f, 2f);
            caption.rectTransform.offsetMax =
                new Vector2(-30f, -2f);

            var arrow =
                CreateText(
                    "Arrow",
                    rect,
                    12,
                    TextAnchor.MiddleCenter);
            arrow.text = "▾";
            arrow.rectTransform.anchorMin =
                new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            arrow.rectTransform.pivot =
                new Vector2(1f, 0.5f);
            arrow.rectTransform.offsetMin =
                new Vector2(-28f, 2f);
            arrow.rectTransform.offsetMax =
                new Vector2(-4f, -2f);

            var template =
                CreateRect(
                    "Template",
                    rect);
            template.anchorMin =
                new Vector2(0f, 0f);
            template.anchorMax =
                new Vector2(1f, 0f);
            template.pivot =
                new Vector2(0.5f, 1f);
            template.anchoredPosition =
                new Vector2(0f, -2f);
            template.sizeDelta =
                new Vector2(0f, 132f);

            var templateImage =
                template.gameObject
                    .AddComponent<Image>();
            templateImage.color =
                new Color(
                    0.055f,
                    0.065f,
                    0.085f,
                    1f);

            var item =
                CreateRect(
                    "Item",
                    template);
            item.anchorMin =
                new Vector2(0f, 1f);
            item.anchorMax =
                new Vector2(1f, 1f);
            item.pivot =
                new Vector2(0.5f, 1f);
            item.offsetMin =
                new Vector2(2f, -30f);
            item.offsetMax =
                new Vector2(-2f, 0f);

            var itemBackground =
                item.gameObject
                    .AddComponent<Image>();
            itemBackground.color =
                new Color(
                    0.075f,
                    0.085f,
                    0.105f,
                    1f);

            var toggle =
                item.gameObject
                    .AddComponent<Toggle>();
            toggle.targetGraphic =
                itemBackground;

            var itemLabel =
                CreateText(
                    "Item Label",
                    item,
                    11,
                    TextAnchor.MiddleLeft);
            itemLabel.color =
                new Color(
                    0.90f,
                    0.92f,
                    0.96f,
                    1f);
            itemLabel.rectTransform.anchorMin =
                Vector2.zero;
            itemLabel.rectTransform.anchorMax =
                Vector2.one;
            itemLabel.rectTransform.offsetMin =
                new Vector2(10f, 0f);
            itemLabel.rectTransform.offsetMax =
                new Vector2(-8f, 0f);

            dropdown.captionText =
                caption;
            dropdown.template =
                template;
            dropdown.itemText =
                itemLabel;

            dropdown.options.Clear();
            if (options != null)
            {
                for (var i = 0;
                     i < options.Count;
                     i++)
                {
                    dropdown.options.Add(
                        new Dropdown.OptionData(
                            options[i] ?? string.Empty));
                }
            }

            dropdown.onValueChanged.AddListener(
                value =>
                    onChanged?.Invoke(
                        value));

            template.gameObject.SetActive(
                false);
            dropdown.RefreshShownValue();

            return dropdown;
        }

        private void OnDashboardOutputSelectionChanged(
            int _)
        {
            if (_dashboardOutputDropdownSyncing)
            {
                return;
            }

            _dashboardOutputSelectionDirty = true;

            if (_dashboardOutputApplyButton != null)
            {
                _dashboardOutputApplyButton.interactable =
                    sceneRuntime != null;
            }
        }

        private void ApplyDashboardOutputSelection()
        {
            if (sceneRuntime == null ||
                _dashboardResolutionDropdown == null ||
                _dashboardFpsDropdown == null)
            {
                SetDashboardNotice(
                    "렌더링 설정을 사용할 수 없습니다.");
                return;
            }

            var current =
                sceneRuntime.CaptureRenderSettings();
            var next =
                current;

            GetDashboardResolutionSelection(
                _dashboardResolutionDropdown.value,
                out var width,
                out var height,
                out var preset);

            next.ResolutionPreset =
                preset;
            next.Width =
                width;
            next.Height =
                height;
            next.TargetFrameRate =
                GetDashboardFpsSelection(
                    _dashboardFpsDropdown.value);

            try
            {
                sceneRuntime.ApplyRenderSettings(
                    next);
                _dashboardOutputSelectionDirty =
                    false;
                SetDashboardNotice(
                    $"출력 설정 적용: {width} × {height}, {next.TargetFrameRate} FPS");
            }
            catch (Exception exception)
            {
                try
                {
                    sceneRuntime.ApplyRenderSettings(
                        current);
                }
                catch
                {
                    // Preserve the original apply failure in the user message.
                }

                SetDashboardNotice(
                    "출력 설정 적용 실패: " +
                    exception.Message);
            }

            RefreshAll();
        }

        private static void GetDashboardResolutionSelection(
            int index,
            out int width,
            out int height,
            out RenderResolutionPreset preset)
        {
            switch (index)
            {
                case 0:
                    width = 1280;
                    height = 720;
                    preset =
                        RenderResolutionPreset.Minimum720p;
                    return;

                case 2:
                    width = 2560;
                    height = 1440;
                    preset =
                        RenderResolutionPreset.Custom;
                    return;

                case 3:
                    width = 3840;
                    height = 2160;
                    preset =
                        RenderResolutionPreset.Custom;
                    return;

                default:
                    width = 1920;
                    height = 1080;
                    preset =
                        RenderResolutionPreset.Recommended1080p;
                    return;
            }
        }

        private static int GetDashboardFpsSelection(
            int index)
        {
            return index switch
            {
                0 => 30,
                2 => 120,
                _ => 60
            };
        }

        private static int FindDashboardResolutionIndex(
            int width,
            int height)
        {
            if (width == 1280 &&
                height == 720)
            {
                return 0;
            }

            if (width == 2560 &&
                height == 1440)
            {
                return 2;
            }

            if (width == 3840 &&
                height == 2160)
            {
                return 3;
            }

            return 1;
        }

        private static int FindDashboardFpsIndex(
            int fps)
        {
            if (fps <= 30)
            {
                return 0;
            }

            if (fps >= 120)
            {
                return 2;
            }

            return 1;
        }

        private Button AddOutputValueRow(
            string label,
            string value,
            Action action)
        {
            var row =
                CreateDashboardRow(
                    _outputDashboardContent,
                    label + " Output Row",
                    34f);

            var labelText =
                CreateText(
                    label + " Label",
                    row,
                    12,
                    TextAnchor.MiddleLeft);
            labelText.text = label;
            labelText.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 88f;

            var valueButton =
                CreateButton(
                    value,
                    row,
                    action);
            valueButton.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            return valueButton;
        }

        private void AddEnvironmentTile(
            Transform parent,
            string label,
            string stateId)
        {
            var button =
                CreateButton(
                    label,
                    parent,
                    () =>
                        ApplyDashboardEnvironment(
                            stateId));
            if (button.targetGraphic is
                Image image)
            {
                image.color =
                    new Color(
                        0.10f,
                        0.12f,
                        0.15f,
                        1f);
            }
        }

        private void ApplyDashboardEnvironment(
            string stateId)
        {
            if (_environmentStateInput == null)
            {
                SetDashboardNotice(
                    "배경 런타임을 사용할 수 없습니다.");
                return;
            }

            _environmentStateInput.text =
                stateId;

            if (_environmentTransitionDurationInput != null)
            {
                _environmentTransitionDurationInput.text =
                    "0";
            }

            ApplyEnvironmentState();
        }

        private void ApplyDashboardAppearancePreset(
            string presetId)
        {
            if (!IsServiceAlive(
                    _appearanceRuntime))
            {
                SetDashboardNotice(
                    "외형 런타임을 사용할 수 없습니다.");
                return;
            }

            if (_appearanceRuntime.SetPreset(
                    presetId,
                    GetSelectedAppearanceTransitionId(),
                    out var error))
            {
                SetDashboardNotice(
                    "프리셋: " +
                    presetId);
            }
            else
            {
                SetDashboardNotice(
                    "프리셋 적용 실패: " +
                    (error ?? "알 수 없는 오류"));
            }
        }

        private void SelectTrackingSourceByKeyword(
            string keyword)
        {
            if (_trackingControls.Count == 0)
            {
                SetDashboardNotice(
                    "트래킹 소스가 없습니다.");
                return;
            }

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var displayName =
                    _trackingControls[i]
                        ?.DisplayName ??
                    string.Empty;

                if (displayName.IndexOf(
                        keyword,
                        StringComparison.OrdinalIgnoreCase) >=
                    0)
                {
                    _trackingControlIndex = i;
                    SelectSection(
                        ApplicationUiSection.Tracking);
                    RefreshAll();
                    return;
                }
            }

            SetDashboardNotice(
                keyword +
                " 소스를 찾지 못했습니다.");
        }

        private void CreateDashboardSliderRow(
            Transform parent,
            string label,
            float minimum,
            float maximum,
            float value)
        {
            var row =
                CreateDashboardRow(
                    parent,
                    label + " Slider Row",
                    30f);

            var labelText =
                CreateText(
                    label + " Slider Label",
                    row,
                    12,
                    TextAnchor.MiddleLeft);
            labelText.text = label;
            labelText.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 76f;

            var valueText =
                CreateText(
                    label + " Slider Value",
                    row,
                    12,
                    TextAnchor.MiddleRight);
            valueText.text =
                value.ToString(
                    "0.00");
            valueText.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 42f;

            var slider =
                CreateSlider(
                    label,
                    row,
                    minimum,
                    maximum,
                    value,
                    next =>
                    {
                        valueText.text =
                            next.ToString(
                                "0.00");
                    });
            var sliderLayout =
                slider.gameObject
                    .AddComponent<LayoutElement>();
            sliderLayout.flexibleWidth = 1f;
            sliderLayout.preferredHeight =
                24f;

            valueText.transform.SetAsLastSibling();
        }

        private void AddDashboardToggle(
            Transform parent,
            string label,
            bool initial,
            Action action = null)
        {
            var state = initial;
            Button button = null;
            button =
                CreateButton(
                    (state ? "☑ " : "○ ") +
                    label,
                    parent,
                    () =>
                    {
                        state = !state;
                        SetButtonLabel(
                            button,
                            (state ? "☑ " : "○ ") +
                            label);
                        action?.Invoke();
                    });

            AddPreferredHeight(
                button.gameObject,
                28f);

            var text =
                button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.alignment =
                    TextAnchor.MiddleLeft;
            }
        }

        private Button AddDashboardTab(
            Transform parent,
            string label,
            bool selected,
            Action action)
        {
            var button =
                CreateButton(
                    label,
                    parent,
                    action);
            var layout =
                button.gameObject
                    .AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = 30f;

            if (selected)
            {
                SetPrimaryButtonStyle(
                    button);
            }

            return button;
        }

        private RectTransform CreateDashboardRow(
            Transform parent,
            string name,
            float height)
        {
            var row =
                CreateRect(
                    name,
                    parent);
            var layoutElement =
                row.gameObject
                    .AddComponent<LayoutElement>();
            layoutElement.preferredHeight =
                height;

            var layout =
                row.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return row;
        }

        private RectTransform CreateDashboardPanel(
            Transform parent,
            string name,
            float height)
        {
            var panel =
                CreateRect(
                    name,
                    parent);
            panel.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = height;

            var image =
                panel.gameObject
                    .AddComponent<Image>();
            image.color =
                new Color(
                    0.09f,
                    0.105f,
                    0.13f,
                    1f);

            var outline =
                panel.gameObject
                    .AddComponent<Outline>();
            outline.effectColor =
                new Color(
                    0.18f,
                    0.23f,
                    0.30f,
                    1f);
            outline.effectDistance =
                new Vector2(1f, -1f);
            return panel;
        }

        private void AddDashboardEmptyState(
            Transform parent,
            string title,
            string detail,
            float height)
        {
            var panel =
                CreateDashboardPanel(
                    parent,
                    title + " Empty State",
                    height);

            var titleText =
                CreateText(
                    title + " Empty Title",
                    panel,
                    13,
                    TextAnchor.MiddleCenter);
            titleText.text =
                "<b>" + title + "</b>\n<size=11><color=#97A3B3>" +
                detail +
                "</color></size>";
            titleText.supportRichText = true;
            titleText.font =
                _dashboardFontMedium;
            Stretch(
                titleText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(16f, 12f),
                new Vector2(-16f, -12f));
        }

        private void SetDashboardVerticalLayout(
            RectTransform content)
        {
            var layout =
                content.GetComponent<
                    VerticalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void HideDashboardLegacyControl(
            Component component)
        {
            if (component != null)
            {
                component.gameObject.SetActive(
                    false);
            }
        }

        private static void AddPreferredHeight(
            GameObject target,
            float height)
        {
            if (target == null)
            {
                return;
            }

            var layout =
                target.GetComponent<
                    LayoutElement>() ??
                target.AddComponent<
                    LayoutElement>();
            layout.preferredHeight =
                height;
        }

        private void SetDashboardNotice(
            string message)
        {
            _lastActionMessage =
                message;
            RefreshAll();
        }

        private void ApplyPretendardTypography()
        {
            if (_root == null)
            {
                return;
            }

            var texts =
                _root.GetComponentsInChildren<Text>(
                    true);

            for (var i = 0;
                 i < texts.Length;
                 i++)
            {
                var text =
                    texts[i];

                if (text == null)
                {
                    continue;
                }

                var button =
                    text.GetComponentInParent<
                        Button>();

                var useSemiBold =
                    text.fontStyle ==
                        FontStyle.Bold ||
                    text.name.IndexOf(
                        "Title",
                        StringComparison.OrdinalIgnoreCase) >=
                        0 ||
                    text.name.IndexOf(
                        "Header",
                        StringComparison.OrdinalIgnoreCase) >=
                        0;

                text.font =
                    useSemiBold
                        ? _dashboardFontSemiBold
                        : button != null
                            ? _dashboardFontMedium
                            : uiFont;

                if (useSemiBold)
                {
                    text.fontStyle =
                        FontStyle.Normal;
                }
            }
        }

        private void EnsureDashboardTrackingConfigurationLoaded()
        {
            if (_dashboardTrackingConfigurationLoaded)
            {
                return;
            }

            _dashboardTrackingConfigurationLoaded = true;
            _dashboardConfiguredTrackingSources.Clear();

            var configured =
                PlayerPrefs.GetString(
                    TrackingInputsConfiguredKey,
                    string.Empty);

            var ids =
                configured.Split(
                    new[]
                    {
                        '\n'
                    },
                    StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0;
                 i < ids.Length;
                 i++)
            {
                var id =
                    ids[i].Trim();

                if (string.IsNullOrWhiteSpace(
                        id) ||
                    FindTrackingControl(id) == null)
                {
                    continue;
                }

                _dashboardConfiguredTrackingSources.Add(
                    id);
            }

            foreach (var controlId in
                     _dashboardConfiguredTrackingSources)
            {
                var control =
                    FindTrackingControl(
                        controlId);

                if (control == null)
                {
                    continue;
                }

                ApplyPersistedDashboardTrackingSettings(
                    control);

                var shouldEnable =
                    PlayerPrefs.GetInt(
                        TrackingInputEnabledKeyPrefix +
                        controlId,
                        0) != 0;

                if (control.ControlEnabled !=
                    shouldEnable)
                {
                    control.TrySetControlEnabled(
                        shouldEnable,
                        out _);
                }
            }
        }

        private void SaveDashboardTrackingConfiguration()
        {
            var ids =
                new List<string>(
                    _dashboardConfiguredTrackingSources);
            ids.Sort(
                StringComparer.OrdinalIgnoreCase);

            PlayerPrefs.SetString(
                TrackingInputsConfiguredKey,
                string.Join(
                    "\n",
                    ids));
            PlayerPrefs.Save();
        }

        private static string
            GetDashboardTrackingSettingKey(
                string controlId,
                string settingKey)
        {
            return
                TrackingInputSettingKeyPrefix +
                controlId +
                "." +
                settingKey;
        }

        private void ApplyPersistedDashboardTrackingSettings(
            ITrackingRuntimeControl control)
        {
            if (control is not
                ITrackingRuntimeConfigurable configurable)
            {
                return;
            }

            var settings =
                configurable.GetConfigurationSettings();

            if (settings == null ||
                settings.Count == 0)
            {
                return;
            }

            var values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
            var hasPersistedValue = false;

            for (var i = 0;
                 i < settings.Count;
                 i++)
            {
                var setting =
                    settings[i];
                var key =
                    GetDashboardTrackingSettingKey(
                        control.ControlId,
                        setting.Key);

                if (PlayerPrefs.HasKey(
                        key))
                {
                    values[setting.Key] =
                        PlayerPrefs.GetString(
                            key,
                            setting.Value);
                    hasPersistedValue = true;
                }
                else
                {
                    values[setting.Key] =
                        setting.Value;
                }
            }

            if (hasPersistedValue)
            {
                configurable.TryApplyConfiguration(
                    values,
                    out _);
            }
        }

        private void ToggleDashboardTrackingSourcePicker()
        {
            _dashboardTrackingSourcePickerOpen =
                !_dashboardTrackingSourcePickerOpen;

            if (_dashboardTrackingSourcePicker != null)
            {
                _dashboardTrackingSourcePicker.gameObject.SetActive(
                    _dashboardTrackingSourcePickerOpen);
            }

            _dashboardTrackingSourceSignature = null;
            RefreshDashboardTrackingSources();
        }

        private void RefreshDashboardTrackingSources()
        {
            if (_dashboardTrackingSourceList == null)
            {
                return;
            }

            EnsureDashboardTrackingConfigurationLoaded();

            var signatureBuilder =
                new System.Text.StringBuilder();

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];

                if (control == null)
                {
                    continue;
                }

                signatureBuilder
                    .Append(control.ControlId)
                    .Append('\u001f')
                    .Append(control.DisplayName)
                    .Append(
                        _dashboardConfiguredTrackingSources.Contains(
                            control.ControlId)
                            ? '1'
                            : '0')
                    .Append('\u001e');
            }

            signatureBuilder.Append(
                _dashboardTrackingSourcePickerOpen
                    ? "picker:1"
                    : "picker:0");

            var signature =
                signatureBuilder.ToString();

            if (!string.Equals(
                    signature,
                    _dashboardTrackingSourceSignature,
                    StringComparison.Ordinal))
            {
                _dashboardTrackingSourceSignature =
                    signature;
                RebuildDashboardTrackingSourceList();
                RebuildDashboardTrackingSourcePicker();
            }

            foreach (var pair in
                     _dashboardTrackingSourceStatusTexts)
            {
                var control =
                    FindTrackingControl(
                        pair.Key);

                SetTextIfChanged(
                    pair.Value,
                    control == null
                        ? "사용 불가"
                        : GetDashboardTrackingSourceStatus(
                            control));
            }

            foreach (var pair in
                     _dashboardTrackingSourceToggleButtons)
            {
                var control =
                    FindTrackingControl(
                        pair.Key);

                SetButtonLabel(
                    pair.Value,
                    control != null &&
                    control.ControlEnabled
                        ? "끄기"
                        : "켜기");
                pair.Value.interactable =
                    control != null;
            }
        }

        private void RebuildDashboardTrackingSourceList()
        {
            _dashboardTrackingSourceStatusTexts.Clear();
            _dashboardTrackingSourceToggleButtons.Clear();
            _dashboardTrackingSourceLastClickTimes.Clear();

            for (var index =
                     _dashboardTrackingSourceList.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(
                    _dashboardTrackingSourceList
                        .GetChild(index)
                        .gameObject);
            }

            var count = 0;

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];

                if (control == null ||
                    string.IsNullOrWhiteSpace(
                        control.ControlId) ||
                    !_dashboardConfiguredTrackingSources.Contains(
                        control.ControlId))
                {
                    continue;
                }

                AddDashboardTrackingSourceRow(
                    control);
                count++;
            }

            if (count == 0)
            {
                var empty =
                    CreateText(
                        "Tracking Source Empty",
                        _dashboardTrackingSourceList,
                        11,
                        TextAnchor.MiddleLeft);
                empty.text =
                    "추가된 입력 소스가 없습니다.  + 버튼을 눌러 추가하세요.";
                empty.color =
                    new Color(
                        0.56f,
                        0.62f,
                        0.70f,
                        1f);
                empty.gameObject
                    .AddComponent<LayoutElement>()
                    .preferredHeight = 30f;
            }

            var layoutElement =
                _dashboardTrackingSourceList
                    .GetComponent<LayoutElement>();

            if (layoutElement != null)
            {
                layoutElement.preferredHeight =
                    Mathf.Max(
                        34f,
                        Mathf.Max(
                            1,
                            count) *
                        32f);
            }
        }

        private void RebuildDashboardTrackingSourcePicker()
        {
            if (_dashboardTrackingSourcePicker == null)
            {
                return;
            }

            for (var index =
                     _dashboardTrackingSourcePicker.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(
                    _dashboardTrackingSourcePicker
                        .GetChild(index)
                        .gameObject);
            }

            var count = 0;

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];

                if (control == null ||
                    string.IsNullOrWhiteSpace(
                        control.ControlId) ||
                    _dashboardConfiguredTrackingSources.Contains(
                        control.ControlId))
                {
                    continue;
                }

                var controlId =
                    control.ControlId;
                var label =
                    "+  " +
                    GetDashboardTrackingSourceDisplayName(
                        control) +
                    "    " +
                    GetDashboardTrackingSourceDescription(
                        control);

                var button =
                    CreateButton(
                        label,
                        _dashboardTrackingSourcePicker,
                        () =>
                            AddDashboardTrackingSource(
                                controlId));
                AddPreferredHeight(
                    button.gameObject,
                    30f);

                var text =
                    button.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.alignment =
                        TextAnchor.MiddleLeft;
                    text.fontSize = 11;
                }

                count++;
            }

            if (count == 0)
            {
                _dashboardTrackingSourcePickerEmptyText =
                    CreateText(
                        "Tracking Source Picker Empty",
                        _dashboardTrackingSourcePicker,
                        11,
                        TextAnchor.MiddleLeft);
                _dashboardTrackingSourcePickerEmptyText.text =
                    "추가 가능한 입력 소스가 없습니다.";
                _dashboardTrackingSourcePickerEmptyText.color =
                    new Color(
                        0.56f,
                        0.62f,
                        0.70f,
                        1f);
                _dashboardTrackingSourcePickerEmptyText.gameObject
                    .AddComponent<LayoutElement>()
                    .preferredHeight = 30f;
            }

            var layoutElement =
                _dashboardTrackingSourcePicker
                    .GetComponent<LayoutElement>();

            if (layoutElement != null)
            {
                layoutElement.preferredHeight =
                    _dashboardTrackingSourcePickerOpen
                        ? Mathf.Max(
                            32f,
                            Mathf.Max(
                                1,
                                count) *
                            32f)
                        : 0f;
            }
        }

        private void AddDashboardTrackingSource(
            string controlId)
        {
            var control =
                FindTrackingControl(
                    controlId);

            if (control == null)
            {
                SetDashboardNotice(
                    "트래킹 입력 소스를 찾을 수 없습니다.");
                return;
            }

            if (_dashboardConfiguredTrackingSources.Add(
                    controlId))
            {
                control.TrySetControlEnabled(
                    false,
                    out _);
                PlayerPrefs.SetInt(
                    TrackingInputEnabledKeyPrefix +
                    controlId,
                    0);
                SaveDashboardTrackingConfiguration();
            }

            _dashboardTrackingSourcePickerOpen = false;

            if (_dashboardTrackingSourcePicker != null)
            {
                _dashboardTrackingSourcePicker.gameObject.SetActive(
                    false);
            }

            _dashboardTrackingSourceSignature = null;
            RefreshDashboardTrackingSources();

            if (control is
                    ITrackingRuntimeConfigurable configurable &&
                configurable.OpenConfigurationOnAdd)
            {
                OpenDashboardTrackingSourceSettings(
                    controlId);
            }
            else
            {
                SetDashboardNotice(
                    GetDashboardTrackingSourceDisplayName(
                        control) +
                    " 입력 소스를 추가했습니다.");
            }
        }

        private void RemoveDashboardTrackingSource(
            string controlId)
        {
            var control =
                FindTrackingControl(
                    controlId);

            if (control != null &&
                control.ControlEnabled)
            {
                control.TrySetControlEnabled(
                    false,
                    out _);
            }

            _dashboardConfiguredTrackingSources.Remove(
                controlId);
            PlayerPrefs.SetInt(
                TrackingInputEnabledKeyPrefix +
                controlId,
                0);
            SaveDashboardTrackingConfiguration();

            _dashboardTrackingSourceSignature = null;
            RefreshDashboardTrackingSources();

            SetDashboardNotice(
                (control != null
                    ? GetDashboardTrackingSourceDisplayName(
                        control)
                    : controlId) +
                " 입력 소스를 제거했습니다.");
        }

        private void AddDashboardTrackingSourceRow(
            ITrackingRuntimeControl control)
        {
            var controlId =
                control.ControlId;

            var row =
                CreateDashboardRow(
                    _dashboardTrackingSourceList,
                    "Tracking Source " +
                    controlId,
                    30f);

            var details =
                CreateRect(
                    "Tracking Source Details " +
                    controlId,
                    row);
            details.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            var detailsImage =
                details.gameObject
                    .AddComponent<Image>();
            detailsImage.color =
                new Color(
                    0.07f,
                    0.08f,
                    0.10f,
                    1f);

            var detailsButton =
                details.gameObject
                    .AddComponent<Button>();
            detailsButton.targetGraphic =
                detailsImage;
            detailsButton.onClick.AddListener(
                () =>
                    HandleDashboardTrackingSourceRowClick(
                        controlId));

            var detailsLayout =
                details.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            detailsLayout.spacing = 6f;
            detailsLayout.padding =
                new RectOffset(
                    7,
                    7,
                    0,
                    0);
            detailsLayout.childAlignment =
                TextAnchor.MiddleLeft;
            detailsLayout.childControlWidth = true;
            detailsLayout.childControlHeight = true;
            detailsLayout.childForceExpandWidth = false;
            detailsLayout.childForceExpandHeight = true;

            var name =
                CreateText(
                    "Tracking Source Name " +
                    controlId,
                    details,
                    11,
                    TextAnchor.MiddleLeft);
            name.text =
                GetDashboardTrackingSourceDisplayName(
                    control);
            name.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 82f;

            var status =
                CreateText(
                    "Tracking Source Status " +
                    controlId,
                    details,
                    11,
                    TextAnchor.MiddleLeft);
            status.color =
                new Color(
                    0.66f,
                    0.71f,
                    0.79f,
                    1f);
            status.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            if (control is
                ITrackingRuntimeConfigurable)
            {
                var settings =
                    CreateButton(
                        "⚙",
                        row,
                        () =>
                            OpenDashboardTrackingSourceSettings(
                                controlId));
                settings.gameObject
                    .AddComponent<LayoutElement>()
                    .preferredWidth = 32f;
            }

            var toggle =
                CreateButton(
                    control.ControlEnabled
                        ? "끄기"
                        : "켜기",
                    row,
                    () =>
                        ToggleDashboardTrackingSource(
                            controlId));
            toggle.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 48f;

            var remove =
                CreateButton(
                    "×",
                    row,
                    () =>
                        RemoveDashboardTrackingSource(
                            controlId));
            remove.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 28f;

            _dashboardTrackingSourceStatusTexts[
                controlId] =
                    status;
            _dashboardTrackingSourceToggleButtons[
                controlId] =
                    toggle;
        }

        private void HandleDashboardTrackingSourceRowClick(
            string controlId)
        {
            var now =
                Time.unscaledTime;
            _dashboardTrackingSourceLastClickTimes.TryGetValue(
                controlId,
                out var previous);
            _dashboardTrackingSourceLastClickTimes[
                controlId] =
                    now;

            if (previous > 0f &&
                now - previous <= 0.35f)
            {
                _dashboardTrackingSourceLastClickTimes[
                    controlId] =
                        0f;
                OpenDashboardTrackingSourceSettings(
                    controlId);
            }
        }

        private void ToggleDashboardTrackingSource(
            string controlId)
        {
            var control =
                FindTrackingControl(
                    controlId);

            if (control == null)
            {
                SetDashboardNotice(
                    "트래킹 입력 소스를 찾을 수 없습니다.");
                return;
            }

            var targetEnabled =
                !control.ControlEnabled;

            if (!control.TrySetControlEnabled(
                    targetEnabled,
                    out var error))
            {
                SetDashboardNotice(
                    GetDashboardTrackingSourceDisplayName(
                        control) +
                    (targetEnabled
                        ? " 켜기 실패: "
                        : " 끄기 실패: ") +
                    (error ?? "알 수 없는 오류"));

                if (targetEnabled &&
                    control is
                        ITrackingRuntimeConfigurable)
                {
                    OpenDashboardTrackingSourceSettings(
                        controlId);
                }

                RefreshAll();
                return;
            }

            PlayerPrefs.SetInt(
                TrackingInputEnabledKeyPrefix +
                controlId,
                targetEnabled
                    ? 1
                    : 0);
            PlayerPrefs.Save();

            SetDashboardNotice(
                GetDashboardTrackingSourceDisplayName(
                    control) +
                (targetEnabled
                    ? " 켜짐"
                    : " 꺼짐"));

            ResolveTrackingControls(
                force: true);
            RefreshAvailability();
            RefreshAll();
        }

        private void BuildTrackingSourceSettingsModal()
        {
            if (_root == null ||
                _dashboardTrackingSourceSettingsModal != null)
            {
                return;
            }

            _dashboardTrackingSourceSettingsModal =
                CreateRect(
                    "Tracking Source Settings Modal Overlay",
                    _root);
            Stretch(
                _dashboardTrackingSourceSettingsModal,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);

            var blocker =
                _dashboardTrackingSourceSettingsModal.gameObject
                    .AddComponent<Image>();
            blocker.color =
                new Color(
                    0.01f,
                    0.015f,
                    0.025f,
                    0.82f);
            blocker.raycastTarget = true;

            var panel =
                CreateRect(
                    "Tracking Source Settings Modal",
                    _dashboardTrackingSourceSettingsModal);
            panel.anchorMin =
                new Vector2(0.5f, 0.5f);
            panel.anchorMax =
                new Vector2(0.5f, 0.5f);
            panel.pivot =
                new Vector2(0.5f, 0.5f);
            panel.sizeDelta =
                new Vector2(560f, 440f);

            var panelImage =
                panel.gameObject
                    .AddComponent<Image>();
            panelImage.color =
                new Color(
                    0.055f,
                    0.065f,
                    0.085f,
                    1f);

            var outline =
                panel.gameObject
                    .AddComponent<Outline>();
            outline.effectColor =
                new Color(
                    0.20f,
                    0.28f,
                    0.40f,
                    0.9f);
            outline.effectDistance =
                new Vector2(1f, -1f);

            _dashboardTrackingSourceSettingsTitle =
                CreateText(
                    "Tracking Source Settings Title",
                    panel,
                    18,
                    TextAnchor.MiddleLeft);
            _dashboardTrackingSourceSettingsTitle.font =
                _dashboardFontSemiBold;
            Stretch(
                _dashboardTrackingSourceSettingsTitle.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(20f, -58f),
                new Vector2(-64f, -14f));

            var close =
                CreateButton(
                    "×",
                    panel,
                    CloseDashboardTrackingSourceSettings);
            var closeRect =
                close.GetComponent<RectTransform>();
            closeRect.anchorMin =
                new Vector2(1f, 1f);
            closeRect.anchorMax =
                new Vector2(1f, 1f);
            closeRect.pivot =
                new Vector2(1f, 1f);
            closeRect.offsetMin =
                new Vector2(-52f, -52f);
            closeRect.offsetMax =
                new Vector2(-12f, -12f);

            _dashboardTrackingSourceSettingsBody =
                CreateRect(
                    "Tracking Source Settings Body",
                    panel);
            _dashboardTrackingSourceSettingsBody.anchorMin =
                new Vector2(0f, 0f);
            _dashboardTrackingSourceSettingsBody.anchorMax =
                new Vector2(1f, 1f);
            _dashboardTrackingSourceSettingsBody.offsetMin =
                new Vector2(20f, 70f);
            _dashboardTrackingSourceSettingsBody.offsetMax =
                new Vector2(-20f, -68f);

            var bodyLayout =
                _dashboardTrackingSourceSettingsBody
                    .gameObject
                    .AddComponent<VerticalLayoutGroup>();
            bodyLayout.spacing = 6f;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;

            var footer =
                CreateDashboardRow(
                    panel,
                    "Tracking Source Settings Footer",
                    38f);
            footer.anchorMin =
                new Vector2(0f, 0f);
            footer.anchorMax =
                new Vector2(1f, 0f);
            footer.pivot =
                new Vector2(0.5f, 0f);
            footer.offsetMin =
                new Vector2(20f, 18f);
            footer.offsetMax =
                new Vector2(-20f, 56f);

            var spacer =
                CreateRect(
                    "Tracking Source Settings Footer Spacer",
                    footer);
            spacer.gameObject
                .AddComponent<LayoutElement>()
                .flexibleWidth = 1f;

            var cancel =
                CreateButton(
                    "취소",
                    footer,
                    CloseDashboardTrackingSourceSettings);
            cancel.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 88f;

            var save =
                CreateButton(
                    "저장",
                    footer,
                    SaveDashboardTrackingSourceSettings);
            save.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 88f;
            SetPrimaryButtonStyle(
                save);

            _dashboardTrackingSourceSettingsModal.gameObject.SetActive(
                false);
        }

        private void OpenDashboardTrackingSourceSettings(
            string controlId)
        {
            var control =
                FindTrackingControl(
                    controlId);

            if (control is not
                ITrackingRuntimeConfigurable configurable)
            {
                SetDashboardNotice(
                    control == null
                        ? "트래킹 입력 소스를 찾을 수 없습니다."
                        : GetDashboardTrackingSourceDisplayName(
                              control) +
                          "은 별도 설정이 필요하지 않습니다.");
                return;
            }

            if (_dashboardTrackingSourceSettingsModal == null)
            {
                BuildTrackingSourceSettingsModal();
            }

            if (_dashboardTrackingSourceSettingsModal == null ||
                _dashboardTrackingSourceSettingsBody == null)
            {
                return;
            }

            _dashboardTrackingSourceSettingsControlId =
                controlId;
            _dashboardTrackingSourceSettingsInputs.Clear();

            for (var index =
                     _dashboardTrackingSourceSettingsBody.childCount - 1;
                 index >= 0;
                 index--)
            {
                Destroy(
                    _dashboardTrackingSourceSettingsBody
                        .GetChild(index)
                        .gameObject);
            }

            SetTextIfChanged(
                _dashboardTrackingSourceSettingsTitle,
                configurable.ConfigurationTitle);

            var settings =
                configurable.GetConfigurationSettings();

            if (settings == null ||
                settings.Count == 0)
            {
                var empty =
                    CreateText(
                        "Tracking Source Settings Empty",
                        _dashboardTrackingSourceSettingsBody,
                        12,
                        TextAnchor.MiddleLeft);
                empty.text =
                    "이 입력 소스에는 변경 가능한 설정이 없습니다.";
                empty.gameObject
                    .AddComponent<LayoutElement>()
                    .preferredHeight = 38f;
            }
            else
            {
                for (var i = 0;
                     i < settings.Count;
                     i++)
                {
                    var setting =
                        settings[i];

                    var row =
                        CreateDashboardRow(
                            _dashboardTrackingSourceSettingsBody,
                            "Tracking Setting " +
                            setting.Key,
                            38f);

                    var label =
                        CreateText(
                            "Tracking Setting Label " +
                            setting.Key,
                            row,
                            12,
                            TextAnchor.MiddleLeft);
                    label.text =
                        setting.Label;
                    label.gameObject
                        .AddComponent<LayoutElement>()
                        .preferredWidth = 132f;

                    var input =
                        CreateInputField(
                            "Tracking Setting Input " +
                            setting.Key,
                            row,
                            setting.Placeholder);
                    input.gameObject
                        .AddComponent<LayoutElement>()
                        .flexibleWidth = 1f;

                    var persistedKey =
                        GetDashboardTrackingSettingKey(
                            controlId,
                            setting.Key);
                    input.text =
                        PlayerPrefs.HasKey(
                            persistedKey)
                            ? PlayerPrefs.GetString(
                                persistedKey,
                                setting.Value)
                            : setting.Value;

                    _dashboardTrackingSourceSettingsInputs[
                        setting.Key] =
                            input;

                    if (!string.IsNullOrWhiteSpace(
                            setting.HelpText))
                    {
                        var help =
                            CreateText(
                                "Tracking Setting Help " +
                                setting.Key,
                                _dashboardTrackingSourceSettingsBody,
                                10,
                                TextAnchor.MiddleLeft);
                        help.text =
                            setting.HelpText;
                        help.color =
                            new Color(
                                0.56f,
                                0.62f,
                                0.70f,
                                1f);
                        help.gameObject
                            .AddComponent<LayoutElement>()
                            .preferredHeight = 18f;
                    }
                }
            }

            _dashboardTrackingSourceSettingsModal.gameObject.SetActive(
                true);
            _dashboardTrackingSourceSettingsModal.SetAsLastSibling();
        }

        private void CloseDashboardTrackingSourceSettings()
        {
            if (_dashboardTrackingSourceSettingsModal != null)
            {
                _dashboardTrackingSourceSettingsModal.gameObject.SetActive(
                    false);
            }

            _dashboardTrackingSourceSettingsControlId =
                null;
            _dashboardTrackingSourceSettingsInputs.Clear();
        }

        private void SaveDashboardTrackingSourceSettings()
        {
            var control =
                FindTrackingControl(
                    _dashboardTrackingSourceSettingsControlId);

            if (control is not
                ITrackingRuntimeConfigurable configurable)
            {
                SetDashboardNotice(
                    "설정할 트래킹 입력 소스를 찾을 수 없습니다.");
                return;
            }

            var values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var pair in
                     _dashboardTrackingSourceSettingsInputs)
            {
                values[pair.Key] =
                    pair.Value?.text ??
                    string.Empty;
            }

            if (!configurable.TryApplyConfiguration(
                    values,
                    out var error))
            {
                SetDashboardNotice(
                    "설정 저장 실패: " +
                    (error ?? "알 수 없는 오류"));
                return;
            }

            foreach (var pair in values)
            {
                PlayerPrefs.SetString(
                    GetDashboardTrackingSettingKey(
                        control.ControlId,
                        pair.Key),
                    pair.Value ?? string.Empty);
            }

            PlayerPrefs.Save();

            CloseDashboardTrackingSourceSettings();
            SetDashboardNotice(
                GetDashboardTrackingSourceDisplayName(
                    control) +
                " 설정을 저장했습니다.");
            RefreshAll();
        }

        private ITrackingRuntimeControl FindTrackingControl(
            string controlId)
        {
            if (string.IsNullOrWhiteSpace(
                    controlId))
            {
                return null;
            }

            for (var i = 0;
                 i < _trackingControls.Count;
                 i++)
            {
                var control =
                    _trackingControls[i];

                if (control != null &&
                    string.Equals(
                        control.ControlId,
                        controlId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return control;
                }
            }

            return null;
        }

        private static string
            GetDashboardTrackingSourceDisplayName(
                ITrackingRuntimeControl control)
        {
            if (control == null)
            {
                return "알 수 없는 소스";
            }

            return control.ControlId switch
            {
                "mediapipe-webcam" =>
                    "MediaPipe",
                "arkit-ifacialmocap" =>
                    "ARKit",
                "vmc-udp" =>
                    "VMC",
                "ultraleap-hands" =>
                    "Ultraleap",
                _ =>
                    string.IsNullOrWhiteSpace(
                        control.DisplayName)
                        ? control.ControlId
                        : control.DisplayName
            };
        }

        private static string
            GetDashboardTrackingSourceDescription(
                ITrackingRuntimeControl control)
        {
            if (control == null)
            {
                return string.Empty;
            }

            return control.ControlId switch
            {
                "mediapipe-webcam" =>
                    "웹캠 · 얼굴 / 손 / 상체",
                "arkit-ifacialmocap" =>
                    "iPhone/iPad · 얼굴 / 표정 / 머리",
                "vmc-udp" =>
                    "외부 VMC · 전신 / 표정",
                "ultraleap-hands" =>
                    "Leap Motion / Ultraleap · 손 / 손가락 / 손목",
                _ =>
                    control.DisplayName ?? string.Empty
            };
        }

        private static string GetDashboardTrackingSourceStatus(
            ITrackingRuntimeControl control)
        {
            if (control == null)
            {
                return "사용 불가";
            }

            if (!control.ControlEnabled)
            {
                return "꺼짐";
            }

            return control.ControlHealthState switch
            {
                TrackingSourceHealthState.Healthy =>
                    "연결됨",
                TrackingSourceHealthState.Degraded =>
                    "연결됨 · 품질 저하",
                TrackingSourceHealthState.Starting =>
                    "시작 중",
                TrackingSourceHealthState.SourceLost =>
                    "켜짐 · 입력 대기",
                TrackingSourceHealthState.Faulted =>
                    string.IsNullOrWhiteSpace(
                        control.ControlError)
                        ? "오류"
                        : "오류 · 확인 필요",
                _ =>
                    "켜짐 · 대기"
            };
        }

        private void RefreshMockupDashboard()
        {
            if (_dashboardModelNameText != null)
            {
                var hasCharacter =
                    sceneRuntime != null &&
                    sceneRuntime.Status.HasCharacter;
                var path =
                    hasCharacter
                        ? sceneRuntime.Status
                            .CurrentCharacterPath
                        : null;

                SetTextIfChanged(
                    _dashboardModelNameText,
                    hasCharacter
                        ? string.IsNullOrWhiteSpace(
                              path)
                            ? "VRM 모델"
                            : Path.GetFileName(
                                path)
                        : "모델이 로드되지 않음");
            }

            if (_dashboardTrackingStatusText != null)
            {
                var active =
                    _trackingPresence != null &&
                    _trackingPresence.Presence
                        .AnySourceAvailable;

                SetTextIfChanged(
                    _dashboardTrackingStatusText,
                    active
                        ? "트래킹 상태   ● 활성"
                        : "트래킹 상태   ● 비활성화");
            }

            RefreshDashboardTrackingSources();

            if (_dashboardEnvironmentStatusText != null)
            {
                var environmentStatus =
                    IsServiceAlive(_environmentRuntime)
                        ? _environmentRuntime.Status
                        : default;
                SetTextIfChanged(
                    _dashboardEnvironmentStatusText,
                    IsServiceAlive(_environmentRuntime)
                        ? "현재 환경   " +
                          (string.IsNullOrWhiteSpace(
                               environmentStatus.StateId)
                              ? "기본"
                              : environmentStatus.StateId)
                        : "현재 환경   사용할 수 없음");
            }

            if (TryGetRenderSettingsForUiRefresh(
                    out var renderSettings))
            {
                if (!_dashboardOutputSelectionDirty)
                {
                    _dashboardOutputDropdownSyncing =
                        true;

                    if (_dashboardResolutionDropdown != null)
                    {
                        _dashboardResolutionDropdown
                            .SetValueWithoutNotify(
                                FindDashboardResolutionIndex(
                                    renderSettings.Width,
                                    renderSettings.Height));
                        _dashboardResolutionDropdown
                            .RefreshShownValue();
                    }

                    if (_dashboardFpsDropdown != null)
                    {
                        _dashboardFpsDropdown
                            .SetValueWithoutNotify(
                                FindDashboardFpsIndex(
                                    renderSettings.TargetFrameRate));
                        _dashboardFpsDropdown
                            .RefreshShownValue();
                    }

                    _dashboardOutputDropdownSyncing =
                        false;
                }

                if (_dashboardOutputApplyButton != null)
                {
                    _dashboardOutputApplyButton.interactable =
                        _dashboardOutputSelectionDirty;
                }
            }
            else
            {
                if (_dashboardResolutionDropdown != null)
                {
                    _dashboardResolutionDropdown.interactable =
                        false;
                }

                if (_dashboardFpsDropdown != null)
                {
                    _dashboardFpsDropdown.interactable =
                        false;
                }

                if (_dashboardOutputApplyButton != null)
                {
                    _dashboardOutputApplyButton.interactable =
                        false;
                }
            }

            var overlayReadable =
                TryGetOverlayOutputForUiRefresh(
                    out var overlayPresent,
                    out _,
                    out var overlaySettings,
                    out _);

            SetButtonLabel(
                _dashboardBackgroundModeButton,
                overlayPresent &&
                overlayReadable
                    ? overlaySettings.Transparent
                        ? "투명 배경 (Alpha)"
                        : "불투명 배경"
                    : "출력 어댑터 없음");
            SetButtonLabel(
                _dashboardTopmostButton,
                overlayPresent &&
                overlayReadable
                    ? overlaySettings.Topmost
                        ? "항상 위: 켜짐"
                        : "항상 위: 꺼짐"
                    : "출력 어댑터 없음");

            RefreshDashboardPresetGallery();

            if (_dashboardSettingsModal != null &&
                _dashboardSettingsModal.gameObject.activeSelf)
            {
                RefreshSettingsControlState();
            }

            if (_dashboardPresetPanel != null)
            {
                var selected =
                    _model.SelectedSection;
                var visible =
                    selected ==
                        ApplicationUiSection.Character ||
                    selected ==
                        ApplicationUiSection.Appearance;

                if (_dashboardPresetPanel.gameObject.activeSelf !=
                    visible)
                {
                    _dashboardPresetPanel.gameObject.SetActive(
                        visible);
                }
            }
        }
    }
}
