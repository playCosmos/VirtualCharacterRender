using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Events;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Protocols.OscEvents;

namespace VCR.Runtime.Protocols.OscEventsUnity
{
    /// <summary>
    /// Optional generic OSC normalized-event receiver.
    ///
    /// UDP receive/OSC parsing occurs on a background thread. Valid normalized
    /// events enter a bounded queue and are published to the configured event
    /// sink only from Unity's main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OscNormalizedEventUdpReceiver :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [Header("Receive")]
        [SerializeField, Range(1, 65535)]
        private int localPort = 39540;

        [Tooltip("Default is loopback-only. Blank accepts any IPv4 sender and should only be used on a trusted LAN.")]
        [SerializeField]
        private string allowedSenderIPv4Address =
            "127.0.0.1";

        [SerializeField]
        private string sourceId =
            "osc.udp";

        [Header("Dispatch")]
        [SerializeField, Range(32, 8192)]
        private int maxQueuedEvents = 1024;

        [SerializeField, Range(1, 2048)]
        private int maxDispatchPerFrame = 128;

        [SerializeField]
        private MonoBehaviour eventSinkBehaviour;

        [SerializeField]
        private bool autoFindEventSink = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        private readonly ConcurrentQueue<NormalizedEvent>
            _queue = new();

        private readonly object _queueSync =
            new();

        private UdpClient _receiver;
        private Thread _receiveThread;
        private volatile bool _running;
        private IPAddress _allowedSender;
        private INormalizedEventSink _sink;
        private float _nextSinkResolveRealtime;

        private int _queuedCount;
        private long _packetCount;
        private long _malformedPacketCount;
        private long _acceptedEventCount;
        private long _rejectedEventCount;
        private long _droppedEventCount;
        private long _dispatchedEventCount;
        private long _dispatchFailureCount;
        private long _rejectedSenderCount;
        private string _backgroundError;

        public int LocalPort =>
            Math.Max(
                1,
                Math.Min(
                    65535,
                    localPort));

        public int MaxQueuedEvents =>
            Math.Max(
                32,
                Math.Min(
                    8192,
                    maxQueuedEvents));

        public int MaxDispatchPerFrame =>
            Math.Max(
                1,
                Math.Min(
                    2048,
                    maxDispatchPerFrame));

        public int QueuedCount =>
            Math.Max(
                0,
                Volatile.Read(
                    ref _queuedCount));

        public long PacketCount =>
            Interlocked.Read(
                ref _packetCount);

        public long MalformedPacketCount =>
            Interlocked.Read(
                ref _malformedPacketCount);

        public long AcceptedEventCount =>
            Interlocked.Read(
                ref _acceptedEventCount);

        public long RejectedEventCount =>
            Interlocked.Read(
                ref _rejectedEventCount);

        public long DroppedEventCount =>
            Interlocked.Read(
                ref _droppedEventCount);

        public long DispatchedEventCount =>
            Interlocked.Read(
                ref _dispatchedEventCount);

        private void Awake()
        {
            ResolveSink(
                force: true);
        }

        private void OnEnable()
        {
            ResolveSink(
                force: true);

            if (Application.isPlaying)
            {
                StartReceiver();
            }
        }

        private void Update()
        {
            var backgroundError =
                Interlocked.Exchange(
                    ref _backgroundError,
                    null);

            if (!string.IsNullOrEmpty(
                    backgroundError))
            {
                Debug.LogWarning(
                    "VCR OSC event receiver: " +
                    backgroundError,
                    this);
            }

            if (!IsServiceAlive(_sink))
            {
                _sink = null;

                ResolveSink();
            }

            if (!IsServiceAlive(_sink))
            {
                return;
            }

            var budget =
                MaxDispatchPerFrame;

            for (var i = 0;
                 i < budget;
                 i++)
            {
                NormalizedEvent value;

                lock (_queueSync)
                {
                    if (!_queue.TryDequeue(
                            out value))
                    {
                        break;
                    }

                    Interlocked.Decrement(
                        ref _queuedCount);
                }

                try
                {
                    _sink.Publish(
                        value);

                    Interlocked.Increment(
                        ref _dispatchedEventCount);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(
                        ref _dispatchFailureCount);
                    Interlocked.Exchange(
                        ref _backgroundError,
                        "OSC event sink threw: " +
                        exception.Message);
                }
            }
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

        /// <summary>
        /// Test/bridge path that applies the same mapping and queue policy as
        /// UDP receive without requiring a socket.
        /// </summary>
        public bool TryQueueMessage(
            OscMessage message,
            out string error)
        {
            return TryQueueMessage(
                message,
                MonotonicClock
                    .NowMicroseconds(),
                out error);
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
                    "protocol.osc.events.queue_limit",
                    MaxQueuedEvents,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.dispatch_limit",
                    MaxDispatchPerFrame,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.packets",
                    PacketCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.malformed",
                    MalformedPacketCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.accepted",
                    AcceptedEventCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.rejected",
                    RejectedEventCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.dropped",
                    DroppedEventCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.dispatched",
                    DispatchedEventCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.dispatch_failures",
                    Interlocked.Read(
                        ref _dispatchFailureCount),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.rejected_senders",
                    Interlocked.Read(
                        ref _rejectedSenderCount),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.osc.events.queue",
                    QueuedCount,
                    "count"));
        }

        private void StartReceiver()
        {
            StopReceiver();

            if (!TryResolveAllowedSender(
                    out var senderError))
            {
                Debug.LogError(
                    "VCR OSC event receiver: " +
                    senderError,
                    this);
                enabled = false;
                return;
            }

            try
            {
                _receiver =
                    new UdpClient(
                        new IPEndPoint(
                            IPAddress.Any,
                            localPort));

                _receiver.Client.ReceiveTimeout =
                    250;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"VCR OSC event receiver: failed to bind UDP {localPort}: {exception.Message}",
                    this);
                enabled = false;
                return;
            }

            _running = true;
            _receiveThread =
                new Thread(
                    ReceiveLoop)
                {
                    IsBackground = true,
                    Name =
                        "VCR OSC Event UDP"
                };

            _receiveThread.Start();
        }

        private bool TryResolveAllowedSender(
            out string error)
        {
            error = null;
            _allowedSender = null;

            if (string.IsNullOrWhiteSpace(
                    allowedSenderIPv4Address))
            {
                return true;
            }

            if (!IPAddress.TryParse(
                    allowedSenderIPv4Address,
                    out _allowedSender) ||
                _allowedSender.AddressFamily !=
                    AddressFamily.InterNetwork)
            {
                _allowedSender = null;
                error =
                    "allowed sender must be a valid IPv4 address or blank.";
                return false;
            }

            return true;
        }

        private void ReceiveLoop()
        {
            var remote =
                new IPEndPoint(
                    IPAddress.Any,
                    0);

            var messages =
                new List<OscMessage>(
                    16);

            while (_running)
            {
                try
                {
                    var packet =
                        _receiver.Receive(
                            ref remote);

                    if (packet == null ||
                        packet.Length == 0)
                    {
                        continue;
                    }

                    Interlocked.Increment(
                        ref _packetCount);

                    if (_allowedSender != null &&
                        !remote.Address.Equals(
                            _allowedSender))
                    {
                        Interlocked.Increment(
                            ref _rejectedSenderCount);
                        continue;
                    }

                    if (!OscPacketReader
                        .TryReadMessages(
                            packet,
                            packet.Length,
                            messages))
                    {
                        Interlocked.Increment(
                            ref _malformedPacketCount);
                        continue;
                    }

                    var timestampUs =
                        MonotonicClock
                            .NowMicroseconds();

                    foreach (var message in
                             messages)
                    {
                        TryQueueMessage(
                            message,
                            timestampUs,
                            out _);
                    }
                }
                catch (SocketException exception)
                {
                    if (!_running)
                    {
                        return;
                    }

                    if (exception.SocketErrorCode ==
                            SocketError.TimedOut ||
                        exception.SocketErrorCode ==
                            SocketError.WouldBlock)
                    {
                        continue;
                    }

                    Interlocked.Exchange(
                        ref _backgroundError,
                        exception.Message);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (!_running)
                    {
                        return;
                    }

                    Interlocked.Exchange(
                        ref _backgroundError,
                        exception.Message);
                }
            }
        }

        private bool TryQueueMessage(
            OscMessage message,
            long receivedTimestampUs,
            out string error)
        {
            if (!OscNormalizedEventMapper
                .TryCreateEvent(
                    message,
                    sourceId,
                    receivedTimestampUs,
                    out var value,
                    out error))
            {
                Interlocked.Increment(
                    ref _rejectedEventCount);
                return false;
            }

            lock (_queueSync)
            {
                _queue.Enqueue(
                    value);
                Interlocked.Increment(
                    ref _queuedCount);

                var limit =
                    MaxQueuedEvents;

                while (Volatile.Read(
                           ref _queuedCount) >
                       limit &&
                       _queue.TryDequeue(
                           out _))
                {
                    Interlocked.Decrement(
                        ref _queuedCount);
                    Interlocked.Increment(
                        ref _droppedEventCount);
                }
            }

            Interlocked.Increment(
                ref _acceptedEventCount);

            return true;
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
                    return;
                }
            }
        }

        private void StopReceiver()
        {
            _running = false;

            try
            {
                _receiver?.Close();
            }
            catch
            {
                // Shutdown path.
            }

            _receiver = null;

            if (_receiveThread != null &&
                _receiveThread.IsAlive)
            {
                _receiveThread.Join(
                    500);
            }

            _receiveThread = null;
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnDestroy()
        {
            StopReceiver();
        }
    }
}
