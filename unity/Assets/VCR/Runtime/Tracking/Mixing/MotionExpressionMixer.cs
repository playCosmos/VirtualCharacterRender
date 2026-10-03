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
        [Tooltip("Optional primary humanoid-pose layer. Existing scenes keep this as the first overlay slot.")]
        [SerializeField] private MonoBehaviour poseLayerProviderBehaviour;
        [Tooltip("Additional humanoid-pose layers applied in array order after the primary pose layer.")]
        [SerializeField] private HumanoidPoseLayerSlot[] additionalPoseLayers =
            Array.Empty<HumanoidPoseLayerSlot>();
        [Tooltip("Optional expression-only layer. It never contributes performer-presence evidence.")]
        [SerializeField] private MonoBehaviour expressionLayerProviderBehaviour;

        [Header("Pose layer")]
        [SerializeField] private HumanoidPoseLayerSettings poseLayerSettings = new();

        [Header("Expression layer")]
        [SerializeField] private ExpressionBlendMode expressionBlendMode =
            ExpressionBlendMode.Override;
        [SerializeField, Range(0f, 1f)] private float expressionLayerWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float expressionDeadzone = 0f;
        [SerializeField, Min(0f)] private float expressionSmoothing = 0f;

        private ITrackingFrameProvider _routedProvider;
        private ITrackingPresenceProvider _presenceProvider;
        private ITrackingFrameProvider _poseLayerProvider;
        private ITrackingFrameProvider _expressionLayerProvider;

        private TrackingFrame _latestPoseFrame;
        private long _lastBasePoseSequence = -1;
        private long _lastLayerPoseSequence = -1;
        private string _lastBasePoseSourceId;
        private string _lastLayerPoseSourceId;
        private long _poseSequence;
        private bool _poseDirty = true;
        private bool _poseSpaceMismatch;
        private long[] _additionalPoseLayerSequences =
            Array.Empty<long>();
        private string[] _additionalPoseLayerSourceIds =
            Array.Empty<string>();

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
        private bool _expressionSmoothingActive;
        private float _nextProviderSearchTime;

        public TrackingPresenceSnapshot Presence =>
            _presenceProvider?.Presence ??
            default;

        public bool HumanoidPosePreSmoothed => false;
        public bool ExpressionsPreSmoothed =>
            expressionSmoothing > 0f;

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
                ResetPoseState();
                ResetExpressionState();
                return;
            }

            UpdatePoseOutput();
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
            ResetPoseState();
            ResetExpressionState();
        }

        public void SetPoseLayerProvider(
            MonoBehaviour provider)
        {
            poseLayerProviderBehaviour =
                provider;
            _poseLayerProvider =
                provider as ITrackingFrameProvider;
            ResetPoseState();
        }

        public void ConfigurePoseLayer(
            HumanoidPoseLayerSettings settings)
        {
            poseLayerSettings =
                settings ??
                new HumanoidPoseLayerSettings();
            _poseDirty = true;
        }

        public void SetAdditionalPoseLayers(
            params HumanoidPoseLayerSlot[] layers)
        {
            additionalPoseLayers =
                layers == null
                    ? Array.Empty<
                        HumanoidPoseLayerSlot>()
                    : (HumanoidPoseLayerSlot[])
                        layers.Clone();

            ResetPoseState();
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
            frame =
                _latestPoseFrame;

            return frame != null;
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
                    "mixer.pose.layer_configured",
                    _poseLayerProvider != null
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "mixer.pose.weight",
                    poseLayerSettings?.Weight ?? 0f,
                    "ratio"));
            output.Add(
                new RuntimeMetric(
                    "mixer.pose.role",
                    (int)(poseLayerSettings?.Role ??
                        MotionLayerRole.Base),
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "mixer.pose.mode",
                    (int)(poseLayerSettings?.BlendMode ??
                        HumanoidPoseBlendMode.Override),
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "mixer.pose.space_mismatch",
                    _poseSpaceMismatch
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "mixer.pose.additional_layers",
                    additionalPoseLayers?.Length ?? 0,
                    "count"));

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
                    "mixer.expression.smoothing_active",
                    _expressionSmoothingActive
                        ? 1.0
                        : 0.0,
                    "bool"));
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

            if (poseLayerProviderBehaviour is
                    ITrackingFrameProvider poseLayer &&
                !ReferenceEquals(
                    poseLayer,
                    this))
            {
                _poseLayerProvider =
                    poseLayer;
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

        private void UpdatePoseOutput()
        {
            TrackingFrame baseFrame = null;
            TrackingFrame primaryLayerFrame = null;

            _routedProvider
                .TryGetLatestHumanoidPose(
                    out baseFrame);

            if (_poseLayerProvider != null &&
                !ReferenceEquals(
                    _poseLayerProvider,
                    _routedProvider))
            {
                _poseLayerProvider
                    .TryGetLatestHumanoidPose(
                        out primaryLayerFrame);
            }

            var baseChanged =
                FrameChanged(
                    baseFrame,
                    ref _lastBasePoseSequence,
                    ref _lastBasePoseSourceId);
            var primaryChanged =
                FrameChanged(
                    primaryLayerFrame,
                    ref _lastLayerPoseSequence,
                    ref _lastLayerPoseSourceId);

            EnsureAdditionalPoseRuntimeState();

            var additionalChanged = false;

            for (var i = 0;
                 i < _additionalPoseLayerSequences.Length;
                 i++)
            {
                TryGetAdditionalPoseLayerFrame(
                    i,
                    out var frame);

                if (FrameChanged(
                        frame,
                        ref _additionalPoseLayerSequences[i],
                        ref _additionalPoseLayerSourceIds[i]))
                {
                    additionalChanged = true;
                }
            }

            if (!baseChanged &&
                !primaryChanged &&
                !additionalChanged &&
                !_poseDirty)
            {
                return;
            }

            _poseDirty = false;
            _poseSpaceMismatch = false;

            var mixedPose =
                baseFrame?.HumanoidPose;
            var hasMixedLayer = false;
            var confidence =
                baseFrame?.Confidence ?? 0f;

            ApplyPoseLayer(
                primaryLayerFrame,
                poseLayerSettings,
                ref mixedPose,
                ref hasMixedLayer,
                ref confidence);

            if (additionalPoseLayers != null)
            {
                for (var i = 0;
                     i < additionalPoseLayers.Length;
                     i++)
                {
                    if (!TryGetAdditionalPoseLayerFrame(
                            i,
                            out var frame))
                    {
                        continue;
                    }

                    ApplyPoseLayer(
                        frame,
                        additionalPoseLayers[i]?.Settings,
                        ref mixedPose,
                        ref hasMixedLayer,
                        ref confidence);
                }
            }

            if (mixedPose == null)
            {
                _latestPoseFrame = null;
                return;
            }

            if (!hasMixedLayer &&
                ReferenceEquals(
                    mixedPose,
                    baseFrame?.HumanoidPose))
            {
                _latestPoseFrame =
                    baseFrame;
                return;
            }

            var nowUs =
                MonotonicClock
                    .NowMicroseconds();

            _latestPoseFrame =
                new TrackingFrame(
                    ++_poseSequence,
                    sourceTimestampUs:
                        nowUs,
                    validRegions:
                        TrackingRegion.FullBody,
                    confidence:
                        confidence,
                    subjectDetected:
                        baseFrame?.SubjectDetected ??
                        false,
                    humanoidPose:
                        mixedPose,
                    sourceId:
                        "motion-expression-mixer:pose",
                    runtimeTimestampUs:
                        nowUs);
        }

        private void ApplyPoseLayer(
            TrackingFrame layerFrame,
            HumanoidPoseLayerSettings settings,
            ref HumanoidPoseState mixedPose,
            ref bool hasMixedLayer,
            ref float confidence)
        {
            if (layerFrame?.HumanoidPose == null ||
                settings == null ||
                !settings.Enabled ||
                settings.Weight <= 0f)
            {
                return;
            }

            var next =
                HumanoidPoseMixerMath.Blend(
                    mixedPose,
                    layerFrame.HumanoidPose,
                    settings,
                    out var mismatch);

            if (mismatch)
            {
                _poseSpaceMismatch = true;
                return;
            }

            if (next == null ||
                ReferenceEquals(
                    next,
                    mixedPose))
            {
                return;
            }

            mixedPose = next;
            hasMixedLayer = true;
            confidence =
                Math.Max(
                    confidence,
                    layerFrame.Confidence);
        }

        private void EnsureAdditionalPoseRuntimeState()
        {
            var count =
                additionalPoseLayers?.Length ?? 0;

            if (_additionalPoseLayerSequences.Length ==
                    count &&
                _additionalPoseLayerSourceIds.Length ==
                    count)
            {
                return;
            }

            _additionalPoseLayerSequences =
                new long[count];
            _additionalPoseLayerSourceIds =
                new string[count];

            for (var i = 0;
                 i < count;
                 i++)
            {
                _additionalPoseLayerSequences[i] = -1;
            }
        }

        private bool TryGetAdditionalPoseLayerFrame(
            int index,
            out TrackingFrame frame)
        {
            frame = null;

            if (additionalPoseLayers == null ||
                index < 0 ||
                index >= additionalPoseLayers.Length)
            {
                return false;
            }

            var provider =
                additionalPoseLayers[index]?.Provider;

            if (provider == null ||
                ReferenceEquals(
                    provider,
                    this) ||
                ReferenceEquals(
                    provider,
                    _routedProvider))
            {
                return false;
            }

            return provider.TryGetLatestHumanoidPose(
                out frame) &&
                frame?.HumanoidPose != null;
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

            if (layerFrame?.Expressions == null ||
                expressionLayerWeight <= 0f)
            {
                _targetExpressions =
                    baseFrame?.Expressions;
                return;
            }

            _targetExpressions =
                ExpressionMixerMath.Blend(
                    baseFrame?.Expressions,
                    layerFrame.Expressions,
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
                _expressionSmoothingActive = false;
                return;
            }

            if (!_outputDirty &&
                !_expressionSmoothingActive)
            {
                return;
            }

            if (expressionSmoothing > 0f)
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

                _expressionSmoothingActive =
                    !ExpressionMixerMath
                        .ApproximatelyEqual(
                            _currentExpressions,
                            _targetExpressions);

                if (!_expressionSmoothingActive)
                {
                    _currentExpressions =
                        _targetExpressions;
                }
            }
            else
            {
                _currentExpressions =
                    _targetExpressions;
                _expressionSmoothingActive = false;
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

        private void ResetPoseState()
        {
            _lastBasePoseSequence = -1;
            _lastLayerPoseSequence = -1;
            _lastBasePoseSourceId = null;
            _lastLayerPoseSourceId = null;
            _latestPoseFrame = null;
            _poseDirty = true;
            _poseSpaceMismatch = false;
            _additionalPoseLayerSequences =
                Array.Empty<long>();
            _additionalPoseLayerSourceIds =
                Array.Empty<string>();
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
            _expressionSmoothingActive = false;
        }
    }
}
