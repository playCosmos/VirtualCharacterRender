using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;

namespace VCR.Editor.P11
{
    internal sealed class
        P11AppearanceTransitionPackageLibraryEntry
    {
        public string AssetPath;
        public string PackageId;
        public int SourceVersion;
        public int EffectiveVersion;
        public bool Migrated;
        public string[] TransitionIds =
            Array.Empty<string>();
        public long FileBytes;
        public DateTime LastWriteUtc;
        public AppearanceTransitionPackage Package;
        public string Error;

        public bool Valid =>
            Package != null &&
            string.IsNullOrWhiteSpace(
                Error);
    }

    internal static class
        P11AppearanceTransitionPackageLibraryUtility
    {
        public const string DefaultFolder =
            "Assets/VCR/TransitionPackages";

        public static bool TryScan(
            string assetFolder,
            out P11AppearanceTransitionPackageLibraryEntry[]
                entries,
            out string error)
        {
            entries =
                Array.Empty<
                    P11AppearanceTransitionPackageLibraryEntry>();
            error = null;

            assetFolder =
                NormalizeAssetFolder(
                    assetFolder);

            if (!IsAssetFolderPath(
                    assetFolder))
            {
                error =
                    "Transition package library folder must be inside Assets.";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(
                    assetFolder))
            {
                error =
                    $"Transition package library folder '{assetFolder}' does not exist.";
                return false;
            }

            var result =
                new List<
                    P11AppearanceTransitionPackageLibraryEntry>();
            var guids =
                AssetDatabase.FindAssets(
                    string.Empty,
                    new[]
                    {
                        assetFolder
                    });

            foreach (var guid in guids)
            {
                var assetPath =
                    AssetDatabase.GUIDToAssetPath(
                        guid);

                if (!string.Equals(
                        Path.GetExtension(
                            assetPath),
                        ".json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
                    "Transition package library folder must be inside Assets.";
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
                            $"Could not create transition package folder '{next}'.";
                        return false;
                    }
                }

                current =
                    next;
            }

            return true;
        }

        public static bool TryAddExternalPackage(
            string sourceFilePath,
            string assetFolder,
            out P11AppearanceTransitionPackageLibraryEntry
                entry,
            out string error)
        {
            entry = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    sourceFilePath) ||
                !File.Exists(
                    sourceFilePath))
            {
                error =
                    "Transition package source JSON does not exist.";
                return false;
            }

            if (!string.Equals(
                    Path.GetExtension(
                        sourceFilePath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Transition package library accepts JSON files only.";
                return false;
            }

            string json;

            try
            {
                json =
                    File.ReadAllText(
                        sourceFilePath);
            }
            catch (Exception exception)
            {
                error =
                    "Transition package source read failed: " +
                    exception.Message;
                return false;
            }

            if (!P11AppearanceTransitionPackageUtility
                .TryDeserialize(
                    json,
                    out var package,
                    out error))
            {
                return false;
            }

            if (!TryEnsureFolder(
                    assetFolder,
                    out error))
            {
                return false;
            }

            var safeName =
                SanitizeFileName(
                    package.PackageId);
            var destination =
                AssetDatabase.GenerateUniqueAssetPath(
                    NormalizeAssetFolder(
                        assetFolder) +
                    "/" +
                    safeName +
                    ".json");
            var absoluteDestination =
                AssetPathToAbsolutePath(
                    destination);

            try
            {
                File.Copy(
                    sourceFilePath,
                    absoluteDestination,
                    overwrite:
                        false);
                AssetDatabase.ImportAsset(
                    destination,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);

                entry =
                    ReadEntry(
                        destination);

                if (!entry.Valid)
                {
                    AssetDatabase.DeleteAsset(
                        destination);
                    error =
                        entry.Error ??
                        "Imported transition package failed validation.";
                    entry = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    if (!AssetDatabase.DeleteAsset(
                            destination) &&
                        File.Exists(
                            absoluteDestination))
                    {
                        File.Delete(
                            absoluteDestination);
                    }
                }
                catch
                {
                }

                error =
                    "Transition package library copy failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool MatchesSearch(
            P11AppearanceTransitionPackageLibraryEntry entry,
            string search)
        {
            if (entry == null ||
                string.IsNullOrWhiteSpace(
                    search))
            {
                return entry != null;
            }

            var term =
                search.Trim();

            if (Contains(
                    entry.PackageId,
                    term) ||
                Contains(
                    entry.AssetPath,
                    term) ||
                Contains(
                    entry.Error,
                    term))
            {
                return true;
            }

            foreach (var transitionId in
                     entry.TransitionIds ??
                     Array.Empty<string>())
            {
                if (Contains(
                        transitionId,
                        term))
                {
                    return true;
                }
            }

            return false;
        }

        private static P11AppearanceTransitionPackageLibraryEntry
            ReadEntry(
                string assetPath)
        {
            var entry =
                new P11AppearanceTransitionPackageLibraryEntry
                {
                    AssetPath =
                        assetPath
                };

            try
            {
                var absolute =
                    AssetPathToAbsolutePath(
                        assetPath);
                var json =
                    File.ReadAllText(
                        absolute);
                var source =
                    JsonUtility.FromJson<
                        AppearanceTransitionPackage>(
                        json);

                entry.SourceVersion =
                    source?.Version ??
                    0;

                if (!P11AppearanceTransitionPackageUtility
                    .TryDeserialize(
                        json,
                        out var package,
                        out var error))
                {
                    entry.PackageId =
                        source?.PackageId;
                    entry.EffectiveVersion =
                        source?.Version ??
                        0;
                    entry.Error =
                        error ??
                        "Transition package validation failed.";
                }
                else
                {
                    entry.Package =
                        package;
                    entry.PackageId =
                        package.PackageId;
                    entry.EffectiveVersion =
                        package.Version;
                    entry.Migrated =
                        entry.SourceVersion > 0 &&
                        entry.SourceVersion !=
                            package.Version;

                    var ids =
                        new string[
                            package.Transitions?.Length ??
                            0];

                    for (var i = 0;
                         i < ids.Length;
                         i++)
                    {
                        ids[i] =
                            package.Transitions[i]?.Id ??
                            "<null>";
                    }

                    entry.TransitionIds =
                        ids;
                }

                var info =
                    new FileInfo(
                        absolute);
                entry.FileBytes =
                    info.Exists
                        ? info.Length
                        : 0L;
                entry.LastWriteUtc =
                    info.Exists
                        ? info.LastWriteTimeUtc
                        : DateTime.MinValue;
            }
            catch (Exception exception)
            {
                entry.Error =
                    "Transition package library read failed: " +
                    exception.Message;
            }

            return entry;
        }

        private static int CompareEntries(
            P11AppearanceTransitionPackageLibraryEntry left,
            P11AppearanceTransitionPackageLibraryEntry right)
        {
            if (left == null)
            {
                return right == null
                    ? 0
                    : 1;
            }

            if (right == null)
            {
                return -1;
            }

            if (left.Valid !=
                right.Valid)
            {
                return left.Valid
                    ? -1
                    : 1;
            }

            var byId =
                string.Compare(
                    left.PackageId,
                    right.PackageId,
                    StringComparison.OrdinalIgnoreCase);

            return byId != 0
                ? byId
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
            string path)
        {
            path =
                string.IsNullOrWhiteSpace(
                    path)
                    ? DefaultFolder
                    : path.Trim();

            return path.Replace(
                    '\\',
                    '/')
                .TrimEnd('/');
        }

        private static bool IsAssetFolderPath(
            string path) =>
                string.Equals(
                    path,
                    "Assets",
                    StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(
                     path) &&
                 path.StartsWith(
                     "Assets/",
                     StringComparison.Ordinal));

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
                    ? "appearance-transitions"
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
    }
}
