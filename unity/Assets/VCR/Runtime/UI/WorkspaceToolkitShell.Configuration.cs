using System;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;

namespace VCR.Runtime.UI
{
    // The application bootstrap is the owner of the versioned runtime config
    // and of backup/recovery policy. Do not write its JSON from the UI.
    public sealed partial class WorkspaceToolkitShell
    {
        private const float ConfigurationConfirmSeconds = 15f;
        private float _workspaceSaveConfirmUntil;
        private float _workspaceReloadConfirmUntil;

        private void ResetWorkspaceConfigurationActions()
        {
            _workspaceSaveConfirmUntil = 0f;
            _workspaceReloadConfirmUntil = 0f;
        }

        private ApplicationRuntimeBootstrap ResolveWorkspaceConfigurationBootstrap()
        {
            return FindFirstObjectByType<ApplicationRuntimeBootstrap>();
        }

        private string GetWorkspaceConfigurationStatus()
        {
            var bootstrap = ResolveWorkspaceConfigurationBootstrap();
            if (bootstrap == null)
                return "Configuration unavailable: application bootstrap was not found.";

            if (bootstrap.AutomaticConfigurationSaveSuppressed)
            {
                var reason = !string.IsNullOrWhiteSpace(bootstrap.LastConfigurationLoadError)
                    ? bootstrap.LastConfigurationLoadError
                    : bootstrap.LastConfigurationLoadWarning;
                return "Configuration recovery active; automatic save disabled. " +
                    (string.IsNullOrWhiteSpace(reason) ? "Review settings before saving." : reason);
            }

            return "Configuration: " +
                (string.IsNullOrWhiteSpace(bootstrap.ConfigurationPath)
                    ? "not yet initialized" : bootstrap.ConfigurationPath) +
                " | Automatic save: enabled";
        }

        private void RefreshWorkspaceConfigurationStatus()
        {
            if (_workspaceNativeSettings != null)
                _workspaceNativeSettings.SetText(94, GetWorkspaceConfigurationStatus());
        }

        private string ApplyWorkspaceConfigurationAction(int action)
        {
            var bootstrap = ResolveWorkspaceConfigurationBootstrap();
            if (bootstrap == null)
                return "Application bootstrap unavailable; settings file was not changed.";
            if (sceneRuntime == null)
                sceneRuntime = bootstrap.SceneRuntime;
            if (sceneRuntime == null)
                return "Scene runtime unavailable; settings file was not changed.";

            try
            {
                if (action == 205)
                {
                    // A recovered/failed configuration is deliberately protected
                    // against automatic saves. Require a second explicit click.
                    if (bootstrap.AutomaticConfigurationSaveSuppressed &&
                        (_workspaceSaveConfirmUntil <= 0f ||
                         Time.unscaledTime > _workspaceSaveConfirmUntil))
                    {
                        _workspaceSaveConfirmUntil = Time.unscaledTime +
                            ConfigurationConfirmSeconds;
                        return "Recovery protection: saving will overwrite the existing " +
                            "configuration. Review values, then press Save again within 15s.";
                    }

                    _workspaceSaveConfirmUntil = 0f;
                    if (!bootstrap.SaveConfiguration(out var saveError))
                        return "Configuration save failed: " +
                            (saveError ?? "unknown error");
                    return "Runtime configuration saved: " + bootstrap.ConfigurationPath;
                }

                if (action == 206)
                {
                    // Reload replaces live runtime settings, so guard against
                    // accidental loss of unsaved configuration changes.
                    if (_workspaceReloadConfirmUntil <= 0f ||
                        Time.unscaledTime > _workspaceReloadConfirmUntil)
                    {
                        _workspaceReloadConfirmUntil = Time.unscaledTime +
                            ConfigurationConfirmSeconds;
                        return "Reload replaces current unsaved settings. " +
                            "Press Reload again within 15s to confirm.";
                    }

                    _workspaceReloadConfirmUntil = 0f;
                    if (!bootstrap.ReloadSavedConfiguration(out var loadError))
                        return "Configuration reload failed: " +
                            (loadError ?? "unknown error");

                    _renderInputsRuntime = null;
                    SynchronizeRenderInputs();
                    // Reload replaces the persisted camera values too. A pending
                    // Inspector FOV draft must not silently overwrite that value.
                    _cameraFieldOfViewRuntime = null;
                    SynchronizeCameraFieldOfView();
                    if (sceneRuntime.TryCaptureRenderSettings(out var settings))
                    {
                        if (_workspaceNativeSettings != null)
                        {
                            _workspaceNativeSettings.SetText(10,
                                settings.RenderScale.ToString("0.###",
                                    System.Globalization.CultureInfo.InvariantCulture));
                            _workspaceNativeSettings.SetText(11,
                                settings.TargetFrameRate.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture));
                            // The native window temporarily requires background
                            // ticking. Restore the newly loaded preference on close.
                            if (_nativeSettingsWasVisible)
                            {
                                _runInBackgroundBeforeSettings = settings.RunInBackground;
                                UnityEngine.Application.runInBackground = true;
                            }
                        }
                    }
                    return "Saved runtime configuration reloaded.";
                }

                return "Unsupported configuration action.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return "Configuration action failed: " + exception.Message;
            }
        }

        private void ApplyWorkspaceConfigurationActionWithNativeFeedback(int action)
        {
            var result = ApplyWorkspaceConfigurationAction(action);
            if (_workspaceNativeSettings != null)
            {
                _workspaceNativeSettings.SetText(94, result);
                // Status is intentionally not refreshed here: confirmation
                // instructions and failure details should remain visible.
            }
        }

        private void BuildWorkspaceConfigurationFallback(VisualElement panel, Action onReload)
        {
            var section = new Label("Runtime configuration");
            section.style.marginTop = 12;
            panel.Add(section);
            var status = new Label(GetWorkspaceConfigurationStatus());
            status.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(status);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.Add(new Button(() =>
            {
                status.text = ApplyWorkspaceConfigurationAction(205);
            }) { text = "Save config" });
            actions.Add(new Button(() =>
            {
                var result = ApplyWorkspaceConfigurationAction(206);
                status.text = result;
                if (string.Equals(result, "Saved runtime configuration reloaded.",
                        StringComparison.Ordinal))
                    onReload?.Invoke();
            }) { text = "Reload config" });
            actions.Add(new Button(() =>
            {
                status.text = GetWorkspaceConfigurationStatus();
            }) { text = "Status" });
            panel.Add(actions);
        }
    }
}
