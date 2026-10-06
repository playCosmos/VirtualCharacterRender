using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal static class P11ExternalMotionImportValidation
    {
        private const string SourceAssetPath =
            "Assets/VCR/Editor/P11/__ExternalMotionValidation.anim";
        private const string ImportFolder =
            "Assets/VCR/Editor/P11/__ExternalMotionValidationImported";
        private const string RollbackFolder =
            "Assets/VCR/Editor/P11/__ExternalMotionValidationRollback";
        private const string BvhImportFolder =
            "Assets/VCR/Editor/P11/__ExternalMotionValidationBvh";

        public static void RunChecks(
            List<string> failures)
        {
            RunBakedMotionCueOwnershipChecks(
                failures);

            AnimationClip sourceClip = null;
            string sidecarPath = null;
            string bvhPath = null;
            string bvhSidecarPath = null;

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
                DeleteIfExists(
                    RollbackFolder);
                DeleteIfExists(
                    BvhImportFolder);

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
                    BakedMotionCueMarker[]
                        importedMarkers =
                            Array.Empty<
                                BakedMotionCueMarker>();
                    string markerError =
                        null;
                    var markersExtracted =
                        P11MotionMarkerUtility
                            .TryExtractFromAnimationClip(
                                importResult.Clips[0],
                                out importedMarkers,
                                out markerError);

                    Expect(
                        importResult.ImportedMarkerCount ==
                            2 &&
                        markersExtracted &&
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

                P11ExternalMotionMarkerFile
                    duplicateFile = null;
                string duplicateParseError =
                    null;
                string duplicateResolveError =
                    null;
                var duplicateParsed =
                    P11ExternalMotionImportUtility
                        .TryParseMarkerFileJson(
                            duplicateJson,
                            out duplicateFile,
                            out duplicateParseError);
                var duplicateRejected =
                    duplicateParsed &&
                    !P11ExternalMotionImportUtility
                        .TryResolveMarkers(
                            duplicateFile,
                            "ExternalMotionValidation",
                            1f,
                            out _,
                            out duplicateResolveError);

                Expect(
                    duplicateRejected &&
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

                bvhPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-external-motion-validation-" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".bvh");
                bvhSidecarPath =
                    bvhPath +
                    ".vcrmarkers.json";

                File.WriteAllText(
                    bvhPath,
@"HIERARCHY
ROOT Hips
{
  OFFSET 0 0 0
  CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation
  JOINT Chest
  {
    OFFSET 0 10 0
    CHANNELS 3 Zrotation Xrotation Yrotation
    JOINT Head
    {
      OFFSET 0 10 0
      CHANNELS 3 Zrotation Xrotation Yrotation
      End Site
      {
        OFFSET 0 5 0
      }
    }
  }
}
MOTION
Frames: 3
Frame Time: 0.5
0 0 0 0 0 0  0 0 0  0 0 0
100 0 0 0 10 0  0 20 0  0 0 5
200 0 0 0 20 0  0 40 0  0 0 10
");

                File.WriteAllText(
                    bvhSidecarPath,
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
    }
  ]
}");

                Expect(
                    P11ExternalMotionImportUtility
                        .TryImport(
                            bvhPath,
                            BvhImportFolder,
                            bvhSidecarPath,
                            autoDetectSidecar:
                                false,
                            new P11ExternalMotionImportOptions
                            {
                                BvhPositionScale =
                                    0.02f,
                                BvhMirrorX =
                                    false
                            },
                            out var bvhResult,
                            out var bvhError),
                    "BVH adapter import must succeed through the shared external motion pipeline: " +
                    bvhError,
                    failures);

                if (bvhResult != null &&
                    bvhResult.CueAssets.Length ==
                        1 &&
                    bvhResult.CueAssets[0] !=
                        null)
                {
                    var cue =
                        bvhResult.CueAssets[0]
                            .Cue;
                    var hipsFound =
                        false;
                    var chestFound =
                        false;
                    var headFound =
                        false;

                    foreach (var track in
                             cue?.Bones ??
                             Array.Empty<
                                 BakedBoneMotionCueTrack>())
                    {
                        hipsFound |=
                            track != null &&
                            track.Bone ==
                            VCR.Runtime.Tracking
                                .HumanoidBoneId.Hips;
                        chestFound |=
                            track != null &&
                            track.Bone ==
                            VCR.Runtime.Tracking
                                .HumanoidBoneId.Chest;
                        headFound |=
                            track != null &&
                            track.Bone ==
                            VCR.Runtime.Tracking
                                .HumanoidBoneId.Head;
                    }

                    Expect(
                        bvhResult.AdapterId ==
                            "bvh" &&
                        bvhResult.SourceAssetPath !=
                            null &&
                        bvhResult.SourceAssetPath
                            .EndsWith(
                                ".bvh.bytes",
                                StringComparison.OrdinalIgnoreCase) &&
                        cue != null &&
                        cue.FrameCount ==
                            3 &&
                        Math.Abs(
                            cue.DurationSeconds -
                            1.0) <
                            0.001 &&
                        cue.RootPositionOffsets
                            .Length ==
                            3 &&
                        Math.Abs(
                            cue.RootPositionOffsets[1]
                                .x -
                            2.0f) <
                            0.001f &&
                        !hipsFound &&
                        chestFound &&
                        headFound &&
                        cue.RootRotationOffsets.Length ==
                            3 &&
                        Math.Abs(
                            cue.RootRotationOffsets[1]
                                .x) >
                            0.05f &&
                        cue.Markers.Length ==
                            1 &&
                        cue.Markers[0].Name ==
                            "swap" &&
                        Math.Abs(
                            cue.Markers[0]
                                .TimeSeconds -
                            0.5f) <
                            0.001f,
                        "BVH adapter must create a three-frame additive humanoid cue, honor position-scale/handedness options, archive source bytes, avoid duplicating ROOT as a Hips track, preserve root rotation, map common bones, and preserve sidecar markers",
                        failures);
                }
                else
                {
                    failures.Add(
                        "BVH adapter import must return exactly one baked cue asset");
                }

                var invalidSidecarPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        "vcr-external-motion-validation-invalid-" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".vcrmarkers.json");

                try
                {
                    File.WriteAllText(
                        invalidSidecarPath,
@"{
  ""Version"": 1,
  ""Clips"": [
    {
      ""ClipName"": ""*"",
      ""Markers"": [
        {
          ""Name"": ""outside"",
          ""TimeMode"": 0,
          ""Time"": 2.0
        }
      ]
    }
  ]
}");

                    Expect(
                        !P11ExternalMotionImportUtility
                            .TryImport(
                                absoluteSource,
                                RollbackFolder,
                                invalidSidecarPath,
                                autoDetectSidecar:
                                    false,
                                out _,
                                out var rollbackError) &&
                        rollbackError != null &&
                        rollbackError.Contains(
                            "outside",
                            StringComparison.OrdinalIgnoreCase) &&
                        !AssetDatabase.IsValidFolder(
                            RollbackFolder),
                        "failed external motion import must roll back newly created assets and destination folder: " +
                        rollbackError,
                        failures);
                }
                finally
                {
                    if (File.Exists(
                            invalidSidecarPath))
                    {
                        try
                        {
                            File.Delete(
                                invalidSidecarPath);
                        }
                        catch
                        {
                        }
                    }
                }
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
                    RollbackFolder);
                DeleteIfExists(
                    BvhImportFolder);
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

                foreach (var tempPath in
                         new[]
                         {
                             bvhPath,
                             bvhSidecarPath
                         })
                {
                    if (!string.IsNullOrWhiteSpace(
                            tempPath) &&
                        File.Exists(
                            tempPath))
                    {
                        try
                        {
                            File.Delete(
                                tempPath);
                        }
                        catch
                        {
                        }
                    }
                }

                AssetDatabase.Refresh();
            }
        }

        private static void RunBakedMotionCueOwnershipChecks(
            List<string> failures)
        {
            GameObject root = null;
            BakedMotionCueAsset asset = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Baked Motion Cue Ownership");
                var source =
                    root.AddComponent<
                        BakedMotionCueSource>();

                asset =
                    ScriptableObject.CreateInstance<
                        BakedMotionCueAsset>();

                var callerOwnedCue =
                    CreateValidationCue(
                        "owned-asset-cue");
                asset.SetCue(
                    callerOwnedCue);

                callerOwnedCue.CueId =
                    "caller-mutated-asset-cue";
                callerOwnedCue
                    .RootPositionOffsets[0] =
                        new Vector3(
                            9f,
                            0f,
                            0f);

                var exportedCue =
                    asset.Cue;
                exportedCue.CueId =
                    "export-mutated-asset-cue";
                exportedCue
                    .RootPositionOffsets[0] =
                        new Vector3(
                            7f,
                            0f,
                            0f);

                var recapturedCue =
                    asset.Cue;

                Expect(
                    recapturedCue.CueId ==
                        "owned-asset-cue" &&
                    recapturedCue
                        .RootPositionOffsets[0] ==
                        Vector3.zero,
                    "baked motion cue assets must deep-clone SetCue input and Cue output so caller mutation cannot rewrite serialized cue data",
                    failures);

                source.ConfigureAssets(
                    asset);

                var cueIdMutationRejected =
                    false;
                var cueAssetMutationRejected =
                    false;

                try
                {
                    ((IList<string>)
                        source.CueIds)
                        .Add(
                            "external-cue-id");
                }
                catch (NotSupportedException)
                {
                    cueIdMutationRejected =
                        true;
                }

                try
                {
                    ((IList<BakedMotionCueAsset>)
                        source.CueAssets)
                        .Add(
                            asset);
                }
                catch (NotSupportedException)
                {
                    cueAssetMutationRejected =
                        true;
                }

                Expect(
                    cueIdMutationRejected &&
                    cueAssetMutationRejected &&
                    source.CueIds.Count == 1 &&
                    source.CueIds[0] ==
                        "owned-asset-cue",
                    "baked motion cue source must expose read-only cue id/asset views instead of mutable backing collections",
                    failures);

                asset.SetCue(
                    CreateInvalidValidationCue(
                        "invalid-asset-cue"));

                var invalidAssetRejected =
                    !source.RebuildCues(
                        out var invalidAssetError);
                var previousAssetCueStillSamples =
                    source.TrySampleCue(
                        "owned-asset-cue",
                        0.5f,
                        out var previousAssetPose,
                        out var previousAssetSampleError);

                Expect(
                    invalidAssetRejected &&
                    !string.IsNullOrWhiteSpace(
                        invalidAssetError) &&
                    source.CueIds.Count == 1 &&
                    source.CueIds[0] ==
                        "owned-asset-cue" &&
                    previousAssetCueStillSamples &&
                    previousAssetPose != null &&
                    string.IsNullOrWhiteSpace(
                        previousAssetSampleError),
                    "failed baked cue asset rebuild must keep the previous validated live cue set intact",
                    failures);

                var runtimeCue =
                    CreateValidationCue(
                        "runtime-owned-cue");

                source.ConfigureCues(
                    runtimeCue);

                runtimeCue.CueId =
                    "caller-mutated-runtime-cue";
                runtimeCue
                    .RootPositionOffsets[0] =
                        new Vector3(
                            5f,
                            0f,
                            0f);

                var runtimeCueStillSamples =
                    source.TrySampleCue(
                        "runtime-owned-cue",
                        0.5f,
                        out var runtimePose,
                        out var runtimeSampleError);

                Expect(
                    source.CueIds.Count == 1 &&
                    source.CueIds[0] ==
                        "runtime-owned-cue" &&
                    runtimeCueStillSamples &&
                    runtimePose != null &&
                    string.IsNullOrWhiteSpace(
                        runtimeSampleError),
                    "ConfigureCues must deep-clone caller-owned cue definitions before installing them into the live runtime",
                    failures);

                source.ConfigureCues(
                    CreateInvalidValidationCue(
                        "invalid-runtime-cue"));

                var configuredRollbackSamples =
                    source.TrySampleCue(
                        "runtime-owned-cue",
                        0.5f,
                        out var rollbackPose,
                        out var rollbackSampleError);

                Expect(
                    source.CueIds.Count == 1 &&
                    source.CueIds[0] ==
                        "runtime-owned-cue" &&
                    configuredRollbackSamples &&
                    rollbackPose != null &&
                    string.IsNullOrWhiteSpace(
                        rollbackSampleError),
                    "failed ConfigureCues replacement must restore the previous configured/live cue set",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "baked motion cue ownership validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }

                if (asset != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            asset);
                }
            }
        }

        private static BakedMotionCueDefinition
            CreateValidationCue(
                string cueId)
        {
            return new BakedMotionCueDefinition
            {
                CueId =
                    cueId,
                DurationSeconds =
                    1f,
                FrameCount =
                    2,
                RootPositionOffsets =
                    new[]
                    {
                        Vector3.zero,
                        new Vector3(
                            1f,
                            0f,
                            0f)
                    },
                RootRotationOffsets =
                    new[]
                    {
                        Quaternion.identity,
                        Quaternion.identity
                    },
                Bones =
                    Array.Empty<
                        BakedBoneMotionCueTrack>(),
                Markers =
                    Array.Empty<
                        BakedMotionCueMarker>()
            };
        }

        private static BakedMotionCueDefinition
            CreateInvalidValidationCue(
                string cueId)
        {
            var cue =
                CreateValidationCue(
                    cueId);
            cue.RootPositionOffsets =
                new[]
                {
                    Vector3.zero
                };
            return cue;
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
