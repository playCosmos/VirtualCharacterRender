using System;
using UnityEngine;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        private void BrowseWorkspaceCharacterFile()
        {
            if (_characterActionPending)
            {
                ReportWorkspaceCharacterBrowse("Model operation in progress.");
                return;
            }

            try
            {
                var adapter = FindFirstObjectByType<DesktopCharacterFileSelectionAdapter>();
                if (adapter == null)
                {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
                    adapter = GetComponent<DesktopCharacterFileSelectionAdapter>();
                    if (adapter == null)
                        adapter = gameObject.AddComponent<DesktopCharacterFileSelectionAdapter>();
#endif
                }

                if (adapter == null || !adapter.IsSupported)
                {
                    ReportWorkspaceCharacterBrowse(
                        adapter?.UnavailableReason ??
                        "File selection is available in Unity Editor and Windows/macOS standalone.");
                    return;
                }

                // Prefer the current page when choosing the initial folder.
                var currentPath = _workspaceNativeSettings != null &&
                                  _workspaceNativeSettings.IsVisible &&
                                  _workspaceNativeSettings.SelectedCategory == 1
                    ? _workspaceNativeSettings.GetText(12)
                    : _modelPathInput?.value;
                var result = adapter.SelectCharacterFile(currentPath);
                if (result.Cancelled)
                    return; // Keep the user's previous path and edit text untouched.

                if (!result.Selected || string.IsNullOrWhiteSpace(result.Path))
                {
                    ReportWorkspaceCharacterBrowse(
                        "VRM selection failed: " + (result.Error ?? "no file selected"));
                    return;
                }

                if (_modelPathInput != null)
                    _modelPathInput.SetValueWithoutNotify(result.Path);
                if (_workspaceNativeSettings != null)
                    _workspaceNativeSettings.SetText(12, result.Path);
                ReportWorkspaceCharacterBrowse(
                    "Selected: " + System.IO.Path.GetFileName(result.Path) +
                    ". Press Load VRM to open the model.");
            }
            catch (Exception exception)
            {
                ReportWorkspaceCharacterBrowse("VRM selection failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        private void ReportWorkspaceCharacterBrowse(string message)
        {
            if (_sourcesFeedback != null)
                _sourcesFeedback.text = message;
            if (_workspaceNativeSettings != null)
                _workspaceNativeSettings.SetText(91, message);
        }
    }
}
