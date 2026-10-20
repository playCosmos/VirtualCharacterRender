using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HolisticLandmarker;

namespace VCR.Runtime.Tracking.MediaPipe
{
    internal static class MediaPipeHolisticNormalizer
    {
        private static readonly UpperBodyJoint[] ConfidenceJoints =
        {
            UpperBodyJoint.LeftShoulder,
            UpperBodyJoint.RightShoulder,
            UpperBodyJoint.LeftElbow,
            UpperBodyJoint.RightElbow,
            UpperBodyJoint.LeftWrist,
            UpperBodyJoint.RightWrist
        };

        // MediaPipe Pose landmark indices.
        private const int Nose = 0;
        private const int LeftShoulder = 11;
        private const int RightShoulder = 12;
        private const int LeftElbow = 13;
        private const int RightElbow = 14;
        private const int LeftWrist = 15;
        private const int RightWrist = 16;
        private const int LeftHip = 23;
        private const int RightHip = 24;

        public static NormalizedUpperBodyState ConvertUpperBody(
            in HolisticLandmarkerResult result)
        {
            var landmarks = result.poseWorldLandmarks.landmarks;
            if (landmarks == null || landmarks.Count <= RightHip)
            {
                return null;
            }

            var joints = new TrackingPoint[(int)UpperBodyJoint.Count];

            if (!TryConvert(
                    landmarks[Nose],
                    out joints[(int)UpperBodyJoint.Nose]) ||
                !TryConvert(
                    landmarks[LeftShoulder],
                    out joints[(int)UpperBodyJoint.LeftShoulder]) ||
                !TryConvert(
                    landmarks[RightShoulder],
                    out joints[(int)UpperBodyJoint.RightShoulder]) ||
                !TryConvert(
                    landmarks[LeftElbow],
                    out joints[(int)UpperBodyJoint.LeftElbow]) ||
                !TryConvert(
                    landmarks[RightElbow],
                    out joints[(int)UpperBodyJoint.RightElbow]) ||
                !TryConvert(
                    landmarks[LeftWrist],
                    out joints[(int)UpperBodyJoint.LeftWrist]) ||
                !TryConvert(
                    landmarks[RightWrist],
                    out joints[(int)UpperBodyJoint.RightWrist]) ||
                !TryConvert(
                    landmarks[LeftHip],
                    out joints[(int)UpperBodyJoint.LeftHip]) ||
                !TryConvert(
                    landmarks[RightHip],
                    out joints[(int)UpperBodyJoint.RightHip]))
            {
                return null;
            }

            return new NormalizedUpperBodyState(
                joints,
                SnapshotArrayOwnership.Transfer);
        }

        public static NormalizedHandState ConvertLeftHand(
            in HolisticLandmarkerResult result)
        {
            return ConvertHand(result.leftHandWorldLandmarks.landmarks, true);
        }

        public static NormalizedHandState ConvertRightHand(
            in HolisticLandmarkerResult result)
        {
            return ConvertHand(result.rightHandWorldLandmarks.landmarks, false);
        }

        public static float EstimateUpperBodyConfidence(NormalizedUpperBodyState body)
        {
            if (body == null)
            {
                return float.NaN;
            }

            var sum = 0f;
            var count = 0;

            foreach (var joint in ConfidenceJoints)
            {
                var confidence = body.Get(joint).Confidence;
                if (float.IsFinite(confidence) &&
                    confidence >= 0f)
                {
                    sum += confidence;
                    count++;
                }
            }

            return count == 0 ? float.NaN : sum / count;
        }

        private static NormalizedHandState ConvertHand(
            List<Landmark> landmarks,
            bool isLeft)
        {
            if (landmarks == null || landmarks.Count < (int)HandJoint.Count)
            {
                return null;
            }

            var joints = new TrackingPoint[(int)HandJoint.Count];
            for (var i = 0; i < joints.Length; i++)
            {
                if (!TryConvert(
                        landmarks[i],
                        out joints[i]))
                {
                    return null;
                }
            }

            return new NormalizedHandState(
                isLeft,
                joints,
                SnapshotArrayOwnership.Transfer);
        }

        private static bool TryConvert(
            in Landmark landmark,
            out TrackingPoint point)
        {
            point = default;

            if (!float.IsFinite(landmark.x) ||
                !float.IsFinite(landmark.y) ||
                !float.IsFinite(landmark.z))
            {
                return false;
            }

            // MediaPipe real-world: +X right, +Y down, +Z back.
            // VCR normalized:       +X right, +Y up,   +Z forward.
            var position = new TrackingVector3(
                landmark.x,
                -landmark.y,
                -landmark.z);

            point = new TrackingPoint(
                position,
                Confidence(
                    landmark.visibility,
                    landmark.presence));
            return true;
        }

        private static float Confidence(
            float? visibility,
            float? presence)
        {
            var visibilityValue =
                SanitizeConfidence(
                    visibility);
            var presenceValue =
                SanitizeConfidence(
                    presence);

            if (visibilityValue >= 0f &&
                presenceValue >= 0f)
            {
                return visibilityValue <
                    presenceValue
                        ? visibilityValue
                        : presenceValue;
            }

            if (visibilityValue >= 0f)
            {
                return visibilityValue;
            }

            return presenceValue;
        }

        private static float SanitizeConfidence(
            float? value)
        {
            if (!value.HasValue ||
                !float.IsFinite(value.Value))
            {
                return -1f;
            }

            if (value.Value <= 0f)
            {
                return 0f;
            }

            if (value.Value >= 1f)
            {
                return 1f;
            }

            return value.Value;
        }
    }
}
