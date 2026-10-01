using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
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
        IRuntimeMetricsSource
    {
        [Header("Receive")]
        [SerializeField, Range(1, 65535)] private int localPort = 39539;
        [Tooltip("Default is loopback-only. Blank accepts any sender and is not recommended outside a trusted LAN.")]
        [SerializeField] private string allowedSenderIPv4Address = "127.0.0.1";

        [Header("Presence")]
        [SerializeField, Min(0.1f)] private float sourceStaleSeconds = 1.0f;
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;

        [Header("Diagnostics")]
        [SerializeField] private bool logStateChanges = true;

        private readonly Stopwatch _clock = new();

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

            if (_source != null &&
                _source.TryTakeLatest(out var frame))
            {
                if (frame.HumanoidPose != null)
                {
                    _latestPoseFrame = frame;
                }

                if (frame.Expressions != null)
                {
                    _latestExpressionFrame = frame;
                }
            }

            UpdatePresence();
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

            _clock.Restart();
            _lastPacketArrivalUs = -1;

            _source = new VmcTrackingSource("vmc-udp");
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
                        _clock.ElapsedMilliseconds * 1000L;
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

            var nowUs =
                _clock.ElapsedMilliseconds * 1000L;
            var lastArrival =
                Interlocked.Read(ref _lastPacketArrivalUs);

            var sourceAvailable =
                lastArrival >= 0 &&
                nowUs - lastArrival <=
                    SecondsToMicroseconds(sourceStaleSeconds);

            var subjectEvidence =
                sourceAvailable &&
                ((_latestPoseFrame != null &&
                  _latestPoseFrame.SubjectDetected) ||
                 (_latestExpressionFrame != null &&
                  _latestExpressionFrame.SubjectDetected));

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
                    nowUs >=
                        SecondsToMicroseconds(sourceStaleSeconds),
                fullBodyConfigured: true,
                fullBodySourceAvailable: sourceAvailable,
                fullBodySubjectEvidence: subjectEvidence);

            if (_presence.HasEvent(
                    TrackingPresenceEvents.TrackingSourceLost) &&
                _source != null)
            {
                _source.MarkSourceLost();
            }

            if (logStateChanges &&
                _presence.Sequence != previous.Sequence)
            {
                Debug.Log(
                    $"VCR VMC presence: state={_presence.SubjectState}, " +
                    $"events={_presence.Events}",
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
