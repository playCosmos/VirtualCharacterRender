using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P11
{
    public sealed class P11ExternalMotionImporterWindow :
        EditorWindow
    {
        private string _sourcePath;
        private string _sidecarPath;
        private string _destinationFolder =
            P11ExternalMotionImportUtility
                .DefaultDestinationFolder;
        private bool _autoDetectSidecar = true;
        private bool _openCueBaker = true;
        private Vector2 _scroll;
        private string _status;
        private MessageType _statusType =
            MessageType.Info;
        private P11ExternalMotionImportResult
            _lastResult;

        [MenuItem("VCR/P11/Open External Motion Importer")]
        public static void Open()
        {
            GetWindow<
                    P11ExternalMotionImporterWindow>(
                    "VCR External Motion Importer")
                .Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "External Motion Import",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Imports Unity-native motion files (.fbx, .dae, .anim) and registered adapter formats. Built-in BVH converts directly to an additive humanoid BakedMotionCueAsset; glTF motion still requires an adapter. FBX/DAE embedded clips are copied to standalone .anim assets before marker editing.",
                MessageType.Info);

            DrawPathField(
                "Motion File",
                ref _sourcePath,
                BrowseMotionFile);

            _destinationFolder =
                EditorGUILayout.TextField(
                    "Destination",
                    _destinationFolder);

            _autoDetectSidecar =
                EditorGUILayout.Toggle(
                    "Auto-detect Marker Sidecar",
                    _autoDetectSidecar);

            using (new EditorGUI.DisabledScope(
                       _autoDetectSidecar))
            {
                DrawPathField(
                    "Marker Sidecar",
                    ref _sidecarPath,
                    BrowseSidecarFile);
            }

            if (_autoDetectSidecar)
            {
                EditorGUILayout.HelpBox(
                    "Auto-detection checks <motion>.vcrmarkers.json and <basename>.vcrmarkers.json next to the source file.",
                    MessageType.None);
            }

            _openCueBaker =
                EditorGUILayout.Toggle(
                    "Open Cue Baker After Import",
                    _openCueBaker);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           string.IsNullOrWhiteSpace(
                               _sourcePath)))
                {
                    if (GUILayout.Button(
                            "Import Motion"))
                    {
                        ImportMotion();
                    }
                }

                if (GUILayout.Button(
                        "Write Sidecar Template"))
                {
                    WriteSidecarTemplate();
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    _status))
            {
                EditorGUILayout.HelpBox(
                    _status,
                    _statusType);
            }

            DrawLastResult();
        }

        private void DrawLastResult()
        {
            if (_lastResult == null)
            {
                return;
            }

            var clips =
                _lastResult.Clips ??
                Array.Empty<AnimationClip>();
            var cues =
                _lastResult.CueAssets ??
                Array.Empty<
                    VCR.Runtime.Tracking.Mixing
                        .BakedMotionCueAsset>();

            if (clips.Length == 0 &&
                cues.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Imported Motion ({_lastResult.AdapterId ?? "unknown"})",
                EditorStyles.boldLabel);

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll,
                    GUILayout.MaxHeight(
                        260f));

            for (var i = 0;
                 i < clips.Length;
                 i++)
            {
                var clip =
                    clips[i];
                var path =
                    i <
                    _lastResult.ClipAssetPaths.Length
                        ? _lastResult
                            .ClipAssetPaths[i]
                        : string.Empty;

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    EditorGUILayout.ObjectField(
                        clip,
                        typeof(AnimationClip),
                        false);
                    EditorGUILayout.LabelField(
                        path,
                        EditorStyles.miniLabel);

                    if (GUILayout.Button(
                            "Bake",
                            GUILayout.Width(
                                52f)))
                    {
                        P11AnimationClipMotionCueBaker
                            .OpenWithClip(
                                clip);
                    }

                    if (GUILayout.Button(
                            "Timeline",
                            GUILayout.Width(
                                68f)))
                    {
                        P11AppearanceTransitionTimelineEditor
                            .OpenWithMarkerClip(
                                clip);
                    }
                }
            }

            for (var i = 0;
                 i < cues.Length;
                 i++)
            {
                var cue =
                    cues[i];
                var path =
                    i <
                    _lastResult.CueAssetPaths.Length
                        ? _lastResult
                            .CueAssetPaths[i]
                        : string.Empty;

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    EditorGUILayout.ObjectField(
                        cue,
                        typeof(
                            VCR.Runtime.Tracking.Mixing
                                .BakedMotionCueAsset),
                        false);
                    EditorGUILayout.LabelField(
                        path,
                        EditorStyles.miniLabel);

                    if (GUILayout.Button(
                            "Timeline",
                            GUILayout.Width(
                                68f)))
                    {
                        P11AppearanceTransitionTimelineEditor
                            .OpenWithMarkerCue(
                                cue);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawPathField(
            string label,
            ref string value,
            Action browse)
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                value =
                    EditorGUILayout.TextField(
                        label,
                        value);

                if (GUILayout.Button(
                        "Browse",
                        GUILayout.Width(
                            70f)))
                {
                    browse?.Invoke();
                }
            }
        }

        private void BrowseMotionFile()
        {
            var selected =
                EditorUtility.OpenFilePanel(
                    "Select External Motion",
                    InitialDirectory(
                        _sourcePath),
                    string.Empty);

            if (!string.IsNullOrWhiteSpace(
                    selected))
            {
                _sourcePath =
                    selected;
            }
        }

        private void BrowseSidecarFile()
        {
            var selected =
                EditorUtility.OpenFilePanel(
                    "Select Motion Marker Sidecar",
                    InitialDirectory(
                        _sidecarPath),
                    "json");

            if (!string.IsNullOrWhiteSpace(
                    selected))
            {
                _sidecarPath =
                    selected;
            }
        }

        private void ImportMotion()
        {
            if (!P11ExternalMotionImportUtility
                .TryImport(
                    _sourcePath,
                    _destinationFolder,
                    _autoDetectSidecar
                        ? null
                        : _sidecarPath,
                    _autoDetectSidecar,
                    out var result,
                    out var error))
            {
                _lastResult =
                    null;
                _status =
                    error ??
                    "External motion import failed.";
                _statusType =
                    MessageType.Error;
                return;
            }

            _lastResult =
                result;
            var clipCount =
                result.Clips?.Length ?? 0;
            var cueCount =
                result.CueAssets?.Length ?? 0;

            _status =
                $"Imported {clipCount} clip(s), {cueCount} baked cue(s), applied {result.ImportedMarkerCount} sidecar marker(s) through '{result.AdapterId ?? "unknown"}'. Source asset: {result.SourceAssetPath}";
            _statusType =
                MessageType.Info;

            if (clipCount > 0)
            {
                Selection.activeObject =
                    result.Clips[0];
                EditorGUIUtility.PingObject(
                    result.Clips[0]);

                if (_openCueBaker)
                {
                    P11AnimationClipMotionCueBaker
                        .OpenWithClip(
                            result.Clips[0]);
                }
            }
            else if (cueCount > 0)
            {
                Selection.activeObject =
                    result.CueAssets[0];
                EditorGUIUtility.PingObject(
                    result.CueAssets[0]);
                P11AppearanceTransitionTimelineEditor
                    .OpenWithMarkerCue(
                        result.CueAssets[0]);
            }
        }

        private void WriteSidecarTemplate()
        {
            var directory =
                InitialDirectory(
                    _sourcePath);
            var baseName =
                string.IsNullOrWhiteSpace(
                    _sourcePath)
                    ? "motion"
                    : Path.GetFileNameWithoutExtension(
                        _sourcePath);
            var path =
                EditorUtility.SaveFilePanel(
                    "Write VCR Marker Sidecar",
                    directory,
                    baseName +
                    ".vcrmarkers",
                    "json");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            const string template =
@"{
  ""Version"": 1,
  ""Clips"": [
    {
      ""ClipName"": ""*"",
      ""Markers"": [
        {
          ""Name"": ""swap"",
          ""TimeMode"": 1,
          ""Time"": 0.5
        },
        {
          ""Name"": ""motion-end"",
          ""TimeMode"": 1,
          ""Time"": 0.9
        }
      ]
    }
  ]
}";

            try
            {
                File.WriteAllText(
                    path,
                    template);
                _status =
                    $"Wrote marker sidecar template: {path}";
                _statusType =
                    MessageType.Info;
            }
            catch (Exception exception)
            {
                _status =
                    "Could not write sidecar template: " +
                    exception.Message;
                _statusType =
                    MessageType.Error;
            }
        }

        private static string InitialDirectory(
            string path)
        {
            if (!string.IsNullOrWhiteSpace(
                    path))
            {
                if (Directory.Exists(
                        path))
                {
                    return path;
                }

                var directory =
                    Path.GetDirectoryName(
                        path);

                if (!string.IsNullOrWhiteSpace(
                        directory) &&
                    Directory.Exists(
                        directory))
                {
                    return directory;
                }
            }

            return Application.dataPath;
        }
    }
}
