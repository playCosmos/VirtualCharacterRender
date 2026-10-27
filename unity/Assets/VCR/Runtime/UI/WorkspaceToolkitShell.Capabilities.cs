using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Capabilities;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        private VisualElement _workspaceCapabilityList;
        private Label _workspaceCapabilityFeedback;
        private float _nextWorkspaceCapabilityRefresh;
        private string _workspaceCapabilitySignature;

        private void ResetWorkspaceCapabilitiesPanel()
        {
            _workspaceCapabilityList = null;
            _workspaceCapabilityFeedback = null;
            _nextWorkspaceCapabilityRefresh = 0f;
            _workspaceCapabilitySignature = null;
        }

        private void BuildWorkspaceCapabilitiesPanel(VisualElement container)
        {
            _workspaceCapabilityFeedback = new Label("Runtime modules and installed providers.");
            container.Add(_workspaceCapabilityFeedback);
            container.Add(new Button(() =>
            {
                _workspaceCapabilitySignature = null;
                _nextWorkspaceCapabilityRefresh = 0f;
                RefreshWorkspaceCapabilitiesPanel();
            }) { text = "Refresh modules" });
            _workspaceCapabilityList = new ScrollView();
            _workspaceCapabilityList.style.minHeight = 60;
            _workspaceCapabilityList.style.flexGrow = 1;
            container.Add(_workspaceCapabilityList);
            RefreshWorkspaceCapabilitiesPanel();
        }

        private void RefreshWorkspaceCapabilitiesPanel()
        {
            if (_workspaceCapabilityList == null ||
                Time.unscaledTime < _nextWorkspaceCapabilityRefresh)
                return;
            _nextWorkspaceCapabilityRefresh = Time.unscaledTime + 2f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var registry = sceneRuntime != null ? sceneRuntime.Capabilities : null;
            try
            {
                var statuses = registry != null
                    ? registry.CaptureStatuses()
                    : Array.Empty<CapabilityStatusSnapshot>();
                var providerIds = DeclarativeCapabilityCatalog.Shared.GetRegisteredProviderIds();
                var signature = new StringBuilder();
                signature.Append(registry == null ? "missing" : "available");
                foreach (var status in statuses)
                    signature.Append('|').Append(status.Id).Append(':').Append(status.State)
                        .Append(':').Append(status.Error);
                foreach (var providerId in providerIds)
                    signature.Append('|').Append(providerId);
                var nextSignature = signature.ToString();
                if (nextSignature == _workspaceCapabilitySignature) return;
                _workspaceCapabilitySignature = nextSignature;

                _workspaceCapabilityList.Clear();
                if (registry == null)
                {
                    _workspaceCapabilityList.Add(new Label("Runtime capability registry unavailable."));
                }
                else if (statuses.Length == 0)
                {
                    _workspaceCapabilityList.Add(new Label("No runtime capabilities registered."));
                }
                foreach (var status in statuses)
                {
                    var id = status.Id;
                    var item = new VisualElement();
                    item.style.marginBottom = 5;
                    item.Add(new Label(id + " — " + status.State));
                    if (!string.IsNullOrWhiteSpace(status.Error))
                    {
                        var error = new Label(status.Error);
                        error.style.color = new Color(1f, 0.55f, 0.45f);
                        item.Add(error);
                    }
                    var toggle = new Button(() => ToggleWorkspaceCapability(id))
                    {
                        text = status.State == CapabilityState.Enabled ? "Disable" : "Enable"
                    };
                    toggle.SetEnabled(sceneRuntime != null &&
                        ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State));
                    item.Add(toggle);
                    _workspaceCapabilityList.Add(item);
                }
                if (providerIds.Length > 0)
                {
                    _workspaceCapabilityList.Add(new Label("Declarative providers (metadata only)"));
                    foreach (var id in providerIds)
                        _workspaceCapabilityList.Add(new Label(id));
                }
                else
                {
                    _workspaceCapabilityList.Add(new Label("No declarative providers registered."));
                }
            }
            catch (Exception ex)
            {
                _workspaceCapabilityFeedback.text = "Module discovery failed: " + ex.Message;
                Debug.LogException(ex, this);
            }
        }

        private void ToggleWorkspaceCapability(string id)
        {
            try
            {
                if (sceneRuntime == null ||
                    !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
                {
                    _workspaceCapabilityFeedback.text = "Runtime is not ready for module changes.";
                    return;
                }
                var registry = sceneRuntime.Capabilities;
                if (registry == null)
                {
                    _workspaceCapabilityFeedback.text = "Capability registry unavailable.";
                    return;
                }
                var enabled = registry.GetState(id) == CapabilityState.Enabled;
                string error = null;
                var success = enabled ? registry.Disable(id) : registry.Enable(id, out error);
                _workspaceCapabilityFeedback.text = success
                    ? "Module '" + id + "' " + (enabled ? "disabled." : "enabled.")
                    : "Module '" + id + "' failed: " + (error ?? registry.GetError(id) ?? "unknown error");
                _workspaceCapabilitySignature = null;
                _nextWorkspaceCapabilityRefresh = 0f;
                _workspaceTrackingPanelSignature = null;
                _nextWorkspaceTrackingPanelRefresh = 0f;
                RefreshWorkspaceCapabilitiesPanel();
                RefreshWorkspaceTrackingPanel();
            }
            catch (Exception ex)
            {
                _workspaceCapabilityFeedback.text = "Module action failed: " + ex.Message;
                Debug.LogException(ex, this);
            }
        }
    }
}
