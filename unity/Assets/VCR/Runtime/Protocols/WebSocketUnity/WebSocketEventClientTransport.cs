using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Protocols.WebSocket;

namespace VCR.Runtime.Protocols.WebSocketUnity
{
    /// <summary>
    /// Optional outbound WebSocket bridge transport for event injection.
    ///
    /// Network receive runs asynchronously. Complete text messages enter a
    /// bounded queue and are delivered to IWebSocketTextMessageHandler only
    /// from Unity's main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebSocketEventClientTransport :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [Header("Endpoint")]
        [SerializeField]
        private string endpoint =
            "ws://127.0.0.1:39541/vcr/events";

        [SerializeField]
        private bool autoConnect = true;

        [SerializeField]
        private bool autoReconnect = true;

        [SerializeField, Min(0.25f)]
        private float reconnectDelaySeconds = 2f;

        [SerializeField, Min(1f)]
        private float keepAliveSeconds = 20f;

        [Header("Ingress")]
        [SerializeField, Range(1024, 1048576)]
        private int maxMessageBytes = 65536;

        [SerializeField, Range(32, 8192)]
        private int maxQueuedMessages = 1024;

        [SerializeField, Range(1, 2048)]
        private int maxDispatchPerFrame = 128;

        [SerializeField]
        private MonoBehaviour messageHandlerBehaviour;

        [SerializeField]
        private bool autoFindMessageHandler = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        private readonly ConcurrentQueue<string>
            _queue = new();

        private readonly object _clientSync =
            new();

        private IWebSocketTextMessageHandler _handler;
        private float _nextHandlerResolveRealtime;
        private CancellationTokenSource _cancellation;
        private Task _runTask;
        private ClientWebSocket _client;
        private long _generation;

        private int _state =
            (int)WebSocketClientTransportState.Stopped;
        private int _queuedCount;

        private long _connectSuccesses;
        private long _connectFailures;
        private long _messagesReceived;
        private long _messagesDropped;
        private long _messagesDispatched;
        private long _handlerRejections;
        private long _transportRejections;

        private string _backgroundError;

        public WebSocketClientTransportState State =>
            (WebSocketClientTransportState)
            Volatile.Read(
                ref _state);

        public int QueuedCount =>
            Math.Max(
                0,
                Volatile.Read(
                    ref _queuedCount));

        public long ConnectSuccessCount =>
            Interlocked.Read(
                ref _connectSuccesses);

        public long ConnectFailureCount =>
            Interlocked.Read(
                ref _connectFailures);

        public long MessagesReceived =>
            Interlocked.Read(
                ref _messagesReceived);

        public long MessagesDropped =>
            Interlocked.Read(
                ref _messagesDropped);

        private void Awake()
        {
            ResolveHandler(
                force: true);
        }

        private void OnEnable()
        {
            ResolveHandler(
                force: true);

            if (Application.isPlaying &&
                autoConnect)
            {
                StartTransport(
                    out _);
            }
        }

        private void Update()
        {
            var error =
                Interlocked.Exchange(
                    ref _backgroundError,
                    null);

            if (!string.IsNullOrEmpty(
                    error))
            {
                Debug.LogWarning(
                    "VCR WebSocket transport: " +
                    error,
                    this);
            }

            if (_handler == null)
            {
                ResolveHandler();
            }

            if (_handler == null)
            {
                return;
            }

            var budget =
                Math.Max(
                    1,
                    maxDispatchPerFrame);

            for (var i = 0;
                 i < budget &&
                 _queue.TryDequeue(
                     out var message);
                 i++)
            {
                Interlocked.Decrement(
                    ref _queuedCount);

                if (_handler.TryHandleText(
                        message,
                        out var handlerError))
                {
                    Interlocked.Increment(
                        ref _messagesDispatched);
                }
                else
                {
                    Interlocked.Increment(
                        ref _handlerRejections);

                    if (!string.IsNullOrWhiteSpace(
                            handlerError))
                    {
                        Interlocked.Exchange(
                            ref _backgroundError,
                            handlerError);
                    }
                }
            }
        }

        public void SetHandler(
            IWebSocketTextMessageHandler handler)
        {
            _handler = handler;
            messageHandlerBehaviour =
                handler as MonoBehaviour;
        }

        public bool StartTransport(
            out string error)
        {
            error = null;

            if (_runTask != null &&
                !_runTask.IsCompleted &&
                _cancellation != null &&
                !_cancellation.IsCancellationRequested)
            {
                return true;
            }

            if (!TryValidateEndpoint(
                    endpoint,
                    out var uri,
                    out error))
            {
                SetState(
                    WebSocketClientTransportState.Faulted);
                return false;
            }

            StopTransport();

            var generation =
                Interlocked.Increment(
                    ref _generation);

            _cancellation =
                new CancellationTokenSource();

            _runTask =
                RunTransportAsync(
                    uri,
                    _cancellation.Token,
                    generation);

            return true;
        }

        public void StopTransport()
        {
            Interlocked.Increment(
                ref _generation);

            var cancellation =
                _cancellation;
            _cancellation = null;

            if (cancellation != null)
            {
                try
                {
                    cancellation.Cancel();
                }
                catch
                {
                    // Shutdown path.
                }
            }

            lock (_clientSync)
            {
                try
                {
                    _client?.Abort();
                }
                catch
                {
                    // Shutdown path.
                }
            }

            _runTask = null;

            SetState(
                WebSocketClientTransportState.Stopped);
        }

        /// <summary>
        /// Source-free validation path through the same bounded main-thread
        /// delivery queue used by network receive.
        /// </summary>
        public bool TryQueueText(
            string message,
            out string error)
        {
            error = null;

            if (message == null)
            {
                error =
                    "WebSocket text message is null.";
                Interlocked.Increment(
                    ref _transportRejections);
                return false;
            }

            int byteCount;

            try
            {
                byteCount =
                    StrictUtf8.GetByteCount(
                        message);
            }
            catch (EncoderFallbackException exception)
            {
                error =
                    "WebSocket text message is not valid UTF-8: " +
                    exception.Message;
                Interlocked.Increment(
                    ref _transportRejections);
                return false;
            }

            if (byteCount >
                Math.Max(
                    1024,
                    maxMessageBytes))
            {
                error =
                    "WebSocket text message exceeds the configured byte limit.";
                Interlocked.Increment(
                    ref _transportRejections);
                return false;
            }

            EnqueueText(
                message);

            return true;
        }

        public static bool TryValidateEndpoint(
            string value,
            out Uri uri,
            out string error)
        {
            uri = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    value) ||
                !Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out uri))
            {
                error =
                    "WebSocket endpoint must be an absolute ws:// or wss:// URI.";
                return false;
            }

            var isWs =
                string.Equals(
                    uri.Scheme,
                    "ws",
                    StringComparison.OrdinalIgnoreCase);
            var isWss =
                string.Equals(
                    uri.Scheme,
                    "wss",
                    StringComparison.OrdinalIgnoreCase);

            if (!isWs &&
                !isWss)
            {
                error =
                    "WebSocket endpoint scheme must be ws or wss.";
                uri = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    uri.Host) ||
                !string.IsNullOrEmpty(
                    uri.UserInfo))
            {
                error =
                    "WebSocket endpoint host is required and URI user-info credentials are not allowed.";
                uri = null;
                return false;
            }

            if (isWs &&
                !uri.IsLoopback)
            {
                error =
                    "Unencrypted ws:// is allowed only for loopback endpoints; remote endpoints require wss://.";
                uri = null;
                return false;
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
                    "protocol.websocket.transport.state",
                    (int)State,
                    "enum"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.connect_success",
                    ConnectSuccessCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.connect_failure",
                    ConnectFailureCount,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.received",
                    MessagesReceived,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.dropped",
                    MessagesDropped,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.dispatched",
                    Interlocked.Read(
                        ref _messagesDispatched),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.handler_rejected",
                    Interlocked.Read(
                        ref _handlerRejections),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.rejected",
                    Interlocked.Read(
                        ref _transportRejections),
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "protocol.websocket.transport.queue",
                    QueuedCount,
                    "count"));
        }

        private async Task RunTransportAsync(
            Uri uri,
            CancellationToken cancellationToken,
            long generation)
        {
            var firstAttempt = true;

            while (!cancellationToken
                       .IsCancellationRequested)
            {
                SetStateIfCurrent(
                    generation,
                    firstAttempt
                        ? WebSocketClientTransportState.Connecting
                        : WebSocketClientTransportState.Reconnecting);

                using var client =
                    new ClientWebSocket();

                client.Options.KeepAliveInterval =
                    TimeSpan.FromSeconds(
                        Math.Max(
                            1f,
                            keepAliveSeconds));

                lock (_clientSync)
                {
                    _client = client;
                }

                try
                {
                    await client.ConnectAsync(
                        uri,
                        cancellationToken);

                    Interlocked.Increment(
                        ref _connectSuccesses);

                    SetStateIfCurrent(
                        generation,
                        WebSocketClientTransportState.Connected);

                    await ReceiveLoopAsync(
                        client,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(
                        ref _connectFailures);

                    Interlocked.Exchange(
                        ref _backgroundError,
                        exception.Message);
                }
                finally
                {
                    lock (_clientSync)
                    {
                        if (ReferenceEquals(
                                _client,
                                client))
                        {
                            _client = null;
                        }
                    }
                }

                if (cancellationToken
                        .IsCancellationRequested)
                {
                    break;
                }

                if (!autoReconnect)
                {
                    SetStateIfCurrent(
                        generation,
                        WebSocketClientTransportState.Faulted);
                    return;
                }

                firstAttempt = false;

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            Math.Max(
                                0.25f,
                                reconnectDelaySeconds)),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            SetStateIfCurrent(
                generation,
                WebSocketClientTransportState.Stopped);
        }

        private async Task ReceiveLoopAsync(
            ClientWebSocket client,
            CancellationToken cancellationToken)
        {
            var chunk =
                new byte[
                    Math.Min(
                        8192,
                        Math.Max(
                            1024,
                            maxMessageBytes))];

            var messageBytes =
                new byte[
                    Math.Max(
                        1024,
                        maxMessageBytes)];

            var offset = 0;
            var discarding = false;

            while (!cancellationToken
                       .IsCancellationRequested &&
                   client.State ==
                       WebSocketState.Open)
            {
                var result =
                    await client.ReceiveAsync(
                        new ArraySegment<byte>(
                            chunk),
                        cancellationToken);

                if (result.MessageType ==
                    WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.MessageType !=
                    WebSocketMessageType.Text)
                {
                    if (!discarding)
                    {
                        Interlocked.Increment(
                            ref _transportRejections);
                    }

                    discarding =
                        !result.EndOfMessage;
                    offset = 0;
                    continue;
                }

                if (discarding)
                {
                    if (result.EndOfMessage)
                    {
                        discarding = false;
                    }

                    continue;
                }

                if (offset + result.Count >
                    messageBytes.Length)
                {
                    Interlocked.Increment(
                        ref _transportRejections);

                    offset = 0;
                    discarding =
                        !result.EndOfMessage;
                    continue;
                }

                Buffer.BlockCopy(
                    chunk,
                    0,
                    messageBytes,
                    offset,
                    result.Count);

                offset +=
                    result.Count;

                if (!result.EndOfMessage)
                {
                    continue;
                }

                try
                {
                    var text =
                        StrictUtf8.GetString(
                            messageBytes,
                            0,
                            offset);

                    EnqueueText(
                        text);
                }
                catch (DecoderFallbackException)
                {
                    Interlocked.Increment(
                        ref _transportRejections);
                }

                offset = 0;
            }
        }

        private void EnqueueText(
            string message)
        {
            _queue.Enqueue(
                message);

            Interlocked.Increment(
                ref _messagesReceived);

            var count =
                Interlocked.Increment(
                    ref _queuedCount);

            var limit =
                Math.Max(
                    32,
                    maxQueuedMessages);

            while (count > limit &&
                   _queue.TryDequeue(
                       out _))
            {
                Interlocked.Decrement(
                    ref _queuedCount);

                Interlocked.Increment(
                    ref _messagesDropped);

                count--;
            }
        }

        private void ResolveHandler(
            bool force = false)
        {
            if (messageHandlerBehaviour is
                IWebSocketTextMessageHandler configured)
            {
                _handler = configured;
                _nextHandlerResolveRealtime = 0f;
                return;
            }

            if (!autoFindMessageHandler)
            {
                return;
            }

            var now =
                Time.realtimeSinceStartup;

            if (!force &&
                now <
                _nextHandlerResolveRealtime)
            {
                return;
            }

            _nextHandlerResolveRealtime =
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
                    IWebSocketTextMessageHandler handler)
                {
                    _handler = handler;
                    messageHandlerBehaviour =
                        behaviour;
                    return;
                }
            }
        }

        private void SetStateIfCurrent(
            long generation,
            WebSocketClientTransportState state)
        {
            if (Interlocked.Read(
                    ref _generation) !=
                generation)
            {
                return;
            }

            SetState(
                state);
        }

        private void SetState(
            WebSocketClientTransportState state)
        {
            Volatile.Write(
                ref _state,
                (int)state);
        }

        private void OnDisable()
        {
            StopTransport();
        }

        private void OnDestroy()
        {
            StopTransport();
        }
    }
}
