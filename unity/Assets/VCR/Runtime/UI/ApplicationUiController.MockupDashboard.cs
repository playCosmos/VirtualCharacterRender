using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace VCR.Runtime.UI
{
    public sealed partial class ApplicationUiController
    {
        private Font _dashboardFontMedium;
        private Font _dashboardFontSemiBold;
        private RectTransform _trackingCameraPrivacyPlaceholder;
        private RectTransform _dashboardPresetPanel;
        private Text _dashboardModelNameText;
        private Text _dashboardTrackingStatusText;

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

            StyleMockupTitleBar(
                titleBar);
            BuildMockupViewportToolbar(
                windowChrome);
            BuildMockupInspector(
                inspector);
            BuildMockupTrackingCard();
            BuildMockupMotionCard();
            BuildMockupControlCard();
            BuildMockupEnvironmentCard();
            BuildMockupOutputCard();
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

        private void BuildMockupViewportToolbar(
            DesktopWindowChromeController windowChrome)
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
                new Vector2(0.68f, 1f);
            left.offsetMin =
                new Vector2(6f, 4f);
            left.offsetMax =
                new Vector2(-4f, -4f);

            var leftLayout =
                left.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            leftLayout.spacing = 6f;
            leftLayout.childControlWidth = true;
            leftLayout.childControlHeight = true;
            leftLayout.childForceExpandWidth = false;
            leftLayout.childForceExpandHeight = true;

            AddToolbarLabel(
                left,
                "화면 비율",
                58f);
            AddToolbarButton(
                left,
                "16:9⌄",
                70f,
                () =>
                    SetDashboardNotice(
                        "화면 비율: 16:9"));
            AddToolbarLabel(
                left,
                "뷰",
                24f);
            AddToolbarButton(
                left,
                "카메라⌄",
                84f,
                () =>
                    SetDashboardNotice(
                        "카메라 뷰"));
            AddToolbarButton(
                left,
                "⌖",
                38f,
                () =>
                    SetDashboardNotice(
                        "카메라 프레이밍"));

            var right =
                CreateRect(
                    "Viewport Toolbar Right",
                    toolbar);
            right.anchorMin =
                new Vector2(0.58f, 0f);
            right.anchorMax =
                new Vector2(1f, 1f);
            right.offsetMin =
                new Vector2(4f, 4f);
            right.offsetMax =
                new Vector2(-6f, -4f);

            var rightLayout =
                right.gameObject
                    .AddComponent<HorizontalLayoutGroup>();
            rightLayout.spacing = 6f;
            rightLayout.childAlignment =
                TextAnchor.MiddleRight;
            rightLayout.childControlWidth = true;
            rightLayout.childControlHeight = true;
            rightLayout.childForceExpandWidth = false;
            rightLayout.childForceExpandHeight = true;

            AddToolbarButton(
                right,
                "⌗ 그리드",
                90f,
                () =>
                    SetDashboardNotice(
                        "그리드 표시 전환"));
            AddToolbarButton(
                right,
                "통계 표시",
                92f,
                () =>
                    SetDashboardNotice(
                        "통계 표시 전환"));
            AddToolbarButton(
                right,
                "⛶",
                38f,
                windowChrome != null
                    ? windowChrome.ToggleZoom
                    : null);
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

        private Button AddToolbarButton(
            Transform parent,
            string label,
            float width,
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
            layout.preferredWidth =
                width;
            layout.preferredHeight =
                30f;

            if (button.targetGraphic is
                Image image)
            {
                image.color =
                    new Color(
                        0.09f,
                        0.105f,
                        0.13f,
                        1f);
            }

            return button;
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

            var presetGrid =
                CreateRect(
                    "Preset Grid",
                    _dashboardPresetPanel);
            Stretch(
                presetGrid,
                Vector2.zero,
                Vector2.one,
                new Vector2(
                    10f,
                    8f),
                new Vector2(
                    -10f,
                    -36f));

            var grid =
                presetGrid.gameObject
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
                    78f,
                    88f);
            grid.constraint =
                GridLayoutGroup.Constraint
                    .FixedRowCount;
            grid.constraintCount = 1;
            grid.childAlignment =
                TextAnchor.MiddleLeft;

            AddPresetTile(
                presetGrid,
                "기본",
                "default");
            AddPresetTile(
                presetGrid,
                "캐주얼",
                "casual");
            AddPresetTile(
                presetGrid,
                "스테이지",
                "stage");
            AddPresetTile(
                presetGrid,
                "나이트",
                "night");
            AddPresetTile(
                presetGrid,
                "커스텀1",
                "custom1");

            var addPreset =
                CreateButton(
                    "+\n추가",
                    presetGrid,
                    () =>
                        SetDashboardNotice(
                            "새 프리셋 추가"));
            addPreset.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 88f;
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

            var tabs =
                CreateDashboardRow(
                    _trackingDashboardContent,
                    "Tracking Tabs",
                    34f);
            AddDashboardTab(
                tabs,
                "캠",
                true,
                () =>
                    SelectTrackingSourceByKeyword(
                        "webcam"));
            AddDashboardTab(
                tabs,
                "ARKit (Mac)",
                false,
                () =>
                    SelectTrackingSourceByKeyword(
                        "arkit"));
            AddDashboardTab(
                tabs,
                "MediaPipe",
                false,
                () =>
                    SelectTrackingSourceByKeyword(
                        "mediapipe"));

            var privacyFrame =
                CreateDashboardPanel(
                    _trackingDashboardContent,
                    "Camera Privacy Frame",
                    122f);

            _trackingCameraPrivacyPlaceholder =
                CreateRect(
                    "Camera Privacy Placeholder",
                    privacyFrame);
            Stretch(
                _trackingCameraPrivacyPlaceholder,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 8f),
                new Vector2(-8f, -8f));

            var privacyText =
                CreateText(
                    "Camera Privacy Message",
                    _trackingCameraPrivacyPlaceholder,
                    13,
                    TextAnchor.MiddleCenter);
            privacyText.text =
                "◉̸\n카메라 미리보기 숨김\n<size=11><color=#A8B1C0>(버튼을 눌러서만 표시합니다)</color></size>";
            privacyText.supportRichText =
                true;
            Stretch(
                privacyText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f));

            if (_trackingCameraPreviewPanel != null)
            {
                _trackingCameraPreviewPanel.SetParent(
                    privacyFrame,
                    false);
                Stretch(
                    _trackingCameraPreviewPanel,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(4f, 4f),
                    new Vector2(-4f, -4f));
                _trackingCameraPreviewPanel.gameObject.SetActive(
                    false);
            }

            _trackingCameraPreviewButton =
                CreateButton(
                    "카메라 미리보기 보기",
                    _trackingDashboardContent,
                    ToggleTrackingCameraPreview);
            AddPreferredHeight(
                _trackingCameraPreviewButton.gameObject,
                32f);
            SetPrimaryButtonStyle(
                _trackingCameraPreviewButton);

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

            var quality =
                CreateDashboardRow(
                    _trackingDashboardContent,
                    "Tracking Quality",
                    30f);
            var qualityLabel =
                CreateText(
                    "Tracking Quality Label",
                    quality,
                    12,
                    TextAnchor.MiddleLeft);
            qualityLabel.text =
                "트래킹 품질";
            qualityLabel.gameObject
                .AddComponent<LayoutElement>()
                .preferredWidth = 82f;
            AddDashboardTab(
                quality,
                "표준",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Tracking));
            AddDashboardTab(
                quality,
                "⚙",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Tracking));
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
                "기본 동작",
                true,
                () =>
                    SelectSection(
                        ApplicationUiSection.MotionExpression));
            AddDashboardTab(
                tabs,
                "표정 프리셋",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Expression));

            var motions =
                new[]
                {
                    "대기 01",
                    "대기 02",
                    "인사하기",
                    "손 흔들기",
                    "놀람",
                    "하트",
                    "사용자 모션 1",
                    "사용자 모션 2"
                };

            for (var i = 0;
                 i < motions.Length;
                 i++)
            {
                var motion =
                    motions[i];
                var button =
                    CreateButton(
                        "▶   " +
                        motion +
                        "                         ···",
                        _motionDashboardContent,
                        () =>
                            SetDashboardNotice(
                                motion));
                AddPreferredHeight(
                    button.gameObject,
                    25f);
                var label =
                    button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.alignment =
                        TextAnchor.MiddleLeft;
                }
            }
        }

        private void BuildMockupControlCard()
        {
            if (_controlDashboardContent == null)
            {
                return;
            }

            SetDashboardVerticalLayout(
                _controlDashboardContent);

            CreateDashboardSliderRow(
                _controlDashboardContent,
                "전체 크기",
                0f,
                2f,
                1f);
            CreateDashboardSliderRow(
                _controlDashboardContent,
                "높이 오프셋",
                -1f,
                1f,
                0f);
            CreateDashboardSliderRow(
                _controlDashboardContent,
                "시선 강도",
                0f,
                1f,
                1f);
            CreateDashboardSliderRow(
                _controlDashboardContent,
                "블링크",
                0f,
                1f,
                1f);

            AddDashboardToggle(
                _controlDashboardContent,
                "물리 연산 (머리카락/의상)",
                true);
            AddDashboardToggle(
                _controlDashboardContent,
                "바닥 그림자",
                true);
            AddDashboardToggle(
                _controlDashboardContent,
                "자동 카메라 프레이밍",
                false);

            var reset =
                CreateButton(
                    "↻ 리셋",
                    _controlDashboardContent,
                    () =>
                        SetDashboardNotice(
                            "조작 값을 초기화했습니다."));
            AddPreferredHeight(
                reset.gameObject,
                34f);
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

            var tabs =
                CreateDashboardRow(
                    _environmentDashboardContent,
                    "Environment Tabs",
                    34f);
            AddDashboardTab(
                tabs,
                "배경",
                true,
                () =>
                    SelectSection(
                        ApplicationUiSection.Environment));
            AddDashboardTab(
                tabs,
                "조명",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Environment));
            AddDashboardTab(
                tabs,
                "카메라",
                false,
                () =>
                    SelectSection(
                        ApplicationUiSection.Environment));

            var gridRect =
                CreateRect(
                    "Environment Preset Grid",
                    _environmentDashboardContent);
            gridRect.gameObject
                .AddComponent<LayoutElement>()
                .preferredHeight = 184f;

            var grid =
                gridRect.gameObject
                    .AddComponent<GridLayoutGroup>();
            grid.cellSize =
                new Vector2(
                    78f,
                    82f);
            grid.spacing =
                new Vector2(
                    6f,
                    6f);
            grid.constraint =
                GridLayoutGroup.Constraint
                    .FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment =
                TextAnchor.UpperCenter;

            AddEnvironmentTile(
                gridRect,
                "▦\n투명 배경",
                "transparent");
            AddEnvironmentTile(
                gridRect,
                "▧\n단색 배경",
                "solid");
            AddEnvironmentTile(
                gridRect,
                "▣\n스튜디오",
                "studio");
            AddEnvironmentTile(
                gridRect,
                "▤\n방",
                "room");
            AddEnvironmentTile(
                gridRect,
                "▥\n야외",
                "outdoor");
            AddEnvironmentTile(
                gridRect,
                "···\n커스텀",
                "custom");
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

            AddOutputValueRow(
                "해상도",
                "1920 × 1080 (Full HD)⌄",
                Apply1080p60);
            AddOutputValueRow(
                "프레임 레이트",
                "60 FPS⌄",
                Apply1080p60);
            AddOutputValueRow(
                "배경 모드",
                "▧ 투명 배경 (Alpha)",
                ToggleOverlayTransparent);
            AddOutputValueRow(
                "안티앨리어싱",
                "TAA (권장)⌄",
                () =>
                    SetDashboardNotice(
                        "안티앨리어싱 설정"));

            AddDashboardToggle(
                _outputDashboardContent,
                "가상 카메라 출력 활성화",
                true);
            AddDashboardToggle(
                _outputDashboardContent,
                "창 항상 위에 표시",
                true,
                ToggleOverlayTopmost);

            var detach =
                CreateButton(
                    "↗ 미리보기 창 분리",
                    _outputDashboardContent,
                    () =>
                        SetDashboardNotice(
                            "미리보기 창 분리"));
            AddPreferredHeight(
                detach.gameObject,
                34f);
        }

        private void AddOutputValueRow(
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
