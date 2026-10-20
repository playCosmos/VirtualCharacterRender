using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P11
{
    public sealed class
        P11AppearanceTransitionPackageLibraryWindow :
        EditorWindow
    {
        private string _folder =
            P11AppearanceTransitionPackageLibraryUtility
                .DefaultFolder;
        private string _search;
        private string _selectedAssetPath;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;
        private P11AppearanceTransitionPackageLibraryEntry[]
            _entries =
                Array.Empty<
                    P11AppearanceTransitionPackageLibraryEntry>();
        private string _message;
        private MessageType _messageType =
            MessageType.Info;

        [MenuItem("VCR/P11/Open Transition Package Library")]
        public static void Open()
        {
            GetWindow<
                    P11AppearanceTransitionPackageLibraryWindow>(
                    "VCR Transition Packages")
                .Show();
        }

        private void OnEnable()
        {
            RefreshLibrary();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Appearance Transition Package Library",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Indexes versioned transition-package JSON files inside the project. Invalid packages remain visible for diagnostics but cannot be sent to the Timeline. Final import still uses the Timeline's collision confirmation and transactional runtime rollback.",
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
                    if (P11AppearanceTransitionPackageLibraryUtility
                        .TryEnsureFolder(
                            _folder,
                            out var error))
                    {
                        _message =
                            $"Transition package library ready at '{_folder}'.";
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

                foreach (var entry in
                         _entries)
                {
                    if (!P11AppearanceTransitionPackageLibraryUtility
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
                            ? entry.Migrated
                                ? "MIGRATED"
                                : "OK"
                            : "INVALID";
                    var label =
                        $"[{state}] {id}  ({entry.TransitionIds.Length})";
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
                        _selectedAssetPath =
                            entry.AssetPath;
                    }
                }

                if (visible == 0)
                {
                    EditorGUILayout.HelpBox(
                        _entries.Length == 0
                            ? "No transition package JSON files are indexed in this folder."
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
                        "Select a package to inspect metadata and send it to the Timeline.",
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
                    "Source Version",
                    entry.SourceVersion.ToString());
                EditorGUILayout.LabelField(
                    "Effective Version",
                    entry.EffectiveVersion.ToString());
                EditorGUILayout.LabelField(
                    "Migrated",
                    entry.Migrated.ToString());
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
                EditorGUILayout.LabelField(
                    "Transitions",
                    EditorStyles.boldLabel);

                if (entry.TransitionIds.Length == 0)
                {
                    EditorGUILayout.LabelField(
                        "<none>");
                }
                else
                {
                    foreach (var transitionId in
                             entry.TransitionIds)
                    {
                        EditorGUILayout.LabelField(
                            "• " +
                            transitionId);
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
                                "Send to Timeline"))
                        {
                            P11AppearanceTransitionTimelineEditor
                                .OpenWithPackage(
                                    entry.Package,
                                    entry.AssetPath);
                            _message =
                                $"Queued package '{entry.PackageId}' for Timeline import.";
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
                            "Package asset path copied.";
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
                    "Add Appearance Transition Package",
                    Application.dataPath,
                    "json");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            if (!P11AppearanceTransitionPackageLibraryUtility
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

            _selectedAssetPath =
                entry.AssetPath;
            _message =
                $"Added package '{entry.PackageId}' to the transition library.";
            _messageType =
                MessageType.Info;
            RefreshLibrary(
                preserveMessage:
                    true);
        }

        private void RefreshLibrary(
            bool preserveMessage = false)
        {
            if (!P11AppearanceTransitionPackageLibraryUtility
                .TryScan(
                    _folder,
                    out _entries,
                    out var error))
            {
                _entries =
                    Array.Empty<
                        P11AppearanceTransitionPackageLibraryEntry>();

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
                    $"Indexed {_entries.Length} transition package file(s).";
                _messageType =
                    MessageType.Info;
            }

            Repaint();
        }

        private P11AppearanceTransitionPackageLibraryEntry
            FindSelected()
        {
            if (string.IsNullOrWhiteSpace(
                    _selectedAssetPath))
            {
                return null;
            }

            foreach (var entry in
                     _entries)
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
