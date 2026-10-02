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
        private const string ShaderName =
            "Hidden/VCR/TrackingLowLight";

        private Material _material;
        private RenderTexture _target;
        private int _preparedFrame = -1;
        private Texture _preparedTexture;

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

            if (_preparedFrame ==
                    Time.frameCount &&
                _preparedTexture != null)
            {
                return _preparedTexture;
            }

            EnsureMaterial();

            if (_material == null)
            {
                return source;
            }

            EnsureTarget(
                source.width,
                source.height);

            _material.SetFloat(
                "_Exposure",
                Mathf.Clamp(
                    exposure,
                    0.5f,
                    3f));

            _material.SetFloat(
                "_Gamma",
                Mathf.Clamp(
                    gamma,
                    0.5f,
                    2f));

            Graphics.Blit(
                source,
                _target,
                _material);

            _preparedFrame =
                Time.frameCount;
            _preparedTexture =
                _target;

            return _preparedTexture;
        }

        public void Dispose()
        {
            _preparedFrame = -1;
            _preparedTexture = null;

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
        }

        private void EnsureMaterial()
        {
            if (_material != null)
            {
                return;
            }

            var shader =
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

        private void EnsureTarget(
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
                return;
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

            _target.Create();
        }
    }
}
