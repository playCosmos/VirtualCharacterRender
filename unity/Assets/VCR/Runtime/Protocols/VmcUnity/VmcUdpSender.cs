using System;
using System.Collections.Generic;
using VCR.Runtime.Core;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Protocols.VmcUnity
{
    /// <summary>
    /// VMC Performer sender using normalized character-motion snapshots.
    /// Protocol code never reads Vrm10Instance directly.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20000)]
    public sealed class VmcUdpSender : MonoBehaviour, IRuntimeMetricsSource
    {
        [Header("Snapshot")]
        [SerializeField] private MonoBehaviour snapshotProviderBehaviour;
        [SerializeField] private bool autoFindSnapshotProvider = true;
        [SerializeField, Min(0.25f)] private float providerResolveIntervalSeconds = 1f;

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

        private readonly byte[] _packetScratch =
            new byte[
                OscPacketReader.MaxPacketBytes];
        private readonly OscArgument[] _argumentScratch =
            new OscArgument[8];

        private INormalizedMotionSnapshotProvider _snapshotProvider;
        private UdpClient _client;
        private IPEndPoint _endpoint;
        private double _nextSendAt;
        private float _nextProviderResolveTime;

        private long _packetCount;
        private long _errorCount;
        private long _borrowedMotionPacketCount;
        private long _borrowedPosePacketCount;
        private long _snapshotPosePacketCount;

        public long PacketCount => _packetCount;
        public long ErrorCount => _errorCount;
        public long BorrowedMotionPacketCount =>
            _borrowedMotionPacketCount;
        public long BorrowedPosePacketCount =>
            _borrowedPosePacketCount;
        public long SnapshotPosePacketCount =>
            _snapshotPosePacketCount;
        public int RemotePort => remotePort;

        public bool IsLoopbackDestination
        {
            get
            {
                return
                    IPAddress.TryParse(
                        remoteIPv4Address,
                        out var address) &&
                    IPAddress.IsLoopback(address);
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _nextProviderResolveTime = 0f;
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

            if (!IsServiceAlive(
                    _snapshotProvider))
            {
                _snapshotProvider = null;

                if (!autoFindSnapshotProvider ||
                    Time.unscaledTime <
                        _nextProviderResolveTime)
                {
                    return;
                }

                _nextProviderResolveTime =
                    Time.unscaledTime +
                    Mathf.Max(
                        0.25f,
                        providerResolveIntervalSeconds);
                ResolveProvider();

                if (!IsServiceAlive(
                        _snapshotProvider))
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

            try
            {
                int packetLength;

                if (_snapshotProvider is
                    IBorrowedNormalizedMotionProvider borrowedMotionProvider)
                {
                    var request =
                        new NormalizedMotionSnapshotRequest(
                            includeHumanoidPose: true,
                            includeExpressions:
                                sendExpressions);

                    if (!borrowedMotionProvider.TryBorrowMotion(
                            in request,
                            out var borrowedMotion) ||
                        !borrowedMotion.HasHumanoidPose ||
                        (sendExpressions &&
                         !borrowedMotion.HasExpressions))
                    {
                        return;
                    }

                    packetLength =
                        BuildBundle(
                            in borrowedMotion,
                            (float)now);
                    _borrowedMotionPacketCount++;
                }
                else
                {
                    var selective =
                        _snapshotProvider as
                            ISelectiveNormalizedMotionSnapshotProvider;
                    var borrowedProvider =
                        _snapshotProvider as
                            IBorrowedHumanoidPoseProvider;
                    var canUseBorrowedPose =
                        borrowedProvider != null &&
                        (!sendExpressions ||
                         selective != null);

                    if (canUseBorrowedPose)
                    {
                        NormalizedExpressionState expressions =
                            null;

                        if (sendExpressions)
                        {
                            var expressionRequest =
                                new NormalizedMotionSnapshotRequest(
                                    includeHumanoidPose: false,
                                    includeExpressions: true);

                            if (!selective.TryCaptureMotion(
                                    in expressionRequest,
                                    out var expressionFrame))
                            {
                                return;
                            }

                            expressions =
                                expressionFrame?
                                    .Expressions;
                        }

                        // Borrow last. The provider may invalidate a borrowed
                        // pose on any subsequent borrow/capture call.
                        if (!borrowedProvider.TryBorrowHumanoidPose(
                                out var borrowedPose))
                        {
                            return;
                        }

                        packetLength =
                            BuildBundle(
                                in borrowedPose,
                                expressions,
                                (float)now);
                        _borrowedPosePacketCount++;
                    }
                    else
                    {
                        TrackingFrame frame;
                        bool captured;

                        if (selective != null)
                        {
                            var request =
                                new NormalizedMotionSnapshotRequest(
                                    includeHumanoidPose: true,
                                    includeExpressions:
                                        sendExpressions);

                            captured =
                                selective.TryCaptureMotion(
                                    in request,
                                    out frame);
                        }
                        else
                        {
                            captured =
                                _snapshotProvider
                                    .TryCaptureMotion(
                                        out frame);
                        }

                        if (!captured ||
                            frame?.HumanoidPose == null)
                        {
                            return;
                        }

                        packetLength =
                            BuildBundle(
                                frame,
                                (float)now);
                        _snapshotPosePacketCount++;
                    }
                }

                _client.Send(
                    _packetScratch,
                    packetLength,
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

        private int BuildBundle(
            TrackingFrame frame,
            float relativeTime)
        {
            var pose =
                frame?.HumanoidPose ??
                throw new InvalidOperationException(
                    "VMC sender requires a humanoid pose snapshot.");

            var packetLength =
                BeginBundle(
                    relativeTime);

            AppendPose(
                pose,
                ref packetLength);
            AppendOptionalExpressions(
                frame.Expressions,
                ref packetLength);

            return packetLength;
        }

        private int BuildBundle(
            in BorrowedMotionSample sample,
            float relativeTime)
        {
            if (!sample.HasHumanoidPose)
            {
                throw new InvalidOperationException(
                    "VMC sender requires a borrowed humanoid pose.");
            }

            var packetLength =
                BeginBundle(
                    relativeTime);

            var pose =
                sample.HumanoidPose;

            AppendPose(
                in pose,
                ref packetLength);

            if (sendExpressions &&
                sample.HasExpressions)
            {
                var expressions =
                    sample.Expressions;

                AppendExpressions(
                    in expressions,
                    ref packetLength);

                AppendNoArgumentMessage(
                    "/VMC/Ext/Blend/Apply",
                    ref packetLength);
            }

            return packetLength;
        }

        private int BuildBundle(
            in BorrowedHumanoidPose pose,
            NormalizedExpressionState expressions,
            float relativeTime)
        {
            if (!pose.IsValid)
            {
                throw new InvalidOperationException(
                    "VMC sender received an invalid borrowed humanoid pose.");
            }

            var packetLength =
                BeginBundle(
                    relativeTime);

            AppendPose(
                in pose,
                ref packetLength);
            AppendOptionalExpressions(
                expressions,
                ref packetLength);

            return packetLength;
        }

        private int BeginBundle(
            float relativeTime)
        {
            if (!OscPacketWriter.TryBeginBundle(
                    _packetScratch,
                    out var packetLength))
            {
                throw new InvalidOperationException(
                    "VMC OSC bundle buffer is unavailable.");
            }

            AppendStatusMessage(
                ref packetLength);
            AppendTimeMessage(
                relativeTime,
                ref packetLength);

            return packetLength;
        }

        private void AppendPose(
            HumanoidPoseState pose,
            ref int packetLength)
        {
            ValidatePoseSpace(
                pose.PoseSpace);

            if (sendRoot)
            {
                AppendTransformMessage(
                    "/VMC/Ext/Root/Pos",
                    "root",
                    pose.RootPosition,
                    pose.RootRotation,
                    ref packetLength);
            }

            for (var i = 0;
                 i <
                 (int)HumanoidBoneId.Count;
                 i++)
            {
                var bone =
                    (HumanoidBoneId)i;

                if (!pose.TryGet(
                        bone,
                        out var bonePose))
                {
                    continue;
                }

                AppendBoneMessage(
                    bone,
                    bonePose,
                    ref packetLength);
            }
        }

        private void AppendPose(
            in BorrowedHumanoidPose pose,
            ref int packetLength)
        {
            ValidatePoseSpace(
                pose.PoseSpace);

            if (sendRoot)
            {
                AppendTransformMessage(
                    "/VMC/Ext/Root/Pos",
                    "root",
                    pose.RootPosition,
                    pose.RootRotation,
                    ref packetLength);
            }

            for (var i = 0;
                 i <
                 (int)HumanoidBoneId.Count;
                 i++)
            {
                var bone =
                    (HumanoidBoneId)i;

                if (!pose.TryGet(
                        bone,
                        out var bonePose))
                {
                    continue;
                }

                AppendBoneMessage(
                    bone,
                    bonePose,
                    ref packetLength);
            }
        }

        private void AppendBoneMessage(
            HumanoidBoneId bone,
            NormalizedBonePose bonePose,
            ref int packetLength)
        {
            var name =
                HumanoidBoneNames
                    .GetCanonical(
                        bone);

            if (string.IsNullOrEmpty(
                    name))
            {
                return;
            }

            AppendTransformMessage(
                "/VMC/Ext/Bone/Pos",
                name,
                bonePose.LocalPosition,
                bonePose.LocalRotation,
                ref packetLength);
        }

        private static void ValidatePoseSpace(
            HumanoidPoseSpace poseSpace)
        {
            if (poseSpace ==
                HumanoidPoseSpace.OriginalLocal)
            {
                return;
            }

            throw new InvalidOperationException(
                "VMC sender requires OriginalLocal humanoid bones by default. " +
                "Capture original VRM humanoid bones or add an explicit normalized-bone sender mode.");
        }

        private void AppendOptionalExpressions(
            NormalizedExpressionState expressions,
            ref int packetLength)
        {
            if (!sendExpressions ||
                expressions == null)
            {
                return;
            }

            AppendExpressions(
                expressions,
                ref packetLength);

            AppendNoArgumentMessage(
                "/VMC/Ext/Blend/Apply",
                ref packetLength);
        }

        private void AppendExpressions(
            NormalizedExpressionState state,
            ref int packetLength)
        {
            for (var i = 0;
                 i <
                 (int)StandardExpression.Count;
                 i++)
            {
                var expression =
                    (StandardExpression)i;

                var name =
                    sendVrm1ExpressionNames
                        ? StandardExpressionNames
                            .GetVrm1Name(
                                expression)
                        : StandardExpressionNames
                            .GetVmcVrm0Name(
                                expression);

                if (string.IsNullOrEmpty(
                        name))
                {
                    continue;
                }

                AppendBlendValueMessage(
                    name,
                    Mathf.Clamp01(
                        state.Get(
                            expression)),
                    ref packetLength);
            }

            foreach (var custom in
                     state.Custom)
            {
                if (string.IsNullOrEmpty(
                        custom.Name))
                {
                    continue;
                }

                AppendBlendValueMessage(
                    custom.Name,
                    Mathf.Clamp01(
                        custom.Value),
                    ref packetLength);
            }
        }

        private void AppendExpressions(
            in BorrowedExpressionState state,
            ref int packetLength)
        {
            for (var i = 0;
                 i <
                 (int)StandardExpression.Count;
                 i++)
            {
                var expression =
                    (StandardExpression)i;

                var name =
                    sendVrm1ExpressionNames
                        ? StandardExpressionNames
                            .GetVrm1Name(
                                expression)
                        : StandardExpressionNames
                            .GetVmcVrm0Name(
                                expression);

                if (string.IsNullOrEmpty(
                        name))
                {
                    continue;
                }

                AppendBlendValueMessage(
                    name,
                    Mathf.Clamp01(
                        state.Get(
                            expression)),
                    ref packetLength);
            }

            foreach (var custom in
                     state.Custom)
            {
                if (string.IsNullOrEmpty(
                        custom.Name))
                {
                    continue;
                }

                AppendBlendValueMessage(
                    custom.Name,
                    Mathf.Clamp01(
                        custom.Value),
                    ref packetLength);
            }
        }

        private void AppendStatusMessage(
            ref int packetLength)
        {
            _argumentScratch[0] =
                OscArgument.FromInt(1);
            _argumentScratch[1] =
                OscArgument.FromInt(3);
            _argumentScratch[2] =
                OscArgument.FromInt(0);
            _argumentScratch[3] =
                OscArgument.FromInt(1);

            AppendMessage(
                "/VMC/Ext/OK",
                4,
                ref packetLength);
        }

        private void AppendTimeMessage(
            float relativeTime,
            ref int packetLength)
        {
            _argumentScratch[0] =
                OscArgument.FromFloat(
                    relativeTime);

            AppendMessage(
                "/VMC/Ext/T",
                1,
                ref packetLength);
        }

        private void AppendNoArgumentMessage(
            string address,
            ref int packetLength)
        {
            AppendMessage(
                address,
                0,
                ref packetLength);
        }

        private void AppendBlendValueMessage(
            string name,
            float value,
            ref int packetLength)
        {
            _argumentScratch[0] =
                OscArgument.FromString(
                    name);
            _argumentScratch[1] =
                OscArgument.FromFloat(
                    value);

            AppendMessage(
                "/VMC/Ext/Blend/Val",
                2,
                ref packetLength);
        }

        private void AppendTransformMessage(
            string address,
            string name,
            TrackingVector3 position,
            TrackingQuaternion rotation,
            ref int packetLength)
        {
            _argumentScratch[0] =
                OscArgument.FromString(
                    name);
            _argumentScratch[1] =
                OscArgument.FromFloat(
                    position.X);
            _argumentScratch[2] =
                OscArgument.FromFloat(
                    position.Y);
            _argumentScratch[3] =
                OscArgument.FromFloat(
                    position.Z);
            _argumentScratch[4] =
                OscArgument.FromFloat(
                    rotation.X);
            _argumentScratch[5] =
                OscArgument.FromFloat(
                    rotation.Y);
            _argumentScratch[6] =
                OscArgument.FromFloat(
                    rotation.Z);
            _argumentScratch[7] =
                OscArgument.FromFloat(
                    rotation.W);

            AppendMessage(
                address,
                8,
                ref packetLength);
        }

        private void AppendMessage(
            string address,
            int argumentCount,
            ref int packetLength)
        {
            if (!OscPacketWriter
                .TryAppendBundleMessage(
                    _packetScratch,
                    ref packetLength,
                    address,
                    _argumentScratch,
                    argumentCount))
            {
                throw new InvalidOperationException(
                    "VMC OSC bundle exceeds the configured packet limit.");
            }
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "protocol.vmc.send.packets",
                PacketCount,
                "count"));

            output.Add(new RuntimeMetric(
                "protocol.vmc.send.errors",
                ErrorCount,
                "count"));

            output.Add(new RuntimeMetric(
                "protocol.vmc.send.borrowed_motion_packets",
                BorrowedMotionPacketCount,
                "count"));

            output.Add(new RuntimeMetric(
                "protocol.vmc.send.borrowed_pose_packets",
                BorrowedPosePacketCount,
                "count"));

            output.Add(new RuntimeMetric(
                "protocol.vmc.send.snapshot_pose_packets",
                SnapshotPosePacketCount,
                "count"));
        }

        public void SetSnapshotProvider(MonoBehaviour provider)
        {
            snapshotProviderBehaviour =
                provider != null
                    ? provider
                    : null;
            _snapshotProvider =
                provider != null
                    ? provider as
                        INormalizedMotionSnapshotProvider
                    : null;
            _nextProviderResolveTime = 0f;
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

        private void ResolveProvider()
        {
            if (snapshotProviderBehaviour != null &&
                snapshotProviderBehaviour is
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

            if (IsLocalAddress(address))
            {
                var receivers =
                    FindObjectsByType<VmcUdpReceiver>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var receiver in receivers)
                {
                    if (receiver != null &&
                        receiver.enabled &&
                        receiver.LocalPort == remotePort)
                    {
                        Debug.LogError(
                            "VCR VMC sender: destination matches an active local VMC receiver port. " +
                            "This would create a self-loop. Change either the receiver listen port or sender destination port.",
                            this);
                        enabled = false;
                        return;
                    }
                }
            }

            _endpoint =
                new IPEndPoint(address, remotePort);
            _client =
                new UdpClient(AddressFamily.InterNetwork);
        }

        private static bool IsLocalAddress(IPAddress address)
        {
            return
                IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.Loopback);
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
