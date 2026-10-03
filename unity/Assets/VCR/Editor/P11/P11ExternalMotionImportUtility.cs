using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal enum P11ExternalMotionMarkerTimeMode
    {
        Seconds = 0,
        Normalized = 1
    }

    [Serializable]
    internal sealed class P11ExternalMotionMarker
    {
        public string Name;
        public P11ExternalMotionMarkerTimeMode TimeMode =
            P11ExternalMotionMarkerTimeMode.Seconds;
        public float Time;
    }

    [Serializable]
    internal sealed class P11ExternalMotionClipMarkerSet
    {
        public string ClipName;
        public P11ExternalMotionMarker[] Markers =
            Array.Empty<P11ExternalMotionMarker>();
    }

    [Serializable]
    internal sealed class P11ExternalMotionMarkerFile
    {
        public const int CurrentVersion = 1;

        public int Version =
            CurrentVersion;
        public P11ExternalMotionClipMarkerSet[] Clips =
            Array.Empty<P11ExternalMotionClipMarkerSet>();
    }

    internal sealed class P11ExternalMotionImportResult
    {
        public string SourceAssetPath;
        public string MarkerSidecarPath;
        public AnimationClip[] Clips =
            Array.Empty<AnimationClip>();
        public string[] ClipAssetPaths =
            Array.Empty<string>();
        public int ImportedMarkerCount;
    }

    internal static class P11ExternalMotionImportUtility
    {
        public const string DefaultDestinationFolder =
            "Assets/VCR/ImportedMotion";

        private static readonly HashSet<string>
            SupportedExtensions =
                new(
                    new[]
                    {
                        ".fbx",
                        ".dae",
                        ".anim"
                    },
                    StringComparer.OrdinalIgnoreCase);

        public static bool TryImport(
            string sourceFilePath,
            string destinationAssetFolder,
            string markerSidecarPath,
            bool autoDetectSidecar,
            out P11ExternalMotionImportResult result,
            out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    sourceFilePath) ||
                !File.Exists(
                    sourceFilePath))
            {
                error =
                    "External motion file does not exist.";
                return false;
            }

            var extension =
                Path.GetExtension(
                    sourceFilePath);

            if (!SupportedExtensions.Contains(
                    extension))
            {
                error =
                    $"Unsupported external motion format '{extension}'. P11 currently supports Unity-native .fbx, .dae, and .anim input. BVH/glTF require a dedicated adapter.";
                return false;
            }

            destinationAssetFolder =
                NormalizeAssetFolder(
                    destinationAssetFolder);

            var createdFolders =
                new List<string>();

            if (!TryEnsureAssetFolder(
                    destinationAssetFolder,
                    createdFolders,
                    out error))
            {
                RollbackFolders(
                    createdFolders);
                return false;
            }

            if (autoDetectSidecar &&
                string.IsNullOrWhiteSpace(
                    markerSidecarPath))
            {
                markerSidecarPath =
                    FindSidecarPath(
                        sourceFilePath);
            }

            P11ExternalMotionMarkerFile markerFile =
                null;

            if (!string.IsNullOrWhiteSpace(
                    markerSidecarPath))
            {
                if (!File.Exists(
                        markerSidecarPath))
                {
                    error =
                        $"Motion marker sidecar '{markerSidecarPath}' does not exist.";
                    return false;
                }

                if (!TryLoadMarkerFile(
                        markerSidecarPath,
                        out markerFile,
                        out error))
                {
                    return false;
                }
            }

            var createdAssets =
                new List<string>();

            try
            {
                var sourceAssetPath =
                    BuildImportedSourceAssetPath(
                        sourceFilePath,
                        destinationAssetFolder);
                var sourceAbsolutePath =
                    AssetPathToAbsolutePath(
                        sourceAssetPath);

                File.Copy(
                    sourceFilePath,
                    sourceAbsolutePath,
                    overwrite:
                        false);
                createdAssets.Add(
                    sourceAssetPath);

                AssetDatabase.ImportAsset(
                    sourceAssetPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                var importedClips =
                    FindImportedClips(
                        sourceAssetPath);

                if (importedClips.Length == 0)
                {
                    error =
                        $"Imported source '{Path.GetFileName(sourceFilePath)}' contains no usable AnimationClip.";
                    RollbackImport(
                        createdAssets,
                        createdFolders);
                    return false;
                }

                var outputClips =
                    new List<AnimationClip>(
                        importedClips.Length);
                var outputPaths =
                    new List<string>(
                        importedClips.Length);
                var importedMarkerCount = 0;
                var sourceExtension =
                    extension.ToLowerInvariant();

                foreach (var sourceClip in
                         importedClips)
                {
                    AnimationClip outputClip;
                    string outputPath;

                    if (sourceExtension ==
                        ".anim")
                    {
                        outputClip =
                            sourceClip;
                        outputPath =
                            sourceAssetPath;
                    }
                    else
                    {
                        outputPath =
                            BuildExtractedClipAssetPath(
                                destinationAssetFolder,
                                sourceFilePath,
                                sourceClip.name);

                        outputClip =
                            UnityEngine.Object
                                .Instantiate(
                                    sourceClip);
                        outputClip.name =
                            sourceClip.name;
                        outputClip.hideFlags =
                            HideFlags.None;

                        AssetDatabase.CreateAsset(
                            outputClip,
                            outputPath);
                        createdAssets.Add(
                            outputPath);
                    }

                    if (markerFile != null)
                    {
                        if (!TryResolveMarkers(
                                markerFile,
                                outputClip.name,
                                outputClip.length,
                                out var markers,
                                out error))
                        {
                            RollbackImport(
                                createdAssets,
                                createdFolders);
                            return false;
                        }

                        if (markers.Length > 0)
                        {
                            if (!P11MotionMarkerUtility
                                .TryApplyMarkers(
                                    outputClip,
                                    markers,
                                    replaceExistingNames:
                                        true,
                                    out error))
                            {
                                RollbackImport(
                                    createdAssets,
                                    createdFolders);
                                return false;
                            }

                            importedMarkerCount +=
                                markers.Length;
                        }
                    }

                    EditorUtility.SetDirty(
                        outputClip);
                    outputClips.Add(
                        outputClip);
                    outputPaths.Add(
                        outputPath);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                result =
                    new P11ExternalMotionImportResult
                    {
                        SourceAssetPath =
                            sourceAssetPath,
                        MarkerSidecarPath =
                            markerSidecarPath,
                        Clips =
                            outputClips.ToArray(),
                        ClipAssetPaths =
                            outputPaths.ToArray(),
                        ImportedMarkerCount =
                            importedMarkerCount
                    };

                return true;
            }
            catch (Exception exception)
            {
                RollbackImport(
                    createdAssets,
                    createdFolders);
                error =
                    "External motion import failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryParseMarkerFileJson(
            string json,
            out P11ExternalMotionMarkerFile markerFile,
            out string error)
        {
            markerFile = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                error =
                    "Motion marker sidecar JSON is empty.";
                return false;
            }

            try
            {
                markerFile =
                    JsonUtility.FromJson<
                        P11ExternalMotionMarkerFile>(
                        json);
            }
            catch (Exception exception)
            {
                error =
                    "Motion marker sidecar JSON parse failed: " +
                    exception.Message;
                return false;
            }

            if (markerFile == null)
            {
                error =
                    "Motion marker sidecar document is missing.";
                return false;
            }

            if (markerFile.Version !=
                P11ExternalMotionMarkerFile
                    .CurrentVersion)
            {
                error =
                    markerFile.Version >
                    P11ExternalMotionMarkerFile
                        .CurrentVersion
                        ? $"Motion marker sidecar version {markerFile.Version} is newer than supported version {P11ExternalMotionMarkerFile.CurrentVersion}."
                        : $"Motion marker sidecar version {markerFile.Version} is unsupported.";
                return false;
            }

            markerFile.Clips ??=
                Array.Empty<
                    P11ExternalMotionClipMarkerSet>();

            var selectors =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var clipSet in
                     markerFile.Clips)
            {
                if (clipSet == null ||
                    string.IsNullOrWhiteSpace(
                        clipSet.ClipName))
                {
                    error =
                        "Every motion marker clip entry requires a non-empty ClipName or '*'.";
                    return false;
                }

                clipSet.ClipName =
                    clipSet.ClipName.Trim();

                if (!selectors.Add(
                        clipSet.ClipName))
                {
                    error =
                        $"Motion marker sidecar contains duplicate clip selector '{clipSet.ClipName}'.";
                    return false;
                }

                clipSet.Markers ??=
                    Array.Empty<
                        P11ExternalMotionMarker>();
            }

            return true;
        }

        public static bool TryResolveMarkers(
            P11ExternalMotionMarkerFile markerFile,
            string clipName,
            float clipDurationSeconds,
            out BakedMotionCueMarker[] markers,
            out string error)
        {
            markers =
                Array.Empty<BakedMotionCueMarker>();
            error = null;

            if (markerFile == null)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(
                    clipName))
            {
                error =
                    "Imported AnimationClip requires a non-empty name.";
                return false;
            }

            if (float.IsNaN(
                    clipDurationSeconds) ||
                float.IsInfinity(
                    clipDurationSeconds) ||
                clipDurationSeconds < 0f)
            {
                error =
                    "Imported AnimationClip duration is invalid.";
                return false;
            }

            var result =
                new List<BakedMotionCueMarker>();
            var names =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var clipSet in
                     markerFile.Clips ??
                     Array.Empty<
                         P11ExternalMotionClipMarkerSet>())
            {
                if (clipSet == null ||
                    (!string.Equals(
                         clipSet.ClipName,
                         "*",
                         StringComparison.Ordinal) &&
                     !string.Equals(
                         clipSet.ClipName,
                         clipName,
                         StringComparison.Ordinal)))
                {
                    continue;
                }

                foreach (var marker in
                         clipSet.Markers ??
                         Array.Empty<
                             P11ExternalMotionMarker>())
                {
                    if (marker == null ||
                        string.IsNullOrWhiteSpace(
                            marker.Name))
                    {
                        error =
                            $"Motion marker entry for clip '{clipName}' requires a non-empty name.";
                        return false;
                    }

                    var markerName =
                        marker.Name.Trim();

                    if (!names.Add(
                            markerName))
                    {
                        error =
                            $"Motion marker sidecar resolves duplicate marker '{markerName}' for clip '{clipName}'.";
                        return false;
                    }

                    float timeSeconds;

                    switch (marker.TimeMode)
                    {
                        case P11ExternalMotionMarkerTimeMode
                            .Seconds:
                            timeSeconds =
                                marker.Time;
                            break;

                        case P11ExternalMotionMarkerTimeMode
                            .Normalized:
                            if (float.IsNaN(
                                    marker.Time) ||
                                float.IsInfinity(
                                    marker.Time) ||
                                marker.Time < 0f ||
                                marker.Time > 1f)
                            {
                                error =
                                    $"Normalized motion marker '{markerName}' must be within 0..1.";
                                return false;
                            }

                            timeSeconds =
                                marker.Time *
                                clipDurationSeconds;
                            break;

                        default:
                            error =
                                $"Motion marker '{markerName}' uses unsupported time mode '{marker.TimeMode}'.";
                            return false;
                    }

                    if (float.IsNaN(
                            timeSeconds) ||
                        float.IsInfinity(
                            timeSeconds) ||
                        timeSeconds < 0f ||
                        timeSeconds >
                            clipDurationSeconds +
                            0.0001f)
                    {
                        error =
                            $"Motion marker '{markerName}' is outside clip '{clipName}' duration.";
                        return false;
                    }

                    result.Add(
                        new BakedMotionCueMarker
                        {
                            Name =
                                markerName,
                            TimeSeconds =
                                Mathf.Clamp(
                                    timeSeconds,
                                    0f,
                                    clipDurationSeconds)
                        });
                }
            }

            result.Sort(
                (left, right) =>
                {
                    var time =
                        left.TimeSeconds.CompareTo(
                            right.TimeSeconds);

                    return time != 0
                        ? time
                        : string.Compare(
                            left.Name,
                            right.Name,
                            StringComparison.Ordinal);
                });

            markers =
                result.ToArray();
            return true;
        }

        private static bool TryLoadMarkerFile(
            string path,
            out P11ExternalMotionMarkerFile markerFile,
            out string error)
        {
            markerFile = null;
            error = null;

            try
            {
                return TryParseMarkerFileJson(
                    File.ReadAllText(
                        path),
                    out markerFile,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    "Motion marker sidecar read failed: " +
                    exception.Message;
                return false;
            }
        }

        private static AnimationClip[] FindImportedClips(
            string sourceAssetPath)
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(
                    sourceAssetPath)
                .OfType<AnimationClip>()
                .Where(
                    clip =>
                        clip != null &&
                        !string.IsNullOrWhiteSpace(
                            clip.name) &&
                        !clip.name.StartsWith(
                            "__preview__",
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    clip =>
                        clip.name,
                    StringComparer.Ordinal)
                .ToArray();
        }

        private static string BuildImportedSourceAssetPath(
            string sourceFilePath,
            string destinationAssetFolder)
        {
            var fileName =
                SanitizeFileName(
                    Path.GetFileNameWithoutExtension(
                        sourceFilePath)) +
                Path.GetExtension(
                    sourceFilePath)
                    .ToLowerInvariant();

            return AssetDatabase
                .GenerateUniqueAssetPath(
                    destinationAssetFolder +
                    "/" +
                    fileName);
        }

        private static string BuildExtractedClipAssetPath(
            string destinationAssetFolder,
            string sourceFilePath,
            string clipName)
        {
            var sourceName =
                SanitizeFileName(
                    Path.GetFileNameWithoutExtension(
                        sourceFilePath));
            var safeClip =
                SanitizeFileName(
                    clipName);

            return AssetDatabase
                .GenerateUniqueAssetPath(
                    destinationAssetFolder +
                    "/" +
                    sourceName +
                    "__" +
                    safeClip +
                    ".anim");
        }

        private static string FindSidecarPath(
            string sourceFilePath)
        {
            var nextToBase =
                Path.Combine(
                    Path.GetDirectoryName(
                        sourceFilePath) ??
                    string.Empty,
                    Path.GetFileNameWithoutExtension(
                        sourceFilePath) +
                    ".vcrmarkers.json");

            if (File.Exists(
                    nextToBase))
            {
                return nextToBase;
            }

            var nextToFullName =
                sourceFilePath +
                ".vcrmarkers.json";

            return File.Exists(
                    nextToFullName)
                ? nextToFullName
                : null;
        }

        private static string NormalizeAssetFolder(
            string path)
        {
            path =
                string.IsNullOrWhiteSpace(
                    path)
                    ? DefaultDestinationFolder
                    : path.Trim();

            path =
                path.Replace(
                    '\\',
                    '/')
                    .TrimEnd('/');

            return path;
        }

        private static bool TryEnsureAssetFolder(
            string assetFolder,
            ICollection<string> createdFolders,
            out string error)
        {
            error = null;

            if (!string.Equals(
                    assetFolder,
                    "Assets",
                    StringComparison.Ordinal) &&
                !assetFolder.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                error =
                    "Destination folder must be inside the Unity Assets folder.";
                return false;
            }

            if (AssetDatabase.IsValidFolder(
                    assetFolder))
            {
                return true;
            }

            var parts =
                assetFolder.Split(
                    new[]
                    {
                        '/'
                    },
                    StringSplitOptions
                        .RemoveEmptyEntries);

            if (parts.Length == 0 ||
                !string.Equals(
                    parts[0],
                    "Assets",
                    StringComparison.Ordinal))
            {
                error =
                    "Destination folder must begin with Assets.";
                return false;
            }

            var current =
                "Assets";

            for (var i = 1;
                 i < parts.Length;
                 i++)
            {
                var next =
                    current +
                    "/" +
                    parts[i];

                if (!AssetDatabase.IsValidFolder(
                        next))
                {
                    var guid =
                        AssetDatabase.CreateFolder(
                            current,
                            parts[i]);

                    if (string.IsNullOrWhiteSpace(
                            guid))
                    {
                        error =
                            $"Could not create asset folder '{next}'.";
                        return false;
                    }

                    createdFolders?.Add(
                        next);
                }

                current =
                    next;
            }

            return true;
        }

        private static string AssetPathToAbsolutePath(
            string assetPath)
        {
            var projectRoot =
                Directory.GetParent(
                        Application.dataPath)
                    ?.FullName;

            if (string.IsNullOrWhiteSpace(
                    projectRoot))
            {
                throw new InvalidOperationException(
                    "Unity project root could not be resolved.");
            }

            return Path.Combine(
                projectRoot,
                assetPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        private static string SanitizeFileName(
            string value)
        {
            value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "motion"
                    : value.Trim();

            foreach (var invalid in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return value;
        }

        private static void RollbackImport(
            IEnumerable<string> assetPaths,
            IEnumerable<string> createdFolders)
        {
            foreach (var path in
                     assetPaths
                         .Reverse())
            {
                if (string.IsNullOrWhiteSpace(
                        path))
                {
                    continue;
                }

                var removed =
                    AssetDatabase.DeleteAsset(
                        path);

                if (removed)
                {
                    continue;
                }

                try
                {
                    var absolute =
                        AssetPathToAbsolutePath(
                            path);

                    if (File.Exists(
                            absolute))
                    {
                        File.Delete(
                            absolute);
                    }

                    var meta =
                        absolute +
                        ".meta";

                    if (File.Exists(
                            meta))
                    {
                        File.Delete(
                            meta);
                    }
                }
                catch
                {
                }
            }

            RollbackFolders(
                createdFolders);
            AssetDatabase.Refresh();
        }

        private static void RollbackFolders(
            IEnumerable<string> createdFolders)
        {
            if (createdFolders == null)
            {
                return;
            }

            foreach (var folder in
                     createdFolders
                         .Reverse())
            {
                if (!string.IsNullOrWhiteSpace(
                        folder) &&
                    AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                }
            }
        }
    }
}
