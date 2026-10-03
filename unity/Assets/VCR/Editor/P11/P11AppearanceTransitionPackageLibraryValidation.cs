using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;

namespace VCR.Editor.P11
{
    internal static class
        P11AppearanceTransitionPackageLibraryValidation
    {
        private const string LibraryFolder =
            "Assets/VCR/Editor/P11/__TransitionPackageLibraryValidation";

        public static void RunChecks(
            List<string> failures)
        {
            string externalPath = null;

            try
            {
                DeleteIfExists(
                    LibraryFolder);

                externalPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-transition-library-" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".json");

                var legacyPackage =
                    new AppearanceTransitionPackage
                    {
                        Version = 1,
                        PackageId =
                            "library-legacy",
                        Transitions =
                            new[]
                            {
                                new AppearanceTransitionPreset
                                {
                                    Id =
                                        "library-fade",
                                    DurationSeconds =
                                        0.5,
                                    Steps =
                                        new[]
                                        {
                                            new AppearanceTransitionStep
                                            {
                                                TimeSeconds =
                                                    0.25,
                                                Kind =
                                                    AppearanceTransitionStepKind
                                                        .Commit
                                            }
                                        }
                                }
                            }
                    };

                File.WriteAllText(
                    externalPath,
                    JsonUtility.ToJson(
                        legacyPackage,
                        prettyPrint:
                            true));

                Expect(
                    P11AppearanceTransitionPackageLibraryUtility
                        .TryAddExternalPackage(
                            externalPath,
                            LibraryFolder,
                            out var added,
                            out var addError) &&
                    added != null &&
                    added.Valid &&
                    added.SourceVersion == 1 &&
                    added.EffectiveVersion ==
                        AppearanceTransitionPackage
                            .CurrentVersion &&
                    added.Migrated &&
                    added.TransitionIds.Length ==
                        1 &&
                    added.TransitionIds[0] ==
                        "library-fade",
                    "transition package library must add and index a migratable v1 package as effective v2: " +
                    addError,
                    failures);

                var invalidAssetPath =
                    LibraryFolder +
                    "/future-package.json";
                File.WriteAllText(
                    AssetPathToAbsolutePath(
                        invalidAssetPath),
                    "{\"Version\":999,\"PackageId\":\"future\",\"Transitions\":[]}");
                AssetDatabase.ImportAsset(
                    invalidAssetPath,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);

                Expect(
                    P11AppearanceTransitionPackageLibraryUtility
                        .TryScan(
                            LibraryFolder,
                            out var scanned,
                            out var scanError) &&
                    scanned.Length ==
                        2,
                    "transition package library scan must keep both valid and invalid JSON entries visible: " +
                    scanError,
                    failures);

                P11AppearanceTransitionPackageLibraryEntry
                    valid = null;
                P11AppearanceTransitionPackageLibraryEntry
                    invalid = null;

                if (P11AppearanceTransitionPackageLibraryUtility
                    .TryScan(
                        LibraryFolder,
                        out var entries,
                        out _))
                {
                    foreach (var entry in
                             entries)
                    {
                        if (entry.Valid)
                        {
                            valid =
                                entry;
                        }
                        else
                        {
                            invalid =
                                entry;
                        }
                    }
                }

                Expect(
                    valid != null &&
                    invalid != null &&
                    invalid.Error != null &&
                    invalid.Error.IndexOf(
                        "newer",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "transition package library must mark unsupported newer versions invalid without dropping the file",
                    failures);

                Expect(
                    valid != null &&
                    P11AppearanceTransitionPackageLibraryUtility
                        .MatchesSearch(
                            valid,
                            "library-fade") &&
                    P11AppearanceTransitionPackageLibraryUtility
                        .MatchesSearch(
                            valid,
                            "library-legacy") &&
                    !P11AppearanceTransitionPackageLibraryUtility
                        .MatchesSearch(
                            valid,
                            "does-not-exist"),
                    "transition package library search must match package ids and transition ids",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "transition package library validation unexpected exception: " +
                    exception);
            }
            finally
            {
                DeleteIfExists(
                    LibraryFolder);

                if (!string.IsNullOrWhiteSpace(
                        externalPath) &&
                    File.Exists(
                        externalPath))
                {
                    try
                    {
                        File.Delete(
                            externalPath);
                    }
                    catch
                    {
                    }
                }

                AssetDatabase.Refresh();
            }
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

        private static void DeleteIfExists(
            string assetPath)
        {
            if (AssetDatabase.IsValidFolder(
                    assetPath) ||
                AssetDatabase.LoadMainAssetAtPath(
                    assetPath) !=
                null)
            {
                AssetDatabase.DeleteAsset(
                    assetPath);
            }
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }
}
