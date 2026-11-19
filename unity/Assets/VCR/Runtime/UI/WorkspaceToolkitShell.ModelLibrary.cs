using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        private const string ModelLibraryPlaceholder = "Select a registered VRM...";
        private ModelLibraryStore _modelLibraryStore;
        private DropdownField _modelLibrarySelection;
        private Label _modelLibraryStatus;
        private Button _modelLibraryUseButton;
        private Button _modelLibraryRemoveButton;
        private string _modelLibrarySelectedPath;

        private void BuildWorkspaceModelLibrary(VisualElement parent)
        {
            parent.Add(new Label("Registered VRM files (manual, local only)"));
            _modelLibraryStatus = new Label("Library not loaded.");
            _modelLibrarySelection = new DropdownField("Registered VRMs")
            {
                name = "vcr-model-library-selection"
            };
            _modelLibrarySelection.RegisterValueChangedCallback(change =>
            {
                _modelLibrarySelectedPath =
                    string.Equals(change.newValue, ModelLibraryPlaceholder,
                        StringComparison.Ordinal) ? null : change.newValue;
                UpdateWorkspaceModelLibraryActions();
            });
            parent.Add(_modelLibrarySelection);

            var actions = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap }
            };
            actions.Add(new Button(AddWorkspaceModelLibraryPath)
                { text = "Register path" });
            _modelLibraryUseButton = new Button(UseWorkspaceModelLibraryPath)
                { text = "Use selected path" };
            _modelLibraryRemoveButton = new Button(RemoveWorkspaceModelLibraryPath)
                { text = "Remove reference" };
            actions.Add(_modelLibraryUseButton);
            actions.Add(_modelLibraryRemoveButton);
            actions.Add(new Button(RefreshWorkspaceModelLibrary)
                { text = "Refresh library" });
            parent.Add(actions);
            parent.Add(_modelLibraryStatus);
            RefreshWorkspaceModelLibrary();
        }

        private void RefreshWorkspaceModelLibrary()
        {
            if (_modelLibraryStatus == null || _modelLibrarySelection == null)
                return;
            try
            {
                _modelLibraryStore ??= ModelLibraryStore.CreateDefault();
                if (!_modelLibraryStore.TryRead(out var entries, out var error))
                {
                    _modelLibrarySelection.choices = new List<string>
                        { ModelLibraryPlaceholder };
                    _modelLibrarySelection.SetValueWithoutNotify(ModelLibraryPlaceholder);
                    _modelLibrarySelectedPath = null;
                    _modelLibraryStatus.text = "Library unavailable: " + error +
                        " Existing JSON/backup will not be overwritten.";
                    UpdateWorkspaceModelLibraryActions();
                    return;
                }

                var choices = new List<string> { ModelLibraryPlaceholder };
                var missingCount = 0;
                var selectionStillExists = false;
                foreach (var entry in entries)
                {
                    // Display the exact local path as identity. Filenames can
                    // collide even if they reside in different directories.
                    choices.Add(entry.Path);
                    if (!File.Exists(entry.Path))
                        missingCount++;
                    if (string.Equals(entry.Path, _modelLibrarySelectedPath,
                        StringComparison.Ordinal))
                        selectionStillExists = true;
                }
                if (!selectionStillExists)
                    _modelLibrarySelectedPath = null;
                _modelLibrarySelection.choices = choices;
                _modelLibrarySelection.SetValueWithoutNotify(
                    _modelLibrarySelectedPath ?? ModelLibraryPlaceholder);
                _modelLibraryStatus.text = entries.Length + " registered VRM(s)" +
                    (missingCount > 0
                        ? "; " + missingCount + " missing file(s)."
                        : ".") +
                    " Registration does not load a model.";
                UpdateWorkspaceModelLibraryActions();
            }
            catch (Exception exception)
            {
                _modelLibraryStatus.text = "Library unavailable: " +
                    exception.GetType().Name;
                _modelLibrarySelectedPath = null;
                UpdateWorkspaceModelLibraryActions();
            }
        }

        private void UpdateWorkspaceModelLibraryActions()
        {
            var selected = !string.IsNullOrWhiteSpace(_modelLibrarySelectedPath);
            _modelLibraryUseButton?.SetEnabled(selected &&
                File.Exists(_modelLibrarySelectedPath));
            _modelLibraryRemoveButton?.SetEnabled(selected);
        }

        private void AddWorkspaceModelLibraryPath()
        {
            var path = _modelPathInput?.value;
            _modelLibraryStore ??= ModelLibraryStore.CreateDefault();
            if (!_modelLibraryStore.TryAdd(path, out var error))
            {
                if (_sourcesFeedback != null)
                    _sourcesFeedback.text = "Library registration rejected: " + error;
                RefreshWorkspaceModelLibrary();
                return;
            }
            _modelLibrarySelectedPath = Path.GetFullPath(path.Trim());
            RefreshWorkspaceModelLibrary();
            if (_sourcesFeedback != null)
                _sourcesFeedback.text = "VRM path registered; active model unchanged.";
        }

        private void UseWorkspaceModelLibraryPath()
        {
            if (string.IsNullOrWhiteSpace(_modelLibrarySelectedPath))
                return;
            if (!File.Exists(_modelLibrarySelectedPath))
            {
                if (_sourcesFeedback != null)
                    _sourcesFeedback.text = "Registered VRM file is missing.";
                RefreshWorkspaceModelLibrary();
                return;
            }
            if (_modelPathInput != null)
                _modelPathInput.SetValueWithoutNotify(_modelLibrarySelectedPath);
            if (_sourcesFeedback != null)
                _sourcesFeedback.text = "Path selected. Click Load VRM to load it.";
        }

        private void RemoveWorkspaceModelLibraryPath()
        {
            if (string.IsNullOrWhiteSpace(_modelLibrarySelectedPath))
                return;
            _modelLibraryStore ??= ModelLibraryStore.CreateDefault();
            if (!_modelLibraryStore.TryRemove(_modelLibrarySelectedPath, out var error))
            {
                if (_sourcesFeedback != null)
                    _sourcesFeedback.text = "Library removal rejected: " + error;
                RefreshWorkspaceModelLibrary();
                return;
            }
            _modelLibrarySelectedPath = null;
            RefreshWorkspaceModelLibrary();
            if (_sourcesFeedback != null)
                _sourcesFeedback.text = "Library reference removed; VRM file and active model untouched.";
        }
    }
}
