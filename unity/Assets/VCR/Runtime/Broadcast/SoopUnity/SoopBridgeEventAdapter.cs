using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Broadcast.Soop;
using VCR.Runtime.Core;
using VCR.Runtime.Events;

namespace VCR.Runtime.Broadcast.SoopUnity
{
    /// <summary>
    /// JSON boundary for a SOOP connector/local bridge.
    ///
    /// Credentials, reconnect logic, platform packet parsing, and raw payloads
    /// stay in the connector. This adapter accepts only the VCR-owned bridge
    /// schema and publishes normalized chat/donation events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoopBridgeEventAdapter :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [SerializeField]
        private MonoBehaviour eventSinkBehaviour;

        [SerializeField]
        private bool autoFindEventSink = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        [SerializeField, Range(256, 262144)]
        private int maxMessageCharacters =
            65536;

        [SerializeField, Range(32, 8192)]
        private int rememberedEventIds =
            2048;

        private readonly Queue<string> _recentEventIds =
            new();

        private readonly HashSet<string> _recentEventIdSet =
            new(StringComparer.Ordinal);

        private readonly object _dedupeSync =
            new();

        private readonly object _parseSync =
            new();
        private readonly SoopBridgeMessage _documentScratch =
            new();

        private INormalizedEventSink _sink;
        private float _nextSinkResolveRealtime;

        private long _accepted;
        private long _rejected;
        private long _duplicates;
        private long _chatEvents;
        private long _donationEvents;

        public int MaxMessageCharacters =>
            Math.Max(
                256,
                Math.Min(
                    262144,
                    maxMessageCharacters));

        public int RememberedEventIdLimit =>
            Math.Max(
                32,
                Math.Min(
                    8192,
                    rememberedEventIds));

        public long AcceptedCount =>
            Interlocked.Read(
                ref _accepted);

        public long RejectedCount =>
            Interlocked.Read(
                ref _rejected);

        public long DuplicateCount =>
            Interlocked.Read(
                ref _duplicates);

        private void Awake()
        {
            ResolveSink(
                force: true);
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
            _nextSinkResolveRealtime = 0f;
        }

        public bool TryHandleText(
            string json,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                return Reject(
                    "SOOP bridge message is empty.",
                    out error);
            }

            if (json.Length >
                MaxMessageCharacters)
            {
                return Reject(
                    "SOOP bridge message exceeds the configured size limit.",
                    out error);
            }

            if (!IsServiceAlive(_sink))
            {
                var hadSink =
                    _sink != null;
                _sink = null;
                ResolveSink(
                    force: hadSink);

                if (!IsServiceAlive(_sink))
                {
                    return Reject(
                        "Normalized event sink is unavailable.",
                        out error);
                }
            }

            NormalizedEvent value;
            string eventId;

            lock (_parseSync)
            {
                ResetDocument(
                    _documentScratch);

                try
                {
                    JsonUtility.FromJsonOverwrite(
                        json,
                        _documentScratch);
                }
                catch (Exception exception)
                {
                    return Reject(
                        "SOOP bridge JSON is invalid: " +
                        exception.Message,
                        out error);
                }

                eventId =
                    _documentScratch.eventId;

                if (!SoopBridgeEventMapper
                    .TryValidateEventId(
                        eventId,
                        out var eventIdError))
                {
                    return Reject(
                        eventIdError,
                        out error);
                }

                // Preserve the existing duplicate contract: a previously
                // delivered event id is accepted as a duplicate before the
                // remaining payload is remapped.
                if (IsDuplicate(
                        eventId))
                {
                    Interlocked.Increment(
                        ref _duplicates);
                    return true;
                }

                if (!SoopBridgeEventMapper
                    .TryCreateEvent(
                        _documentScratch,
                        MonotonicClock
                            .NowMicroseconds(),
                        out value,
                        out error))
                {
                    Interlocked.Increment(
                        ref _rejected);
                    return false;
                }
            }

            try
            {
                _sink.Publish(
                    value);
            }
            catch (Exception exception)
            {
                return Reject(
                    "Normalized event sink failed: " +
                    exception.Message,
                    out error);
            }

            RememberEventId(
                eventId);

            Interlocked.Increment(
                ref _accepted);

            if (value.Type ==
                NormalizedEventTypes
                    .BroadcastChatMessage)
            {
                Interlocked.Increment(
                    ref _chatEvents);
            }
            else if (value.Type ==
                     NormalizedEventTypes
                         .BroadcastDonation)
            {
                Interlocked.Increment(
                    ref _donationEvents);
            }

            return true;
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
                    "broadcast.soop.accepted",
                    AcceptedCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "broadcast.soop.rejected",
                    RejectedCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "broadcast.soop.duplicates",
                    DuplicateCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "broadcast.soop.chat",
                    Interlocked.Read(
                        ref _chatEvents),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "broadcast.soop.donation",
                    Interlocked.Read(
                        ref _donationEvents),
                    "count"));
        }

        private bool IsDuplicate(
            string eventId)
        {
            if (string.IsNullOrWhiteSpace(
                    eventId))
            {
                return false;
            }

            lock (_dedupeSync)
            {
                return _recentEventIdSet
                    .Contains(
                        eventId);
            }
        }

        private void RememberEventId(
            string eventId)
        {
            if (string.IsNullOrWhiteSpace(
                    eventId))
            {
                return;
            }

            lock (_dedupeSync)
            {
                if (!_recentEventIdSet.Add(
                        eventId))
                {
                    return;
                }

                _recentEventIds.Enqueue(
                    eventId);

                var limit =
                    RememberedEventIdLimit;

                while (_recentEventIds.Count >
                       limit)
                {
                    var removed =
                        _recentEventIds.Dequeue();

                    _recentEventIdSet.Remove(
                        removed);
                }
            }
        }

        private static void ResetDocument(
            SoopBridgeMessage document)
        {
            // Match a freshly constructed wire DTO so fields omitted by
            // FromJsonOverwrite cannot inherit values from an earlier message.
            document.version =
                SoopBridgeEventMapper.CurrentVersion;
            document.type = null;
            document.eventId = null;
            document.userId = null;
            document.nickname = null;
            document.text = null;
            document.count = 0;
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

        private void ResolveSink(
            bool force = false)
        {
            if (eventSinkBehaviour != null &&
                eventSinkBehaviour is
                    INormalizedEventSink configured)
            {
                _sink = configured;
                _nextSinkResolveRealtime = 0f;
                return;
            }

            if (!autoFindEventSink)
            {
                return;
            }

            var now =
                Time.realtimeSinceStartup;

            if (!force &&
                now <
                _nextSinkResolveRealtime)
            {
                return;
            }

            _nextSinkResolveRealtime =
                now +
                Mathf.Max(
                    0.1f,
                    autoFindRetrySeconds);

            // An adapter with a destroyed explicitly configured sink
            // must first recover to a replacement on its own GameObject,
            // not an unrelated sink elsewhere in the Editor scene.
            var localBehaviours =
                GetComponents<MonoBehaviour>();
            foreach (var behaviour in localBehaviours)
            {
                if (behaviour != null &&
                    !ReferenceEquals(behaviour, this) &&
                    behaviour is INormalizedEventSink localSink)
                {
                    _sink = localSink;
                    eventSinkBehaviour = behaviour;
                    _nextSinkResolveRealtime = 0f;
                    return;
                }
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in
                     behaviours)
            {
                if (ReferenceEquals(
                        behaviour,
                        this))
                {
                    continue;
                }

                if (behaviour is
                    INormalizedEventSink sink)
                {
                    _sink = sink;
                    eventSinkBehaviour =
                        behaviour;
                    _nextSinkResolveRealtime = 0f;
                    return;
                }
            }
        }

        private bool Reject(
            string message,
            out string error)
        {
            error = message;
            Interlocked.Increment(
                ref _rejected);
            return false;
        }
    }
}
