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
            if (!IsServiceAlive(_presenceProvider))
            {
                _presenceProvider = null;
                _lastPresenceSequence = -1;
            }

            if (!IsServiceAlive(_sink))
            {
                _sink = null;
            }

            if ((_presenceProvider == null ||
                 _sink == null) &&
                autoFindDependencies &&
                Time.unscaledTime >= _nextSearchTime)
            {
                _nextSearchTime =
                    Time.unscaledTime + 1f;
                ResolveDependencies();
            }

            if (!IsServiceAlive(_presenceProvider) ||
                !IsServiceAlive(_sink))
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
            AssignProvider(
                IsServiceAlive(provider)
                    ? provider
                    : null,
                provider as MonoBehaviour);
        }

        public void SetSink(
            INormalizedEventSink sink)
        {
            _sink =
                IsServiceAlive(sink)
                    ? sink
                    : null;
            eventSinkBehaviour =
                _sink as MonoBehaviour;
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

        private void AssignProvider(
            ITrackingPresenceProvider provider,
            MonoBehaviour behaviour)
        {
            if (!ReferenceEquals(
                    _presenceProvider,
                    provider))
            {
                _lastPresenceSequence = -1;
            }

            _presenceProvider = provider;
            presenceProviderBehaviour =
                provider != null
                    ? behaviour
                    : null;
        }

        private void ResolveDependencies()
        {
            if (presenceProviderBehaviour != null &&
                presenceProviderBehaviour is
                    ITrackingPresenceProvider provider)
            {
                AssignProvider(
                    provider,
                    presenceProviderBehaviour);
            }

            if (eventSinkBehaviour != null &&
                eventSinkBehaviour is
                    INormalizedEventSink sink)
            {
                _sink = sink;
            }

            if (!autoFindDependencies)
            {
                return;
            }

            if (!IsServiceAlive(_presenceProvider))
            {
                _presenceProvider = null;

                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is
                        ITrackingRouteProvider route)
                    {
                        AssignProvider(
                            route,
                            behaviour);
                        break;
                    }

                    if (_presenceProvider == null &&
                        behaviour is
                            ITrackingPresenceProvider direct)
                    {
                        AssignProvider(
                            direct,
                            behaviour);
                    }
                }
            }

            if (!IsServiceAlive(_sink))
            {
                _sink = null;

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
