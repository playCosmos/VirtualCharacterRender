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
    /// Upper body remains on the MediaPipe fallback provider while an optional
    /// preferred hand provider (Ultraleap) can override left/right hands
    /// independently. Full-body and expression sources route separately. When
    /// the selected face policy favors ARKit, redundant MediaPipe face inference
    /// can sleep.
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
        [Tooltip("Optional hands-only provider. When usable its left/right hand data overrides fallback-provider hands while the fallback upper body remains active.")]
        [SerializeField] private MonoBehaviour preferredHandsProviderBehaviour;
        [SerializeField] private MonoBehaviour fallbackProviderBehaviour;
        [Tooltip("Optional VMC/full-body provider. Face source priority is controlled by the routing policy.")]
        [SerializeField] private MonoBehaviour externalPoseProviderBehaviour;
        [Tooltip("Optional expression-only fallback such as AudioDrivenExpressionSource. It is never used as performer-presence evidence.")]
        [SerializeField] private MonoBehaviour expressionFallbackProviderBehaviour;

        [Header("Routing policy")]
        [SerializeField] private TrackingRoutePolicy routePolicy =
            TrackingRoutePolicy.CreateDefault();
        [SerializeField] private bool disableFallbackFaceWhenPreferred = true;
        [SerializeField] private bool disableExpressionFallbackWhenExternal = true;

        [Header("Presence - provisional P0 defaults")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        private ITrackingFrameProvider _preferredFaceProvider;
        private ITrackingPresenceProvider _preferredPresence;
        private ITrackingSourceHealthProvider _preferredFaceHealth;

        private ITrackingFrameProvider _preferredHandsProvider;
        private ITrackingPresenceProvider _preferredHandsPresence;
        private ITrackingSourceHealthProvider _preferredHandsHealth;

        private ITrackingFrameProvider _fallbackProvider;
        private ITrackingPresenceProvider _fallbackPresence;
        private ITrackingSourceHealthProvider _fallbackHealth;
        private IFaceTrackingActivationControl _fallbackFaceActivation;

        private ITrackingFrameProvider _externalPoseProvider;
        private ITrackingPresenceProvider _externalPosePresence;
        private ITrackingSourceHealthProvider _externalPoseHealth;

        private ITrackingFrameProvider _expressionFallbackProvider;
        private ITrackingSourceHealthProvider _expressionFallbackHealth;
        private IExpressionTrackingActivationControl
            _expressionFallbackActivation;

        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        private TrackingFrame _latestFace;
        private TrackingFrame _latestBodyHands;
        private TrackingFrame _latestHumanoidPose;
        private TrackingFrame _latestExpressions;

        private TrackingFrame _selectedFaceFrame;
        private string _selectedFaceSourceId;

        private TrackingFrame _selectedBodyFrame;
        private TrackingFrame _selectedHandsFrame;
        private string _selectedBodySourceId;
        private long _bodySequence;

        private TrackingFrame _selectedPoseFrame;
        private string _selectedPoseSourceId;

        private TrackingFrame _selectedExpressionFrame;
        private string _selectedExpressionSourceId;

        private long _faceSourceSwitches;
        private long _bodySourceSwitches;
        private long _poseSourceSwitches;
        private long _expressionSourceSwitches;
        private long _providerFailureCount;

        private TrackingRouteStatus _routeStatus;

        public TrackingPresenceSnapshot Presence => _presence;
        public TrackingRouteStatus RouteStatus => _routeStatus;

        private void Awake()
        {
            SanitizePresenceTimingConfiguration();
            EnsureRoutePolicy();
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

            var faceSelection =
                FaceSourceSelection.None;

            try
            {
                var preferredUsable =
                    TryGetUsableFaceCandidate(
                        _preferredFaceProvider,
                        _preferredPresence,
                        _preferredFaceHealth,
                        out var preferredFrame);
                var preferredOutranksFallback =
                    preferredUsable &&
                    PreferredFaceOutranksFallback();

                UpdateFallbackFaceActivation(
                    preferredUsable,
                    preferredOutranksFallback);

                TrackingFrame fallbackFrame = null;
                var fallbackUsable = false;

                if (!preferredUsable ||
                    !preferredOutranksFallback)
                {
                    fallbackUsable =
                        TryGetUsableFaceCandidate(
                            _fallbackProvider,
                            _fallbackPresence,
                            _fallbackHealth,
                            out fallbackFrame);
                }

                faceSelection =
                    SelectFaceSource(
                        preferredUsable,
                        fallbackUsable,
                        preferredOutranksFallback);

                UpdateFaceSnapshot(
                    faceSelection,
                    preferredFrame,
                    fallbackFrame);
            }
            catch
            {
                _providerFailureCount++;
                _latestFace = null;
                ResetFaceSelection();
            }

            try
            {
                UpdateBodyHandsSnapshot();
            }
            catch
            {
                _providerFailureCount++;
                _latestBodyHands = null;
                ResetBodySelection();
            }

            try
            {
                UpdateExternalPoseSnapshots();
            }
            catch
            {
                _providerFailureCount++;
                _latestHumanoidPose = null;
                _latestExpressions = null;
                ResetHumanoidPoseSelection();
                ResetExpressionSelection();
            }

            try
            {
                UpdatePresence();
            }
            catch
            {
                _providerFailureCount++;
            }

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
                policy;
            EnsureRoutePolicy();
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

        public void SetPreferredHandsProvider(MonoBehaviour provider)
        {
            preferredHandsProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            AssignPreferredHandsProvider(
                preferredHandsProviderBehaviour);
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
                (next == null ||
                 IsServiceAlive(
                     _preferredFaceProvider)))
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

        private void AssignPreferredHandsProvider(
            MonoBehaviour behaviour)
        {
            var next =
                behaviour != null
                    ? behaviour as ITrackingFrameProvider
                    : null;

            if (ReferenceEquals(
                    _preferredHandsProvider,
                    next) &&
                (next == null ||
                 IsServiceAlive(
                     _preferredHandsProvider)))
            {
                return;
            }

            _preferredHandsProvider =
                next;
            _preferredHandsPresence =
                behaviour != null
                    ? behaviour as ITrackingPresenceProvider
                    : null;
            _preferredHandsHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;

            ResetBodySelection();
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
                (next == null ||
                 IsServiceAlive(
                     _fallbackProvider)))
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
                (next == null ||
                 IsServiceAlive(
                     _externalPoseProvider)))
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
                (next == null ||
                 IsServiceAlive(
                     _expressionFallbackProvider)))
            {
                return;
            }

            RestoreExpressionFallback();

            _expressionFallbackProvider = next;
            _expressionFallbackHealth =
                behaviour != null
                    ? behaviour as ITrackingSourceHealthProvider
                    : null;
            _expressionFallbackActivation =
                behaviour != null
                    ? behaviour as IExpressionTrackingActivationControl
                    : null;
            ResetExpressionSelection();
        }

        private void ResolveProviders()
        {
            AssignPreferredFaceProvider(
                preferredFaceProviderBehaviour != null
                    ? preferredFaceProviderBehaviour
                    : null);
            AssignPreferredHandsProvider(
                preferredHandsProviderBehaviour != null
                    ? preferredHandsProviderBehaviour
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

        private static FaceSourceSelection SelectFaceSource(
            bool preferredUsable,
            bool fallbackUsable,
            bool preferredOutranksFallback)
        {
            if (preferredUsable &&
                fallbackUsable)
            {
                return
                    preferredOutranksFallback
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

        private static bool TryGetUsableFaceCandidate(
            ITrackingFrameProvider provider,
            ITrackingPresenceProvider presenceProvider,
            ITrackingSourceHealthProvider healthProvider,
            out TrackingFrame frame)
        {
            frame = null;

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
                    out frame) &&
                frame?.Face != null &&
                frame.SubjectDetected;
        }

        private bool PreferredFaceOutranksFallback()
        {
            var policy =
                EnsureRoutePolicy();

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

        private TrackingRoutePolicy EnsureRoutePolicy()
        {
            if (routePolicy == null)
            {
                routePolicy =
                    TrackingRoutePolicy.CreateDefault();
            }

            return routePolicy;
        }

        private void UpdateFallbackFaceActivation(
            bool preferredUsable,
            bool preferredOutranksFallback)
        {
            if (!disableFallbackFaceWhenPreferred ||
                !IsServiceAlive(_fallbackFaceActivation))
            {
                return;
            }

            var shouldEnableFallback =
                !preferredUsable ||
                !preferredOutranksFallback;

            try
            {
                if (_fallbackFaceActivation.FaceTrackingEnabled !=
                    shouldEnableFallback)
                {
                    _fallbackFaceActivation.SetFaceTrackingEnabled(
                        shouldEnableFallback);
                }
            }
            catch
            {
                _providerFailureCount++;
            }
        }

        private void UpdateFaceSnapshot(
            FaceSourceSelection selection,
            TrackingFrame preferredFrame,
            TrackingFrame fallbackFrame)
        {
            var selected =
                selection ==
                    FaceSourceSelection.Preferred
                    ? preferredFrame
                    : selection ==
                        FaceSourceSelection.Fallback
                        ? fallbackFrame
                        : null;

            if (selected?.Face == null)
            {
                _latestFace = null;
                ResetFaceSelection();
                return;
            }

            if (ReferenceEquals(
                    selected,
                    _selectedFaceFrame))
            {
                return;
            }

            CountSourceSwitch(
                _selectedFaceSourceId,
                selected.SourceId,
                ref _faceSourceSwitches);

            _selectedFaceFrame = selected;
            _selectedFaceSourceId = selected.SourceId;

            _latestFace = selected;
        }

        private void UpdateBodyHandsSnapshot()
        {
            TrackingFrame fallbackFrame = null;
            var fallbackUsable =
                IsServiceAlive(
                    _fallbackProvider) &&
                (IsSourceHealthUsable(
                     _fallbackHealth,
                     TrackingRegion.UpperBody) ||
                 IsSourceHealthUsable(
                     _fallbackHealth,
                     TrackingRegion.Hands)) &&
                _fallbackProvider.TryGetLatestBodyHands(
                    out fallbackFrame) &&
                fallbackFrame != null &&
                (fallbackFrame.UpperBody != null ||
                 fallbackFrame.LeftHand != null ||
                 fallbackFrame.RightHand != null);

            TrackingFrame preferredHandsFrame = null;
            var preferredHandsUsable =
                IsServiceAlive(
                    _preferredHandsProvider) &&
                IsSourceHealthUsable(
                    _preferredHandsHealth,
                    TrackingRegion.Hands) &&
                _preferredHandsProvider.TryGetLatestBodyHands(
                    out preferredHandsFrame) &&
                preferredHandsFrame != null &&
                preferredHandsFrame.SubjectDetected &&
                (preferredHandsFrame.LeftHand != null ||
                 preferredHandsFrame.RightHand != null);

            if (!fallbackUsable &&
                !preferredHandsUsable)
            {
                _latestBodyHands = null;
                ResetBodySelection();
                return;
            }

            if (ReferenceEquals(
                    fallbackFrame,
                    _selectedBodyFrame) &&
                ReferenceEquals(
                    preferredHandsFrame,
                    _selectedHandsFrame))
            {
                return;
            }

            var upperBody =
                fallbackUsable
                    ? fallbackFrame.UpperBody
                    : null;

            var leftHand =
                preferredHandsUsable &&
                preferredHandsFrame.LeftHand != null
                    ? preferredHandsFrame.LeftHand
                    : fallbackUsable
                        ? fallbackFrame.LeftHand
                        : null;

            var rightHand =
                preferredHandsUsable &&
                preferredHandsFrame.RightHand != null
                    ? preferredHandsFrame.RightHand
                    : fallbackUsable
                        ? fallbackFrame.RightHand
                        : null;

            var regions =
                TrackingRegion.None;

            if (upperBody != null)
            {
                regions |=
                    TrackingRegion.UpperBody;
            }

            if (leftHand != null)
            {
                regions |=
                    TrackingRegion.LeftHand;
            }

            if (rightHand != null)
            {
                regions |=
                    TrackingRegion.RightHand;
            }

            if (regions ==
                TrackingRegion.None)
            {
                _latestBodyHands = null;
                ResetBodySelection();
                return;
            }

            var preferredHandsSelected =
                preferredHandsUsable &&
                (ReferenceEquals(
                     leftHand,
                     preferredHandsFrame.LeftHand) ||
                 ReferenceEquals(
                     rightHand,
                     preferredHandsFrame.RightHand));

            var nextSourceId =
                BuildBodyHandsSourceId(
                    fallbackUsable
                        ? fallbackFrame
                        : null,
                    preferredHandsSelected
                        ? preferredHandsFrame
                        : null);

            CountSourceSwitch(
                _selectedBodySourceId,
                nextSourceId,
                ref _bodySourceSwitches);

            _selectedBodyFrame =
                fallbackFrame;
            _selectedHandsFrame =
                preferredHandsFrame;
            _selectedBodySourceId =
                nextSourceId;

            var runtimeTimestampUs =
                Math.Max(
                    fallbackUsable
                        ? fallbackFrame.RuntimeTimestampUs
                        : 0L,
                    preferredHandsSelected
                        ? preferredHandsFrame.RuntimeTimestampUs
                        : 0L);

            var sourceTimestampUs =
                Math.Max(
                    fallbackUsable
                        ? fallbackFrame.SourceTimestampUs
                        : 0L,
                    preferredHandsSelected
                        ? preferredHandsFrame.SourceTimestampUs
                        : 0L);

            _latestBodyHands =
                new TrackingFrame(
                    ++_bodySequence,
                    sourceTimestampUs,
                    regions,
                    MaxFiniteConfidence(
                        fallbackUsable
                            ? fallbackFrame.Confidence
                            : float.NaN,
                        preferredHandsSelected
                            ? preferredHandsFrame.Confidence
                            : float.NaN),
                    subjectDetected:
                        (fallbackUsable &&
                         fallbackFrame.SubjectDetected) ||
                        (preferredHandsSelected &&
                         preferredHandsFrame.SubjectDetected),
                    upperBody:
                        upperBody,
                    leftHand:
                        leftHand,
                    rightHand:
                        rightHand,
                    sourceId:
                        nextSourceId,
                    runtimeTimestampUs:
                        runtimeTimestampUs);
        }

        private static string BuildBodyHandsSourceId(
            TrackingFrame fallbackFrame,
            TrackingFrame preferredHandsFrame)
        {
            if (fallbackFrame == null)
            {
                return
                    preferredHandsFrame?.SourceId;
            }

            if (preferredHandsFrame == null)
            {
                return
                    fallbackFrame.SourceId;
            }

            return
                "body:" +
                (fallbackFrame.SourceId ??
                 "fallback") +
                "|hands:" +
                (preferredHandsFrame.SourceId ??
                 "preferred");
        }

        private static float MaxFiniteConfidence(
            float first,
            float second)
        {
            var firstValid =
                float.IsFinite(first);
            var secondValid =
                float.IsFinite(second);

            if (firstValid &&
                secondValid)
            {
                return Math.Max(
                    first,
                    second);
            }

            if (firstValid)
            {
                return first;
            }

            return
                secondValid
                    ? second
                    : float.NaN;
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
                ResetHumanoidPoseSelection();
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
                ResetHumanoidPoseSelection();
                return;
            }

            if (!_externalPoseProvider.TryGetLatestHumanoidPose(
                    out var poseFrame) ||
                poseFrame?.HumanoidPose == null)
            {
                ResetHumanoidPoseSelection();
                return;
            }

            if (ReferenceEquals(
                    poseFrame,
                    _selectedPoseFrame))
            {
                return;
            }

            CountSourceSwitch(
                _selectedPoseSourceId,
                poseFrame.SourceId,
                ref _poseSourceSwitches);

            _selectedPoseFrame =
                poseFrame;
            _selectedPoseSourceId =
                poseFrame.SourceId;

            _latestHumanoidPose =
                poseFrame;
        }

        private void UpdateExpressionSnapshot()
        {
            var externalUsable =
                TryGetUsableExpression(
                    _externalPoseProvider,
                    _externalPoseHealth,
                    out var externalFrame);
            var externalOutranksFallback =
                externalUsable &&
                ExternalExpressionOutranksFallback();

            UpdateExpressionFallbackActivation(
                externalUsable,
                externalOutranksFallback);

            TrackingFrame fallbackFrame = null;
            var fallbackUsable = false;

            if (!externalUsable ||
                !externalOutranksFallback)
            {
                fallbackUsable =
                    TryGetUsableExpression(
                        _expressionFallbackProvider,
                        _expressionFallbackHealth,
                        out fallbackFrame);
            }

            TrackingFrame selected = null;

            if (externalUsable &&
                fallbackUsable)
            {
                selected =
                    externalOutranksFallback
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

            if (ReferenceEquals(
                    selected,
                    _selectedExpressionFrame))
            {
                return;
            }

            CountSourceSwitch(
                _selectedExpressionSourceId,
                selected.SourceId,
                ref _expressionSourceSwitches);

            _selectedExpressionFrame =
                selected;
            _selectedExpressionSourceId =
                selected.SourceId;

            _latestExpressions =
                selected;
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

        private void UpdateExpressionFallbackActivation(
            bool externalUsable,
            bool externalOutranksFallback)
        {
            if (!disableExpressionFallbackWhenExternal ||
                !IsServiceAlive(
                    _expressionFallbackActivation))
            {
                return;
            }

            var shouldEnableFallback =
                !externalUsable ||
                !externalOutranksFallback;

            try
            {
                if (_expressionFallbackActivation
                        .ExpressionTrackingEnabled !=
                    shouldEnableFallback)
                {
                    _expressionFallbackActivation
                        .SetExpressionTrackingEnabled(
                            shouldEnableFallback);
                }
            }
            catch
            {
                _providerFailureCount++;
            }
        }

        private bool ExternalExpressionOutranksFallback()
        {
            var policy =
                EnsureRoutePolicy();

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
            TrackingPresenceSnapshot? preferredHands =
                IsServiceAlive(_preferredHandsPresence)
                    ? _preferredHandsPresence.Presence
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
                IsServiceAlive(_fallbackProvider) ||
                IsServiceAlive(_preferredHandsProvider);

            var fallbackBodyAvailable =
                fallback.HasValue &&
                fallback.Value.BodyHandsSourceAvailable;
            var preferredHandsAvailable =
                preferredHands.HasValue &&
                preferredHands.Value.BodyHandsSourceAvailable;

            var bodyAvailable =
                fallbackBodyAvailable ||
                preferredHandsAvailable;

            var bodyEvidence =
                (fallbackBodyAvailable &&
                 fallback.Value.BodyHandsSubjectEvidence) ||
                (preferredHandsAvailable &&
                 preferredHands.Value.BodyHandsSubjectEvidence);

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
                (preferredHands.HasValue &&
                 preferredHands.Value.SubjectState != SubjectPresenceState.Unknown) ||
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

            output.Add(
                new RuntimeMetric(
                    "tracking.route.fallback_expression_inference_enabled",
                    IsExpressionFallbackInferenceEnabled()
                        ? 1.0
                        : 0.0,
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

            output.Add(
                new RuntimeMetric(
                    "tracking.route.provider_failures",
                    _providerFailureCount,
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
                "tracking.route.preferred_hands_health",
                _preferredHandsHealth,
                TrackingRegion.Hands);
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
                IsFallbackFaceInferenceEnabled();

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

            return TrackingTimestampMath
                .AgeMillisecondsOrNaN(
                    nowUs,
                    frame.RuntimeTimestampUs);
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

        private void AddHealthMetric(
            List<RuntimeMetric> output,
            string name,
            ITrackingSourceHealthProvider provider,
            TrackingRegion region)
        {
            if (!IsServiceAlive(provider))
            {
                return;
            }

            try
            {
                if (!provider.TryGetSourceHealth(
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
            catch
            {
                _providerFailureCount++;
            }
        }

        private bool IsFallbackFaceInferenceEnabled()
        {
            if (!IsServiceAlive(_fallbackProvider))
            {
                return false;
            }

            if (!IsServiceAlive(_fallbackFaceActivation))
            {
                return true;
            }

            try
            {
                return _fallbackFaceActivation
                    .FaceTrackingEnabled;
            }
            catch
            {
                _providerFailureCount++;
                return false;
            }
        }

        private bool IsExpressionFallbackInferenceEnabled()
        {
            if (!IsServiceAlive(
                    _expressionFallbackProvider))
            {
                return false;
            }

            if (!IsServiceAlive(
                    _expressionFallbackActivation))
            {
                return true;
            }

            try
            {
                return _expressionFallbackActivation
                    .ExpressionTrackingEnabled;
            }
            catch
            {
                _providerFailureCount++;
                return false;
            }
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
            _selectedFaceFrame = null;
            _selectedFaceSourceId = null;
        }

        private void ResetBodySelection()
        {
            _selectedBodyFrame = null;
            _selectedHandsFrame = null;
            _selectedBodySourceId = null;
        }

        private void ResetPoseSelection()
        {
            ResetHumanoidPoseSelection();
            ResetExpressionSelection();
        }

        private void ResetHumanoidPoseSelection()
        {
            _selectedPoseFrame = null;
            _selectedPoseSourceId = null;
            _latestHumanoidPose = null;
        }

        private void ResetExpressionSelection()
        {
            _selectedExpressionFrame = null;
            _selectedExpressionSourceId = null;
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

        private void RestoreExpressionFallback()
        {
            if (IsServiceAlive(
                    _expressionFallbackActivation) &&
                !_expressionFallbackActivation
                    .ExpressionTrackingEnabled)
            {
                _expressionFallbackActivation
                    .SetExpressionTrackingEnabled(
                        true);
            }
        }

        private void OnDisable()
        {
            RestoreFallbackFace();
            RestoreExpressionFallback();
        }

        private void OnDestroy()
        {
            RestoreFallbackFace();
            RestoreExpressionFallback();
        }

        private static long NowUs()
        {
            return (long)(
                Time.realtimeSinceStartupAsDouble * 1_000_000.0);
        }

        private void SanitizePresenceTimingConfiguration()
        {
            subjectLostGraceSeconds =
                SanitizeNonNegativeSeconds(
                    subjectLostGraceSeconds,
                    fallback:
                        0.5f);
            subjectRestoreStabilitySeconds =
                SanitizeNonNegativeSeconds(
                    subjectRestoreStabilitySeconds,
                    fallback:
                        0.15f);
        }

        private static float SanitizeNonNegativeSeconds(
            float value,
            float fallback)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return fallback;
            }

            return Math.Max(
                0f,
                value);
        }

        private static long SecondsToMicroseconds(
            float seconds)
        {
            if (float.IsNaN(seconds) ||
                float.IsInfinity(seconds) ||
                seconds <= 0f)
            {
                return 0L;
            }

            var microseconds =
                (double)seconds *
                1_000_000.0;

            if (microseconds >=
                long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)microseconds;
        }
    }
}
