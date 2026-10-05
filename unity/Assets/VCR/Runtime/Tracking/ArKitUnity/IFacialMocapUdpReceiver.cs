using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking.ArKit;

namespace VCR.Runtime.Tracking.ArKitUnity
{
    /// <summary>
    /// Desktop UDP receiver for the public iFacialMocap/FaceMotion3D ARKit
    /// streaming format.
    ///
    /// Network I/O and text parsing run off the Unity main thread. Only the
    /// newest parsed frame is converted to normalized tracking on Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IFacialMocapUdpReceiver :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingPresenceProvider,
        ITrackingSourceHealthProvider,
        ITrackingRuntimeControl,
        IRuntimeMetricsSource
    {
        private const int MaxDatagramBytes =
            16 * 1024;
        private const int UdpReceiveBufferBytes =
            65535;

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        [Header("iOS sender")]
        [SerializeField] private string iosIPv4Address = "";
        [SerializeField, Range(1, 65535)] private int remotePort =
            IFacialMocapFrameParser.DefaultPort;
        [SerializeField, Range(1, 65535)] private int localPort =
            IFacialMocapFrameParser.DefaultPort;
        [SerializeField, Min(0.25f)] private float handshakeRetrySeconds = 2f;

        [Header("Head conversion - provisional P0 profile")]
        [Tooltip("Common iFacialMocap consumers invert X/Y head Euler signs. Validate on physical hardware.")]
        [SerializeField] private Vector3 headEulerSigns =
            new Vector3(-1f, -1f, 1f);
        [SerializeField] private bool useHeadPosition = false;
        [SerializeField] private Vector3 headPositionSigns =
            new Vector3(1f, 1f, -1f);
        [SerializeField] private float headPositionScale = 1f;

        [Header("Presence")]
        [SerializeField, Min(0.1f)] private float sourceStaleSeconds = 1f;
        [SerializeField, Min(0f)] private float restoreStabilitySeconds = 0.15f;

        private readonly LatestValueBuffer<IFacialMocapFrame> _rawFrames = new();
        private readonly Stopwatch _clock = new();

        private ArKitFaceSource _source;
        private TrackingFrame _latestFace;
        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        private UdpClient _receiver;
        private Thread _receiveThread;
        private volatile bool _running;
        private IPEndPoint _iosEndpoint;

        private long _datagramCount;
        private long _packetCount;
        private long _rejectedSenderCount;
        private long _parseFailureCount;
        private long _oversizedDatagramCount;
        private long _invalidUtf8Count;
        private long _handshakeCount;
        private long _socketErrorCount;

        private float _nextHandshakeTime;
        private string _backgroundError;
        private string _lastError;
        private ArKitReceiverLifecycleState _state =
            ArKitReceiverLifecycleState.Stopped;

        public string ControlId => "arkit-ifacialmocap";
        public string DisplayName => "ARKit / iFacialMocap";
        public bool ControlEnabled => enabled;
        public TrackingSourceHealthState ControlHealthState =>
            _state switch
            {
                ArKitReceiverLifecycleState.Starting =>
                    TrackingSourceHealthState.Starting,
                ArKitReceiverLifecycleState.Running =>
                    TrackingSourceHealthState.Healthy,
                ArKitReceiverLifecycleState.SourceLost =>
                    TrackingSourceHealthState.SourceLost,
                ArKitReceiverLifecycleState.Faulted =>
                    TrackingSourceHealthState.Faulted,
                _ =>
                    TrackingSourceHealthState.Stopped
            };
        public string ControlError => _lastError;

        public TrackingPresenceSnapshot Presence => _presence;
        public long PacketCount => Interlocked.Read(ref _packetCount);
        public ITrackingSource FaceTrackingSource => _source;

        public ArKitReceiverStatus Status =>
            new(
                _state,
                iosIPv4Address,
                remotePort,
                localPort,
                Interlocked.Read(ref _datagramCount),
                Interlocked.Read(ref _packetCount),
                Interlocked.Read(ref _rejectedSenderCount),
                Interlocked.Read(ref _parseFailureCount),
                Interlocked.Read(ref _handshakeCount),
                Interlocked.Read(ref _socketErrorCount),
                _lastError);

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            StartReceiver();
        }

        private void Update()
        {
            var backgroundError =
                Interlocked.Exchange(ref _backgroundError, null);
            if (!string.IsNullOrEmpty(backgroundError))
            {
                _lastError = backgroundError;
                Interlocked.Increment(
                    ref _socketErrorCount);

                Debug.LogWarning(
                    $"VCR ARKit/iFacialMocap receiver: {backgroundError}",
                    this);
            }

            if (_rawFrames.TakeLatest() is IFacialMocapFrame raw)
            {
                PublishRawFrame(raw);
            }

            if (_source != null &&
                _source.TryTakeLatest(out var latest))
            {
                _latestFace = latest;
            }

            UpdatePresence();

            if (!_presence.AnySourceAvailable)
            {
                if (_state ==
                    ArKitReceiverLifecycleState.Running)
                {
                    _state =
                        ArKitReceiverLifecycleState.SourceLost;
                }

                if (Time.unscaledTime >=
                    _nextHandshakeTime)
                {
                    SendHandshake();
                }
            }
            else if (_state ==
                     ArKitReceiverLifecycleState.SourceLost)
            {
                _state =
                    ArKitReceiverLifecycleState.Running;
                _lastError = null;
            }
        }

        public bool TrySetControlEnabled(
            bool enabledValue,
            out string error)
        {
            error = null;
            enabled = enabledValue;

            if (enabledValue &&
                !enabled)
            {
                error =
                    _lastError ??
                    "ARKit receiver could not be enabled.";
                return false;
            }

            return true;
        }

        public bool TryRecover(
            out string error)
        {
            error = null;

            if (!Application.isPlaying)
            {
                error =
                    "Tracking recovery requires play mode.";
                return false;
            }

            if (!enabled)
            {
                enabled = true;

                if (!enabled)
                {
                    error =
                        _lastError ??
                        "ARKit receiver recovery failed.";
                    return false;
                }

                return true;
            }

            StartReceiver();

            if (!enabled ||
                _state ==
                    ArKitReceiverLifecycleState.Faulted)
            {
                error =
                    _lastError ??
                    "ARKit receiver recovery failed.";
                return false;
            }

            return true;
        }

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            if (_source == null ||
                (region &
                 (TrackingRegion.Face |
                  TrackingRegion.Head)) == 0)
            {
                snapshot = default;
                return false;
            }

            snapshot =
                new TrackingSourceHealthSnapshot(
                    _source.SourceId,
                    _source.Kind,
                    _source.Regions,
                    _source.Health,
                    _latestFace?
                        .RuntimeTimestampUs ?? 0);
            return true;
        }

        public bool TryGetLatestFace(out TrackingFrame frame)
        {
            frame = _latestFace;
            return frame != null;
        }

        public bool TryGetLatestBodyHands(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }


        [ContextMenu("Send iFacialMocap Handshake")]
        public void SendHandshake()
        {
            _nextHandshakeTime =
                Time.unscaledTime + handshakeRetrySeconds;

            if (_iosEndpoint == null)
            {
                return;
            }

            try
            {
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                var bytes = Encoding.UTF8.GetBytes(
                    IFacialMocapFrameParser.StartStreamingV2Command);
                sender.Send(bytes, bytes.Length, _iosEndpoint);
                Interlocked.Increment(
                    ref _handshakeCount);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"VCR: failed to send iFacialMocap handshake: {exception.Message}",
                    this);
            }
        }

        private void StartReceiver()
        {
            if (!StopReceiver())
            {
                _lastError =
                    "Previous ARKit receive thread did not stop cleanly.";
                _state =
                    ArKitReceiverLifecycleState.Faulted;
                Debug.LogError(
                    "VCR ARKit/iFacialMocap: " +
                    _lastError,
                    this);
                enabled = false;
                return;
            }

            _state =
                ArKitReceiverLifecycleState.Starting;
            _lastError = null;

            if (!IPAddress.TryParse(
                iosIPv4Address,
                out var iosAddress) ||
                iosAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                _lastError =
                    "Enter the iPhone/iPad IPv4 address before enabling the receiver.";
                _state =
                    ArKitReceiverLifecycleState.Faulted;

                Debug.LogWarning(
                    "VCR ARKit/iFacialMocap: " +
                    _lastError,
                    this);
                enabled = false;
                return;
            }

            _iosEndpoint = new IPEndPoint(iosAddress, remotePort);
            _clock.Restart();

            _source = new ArKitFaceSource("arkit-ifacialmocap-udp");
            _source.Start();

            _presenceResolver = new TrackingPresenceResolver(
                subjectLostGraceUs: 0,
                subjectRestoreStabilityUs:
                    SecondsToMicroseconds(restoreStabilitySeconds),
                sourceStaleUs:
                    SecondsToMicroseconds(sourceStaleSeconds));
            _presenceResolver.Reset(0);
            _presence = _presenceResolver.Snapshot;

            try
            {
                _receiver = new UdpClient(
                    new IPEndPoint(IPAddress.Any, localPort));
                _receiver.Client.ReceiveTimeout = 250;
            }
            catch (Exception exception)
            {
                _source.MarkSourceLost(exception.Message);
                _lastError = exception.Message;
                _state =
                    ArKitReceiverLifecycleState.Faulted;

                Debug.LogError(
                    $"VCR ARKit/iFacialMocap: failed to bind UDP {localPort}: {exception.Message}",
                    this);
                enabled = false;
                return;
            }

            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "VCR iFacialMocap UDP"
            };
            _receiveThread.Start();

            _state =
                ArKitReceiverLifecycleState.Running;
            _nextHandshakeTime = 0f;
            SendHandshake();
        }

        private void ReceiveLoop()
        {
            var receiver =
                _receiver;
            var acceptedAddress =
                _iosEndpoint?.Address;

            if (receiver == null)
            {
                return;
            }

            var socket =
                receiver.Client;
            EndPoint remote =
                new IPEndPoint(
                    IPAddress.Any,
                    0);
            var bytes =
                new byte[
                    UdpReceiveBufferBytes];
            var textCharacters =
                new char[
                    IFacialMocapFrameParser
                        .MaxTextCharacters];

            while (_running)
            {
                try
                {
                    var byteCount =
                        socket.ReceiveFrom(
                            bytes,
                            0,
                            bytes.Length,
                            SocketFlags.None,
                            ref remote);

                    if (byteCount <= 0 ||
                        remote is not
                            IPEndPoint remoteIp)
                    {
                        continue;
                    }

                    Interlocked.Increment(
                        ref _datagramCount);

                    if (byteCount >
                        MaxDatagramBytes)
                    {
                        Interlocked.Increment(
                            ref _oversizedDatagramCount);
                        Interlocked.Increment(
                            ref _parseFailureCount);
                        continue;
                    }

                    if (acceptedAddress != null &&
                        !remoteIp.Address.Equals(
                            acceptedAddress))
                    {
                        Interlocked.Increment(
                            ref _rejectedSenderCount);
                        continue;
                    }

                    int textLength;

                    try
                    {
                        textLength =
                            StrictUtf8.GetChars(
                                bytes,
                                0,
                                byteCount,
                                textCharacters,
                                0);
                    }
                    catch (DecoderFallbackException)
                    {
                        Interlocked.Increment(
                            ref _invalidUtf8Count);
                        Interlocked.Increment(
                            ref _parseFailureCount);
                        continue;
                    }

                    if (textLength <= 0 ||
                        textLength >
                            IFacialMocapFrameParser
                                .MaxTextCharacters)
                    {
                        Interlocked.Increment(
                            ref _parseFailureCount);
                        continue;
                    }

                    if (IFacialMocapFrameParser.TryParse(
                        textCharacters.AsSpan(
                            0,
                            textLength),
                        out var frame))
                    {
                        _rawFrames.Publish(frame);
                        Interlocked.Increment(ref _packetCount);
                    }
                    else
                    {
                        Interlocked.Increment(
                            ref _parseFailureCount);
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

        private void PublishRawFrame(IFacialMocapFrame raw)
        {
            if (_source == null)
            {
                return;
            }

            var rotation = raw.HasHead
                ? Quaternion.Euler(
                    raw.HeadEulerXDegrees * headEulerSigns.x,
                    raw.HeadEulerYDegrees * headEulerSigns.y,
                    raw.HeadEulerZDegrees * headEulerSigns.z)
                : Quaternion.identity;

            var position = TrackingVector3.Zero;
            if (useHeadPosition && raw.HasHead)
            {
                position = new TrackingVector3(
                    raw.HeadPositionX * headPositionSigns.x *
                        headPositionScale,
                    raw.HeadPositionY * headPositionSigns.y *
                        headPositionScale,
                    raw.HeadPositionZ * headPositionSigns.z *
                        headPositionScale);
            }

            var normalized =
                new NormalizedFaceState(
                    new TrackingQuaternion(
                        rotation.x,
                        rotation.y,
                        rotation.z,
                        rotation.w),
                    position,
                    raw.DetachCoefficientOwnership(),
                    SnapshotArrayOwnership.Transfer);

            _source.Publish(
                normalized,
                _clock.ElapsedMilliseconds * 1000L);
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var nowUs = _clock.ElapsedMilliseconds * 1000L;
            _presence = _presenceResolver.Update(
                nowUs,
                _latestFace,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            if (_presence.HasEvent(
                    TrackingPresenceEvents.TrackingSourceLost) &&
                _source != null)
            {
                _source.MarkSourceLost();
            }
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.state",
                (int)_state,
                "enum"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.datagrams",
                Interlocked.Read(ref _datagramCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.packets",
                PacketCount,
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.rejected_sender",
                Interlocked.Read(ref _rejectedSenderCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.parse_failures",
                Interlocked.Read(ref _parseFailureCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.oversized_datagrams",
                Interlocked.Read(
                    ref _oversizedDatagramCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.invalid_utf8",
                Interlocked.Read(
                    ref _invalidUtf8Count),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.handshakes",
                Interlocked.Read(ref _handshakeCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.arkit.ifacialmocap.socket_errors",
                Interlocked.Read(ref _socketErrorCount),
                "count"));
        }

        private bool StopReceiver()
        {
            _running = false;

            var receiver =
                _receiver;
            _receiver = null;

            try
            {
                receiver?.Close();
            }
            catch
            {
                // Shutdown path.
            }

            var thread =
                _receiveThread;

            if (thread != null &&
                thread.IsAlive &&
                Thread.CurrentThread != thread)
            {
                thread.Join(750);
            }

            if (thread != null &&
                thread.IsAlive)
            {
                Interlocked.Exchange(
                    ref _backgroundError,
                    "ARKit receive thread did not stop within the shutdown deadline.");
                _state =
                    ArKitReceiverLifecycleState.Faulted;
                return false;
            }

            _receiveThread = null;
            _rawFrames.Clear();

            try
            {
                _source?.Dispose();
            }
            catch
            {
                // Shutdown path.
            }

            _source = null;
            _latestFace = null;
            _iosEndpoint = null;

            if (_state !=
                ArKitReceiverLifecycleState.Faulted)
            {
                _state =
                    ArKitReceiverLifecycleState.Stopped;
            }

            return true;
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void OnDestroy()
        {
            StopReceiver();
        }

        private static long SecondsToMicroseconds(float seconds)
        {
            return (long)(Math.Max(0f, seconds) * 1_000_000.0);
        }
    }
}
