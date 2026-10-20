using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Editor.P12
{
    internal enum P12SkinnedBoneMappingStrategy
    {
        HumanoidBone = 0,
        ExactName = 1
    }

    internal sealed class P12SkinnedBoneMapping
    {
        public Transform SourceBone;
        public Transform TargetBone;
        public P12SkinnedBoneMappingStrategy Strategy;
        public HumanBodyBones HumanoidBone =
            HumanBodyBones.LastBone;
    }

    internal sealed class P12SkinnedCompatibilityReport
    {
        public bool StructurallyCompatible;
        public bool RequiresBindPosePreview = true;
        public int SourceBoneCount;
        public int MappedBoneCount;
        public int HumanoidMappedCount;
        public int ExactNameMappedCount;
        public P12SkinnedBoneMapping[] Mappings =
            Array.Empty<P12SkinnedBoneMapping>();
        public string[] Errors =
            Array.Empty<string>();
        public string[] Warnings =
            Array.Empty<string>();
    }

    internal static class P12SkinnedCompatibilityAnalyzer
    {
        public static bool TryAnalyze(
            SkinnedMeshRenderer sourceRenderer,
            Animator targetAnimator,
            out P12SkinnedCompatibilityReport report,
            out string error)
        {
            if (targetAnimator == null ||
                targetAnimator.avatar == null ||
                !targetAnimator.isHuman)
            {
                report = null;
                error =
                    "Skinned compatibility analysis requires a humanoid target Animator.";
                return false;
            }

            return TryAnalyzeStructure(
                sourceRenderer,
                targetAnimator.transform,
                targetAnimator,
                requireHumanoidTarget:
                    true,
                out report,
                out error);
        }

        internal static bool TryAnalyzeStructure(
            SkinnedMeshRenderer sourceRenderer,
            Transform targetRoot,
            Animator targetAnimator,
            bool requireHumanoidTarget,
            out P12SkinnedCompatibilityReport report,
            out string error)
        {
            report = null;
            error = null;

            if (sourceRenderer == null)
            {
                error =
                    "Source SkinnedMeshRenderer is required.";
                return false;
            }

            if (sourceRenderer.sharedMesh == null)
            {
                error =
                    "Source SkinnedMeshRenderer has no shared mesh.";
                return false;
            }

            if (targetRoot == null)
            {
                error =
                    "Target skeleton root is required.";
                return false;
            }

            if (requireHumanoidTarget &&
                (targetAnimator == null ||
                 targetAnimator.avatar == null ||
                 !targetAnimator.isHuman))
            {
                error =
                    "Target skeleton must use a humanoid Animator.";
                return false;
            }

            var sourceBones =
                sourceRenderer.bones ??
                Array.Empty<Transform>();
            var bindposes =
                sourceRenderer.sharedMesh
                    .bindposes ??
                Array.Empty<Matrix4x4>();
            var errors =
                new List<string>();
            var warnings =
                new List<string>();
            var mappings =
                new List<P12SkinnedBoneMapping>();
            var mappingBySource =
                new Dictionary<Transform, Transform>();
            var sourceHumanoid =
                BuildSourceHumanoidMap(
                    sourceRenderer);
            var targetByName =
                BuildTargetNameIndex(
                    targetRoot);

            if (sourceBones.Length == 0)
            {
                errors.Add(
                    "Source skinned mesh has no bones.");
            }

            if (bindposes.Length !=
                sourceBones.Length)
            {
                errors.Add(
                    $"Source bindpose count {bindposes.Length} does not match bone count {sourceBones.Length}.");
            }

            for (var i = 0;
                 i < bindposes.Length;
                 i++)
            {
                if (!IsFinite(
                        bindposes[i]) ||
                    Mathf.Abs(
                        bindposes[i].determinant) <
                        0.0000001f)
                {
                    errors.Add(
                        $"Source bindpose {i} is non-finite or singular.");
                }
            }

            var humanoidMapped = 0;
            var exactMapped = 0;

            foreach (var sourceBone in
                     sourceBones)
            {
                if (sourceBone == null)
                {
                    errors.Add(
                        "Source skinned mesh contains a null bone.");
                    continue;
                }

                Transform targetBone = null;
                var strategy =
                    P12SkinnedBoneMappingStrategy
                        .ExactName;
                var humanoidBone =
                    HumanBodyBones.LastBone;

                if (targetAnimator != null &&
                    sourceHumanoid.TryGetValue(
                        sourceBone,
                        out humanoidBone))
                {
                    targetBone =
                        targetAnimator.GetBoneTransform(
                            humanoidBone);

                    if (targetBone != null)
                    {
                        strategy =
                            P12SkinnedBoneMappingStrategy
                                .HumanoidBone;
                        humanoidMapped++;
                    }
                }

                if (targetBone == null)
                {
                    if (!targetByName.TryGetValue(
                            sourceBone.name,
                            out var candidates) ||
                        candidates.Count == 0)
                    {
                        errors.Add(
                            $"No target bone matches source bone '{sourceBone.name}'.");
                        continue;
                    }

                    if (candidates.Count > 1)
                    {
                        errors.Add(
                            $"Target skeleton contains {candidates.Count} bones named '{sourceBone.name}', so exact-name mapping is ambiguous.");
                        continue;
                    }

                    targetBone =
                        candidates[0];
                    strategy =
                        P12SkinnedBoneMappingStrategy
                            .ExactName;
                    exactMapped++;
                }

                mappingBySource[
                    sourceBone] =
                        targetBone;
                mappings.Add(
                    new P12SkinnedBoneMapping
                    {
                        SourceBone =
                            sourceBone,
                        TargetBone =
                            targetBone,
                        Strategy =
                            strategy,
                        HumanoidBone =
                            humanoidBone
                    });
            }

            foreach (var mapping in mappings)
            {
                var sourceParent =
                    FindNearestMappedParent(
                        mapping.SourceBone.parent,
                        mappingBySource);

                if (sourceParent == null)
                {
                    continue;
                }

                var expectedTargetParent =
                    mappingBySource[
                        sourceParent];

                if (!IsAncestorOrSelf(
                        expectedTargetParent,
                        mapping.TargetBone.parent))
                {
                    errors.Add(
                        $"Mapped hierarchy mismatch for '{mapping.SourceBone.name}': target bone is not under mapped parent '{expectedTargetParent.name}'.");
                }
            }

            if (sourceRenderer.rootBone != null &&
                !mappingBySource.ContainsKey(
                    sourceRenderer.rootBone))
            {
                errors.Add(
                    $"Source root bone '{sourceRenderer.rootBone.name}' could not be mapped.");
            }

            if (exactMapped > 0)
            {
                warnings.Add(
                    $"{exactMapped} bone(s) rely on unique exact-name mapping rather than humanoid-bone identity.");
            }

            if (sourceHumanoid.Count == 0)
            {
                warnings.Add(
                    "Source skeleton has no usable humanoid Animator mapping; compatibility relies entirely on bone names/hierarchy.");
            }

            warnings.Add(
                "Structural compatibility does not prove bind-pose fit. Real target-avatar preview is required before enabling a skinned package.");

            report =
                new P12SkinnedCompatibilityReport
                {
                    StructurallyCompatible =
                        errors.Count == 0 &&
                        mappings.Count ==
                            sourceBones.Length,
                    RequiresBindPosePreview =
                        true,
                    SourceBoneCount =
                        sourceBones.Length,
                    MappedBoneCount =
                        mappings.Count,
                    HumanoidMappedCount =
                        humanoidMapped,
                    ExactNameMappedCount =
                        exactMapped,
                    Mappings =
                        mappings.ToArray(),
                    Errors =
                        errors.ToArray(),
                    Warnings =
                        warnings.ToArray()
                };

            return true;
        }

        private static Dictionary<
            Transform,
            HumanBodyBones> BuildSourceHumanoidMap(
            SkinnedMeshRenderer sourceRenderer)
        {
            var result =
                new Dictionary<
                    Transform,
                    HumanBodyBones>();
            var animator =
                sourceRenderer
                    .GetComponentInParent<Animator>();

            if (animator == null ||
                animator.avatar == null ||
                !animator.isHuman)
            {
                return result;
            }

            for (var i = 0;
                 i <
                 (int)HumanBodyBones.LastBone;
                 i++)
            {
                var bone =
                    (HumanBodyBones)i;
                var transform =
                    animator.GetBoneTransform(
                        bone);

                if (transform != null &&
                    !result.ContainsKey(
                        transform))
                {
                    result.Add(
                        transform,
                        bone);
                }
            }

            return result;
        }

        private static Dictionary<
            string,
            List<Transform>>
            BuildTargetNameIndex(
                Transform root)
        {
            var result =
                new Dictionary<
                    string,
                    List<Transform>>(
                        StringComparer.Ordinal);
            var stack =
                new Stack<Transform>();
            stack.Push(
                root);

            while (stack.Count > 0)
            {
                var current =
                    stack.Pop();

                if (!result.TryGetValue(
                        current.name,
                        out var list))
                {
                    list =
                        new List<Transform>();
                    result.Add(
                        current.name,
                        list);
                }

                list.Add(
                    current);

                for (var i =
                         current.childCount - 1;
                     i >= 0;
                     i--)
                {
                    stack.Push(
                        current.GetChild(
                            i));
                }
            }

            return result;
        }

        private static Transform FindNearestMappedParent(
            Transform sourceParent,
            IReadOnlyDictionary<Transform, Transform>
                mappingBySource)
        {
            var current =
                sourceParent;

            while (current != null)
            {
                if (mappingBySource.ContainsKey(
                        current))
                {
                    return current;
                }

                current =
                    current.parent;
            }

            return null;
        }

        private static bool IsAncestorOrSelf(
            Transform expected,
            Transform current)
        {
            while (current != null)
            {
                if (ReferenceEquals(
                        expected,
                        current))
                {
                    return true;
                }

                current =
                    current.parent;
            }

            return false;
        }

        private static bool IsFinite(
            Matrix4x4 matrix)
        {
            for (var row = 0;
                 row < 4;
                 row++)
            {
                for (var column = 0;
                     column < 4;
                     column++)
                {
                    var value =
                        matrix[
                            row,
                            column];

                    if (float.IsNaN(
                            value) ||
                        float.IsInfinity(
                            value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
