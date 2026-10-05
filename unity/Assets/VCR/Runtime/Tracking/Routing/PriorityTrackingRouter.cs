using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Routing
{
    /// <summary>
    /// Region router for one performer.
    ///
    /// Face ownership is selected from explicit source-kind priority policy.
    /// Body/hands remain on the MediaPipe fallback provider, while optional
    /// full-body and expression sources route independently. When the selected
    /// face policy favors ARKit, redundant MediaPipe face inference can sleep.
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
        [Tooltip("Optional VMC/full-body provider. Face source priority is controlled by the routing policy.")]
        [SerializeField] private MonoBehaviour externalPoseProviderBehaviour;
        [Tooltip("Optional expression-only fallback such as AudioDrivenExpressionSource. It is never used as performer-presence evidence.")]
        [SerializeField] private MonoBehaviour expressionFallbackProviderBehaviour;

        [Header("Routing policy")]
        [SerializeField] private TrackingRoutePolicy routePolicy =
            TrackingRoutePolicy.CreateDefault();
        [SerializeField] private bool disableFallbackFaceWhenPreferred = true;

        [Header("Presence - provisional P0 defaults")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        private ITrackingFrameProvider _preferredFaceProvider;
        private ITrackingPresenceProvider _preferredPresence;
        private ITrackingSourceHealthProvider _preferredFaceHealth;

        private ITrackingFrameProvider _fallbackProvider;
        private ITrackingPresenceProvider _fallbackPresence;
        private ITrackingSourceHealthProvider _fallbackHealth;
        private IFaceTrackingActivationControl _fallbackFaceActivation;

        private ITrackingFrameProvider _externalPoseProvider;
        private ITrackingPresenceProvider _externalPosePresence;
        private ITrackingSourceHealthProvider _externalPoseHealth;

        private ITrackingFrameProvider _expressionFallbackProvider;
        private ITrackingSourceHealthProvider _expressionFallbackHealth;

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
            routePolicy ??=
                TrackingRoutePolicy.CreateDefault();
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

            UpdateFallbackFaceActivation();
            var faceSelection =
                SelectFaceSource();
            UpdateFaceSnapshot(faceSelection);
            UpdateBodyHandsSnapshot();
            UpdateExternalPoseSnapshots();
            UpdatePresence();
            UpdateRouteStatus(
                faceSelection ==
                FaceSourceSelection.Preferred);
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

        public void SetRoutePolicy(
            TrackingRoutePolicy policy)
        {
            routePolicy =
                policy ??
                TrackingRoutePolicy.CreateDefault();
            ResetFaceSelection();
        }

        public void SetPreferredFaceProvider(MonoBehaviour provider)
        {
            preferredFaceProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            AssignPreferredFaceProvider(
                preferredFaceProviderBehaviour);
        }

        public void SetFallbackProvider(MonoBehaviour provider)
        {
            fallbackProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            AssignFallbackProvider(
                fallbackProviderBehaviour);
        }

        public void SetExternalPoseProvider(MonoBehaviour provider)
        {
            externalPoseProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            AssignExternalPoseProvider(
                externalPoseProviderBehaviour);
        }

        public void SetExpressionFallbackProvider(
            MonoBehaviour provider)
        {
            expressionFallbackProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            AssignExpressionFallbackProvider(
                expressionFallbackProviderBehaviour);
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private void AssignPreferredFaceProvider(
            MonoBehaviour behaviour)
        {
            var next =
                behaviour != null
                    ? behaviour as ITrackingFrameProvider
                    : null;

            if (ReferenceEquals(
                    _preferredFaceProvider,
                    next) &&
                IsServiceAlive(
                    _preferredFaceProvider))
            {
                return;
            }

            _preferredFaceProvider = next;
            _preferredPresence =
                behaviour != null
                    ? behaviour as ITrackingPresenceProvider
                    : null;
            _preferredFaceHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;
            ResetFaceSelection();
        }

        private void AssignFallbackProvider(
            MonoBehaviour behaviour)
        {
            var next =
                behaviour != null
                    ? behaviour as ITrackingFrameProvider
                    : null;

            if (ReferenceEquals(
                    _fallbackProvider,
                    next) &&
                IsServiceAlive(
                    _fallbackProvider))
            {
                return;
            }

            RestoreFallbackFace();

            _fallbackProvider = next;
            _fallbackPresence =
                behaviour != null
                    ? behaviour as ITrackingPresenceProvider
                    : null;
            _fallbackHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;
            _fallbackFaceActivation =
                behaviour != null
                    ? behaviour as IFaceTrackingActivationControl
                    : null;

            ResetFaceSelection();
            ResetBodySelection();
        }

        private void AssignExternalPoseProvider(
            MonoBehaviour behaviour)
        {
            var next =
                behaviour != null
                    ? behaviour as ITrackingFrameProvider
                    : null;

            if (ReferenceEquals(
                    _externalPoseProvider,
                    next) &&
                IsServiceAlive(
                    _externalPoseProvider))
            {
                return;
            }

            _externalPoseProvider = next;
            _externalPosePresence =
                behaviour != null
                    ? behaviour as ITrackingPresenceProvider
                    : null;
            _externalPoseHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;
            ResetPoseSelection();
        }

        private void AssignExpressionFallbackProvider(
            MonoBehaviour behaviour)
        {
            var next =
                behaviour != null
                    ? behaviour as ITrackingFrameProvider
                    : null;

            if (ReferenceEquals(
                    _expressionFallbackProvider,
                    next) &&
                IsServiceAlive(
                    _expressionFallbackProvider))
            {
                return;
            }

            _expressionFallbackProvider = next;
            _expressionFallbackHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;
            ResetExpressionSelection();
        }

        private void ResolveProviders()
        {
            AssignPreferredFaceProvider(
                preferredFaceProviderBehaviour != null
                    ? preferredFaceProviderBehaviour
                    : null);
            AssignFallbackProvider(
                fallbackProviderBehaviour != null
                    ? fallbackProviderBehaviour
                    : null);
            AssignExternalPoseProvider(
                externalPoseProviderBehaviour != null
                    ? externalPoseProviderBehaviour
                    : null);
            AssignExpressionFallbackProvider(
                expressionFallbackProviderBehaviour != null
                    ? expressionFallbackProviderBehaviour
                    : null);
        }

        private FaceSourceSelection SelectFaceSource()
        {
            var preferredUsable =
                IsFaceCandidateUsable(
                    _preferredFaceProvider,
                    _preferredPresence,
                    _preferredFaceHealth);

            var fallbackUsable =
                IsFaceCandidateUsable(
                    _fallbackProvider,
                    _fallbackPresence,
                    _fallbackHealth);

            if (preferredUsable &&
                fallbackUsable)
            {
                return
                    PreferredFaceOutranksFallback()
                        ? FaceSourceSelection.Preferred
                        : FaceSourceSelection.Fallback;
            }

            if (preferredUsable)
            {
                return FaceSourceSelection.Preferred;
            }

            return
                fallbackUsable
                    ? FaceSourceSelection.Fallback
                    : FaceSourceSelection.None;
        }

        private bool IsFaceCandidateUsable(
            ITrackingFrameProvider provider,
            ITrackingPresenceProvider presenceProvider,
            ITrackingSourceHealthProvider healthProvider)
        {
            if (!IsServiceAlive(provider) ||
                !IsSourceHealthUsable(
                    healthProvider,
                    TrackingRegion.Face))
            {
                return false;
            }

            if (IsServiceAlive(presenceProvider))
            {
                var presence =
                    presenceProvider.Presence;

                if (!presence.FaceSourceAvailable ||
                    !presence.FaceSubjectEvidence)
                {
                    return false;
                }
            }

            return
                provider.TryGetLatestFace(
                    out var frame) &&
                frame?.Face != null &&
                frame.SubjectDetected;
        }

        private bool PreferredFaceOutranksFallback()
        {
            var policy =
                routePolicy ??
                TrackingRoutePolicy.CreateDefault();

            var preferredKind =
                GetSourceKind(
                    _preferredFaceHealth,
                    TrackingRegion.Face);
            var fallbackKind =
                GetSourceKind(
                    _fallbackHealth,
                    TrackingRegion.Face);

            var preferredPriority =
                policy.GetFacePriority(
                    preferredKind);
            var fallbackPriority =
                policy.GetFacePriority(
                    fallbackKind);

            // Missing/legacy health metadata preserves the historical
            // preferred-then-fallback behavior.
            if (preferredPriority == int.MaxValue &&
                fallbackPriority == int.MaxValue)
            {
                return true;
            }

            return
                preferredPriority <=
                fallbackPriority;
        }

        private void UpdateFallbackFaceActivation()
        {
            if (!disableFallbackFaceWhenPreferred ||
                !IsServiceAlive(_fallbackFaceActivation))
            {
                return;
            }

            var preferredUsable =
                IsFaceCandidateUsable(
                    _preferredFaceProvider,
                    _preferredPresence,
                    _preferredFaceHealth);

            var shouldEnableFallback =
                !preferredUsable ||
                !PreferredFaceOutranksFallback();

            if (_fallbackFaceActivation.FaceTrackingEnabled !=
                shouldEnableFallback)
            {
                _fallbackFaceActivation.SetFaceTrackingEnabled(
                    shouldEnableFallback);
            }
        }

        private void UpdateFaceSnapshot(
            FaceSourceSelection selection)
        {
            ITrackingFrameProvider selectedProvider =
                selection ==
                    FaceSourceSelection.Preferred
                    ? _preferredFaceProvider
                    : selection ==
                        FaceSourceSelection.Fallback
                        ? _fallbackProvider
                        : null;

            if (!IsServiceAlive(selectedProvider) ||
                !selectedProvider.TryGetLatestFace(
                    out var selected) ||
                selected?.Face == null)
            {
                _latestFace = null;
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
            if (!IsServiceAlive(_fallbackProvider) ||
                !IsSourceHealthUsable(
                    _fallbackHealth,
                    TrackingRegion.UpperBody) ||
                !_fallbackProvider.TryGetLatestBodyHands(out var selected) ||
                selected == null)
            {
                _latestBodyHands = null;
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
            UpdateExternalPoseSnapshot();
            UpdateExpressionSnapshot();
        }

        private void UpdateExternalPoseSnapshot()
        {
            if (!IsServiceAlive(_externalPoseProvider))
            {
                _latestHumanoidPose = null;
                return;
            }

            TrackingPresenceSnapshot? presence =
                IsServiceAlive(_externalPosePresence)
                    ? _externalPosePresence.Presence
                    : null;
            var usable =
                IsSourceHealthUsable(
                    _externalPoseHealth,
                    TrackingRegion.FullBody) &&
                (!presence.HasValue ||
                 (presence.Value.FullBodySourceAvailable &&
                  presence.Value.FullBodySubjectEvidence));

            if (!usable)
            {
                _latestHumanoidPose = null;
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

                _selectedPoseChildSequence =
                    poseFrame.Sequence;
                _selectedPoseSourceId =
                    poseFrame.SourceId;

                _latestHumanoidPose =
                    new TrackingFrame(
                        ++_poseSequence,
                        poseFrame.SourceTimestampUs,
                        poseFrame.ValidRegions,
                        poseFrame.Confidence,
                        poseFrame.SubjectDetected,
                        humanoidPose:
                            poseFrame.HumanoidPose,
                        sourceId:
                            poseFrame.SourceId,
                        runtimeTimestampUs:
                            poseFrame.RuntimeTimestampUs);
            }
        }

        private void UpdateExpressionSnapshot()
        {
            var externalUsable =
                TryGetUsableExpression(
                    _externalPoseProvider,
                    _externalPoseHealth,
                    out var externalFrame);

            var fallbackUsable =
                TryGetUsableExpression(
                    _expressionFallbackProvider,
                    _expressionFallbackHealth,
                    out var fallbackFrame);

            TrackingFrame selected = null;

            if (externalUsable &&
                fallbackUsable)
            {
                selected =
                    ExternalExpressionOutranksFallback()
                        ? externalFrame
                        : fallbackFrame;
            }
            else if (externalUsable)
            {
                selected = externalFrame;
            }
            else if (fallbackUsable)
            {
                selected = fallbackFrame;
            }

            if (selected?.Expressions == null)
            {
                ResetExpressionSelection();
                return;
            }

            if (selected.Sequence ==
                    _selectedExpressionChildSequence &&
                string.Equals(
                    selected.SourceId,
                    _selectedExpressionSourceId,
                    StringComparison.Ordinal))
            {
                return;
            }

            CountSourceSwitch(
                _selectedExpressionSourceId,
                selected.SourceId,
                ref _expressionSourceSwitches);

            _selectedExpressionChildSequence =
                selected.Sequence;
            _selectedExpressionSourceId =
                selected.SourceId;

            _latestExpressions =
                new TrackingFrame(
                    ++_expressionSequence,
                    selected.SourceTimestampUs,
                    selected.ValidRegions |
                        TrackingRegion.Expressions,
                    selected.Confidence,
                    selected.SubjectDetected,
                    expressions:
                        selected.Expressions,
                    sourceId:
                        selected.SourceId,
                    runtimeTimestampUs:
                        selected.RuntimeTimestampUs);
        }

        private static bool TryGetUsableExpression(
            ITrackingFrameProvider provider,
            ITrackingSourceHealthProvider healthProvider,
            out TrackingFrame frame)
        {
            frame = null;

            if (!IsServiceAlive(provider) ||
                !IsSourceHealthUsable(
                    healthProvider,
                    TrackingRegion.Expressions))
            {
                return false;
            }

            return
                provider.TryGetLatestExpressions(
                    out frame) &&
                frame?.Expressions != null;
        }

        private bool ExternalExpressionOutranksFallback()
        {
            var policy =
                routePolicy ??
                TrackingRoutePolicy.CreateDefault();

            var externalKind =
                GetSourceKind(
                    _externalPoseHealth,
                    TrackingRegion.Expressions);
            var fallbackKind =
                GetSourceKind(
                    _expressionFallbackHealth,
                    TrackingRegion.Expressions);

            var externalPriority =
                policy.GetExpressionPriority(
                    externalKind);
            var fallbackPriority =
                policy.GetExpressionPriority(
                    fallbackKind);

            if (externalPriority == int.MaxValue &&
                fallbackPriority == int.MaxValue)
            {
                return true;
            }

            return
                externalPriority <=
                fallbackPriority;
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            TrackingPresenceSnapshot? preferred =
                IsServiceAlive(_preferredPresence)
                    ? _preferredPresence.Presence
                    : null;
            TrackingPresenceSnapshot? fallback =
                IsServiceAlive(_fallbackPresence)
                    ? _fallbackPresence.Presence
                    : null;
            TrackingPresenceSnapshot? external =
                IsServiceAlive(_externalPosePresence)
                    ? _externalPosePresence.Presence
                    : null;

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

            var bodyConfigured =
                IsServiceAlive(_fallbackProvider);
            var bodyAvailable =
                fallback.HasValue &&
                fallback.Value.BodyHandsSourceAvailable;

            var bodyEvidence =
                bodyAvailable &&
                fallback.Value.BodyHandsSubjectEvidence;

            var faceConfigured =
                IsServiceAlive(_preferredFaceProvider) ||
                IsServiceAlive(_fallbackProvider);

            var fullBodyConfigured =
                IsServiceAlive(_externalPoseProvider);

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

            AddHealthMetric(
                output,
                "tracking.route.preferred_face_health",
                _preferredFaceHealth,
                TrackingRegion.Face);
            AddHealthMetric(
                output,
                "tracking.route.fallback_face_health",
                _fallbackHealth,
                TrackingRegion.Face);
            AddHealthMetric(
                output,
                "tracking.route.body_health",
                _fallbackHealth,
                TrackingRegion.UpperBody);
            AddHealthMetric(
                output,
                "tracking.route.fullbody_health",
                _externalPoseHealth,
                TrackingRegion.FullBody);
            AddHealthMetric(
                output,
                "tracking.route.external_expression_health",
                _externalPoseHealth,
                TrackingRegion.Expressions);
            AddHealthMetric(
                output,
                "tracking.route.fallback_expression_health",
                _expressionFallbackHealth,
                TrackingRegion.Expressions);
        }

        private void UpdateRouteStatus(
            bool preferredFaceActive)
        {
            var nowUs =
                MonotonicClock.NowMicroseconds();

            var fallbackFaceInferenceEnabled =
                IsServiceAlive(_fallbackProvider) &&
                (!IsServiceAlive(_fallbackFaceActivation) ||
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

        private static TrackingSourceKind GetSourceKind(
            ITrackingSourceHealthProvider provider,
            TrackingRegion region)
        {
            if (IsServiceAlive(provider) &&
                provider.TryGetSourceHealth(
                    region,
                    out var snapshot))
            {
                return snapshot.Kind;
            }

            return TrackingSourceKind.Unknown;
        }

        private static bool IsSourceHealthUsable(
            ITrackingSourceHealthProvider provider,
            TrackingRegion region)
        {
            if (!IsServiceAlive(provider))
            {
                return true;
            }

            return
                provider.TryGetSourceHealth(
                    region,
                    out var snapshot) &&
                snapshot.IsUsable;
        }

        private static void AddHealthMetric(
            List<RuntimeMetric> output,
            string name,
            ITrackingSourceHealthProvider provider,
            TrackingRegion region)
        {
            if (!IsServiceAlive(provider) ||
                !provider.TryGetSourceHealth(
                    region,
                    out var snapshot))
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    name,
                    (int)snapshot.Health.State,
                    "enum"));
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

        private enum FaceSourceSelection
        {
            None = 0,
            Preferred = 1,
            Fallback = 2
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
            _latestHumanoidPose = null;
            ResetExpressionSelection();
        }

        private void ResetExpressionSelection()
        {
            _selectedExpressionSourceId = null;
            _selectedExpressionChildSequence = -1;
            _latestExpressions = null;
        }

        private void RestoreFallbackFace()
        {
            if (IsServiceAlive(_fallbackFaceActivation) &&
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
