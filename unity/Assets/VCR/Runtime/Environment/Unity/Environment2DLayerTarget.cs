using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Lightweight controller for preconfigured 2D environment visuals.
    ///
    /// StaticImage and Video do not require an environment recurring update.
    /// Parallax moves only when the environment runtime dispatches an update,
    /// allowing Hz10/Hz30/EveryFrame policy to own the cost.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Environment2DLayerTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget,
        IRuntimeMetricsSource
    {
        [SerializeField] private Environment2DLayerMode mode =
            Environment2DLayerMode.StaticImage;
        [SerializeField] private RawImage image;
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Parallax")]
        [SerializeField] private RectTransform layerTransform;
        [SerializeField] private Transform parallaxReference;
        [SerializeField] private Vector2 parallaxScale =
            new Vector2(20f, 20f);

        [Header("Video")]
        [SerializeField] private bool autoPlayVideo = true;
        [SerializeField] private bool pauseVideoOnDisable = true;

        private Vector2 _baseAnchoredPosition;
        private Vector3 _baseReferencePosition;
        private bool _parallaxReferenceCaptured;
        private long _parallaxUpdateCount;

        public Environment2DLayerMode Mode => mode;
        public long ParallaxUpdateCount => _parallaxUpdateCount;

        private void Awake()
        {
            CaptureParallaxReference();
        }

        private void OnEnable()
        {
            if (mode ==
                    Environment2DLayerMode.Video &&
                autoPlayVideo &&
                videoPlayer != null &&
                Application.isPlaying)
            {
                videoPlayer.Play();
            }
        }

        private void OnDisable()
        {
            if (pauseVideoOnDisable &&
                videoPlayer != null &&
                videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
            }
        }

        public void ConfigureStaticImage(
            RawImage target)
        {
            mode =
                Environment2DLayerMode.StaticImage;
            image = target;
            videoPlayer = null;
            _parallaxReferenceCaptured = false;
        }

        public void ConfigureVideo(
            VideoPlayer player,
            RawImage target = null,
            bool autoPlay = true)
        {
            mode =
                Environment2DLayerMode.Video;
            videoPlayer = player;
            image = target;
            autoPlayVideo = autoPlay;
            _parallaxReferenceCaptured = false;
        }

        public void ConfigureParallax(
            RawImage target,
            RectTransform transformTarget,
            Transform reference,
            Vector2 scale)
        {
            mode =
                Environment2DLayerMode.Parallax;
            image = target;
            layerTransform = transformTarget;
            parallaxReference = reference;
            parallaxScale = scale;
            CaptureParallaxReference();
        }

        public bool ValidateConfiguration(
            out string error)
        {
            error = null;

            if (mode ==
                    Environment2DLayerMode.StaticImage &&
                image == null)
            {
                error =
                    "StaticImage environment layer requires a RawImage.";
                return false;
            }

            if (mode ==
                    Environment2DLayerMode.Video &&
                videoPlayer == null)
            {
                error =
                    "Video environment layer requires a VideoPlayer.";
                return false;
            }

            if (mode ==
                Environment2DLayerMode.Parallax)
            {
                if (layerTransform == null)
                {
                    error =
                        "Parallax environment layer requires a RectTransform.";
                    return false;
                }

                if (parallaxReference == null)
                {
                    error =
                        "Parallax environment layer requires a reference Transform.";
                    return false;
                }
            }

            return true;
        }

        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
            if (mode !=
                    Environment2DLayerMode.Parallax ||
                layerTransform == null ||
                parallaxReference == null)
            {
                return;
            }

            if (!_parallaxReferenceCaptured)
            {
                CaptureParallaxReference();
            }

            var delta =
                parallaxReference.localPosition -
                _baseReferencePosition;

            layerTransform.anchoredPosition =
                _baseAnchoredPosition +
                new Vector2(
                    delta.x *
                        parallaxScale.x,
                    delta.y *
                        parallaxScale.y);

            _parallaxUpdateCount++;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "environment.2d.mode",
                    (int)mode,
                    "enum"));

            output.Add(
                new RuntimeMetric(
                    "environment.2d.parallax_updates",
                    _parallaxUpdateCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "environment.2d.video_playing",
                    videoPlayer != null &&
                    videoPlayer.isPlaying
                        ? 1.0
                        : 0.0,
                    "bool"));
        }

        private void CaptureParallaxReference()
        {
            if (layerTransform == null ||
                parallaxReference == null)
            {
                _parallaxReferenceCaptured =
                    false;
                return;
            }

            _baseAnchoredPosition =
                layerTransform.anchoredPosition;
            _baseReferencePosition =
                parallaxReference.localPosition;
            _parallaxReferenceCaptured =
                true;
        }
    }
}
