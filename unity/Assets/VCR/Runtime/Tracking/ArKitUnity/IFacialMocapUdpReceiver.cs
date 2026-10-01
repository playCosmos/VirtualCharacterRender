using System;
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
        ITrackingPresenceProvider
    {
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

        private long _packetCount;
        private float _nextHandshakeTime;
        private string _backgroundError;

        public TrackingPresenceSnapshot Presence => _presence;
        public long PacketCount => Interlocked.Read(ref _packetCount);
        public ITrackingSource FaceTrackingSource => _source;

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

            if (!_presence.AnySourceAvailable &&
                Time.unscaledTime >= _nextHandshakeTime)
            {
                SendHandshake();
            }
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
            StopReceiver();

            if (!IPAddress.TryParse(
                iosIPv4Address,
                out var iosAddress) ||
                iosAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                Debug.LogWarning(
                    "VCR ARKit/iFacialMocap: enter the iPhone/iPad IPv4 address before enabling the receiver.",
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

            _nextHandshakeTime = 0f;
            SendHandshake();
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    var bytes = _receiver.Receive(ref remote);
                    if (bytes == null || bytes.Length == 0)
                    {
                        continue;
                    }

                    if (_iosEndpoint != null &&
                        !remote.Address.Equals(_iosEndpoint.Address))
                    {
                        continue;
                    }

                    var text = Encoding.UTF8.GetString(bytes);
                    if (IFacialMocapFrameParser.TryParse(
                        text,
                        out var frame))
                    {
                        _rawFrames.Publish(frame);
                        Interlocked.Increment(ref _packetCount);
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

            var normalized = new NormalizedFaceState(
                new TrackingQuaternion(
                    rotation.x,
                    rotation.y,
                    rotation.z,
                    rotation.w),
                position,
                raw.Coefficients);

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

            _latestFace = null;
            _iosEndpoint = null;
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
