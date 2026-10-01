using System;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Events.Unity
{
    /// <summary>
    /// Converts tracking-derived subject/source transitions into normalized
    /// events. Subject loss means the performer disappeared from tracking; it
    /// is intentionally not named or interpreted as AFK.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackingPresenceEventAdapter :
        MonoBehaviour
    {
        [SerializeField] private MonoBehaviour presenceProviderBehaviour;
        [SerializeField] private MonoBehaviour eventSinkBehaviour;
        [SerializeField] private bool autoFindDependencies = true;

        private ITrackingPresenceProvider _presenceProvider;
        private INormalizedEventSink _sink;
        private long _lastPresenceSequence = -1;
        private float _nextSearchTime;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            if ((_presenceProvider == null ||
                 _sink == null) &&
                autoFindDependencies &&
                Time.unscaledTime >= _nextSearchTime)
            {
                _nextSearchTime =
                    Time.unscaledTime + 1f;
                ResolveDependencies();
            }

            if (_presenceProvider == null ||
                _sink == null)
            {
                return;
            }

            var snapshot =
                _presenceProvider.Presence;

            if (snapshot.Sequence ==
                _lastPresenceSequence)
            {
                return;
            }

            _lastPresenceSequence =
                snapshot.Sequence;

            var events = snapshot.Events;
            if (events == TrackingPresenceEvents.None)
            {
                return;
            }

            if ((events &
                 TrackingPresenceEvents.SubjectLost) != 0)
            {
                Emit(
                    NormalizedEventTypes
                        .TrackingSubjectLost);
            }

            if ((events &
                 TrackingPresenceEvents.SubjectRestored) != 0)
            {
                Emit(
                    NormalizedEventTypes
                        .TrackingSubjectRestored);
            }

            if ((events &
                 TrackingPresenceEvents.TrackingSourceLost) != 0)
            {
                Emit(
                    NormalizedEventTypes
                        .TrackingSourceLost);
            }

            if ((events &
                 TrackingPresenceEvents.TrackingSourceRestored) != 0)
            {
                Emit(
                    NormalizedEventTypes
                        .TrackingSourceRestored);
            }
        }

        public void SetProvider(
            ITrackingPresenceProvider provider)
        {
            _presenceProvider = provider;
            presenceProviderBehaviour =
                provider as MonoBehaviour;
            _lastPresenceSequence = -1;
        }

        public void SetSink(
            INormalizedEventSink sink)
        {
            _sink = sink;
            eventSinkBehaviour =
                sink as MonoBehaviour;
        }

        private void ResolveDependencies()
        {
            if (presenceProviderBehaviour is
                ITrackingPresenceProvider provider)
            {
                _presenceProvider = provider;
            }

            if (eventSinkBehaviour is
                INormalizedEventSink sink)
            {
                _sink = sink;
            }

            if (!autoFindDependencies)
            {
                return;
            }

            if (_presenceProvider == null)
            {
                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is
                        ITrackingRouteProvider route)
                    {
                        _presenceProvider = route;
                        presenceProviderBehaviour =
                            behaviour;
                        break;
                    }

                    if (_presenceProvider == null &&
                        behaviour is
                            ITrackingPresenceProvider direct)
                    {
                        _presenceProvider = direct;
                        presenceProviderBehaviour =
                            behaviour;
                    }
                }
            }

            if (_sink == null)
            {
                var hub =
                    FindFirstObjectByType<
                        NormalizedEventHub>();

                if (hub != null)
                {
                    _sink = hub;
                    eventSinkBehaviour = hub;
                }
            }
        }

        private void Emit(string type)
        {
            _sink.Publish(
                new NormalizedEvent(
                    type,
                    sourceId:
                        "tracking.presence",
                    timestampUs:
                        MonotonicClock.NowMicroseconds()));
        }
    }
}
