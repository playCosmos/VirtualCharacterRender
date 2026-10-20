using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VCR.Runtime.Core;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// One-character desktop rendering baseline promoted into the P1 renderer core.
    ///
    /// Graphics quality and frame pacing remain independent from runtime capability
    /// selection. Global runtime overrides are captured once and can be restored
    /// during controlled shutdown.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopRenderBootstrap :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool transparentCameraBackground = true;
        [SerializeField] private bool allowHdr = false;
        [SerializeField] private bool allowMsaa = true;

        [Header("Resolution")]
        [SerializeField] private RenderResolutionPreset resolutionPreset =
            RenderResolutionPreset.Recommended1080p;
        [SerializeField, Min(320)] private int customWidth = 1920;
        [SerializeField, Min(240)] private int customHeight = 1080;
        [SerializeField] private bool applyStandaloneWindowResolution = false;

        [Header("Graphics quality")]
        [SerializeField, Range(0.5f, 2.0f)] private float renderScale = 1.0f;

        [Header("Frame pacing")]
        [SerializeField, Range(30, 240)] private int targetFrameRate = 60;
        [Tooltip("Disabled by default because monitor refresh may not be 60 Hz. Frame pacing is measured independently.")]
        [SerializeField] private bool useVSync = false;
        [SerializeField] private bool runInBackground = true;

        private bool _runtimeStateCaptured;
        private bool _originalRunInBackground;
        private int _originalVSyncCount;
        private int _originalTargetFrameRate;

        private UniversalRenderPipelineAsset _capturedUrpAsset;
        private float _originalRenderScale;

        private bool _cameraStateCaptured;
        private CameraClearFlags _originalClearFlags;
        private Color _originalBackgroundColor;
        private bool _originalAllowHdr;
        private bool _originalAllowMsaa;

        public int RequestedWidth => GetResolution().width;
        public int RequestedHeight => GetResolution().height;
        public int TargetFrameRate => targetFrameRate;
        public float RenderScale => renderScale;
        public bool UseVSync => useVSync;
        public bool RunInBackground => runInBackground;
        public RenderResolutionPreset ResolutionPreset => resolutionPreset;

        private void Awake()
        {
            ResolveTargetCamera();
            Apply();
        }

        [ContextMenu("Apply Render Baseline")]
        public void Apply()
        {
            ResolveTargetCamera();
            CaptureRuntimeState();
            SanitizeConfiguration();

            Application.runInBackground = runInBackground;

            QualitySettings.vSyncCount =
                useVSync ? 1 : 0;

            Application.targetFrameRate =
                Math.Max(30, targetFrameRate);

            ConfigureCamera();
            ConfigureRenderScale();

            if (applyStandaloneWindowResolution &&
                Application.isPlaying &&
                !Application.isEditor)
            {
                var resolution = GetResolution();

                Screen.SetResolution(
                    resolution.width,
                    resolution.height,
                    FullScreenMode.Windowed);
            }
        }

        public RenderRuntimeSettings CaptureSettings()
        {
            var resolution = GetResolution();

            return new RenderRuntimeSettings
            {
                ResolutionPreset = resolutionPreset,
                Width = resolution.width,
                Height = resolution.height,
                RenderScale = renderScale,
                TargetFrameRate = targetFrameRate,
                UseVSync = useVSync,
                RunInBackground = runInBackground
            };
        }

        public void Apply(RenderRuntimeSettings settings)
        {
            resolutionPreset =
                SanitizeResolutionPreset(
                    settings.ResolutionPreset);
            customWidth =
                Math.Max(
                    320,
                    settings.Width);
            customHeight =
                Math.Max(
                    240,
                    settings.Height);
            renderScale =
                SanitizeRenderScale(
                    settings.RenderScale);
            targetFrameRate =
                Math.Clamp(
                    settings.TargetFrameRate,
                    30,
                    240);
            useVSync = settings.UseVSync;
            runInBackground = settings.RunInBackground;

            Apply();
        }

        public void SetPreset(RenderResolutionPreset preset)
        {
            resolutionPreset =
                SanitizeResolutionPreset(
                    preset);
            Apply();
        }

        public void SetCustomResolution(
            int width,
            int height)
        {
            customWidth = Math.Max(320, width);
            customHeight = Math.Max(240, height);
            resolutionPreset = RenderResolutionPreset.Custom;
            Apply();
        }

        public void SetRenderScale(float scale)
        {
            renderScale =
                SanitizeRenderScale(
                    scale);
            Apply();
        }

        public void SetFramePacing(
            int framesPerSecond,
            bool vSync)
        {
            targetFrameRate = Math.Clamp(
                framesPerSecond,
                30,
                240);
            useVSync = vSync;
            Apply();
        }

        public void SetRunInBackground(bool enabled)
        {
            runInBackground = enabled;
            Apply();
        }

        /// <summary>
        /// Restores process-global rendering settings and the camera values that
        /// were present before the first P1 runtime Apply call.
        /// </summary>
        public void RestoreRuntimeOverrides()
        {
            if (_runtimeStateCaptured)
            {
                Application.runInBackground =
                    _originalRunInBackground;
                QualitySettings.vSyncCount =
                    _originalVSyncCount;
                Application.targetFrameRate =
                    _originalTargetFrameRate;

                if (_capturedUrpAsset != null)
                {
                    _capturedUrpAsset.renderScale =
                        _originalRenderScale;
                }

                _runtimeStateCaptured = false;
                _capturedUrpAsset = null;
            }

            if (_cameraStateCaptured &&
                targetCamera != null)
            {
                targetCamera.clearFlags =
                    _originalClearFlags;
                targetCamera.backgroundColor =
                    _originalBackgroundColor;
                targetCamera.allowHDR =
                    _originalAllowHdr;
                targetCamera.allowMSAA =
                    _originalAllowMsaa;

                _cameraStateCaptured = false;
            }
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "render.screen.width",
                Screen.width,
                "px"));

            output.Add(new RuntimeMetric(
                "render.screen.height",
                Screen.height,
                "px"));

            output.Add(new RuntimeMetric(
                "render.target_fps",
                Application.targetFrameRate,
                "fps"));

            output.Add(new RuntimeMetric(
                "render.vsync",
                QualitySettings.vSyncCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.scale",
                GetActiveUrpAsset()?.renderScale ?? 1.0f,
                "ratio"));
        }

        private void CaptureRuntimeState()
        {
            if (!_runtimeStateCaptured)
            {
                _originalRunInBackground =
                    Application.runInBackground;
                _originalVSyncCount =
                    QualitySettings.vSyncCount;
                _originalTargetFrameRate =
                    Application.targetFrameRate;

                _capturedUrpAsset = GetActiveUrpAsset();
                if (_capturedUrpAsset != null)
                {
                    _originalRenderScale =
                        _capturedUrpAsset.renderScale;
                }

                _runtimeStateCaptured = true;
            }

            if (!_cameraStateCaptured &&
                targetCamera != null)
            {
                _originalClearFlags =
                    targetCamera.clearFlags;
                _originalBackgroundColor =
                    targetCamera.backgroundColor;
                _originalAllowHdr =
                    targetCamera.allowHDR;
                _originalAllowMsaa =
                    targetCamera.allowMSAA;
                _cameraStateCaptured = true;
            }
        }

        private void ResolveTargetCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void ConfigureCamera()
        {
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.clearFlags =
                CameraClearFlags.SolidColor;

            if (transparentCameraBackground)
            {
                var background =
                    targetCamera.backgroundColor;
                background.a = 0f;
                targetCamera.backgroundColor =
                    background;
            }

            targetCamera.allowHDR = allowHdr;
            targetCamera.allowMSAA = allowMsaa;
        }

        private void ConfigureRenderScale()
        {
            var asset = GetActiveUrpAsset();
            if (asset == null)
            {
                return;
            }

            renderScale =
                SanitizeRenderScale(
                    renderScale);
            asset.renderScale =
                renderScale;
        }

        private void SanitizeConfiguration()
        {
            resolutionPreset =
                SanitizeResolutionPreset(
                    resolutionPreset);
            customWidth =
                Math.Max(
                    320,
                    customWidth);
            customHeight =
                Math.Max(
                    240,
                    customHeight);
            renderScale =
                SanitizeRenderScale(
                    renderScale);
            targetFrameRate =
                Math.Clamp(
                    targetFrameRate,
                    30,
                    240);
        }

        private static float SanitizeRenderScale(
            float value)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return 1f;
            }

            return Mathf.Clamp(
                value,
                0.5f,
                2.0f);
        }

        private static RenderResolutionPreset
            SanitizeResolutionPreset(
                RenderResolutionPreset value) =>
                value == RenderResolutionPreset.Minimum720p ||
                value == RenderResolutionPreset.Recommended1080p ||
                value == RenderResolutionPreset.Custom
                    ? value
                    : RenderResolutionPreset.Recommended1080p;

        private static UniversalRenderPipelineAsset GetActiveUrpAsset()
        {
            return
                GraphicsSettings.currentRenderPipeline
                    as UniversalRenderPipelineAsset ??
                QualitySettings.renderPipeline
                    as UniversalRenderPipelineAsset;
        }

        private (int width, int height) GetResolution()
        {
            return resolutionPreset switch
            {
                RenderResolutionPreset.Minimum720p =>
                    (1280, 720),

                RenderResolutionPreset.Recommended1080p =>
                    (1920, 1080),

                _ =>
                    (
                        Math.Max(320, customWidth),
                        Math.Max(240, customHeight)
                    )
            };
        }

        private void OnDestroy()
        {
            RestoreRuntimeOverrides();
        }
    }
}
