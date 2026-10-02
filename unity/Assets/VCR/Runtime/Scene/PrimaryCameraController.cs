using UnityEngine;

namespace VCR.Runtime.Scene
{
    /// <summary>
    /// P1 primary-camera pose/projection adapter.
    ///
    /// Rendering-surface concerns such as transparent clear, HDR and MSAA stay
    /// in DesktopRenderBootstrap. This component owns only camera transform and
    /// projection values and has no per-frame update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrimaryCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SceneCameraSettings settings =
            new()
            {
                LocalPosition = new Vector3(0f, 1.35f, -3f),
                LocalEulerAngles = Vector3.zero,
                FieldOfView = 35f,
                NearClipPlane = 0.05f,
                FarClipPlane = 100f
            };

        private bool _captured;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private float _originalFieldOfView;
        private float _originalNearClip;
        private float _originalFarClip;

        public Camera TargetCamera => targetCamera;
        public SceneCameraSettings Settings => settings;

        private void Awake()
        {
            ResolveCamera();
            Apply();
        }

        public void Configure(
            Camera camera,
            SceneCameraSettings value)
        {
            if (_captured &&
                targetCamera != camera)
            {
                Restore();
            }

            targetCamera = camera;
            settings = Sanitize(value);
            Apply();
        }

        public void Apply()
        {
            ResolveCamera();
            if (targetCamera == null)
            {
                return;
            }

            Capture();

            var value = Sanitize(settings);
            settings = value;

            targetCamera.transform.localPosition =
                value.LocalPosition;
            targetCamera.transform.localRotation =
                Quaternion.Euler(
                    value.LocalEulerAngles);
            targetCamera.fieldOfView =
                value.FieldOfView;
            targetCamera.nearClipPlane =
                value.NearClipPlane;
            targetCamera.farClipPlane =
                value.FarClipPlane;
        }

        public void Restore()
        {
            if (!_captured ||
                targetCamera == null)
            {
                _captured = false;
                return;
            }

            targetCamera.transform.localPosition =
                _originalLocalPosition;
            targetCamera.transform.localRotation =
                _originalLocalRotation;
            targetCamera.fieldOfView =
                _originalFieldOfView;
            targetCamera.nearClipPlane =
                _originalNearClip;
            targetCamera.farClipPlane =
                _originalFarClip;

            _captured = false;
        }

        private void ResolveCamera()
        {
            if (targetCamera == null)
            {
                targetCamera =
                    GetComponent<Camera>() ??
                    GetComponentInChildren<Camera>(true) ??
                    Camera.main;
            }
        }

        private void Capture()
        {
            if (_captured)
            {
                return;
            }

            _originalLocalPosition =
                targetCamera.transform.localPosition;
            _originalLocalRotation =
                targetCamera.transform.localRotation;
            _originalFieldOfView =
                targetCamera.fieldOfView;
            _originalNearClip =
                targetCamera.nearClipPlane;
            _originalFarClip =
                targetCamera.farClipPlane;
            _captured = true;
        }

        private static SceneCameraSettings Sanitize(
            SceneCameraSettings value)
        {
            value.FieldOfView =
                Mathf.Clamp(
                    value.FieldOfView,
                    1f,
                    179f);

            value.NearClipPlane =
                Mathf.Max(
                    0.001f,
                    value.NearClipPlane);

            value.FarClipPlane =
                Mathf.Max(
                    value.NearClipPlane + 0.01f,
                    value.FarClipPlane);

            return value;
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
