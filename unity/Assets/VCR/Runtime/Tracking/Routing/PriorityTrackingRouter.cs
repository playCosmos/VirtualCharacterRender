using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Routing
{
    /// <summary>
    /// Region router for one performer.
    ///
    /// Preferred face provider (ARKit) wins only while it is stably present.
    /// Body/hands remain on the fallback provider (MediaPipe). When preferred
    /// face is active, an optional activation control suspends expensive
    /// fallback face inference.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(5000)]
    public sealed class PriorityTrackingRouter :
        MonoBehaviour,
        ITrackingRouteProvider,
        ITrackingRouteStatusProvider,
        IRuntimeMetricsSource
    {
        [Header("Providers")]
        [SerializeField] private MonoBehaviour preferredFaceProviderBehaviour;
        [SerializeField] private MonoBehaviour fallbackProviderBehaviour;
        [Tooltip("Optional VMC/full-body provider. Face priority remains ARKit > MediaPipe.")]
        [SerializeField] private MonoBehaviour externalPoseProviderBehaviour;
        [SerializeField] private bool disableFallbackFaceWhenPreferred = true;

        [Header("Presence - provisional P0 defaults")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        private ITrackingFrameProvider _preferredFaceProvider;
        private ITrackingPresenceProvider _preferredPresence;

        private ITrackingFrameProvider _fallbackProvider;
        private ITrackingPresenceProvider _fallbackPresence;
        private IFaceTrackingActivationControl _fallbackFaceActivation;

        private ITrackingFrameProvider _externalPoseProvider;
        private ITrackingPresenceProvider _externalPosePresence;

        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        private TrackingFrame _latestFace;
        private TrackingFrame _latestBodyHands;
        private TrackingFrame _latestHumanoidPose;
        private TrackingFrame _latestExpressions;

        private string _selectedFaceSourceId;
        private long _selectedFaceChildSequence = -1;
        private long _faceSequence;

        private string _selectedBodySourceId;
        private long _selectedBodyChildSequence = -1;
        private long _bodySequence;

        private string _selectedPoseSourceId;
        private long _selectedPoseChildSequence = -1;
        private long _poseSequence;

        private string _selectedExpressionSourceId;
        private long _selectedExpressionChildSequence = -1;
        private long _expressionSequence;

        private long _faceSourceSwitches;
        private long _bodySourceSwitches;
        private long _poseSourceSwitches;
        private long _expressionSourceSwitches;

        private TrackingRouteStatus _routeStatus;

        public TrackingPresenceSnapshot Presence => _presence;
        public TrackingRouteStatus RouteStatus => _routeStatus;

        private void Awake()
        {
            ResolveProviders();

            _presenceResolver = new TrackingPresenceResolver(
                SecondsToMicroseconds(subjectLostGraceSeconds),
                SecondsToMicroseconds(subjectRestoreStabilitySeconds),
                sourceStaleUs: 1_000_000);

            _presenceResolver.Reset(NowUs());
            _presence = _presenceResolver.Snapshot;
            UpdateRouteStatus(preferredFaceActive: false);
        }

        private void OnEnable()
        {
            ResolveProviders();
        }

        private void Update()
        {
            ResolveProviders();

            var preferredUsable = IsPreferredFaceUsable();
            UpdateFallbackFaceActivation(preferredUsable);
            UpdateFaceSnapshot(preferredUsable);
            UpdateBodyHandsSnapshot();
            UpdateExternalPoseSnapshots();
            UpdatePresence();
            UpdateRouteStatus(preferredUsable);
        }

        public bool TryGetLatestFace(out TrackingFrame frame)
        {
            frame = _latestFace;
            return frame != null;
        }

        public bool TryGetLatestBodyHands(out TrackingFrame frame)
        {
            frame = _latestBodyHands;
            return frame != null;
        }

        public bool TryGetLatestHumanoidPose(out TrackingFrame frame)
        {
            frame = _latestHumanoidPose;
            return frame != null;
        }

        public bool TryGetLatestExpressions(out TrackingFrame frame)
        {
            frame = _latestExpressions;
            return frame != null;
        }

        public void SetPreferredFaceProvider(MonoBehaviour provider)
        {
            preferredFaceProviderBehaviour = provider;
            _preferredFaceProvider = provider as ITrackingFrameProvider;
            _preferredPresence = provider as ITrackingPresenceProvider;
            ResetFaceSelection();
        }

        public void SetFallbackProvider(MonoBehaviour provider)
        {
            RestoreFallbackFace();

            fallbackProviderBehaviour = provider;
            _fallbackProvider = provider as ITrackingFrameProvider;
            _fallbackPresence = provider as ITrackingPresenceProvider;
            _fallbackFaceActivation =
                provider as IFaceTrackingActivationControl;

            ResetFaceSelection();
            ResetBodySelection();
        }

        public void SetExternalPoseProvider(MonoBehaviour provider)
        {
            externalPoseProviderBehaviour = provider;
            _externalPoseProvider = provider as ITrackingFrameProvider;
            _externalPosePresence = provider as ITrackingPresenceProvider;
            ResetPoseSelection();
        }

        private void ResolveProviders()
        {
            if (preferredFaceProviderBehaviour != null)
            {
                _preferredFaceProvider ??=
                    preferredFaceProviderBehaviour as ITrackingFrameProvider;
                _preferredPresence ??=
                    preferredFaceProviderBehaviour as ITrackingPresenceProvider;
            }

            if (fallbackProviderBehaviour != null)
            {
                _fallbackProvider ??=
                    fallbackProviderBehaviour as ITrackingFrameProvider;
                _fallbackPresence ??=
                    fallbackProviderBehaviour as ITrackingPresenceProvider;
                _fallbackFaceActivation ??=
                    fallbackProviderBehaviour as IFaceTrackingActivationControl;
            }

            if (externalPoseProviderBehaviour != null)
            {
                _externalPoseProvider ??=
                    externalPoseProviderBehaviour as ITrackingFrameProvider;
                _externalPosePresence ??=
                    externalPoseProviderBehaviour as ITrackingPresenceProvider;
            }
        }

        private bool IsPreferredFaceUsable()
        {
            if (_preferredFaceProvider == null)
            {
                return false;
            }

            if (_preferredPresence != null)
            {
                var presence = _preferredPresence.Presence;
                return
                    presence.FaceSourceAvailable &&
                    presence.FaceSubjectEvidence &&
                    _preferredFaceProvider.TryGetLatestFace(out var frame) &&
                    frame?.Face != null &&
                    frame.SubjectDetected;
            }

            return
                _preferredFaceProvider.TryGetLatestFace(out var fallbackFrame) &&
                fallbackFrame?.Face != null &&
                fallbackFrame.SubjectDetected;
        }

        private void UpdateFallbackFaceActivation(bool preferredUsable)
        {
            if (!disableFallbackFaceWhenPreferred ||
                _fallbackFaceActivation == null)
            {
                return;
            }

            var shouldEnableFallback = !preferredUsable;
            if (_fallbackFaceActivation.FaceTrackingEnabled !=
                shouldEnableFallback)
            {
                _fallbackFaceActivation.SetFaceTrackingEnabled(
                    shouldEnableFallback);
            }
        }

        private void UpdateFaceSnapshot(bool preferredUsable)
        {
            TrackingFrame selected = null;

            if (preferredUsable &&
                _preferredFaceProvider != null)
            {
                _preferredFaceProvider.TryGetLatestFace(out selected);
            }

            if ((selected == null || selected.Face == null) &&
                _fallbackProvider != null)
            {
                _fallbackProvider.TryGetLatestFace(out selected);
            }

            if (selected?.Face == null)
            {
                return;
            }

            if (selected.Sequence == _selectedFaceChildSequence &&
                string.Equals(
                    selected.SourceId,
                    _selectedFaceSourceId,
                    StringComparison.Ordinal))
            {
                return;
            }

            CountSourceSwitch(
                _selectedFaceSourceId,
                selected.SourceId,
                ref _faceSourceSwitches);

            _selectedFaceChildSequence = selected.Sequence;
            _selectedFaceSourceId = selected.SourceId;

            _latestFace = new TrackingFrame(
                ++_faceSequence,
                selected.SourceTimestampUs,
                selected.ValidRegions,
                selected.Confidence,
                selected.SubjectDetected,
                face: selected.Face,
                sourceId: selected.SourceId,
                runtimeTimestampUs: selected.RuntimeTimestampUs);
        }

        private void UpdateBodyHandsSnapshot()
        {
            if (_fallbackProvider == null ||
                !_fallbackProvider.TryGetLatestBodyHands(out var selected) ||
                selected == null)
            {
                return;
            }

            if (selected.Sequence == _selectedBodyChildSequence &&
                string.Equals(
                    selected.SourceId,
                    _selectedBodySourceId,
                    StringComparison.Ordinal))
            {
                return;
            }

            CountSourceSwitch(
                _selectedBodySourceId,
                selected.SourceId,
                ref _bodySourceSwitches);

            _selectedBodyChildSequence = selected.Sequence;
            _selectedBodySourceId = selected.SourceId;

            _latestBodyHands = new TrackingFrame(
                ++_bodySequence,
                selected.SourceTimestampUs,
                selected.ValidRegions,
                selected.Confidence,
                selected.SubjectDetected,
                upperBody: selected.UpperBody,
                leftHand: selected.LeftHand,
                rightHand: selected.RightHand,
                sourceId: selected.SourceId,
                runtimeTimestampUs: selected.RuntimeTimestampUs);
        }

        private void UpdateExternalPoseSnapshots()
        {
            if (_externalPoseProvider == null)
            {
                _latestHumanoidPose = null;
                _latestExpressions = null;
                return;
            }

            var presence = _externalPosePresence?.Presence;
            var usable =
                !presence.HasValue ||
                (presence.Value.FullBodySourceAvailable &&
                 presence.Value.FullBodySubjectEvidence);

            if (!usable)
            {
                _latestHumanoidPose = null;
                _latestExpressions = null;
                return;
            }

            if (_externalPoseProvider.TryGetLatestHumanoidPose(
                    out var poseFrame) &&
                poseFrame?.HumanoidPose != null &&
                (poseFrame.Sequence != _selectedPoseChildSequence ||
                 !string.Equals(
                     poseFrame.SourceId,
                     _selectedPoseSourceId,
                     StringComparison.Ordinal)))
            {
                CountSourceSwitch(
                    _selectedPoseSourceId,
                    poseFrame.SourceId,
                    ref _poseSourceSwitches);

                _selectedPoseChildSequence = poseFrame.Sequence;
                _selectedPoseSourceId = poseFrame.SourceId;

                _latestHumanoidPose = new TrackingFrame(
                    ++_poseSequence,
                    poseFrame.SourceTimestampUs,
                    poseFrame.ValidRegions,
                    poseFrame.Confidence,
                    poseFrame.SubjectDetected,
                    humanoidPose: poseFrame.HumanoidPose,
                    sourceId: poseFrame.SourceId,
                    runtimeTimestampUs: poseFrame.RuntimeTimestampUs);
            }

            if (_externalPoseProvider.TryGetLatestExpressions(
                    out var expressionFrame) &&
                expressionFrame?.Expressions != null &&
                (expressionFrame.Sequence !=
                    _selectedExpressionChildSequence ||
                 !string.Equals(
                     expressionFrame.SourceId,
                     _selectedExpressionSourceId,
                     StringComparison.Ordinal)))
            {
                CountSourceSwitch(
                    _selectedExpressionSourceId,
                    expressionFrame.SourceId,
                    ref _expressionSourceSwitches);

                _selectedExpressionChildSequence =
                    expressionFrame.Sequence;
                _selectedExpressionSourceId =
                    expressionFrame.SourceId;

                _latestExpressions = new TrackingFrame(
                    ++_expressionSequence,
                    expressionFrame.SourceTimestampUs,
                    expressionFrame.ValidRegions,
                    expressionFrame.Confidence,
                    expressionFrame.SubjectDetected,
                    expressions: expressionFrame.Expressions,
                    sourceId: expressionFrame.SourceId,
                    runtimeTimestampUs: expressionFrame.RuntimeTimestampUs);
            }
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var preferred = _preferredPresence?.Presence;
            var fallback = _fallbackPresence?.Presence;
            var external = _externalPosePresence?.Presence;

            var preferredFaceAvailable =
                preferred.HasValue &&
                preferred.Value.FaceSourceAvailable;

            var fallbackFaceAvailable =
                fallback.HasValue &&
                fallback.Value.FaceSourceAvailable;

            var faceAvailable =
                preferredFaceAvailable || fallbackFaceAvailable;

            var faceEvidence =
                (preferredFaceAvailable &&
                 preferred.Value.FaceSubjectEvidence) ||
                (fallbackFaceAvailable &&
                 fallback.Value.FaceSubjectEvidence);

            var bodyConfigured = _fallbackProvider != null;
            var bodyAvailable =
                fallback.HasValue &&
                fallback.Value.BodyHandsSourceAvailable;

            var bodyEvidence =
                bodyAvailable &&
                fallback.Value.BodyHandsSubjectEvidence;

            var faceConfigured =
                _preferredFaceProvider != null ||
                _fallbackProvider != null;

            var fullBodyConfigured =
                _externalPoseProvider != null;

            var fullBodyAvailable =
                external.HasValue &&
                external.Value.FullBodySourceAvailable;

            var fullBodyEvidence =
                fullBodyAvailable &&
                external.Value.FullBodySubjectEvidence;

            var decisionReady =
                faceAvailable ||
                bodyAvailable ||
                fullBodyAvailable ||
                (preferred.HasValue &&
                 preferred.Value.SubjectState != SubjectPresenceState.Unknown) ||
                (fallback.HasValue &&
                 fallback.Value.SubjectState != SubjectPresenceState.Unknown) ||
                _latestFace != null ||
                _latestBodyHands != null ||
                _latestHumanoidPose != null ||
                _latestExpressions != null;

            _presence = _presenceResolver.UpdateResolved(
                NowUs(),
                faceConfigured,
                faceAvailable,
                faceEvidence,
                bodyConfigured,
                bodyAvailable,
                bodyEvidence,
                decisionReady,
                fullBodyConfigured,
                fullBodyAvailable,
                fullBodyEvidence);
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            var status = _routeStatus;

            output.Add(
                new RuntimeMetric(
                    "tracking.route.preferred_face_active",
                    status.PreferredFaceActive ? 1.0 : 0.0,
                    "bool"));

            output.Add(
                new RuntimeMetric(
                    "tracking.route.fallback_face_inference_enabled",
                    status.FallbackFaceInferenceEnabled ? 1.0 : 0.0,
                    "bool"));

            AddAgeMetric(
                output,
                "tracking.route.face_age",
                status.FaceAgeMs);
            AddAgeMetric(
                output,
                "tracking.route.body_age",
                status.BodyHandsAgeMs);
            AddAgeMetric(
                output,
                "tracking.route.fullbody_age",
                status.FullBodyAgeMs);
            AddAgeMetric(
                output,
                "tracking.route.expression_age",
                status.ExpressionAgeMs);

            output.Add(
                new RuntimeMetric(
                    "tracking.route.face_switches",
                    _faceSourceSwitches,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "tracking.route.body_switches",
                    _bodySourceSwitches,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "tracking.route.fullbody_switches",
                    _poseSourceSwitches,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "tracking.route.expression_switches",
                    _expressionSourceSwitches,
                    "count"));
        }

        private void UpdateRouteStatus(
            bool preferredFaceActive)
        {
            var nowUs =
                MonotonicClock.NowMicroseconds();

            var fallbackFaceInferenceEnabled =
                _fallbackProvider != null &&
                (_fallbackFaceActivation == null ||
                 _fallbackFaceActivation.FaceTrackingEnabled);

            _routeStatus =
                new TrackingRouteStatus(
                    _latestFace?.SourceId,
                    _latestBodyHands?.SourceId,
                    _latestHumanoidPose?.SourceId,
                    _latestExpressions?.SourceId,
                    preferredFaceActive,
                    fallbackFaceInferenceEnabled,
                    FrameAgeMs(
                        _latestFace,
                        nowUs),
                    FrameAgeMs(
                        _latestBodyHands,
                        nowUs),
                    FrameAgeMs(
                        _latestHumanoidPose,
                        nowUs),
                    FrameAgeMs(
                        _latestExpressions,
                        nowUs));
        }

        private static double FrameAgeMs(
            TrackingFrame frame,
            long nowUs)
        {
            if (frame == null ||
                frame.RuntimeTimestampUs <= 0)
            {
                return double.NaN;
            }

            return Math.Max(
                0.0,
                (nowUs -
                 frame.RuntimeTimestampUs) /
                1000.0);
        }

        private static void AddAgeMetric(
            List<RuntimeMetric> output,
            string name,
            double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    name,
                    value,
                    "ms"));
        }

        private static void CountSourceSwitch(
            string previousSourceId,
            string nextSourceId,
            ref long counter)
        {
            if (!string.IsNullOrEmpty(
                    previousSourceId) &&
                !string.Equals(
                    previousSourceId,
                    nextSourceId,
                    StringComparison.Ordinal))
            {
                counter++;
            }
        }

        private void ResetFaceSelection()
        {
            _selectedFaceSourceId = null;
            _selectedFaceChildSequence = -1;
        }

        private void ResetBodySelection()
        {
            _selectedBodySourceId = null;
            _selectedBodyChildSequence = -1;
        }

        private void ResetPoseSelection()
        {
            _selectedPoseSourceId = null;
            _selectedPoseChildSequence = -1;
            _selectedExpressionSourceId = null;
            _selectedExpressionChildSequence = -1;
            _latestHumanoidPose = null;
            _latestExpressions = null;
        }

        private void RestoreFallbackFace()
        {
            if (_fallbackFaceActivation != null &&
                !_fallbackFaceActivation.FaceTrackingEnabled)
            {
                _fallbackFaceActivation.SetFaceTrackingEnabled(true);
            }
        }

        private void OnDisable()
        {
            RestoreFallbackFace();
        }

        private void OnDestroy()
        {
            RestoreFallbackFace();
        }

        private static long NowUs()
        {
            return (long)(
                Time.realtimeSinceStartupAsDouble * 1_000_000.0);
        }

        private static long SecondsToMicroseconds(float seconds)
        {
            return (long)(
                Math.Max(0f, seconds) * 1_000_000.0);
        }
    }
}
