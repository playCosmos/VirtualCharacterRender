using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P11
{
    internal static class P11ExternalMotionImportValidation
    {
        private const string SourceAssetPath =
            "Assets/VCR/Editor/P11/__ExternalMotionValidation.anim";
        private const string ImportFolder =
            "Assets/VCR/Editor/P11/__ExternalMotionValidationImported";

        public static void RunChecks(
            List<string> failures)
        {
            AnimationClip sourceClip = null;
            string sidecarPath = null;

            try
            {
                sourceClip =
                    new AnimationClip
                    {
                        name =
                            "ExternalMotionValidation"
                    };
                sourceClip.SetCurve(
                    string.Empty,
                    typeof(Transform),
                    "localPosition.x",
                    AnimationCurve.Linear(
                        0f,
                        0f,
                        1f,
                        1f));

                DeleteIfExists(
                    SourceAssetPath);
                DeleteIfExists(
                    ImportFolder);

                AssetDatabase.CreateAsset(
                    sourceClip,
                    SourceAssetPath);
                AssetDatabase.SaveAssets();

                sidecarPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-external-motion-validation-" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".vcrmarkers.json");

                File.WriteAllText(
                    sidecarPath,
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
        }
      ]
    },
    {
      ""ClipName"": ""ExternalMotionValidation"",
      ""Markers"": [
        {
          ""Name"": ""motion-end"",
          ""TimeMode"": 0,
          ""Time"": 0.9
        }
      ]
    }
  ]
}");

                var absoluteSource =
                    AssetPathToAbsolutePath(
                        SourceAssetPath);

                Expect(
                    P11ExternalMotionImportUtility
                        .TryImport(
                            absoluteSource,
                            ImportFolder,
                            sidecarPath,
                            autoDetectSidecar:
                                false,
                            out var importResult,
                            out var importError),
                    "external standalone .anim import with marker sidecar must succeed: " +
                    importError,
                    failures);

                if (importResult != null &&
                    importResult.Clips.Length ==
                        1)
                {
                    Expect(
                        importResult.ImportedMarkerCount ==
                            2 &&
                        P11MotionMarkerUtility
                            .TryExtractFromAnimationClip(
                                importResult.Clips[0],
                                out var importedMarkers,
                                out var markerError) &&
                        importedMarkers.Length ==
                            2 &&
                        importedMarkers[0].Name ==
                            "swap" &&
                        Math.Abs(
                            importedMarkers[0]
                                .TimeSeconds -
                            0.5f) <
                            0.001f &&
                        importedMarkers[1].Name ==
                            "motion-end" &&
                        Math.Abs(
                            importedMarkers[1]
                                .TimeSeconds -
                            0.9f) <
                            0.001f,
                        "external motion import must resolve normalized/seconds sidecar markers and apply them to the standalone clip: " +
                        markerError,
                        failures);
                }
                else
                {
                    failures.Add(
                        "external motion import must return exactly one standalone clip for a .anim source");
                }

                const string duplicateJson =
@"{
  ""Version"": 1,
  ""Clips"": [
    {
      ""ClipName"": ""*"",
      ""Markers"": [
        { ""Name"": ""swap"", ""TimeMode"": 0, ""Time"": 0.2 }
      ]
    },
    {
      ""ClipName"": ""ExternalMotionValidation"",
      ""Markers"": [
        { ""Name"": ""swap"", ""TimeMode"": 0, ""Time"": 0.4 }
      ]
    }
  ]
}";

                Expect(
                    P11ExternalMotionImportUtility
                        .TryParseMarkerFileJson(
                            duplicateJson,
                            out var duplicateFile,
                            out var duplicateParseError) &&
                    !P11ExternalMotionImportUtility
                        .TryResolveMarkers(
                            duplicateFile,
                            "ExternalMotionValidation",
                            1f,
                            out _,
                            out var duplicateResolveError) &&
                    duplicateResolveError != null &&
                    duplicateResolveError.Contains(
                        "duplicate",
                        StringComparison.OrdinalIgnoreCase),
                    "wildcard and exact sidecar entries must reject duplicate resolved marker names: " +
                    duplicateParseError +
                    " / " +
                    duplicateResolveError,
                    failures);

                var newerJson =
                    "{\"Version\":" +
                    (P11ExternalMotionMarkerFile
                         .CurrentVersion +
                     1) +
                    ",\"Clips\":[]}";

                Expect(
                    !P11ExternalMotionImportUtility
                        .TryParseMarkerFileJson(
                            newerJson,
                            out _,
                            out var newerError) &&
                    newerError != null &&
                    newerError.Contains(
                        "newer",
                        StringComparison.OrdinalIgnoreCase),
                    "external motion marker sidecar must reject unsupported newer versions",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "external motion import validation unexpected exception: " +
                    exception);
            }
            finally
            {
                DeleteIfExists(
                    ImportFolder);
                DeleteIfExists(
                    SourceAssetPath);

                if (!string.IsNullOrWhiteSpace(
                        sidecarPath) &&
                    File.Exists(
                        sidecarPath))
                {
                    try
                    {
                        File.Delete(
                            sidecarPath);
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

            return Path.Combine(
                projectRoot ??
                string.Empty,
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
                    assetPath) != null)
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
