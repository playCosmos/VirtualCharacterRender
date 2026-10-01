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
            joints[(int)UpperBodyJoint.Nose] = Convert(landmarks[Nose]);
            joints[(int)UpperBodyJoint.LeftShoulder] = Convert(landmarks[LeftShoulder]);
            joints[(int)UpperBodyJoint.RightShoulder] = Convert(landmarks[RightShoulder]);
            joints[(int)UpperBodyJoint.LeftElbow] = Convert(landmarks[LeftElbow]);
            joints[(int)UpperBodyJoint.RightElbow] = Convert(landmarks[RightElbow]);
            joints[(int)UpperBodyJoint.LeftWrist] = Convert(landmarks[LeftWrist]);
            joints[(int)UpperBodyJoint.RightWrist] = Convert(landmarks[RightWrist]);
            joints[(int)UpperBodyJoint.LeftHip] = Convert(landmarks[LeftHip]);
            joints[(int)UpperBodyJoint.RightHip] = Convert(landmarks[RightHip]);

            return new NormalizedUpperBodyState(joints);
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
                if (confidence >= 0f)
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
                joints[i] = Convert(landmarks[i]);
            }

            return new NormalizedHandState(isLeft, joints);
        }

        private static TrackingPoint Convert(in Landmark landmark)
        {
            // MediaPipe real-world: +X right, +Y down, +Z back.
            // VCR normalized:       +X right, +Y up,   +Z forward.
            var position = new TrackingVector3(
                landmark.x,
                -landmark.y,
                -landmark.z);

            var confidence = Confidence(landmark.visibility, landmark.presence);
            return new TrackingPoint(position, confidence);
        }

        private static float Confidence(float? visibility, float? presence)
        {
            if (visibility.HasValue && presence.HasValue)
            {
                return visibility.Value < presence.Value
                    ? visibility.Value
                    : presence.Value;
            }

            if (visibility.HasValue) return visibility.Value;
            if (presence.HasValue) return presence.Value;
            return -1f;
        }
    }
}
