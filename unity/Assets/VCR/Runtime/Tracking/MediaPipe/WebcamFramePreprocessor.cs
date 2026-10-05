using UnityEngine;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Optional one-pass GPU preprocessing for webcam inference input.
    /// Disabled mode returns the original texture with no render-target work.
    /// The processed texture is cached once per Unity frame so Face/Holistic
    /// can share it.
    /// </summary>
    public sealed class WebcamFramePreprocessor
    {
        private const string ShaderResourcePath =
            "TrackingLowLight";
        private const string ShaderName =
            "Hidden/VCR/TrackingLowLight";

        private Material _material;
        private RenderTexture _target;
        private bool _materialResolveAttempted;
        private int _preparedFrame = -1;
        private Texture _preparedTexture;
        private Texture _preparedSource;
        private WebcamPreprocessingMode _preparedMode;
        private float _preparedExposure;
        private float _preparedGamma;

        public Texture Prepare(
            Texture source,
            WebcamPreprocessingMode mode,
            float exposure,
            float gamma)
        {
            if (source == null ||
                mode ==
                WebcamPreprocessingMode.Disabled)
            {
                return source;
            }

            var clampedExposure =
                Mathf.Clamp(
                    exposure,
                    0.5f,
                    3f);
            var clampedGamma =
                Mathf.Clamp(
                    gamma,
                    0.5f,
                    2f);
            var targetWidth =
                Mathf.Max(
                    16,
                    source.width);
            var targetHeight =
                Mathf.Max(
                    16,
                    source.height);

            if (_preparedFrame ==
                    Time.frameCount &&
                _preparedTexture != null &&
                ReferenceEquals(
                    _preparedSource,
                    source) &&
                _preparedMode == mode &&
                Mathf.Approximately(
                    _preparedExposure,
                    clampedExposure) &&
                Mathf.Approximately(
                    _preparedGamma,
                    clampedGamma) &&
                _target != null &&
                _target.width ==
                    targetWidth &&
                _target.height ==
                    targetHeight &&
                _target.IsCreated())
            {
                return _preparedTexture;
            }

            EnsureMaterial();

            if (_material == null ||
                !EnsureTarget(
                    targetWidth,
                    targetHeight))
            {
                return source;
            }

            _material.SetFloat(
                "_Exposure",
                clampedExposure);

            _material.SetFloat(
                "_Gamma",
                clampedGamma);

            Graphics.Blit(
                source,
                _target,
                _material);

            _preparedFrame =
                Time.frameCount;
            _preparedTexture =
                _target;
            _preparedSource =
                source;
            _preparedMode =
                mode;
            _preparedExposure =
                clampedExposure;
            _preparedGamma =
                clampedGamma;

            return _preparedTexture;
        }

        public void Dispose()
        {
            _preparedFrame = -1;
            _preparedTexture = null;
            _preparedSource = null;
            _preparedMode =
                default;
            _preparedExposure = 0f;
            _preparedGamma = 0f;

            if (_target != null)
            {
                _target.Release();

                if (Application.isPlaying)
                {
                    Object.Destroy(
                        _target);
                }
                else
                {
                    Object.DestroyImmediate(
                        _target);
                }

                _target = null;
            }

            if (_material != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(
                        _material);
                }
                else
                {
                    Object.DestroyImmediate(
                        _material);
                }

                _material = null;
            }

            _materialResolveAttempted =
                false;
        }

        private void EnsureMaterial()
        {
            if (_material != null ||
                _materialResolveAttempted)
            {
                return;
            }

            _materialResolveAttempted =
                true;

            var shader =
                Resources.Load<Shader>(
                    ShaderResourcePath) ??
                Shader.Find(
                    ShaderName);

            if (shader == null ||
                !shader.isSupported)
            {
                return;
            }

            _material =
                new Material(shader)
                {
                    hideFlags =
                        HideFlags.DontSave
                };
        }

        private bool EnsureTarget(
            int width,
            int height)
        {
            width =
                Mathf.Max(
                    16,
                    width);
            height =
                Mathf.Max(
                    16,
                    height);

            if (_target != null &&
                _target.width == width &&
                _target.height == height)
            {
                return
                    _target.IsCreated() ||
                    _target.Create();
            }

            if (_target != null)
            {
                _target.Release();

                if (Application.isPlaying)
                {
                    Object.Destroy(
                        _target);
                }
                else
                {
                    Object.DestroyImmediate(
                        _target);
                }
            }

            _target =
                new RenderTexture(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32)
                {
                    name =
                        "VCR Tracking Preprocess",
                    hideFlags =
                        HideFlags.DontSave,
                    useMipMap = false,
                    autoGenerateMips = false
                };

            return _target.Create();
        }
    }
}
