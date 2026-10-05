using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Protocols.Vmc;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Protocols.VmcUnity
{
    /// <summary>
    /// OSC/UDP VMC Marionette receiver.
    ///
    /// Default security posture is loopback-only sender acceptance. Set the
    /// allowed sender address explicitly for LAN performer applications.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VmcUdpReceiver :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingPresenceProvider,
        ITrackingSourceHealthProvider,
        ITrackingRuntimeControl,
        IRuntimeMetricsSource
    {
        private const int UdpReceiveBufferBytes =
            65535;

        [Header("Receive")]
        [SerializeField, Range(1, 65535)] private int localPort = 39539;
        [Tooltip("Default is loopback-only. Blank accepts any sender and is not recommended outside a trusted LAN.")]
        [SerializeField] private string allowedSenderIPv4Address = "127.0.0.1";

        [Header("Compatibility")]
        [Tooltip("VMC Protocol defaults to original/non-normalized VRM1 bones. Enable only for senders explicitly configured to transmit normalized ControlRig bones.")]
        [SerializeField] private bool incomingBonesAreNormalized = false;

        [Header("Presence")]
        [SerializeField, Min(0.1f)] private float sourceStaleSeconds = 1.0f;
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        [Header("Diagnostics")]
        [SerializeField] private bool logStateChanges = true;

        private VmcTrackingSource _source;
        private UdpClient _receiver;
        private Thread _receiveThread;
        private volatile bool _running;

        private IPAddress _allowedSender;
        private long _lastPacketArrivalUs;
        private long _packetCount;
        private long _malformedPacketCount;
        private string _backgroundError;

        private TrackingFrame _latestPoseFrame;
        private TrackingFrame _latestExpressionFrame;

        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        public string ControlId => "vmc-udp";
        public string DisplayName => "VMC UDP";
        public bool ControlEnabled => enabled;
        public TrackingSourceHealthState ControlHealthState =>
            _source?.Health.State ??
            (enabled
                ? TrackingSourceHealthState.Starting
                : TrackingSourceHealthState.Stopped);
        public string ControlError =>
            _source?.Health.Error ??
            _backgroundError;

        public ITrackingSource TrackingSource => _source;
        public TrackingPresenceSnapshot Presence => _presence;
        public int LocalPort => localPort;
        public long PacketCount => Interlocked.Read(ref _packetCount);
        public long MalformedPacketCount =>
            Interlocked.Read(ref _malformedPacketCount);

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                StartReceiver();
            }
        }

        private void Update()
        {
            var error = Interlocked.Exchange(
                ref _backgroundError,
                null);

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning(
                    $"VCR VMC receiver: {error}",
                    this);
            }

            if (_source != null)
            {
                if (_source.TryTakeLatestPose(out var poseFrame))
                {
                    _latestPoseFrame = poseFrame;
                }

                if (_source.TryTakeLatestExpressions(
                        out var expressionFrame))
                {
                    _latestExpressionFrame = expressionFrame;
                }

                // Drain status-only frames so the source's single-slot
                // compatibility buffer cannot retain old control state.
                _source.TryTakeLatest(out _);
            }

            UpdatePresence();
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
                    ControlError ??
                    "VMC receiver could not be enabled.";
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
                        ControlError ??
                        "VMC receiver recovery failed.";
                    return false;
                }

                return true;
            }

            StartReceiver();

            if (!enabled)
            {
                error =
                    ControlError ??
                    "VMC receiver recovery failed.";
                return false;
            }

            return true;
        }

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            var supported =
                TrackingRegion.FullBody |
                TrackingRegion.Expressions;

            if (_source == null ||
                (region & supported) == 0)
            {
                snapshot = default;
                return false;
            }

            var poseTimestamp =
                _latestPoseFrame?
                    .RuntimeTimestampUs ?? 0;
            var expressionTimestamp =
                _latestExpressionFrame?
                    .RuntimeTimestampUs ?? 0;

            var runtimeTimestamp =
                (region &
                 TrackingRegion.Expressions) != 0
                    ? expressionTimestamp
                    : poseTimestamp;

            snapshot =
                new TrackingSourceHealthSnapshot(
                    _source.SourceId,
                    _source.Kind,
                    _source.Regions,
                    _source.Health,
                    runtimeTimestamp);
            return true;
        }

        public bool TryGetLatestFace(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestBodyHands(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(out TrackingFrame frame)
        {
            frame = _latestPoseFrame;
            return frame != null;
        }

        public bool TryGetLatestExpressions(out TrackingFrame frame)
        {
            frame = _latestExpressionFrame;
            return frame != null;
        }

        private void StartReceiver()
        {
            if (!StopReceiver())
            {
                Debug.LogError(
                    "VCR VMC receiver: previous receive thread did not stop cleanly; refusing to start an overlapping receiver.",
                    this);
                enabled = false;
                return;
            }

            _allowedSender = null;
            if (!string.IsNullOrWhiteSpace(
                allowedSenderIPv4Address))
            {
                if (!IPAddress.TryParse(
                    allowedSenderIPv4Address,
                    out _allowedSender) ||
                    _allowedSender.AddressFamily !=
                        AddressFamily.InterNetwork)
                {
                    Debug.LogError(
                        "VCR VMC: allowed sender must be a valid IPv4 address or blank.",
                        this);
                    enabled = false;
                    return;
                }
            }

            var senders =
                FindObjectsByType<VmcUdpSender>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var sender in senders)
            {
                if (sender != null &&
                    sender.isActiveAndEnabled &&
                    sender.IsLoopbackDestination &&
                    sender.RemotePort == localPort)
                {
                    Debug.LogError(
                        "VCR VMC receiver: an active VMC sender already targets this process's loopback listen port. " +
                        "Use distinct ports to avoid a self-feedback loop.",
                        this);
                    enabled = false;
                    return;
                }
            }

            _lastPacketArrivalUs = -1;

            _source = new VmcTrackingSource(
                "vmc-udp",
                incomingBonesAreNormalized
                    ? HumanoidPoseSpace.NormalizedLocal
                    : HumanoidPoseSpace.OriginalLocal);
            _source.Start();

            _presenceResolver = new TrackingPresenceResolver(
                SecondsToMicroseconds(subjectLostGraceSeconds),
                SecondsToMicroseconds(subjectRestoreStabilitySeconds),
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
                Debug.LogError(
                    $"VCR VMC: failed to bind UDP {localPort}: {exception.Message}",
                    this);
                enabled = false;
                return;
            }

            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "VCR VMC UDP"
            };
            _receiveThread.Start();

            Debug.Log(
                $"VCR VMC receiver listening on UDP {localPort}; " +
                $"sender={allowedSenderIPv4Address}",
                this);
        }

        private void ReceiveLoop()
        {
            var receiver =
                _receiver;

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
            var packet =
                new byte[
                    UdpReceiveBufferBytes];
            var messages =
                new List<OscMessage>(
                    64);

            while (_running)
            {
                try
                {
                    var packetLength =
                        socket.ReceiveFrom(
                            packet,
                            0,
                            packet.Length,
                            SocketFlags.None,
                            ref remote);

                    if (packetLength <= 0 ||
                        remote is not
                            IPEndPoint remoteIp)
                    {
                        continue;
                    }

                    if (_allowedSender != null &&
                        !remoteIp.Address.Equals(
                            _allowedSender))
                    {
                        continue;
                    }

                    var arrivalUs =
                        MonotonicClock
                            .NowMicroseconds();
                    Interlocked.Exchange(
                        ref _lastPacketArrivalUs,
                        arrivalUs);
                    Interlocked.Increment(
                        ref _packetCount);

                    if (!OscPacketReader
                        .TryReadMessages(
                            packet,
                            packetLength,
                            messages))
                    {
                        Interlocked.Increment(
                            ref _malformedPacketCount);
                        continue;
                    }

                    if (!_running)
                    {
                        return;
                    }

                    var source =
                        _source;

                    if (source == null)
                    {
                        return;
                    }

                    source.Process(
                        messages,
                        arrivalUs);
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

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var nowUs = MonotonicClock.NowMicroseconds();
            var lastArrival =
                Interlocked.Read(ref _lastPacketArrivalUs);
            var staleUs =
                SecondsToMicroseconds(sourceStaleSeconds);

            var transportAvailable =
                lastArrival >= 0 &&
                nowUs - lastArrival <= staleUs;

            var poseAvailable =
                transportAvailable &&
                _latestPoseFrame != null &&
                _latestPoseFrame.RuntimeTimestampUs > 0 &&
                nowUs - _latestPoseFrame.RuntimeTimestampUs <= staleUs;

            var subjectEvidence =
                poseAvailable &&
                _source != null &&
                _source.LastSubjectDetected;

            var previous = _presence;

            _presence = _presenceResolver.UpdateResolved(
                nowUs,
                faceConfigured: false,
                faceSourceAvailable: false,
                faceSubjectEvidence: false,
                bodyHandsConfigured: false,
                bodyHandsSourceAvailable: false,
                bodyHandsSubjectEvidence: false,
                sourceDecisionReady:
                    lastArrival >= 0 ||
                    nowUs >= staleUs,
                fullBodyConfigured: true,
                fullBodySourceAvailable: poseAvailable,
                fullBodySubjectEvidence: subjectEvidence);

            if (_presence.HasEvent(
                    TrackingPresenceEvents.TrackingSourceLost) &&
                _source != null)
            {
                _source.MarkSourceLost(
                    transportAvailable
                        ? "VMC pose stream became stale."
                        : "VMC UDP transport became stale.");
            }

            if (logStateChanges &&
                _presence.Sequence != previous.Sequence)
            {
                Debug.Log(
                    $"VCR VMC presence: state={_presence.SubjectState}, " +
                    $"events={_presence.Events}, transport={(transportAvailable ? "up" : "down")}, " +
                    $"pose={(poseAvailable ? "fresh" : "stale")}",
                    this);
            }
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "protocol.vmc.receive.packets",
                PacketCount,
                "count"));

            output.Add(new RuntimeMetric(
                "protocol.vmc.receive.malformed",
                MalformedPacketCount,
                "count"));
        }

        private bool StopReceiver()
        {
            _running = false;

            var source =
                _source;

            try
            {
                source?.Stop();
            }
            catch
            {
                // Best-effort shutdown. Dispose is deferred until the
                // receive thread has definitely exited.
            }

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
                    "VMC receive thread did not stop within the shutdown deadline.");
                return false;
            }

            _receiveThread = null;

            try
            {
                source?.Dispose();
            }
            catch
            {
                // Shutdown path.
            }

            if (ReferenceEquals(
                    _source,
                    source))
            {
                _source = null;
            }

            _latestPoseFrame = null;
            _latestExpressionFrame = null;
            _lastPacketArrivalUs = -1;
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
            return (long)(
                Math.Max(0f, seconds) * 1_000_000.0);
        }
    }
}
