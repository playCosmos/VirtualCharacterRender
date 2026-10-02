using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Mixing
{
    /// <summary>
    /// One-performer P5 mixer entry point.
    ///
    /// Routed face/body/full-body frames pass through unchanged. Routed
    /// expressions form the base layer and one optional expression provider is
    /// blended on top. Pose/procedural layers can extend this component without
    /// changing the character-facing provider contract.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(6000)]
    public sealed class MotionExpressionMixer :
        MonoBehaviour,
        ITrackingMixProvider,
        IRuntimeMetricsSource
    {
        [Header("Inputs")]
        [SerializeField] private MonoBehaviour routedProviderBehaviour;
        [SerializeField] private bool autoFindRoutedProvider = true;
        [Tooltip("Optional expression-only layer. It never contributes performer-presence evidence.")]
        [SerializeField] private MonoBehaviour expressionLayerProviderBehaviour;

        [Header("Expression layer")]
        [SerializeField] private ExpressionBlendMode expressionBlendMode =
            ExpressionBlendMode.Override;
        [SerializeField, Range(0f, 1f)] private float expressionLayerWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float expressionDeadzone = 0f;
        [SerializeField, Min(0f)] private float expressionSmoothing = 0f;

        private ITrackingFrameProvider _routedProvider;
        private ITrackingPresenceProvider _presenceProvider;
        private ITrackingFrameProvider _expressionLayerProvider;

        private NormalizedExpressionState _targetExpressions;
        private NormalizedExpressionState _currentExpressions;
        private TrackingFrame _latestExpressionFrame;

        private long _lastBaseSequence = -1;
        private long _lastLayerSequence = -1;
        private string _lastBaseSourceId;
        private string _lastLayerSourceId;
        private long _expressionSequence;

        private bool _targetDirty = true;
        private bool _outputDirty;
        private float _nextProviderSearchTime;

        public TrackingPresenceSnapshot Presence =>
            _presenceProvider?.Presence ??
            default;

        private void Awake()
        {
            ResolveProviders();
        }

        private void Update()
        {
            if (_routedProvider == null &&
                autoFindRoutedProvider &&
                Time.unscaledTime >=
                    _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime + 1f;
                ResolveProviders();
            }

            if (_routedProvider == null)
            {
                ResetExpressionState();
                return;
            }

            UpdateExpressionTarget();
            UpdateExpressionOutput(
                Time.unscaledDeltaTime);
        }

        public void SetRoutedProvider(
            MonoBehaviour provider)
        {
            routedProviderBehaviour = provider;
            _routedProvider =
                provider as ITrackingFrameProvider;
            _presenceProvider =
                provider as ITrackingPresenceProvider;
            ResetExpressionState();
        }

        public void SetExpressionLayerProvider(
            MonoBehaviour provider)
        {
            expressionLayerProviderBehaviour =
                provider;
            _expressionLayerProvider =
                provider as ITrackingFrameProvider;
            ResetExpressionState();
        }

        public void ConfigureExpressionLayer(
            ExpressionBlendMode mode,
            float weight,
            float deadzone,
            float smoothing)
        {
            expressionBlendMode = mode;
            expressionLayerWeight =
                Mathf.Clamp01(weight);
            expressionDeadzone =
                Mathf.Clamp01(deadzone);
            expressionSmoothing =
                Mathf.Max(0f, smoothing);
            _targetDirty = true;
        }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            if (_routedProvider == null)
            {
                frame = null;
                return false;
            }

            return _routedProvider
                .TryGetLatestFace(
                    out frame);
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            if (_routedProvider == null)
            {
                frame = null;
                return false;
            }

            return _routedProvider
                .TryGetLatestBodyHands(
                    out frame);
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            if (_routedProvider == null)
            {
                frame = null;
                return false;
            }

            return _routedProvider
                .TryGetLatestHumanoidPose(
                    out frame);
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame =
                _latestExpressionFrame;

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
                    "mixer.expression.weight",
                    expressionLayerWeight,
                    "ratio"));
            output.Add(
                new RuntimeMetric(
                    "mixer.expression.deadzone",
                    expressionDeadzone,
                    "ratio"));
            output.Add(
                new RuntimeMetric(
                    "mixer.expression.mode",
                    (int)expressionBlendMode,
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "mixer.expression.smoothing",
                    expressionSmoothing,
                    "rate"));
            output.Add(
                new RuntimeMetric(
                    "mixer.expression.layer_configured",
                    _expressionLayerProvider != null
                        ? 1.0
                        : 0.0,
                    "bool"));
        }

        private void ResolveProviders()
        {
            if (routedProviderBehaviour is
                    ITrackingFrameProvider configured &&
                !(configured is ITrackingMixProvider))
            {
                _routedProvider =
                    configured;
                _presenceProvider =
                    routedProviderBehaviour as
                        ITrackingPresenceProvider;
            }

            if (expressionLayerProviderBehaviour is
                    ITrackingFrameProvider layer &&
                !ReferenceEquals(
                    layer,
                    this))
            {
                _expressionLayerProvider =
                    layer;
            }

            if (_routedProvider != null ||
                !autoFindRoutedProvider)
            {
                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            ITrackingFrameProvider direct = null;
            ITrackingPresenceProvider directPresence = null;
            MonoBehaviour directBehaviour = null;

            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(
                        behaviour,
                        this) ||
                    behaviour is ITrackingMixProvider)
                {
                    continue;
                }

                if (behaviour is
                    ITrackingRouteProvider route)
                {
                    _routedProvider = route;
                    _presenceProvider = route;
                    routedProviderBehaviour =
                        behaviour;
                    return;
                }

                if (direct == null &&
                    behaviour is
                        ITrackingFrameProvider provider)
                {
                    direct = provider;
                    directPresence =
                        behaviour as
                            ITrackingPresenceProvider;
                    directBehaviour =
                        behaviour;
                }
            }

            if (direct != null)
            {
                _routedProvider = direct;
                _presenceProvider =
                    directPresence;
                routedProviderBehaviour =
                    directBehaviour;
            }
        }

        private void UpdateExpressionTarget()
        {
            TrackingFrame baseFrame = null;
            TrackingFrame layerFrame = null;

            _routedProvider
                .TryGetLatestExpressions(
                    out baseFrame);

            if (_expressionLayerProvider != null &&
                !ReferenceEquals(
                    _expressionLayerProvider,
                    _routedProvider))
            {
                _expressionLayerProvider
                    .TryGetLatestExpressions(
                        out layerFrame);
            }

            var baseChanged =
                FrameChanged(
                    baseFrame,
                    ref _lastBaseSequence,
                    ref _lastBaseSourceId);

            var layerChanged =
                FrameChanged(
                    layerFrame,
                    ref _lastLayerSequence,
                    ref _lastLayerSourceId);

            if (!baseChanged &&
                !layerChanged &&
                !_targetDirty)
            {
                return;
            }

            _targetDirty = false;
            _outputDirty = true;

            if (baseFrame?.Expressions == null &&
                (layerFrame?.Expressions == null ||
                 expressionLayerWeight <= 0f))
            {
                _targetExpressions = null;
                return;
            }

            _targetExpressions =
                ExpressionMixerMath.Blend(
                    baseFrame?.Expressions,
                    layerFrame?.Expressions,
                    expressionLayerWeight,
                    expressionDeadzone,
                    expressionBlendMode);
        }

        private void UpdateExpressionOutput(
            float deltaSeconds)
        {
            if (_targetExpressions == null)
            {
                _currentExpressions = null;
                _latestExpressionFrame = null;
                _outputDirty = false;
                return;
            }

            if (!_outputDirty &&
                expressionSmoothing <= 0f)
            {
                return;
            }

            if (expressionSmoothing > 0f &&
                _currentExpressions != null)
            {
                var alpha =
                    ExpressionMixerMath
                        .SmoothAlpha(
                            expressionSmoothing,
                            deltaSeconds);

                _currentExpressions =
                    ExpressionMixerMath.Blend(
                        _currentExpressions,
                        _targetExpressions,
                        alpha,
                        0f,
                        ExpressionBlendMode.Override);
            }
            else
            {
                _currentExpressions =
                    _targetExpressions;
            }

            var nowUs =
                MonotonicClock
                    .NowMicroseconds();

            _latestExpressionFrame =
                new TrackingFrame(
                    ++_expressionSequence,
                    sourceTimestampUs:
                        nowUs,
                    validRegions:
                        TrackingRegion.Expressions,
                    confidence:
                        1f,
                    subjectDetected:
                        false,
                    expressions:
                        _currentExpressions,
                    sourceId:
                        "motion-expression-mixer",
                    runtimeTimestampUs:
                        nowUs);

            _outputDirty = false;
        }

        private static bool FrameChanged(
            TrackingFrame frame,
            ref long lastSequence,
            ref string lastSourceId)
        {
            var sequence =
                frame?.Sequence ?? -1;
            var sourceId =
                frame?.SourceId;

            if (sequence == lastSequence &&
                string.Equals(
                    sourceId,
                    lastSourceId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            lastSequence = sequence;
            lastSourceId = sourceId;
            return true;
        }

        private void ResetExpressionState()
        {
            _lastBaseSequence = -1;
            _lastLayerSequence = -1;
            _lastBaseSourceId = null;
            _lastLayerSourceId = null;
            _targetExpressions = null;
            _currentExpressions = null;
            _latestExpressionFrame = null;
            _targetDirty = true;
            _outputDirty = false;
        }
    }
}
