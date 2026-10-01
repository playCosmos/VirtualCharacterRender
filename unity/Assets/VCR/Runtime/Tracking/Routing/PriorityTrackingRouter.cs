using System;
using UnityEngine;

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
        ITrackingRouteProvider
    {
        [Header("Providers")]
        [SerializeField] private MonoBehaviour preferredFaceProviderBehaviour;
        [SerializeField] private MonoBehaviour fallbackProviderBehaviour;
        [SerializeField] private bool disableFallbackFaceWhenPreferred = true;

        [Header("Presence - provisional P0 defaults")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        private ITrackingFrameProvider _preferredFaceProvider;
        private ITrackingPresenceProvider _preferredPresence;

        private ITrackingFrameProvider _fallbackProvider;
        private ITrackingPresenceProvider _fallbackPresence;
        private IFaceTrackingActivationControl _fallbackFaceActivation;

        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        private TrackingFrame _latestFace;
        private TrackingFrame _latestBodyHands;

        private string _selectedFaceSourceId;
        private long _selectedFaceChildSequence = -1;
        private long _faceSequence;

        private string _selectedBodySourceId;
        private long _selectedBodyChildSequence = -1;
        private long _bodySequence;

        public TrackingPresenceSnapshot Presence => _presence;

        private void Awake()
        {
            ResolveProviders();

            _presenceResolver = new TrackingPresenceResolver(
                SecondsToMicroseconds(subjectLostGraceSeconds),
                SecondsToMicroseconds(subjectRestoreStabilitySeconds),
                sourceStaleUs: 1_000_000);

            _presenceResolver.Reset(NowUs());
            _presence = _presenceResolver.Snapshot;
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
            UpdatePresence();
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
                    presence.SubjectState == SubjectPresenceState.Present &&
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

            _selectedFaceChildSequence = selected.Sequence;
            _selectedFaceSourceId = selected.SourceId;

            _latestFace = new TrackingFrame(
                ++_faceSequence,
                selected.SourceTimestampUs,
                selected.ValidRegions,
                selected.Confidence,
                selected.SubjectDetected,
                face: selected.Face,
                sourceId: selected.SourceId);
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
                sourceId: selected.SourceId);
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var preferred = _preferredPresence?.Presence;
            var fallback = _fallbackPresence?.Presence;

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
                 preferred.Value.SubjectState ==
                    SubjectPresenceState.Present) ||
                (fallbackFaceAvailable &&
                 fallback.Value.SubjectState ==
                    SubjectPresenceState.Present);

            var bodyConfigured = _fallbackProvider != null;
            var bodyAvailable =
                fallback.HasValue &&
                fallback.Value.BodyHandsSourceAvailable;

            var bodyEvidence =
                bodyAvailable &&
                _latestBodyHands != null &&
                _latestBodyHands.SubjectDetected;

            var faceConfigured =
                _preferredFaceProvider != null ||
                _fallbackProvider != null;

            var decisionReady =
                faceAvailable ||
                bodyAvailable ||
                (preferred.HasValue &&
                 preferred.Value.SubjectState != SubjectPresenceState.Unknown) ||
                (fallback.HasValue &&
                 fallback.Value.SubjectState != SubjectPresenceState.Unknown) ||
                _latestFace != null ||
                _latestBodyHands != null;

            _presence = _presenceResolver.UpdateResolved(
                NowUs(),
                faceConfigured,
                faceAvailable,
                faceEvidence,
                bodyConfigured,
                bodyAvailable,
                bodyEvidence,
                decisionReady);
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
