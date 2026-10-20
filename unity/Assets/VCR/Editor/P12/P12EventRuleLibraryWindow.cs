using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P12
{
    public sealed class P12EventRuleLibraryWindow :
        EditorWindow
    {
        private string _folder =
            P12EventRuleLibraryBrowserUtility
                .DefaultFolder;
        private string _search;
        private string _selectedAssetPath;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;
        private P12EventRuleLibraryEntry[] _entries =
            Array.Empty<P12EventRuleLibraryEntry>();
        private string _message;
        private MessageType _messageType =
            MessageType.Info;
        private P12EventRuleLibraryDiff _revisionDiff;
        private string _revisionDiffFromAssetPath;
        private string _revisionDiffToAssetPath;

        [MenuItem("VCR/P12/Open Event Rule Library")]
        public static void Open()
        {
            GetWindow<
                    P12EventRuleLibraryWindow>(
                    "VCR Event Rules")
                .Show();
        }

        private void OnEnable()
        {
            RefreshLibrary();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Event Rule Library",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Indexes versioned event-rule JSON packages inside the project. Invalid packages remain visible for diagnostics. Valid packages are handed to the Event Node Editor, which performs the final validated merge and deterministic ID collision suffixing.",
                MessageType.Info);

            DrawFolderControls();

            _search =
                EditorGUILayout.TextField(
                    "Search",
                    _search ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(
                    _message))
            {
                EditorGUILayout.HelpBox(
                    _message,
                    _messageType);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                DrawLibraryList();
                DrawSelectedDetails();
            }
        }

        private void DrawFolderControls()
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                _folder =
                    EditorGUILayout.TextField(
                        "Library Folder",
                        _folder);

                if (GUILayout.Button(
                        "Create",
                        GUILayout.Width(
                            72f)))
                {
                    if (P12EventRuleLibraryBrowserUtility
                        .TryEnsureFolder(
                            _folder,
                            out var error))
                    {
                        _message =
                            $"Event rule library ready at '{_folder}'.";
                        _messageType =
                            MessageType.Info;
                        RefreshLibrary();
                    }
                    else
                    {
                        _message =
                            error;
                        _messageType =
                            MessageType.Error;
                    }
                }

                if (GUILayout.Button(
                        "Refresh",
                        GUILayout.Width(
                            72f)))
                {
                    RefreshLibrary();
                }

                if (GUILayout.Button(
                        "Add JSON…",
                        GUILayout.Width(
                            92f)))
                {
                    AddExternalJson();
                }
            }
        }

        private void DrawLibraryList()
        {
            using (new EditorGUILayout
                       .VerticalScope(
                           GUILayout.Width(
                               Mathf.Max(
                                   320f,
                                   position.width *
                                   0.46f))))
            {
                EditorGUILayout.LabelField(
                    $"Packages ({_entries.Length})",
                    EditorStyles.boldLabel);

                _listScroll =
                    EditorGUILayout.BeginScrollView(
                        _listScroll);

                var visible = 0;

                foreach (var entry in _entries)
                {
                    if (!P12EventRuleLibraryBrowserUtility
                        .MatchesSearch(
                            entry,
                            _search))
                    {
                        continue;
                    }

                    visible++;

                    var id =
                        string.IsNullOrWhiteSpace(
                            entry.PackageId)
                            ? Path.GetFileNameWithoutExtension(
                                entry.AssetPath)
                            : entry.PackageId;
                    var state =
                        entry.Valid
                            ? "OK"
                            : "INVALID";
                    var label =
                        $"[{state}] {id}  r{entry.Revision}  ({entry.RuleIds.Length})";
                    var selected =
                        string.Equals(
                            _selectedAssetPath,
                            entry.AssetPath,
                            StringComparison.Ordinal);

                    if (GUILayout.Toggle(
                            selected,
                            label,
                            "Button"))
                    {
                        SelectAsset(
                            entry.AssetPath);
                    }
                }

                if (visible == 0)
                {
                    EditorGUILayout.HelpBox(
                        _entries.Length == 0
                            ? "No event rule library JSON files are indexed in this folder."
                            : "No package matches the current search.",
                        MessageType.None);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectedDetails()
        {
            using (new EditorGUILayout
                       .VerticalScope())
            {
                EditorGUILayout.LabelField(
                    "Selected Package",
                    EditorStyles.boldLabel);

                var entry =
                    FindSelected();

                if (entry == null)
                {
                    EditorGUILayout.HelpBox(
                        "Select a package to inspect it and send it to the Event Node Editor.",
                        MessageType.None);
                    return;
                }

                _detailsScroll =
                    EditorGUILayout.BeginScrollView(
                        _detailsScroll);

                EditorGUILayout.LabelField(
                    "Asset Path",
                    entry.AssetPath ?? "<none>");
                EditorGUILayout.LabelField(
                    "Package ID",
                    entry.PackageId ?? "<missing>");
                EditorGUILayout.LabelField(
                    "Version",
                    entry.Version.ToString());
                EditorGUILayout.LabelField(
                    "Revision",
                    entry.Revision.ToString());
                EditorGUILayout.LabelField(
                    "Description",
                    string.IsNullOrWhiteSpace(
                        entry.Description)
                        ? "<none>"
                        : entry.Description,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Tags",
                    entry.Tags != null &&
                    entry.Tags.Length > 0
                        ? string.Join(
                            ", ",
                            entry.Tags)
                        : "<none>");
                EditorGUILayout.LabelField(
                    "File Size",
                    FormatBytes(
                        entry.FileBytes));
                EditorGUILayout.LabelField(
                    "Modified UTC",
                    entry.LastWriteUtc ==
                    DateTime.MinValue
                        ? "<unknown>"
                        : entry.LastWriteUtc
                            .ToString(
                                "u"));

                EditorGUILayout.Space();
                DrawRevisionHistory(
                    entry);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "Rules",
                    EditorStyles.boldLabel);

                if (entry.RuleIds.Length == 0)
                {
                    EditorGUILayout.LabelField(
                        "<none>");
                }
                else
                {
                    foreach (var ruleId in entry.RuleIds)
                    {
                        EditorGUILayout.LabelField(
                            "• " +
                            ruleId);
                    }
                }

                if (!entry.Valid)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        entry.Error ??
                        "Package validation failed.",
                        MessageType.Error);
                }

                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(
                               !entry.Valid))
                    {
                        if (GUILayout.Button(
                                "Send to Event Nodes"))
                        {
                            P12EventNodeEditorWindow
                                .OpenWithLibraryPackage(
                                    entry.Package,
                                    entry.AssetPath);
                            _message =
                                $"Queued event rule package '{entry.PackageId}' for Event Node Editor import.";
                            _messageType =
                                MessageType.Info;
                        }
                    }

                    if (GUILayout.Button(
                            "Ping Asset"))
                    {
                        var asset =
                            AssetDatabase
                                .LoadMainAssetAtPath(
                                    entry.AssetPath);

                        if (asset != null)
                        {
                            Selection.activeObject =
                                asset;
                            EditorGUIUtility.PingObject(
                                asset);
                        }
                    }

                    if (GUILayout.Button(
                            "Copy Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer =
                            entry.AssetPath ??
                            string.Empty;
                        _message =
                            "Event rule package asset path copied.";
                        _messageType =
                            MessageType.Info;
                    }
                }
            }
        }

        private void AddExternalJson()
        {
            var path =
                EditorUtility.OpenFilePanel(
                    "Add Event Rule Library Package",
                    Application.dataPath,
                    "json");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            if (!P12EventRuleLibraryBrowserUtility
                .TryAddExternalPackage(
                    path,
                    _folder,
                    out var entry,
                    out var error))
            {
                _message =
                    "Package add failed: " +
                    (error ?? "unknown error");
                _messageType =
                    MessageType.Error;
                return;
            }

            SelectAsset(
                entry.AssetPath);
            _message =
                $"Added event rule package '{entry.PackageId}' to the library.";
            _messageType =
                MessageType.Info;
            RefreshLibrary(
                preserveMessage:
                    true);
        }

        private void RefreshLibrary(
            bool preserveMessage = false)
        {
            if (!P12EventRuleLibraryBrowserUtility
                .TryScan(
                    _folder,
                    out _entries,
                    out var error))
            {
                _entries =
                    Array.Empty<
                        P12EventRuleLibraryEntry>();

                if (!preserveMessage)
                {
                    _message =
                        error;
                    _messageType =
                        MessageType.Warning;
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    _selectedAssetPath) &&
                FindSelected() == null)
            {
                _selectedAssetPath =
                    null;
            }

            if (string.IsNullOrWhiteSpace(
                    _selectedAssetPath) &&
                _entries.Length > 0)
            {
                _selectedAssetPath =
                    _entries[0].AssetPath;
            }

            if (!preserveMessage)
            {
                _message =
                    $"Indexed {_entries.Length} event rule package file(s).";
                _messageType =
                    MessageType.Info;
            }

            Repaint();
        }

        private void SelectAsset(
            string assetPath)
        {
            if (string.Equals(
                    _selectedAssetPath,
                    assetPath,
                    StringComparison.Ordinal))
            {
                return;
            }

            _selectedAssetPath =
                assetPath;
            ClearRevisionDiff();
        }

        private void DrawRevisionHistory(
            P12EventRuleLibraryEntry entry)
        {
            EditorGUILayout.LabelField(
                "Revision History",
                EditorStyles.boldLabel);

            if (!entry.Valid)
            {
                EditorGUILayout.HelpBox(
                    "Revision history is available only for valid packages.",
                    MessageType.None);
                return;
            }

            if (!P12EventRuleLibraryBrowserUtility
                .TryGetRevisionHistory(
                    _entries,
                    entry,
                    out var history,
                    out var historyError))
            {
                EditorGUILayout.HelpBox(
                    historyError ??
                    "Revision history is unavailable.",
                    MessageType.Warning);
                return;
            }

            if (history.Length == 0)
            {
                EditorGUILayout.LabelField(
                    "<none>");
                return;
            }

            P12EventRuleLibraryBrowserUtility
                .TryFindPreviousRevision(
                    _entries,
                    entry,
                    out var previous,
                    out _);
            P12EventRuleLibraryBrowserUtility
                .TryFindNextRevision(
                    _entries,
                    entry,
                    out var next,
                    out _);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           previous == null))
                {
                    if (GUILayout.Button(
                            "Previous Revision"))
                    {
                        SelectAsset(
                            previous.AssetPath);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(
                           next == null))
                {
                    if (GUILayout.Button(
                            "Next Revision"))
                    {
                        SelectAsset(
                            next.AssetPath);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(
                           previous == null))
                {
                    if (GUILayout.Button(
                            "Compare Previous"))
                    {
                        CompareRevision(
                            previous,
                            entry);
                    }
                }

                using (new EditorGUI.DisabledScope(
                           _revisionDiff == null))
                {
                    if (GUILayout.Button(
                            "Clear Diff"))
                    {
                        ClearRevisionDiff();
                    }
                }
            }

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                foreach (var revisionEntry in history)
                {
                    var current =
                        string.Equals(
                            revisionEntry.AssetPath,
                            entry.AssetPath,
                            StringComparison.Ordinal);
                    var label =
                        $"r{revisionEntry.Revision}" +
                        (current
                            ? "  [selected]"
                            : string.Empty) +
                        $"  {Path.GetFileName(revisionEntry.AssetPath)}";

                    if (GUILayout.Button(
                            label,
                            current
                                ? EditorStyles.miniButtonMid
                                : EditorStyles.miniButton))
                    {
                        SelectAsset(
                            revisionEntry.AssetPath);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            DrawRevisionDiff(
                entry);
        }

        private void CompareRevision(
            P12EventRuleLibraryEntry from,
            P12EventRuleLibraryEntry to)
        {
            if (from == null ||
                to == null ||
                !from.Valid ||
                !to.Valid)
            {
                _message =
                    "Revision diff requires two valid packages.";
                _messageType =
                    MessageType.Warning;
                ClearRevisionDiff();
                return;
            }

            if (!P12EventRuleLibraryUtility
                .TryDiffPackages(
                    from.Package,
                    to.Package,
                    out _revisionDiff,
                    out var error))
            {
                _message =
                    "Revision diff failed: " +
                    (error ?? "unknown error");
                _messageType =
                    MessageType.Error;
                ClearRevisionDiff();
                return;
            }

            _revisionDiffFromAssetPath =
                from.AssetPath;
            _revisionDiffToAssetPath =
                to.AssetPath;
            _message =
                $"Compared '{to.PackageId}' revision {from.Revision} → {to.Revision}.";
            _messageType =
                MessageType.Info;
        }

        private void DrawRevisionDiff(
            P12EventRuleLibraryEntry selected)
        {
            if (_revisionDiff == null ||
                !string.Equals(
                    _revisionDiffToAssetPath,
                    selected.AssetPath,
                    StringComparison.Ordinal))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Diff r{_revisionDiff.FromRevision} → r{_revisionDiff.ToRevision}",
                EditorStyles.boldLabel);

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Metadata",
                    $"description {FormatChanged(_revisionDiff.DescriptionChanged)}, tags {FormatChanged(_revisionDiff.TagsChanged)}");
                EditorGUILayout.LabelField(
                    "Rules",
                    $"+{_revisionDiff.AddedRuleIds.Length} / -{_revisionDiff.RemovedRuleIds.Length} / changed {_revisionDiff.ChangedRuleIds.Length} / unchanged {_revisionDiff.UnchangedRuleIds.Length}");

                DrawRuleIdGroup(
                    "Added",
                    _revisionDiff.AddedRuleIds);
                DrawRuleIdGroup(
                    "Removed",
                    _revisionDiff.RemovedRuleIds);
                DrawRuleIdGroup(
                    "Changed",
                    _revisionDiff.ChangedRuleIds);

                if (!_revisionDiff.HasChanges)
                {
                    EditorGUILayout.HelpBox(
                        "No metadata or rule changes were detected between these revisions.",
                        MessageType.Info);
                }

                if (!string.IsNullOrWhiteSpace(
                        _revisionDiffFromAssetPath))
                {
                    EditorGUILayout.LabelField(
                        "From",
                        _revisionDiffFromAssetPath,
                        EditorStyles.miniLabel);
                }
            }
        }

        private static void DrawRuleIdGroup(
            string label,
            string[] ids)
        {
            if (ids == null ||
                ids.Length == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(
                label,
                string.Join(
                    ", ",
                    ids),
                EditorStyles.wordWrappedMiniLabel);
        }

        private static string FormatChanged(
            bool changed) =>
                changed
                    ? "changed"
                    : "same";

        private void ClearRevisionDiff()
        {
            _revisionDiff = null;
            _revisionDiffFromAssetPath = null;
            _revisionDiffToAssetPath = null;
        }

        private P12EventRuleLibraryEntry FindSelected()
        {
            if (string.IsNullOrWhiteSpace(
                    _selectedAssetPath))
            {
                return null;
            }

            foreach (var entry in _entries)
            {
                if (entry != null &&
                    string.Equals(
                        entry.AssetPath,
                        _selectedAssetPath,
                        StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private static string FormatBytes(
            long bytes)
        {
            if (bytes < 1024)
            {
                return bytes +
                       " B";
            }

            if (bytes <
                1024L * 1024L)
            {
                return
                    (bytes / 1024.0)
                    .ToString(
                        "0.0") +
                    " KiB";
            }

            return
                (bytes /
                 (1024.0 * 1024.0))
                .ToString(
                    "0.00") +
                " MiB";
        }
    }
}
