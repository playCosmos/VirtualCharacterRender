using UnityEngine;

namespace VCR.Runtime.Scene
{
    /// <summary>
    /// P1 adapter for the primary environment light.
    /// No Update loop is used; state changes are explicit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrimaryLightController : MonoBehaviour
    {
        [SerializeField] private Light targetLight;
        [SerializeField] private SceneLightSettings settings =
            new()
            {
                Enabled = true,
                LocalEulerAngles =
                    new Vector3(45f, -30f, 0f),
                Color = Color.white,
                Intensity = 1f,
                Shadows = LightShadows.None
            };

        private bool _captured;
        private bool _originalEnabled;
        private Quaternion _originalLocalRotation;
        private Color _originalColor;
        private float _originalIntensity;
        private LightShadows _originalShadows;

        public Light TargetLight => targetLight;
        public SceneLightSettings Settings => settings;

        private void Awake()
        {
            ResolveLight();
            Apply();
        }

        public void Configure(
            Light light,
            SceneLightSettings value)
        {
            if (_captured &&
                targetLight != light)
            {
                Restore();
            }

            targetLight = light;
            settings = Sanitize(value);
            Apply();
        }

        public void Apply()
        {
            ResolveLight();
            if (targetLight == null)
            {
                return;
            }

            Capture();

            var value = Sanitize(settings);
            settings = value;

            targetLight.enabled =
                value.Enabled;
            targetLight.transform.localRotation =
                Quaternion.Euler(
                    value.LocalEulerAngles);
            targetLight.color =
                value.Color;
            targetLight.intensity =
                value.Intensity;
            targetLight.shadows =
                value.Shadows;
        }

        public void Restore()
        {
            if (!_captured ||
                targetLight == null)
            {
                _captured = false;
                return;
            }

            targetLight.enabled =
                _originalEnabled;
            targetLight.transform.localRotation =
                _originalLocalRotation;
            targetLight.color =
                _originalColor;
            targetLight.intensity =
                _originalIntensity;
            targetLight.shadows =
                _originalShadows;

            _captured = false;
        }

        private void ResolveLight()
        {
            if (targetLight == null)
            {
                targetLight =
                    GetComponent<Light>() ??
                    GetComponentInChildren<Light>(true);
            }
        }

        private void Capture()
        {
            if (_captured)
            {
                return;
            }

            _originalEnabled =
                targetLight.enabled;
            _originalLocalRotation =
                targetLight.transform.localRotation;
            _originalColor =
                targetLight.color;
            _originalIntensity =
                targetLight.intensity;
            _originalShadows =
                targetLight.shadows;
            _captured = true;
        }

        private static SceneLightSettings Sanitize(
            SceneLightSettings value)
        {
            var fallback =
                SceneLightSettings.DefaultDirectional;

            value.LocalEulerAngles =
                new Vector3(
                    FiniteOrDefault(
                        value.LocalEulerAngles.x,
                        fallback.LocalEulerAngles.x),
                    FiniteOrDefault(
                        value.LocalEulerAngles.y,
                        fallback.LocalEulerAngles.y),
                    FiniteOrDefault(
                        value.LocalEulerAngles.z,
                        fallback.LocalEulerAngles.z));

            value.Color =
                new Color(
                    FiniteOrDefault(
                        value.Color.r,
                        fallback.Color.r),
                    FiniteOrDefault(
                        value.Color.g,
                        fallback.Color.g),
                    FiniteOrDefault(
                        value.Color.b,
                        fallback.Color.b),
                    FiniteOrDefault(
                        value.Color.a,
                        fallback.Color.a));

            value.Intensity =
                Mathf.Max(
                    0f,
                    FiniteOrDefault(
                        value.Intensity,
                        fallback.Intensity));

            value.Shadows =
                value.Shadows == LightShadows.None ||
                value.Shadows == LightShadows.Hard ||
                value.Shadows == LightShadows.Soft
                    ? value.Shadows
                    : fallback.Shadows;

            return value;
        }

        private static float FiniteOrDefault(
            float value,
            float fallback) =>
                !float.IsNaN(value) &&
                !float.IsInfinity(value)
                    ? value
                    : fallback;

        private void OnDestroy()
        {
            Restore();
        }
    }
}
