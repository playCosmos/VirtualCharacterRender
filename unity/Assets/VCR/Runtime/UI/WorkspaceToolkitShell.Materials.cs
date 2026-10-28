using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Materials.Unity;

namespace VCR.Runtime.UI
{
    // Material operations remain delegated to the existing non-destructive
    // override controller; this UI does not modify imported source assets.
    public sealed partial class WorkspaceToolkitShell
    {
        private const int WorkspaceMaterialSlotDisplayLimit = 64;
        private ScrollView _workspaceMaterialSlots;
        private Label _workspaceMaterialsSummary;
        private Label _workspaceMaterialsFeedback;
        private TextField _workspaceMaterialSlotInput;
        private TextField _workspaceMaterialShaderInput;
        private TextField _workspaceMaterialPropertyInput;
        private TextField _workspaceMaterialValueInput;
        private TextField _workspacePackagePathInput;
        private MaterialOverrideController _workspaceMaterialController;
        private RuntimeShaderPackageLoader _workspacePackageLoader;
        private float _nextWorkspaceMaterialsRefresh;
        private string _workspaceMaterialSignature;

        private void ResetWorkspaceMaterialsPanel()
        {
            _workspaceMaterialSlots = null;
            _workspaceMaterialsSummary = null;
            _workspaceMaterialsFeedback = null;
            _workspaceMaterialSlotInput = null;
            _workspaceMaterialShaderInput = null;
            _workspaceMaterialPropertyInput = null;
            _workspaceMaterialValueInput = null;
            _workspacePackagePathInput = null;
            _workspaceMaterialController = null;
            _workspacePackageLoader = null;
            _nextWorkspaceMaterialsRefresh = 0f;
            _workspaceMaterialSignature = null;
        }

        private void BuildWorkspaceMaterialsPanel(VisualElement panel)
        {
            _workspaceMaterialsSummary = new Label("Material runtime not yet discovered.");
            panel.Add(_workspaceMaterialsSummary);
            _workspaceMaterialsFeedback = new Label(
                "Select a material slot before applying a compiled shader.");
            panel.Add(_workspaceMaterialsFeedback);
            var refresh = new VisualElement();
            refresh.style.flexDirection = FlexDirection.Row;
            refresh.style.flexWrap = Wrap.Wrap;
            refresh.Add(new Button(() =>
            {
                _workspaceMaterialSignature = null;
                _nextWorkspaceMaterialsRefresh = 0f;
                RefreshWorkspaceMaterialsPanel();
            }) { text = "Refresh view" });
            refresh.Add(new Button(RefreshWorkspaceMaterialSlots)
            {
                text = "Rescan slots (resets overrides)",
                tooltip = "Rescanning restores the source materials and clears overrides."
            });
            panel.Add(refresh);

            _workspaceMaterialSlots = new ScrollView();
            _workspaceMaterialSlots.name = "vcr-material-slots";
            _workspaceMaterialSlots.style.minHeight = 55;
            _workspaceMaterialSlots.style.maxHeight = 185;
            panel.Add(_workspaceMaterialSlots);

            _workspaceMaterialSlotInput = new TextField("Material slot ID");
            _workspaceMaterialSlotInput.tooltip = "Pick an existing slot from the list above.";
            panel.Add(_workspaceMaterialSlotInput);
            _workspaceMaterialShaderInput = new TextField("Compiled shader ID");
            _workspaceMaterialShaderInput.tooltip =
                "Registered or built-in compiled shader ID; raw shader source is not supported.";
            panel.Add(_workspaceMaterialShaderInput);

            var shaderActions = new VisualElement();
            shaderActions.style.flexDirection = FlexDirection.Row;
            shaderActions.Add(new Button(ApplyWorkspaceMaterialShader) { text = "Apply shader" });
            shaderActions.Add(new Button(ClearWorkspaceMaterialOverride) { text = "Clear override" });
            panel.Add(shaderActions);

            _workspaceMaterialPropertyInput = new TextField("Float property");
            _workspaceMaterialPropertyInput.value = "_Smoothness";
            panel.Add(_workspaceMaterialPropertyInput);
            _workspaceMaterialValueInput = new TextField("Float value");
            _workspaceMaterialValueInput.value = "0.5";
            panel.Add(_workspaceMaterialValueInput);
            panel.Add(new Button(ApplyWorkspaceMaterialFloat) { text = "Set float property" });

            panel.Add(new Label("Shader packages (precompiled, validated)"));
            _workspacePackagePathInput = new TextField("Package directory");
            _workspacePackagePathInput.tooltip =
                "Path to an existing declarative shader package root containing manifest.json.";
            panel.Add(_workspacePackagePathInput);
            var packageActions = new VisualElement();
            packageActions.style.flexDirection = FlexDirection.Row;
            packageActions.style.flexWrap = Wrap.Wrap;
            packageActions.Add(new Button(LoadWorkspaceShaderPackage) { text = "Load package" });
            packageActions.Add(new Button(ReloadWorkspaceShaderPackage) { text = "Reload" });
            packageActions.Add(new Button(UnloadWorkspaceShaderPackage) { text = "Unload" });
            panel.Add(packageActions);

            RefreshWorkspaceMaterialsPanel();
        }

        private void RefreshWorkspaceMaterialsPanel()
        {
            if (_workspaceMaterialSlots == null ||
                Time.unscaledTime < _nextWorkspaceMaterialsRefresh)
                return;
            _nextWorkspaceMaterialsRefresh = Time.unscaledTime + 2f;
            try
            {
                if (sceneRuntime == null)
                    sceneRuntime = FindFirstObjectByType<VCR.Runtime.Scene.SingleCharacterSceneRuntime>();
                // Only edit the current model. Looking up an arbitrary scene-wide
                // controller can affect a different character after reload.
                var current = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
                _workspaceMaterialController = current != null
                    ? current.GetComponent<MaterialOverrideController>() : null;
                if (_workspacePackageLoader == null)
                    _workspacePackageLoader = FindFirstObjectByType<RuntimeShaderPackageLoader>();

                var controller = _workspaceMaterialController;
                var package = _workspacePackageLoader;
                var shaders = RuntimeShaderRegistry.GetRegisteredIds();
                var slots = controller != null ? controller.GetSlots() :
                    Array.Empty<VCR.Runtime.Materials.MaterialSlotDescriptor>();
                var signature = new StringBuilder(256);
                signature.Append(controller != null ? controller.GetInstanceID() : 0)
                    .Append('|').Append(package != null ? package.GetInstanceID() : 0)
                    .Append('|').Append(package != null ? package.ActivePackageId : "")
                    .Append('|').Append(package != null ? package.ActivePackageVersion : "")
                    .Append('|').Append(shaders.Length).Append('|').Append(slots.Length);
                foreach (var shaderId in shaders)
                    signature.Append('|').Append(shaderId);
                foreach (var slot in slots)
                {
                    signature.Append('|').Append(slot.Id).Append(':')
                        .Append(slot.SourceShaderName);
                    if (controller != null && controller.TryGetStatus(slot.Id, out var status))
                        signature.Append(':').Append(status.Health)
                            .Append(':').Append(status.ShaderId).Append(':').Append(status.Error);
                }
                if (_workspaceMaterialSignature == signature.ToString())
                    return;
                _workspaceMaterialSignature = signature.ToString();

                _workspaceMaterialsSummary.text = "Slots: " + slots.Length +
                    " | Registered shaders: " + shaders.Length +
                    " | Package: " + (package == null
                        ? "loader unavailable"
                        : (string.IsNullOrWhiteSpace(package.ActivePackageId)
                            ? "none" : package.ActivePackageId + " " + package.ActivePackageVersion));

                _workspaceMaterialSlots.Clear();
                if (controller == null)
                    _workspaceMaterialSlots.Add(new Label(
                        "No material override controller in the active scene."));
                else if (slots.Length == 0)
                    _workspaceMaterialSlots.Add(new Label(
                        "No material slots discovered. Use Rescan after loading a model."));

                for (var i = 0; i < Math.Min(slots.Length, WorkspaceMaterialSlotDisplayLimit); i++)
                {
                    var slot = slots[i];
                    var slotId = slot.Id;
                    var activeShader = slot.SourceShaderName;
                    var health = "source";
                    if (controller != null && controller.TryGetStatus(slot.Id, out var status))
                    {
                        activeShader = status.ShaderId ?? activeShader;
                        health = status.Health.ToString();
                    }
                    var label = slot.RendererPath + " #" + slot.SlotIndex +
                        " — " + activeShader + " (" + health + ")";
                    _workspaceMaterialSlots.Add(new Button(() =>
                    {
                        if (_workspaceMaterialSlotInput != null)
                            _workspaceMaterialSlotInput.value = slotId;
                        if (_workspaceMaterialShaderInput != null)
                            _workspaceMaterialShaderInput.value = activeShader;
                    }) { text = label, tooltip = slotId });
                }
                if (slots.Length > WorkspaceMaterialSlotDisplayLimit)
                    _workspaceMaterialSlots.Add(new Label("Showing first " +
                        WorkspaceMaterialSlotDisplayLimit + " of " + slots.Length + " slots."));

                if (shaders.Length > 0)
                {
                    _workspaceMaterialSlots.Add(new Label("Available registered shader IDs:"));
                    for (var i = 0; i < Math.Min(shaders.Length, 32); i++)
                    {
                        var id = shaders[i];
                        _workspaceMaterialSlots.Add(new Button(() =>
                        {
                            if (_workspaceMaterialShaderInput != null)
                                _workspaceMaterialShaderInput.value = id;
                        }) { text = id });
                    }
                }
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Material discovery failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void SetWorkspaceMaterialsFeedback(string message)
        {
            if (_workspaceMaterialsFeedback != null)
                _workspaceMaterialsFeedback.text = message;
        }

        private void WorkspaceMaterialActionFinished(string message)
        {
            SetWorkspaceMaterialsFeedback(message);
            _workspaceMaterialSignature = null;
            _nextWorkspaceMaterialsRefresh = 0f;
            RefreshWorkspaceMaterialsPanel();
        }

        private bool TryGetWorkspaceMaterialTarget(out MaterialOverrideController target)
        {
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<VCR.Runtime.Scene.SingleCharacterSceneRuntime>();
            var current = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            target = current != null
                ? current.GetComponent<MaterialOverrideController>() : null;
            if (sceneRuntime == null || !sceneRuntime.Status.HasCharacter || target == null)
            {
                SetWorkspaceMaterialsFeedback(
                    "Load a VRM and connect a material override controller before editing.");
                return false;
            }
            _workspaceMaterialController = target;
            return true;
        }

        private void RefreshWorkspaceMaterialSlots()
        {
            if (!TryGetWorkspaceMaterialTarget(out var target)) return;
            try
            {
                target.RefreshSlots();
                WorkspaceMaterialActionFinished(
                    "Material slots rescanned; existing overrides were cleared.");
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Material rescan failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ApplyWorkspaceMaterialShader()
        {
            if (!TryGetWorkspaceMaterialTarget(out var target)) return;
            var slot = _workspaceMaterialSlotInput?.value?.Trim();
            var shader = _workspaceMaterialShaderInput?.value?.Trim();
            if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(shader))
            {
                SetWorkspaceMaterialsFeedback("Select a valid slot and compiled shader ID.");
                return;
            }
            try
            {
                if (target.TryApplyShaderId(slot, shader, out var error))
                    WorkspaceMaterialActionFinished("Applied shader " + shader + " to " + slot + ".");
                else
                    SetWorkspaceMaterialsFeedback("Shader apply failed: " + error);
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Shader apply failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ApplyWorkspaceMaterialFloat()
        {
            if (!TryGetWorkspaceMaterialTarget(out var target)) return;
            var slot = _workspaceMaterialSlotInput?.value?.Trim();
            var property = _workspaceMaterialPropertyInput?.value?.Trim();
            if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(property) ||
                !float.TryParse(_workspaceMaterialValueInput?.value?.Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                SetWorkspaceMaterialsFeedback(
                    "A slot, property and finite invariant-culture float are required.");
                return;
            }
            try
            {
                if (target.TrySetFloat(slot, property, value, out var error))
                    WorkspaceMaterialActionFinished("Updated " + property + " on " + slot + ".");
                else
                    SetWorkspaceMaterialsFeedback("Material float update failed: " + error);
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Material float update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ClearWorkspaceMaterialOverride()
        {
            if (!TryGetWorkspaceMaterialTarget(out var target)) return;
            var slot = _workspaceMaterialSlotInput?.value?.Trim();
            if (string.IsNullOrWhiteSpace(slot))
            {
                SetWorkspaceMaterialsFeedback("Select a material slot to clear.");
                return;
            }
            try
            {
                if (target.ClearOverride(slot))
                    WorkspaceMaterialActionFinished("Restored source material on " + slot + ".");
                else
                    SetWorkspaceMaterialsFeedback("Unable to clear material override for " + slot + ".");
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Clear override failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private RuntimeShaderPackageLoader ResolveWorkspacePackageLoader()
        {
            if (_workspacePackageLoader == null)
                _workspacePackageLoader = FindFirstObjectByType<RuntimeShaderPackageLoader>();
            if (_workspacePackageLoader == null)
                SetWorkspaceMaterialsFeedback("Shader package loader is not available.");
            return _workspacePackageLoader;
        }

        private void LoadWorkspaceShaderPackage()
        {
            var loader = ResolveWorkspacePackageLoader();
            if (loader == null) return;
            var path = _workspacePackagePathInput?.value?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetWorkspaceMaterialsFeedback("Choose a shader package directory.");
                return;
            }
            try
            {
                if (loader.TryLoadPackage(path, out var error))
                    WorkspaceMaterialActionFinished("Shader package loaded: " +
                        loader.ActivePackageId + ".");
                else
                    SetWorkspaceMaterialsFeedback("Package load failed: " + error);
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Package load failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ReloadWorkspaceShaderPackage()
        {
            var loader = ResolveWorkspacePackageLoader();
            if (loader == null) return;
            try
            {
                if (loader.TryReloadActive(out var error))
                    WorkspaceMaterialActionFinished("Active shader package reloaded.");
                else
                    SetWorkspaceMaterialsFeedback("Package reload failed: " + error);
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Package reload failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void UnloadWorkspaceShaderPackage()
        {
            var loader = ResolveWorkspacePackageLoader();
            if (loader == null) return;
            try
            {
                loader.UnloadActivePackage();
                WorkspaceMaterialActionFinished("Shader package unloaded.");
            }
            catch (Exception ex)
            {
                SetWorkspaceMaterialsFeedback("Package unload failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }
    }
}
