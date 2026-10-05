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
            var hasBone =
                new bool[
                    (int)HumanoidBoneId.Count];

            SamplePose(
                bones,
                hasBone,
                out var rootPosition,
                out var rootRotation);

            return new HumanoidPoseState(
                HumanoidPoseSpace.OriginalLocal,
                rootPosition,
                rootRotation,
                bones,
                hasBone,
                SnapshotArrayOwnership.Transfer);
        }

        private void SamplePose(
            NormalizedBonePose[] bones,
            bool[] hasBone,
            out TrackingVector3 rootPosition,
            out TrackingQuaternion rootRotation)
        {
            Array.Clear(
                hasBone,
                0,
                hasBone.Length);

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

                hasBone[i] = true;
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
