using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Derives stable performer presence independently from source availability.
    ///
    /// A source can be alive while reporting no subject. Conversely, if all
    /// configured source callbacks become stale, the subject state becomes
    /// Unknown rather than falsely emitting SubjectLost.
    /// </summary>
    public sealed class TrackingPresenceResolver
    {
        private readonly long _subjectLostGraceUs;
        private readonly long _subjectRestoreStabilityUs;
        private readonly long _sourceStaleUs;

        private bool _started;
        private long _startedAtUs;

        private bool _availabilityEstablished;
        private bool _lastAnySourceAvailable;

        private bool _subjectStateEstablished;
        private SubjectPresenceState _subjectState = SubjectPresenceState.Unknown;

        private long _lostCandidateSinceUs = -1;
        private long _restoreCandidateSinceUs = -1;
        private long _sequence;

        private TrackingPresenceSnapshot _snapshot;

        public TrackingPresenceResolver(
            long subjectLostGraceUs,
            long subjectRestoreStabilityUs,
            long sourceStaleUs)
        {
            if (subjectLostGraceUs < 0) throw new ArgumentOutOfRangeException(nameof(subjectLostGraceUs));
            if (subjectRestoreStabilityUs < 0) throw new ArgumentOutOfRangeException(nameof(subjectRestoreStabilityUs));
            if (sourceStaleUs <= 0) throw new ArgumentOutOfRangeException(nameof(sourceStaleUs));

            _subjectLostGraceUs = subjectLostGraceUs;
            _subjectRestoreStabilityUs = subjectRestoreStabilityUs;
            _sourceStaleUs = sourceStaleUs;
        }

        public TrackingPresenceSnapshot Snapshot => _snapshot;

        public TrackingPresenceSnapshot Update(
            long nowUs,
            TrackingFrame faceFrame,
            bool faceConfigured,
            TrackingFrame bodyHandsFrame,
            bool bodyHandsConfigured)
        {
            var faceAvailable =
                faceConfigured &&
                IsFresh(faceFrame, nowUs, _sourceStaleUs);

            var bodyHandsAvailable =
                bodyHandsConfigured &&
                IsFresh(bodyHandsFrame, nowUs, _sourceStaleUs);

            var sourceDecisionReady =
                faceFrame != null ||
                bodyHandsFrame != null ||
                IsSourceDecisionReady(nowUs);

            var faceSubjectEvidence =
                faceAvailable &&
                faceFrame != null &&
                faceFrame.SubjectDetected;

            var bodySubjectEvidence =
                bodyHandsAvailable &&
                bodyHandsFrame != null &&
                bodyHandsFrame.SubjectDetected;

            return UpdateResolved(
                nowUs,
                faceConfigured,
                faceAvailable,
                faceSubjectEvidence,
                bodyHandsConfigured,
                bodyHandsAvailable,
                bodySubjectEvidence,
                sourceDecisionReady,
                fullBodyConfigured: false,
                fullBodySourceAvailable: false,
                fullBodySubjectEvidence: false);
        }

        /// <summary>
        /// Updates presence from already-resolved source availability/evidence.
        /// Use this when child providers have different local timestamp origins.
        /// </summary>
        public TrackingPresenceSnapshot UpdateResolved(
            long nowUs,
            bool faceConfigured,
            bool faceSourceAvailable,
            bool faceSubjectEvidence,
            bool bodyHandsConfigured,
            bool bodyHandsSourceAvailable,
            bool bodyHandsSubjectEvidence,
            bool sourceDecisionReady = true,
            bool fullBodyConfigured = false,
            bool fullBodySourceAvailable = false,
            bool fullBodySubjectEvidence = false)
        {
            EnsureStarted(nowUs);

            var anyConfigured =
                faceConfigured ||
                bodyHandsConfigured ||
                fullBodyConfigured;

            var anySourceAvailable =
                faceSourceAvailable ||
                bodyHandsSourceAvailable ||
                fullBodySourceAvailable;

            var events = TrackingPresenceEvents.None;

            if (anyConfigured && sourceDecisionReady)
            {
                if (!_availabilityEstablished)
                {
                    _availabilityEstablished = true;
                    _lastAnySourceAvailable = anySourceAvailable;
                }
                else if (anySourceAvailable != _lastAnySourceAvailable)
                {
                    events |= anySourceAvailable
                        ? TrackingPresenceEvents.TrackingSourceRestored
                        : TrackingPresenceEvents.TrackingSourceLost;

                    _lastAnySourceAvailable = anySourceAvailable;
                }
            }

            var subjectEvidence =
                faceSubjectEvidence ||
                bodyHandsSubjectEvidence ||
                fullBodySubjectEvidence;

            if (!anySourceAvailable)
            {
                _lostCandidateSinceUs = -1;
                _restoreCandidateSinceUs = -1;
                _subjectState = SubjectPresenceState.Unknown;
            }
            else if (subjectEvidence)
            {
                _lostCandidateSinceUs = -1;

                if (_subjectState == SubjectPresenceState.Present)
                {
                    _restoreCandidateSinceUs = -1;
                }
                else
                {
                    if (_restoreCandidateSinceUs < 0)
                    {
                        _restoreCandidateSinceUs = nowUs;
                    }

                    if (nowUs - _restoreCandidateSinceUs >=
                        _subjectRestoreStabilityUs)
                    {
                        var wasEstablished = _subjectStateEstablished;
                        var wasLost =
                            _subjectState == SubjectPresenceState.Lost;

                        _subjectState = SubjectPresenceState.Present;
                        _subjectStateEstablished = true;
                        _restoreCandidateSinceUs = -1;

                        if (wasEstablished && wasLost)
                        {
                            events |=
                                TrackingPresenceEvents.SubjectRestored;
                        }
                    }
                }
            }
            else
            {
                _restoreCandidateSinceUs = -1;

                if (_subjectState == SubjectPresenceState.Lost)
                {
                    _lostCandidateSinceUs = -1;
                }
                else
                {
                    if (_lostCandidateSinceUs < 0)
                    {
                        _lostCandidateSinceUs = nowUs;
                    }

                    if (nowUs - _lostCandidateSinceUs >=
                        _subjectLostGraceUs)
                    {
                        var wasEstablished = _subjectStateEstablished;
                        var wasPresent =
                            _subjectState == SubjectPresenceState.Present;

                        _subjectState = SubjectPresenceState.Lost;
                        _subjectStateEstablished = true;
                        _lostCandidateSinceUs = -1;

                        if (wasEstablished && wasPresent)
                        {
                            events |= TrackingPresenceEvents.SubjectLost;
                        }
                    }
                }
            }

            if (events != TrackingPresenceEvents.None)
            {
                _sequence++;
            }

            _snapshot = new TrackingPresenceSnapshot(
                _sequence,
                nowUs,
                _subjectState,
                faceSourceAvailable,
                bodyHandsSourceAvailable,
                fullBodySourceAvailable,
                faceSubjectEvidence,
                bodyHandsSubjectEvidence,
                fullBodySubjectEvidence,
                anySourceAvailable,
                subjectEvidence,
                events);

            return _snapshot;
        }

        public void Reset(long nowUs)
        {
            _started = true;
            _startedAtUs = nowUs;
            _availabilityEstablished = false;
            _lastAnySourceAvailable = false;
            _subjectStateEstablished = false;
            _subjectState = SubjectPresenceState.Unknown;
            _lostCandidateSinceUs = -1;
            _restoreCandidateSinceUs = -1;
            _sequence++;
            _snapshot = new TrackingPresenceSnapshot(
                _sequence,
                nowUs,
                SubjectPresenceState.Unknown,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                TrackingPresenceEvents.None);
        }

        private void EnsureStarted(long nowUs)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _startedAtUs = nowUs;
        }

        private bool IsSourceDecisionReady(long nowUs)
        {
            EnsureStarted(nowUs);
            return nowUs - _startedAtUs >= _sourceStaleUs;
        }

        private static bool IsFresh(
            TrackingFrame frame,
            long nowUs,
            long staleUs)
        {
            if (frame == null)
            {
                return false;
            }

            var ageUs = nowUs - frame.SourceTimestampUs;
            return ageUs >= 0 && ageUs <= staleUs;
        }
    }
}
