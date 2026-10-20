using System;
using UnityEngine;

namespace VCR.Runtime.Scene
{
    [Serializable]
    public struct SceneCameraSettings
    {
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;

        [Range(1f, 179f)]
        public float FieldOfView;

        [Min(0.001f)]
        public float NearClipPlane;

        [Min(0.01f)]
        public float FarClipPlane;

        public static SceneCameraSettings Default =>
            new()
            {
                LocalPosition =
                    new Vector3(0f, 1.35f, -3f),
                LocalEulerAngles =
                    Vector3.zero,
                FieldOfView = 35f,
                NearClipPlane = 0.05f,
                FarClipPlane = 100f
            };
    }
}
