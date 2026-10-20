using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P12
{
    internal sealed class P12EventRuleLibraryEntry
    {
        public string AssetPath;
        public string PackageId;
        public int Version;
        public string Description;
        public string[] Tags =
            Array.Empty<string>();
        public int Revision;
        public string[] RuleIds =
            Array.Empty<string>();
        public long FileBytes;
        public DateTime LastWriteUtc;
        public P12EventRuleLibraryPackage Package;
        public string Error;

        public bool Valid =>
            Package != null &&
            string.IsNullOrWhiteSpace(
                Error);
    }

    internal static class P12EventRuleLibraryBrowserUtility
    {
        public const string DefaultFolder =
            "Assets/VCR/EventRuleLibraries";

        public static bool TryScan(
            string assetFolder,
            out P12EventRuleLibraryEntry[] entries,
            out string error)
        {
            entries =
                Array.Empty<P12EventRuleLibraryEntry>();
            error = null;
            assetFolder =
                NormalizeAssetFolder(
                    assetFolder);

            if (!IsAssetFolderPath(
                    assetFolder))
            {
                error =
                    "Event rule library folder must be inside Assets.";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(
                    assetFolder))
            {
                error =
                    $"Event rule library folder '{assetFolder}' does not exist.";
                return false;
            }

            var result =
                new List<P12EventRuleLibraryEntry>();
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
                    "Event rule library folder must be inside Assets.";
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
                            $"Could not create event rule library folder '{next}'.";
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
            out P12EventRuleLibraryEntry entry,
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
                    "Event rule library source JSON does not exist.";
                return false;
            }

            if (!string.Equals(
                    Path.GetExtension(
                        sourceFilePath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Event rule library accepts JSON files only.";
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
                    "Event rule library source read failed: " +
                    exception.Message;
                return false;
            }

            if (!P12EventRuleLibraryUtility
                .TryParse(
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
                        "Imported event rule package failed validation.";
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
                    "Event rule library copy failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryGetRevisionHistory(
            P12EventRuleLibraryEntry[] entries,
            P12EventRuleLibraryEntry selected,
            out P12EventRuleLibraryEntry[] history,
            out string error)
        {
            history =
                Array.Empty<P12EventRuleLibraryEntry>();
            error = null;

            if (selected == null ||
                !selected.Valid ||
                string.IsNullOrWhiteSpace(
                    selected.PackageId))
            {
                error =
                    "A valid event rule package with PackageId is required for revision history.";
                return false;
            }

            var matches =
                new List<
                    P12EventRuleLibraryEntry>();

            foreach (var entry in
                     entries ??
                     Array.Empty<
                         P12EventRuleLibraryEntry>())
            {
                if (entry == null ||
                    !entry.Valid ||
                    !string.Equals(
                        entry.PackageId,
                        selected.PackageId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(
                    entry);
            }

            matches.Sort(
                (left, right) =>
                {
                    var byRevision =
                        left.Revision.CompareTo(
                            right.Revision);

                    return byRevision != 0
                        ? byRevision
                        : string.Compare(
                            left.AssetPath,
                            right.AssetPath,
                            StringComparison.Ordinal);
                });

            for (var i = 1;
                 i < matches.Count;
                 i++)
            {
                if (matches[i - 1].Revision ==
                    matches[i].Revision)
                {
                    error =
                        $"Event rule package '{selected.PackageId}' has multiple valid files for revision {matches[i].Revision}; revision history is ambiguous.";
                    return false;
                }
            }

            history =
                matches.ToArray();
            return true;
        }

        public static bool TryFindAdjacentRevision(
            P12EventRuleLibraryEntry[] entries,
            P12EventRuleLibraryEntry selected,
            int direction,
            out P12EventRuleLibraryEntry adjacent,
            out string error)
        {
            adjacent = null;

            if (!TryGetRevisionHistory(
                    entries,
                    selected,
                    out var history,
                    out error))
            {
                return false;
            }

            if (direction == 0)
            {
                error =
                    "Revision navigation direction cannot be zero.";
                return false;
            }

            var selectedIndex =
                Array.FindIndex(
                    history,
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.AssetPath,
                            selected.AssetPath,
                            StringComparison.Ordinal));

            if (selectedIndex < 0)
            {
                error =
                    "Selected event rule package is not present in its revision history.";
                return false;
            }

            var targetIndex =
                selectedIndex +
                (direction < 0
                    ? -1
                    : 1);

            if (targetIndex < 0 ||
                targetIndex >=
                history.Length)
            {
                return true;
            }

            adjacent =
                history[
                    targetIndex];
            return true;
        }

        public static bool TryFindPreviousRevision(
            P12EventRuleLibraryEntry[] entries,
            P12EventRuleLibraryEntry selected,
            out P12EventRuleLibraryEntry previous,
            out string error) =>
                TryFindAdjacentRevision(
                    entries,
                    selected,
                    -1,
                    out previous,
                    out error);

        public static bool TryFindNextRevision(
            P12EventRuleLibraryEntry[] entries,
            P12EventRuleLibraryEntry selected,
            out P12EventRuleLibraryEntry next,
            out string error) =>
                TryFindAdjacentRevision(
                    entries,
                    selected,
                    1,
                    out next,
                    out error);

        public static bool MatchesSearch(
            P12EventRuleLibraryEntry entry,
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
                    entry.Description,
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

            foreach (var tag in
                     entry.Tags ??
                     Array.Empty<string>())
            {
                if (Contains(
                        tag,
                        term))
                {
                    return true;
                }
            }

            foreach (var ruleId in
                     entry.RuleIds ??
                     Array.Empty<string>())
            {
                if (Contains(
                        ruleId,
                        term))
                {
                    return true;
                }
            }

            return false;
        }

        private static P12EventRuleLibraryEntry ReadEntry(
            string assetPath)
        {
            var entry =
                new P12EventRuleLibraryEntry
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
                        P12EventRuleLibraryPackage>(
                        json);
                entry.PackageId =
                    source?.PackageId;
                entry.Version =
                    source?.Version ??
                    0;
                entry.Description =
                    source?.Description;
                entry.Tags =
                    source?.Tags ??
                    Array.Empty<string>();
                entry.Revision =
                    source?.Revision ??
                    0;

                if (!P12EventRuleLibraryUtility
                    .TryParse(
                        json,
                        out var package,
                        out var error))
                {
                    entry.Error =
                        error ??
                        "Event rule library validation failed.";
                }
                else
                {
                    entry.Package =
                        package;
                    entry.PackageId =
                        package.PackageId;
                    entry.Version =
                        package.Version;
                    entry.Description =
                        package.Description;
                    entry.Tags =
                        package.Tags ??
                        Array.Empty<string>();
                    entry.Revision =
                        package.Revision;
                    var rules =
                        package.Rules ??
                        Array.Empty<
                            VCR.Runtime.EventRuntime.EventRuntimeRule>();
                    var ids =
                        new string[
                            rules.Length];

                    for (var i = 0;
                         i < ids.Length;
                         i++)
                    {
                        ids[i] =
                            rules[i]?.Id ??
                            "<null>";
                    }

                    entry.RuleIds =
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
                    "Event rule library read failed: " +
                    exception.Message;
            }

            return entry;
        }

        private static int CompareEntries(
            P12EventRuleLibraryEntry left,
            P12EventRuleLibraryEntry right)
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
                    ? "event-rules"
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
