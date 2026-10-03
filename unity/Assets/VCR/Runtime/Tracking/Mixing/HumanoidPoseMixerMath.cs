using System;

namespace VCR.Runtime.Tracking.Mixing
{
    public static class HumanoidPoseMixerMath
    {
        public static HumanoidPoseState Blend(
            HumanoidPoseState basePose,
            HumanoidPoseState layerPose,
            HumanoidPoseLayerSettings settings,
            out bool poseSpaceMismatch)
        {
            poseSpaceMismatch = false;

            if (layerPose == null ||
                settings == null ||
                !settings.Enabled ||
                settings.Weight <= 0f)
            {
                return basePose;
            }

            if (basePose != null &&
                basePose.PoseSpace !=
                    layerPose.PoseSpace)
            {
                poseSpaceMismatch = true;
                return basePose;
            }

            var weight =
                Clamp01(settings.Weight);
            var poseSpace =
                basePose?.PoseSpace ??
                layerPose.PoseSpace;

            var rootPosition =
                basePose?.RootPosition ??
                TrackingVector3.Zero;
            var rootRotation =
                basePose?.RootRotation ??
                TrackingQuaternion.Identity;

            if (settings.AffectRootPosition)
            {
                rootPosition =
                    settings.BlendMode ==
                        HumanoidPoseBlendMode.Additive
                        ? Add(
                            rootPosition,
                            Scale(
                                layerPose.RootPosition,
                                weight))
                        : Lerp(
                            rootPosition,
                            layerPose.RootPosition,
                            weight);
            }

            if (settings.AffectRootRotation)
            {
                rootRotation =
                    settings.BlendMode ==
                        HumanoidPoseBlendMode.Additive
                        ? Normalize(
                            Multiply(
                                rootRotation,
                                Nlerp(
                                    TrackingQuaternion.Identity,
                                    layerPose.RootRotation,
                                    weight)))
                        : Nlerp(
                            rootRotation,
                            layerPose.RootRotation,
                            weight);
            }

            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var hasBone =
                new bool[
                    (int)HumanoidBoneId.Count];

            for (var i = 0;
                 i < bones.Length;
                 i++)
            {
                var bone =
                    (HumanoidBoneId)i;
                var hasBase =
                    basePose != null &&
                    basePose.TryGet(
                        bone,
                        out var baseBone);
                var hasLayer =
                    layerPose.TryGet(
                        bone,
                        out var layerBone);

                if (!hasLayer ||
                    !settings.BoneMask
                        .Includes(bone))
                {
                    if (hasBase)
                    {
                        bones[i] = baseBone;
                        hasBone[i] = true;
                    }

                    continue;
                }

                var basePosition =
                    hasBase
                        ? baseBone.LocalPosition
                        : TrackingVector3.Zero;
                var baseRotation =
                    hasBase
                        ? baseBone.LocalRotation
                        : TrackingQuaternion.Identity;

                TrackingVector3 position;
                TrackingQuaternion rotation;

                if (settings.BlendMode ==
                    HumanoidPoseBlendMode.Additive)
                {
                    position =
                        Add(
                            basePosition,
                            Scale(
                                layerBone.LocalPosition,
                                weight));

                    rotation =
                        Normalize(
                            Multiply(
                                baseRotation,
                                Nlerp(
                                    TrackingQuaternion.Identity,
                                    layerBone.LocalRotation,
                                    weight)));
                }
                else
                {
                    position =
                        Lerp(
                            basePosition,
                            layerBone.LocalPosition,
                            weight);

                    rotation =
                        Nlerp(
                            baseRotation,
                            layerBone.LocalRotation,
                            weight);
                }

                bones[i] =
                    new NormalizedBonePose(
                        position,
                        rotation);
                hasBone[i] = true;
            }

            return new HumanoidPoseState(
                poseSpace,
                rootPosition,
                rootRotation,
                bones,
                hasBone);
        }

        private static TrackingVector3 Add(
            TrackingVector3 a,
            TrackingVector3 b)
        {
            return new TrackingVector3(
                a.X + b.X,
                a.Y + b.Y,
                a.Z + b.Z);
        }

        private static TrackingVector3 Scale(
            TrackingVector3 value,
            float scalar)
        {
            return new TrackingVector3(
                value.X * scalar,
                value.Y * scalar,
                value.Z * scalar);
        }

        private static TrackingVector3 Lerp(
            TrackingVector3 a,
            TrackingVector3 b,
            float t)
        {
            var clamped =
                Clamp01(t);

            return new TrackingVector3(
                a.X +
                    (b.X - a.X) *
                    clamped,
                a.Y +
                    (b.Y - a.Y) *
                    clamped,
                a.Z +
                    (b.Z - a.Z) *
                    clamped);
        }

        private static TrackingQuaternion Multiply(
            TrackingQuaternion a,
            TrackingQuaternion b)
        {
            return new TrackingQuaternion(
                a.W * b.X +
                    a.X * b.W +
                    a.Y * b.Z -
                    a.Z * b.Y,
                a.W * b.Y -
                    a.X * b.Z +
                    a.Y * b.W +
                    a.Z * b.X,
                a.W * b.Z +
                    a.X * b.Y -
                    a.Y * b.X +
                    a.Z * b.W,
                a.W * b.W -
                    a.X * b.X -
                    a.Y * b.Y -
                    a.Z * b.Z);
        }

        private static TrackingQuaternion Nlerp(
            TrackingQuaternion a,
            TrackingQuaternion b,
            float t)
        {
            var clamped =
                Clamp01(t);
            var dot =
                a.X * b.X +
                a.Y * b.Y +
                a.Z * b.Z +
                a.W * b.W;

            if (dot < 0f)
            {
                b = new TrackingQuaternion(
                    -b.X,
                    -b.Y,
                    -b.Z,
                    -b.W);
            }

            return Normalize(
                new TrackingQuaternion(
                    a.X +
                        (b.X - a.X) *
                        clamped,
                    a.Y +
                        (b.Y - a.Y) *
                        clamped,
                    a.Z +
                        (b.Z - a.Z) *
                        clamped,
                    a.W +
                        (b.W - a.W) *
                        clamped));
        }

        private static TrackingQuaternion Normalize(
            TrackingQuaternion value)
        {
            var lengthSquared =
                value.X * value.X +
                value.Y * value.Y +
                value.Z * value.Z +
                value.W * value.W;

            if (lengthSquared <= 0.0000001f)
            {
                return TrackingQuaternion.Identity;
            }

            var inverse =
                1f /
                (float)Math.Sqrt(
                    lengthSquared);

            return new TrackingQuaternion(
                value.X * inverse,
                value.Y * inverse,
                value.Z * inverse,
                value.W * inverse);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(
                0f,
                Math.Min(
                    1f,
                    value));
        }
    }
}
