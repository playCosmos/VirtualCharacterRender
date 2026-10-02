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
                Shadows = LightShadows.Soft
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
            value.Intensity =
                Mathf.Max(
                    0f,
                    value.Intensity);
            return value;
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
