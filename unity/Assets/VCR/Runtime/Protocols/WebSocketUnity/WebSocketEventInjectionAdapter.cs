using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Events;
using VCR.Runtime.Protocols.WebSocket;

namespace VCR.Runtime.Protocols.WebSocketUnity
{
    /// <summary>
    /// Unity JSON adapter for the transport-neutral WebSocket event protocol.
    ///
    /// A WebSocket transport supplies complete text messages through
    /// IWebSocketTextMessageHandler. This component performs no socket polling
    /// and owns no credentials.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebSocketEventInjectionAdapter :
        MonoBehaviour,
        IWebSocketTextMessageHandler,
        IRuntimeMetricsSource
    {
        [SerializeField]
        private MonoBehaviour eventSinkBehaviour;

        [SerializeField]
        private bool autoFindEventSink = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        [SerializeField]
        private string sourceId =
            "websocket.external";

        [SerializeField, Range(256, 262144)]
        private int maxMessageCharacters =
            65536;

        private INormalizedEventSink _sink;
        private float _nextSinkResolveRealtime;
        private long _accepted;
        private long _rejected;

        public long AcceptedCount =>
            Interlocked.Read(
                ref _accepted);

        public long RejectedCount =>
            Interlocked.Read(
                ref _rejected);

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
            string message,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    message))
            {
                return Reject(
                    "WebSocket event message is empty.",
                    out error);
            }

            if (message.Length >
                Math.Max(
                    256,
                    maxMessageCharacters))
            {
                return Reject(
                    "WebSocket event message exceeds the configured size limit.",
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

            WebSocketEventMessage document;

            try
            {
                document =
                    JsonUtility.FromJson<
                        WebSocketEventMessage>(
                        message);
            }
            catch (Exception exception)
            {
                return Reject(
                    "WebSocket event JSON is invalid: " +
                    exception.Message,
                    out error);
            }

            if (!WebSocketEventProtocol
                .TryCreateEvent(
                    document,
                    sourceId,
                    MonotonicClock
                        .NowMicroseconds(),
                    out var value,
                    out error))
            {
                Interlocked.Increment(
                    ref _rejected);
                return false;
            }

            _sink.Publish(
                value);

            Interlocked.Increment(
                ref _accepted);

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
                    "protocol.websocket.events.accepted",
                    AcceptedCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.events.rejected",
                    RejectedCount,
                    "count"));
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
