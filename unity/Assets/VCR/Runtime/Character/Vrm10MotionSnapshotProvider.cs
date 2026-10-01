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
        INormalizedMotionSnapshotProvider
    {
        [SerializeField] private Vrm10Instance target;

        private long _sequence;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<Vrm10Instance>();
            }
        }

        public bool TryCaptureMotion(out TrackingFrame frame)
        {
            frame = null;

            if (target == null ||
                target.Runtime == null ||
                target.Humanoid == null)
            {
                return false;
            }

            var bones =
                new NormalizedBonePose[(int)HumanoidBoneId.Count];
            var hasBone =
                new bool[(int)HumanoidBoneId.Count];

            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var boneId = (HumanoidBoneId)i;

                if (!Enum.TryParse(
                    boneId.ToString(),
                    ignoreCase: false,
                    out HumanBodyBones unityBone) ||
                    unityBone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                var bone =
                    target.Humanoid.GetBoneTransform(unityBone);

                if (bone == null)
                {
                    continue;
                }

                var p = bone.localPosition;
                var q = bone.localRotation;

                bones[i] = new NormalizedBonePose(
                    new TrackingVector3(p.x, p.y, p.z),
                    new TrackingQuaternion(
                        q.x,
                        q.y,
                        q.z,
                        q.w));

                hasBone[i] = true;
            }

            var rootPosition = target.transform.localPosition;
            var rootRotation = target.transform.localRotation;

            var pose = new HumanoidPoseState(
                HumanoidPoseSpace.OriginalLocal,
                new TrackingVector3(
                    rootPosition.x,
                    rootPosition.y,
                    rootPosition.z),
                new TrackingQuaternion(
                    rootRotation.x,
                    rootRotation.y,
                    rootRotation.z,
                    rootRotation.w),
                bones,
                hasBone);

            var expressions = CaptureExpressions();

            frame = new TrackingFrame(
                ++_sequence,
                NowUs(),
                TrackingRegion.FullBody,
                1f,
                subjectDetected: true,
                humanoidPose: pose,
                expressions: expressions,
                sourceId: "character-runtime",
                runtimeTimestampUs: NowUs());

            return true;
        }

        private NormalizedExpressionState CaptureExpressions()
        {
            var standard =
                new float[(int)StandardExpression.Count];
            var custom =
                new List<NamedExpressionValue>();

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
                custom.ToArray());
        }

        private static long NowUs()
        {
            return (long)(
                Time.realtimeSinceStartupAsDouble *
                1_000_000.0);
        }
    }
}
