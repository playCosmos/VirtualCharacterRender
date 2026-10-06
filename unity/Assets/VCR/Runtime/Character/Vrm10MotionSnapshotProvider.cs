using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// Captures the actual VRM runtime pose into source-neutral motion state.
    ///
    /// VMC Protocol recommends sending original/non-normalized humanoid bones
    /// for VRM1, so this provider samples target.Humanoid rather than ControlRig.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(19000)]
    public sealed class Vrm10MotionSnapshotProvider :
        MonoBehaviour,
        ISelectiveNormalizedMotionSnapshotProvider,
        IBorrowedNormalizedMotionProvider,
        IBorrowedHumanoidPoseProvider
    {
        [SerializeField] private Vrm10Instance target;

        private readonly List<NamedExpressionValue>
            _customExpressionScratch =
                new(16);

        private Vrm10Instance _cachedBoneTarget;
        private Transform[] _boneTransforms;
        private NormalizedBonePose[] _borrowedBones;
        private bool[] _borrowedHasBone;
        private readonly float[] _borrowedStandardExpressions =
            new float[
                (int)StandardExpression.Count];
        private NamedExpressionValue[] _borrowedCustomExpressions =
            new NamedExpressionValue[16];
        private int _borrowedCustomExpressionCount;
        private long _sequence;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<Vrm10Instance>();
            }
        }

        public bool TryCaptureMotion(
            out TrackingFrame frame)
        {
            var request =
                NormalizedMotionSnapshotRequest.Full;

            return TryCaptureMotion(
                in request,
                out frame);
        }

        public bool TryCaptureMotion(
            in NormalizedMotionSnapshotRequest request,
            out TrackingFrame frame)
        {
            frame = null;

            if (!request.HasAnyDomain ||
                target == null ||
                target.Runtime == null ||
                (request.IncludeHumanoidPose &&
                 target.Humanoid == null))
            {
                return false;
            }

            HumanoidPoseState pose = null;
            NormalizedExpressionState expressions = null;
            var regions =
                TrackingRegion.None;

            if (request.IncludeHumanoidPose)
            {
                pose =
                    CapturePose();
                regions |=
                    TrackingRegion.FullBody;
            }

            if (request.IncludeExpressions)
            {
                expressions =
                    CaptureExpressions();
                regions |=
                    TrackingRegion.Expressions;
            }

            var timestampUs =
                NowUs();

            frame =
                new TrackingFrame(
                    ++_sequence,
                    timestampUs,
                    regions,
                    1f,
                    subjectDetected: true,
                    humanoidPose: pose,
                    expressions: expressions,
                    sourceId: "character-runtime",
                    runtimeTimestampUs:
                        timestampUs);

            return true;
        }

        public bool TryBorrowMotion(
            in NormalizedMotionSnapshotRequest request,
            out BorrowedMotionSample sample)
        {
            sample = default;

            if (!request.HasAnyDomain ||
                target == null ||
                target.Runtime == null ||
                (request.IncludeHumanoidPose &&
                 target.Humanoid == null))
            {
                return false;
            }

            var regions =
                TrackingRegion.None;
            var pose =
                default(BorrowedHumanoidPose);
            var expressions =
                default(BorrowedExpressionState);

            if (request.IncludeHumanoidPose)
            {
                if (!TryBorrowHumanoidPose(
                        out pose))
                {
                    return false;
                }

                regions |=
                    TrackingRegion.FullBody;
            }

            if (request.IncludeExpressions)
            {
                SampleBorrowedExpressions(
                    out expressions);
                regions |=
                    TrackingRegion.Expressions;
            }

            sample =
                new BorrowedMotionSample(
                    regions,
                    pose,
                    expressions);

            return true;
        }

        public bool TryBorrowHumanoidPose(
            out BorrowedHumanoidPose pose)
        {
            pose = default;

            if (target == null ||
                target.Runtime == null ||
                target.Humanoid == null)
            {
                return false;
            }

            EnsureBoneCache();
            EnsureBorrowedPoseBuffers();

            SamplePose(
                _borrowedBones,
                _borrowedHasBone,
                out _,
                out var rootPosition,
                out var rootRotation);

            pose =
                new BorrowedHumanoidPose(
                    HumanoidPoseSpace.OriginalLocal,
                    rootPosition,
                    rootRotation,
                    _borrowedBones,
                    _borrowedHasBone);

            return true;
        }

        private HumanoidPoseState CapturePose()
        {
            EnsureBoneCache();

            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];

            SamplePose(
                bones,
                hasBone: null,
                out var boneMask,
                out var rootPosition,
                out var rootRotation);

            return new HumanoidPoseState(
                HumanoidPoseSpace.OriginalLocal,
                rootPosition,
                rootRotation,
                bones,
                boneMask,
                SnapshotArrayOwnership.Transfer);
        }

        private void SamplePose(
            NormalizedBonePose[] bones,
            bool[] hasBone,
            out ulong boneMask,
            out TrackingVector3 rootPosition,
            out TrackingQuaternion rootRotation)
        {
            if (hasBone != null)
            {
                Array.Clear(
                    hasBone,
                    0,
                    hasBone.Length);
            }

            boneMask = 0;

            for (var i = 0;
                 i < _boneTransforms.Length;
                 i++)
            {
                var bone =
                    _boneTransforms[i];

                if (bone == null)
                {
                    continue;
                }

                var p =
                    bone.localPosition;
                var q =
                    bone.localRotation;

                bones[i] =
                    new NormalizedBonePose(
                        new TrackingVector3(
                            p.x,
                            p.y,
                            p.z),
                        new TrackingQuaternion(
                            q.x,
                            q.y,
                            q.z,
                            q.w));

                if (hasBone != null)
                {
                    hasBone[i] = true;
                }

                boneMask |=
                    1UL << i;
            }

            var rootP =
                target.transform.localPosition;
            var rootQ =
                target.transform.localRotation;

            rootPosition =
                new TrackingVector3(
                    rootP.x,
                    rootP.y,
                    rootP.z);
            rootRotation =
                new TrackingQuaternion(
                    rootQ.x,
                    rootQ.y,
                    rootQ.z,
                    rootQ.w);
        }

        private void EnsureBorrowedPoseBuffers()
        {
            var count =
                (int)HumanoidBoneId.Count;

            if (_borrowedBones == null ||
                _borrowedBones.Length != count)
            {
                _borrowedBones =
                    new NormalizedBonePose[count];
            }

            if (_borrowedHasBone == null ||
                _borrowedHasBone.Length != count)
            {
                _borrowedHasBone =
                    new bool[count];
            }
        }

        private void SampleBorrowedExpressions(
            out BorrowedExpressionState expressions)
        {
            Array.Clear(
                _borrowedStandardExpressions,
                0,
                _borrowedStandardExpressions.Length);

            var previousCustomCount =
                _borrowedCustomExpressionCount;
            var customCount = 0;

            foreach (var pair in
                target.Runtime.Expression.GetWeights())
            {
                var key =
                    pair.Key;
                var value =
                    Mathf.Clamp01(
                        pair.Value);

                if (StandardExpressionNames.TryParse(
                        key.Name,
                        out var expression))
                {
                    _borrowedStandardExpressions[
                        (int)expression] =
                            value;
                    continue;
                }

                if (string.IsNullOrEmpty(
                        key.Name))
                {
                    continue;
                }

                EnsureBorrowedCustomExpressionCapacity(
                    customCount + 1);

                _borrowedCustomExpressions[
                    customCount++] =
                        new NamedExpressionValue(
                            key.Name,
                            value);
            }

            if (customCount <
                previousCustomCount)
            {
                Array.Clear(
                    _borrowedCustomExpressions,
                    customCount,
                    previousCustomCount -
                    customCount);
            }

            _borrowedCustomExpressionCount =
                customCount;

            expressions =
                new BorrowedExpressionState(
                    _borrowedStandardExpressions,
                    _borrowedCustomExpressions,
                    customCount);
        }

        private void EnsureBorrowedCustomExpressionCapacity(
            int required)
        {
            if (_borrowedCustomExpressions.Length >=
                required)
            {
                return;
            }

            var next =
                Math.Max(
                    required,
                    _borrowedCustomExpressions.Length *
                    2);

            Array.Resize(
                ref _borrowedCustomExpressions,
                next);
        }

        private NormalizedExpressionState CaptureExpressions()
        {
            var standard =
                new float[(int)StandardExpression.Count];
            var custom =
                _customExpressionScratch;
            custom.Clear();

            foreach (var pair in
                target.Runtime.Expression.GetWeights())
            {
                var key = pair.Key;
                var value = Mathf.Clamp01(pair.Value);

                if (StandardExpressionNames.TryParse(
                    key.Name,
                    out var expression))
                {
                    standard[(int)expression] = value;
                }
                else if (!string.IsNullOrEmpty(key.Name))
                {
                    custom.Add(
                        new NamedExpressionValue(
                            key.Name,
                            value));
                }
            }

            return new NormalizedExpressionState(
                standard,
                custom.ToArray(),
                SnapshotArrayOwnership.Transfer);
        }

        private void EnsureBoneCache()
        {
            if (ReferenceEquals(
                    _cachedBoneTarget,
                    target) &&
                _boneTransforms != null)
            {
                return;
            }

            _cachedBoneTarget =
                target;
            _boneTransforms =
                new Transform[
                    (int)HumanoidBoneId.Count];

            if (target == null ||
                target.Humanoid == null)
            {
                return;
            }

            for (var i = 0;
                 i < _boneTransforms.Length;
                 i++)
            {
                var canonical =
                    HumanoidBoneNames.GetCanonical(
                        (HumanoidBoneId)i);

                if (string.IsNullOrEmpty(
                        canonical) ||
                    !Enum.TryParse(
                        canonical,
                        ignoreCase: false,
                        out HumanBodyBones unityBone) ||
                    unityBone ==
                        HumanBodyBones.LastBone)
                {
                    continue;
                }

                _boneTransforms[i] =
                    target.Humanoid
                        .GetBoneTransform(
                            unityBone);
            }
        }

        private static long NowUs()
        {
            return (long)(
                Time.realtimeSinceStartupAsDouble *
                1_000_000.0);
        }
    }
}
