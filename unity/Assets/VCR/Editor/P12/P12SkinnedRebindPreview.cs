using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Editor.P12
{
    internal sealed class P12SkinnedRebindPreviewSession :
        IDisposable
    {
        public GameObject PreviewObject;
        public SkinnedMeshRenderer PreviewRenderer;
        public float AverageBindMatrixDelta;
        public float MaxBindMatrixDelta;

        public void Dispose()
        {
            if (PreviewObject != null)
            {
                UnityEngine.Object
                    .DestroyImmediate(
                        PreviewObject);
                PreviewObject = null;
                PreviewRenderer = null;
            }
        }
    }

    internal static class P12SkinnedRebindPreview
    {
        public static bool TryCreate(
            SkinnedMeshRenderer sourceRenderer,
            Transform targetParent,
            P12SkinnedCompatibilityReport report,
            out P12SkinnedRebindPreviewSession session,
            out string error)
        {
            session = null;
            error = null;

            if (sourceRenderer == null ||
                sourceRenderer.sharedMesh == null)
            {
                error =
                    "Source skinned renderer/mesh is required for preview.";
                return false;
            }

            if (targetParent == null)
            {
                error =
                    "Target parent is required for preview.";
                return false;
            }

            if (report == null ||
                !report.StructurallyCompatible)
            {
                error =
                    "A structurally compatible analysis report is required before preview.";
                return false;
            }

            var map =
                new Dictionary<Transform, Transform>();

            foreach (var mapping in
                     report.Mappings ??
                     Array.Empty<
                         P12SkinnedBoneMapping>())
            {
                if (mapping?.SourceBone == null ||
                    mapping.TargetBone == null)
                {
                    error =
                        "Compatibility report contains an incomplete bone mapping.";
                    return false;
                }

                map[
                    mapping.SourceBone] =
                        mapping.TargetBone;
            }

            var sourceBones =
                sourceRenderer.bones ??
                Array.Empty<Transform>();
            var targetBones =
                new Transform[
                    sourceBones.Length];

            for (var i = 0;
                 i < sourceBones.Length;
                 i++)
            {
                if (sourceBones[i] == null ||
                    !map.TryGetValue(
                        sourceBones[i],
                        out targetBones[i]) ||
                    targetBones[i] == null)
                {
                    error =
                        $"Preview cannot map source bone index {i}.";
                    return false;
                }
            }

            Transform targetRootBone = null;

            if (sourceRenderer.rootBone != null &&
                !map.TryGetValue(
                    sourceRenderer.rootBone,
                    out targetRootBone))
            {
                error =
                    $"Preview cannot map source root bone '{sourceRenderer.rootBone.name}'.";
                return false;
            }

            GameObject previewObject = null;

            try
            {
                previewObject =
                    new GameObject(
                        "[VCR Preview] " +
                        sourceRenderer.name);
                previewObject.hideFlags =
                    HideFlags.DontSaveInEditor |
                    HideFlags.DontSaveInBuild;
                previewObject.transform.SetParent(
                    targetParent,
                    false);
                previewObject.transform.localPosition =
                    sourceRenderer.transform
                        .localPosition;
                previewObject.transform.localRotation =
                    sourceRenderer.transform
                        .localRotation;
                previewObject.transform.localScale =
                    sourceRenderer.transform
                        .localScale;

                var preview =
                    previewObject.AddComponent<
                        SkinnedMeshRenderer>();
                preview.sharedMesh =
                    sourceRenderer.sharedMesh;
                preview.sharedMaterials =
                    sourceRenderer.sharedMaterials;
                preview.bones =
                    targetBones;
                preview.rootBone =
                    targetRootBone;
                preview.localBounds =
                    sourceRenderer.localBounds;
                preview.quality =
                    sourceRenderer.quality;
                preview.updateWhenOffscreen =
                    sourceRenderer.updateWhenOffscreen;
                preview.enabled =
                    true;

                var blendShapeCount =
                    preview.sharedMesh != null
                        ? preview.sharedMesh
                            .blendShapeCount
                        : 0;

                for (var i = 0;
                     i < blendShapeCount;
                     i++)
                {
                    preview.SetBlendShapeWeight(
                        i,
                        sourceRenderer
                            .GetBlendShapeWeight(
                                i));
                }

                MeasureBindMatrixDelta(
                    preview,
                    out var averageDelta,
                    out var maxDelta);

                session =
                    new P12SkinnedRebindPreviewSession
                    {
                        PreviewObject =
                            previewObject,
                        PreviewRenderer =
                            preview,
                        AverageBindMatrixDelta =
                            averageDelta,
                        MaxBindMatrixDelta =
                            maxDelta
                    };

                return true;
            }
            catch (Exception exception)
            {
                if (previewObject != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            previewObject);
                }

                error =
                    "Skinned rebind preview creation failed: " +
                    exception.Message;
                return false;
            }
        }

        private static void MeasureBindMatrixDelta(
            SkinnedMeshRenderer preview,
            out float averageDelta,
            out float maxDelta)
        {
            averageDelta = 0f;
            maxDelta = 0f;

            var mesh =
                preview.sharedMesh;
            var bindposes =
                mesh?.bindposes ??
                Array.Empty<Matrix4x4>();
            var bones =
                preview.bones ??
                Array.Empty<Transform>();
            var count =
                Math.Min(
                    bindposes.Length,
                    bones.Length);

            if (count == 0)
            {
                return;
            }

            var total = 0f;
            var samples = 0;

            for (var i = 0;
                 i < count;
                 i++)
            {
                if (bones[i] == null)
                {
                    continue;
                }

                var candidate =
                    bones[i].worldToLocalMatrix *
                    preview.transform
                        .localToWorldMatrix;

                for (var row = 0;
                     row < 4;
                     row++)
                {
                    for (var column = 0;
                         column < 4;
                         column++)
                    {
                        var delta =
                            Mathf.Abs(
                                bindposes[i][
                                    row,
                                    column] -
                                candidate[
                                    row,
                                    column]);

                        total +=
                            delta;
                        samples++;
                        maxDelta =
                            Mathf.Max(
                                maxDelta,
                                delta);
                    }
                }
            }

            averageDelta =
                samples > 0
                    ? total /
                      samples
                    : 0f;
        }
    }
}
