using UnityEngine;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Applies environment lighting influence to a Unity Light while preserving
    /// and restoring the light's source values. Character materials are never
    /// rewritten by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentLightInfluenceTarget :
        MonoBehaviour,
        IEnvironmentLightingTarget
    {
        [SerializeField] private Light targetLight;

        private bool _captured;
        private Color _sourceColor;
        private float _sourceIntensity;

        public Light TargetLight => targetLight;

        private void Awake()
        {
            ResolveLight();
        }

        public void Configure(
            Light light)
        {
            if (_captured &&
                targetLight != light)
            {
                Restore();
            }

            targetLight = light;
            ResolveLight();
        }

        public bool ValidateEnvironmentLighting(
            EnvironmentLightingProfile profile,
            out string error)
        {
            ResolveLight();

            if (targetLight == null)
            {
                error =
                    "Environment lighting target requires a Unity Light.";
                return false;
            }

            error = null;
            return true;
        }

        public void ApplyEnvironmentLighting(
            EnvironmentLightingProfile profile)
        {
            ResolveLight();

            if (targetLight == null)
            {
                return;
            }

            Capture();

            var influenceColor =
                new Color(
                    profile.Red,
                    profile.Green,
                    profile.Blue,
                    _sourceColor.a);

            targetLight.color =
                Color.Lerp(
                    _sourceColor,
                    influenceColor,
                    profile.Weight);

            targetLight.intensity =
                _sourceIntensity *
                Mathf.Lerp(
                    1f,
                    profile.IntensityMultiplier,
                    profile.Weight);
        }

        public void Restore()
        {
            if (!_captured ||
                targetLight == null)
            {
                _captured = false;
                return;
            }

            targetLight.color =
                _sourceColor;
            targetLight.intensity =
                _sourceIntensity;
            _captured = false;
        }

        private void ResolveLight()
        {
            if (targetLight == null)
            {
                targetLight =
                    GetComponent<Light>() ??
                    GetComponentInChildren<
                        Light>(true);
            }
        }

        private void Capture()
        {
            if (_captured ||
                targetLight == null)
            {
                return;
            }

            _sourceColor =
                targetLight.color;
            _sourceIntensity =
                targetLight.intensity;
            _captured = true;
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
