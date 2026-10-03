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

        private INormalizedEventSink _sink;

        private long _accepted;
        private long _rejected;
        private long _duplicates;
        private long _chatEvents;
        private long _donationEvents;

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
            ResolveSink();
        }

        public void SetSink(
            INormalizedEventSink sink)
        {
            _sink = sink;
            eventSinkBehaviour =
                sink as MonoBehaviour;
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
                Math.Max(
                    256,
                    maxMessageCharacters))
            {
                return Reject(
                    "SOOP bridge message exceeds the configured size limit.",
                    out error);
            }

            if (_sink == null)
            {
                ResolveSink();

                if (_sink == null)
                {
                    return Reject(
                        "Normalized event sink is unavailable.",
                        out error);
                }
            }

            SoopBridgeMessage document;

            try
            {
                document =
                    JsonUtility.FromJson<
                        SoopBridgeMessage>(
                        json);
            }
            catch (Exception exception)
            {
                return Reject(
                    "SOOP bridge JSON is invalid: " +
                    exception.Message,
                    out error);
            }

            if (document == null)
            {
                return Reject(
                    "SOOP bridge JSON produced no message.",
                    out error);
            }

            if (IsDuplicate(
                    document.eventId))
            {
                Interlocked.Increment(
                    ref _duplicates);
                return true;
            }

            if (!SoopBridgeEventMapper
                .TryCreateEvent(
                    document,
                    MonotonicClock
                        .NowMicroseconds(),
                    out var value,
                    out error))
            {
                Interlocked.Increment(
                    ref _rejected);
                return false;
            }

            RememberEventId(
                document.eventId);

            _sink.Publish(
                value);

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
                    Math.Max(
                        32,
                        rememberedEventIds);

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

        private void ResolveSink()
        {
            if (eventSinkBehaviour is
                INormalizedEventSink configured)
            {
                _sink = configured;
                return;
            }

            if (!autoFindEventSink)
            {
                return;
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
