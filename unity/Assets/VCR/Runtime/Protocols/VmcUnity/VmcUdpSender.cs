using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using VCR.Runtime.Protocols.Osc;

namespace VCR.Runtime.Protocols.VmcUnity
{
    /// <summary>
    /// VMC Performer sender using normalized character-motion snapshots.
    /// Protocol code never reads Vrm10Instance directly.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20000)]
    public sealed class VmcUdpSender : MonoBehaviour
    {
        [Header("Snapshot")]
        [SerializeField] private MonoBehaviour snapshotProviderBehaviour;
        [SerializeField] private bool autoFindSnapshotProvider = true;

        [Header("Destination")]
        [SerializeField] private string remoteIPv4Address = "127.0.0.1";
        [SerializeField, Range(1, 65535)] private int remotePort = 39539;
        [SerializeField, Range(1, 120)] private int sendRateHz = 60;

        [Header("Compatibility")]
        [Tooltip("Default false follows VMC guidance: VRM1 runtimes transmit VRM0 preset expression names for compatibility.")]
        [SerializeField] private bool sendVrm1ExpressionNames = false;
        [SerializeField] private bool sendExpressions = true;
        [SerializeField] private bool sendRoot = true;

        [Header("Diagnostics")]
        [SerializeField] private bool logErrors = true;

        private INormalizedMotionSnapshotProvider _snapshotProvider;
        private UdpClient _client;
        private IPEndPoint _endpoint;
        private double _nextSendAt;

        private long _packetCount;
        private long _errorCount;

        public long PacketCount => _packetCount;
        public long ErrorCount => _errorCount;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveProvider();
            OpenClient();
            _nextSendAt = Time.realtimeSinceStartupAsDouble;
        }

        private void LateUpdate()
        {
            if (_client == null || _endpoint == null)
            {
                return;
            }

            if (_snapshotProvider == null)
            {
                ResolveProvider();
                if (_snapshotProvider == null)
                {
                    return;
                }
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextSendAt)
            {
                return;
            }

            var interval = 1.0 / Math.Max(1, sendRateHz);
            _nextSendAt = now + interval;

            if (!_snapshotProvider.TryCaptureMotion(out var frame) ||
                frame?.HumanoidPose == null)
            {
                return;
            }

            try
            {
                var packet = BuildBundle(frame, (float)now);
                _client.Send(
                    packet,
                    packet.Length,
                    _endpoint);
                _packetCount++;
            }
            catch (Exception exception)
            {
                _errorCount++;

                if (logErrors)
                {
                    Debug.LogWarning(
                        $"VCR VMC sender: {exception.Message}",
                        this);
                }
            }
        }

        private byte[] BuildBundle(
            TrackingFrame frame,
            float relativeTime)
        {
            var messages = new List<byte[]>(96);

            messages.Add(
                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/OK",
                    OscArgument.FromInt(1),
                    OscArgument.FromInt(3),
                    OscArgument.FromInt(0),
                    OscArgument.FromInt(1)));

            messages.Add(
                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/T",
                    OscArgument.FromFloat(relativeTime)));

            var pose = frame.HumanoidPose;

            if (sendRoot)
            {
                messages.Add(
                    WriteTransformMessage(
                        "/VMC/Ext/Root/Pos",
                        "root",
                        pose.RootPosition,
                        pose.RootRotation));
            }

            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var bone = (HumanoidBoneId)i;
                if (!pose.TryGet(bone, out var bonePose))
                {
                    continue;
                }

                var name = HumanoidBoneNames.GetCanonical(bone);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                messages.Add(
                    WriteTransformMessage(
                        "/VMC/Ext/Bone/Pos",
                        name,
                        bonePose.LocalPosition,
                        bonePose.LocalRotation));
            }

            if (sendExpressions &&
                frame.Expressions != null)
            {
                AppendExpressions(
                    messages,
                    frame.Expressions);

                messages.Add(
                    OscPacketWriter.WriteMessage(
                        "/VMC/Ext/Blend/Apply"));
            }

            return OscPacketWriter.WriteBundle(messages);
        }

        private void AppendExpressions(
            List<byte[]> messages,
            NormalizedExpressionState state)
        {
            for (var i = 0;
                 i < (int)StandardExpression.Count;
                 i++)
            {
                var expression = (StandardExpression)i;

                var name = sendVrm1ExpressionNames
                    ? StandardExpressionNames.GetVrm1Name(expression)
                    : StandardExpressionNames.GetVmcVrm0Name(expression);

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                messages.Add(
                    OscPacketWriter.WriteMessage(
                        "/VMC/Ext/Blend/Val",
                        OscArgument.FromString(name),
                        OscArgument.FromFloat(
                            Mathf.Clamp01(
                                state.Get(expression)))));
            }

            foreach (var custom in state.Custom)
            {
                if (string.IsNullOrEmpty(custom.Name))
                {
                    continue;
                }

                messages.Add(
                    OscPacketWriter.WriteMessage(
                        "/VMC/Ext/Blend/Val",
                        OscArgument.FromString(custom.Name),
                        OscArgument.FromFloat(
                            Mathf.Clamp01(custom.Value))));
            }
        }

        private static byte[] WriteTransformMessage(
            string address,
            string name,
            TrackingVector3 position,
            TrackingQuaternion rotation)
        {
            return OscPacketWriter.WriteMessage(
                address,
                OscArgument.FromString(name),
                OscArgument.FromFloat(position.X),
                OscArgument.FromFloat(position.Y),
                OscArgument.FromFloat(position.Z),
                OscArgument.FromFloat(rotation.X),
                OscArgument.FromFloat(rotation.Y),
                OscArgument.FromFloat(rotation.Z),
                OscArgument.FromFloat(rotation.W));
        }

        private void ResolveProvider()
        {
            if (snapshotProviderBehaviour is
                INormalizedMotionSnapshotProvider configured)
            {
                _snapshotProvider = configured;
                return;
            }

            if (!autoFindSnapshotProvider)
            {
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    INormalizedMotionSnapshotProvider provider)
                {
                    _snapshotProvider = provider;
                    snapshotProviderBehaviour = behaviour;
                    return;
                }
            }
        }

        private void OpenClient()
        {
            CloseClient();

            if (!IPAddress.TryParse(
                remoteIPv4Address,
                out var address) ||
                address.AddressFamily !=
                    AddressFamily.InterNetwork)
            {
                Debug.LogError(
                    "VCR VMC sender: destination must be an IPv4 address.",
                    this);
                enabled = false;
                return;
            }

            _endpoint =
                new IPEndPoint(address, remotePort);
            _client =
                new UdpClient(AddressFamily.InterNetwork);
        }

        private void CloseClient()
        {
            try
            {
                _client?.Close();
            }
            catch
            {
                // Shutdown path.
            }

            _client = null;
            _endpoint = null;
        }

        private void OnDisable()
        {
            CloseClient();
        }

        private void OnDestroy()
        {
            CloseClient();
        }
    }
}
