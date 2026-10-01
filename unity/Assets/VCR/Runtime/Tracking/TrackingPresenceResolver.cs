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
            if (!_started)
            {
                _started = true;
                _startedAtUs = nowUs;
            }

            var faceAvailable =
                faceConfigured &&
                IsFresh(faceFrame, nowUs, _sourceStaleUs);

            var bodyHandsAvailable =
                bodyHandsConfigured &&
                IsFresh(bodyHandsFrame, nowUs, _sourceStaleUs);

            var anyConfigured = faceConfigured || bodyHandsConfigured;
            var anySourceAvailable = faceAvailable || bodyHandsAvailable;
            var sourceDecisionReady =
                faceFrame != null ||
                bodyHandsFrame != null ||
                nowUs - _startedAtUs >= _sourceStaleUs;

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
                (faceAvailable && faceFrame != null && faceFrame.SubjectDetected) ||
                (bodyHandsAvailable && bodyHandsFrame != null && bodyHandsFrame.SubjectDetected);

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

                    if (nowUs - _restoreCandidateSinceUs >= _subjectRestoreStabilityUs)
                    {
                        var wasEstablished = _subjectStateEstablished;
                        var wasLost = _subjectState == SubjectPresenceState.Lost;

                        _subjectState = SubjectPresenceState.Present;
                        _subjectStateEstablished = true;
                        _restoreCandidateSinceUs = -1;

                        if (wasEstablished && wasLost)
                        {
                            events |= TrackingPresenceEvents.SubjectRestored;
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

                    if (nowUs - _lostCandidateSinceUs >= _subjectLostGraceUs)
                    {
                        var wasEstablished = _subjectStateEstablished;
                        var wasPresent = _subjectState == SubjectPresenceState.Present;

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
                faceAvailable,
                bodyHandsAvailable,
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
                TrackingPresenceEvents.None);
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
