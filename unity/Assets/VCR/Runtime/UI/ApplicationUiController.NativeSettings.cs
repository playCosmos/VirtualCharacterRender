using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.UI
{
    public sealed partial class ApplicationUiController
    {
        private DesktopNativeSettingsWindow _nativeSettings;
        private readonly Dictionary<int, Action> _nativeSettingsActions =
            new Dictionary<int, Action>();
        private readonly Dictionary<int, string> _nativeTrackingFieldKeys =
            new Dictionary<int, string>();
        private readonly Dictionary<int, string> _nativeTrackingSourceIds =
            new Dictionary<int, string>();
        private string _nativeActiveTrackingSource;
        private bool _nativeSettingsWasVisible;
        private bool _nativeSettingsRunInBackgroundBeforeOpen;

        private void OpenNativeSettings()
        {
            if (!DesktopNativeSettingsWindow.Supported)
                return;

            if (_nativeSettings == null)
            {
                try
                {
                    _nativeSettings = new DesktopNativeSettingsWindow();
                    BuildNativeSettings();
                }
                catch (Exception exception)
                {
                    Debug.LogError("Native Settings window unavailable: " + exception, this);
                    _nativeSettings?.Dispose();
                    _nativeSettings = null;
                    return;
                }
            }

            if (!_nativeSettings.IsVisible)
            {
                _nativeSettingsRunInBackgroundBeforeOpen =
                    UnityEngine.Application.runInBackground;
            }

            // The Settings window has its own OS focus. Even when the user's
            // render preference disables background execution, Settings input
            // still needs Unity's main-thread Update to process and save.
            UnityEngine.Application.runInBackground = true;
            _nativeSettings.Open();
            _nativeSettingsWasVisible = _nativeSettings.IsVisible;
        }

        private void AddNativeAction(int category, int id, string label, Action action)
        {
            _nativeSettings.Add(category, id, DesktopNativeSettingsWindow.ControlKind.Button, label);
            _nativeSettingsActions[id] = action;
        }

        private void BuildNativeSettings()
        {
            _nativeSettings.Add(0, 1, DesktopNativeSettingsWindow.ControlKind.Label,
                "프로그램 동작과 렌더링 설정");
            var config = sceneRuntime != null ? sceneRuntime.CaptureRenderSettings() : default;
            _nativeSettings.Add(0, 10, DesktopNativeSettingsWindow.ControlKind.Edit,
                "렌더 스케일 (0.5 ~ 2.0)",
                config.RenderScale.ToString("0.###", CultureInfo.InvariantCulture));
            AddNativeAction(0, 200, "렌더 스케일 적용", ApplyNativeRenderScale);
            _nativeSettings.Add(0, 11, DesktopNativeSettingsWindow.ControlKind.Edit,
                "목표 FPS (30 ~ 240)", config.TargetFrameRate.ToString(CultureInfo.InvariantCulture));
            AddNativeAction(0, 201, "목표 FPS 적용", ApplyNativeFps);
            AddNativeAction(0, 202, "VSync 변경", ToggleSettingsVsync);
            AddNativeAction(0, 203, "백그라운드 실행 변경", ToggleSettingsRunInBackground);
            AddNativeAction(0, 204, "저장된 설정 불러오기", ShowProfileLoadUnavailable);
            AddNativeAction(0, 205, "현재 설정 저장", SaveConfiguration);

            _nativeSettings.Add(1, 2, DesktopNativeSettingsWindow.ControlKind.Label,
                "모델 경로 및 자동 복원");
            var initial = _characterPathInput != null ? _characterPathInput.text
                : PlayerPrefs.GetString("VCR.Character.LastSuccessfulPath.v1", "");
            _nativeSettings.Add(1, 12, DesktopNativeSettingsWindow.ControlKind.Edit,
                "VRM 파일 경로", initial);
            AddNativeAction(1, 300, "선택한 VRM 불러오기", ApplyNativeCharacterPath);
            AddNativeAction(1, 301, "파일 선택…", BrowseNativeCharacter);
            AddNativeAction(1, 302, "현재 모델 다시 불러오기", ReloadCharacter);
            AddNativeAction(1, 303, "모델 해제 및 자동 복원 해제", UnloadCharacter);

            _nativeSettings.Add(3, 3, DesktopNativeSettingsWindow.ControlKind.Label,
                "OS 창 표시 및 투명 합성");
            AddNativeAction(3, 401, "투명 배경 켜기 / 끄기", ToggleOverlayTransparent);
            AddNativeAction(3, 402, "클릭 통과 켜기 / 끄기", ToggleOverlayClickThrough);
            AddNativeAction(3, 403, "출력 복구", RecoverOutput);

            RebuildNativeTrackingSources();
            _nativeSettings.SelectCategory(0);
        }

        private void ApplyNativeRenderScale()
        {
            if (sceneRuntime == null ||
                !float.TryParse(_nativeSettings.GetText(10), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var scale) ||
                float.IsNaN(scale) || float.IsInfinity(scale) ||
                scale < 0.5f || scale > 2.0f)
            {
                _lastActionMessage = "렌더 스케일은 0.5 ~ 2.0 값이어야 합니다.";
                Debug.LogWarning(_lastActionMessage, this);
                return;
            }
            var settings = sceneRuntime.CaptureRenderSettings();
            settings.RenderScale = scale;
            ApplySettingsRender(settings, "Render scale updated.");
        }

        private void ApplyNativeFps()
        {
            if (sceneRuntime == null ||
                !int.TryParse(_nativeSettings.GetText(11), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var fps) ||
                fps < 30 || fps > 240)
            {
                _lastActionMessage = "목표 프레임은 30 ~ 240 FPS여야 합니다.";
                Debug.LogWarning(_lastActionMessage, this);
                return;
            }
            var settings = sceneRuntime.CaptureRenderSettings();
            settings.TargetFrameRate = fps;
            ApplySettingsRender(settings, "Target FPS updated.");
        }

        private void ApplyNativeCharacterPath()
        {
            if (_characterPathInput == null) return;
            _characterPathInput.text = _nativeSettings.GetText(12);
            LoadCharacterFromPath();
        }

        private void BrowseNativeCharacter()
        {
            BrowseCharacterFile();
            if (_characterPathInput != null)
                _nativeSettings.SetText(12, _characterPathInput.text);
        }

        private void RebuildNativeTrackingSources()
        {
            if (_nativeSettings == null) return;
            _nativeSettings.ClearCategory(2);
            _nativeTrackingSourceIds.Clear();
            _nativeTrackingFieldKeys.Clear();
            _nativeActiveTrackingSource = null;
            _nativeSettings.Add(2, 4, DesktopNativeSettingsWindow.ControlKind.Label,
                "트래킹 장치 설정");
            AddNativeAction(2, 450, "장치 다시 확인", () =>
            {
                ResolveTrackingControls(force: true);
                RebuildNativeTrackingSources();
            });
            var offset = 0;
            foreach (var control in _trackingControls)
            {
                if (control == null || !(control is ITrackingRuntimeConfigurable))
                    continue;
                var id = 5000 + offset++;
                var sourceId = control.ControlId;
                _nativeTrackingSourceIds[id] = sourceId;
                AddNativeAction(2, id, control.DisplayName + " 설정",
                    () => ShowNativeTrackingDetails(sourceId));
            }
            if (offset == 0)
                _nativeSettings.Add(2, 5, DesktopNativeSettingsWindow.ControlKind.Label,
                    "설정 가능한 트래킹 장치 없음");
        }

        private void ShowNativeTrackingDetails(string controlId)
        {
            var control = FindTrackingControl(controlId);
            if (!(control is ITrackingRuntimeConfigurable configured)) return;
            _nativeActiveTrackingSource = controlId;
            _nativeSettings.ClearCategory(2);
            _nativeTrackingFieldKeys.Clear();
            _nativeSettings.Add(2, 6, DesktopNativeSettingsWindow.ControlKind.Label,
                configured.ConfigurationTitle);
            var id = 6000;
            foreach (var setting in configured.GetConfigurationSettings())
            {
                _nativeSettings.Add(2, id, DesktopNativeSettingsWindow.ControlKind.Edit,
                    setting.Label, setting.Value);
                _nativeTrackingFieldKeys[id++] = setting.Key;
            }
            AddNativeAction(2, 6900, "설정 저장 및 적용", SaveNativeTrackingDetails);
            AddNativeAction(2, 6901, "장치 목록으로", RebuildNativeTrackingSources);
            _nativeSettings.SelectCategory(2);
        }

        private void SaveNativeTrackingDetails()
        {
            var control = FindTrackingControl(_nativeActiveTrackingSource);
            if (!(control is ITrackingRuntimeConfigurable configurable)) return;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _nativeTrackingFieldKeys)
                values[entry.Value] = _nativeSettings.GetText(entry.Key);

            if (!configurable.TryApplyConfiguration(values, out var error))
            {
                SetDashboardNotice("설정 저장 실패: " + (error ?? "알 수 없는 오류"));
                return;
            }
            foreach (var entry in values)
                PlayerPrefs.SetString(
                    GetDashboardTrackingSettingKey(control.ControlId, entry.Key),
                    entry.Value ?? "");
            PlayerPrefs.Save();
            SetDashboardNotice(control.DisplayName + " 설정 적용됨");
            RebuildNativeTrackingSources();
        }

        private void OpenNativeTrackingDevice(string controlId)
        {
            OpenNativeSettings();
            if (_nativeSettings == null) return;
            ShowNativeTrackingDetails(controlId);
            _nativeSettings.SelectCategory(2);
        }

        private void PumpNativeSettings()
        {
            if (_nativeSettings == null) return;

            if (!_nativeSettings.IsVisible && _nativeSettingsWasVisible)
            {
                UnityEngine.Application.runInBackground =
                    sceneRuntime != null
                        ? sceneRuntime.CaptureRenderSettings().RunInBackground
                        : _nativeSettingsRunInBackgroundBeforeOpen;
                _nativeSettingsWasVisible = false;
            }

            for (var i = 0; i < 48; ++i)
            {
                var id = _nativeSettings.PollAction();
                if (id == 0) break;
                try
                {
                    if (id >= 100 && id < 104)
                    {
                        if (id == 102 && _nativeActiveTrackingSource == null)
                            RebuildNativeTrackingSources();
                        _nativeSettings.SelectCategory(id - 100);
                    }
                    else if (_nativeSettingsActions.TryGetValue(id, out var action))
                    {
                        action?.Invoke();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            // Reassert this after actions; toggling the background preference
            // can change the property while this window still has focus.
            if (_nativeSettings.IsVisible)
            {
                UnityEngine.Application.runInBackground = true;
                _nativeSettingsWasVisible = true;
            }
        }

        private void DisposeNativeSettings()
        {
            _nativeSettings?.Dispose();
            _nativeSettings = null;
            _nativeSettingsActions.Clear();
            _nativeTrackingFieldKeys.Clear();
            _nativeTrackingSourceIds.Clear();
            _nativeActiveTrackingSource = null;
        }
    }
}
