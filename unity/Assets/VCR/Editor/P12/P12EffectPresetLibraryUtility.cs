using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace VCR.Editor.P12
{
    internal sealed class P12EffectPresetLibraryEntry
    {
        public string AssetPath;
        public string EffectId;
        public string PrefabPath;
        public int ParticleSystemCount;
        public bool RestartOnPlay;
        public bool DeactivateOnStop;
        public bool StartInactive;
        public long FileBytes;
        public DateTime LastWriteUtc;
        public P12EffectPresetAsset Preset;
        public string Error;

        public bool Valid =>
            Preset != null &&
            string.IsNullOrWhiteSpace(
                Error);
    }

    internal static class P12EffectPresetLibraryUtility
    {
        public static string DefaultFolder =>
            P12EffectPresetUtility
                .DefaultPresetFolder;

        public static bool TryScan(
            string assetFolder,
            out P12EffectPresetLibraryEntry[] entries,
            out string error)
        {
            entries =
                Array.Empty<
                    P12EffectPresetLibraryEntry>();
            error = null;
            assetFolder =
                NormalizeAssetFolder(
                    assetFolder);

            if (!IsAssetFolderPath(
                    assetFolder))
            {
                error =
                    "Effect preset library folder must be inside Assets.";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(
                    assetFolder))
            {
                error =
                    $"Effect preset library folder '{assetFolder}' does not exist.";
                return false;
            }

            var result =
                new List<
                    P12EffectPresetLibraryEntry>();
            var guids =
                AssetDatabase.FindAssets(
                    "t:P12EffectPresetAsset",
                    new[]
                    {
                        assetFolder
                    });

            foreach (var guid in guids)
            {
                var assetPath =
                    AssetDatabase.GUIDToAssetPath(
                        guid);

                result.Add(
                    ReadEntry(
                        assetPath));
            }

            result.Sort(
                CompareEntries);
            entries =
                result.ToArray();
            return true;
        }

        public static bool TryEnsureFolder(
            string assetFolder,
            out string error)
        {
            error = null;
            assetFolder =
                NormalizeAssetFolder(
                    assetFolder);

            if (!IsAssetFolderPath(
                    assetFolder))
            {
                error =
                    "Effect preset library folder must be inside Assets.";
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
                            $"Could not create effect preset library folder '{next}'.";
                        return false;
                    }
                }

                current =
                    next;
            }

            return true;
        }

        public static bool MatchesSearch(
            P12EffectPresetLibraryEntry entry,
            string search)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    search))
            {
                return true;
            }

            var term =
                search.Trim();

            return
                Contains(
                    entry.EffectId,
                    term) ||
                Contains(
                    entry.AssetPath,
                    term) ||
                Contains(
                    entry.PrefabPath,
                    term) ||
                Contains(
                    entry.Error,
                    term);
        }

        private static P12EffectPresetLibraryEntry
            ReadEntry(
                string assetPath)
        {
            var entry =
                new P12EffectPresetLibraryEntry
                {
                    AssetPath =
                        assetPath
                };

            try
            {
                entry.Preset =
                    AssetDatabase.LoadAssetAtPath<
                        P12EffectPresetAsset>(
                        assetPath);

                if (entry.Preset == null)
                {
                    entry.Error =
                        "Asset did not load as P12EffectPresetAsset.";
                    return entry;
                }

                entry.EffectId =
                    entry.Preset.EffectId;
                entry.RestartOnPlay =
                    entry.Preset.RestartOnPlay;
                entry.DeactivateOnStop =
                    entry.Preset.DeactivateOnStop;
                entry.StartInactive =
                    entry.Preset.StartInactive;
                entry.PrefabPath =
                    entry.Preset.Prefab != null
                        ? AssetDatabase.GetAssetPath(
                            entry.Preset.Prefab)
                        : null;
                entry.ParticleSystemCount =
                    entry.Preset.Prefab != null
                        ? entry.Preset.Prefab
                            .GetComponentsInChildren<
                                UnityEngine.ParticleSystem>(
                                includeInactive:
                                    true)
                            .Length
                        : 0;

                if (!P12EffectPresetUtility
                    .TryValidatePreset(
                        entry.Preset,
                        out var validationError))
                {
                    entry.Error =
                        validationError;
                }

                PopulateFileMetadata(
                    entry);
            }
            catch (Exception exception)
            {
                entry.Error =
                    "Effect preset library read failed: " +
                    exception.Message;
            }

            return entry;
        }

        private static void PopulateFileMetadata(
            P12EffectPresetLibraryEntry entry)
        {
            if (entry == null ||
                string.IsNullOrWhiteSpace(
                    entry.AssetPath))
            {
                return;
            }

            try
            {
                var absolute =
                    AssetPathToAbsolutePath(
                        entry.AssetPath);

                if (!File.Exists(
                        absolute))
                {
                    return;
                }

                var info =
                    new FileInfo(
                        absolute);
                entry.FileBytes =
                    info.Length;
                entry.LastWriteUtc =
                    info.LastWriteTimeUtc;
            }
            catch
            {
            }
        }

        private static int CompareEntries(
            P12EffectPresetLibraryEntry left,
            P12EffectPresetLibraryEntry right)
        {
            var valid =
                right.Valid.CompareTo(
                    left.Valid);

            if (valid != 0)
            {
                return valid;
            }

            var id =
                string.Compare(
                    left.EffectId,
                    right.EffectId,
                    StringComparison.OrdinalIgnoreCase);

            return id != 0
                ? id
                : string.Compare(
                    left.AssetPath,
                    right.AssetPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(
            string value,
            string term) =>
                !string.IsNullOrWhiteSpace(
                    value) &&
                value.IndexOf(
                    term,
                    StringComparison.OrdinalIgnoreCase) >=
                    0;

        private static string NormalizeAssetFolder(
            string value) =>
                string.IsNullOrWhiteSpace(
                    value)
                    ? DefaultFolder
                    : value.Trim()
                        .Replace(
                            '\\',
                            '/')
                        .TrimEnd('/');

        private static bool IsAssetFolderPath(
            string value) =>
                string.Equals(
                    value,
                    "Assets",
                    StringComparison.Ordinal) ||
                value.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal);

        private static string AssetPathToAbsolutePath(
            string assetPath)
        {
            var projectRoot =
                Directory.GetParent(
                        UnityEngine.Application
                            .dataPath)
                    ?.FullName;

            return Path.Combine(
                projectRoot ??
                string.Empty,
                assetPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }
    }
}
