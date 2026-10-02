using System;
using UnityEngine;

namespace VCR.Runtime.Scene
{
    [Serializable]
    public struct SceneLightSettings
    {
        public bool Enabled;
        public Vector3 LocalEulerAngles;
        public Color Color;

        [Min(0f)]
        public float Intensity;

        public LightShadows Shadows;

        public static SceneLightSettings DefaultDirectional =>
            new()
            {
                Enabled = true,
                LocalEulerAngles =
                    new Vector3(45f, -30f, 0f),
                Color = Color.white,
                Intensity = 1f,
                Shadows = LightShadows.Soft
            };
    }
}
