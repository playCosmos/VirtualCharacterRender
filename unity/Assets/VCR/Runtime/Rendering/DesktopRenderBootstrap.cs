using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// P0 one-character desktop render baseline.
    ///
    /// Resolution preset and frame budget are separate from capability/profile
    /// selection. Higher custom resolutions are allowed but are not covered by
    /// the 720p60/1080p60 baseline guarantee.
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
        [SerializeField] private bool applyStandaloneWindowResolution = true;

        [Header("Frame pacing")]
        [SerializeField, Range(30, 240)] private int targetFrameRate = 60;
        [Tooltip("Disabled by default because monitor refresh may not be 60 Hz. P0 uses targetFrameRate and diagnostics instead.")]
        [SerializeField] private bool useVSync = false;
        [SerializeField] private bool runInBackground = true;

        public int RequestedWidth => GetResolution().width;
        public int RequestedHeight => GetResolution().height;
        public int TargetFrameRate => targetFrameRate;
        public RenderResolutionPreset ResolutionPreset => resolutionPreset;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            Apply();
        }

        [ContextMenu("Apply Render Baseline")]
        public void Apply()
        {
            Application.runInBackground = runInBackground;

            QualitySettings.vSyncCount =
                useVSync ? 1 : 0;

            Application.targetFrameRate =
                Math.Max(30, targetFrameRate);

            ConfigureCamera();

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

        public void SetPreset(RenderResolutionPreset preset)
        {
            resolutionPreset = preset;
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
    }
}
