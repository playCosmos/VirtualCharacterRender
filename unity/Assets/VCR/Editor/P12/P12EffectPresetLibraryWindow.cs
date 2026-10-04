using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P12
{
    public sealed class P12EffectPresetLibraryWindow :
        EditorWindow
    {
        private string _folder =
            P12EffectPresetLibraryUtility
                .DefaultFolder;
        private string _search;
        private string _selectedAssetPath;
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;
        private P12EffectPresetLibraryEntry[] _entries =
            Array.Empty<
                P12EffectPresetLibraryEntry>();
        private string _message;
        private MessageType _messageType =
            MessageType.Info;

        [MenuItem("VCR/P12/Open Effect Preset Library")]
        public static void Open()
        {
            GetWindow<
                    P12EffectPresetLibraryWindow>(
                    "VCR Effect Presets")
                .Show();
        }

        private void OnEnable()
        {
            RefreshLibrary();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Effect Preset Library",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Indexes project-local P12EffectPresetAsset files. Validation status is shown before a preset is handed to Scene Automation Authoring. This library does not import external executable prefab packages.",
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
                    if (P12EffectPresetLibraryUtility
                        .TryEnsureFolder(
                            _folder,
                            out var error))
                    {
                        _message =
                            $"Effect preset library ready at '{_folder}'.";
                        _messageType =
                            MessageType.Info;
                        RefreshLibrary(
                            preserveMessage:
                                true);
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
            }
        }

        private void DrawLibraryList()
        {
            using (new EditorGUILayout
                       .VerticalScope(
                           GUILayout.Width(
                               Mathf.Max(
                                   310f,
                                   position.width *
                                   0.44f))))
            {
                EditorGUILayout.LabelField(
                    $"Presets ({_entries.Length})",
                    EditorStyles.boldLabel);

                _listScroll =
                    EditorGUILayout.BeginScrollView(
                        _listScroll);

                var visible = 0;

                foreach (var entry in _entries)
                {
                    if (!P12EffectPresetLibraryUtility
                        .MatchesSearch(
                            entry,
                            _search))
                    {
                        continue;
                    }

                    visible++;

                    var id =
                        string.IsNullOrWhiteSpace(
                            entry.EffectId)
                            ? Path.GetFileNameWithoutExtension(
                                entry.AssetPath)
                            : entry.EffectId;
                    var state =
                        entry.Valid
                            ? "OK"
                            : "INVALID";
                    var label =
                        $"[{state}] {id}  ({entry.ParticleSystemCount} ps)";
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
                            ? "No effect preset assets are indexed in this folder."
                            : "No effect preset matches the current search.",
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
                    "Selected Preset",
                    EditorStyles.boldLabel);

                var entry =
                    FindSelected();

                if (entry == null)
                {
                    EditorGUILayout.HelpBox(
                        "Select an effect preset to inspect it and send it to Scene Automation Authoring.",
                        MessageType.None);
                    return;
                }

                _detailsScroll =
                    EditorGUILayout.BeginScrollView(
                        _detailsScroll);

                EditorGUILayout.LabelField(
                    "Effect ID",
                    entry.EffectId ??
                    "<missing>");
                EditorGUILayout.LabelField(
                    "Asset Path",
                    entry.AssetPath ??
                    "<none>");
                EditorGUILayout.LabelField(
                    "Prefab",
                    entry.PrefabPath ??
                    "<missing>");
                EditorGUILayout.LabelField(
                    "Particle Systems",
                    entry.ParticleSystemCount
                        .ToString());
                EditorGUILayout.LabelField(
                    "Restart On Play",
                    entry.RestartOnPlay
                        .ToString());
                EditorGUILayout.LabelField(
                    "Deactivate On Stop",
                    entry.DeactivateOnStop
                        .ToString());
                EditorGUILayout.LabelField(
                    "Start Inactive",
                    entry.StartInactive
                        .ToString());
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

                if (!entry.Valid)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        entry.Error ??
                        "Effect preset validation failed.",
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
                                "Send to Scene Automation"))
                        {
                            P12SceneAutomationAuthoringWindow
                                .OpenWithEffectPreset(
                                    entry.Preset);
                            _message =
                                $"Sent effect preset '{entry.EffectId}' to Scene Automation Authoring.";
                            _messageType =
                                MessageType.Info;
                        }
                    }

                    if (GUILayout.Button(
                            "Ping Preset"))
                    {
                        Ping(
                            entry.Preset);
                    }

                    using (new EditorGUI.DisabledScope(
                               entry.Preset?.Prefab ==
                               null))
                    {
                        if (GUILayout.Button(
                                "Ping Prefab"))
                        {
                            Ping(
                                entry.Preset.Prefab);
                        }
                    }

                    if (GUILayout.Button(
                            "Copy Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer =
                            entry.AssetPath ??
                            string.Empty;
                        _message =
                            "Effect preset asset path copied.";
                        _messageType =
                            MessageType.Info;
                    }
                }
            }
        }

        private void RefreshLibrary(
            bool preserveMessage = false)
        {
            if (!P12EffectPresetLibraryUtility
                .TryScan(
                    _folder,
                    out _entries,
                    out var error))
            {
                _entries =
                    Array.Empty<
                        P12EffectPresetLibraryEntry>();

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
                    $"Indexed {_entries.Length} effect preset asset(s).";
                _messageType =
                    MessageType.Info;
            }

            Repaint();
        }

        private P12EffectPresetLibraryEntry
            FindSelected()
        {
            if (string.IsNullOrWhiteSpace(
                    _selectedAssetPath))
            {
                return null;
            }

            return Array.Find(
                _entries,
                entry =>
                    entry != null &&
                    string.Equals(
                        entry.AssetPath,
                        _selectedAssetPath,
                        StringComparison.Ordinal));
        }

        private static void Ping(
            UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            Selection.activeObject =
                asset;
            EditorGUIUtility.PingObject(
                asset);
        }

        private static string FormatBytes(
            long bytes)
        {
            if (bytes <= 0)
            {
                return "<unknown>";
            }

            if (bytes < 1024)
            {
                return bytes +
                    " B";
            }

            var kb =
                bytes /
                1024.0;

            if (kb < 1024.0)
            {
                return kb.ToString(
                    "0.0") +
                    " KB";
            }

            return (kb / 1024.0)
                .ToString(
                    "0.0") +
                " MB";
        }
    }
}
