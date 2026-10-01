using System;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;

namespace VCR.Runtime.Tracking.MediaPipe
{
    internal static class MediaPipeFaceNormalizer
    {
        public static bool TryConvert(
            in FaceLandmarkerResult result,
            out NormalizedFaceState state)
        {
            if (result.faceLandmarks == null || result.faceLandmarks.Count == 0)
            {
                state = null;
                return false;
            }

            var coefficients = new float[(int)FaceCoefficient.Count];

            if (result.faceBlendshapes != null && result.faceBlendshapes.Count > 0)
            {
                var categories = result.faceBlendshapes[0].categories;
                if (categories != null)
                {
                    foreach (var category in categories)
                    {
                        if (TryMapCoefficient(category.categoryName, out var coefficient))
                        {
                            coefficients[(int)coefficient] = Clamp01(category.score);
                        }
                    }
                }
            }

            var headRotation = TrackingQuaternion.Identity;
            var headPosition = TrackingVector3.Zero;

            if (result.facialTransformationMatrixes != null &&
                result.facialTransformationMatrixes.Count > 0)
            {
                var matrix = result.facialTransformationMatrixes[0];
                var rotation = matrix.rotation;
                var translation = matrix.GetColumn(3);

                headRotation = new TrackingQuaternion(
                    rotation.x,
                    rotation.y,
                    rotation.z,
                    rotation.w);

                headPosition = new TrackingVector3(
                    translation.x,
                    translation.y,
                    translation.z);
            }

            state = new NormalizedFaceState(
                headRotation,
                headPosition,
                coefficients);

            return true;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool TryMapCoefficient(
            string name,
            out FaceCoefficient coefficient)
        {
            switch (name)
            {
                case "_neutral": coefficient = FaceCoefficient.Neutral; return true;
                case "browDownLeft": coefficient = FaceCoefficient.BrowDownLeft; return true;
                case "browDownRight": coefficient = FaceCoefficient.BrowDownRight; return true;
                case "browInnerUp": coefficient = FaceCoefficient.BrowInnerUp; return true;
                case "browOuterUpLeft": coefficient = FaceCoefficient.BrowOuterUpLeft; return true;
                case "browOuterUpRight": coefficient = FaceCoefficient.BrowOuterUpRight; return true;
                case "cheekPuff": coefficient = FaceCoefficient.CheekPuff; return true;
                case "cheekSquintLeft": coefficient = FaceCoefficient.CheekSquintLeft; return true;
                case "cheekSquintRight": coefficient = FaceCoefficient.CheekSquintRight; return true;
                case "eyeBlinkLeft": coefficient = FaceCoefficient.EyeBlinkLeft; return true;
                case "eyeBlinkRight": coefficient = FaceCoefficient.EyeBlinkRight; return true;
                case "eyeLookDownLeft": coefficient = FaceCoefficient.EyeLookDownLeft; return true;
                case "eyeLookDownRight": coefficient = FaceCoefficient.EyeLookDownRight; return true;
                case "eyeLookInLeft": coefficient = FaceCoefficient.EyeLookInLeft; return true;
                case "eyeLookInRight": coefficient = FaceCoefficient.EyeLookInRight; return true;
                case "eyeLookOutLeft": coefficient = FaceCoefficient.EyeLookOutLeft; return true;
                case "eyeLookOutRight": coefficient = FaceCoefficient.EyeLookOutRight; return true;
                case "eyeLookUpLeft": coefficient = FaceCoefficient.EyeLookUpLeft; return true;
                case "eyeLookUpRight": coefficient = FaceCoefficient.EyeLookUpRight; return true;
                case "eyeSquintLeft": coefficient = FaceCoefficient.EyeSquintLeft; return true;
                case "eyeSquintRight": coefficient = FaceCoefficient.EyeSquintRight; return true;
                case "eyeWideLeft": coefficient = FaceCoefficient.EyeWideLeft; return true;
                case "eyeWideRight": coefficient = FaceCoefficient.EyeWideRight; return true;
                case "jawForward": coefficient = FaceCoefficient.JawForward; return true;
                case "jawLeft": coefficient = FaceCoefficient.JawLeft; return true;
                case "jawOpen": coefficient = FaceCoefficient.JawOpen; return true;
                case "jawRight": coefficient = FaceCoefficient.JawRight; return true;
                case "mouthClose": coefficient = FaceCoefficient.MouthClose; return true;
                case "mouthDimpleLeft": coefficient = FaceCoefficient.MouthDimpleLeft; return true;
                case "mouthDimpleRight": coefficient = FaceCoefficient.MouthDimpleRight; return true;
                case "mouthFrownLeft": coefficient = FaceCoefficient.MouthFrownLeft; return true;
                case "mouthFrownRight": coefficient = FaceCoefficient.MouthFrownRight; return true;
                case "mouthFunnel": coefficient = FaceCoefficient.MouthFunnel; return true;
                case "mouthLeft": coefficient = FaceCoefficient.MouthLeft; return true;
                case "mouthLowerDownLeft": coefficient = FaceCoefficient.MouthLowerDownLeft; return true;
                case "mouthLowerDownRight": coefficient = FaceCoefficient.MouthLowerDownRight; return true;
                case "mouthPressLeft": coefficient = FaceCoefficient.MouthPressLeft; return true;
                case "mouthPressRight": coefficient = FaceCoefficient.MouthPressRight; return true;
                case "mouthPucker": coefficient = FaceCoefficient.MouthPucker; return true;
                case "mouthRight": coefficient = FaceCoefficient.MouthRight; return true;
                case "mouthRollLower": coefficient = FaceCoefficient.MouthRollLower; return true;
                case "mouthRollUpper": coefficient = FaceCoefficient.MouthRollUpper; return true;
                case "mouthShrugLower": coefficient = FaceCoefficient.MouthShrugLower; return true;
                case "mouthShrugUpper": coefficient = FaceCoefficient.MouthShrugUpper; return true;
                case "mouthSmileLeft": coefficient = FaceCoefficient.MouthSmileLeft; return true;
                case "mouthSmileRight": coefficient = FaceCoefficient.MouthSmileRight; return true;
                case "mouthStretchLeft": coefficient = FaceCoefficient.MouthStretchLeft; return true;
                case "mouthStretchRight": coefficient = FaceCoefficient.MouthStretchRight; return true;
                case "mouthUpperUpLeft": coefficient = FaceCoefficient.MouthUpperUpLeft; return true;
                case "mouthUpperUpRight": coefficient = FaceCoefficient.MouthUpperUpRight; return true;
                case "noseSneerLeft": coefficient = FaceCoefficient.NoseSneerLeft; return true;
                case "noseSneerRight": coefficient = FaceCoefficient.NoseSneerRight; return true;
                default:
                    coefficient = default;
                    return false;
            }
        }
    }
}
