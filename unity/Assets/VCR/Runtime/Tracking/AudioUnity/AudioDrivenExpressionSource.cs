using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.AudioUnity
{
    /// <summary>
    /// Optional audio-driven mouth fallback.
    ///
    /// It emits expression state only and intentionally does not implement
    /// ITrackingPresenceProvider: audio level is not visual subject-presence
    /// evidence. Routing/mixing decides when this optional fallback contributes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDrivenExpressionSource :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingSourceHealthProvider,
        IRuntimeMetricsSource
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField, Range(64, 2048)] private int sampleCount = 256;
        [SerializeField, Range(0f, 0.25f)] private float noiseThreshold = 0.01f;
        [SerializeField, Range(0f, 100f)] private float gain = 18f;
        [SerializeField, Min(0.001f)] private float attackSeconds = 0.035f;
        [SerializeField, Min(0.001f)] private float releaseSeconds = 0.12f;

        private float[] _samples;
        private TrackingFrame _latest;
        private long _sequence;
        private const float PublishEpsilon =
            0.0005f;

        private float _value;
        private float _rms;
        private float _lastPublishedValue =
            float.NaN;
        private long _lastSampleTimestampUs;

        public float CurrentValue => _value;
        public float CurrentRms => _rms;

        private void Awake()
        {
            EnsureBuffer();
        }

        private void Update()
        {
            if (audioSource == null ||
                !audioSource.isActiveAndEnabled)
            {
                UpdateValue(
                    0f);
                return;
            }

            EnsureBuffer();

            audioSource.GetOutputData(
                _samples,
                channel: 0);

            _rms =
                AudioDrivenExpressionMath
                    .ComputeRms(
                        _samples);

            var target =
                AudioDrivenExpressionMath
                    .NormalizeLevel(
                        _rms,
                        noiseThreshold,
                        gain);

            UpdateValue(
                target);
        }

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            if ((region &
                 TrackingRegion.Expressions) == 0)
            {
                snapshot = default;
                return false;
            }

            var state =
                !isActiveAndEnabled
                    ? TrackingSourceHealthState.Stopped
                    : audioSource == null ||
                      !audioSource.isActiveAndEnabled
                        ? TrackingSourceHealthState.Degraded
                        : TrackingSourceHealthState.Healthy;

            snapshot =
                new TrackingSourceHealthSnapshot(
                    "audio-mouth-fallback",
                    TrackingSourceKind.AudioFallback,
                    TrackingRegion.Expressions,
                    new TrackingSourceHealth(
                        state,
                        _lastSampleTimestampUs,
                        _value,
                        null),
                    _latest?.RuntimeTimestampUs ?? 0);
            return true;
        }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = _latest;
            return frame != null;
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
                    "tracking.audio.rms",
                    _rms,
                    "ratio"));

            output.Add(
                new RuntimeMetric(
                    "tracking.audio.mouth",
                    _value,
                    "ratio"));
        }

        private void UpdateValue(
            float target)
        {
            target =
                Mathf.Clamp01(
                    target);

            var next =
                AudioDrivenExpressionMath
                    .Smooth(
                        _value,
                        target,
                        Time.unscaledDeltaTime,
                        attackSeconds,
                        releaseSeconds);

            if (Mathf.Abs(
                    next -
                    target) <=
                PublishEpsilon)
            {
                next =
                    target;
            }

            _value =
                Mathf.Clamp01(
                    next);

            var nowUs =
                MonotonicClock
                    .NowMicroseconds();
            _lastSampleTimestampUs =
                nowUs;

            var boundaryChanged =
                (_value <= 0f ||
                 _value >= 1f) &&
                _lastPublishedValue !=
                    _value;

            if (_latest != null &&
                !boundaryChanged &&
                !float.IsNaN(
                    _lastPublishedValue) &&
                Mathf.Abs(
                    _value -
                    _lastPublishedValue) <
                    PublishEpsilon)
            {
                return;
            }

            _lastPublishedValue =
                _value;

            var standard =
                new float[
                    (int)StandardExpression.Count];

            standard[
                (int)StandardExpression.Aa] =
                _value;

            _latest =
                new TrackingFrame(
                    ++_sequence,
                    sourceTimestampUs:
                        nowUs,
                    validRegions:
                        TrackingRegion.Expressions,
                    confidence:
                        _value,
                    subjectDetected:
                        false,
                    expressions:
                        new NormalizedExpressionState(
                            standard),
                    sourceId:
                        "audio-mouth-fallback",
                    runtimeTimestampUs:
                        nowUs);
        }

        private void EnsureBuffer()
        {
            var count =
                Mathf.ClosestPowerOfTwo(
                    Mathf.Clamp(
                        sampleCount,
                        64,
                        2048));

            if (_samples == null ||
                _samples.Length != count)
            {
                _samples =
                    new float[count];
            }
        }
    }
}
