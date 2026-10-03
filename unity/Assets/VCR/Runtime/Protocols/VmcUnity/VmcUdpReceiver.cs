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
            StopReceiver();

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
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var messages = new List<OscMessage>(64);

            while (_running)
            {
                try
                {
                    var packet = _receiver.Receive(ref remote);
                    if (packet == null || packet.Length == 0)
                    {
                        continue;
                    }

                    if (_allowedSender != null &&
                        !remote.Address.Equals(_allowedSender))
                    {
                        continue;
                    }

                    var arrivalUs =
                        MonotonicClock.NowMicroseconds();
                    Interlocked.Exchange(
                        ref _lastPacketArrivalUs,
                        arrivalUs);
                    Interlocked.Increment(ref _packetCount);

                    if (!OscPacketReader.TryReadMessages(
                        packet,
                        packet.Length,
                        messages))
                    {
                        Interlocked.Increment(
                            ref _malformedPacketCount);
                        continue;
                    }

                    _source.Process(messages, arrivalUs);
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
                _receiveThread.Join(500);
            }

            _receiveThread = null;

            _source?.Dispose();
            _source = null;

            _latestPoseFrame = null;
            _latestExpressionFrame = null;
            _lastPacketArrivalUs = -1;
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
