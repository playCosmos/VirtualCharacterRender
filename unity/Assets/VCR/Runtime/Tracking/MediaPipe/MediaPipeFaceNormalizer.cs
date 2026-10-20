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

                if (float.IsFinite(rotation.x) &&
                    float.IsFinite(rotation.y) &&
                    float.IsFinite(rotation.z) &&
                    float.IsFinite(rotation.w))
                {
                    headRotation = new TrackingQuaternion(
                        rotation.x,
                        rotation.y,
                        rotation.z,
                        rotation.w);
                }

                if (float.IsFinite(translation.x) &&
                    float.IsFinite(translation.y) &&
                    float.IsFinite(translation.z))
                {
                    headPosition = new TrackingVector3(
                        translation.x,
                        translation.y,
                        translation.z);
                }
            }

            state =
                new NormalizedFaceState(
                    headRotation,
                    headPosition,
                    coefficients,
                    SnapshotArrayOwnership.Transfer);

            return true;
        }

        private static float Clamp01(float value)
        {
            if (!float.IsFinite(value) ||
                value <= 0f)
            {
                return 0f;
            }

            if (value >= 1f)
            {
                return 1f;
            }

            return value;
        }

        private static bool TryMapCoefficient(
            string name,
            out FaceCoefficient coefficient)
        {
            return FaceCoefficientNames.TryParse(name, out coefficient);
        }
    }
}
