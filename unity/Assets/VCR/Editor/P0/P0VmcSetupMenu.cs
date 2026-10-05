using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Protocols.Vmc;
using VCR.Runtime.Protocols.VmcUnity;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Runtime.Tracking.Routing;

namespace VCR.Editor.P0
{
    public static class P0VmcSetupMenu
    {
        [MenuItem("VCR/P0/Validate OSC and VMC Codec")]
        public static void ValidateCodec()
        {
            var messages = new List<byte[]>
            {
                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/OK",
                    OscArgument.FromInt(1),
                    OscArgument.FromInt(3),
                    OscArgument.FromInt(0),
                    OscArgument.FromInt(1)),

                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/T",
                    OscArgument.FromFloat(1.25f)),

                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/Root/Pos",
                    OscArgument.FromString("root"),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(1f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(1f)),

                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/Bone/Pos",
                    OscArgument.FromString("Hips"),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0.9f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(0f),
                    OscArgument.FromFloat(1f)),

                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/Blend/Val",
                    OscArgument.FromString("Joy"),
                    OscArgument.FromFloat(0.6f)),

                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/Blend/Apply")
            };

            var packet = OscPacketWriter.WriteBundle(messages);
            var decoded = new List<OscMessage>();

            if (!OscPacketReader.TryReadMessages(
                packet,
                packet.Length,
                decoded))
            {
                Debug.LogError("VCR P0 OSC/VMC codec: FAIL (OSC bundle decode)");
                return;
            }

            var accumulator =
                new VmcFrameAccumulator("vmc-self-test");

            if (!accumulator.Process(
                decoded,
                arrivalTimestampUs: 1_300_000,
                out var frame) ||
                frame == null ||
                frame.HumanoidPose == null ||
                frame.Expressions == null)
            {
                Debug.LogError("VCR P0 OSC/VMC codec: FAIL (VMC pose/expression decode)");
                return;
            }

            var hasHips =
                frame.HumanoidPose.TryGet(
                    HumanoidBoneId.Hips,
                    out var hips);

            var goldenMessage =
                OscPacketWriter.WriteMessage(
                    "/x",
                    OscArgument.FromInt(1),
                    OscArgument.FromFloat(1f),
                    OscArgument.FromString("A"));
            var goldenExpected =
                new byte[]
                {
                    (byte)'/', (byte)'x', 0, 0,
                    (byte)',', (byte)'i', (byte)'f', (byte)'s',
                    0, 0, 0, 0,
                    0, 0, 0, 1,
                    0x3f, 0x80, 0, 0,
                    (byte)'A', 0, 0, 0
                };
            var goldenBundle =
                OscPacketWriter.WriteBundle(
                    new[]
                    {
                        goldenMessage
                    });
            var goldenWriterPass =
                BytesEqual(
                    goldenMessage,
                    goldenExpected) &&
                goldenBundle.Length ==
                    20 +
                    goldenMessage.Length &&
                goldenBundle[0] ==
                    (byte)'#' &&
                goldenBundle[1] ==
                    (byte)'b' &&
                goldenBundle[2] ==
                    (byte)'u' &&
                goldenBundle[3] ==
                    (byte)'n' &&
                goldenBundle[4] ==
                    (byte)'d' &&
                goldenBundle[5] ==
                    (byte)'l' &&
                goldenBundle[6] ==
                    (byte)'e' &&
                goldenBundle[7] == 0 &&
                goldenBundle[15] == 1 &&
                goldenBundle[16] == 0 &&
                goldenBundle[17] == 0 &&
                goldenBundle[18] == 0 &&
                goldenBundle[19] ==
                    goldenMessage.Length;

            var reusableBuffer =
                new byte[
                    OscPacketReader.MaxPacketBytes];

            for (var i = 0;
                 i < reusableBuffer.Length;
                 i++)
            {
                reusableBuffer[i] =
                    0xff;
            }

            var reusableArguments =
                new[]
                {
                    OscArgument.FromInt(1),
                    OscArgument.FromFloat(1f),
                    OscArgument.FromString("A")
                };

            var reusableWriterPass =
                OscPacketWriter.TryBeginBundle(
                    reusableBuffer,
                    out var reusableLength) &&
                OscPacketWriter.TryAppendBundleMessage(
                    reusableBuffer,
                    ref reusableLength,
                    "/x",
                    reusableArguments,
                    reusableArguments.Length) &&
                BytesEqualPrefix(
                    reusableBuffer,
                    reusableLength,
                    goldenBundle);

            var malformed = new byte[]
            {
                (byte)'/', (byte)'x', 0, 0,
                (byte)',', (byte)'f', 0, 0,
                0, 0, 0
            };
            var malformedDecoded = new List<OscMessage>();
            var malformedRejected =
                !OscPacketReader.TryReadMessages(
                    malformed,
                    malformed.Length,
                    malformedDecoded);

            var vrm1Alias =
                StandardExpressionNames.TryParse(
                    "happy",
                    out var vrm1Happy) &&
                vrm1Happy == StandardExpression.Happy;

            var vrm0Alias =
                StandardExpressionNames.TryParse(
                    "Joy",
                    out var vrm0Happy) &&
                vrm0Happy == StandardExpression.Happy;

            var explicitArguments =
                new OscArgument[2];
            explicitArguments[0] =
                OscArgument.FromString("Joy");
            explicitArguments[1] =
                OscArgument.FromFloat(0.25f);

            var explicitCountPacket =
                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/Blend/Val",
                    explicitArguments,
                    2);
            var explicitCountDecoded =
                new List<OscMessage>();
            var explicitCountParsed =
                OscPacketReader.TryReadMessages(
                    explicitCountPacket,
                    explicitCountPacket.Length,
                    explicitCountDecoded) &&
                explicitCountDecoded.Count == 1 &&
                explicitCountDecoded[0]
                    .Arguments.Length == 2 &&
                explicitCountDecoded[0]
                    .Arguments[0]
                    .TryGetString(
                        out var explicitName) &&
                explicitName == "Joy" &&
                explicitCountDecoded[0]
                    .Arguments[1]
                    .TryGetFloat(
                        out var explicitValue) &&
                Mathf.Approximately(
                    explicitValue,
                    0.25f);

            var heartbeatPacket =
                OscPacketWriter.WriteMessage(
                    "/VMC/Ext/T",
                    OscArgument.FromFloat(2.0f));
            var heartbeatDecoded = new List<OscMessage>();
            var heartbeatParsed =
                OscPacketReader.TryReadMessages(
                    heartbeatPacket,
                    heartbeatPacket.Length,
                    heartbeatDecoded);
            var heartbeatProducedFrame =
                heartbeatParsed &&
                accumulator.Process(
                    heartbeatDecoded,
                    arrivalTimestampUs: 2_000_000,
                    out _);

            var expressionPacket =
                OscPacketWriter.WriteBundle(
                    new[]
                    {
                        OscPacketWriter.WriteMessage(
                            "/VMC/Ext/Blend/Val",
                            OscArgument.FromString("Blink_L"),
                            OscArgument.FromFloat(0.8f)),
                        OscPacketWriter.WriteMessage(
                            "/VMC/Ext/Blend/Apply")
                    });
            var expressionDecoded = new List<OscMessage>();
            var expressionParsed =
                OscPacketReader.TryReadMessages(
                    expressionPacket,
                    expressionPacket.Length,
                    expressionDecoded);
            TrackingFrame expressionFrame = null;
            var expressionProducedFrame =
                expressionParsed &&
                accumulator.Process(
                    expressionDecoded,
                    arrivalTimestampUs: 2_100_000,
                    out expressionFrame);

            var oversized =
                new byte[OscPacketReader.MaxPacketBytes + 1];
            var oversizedAccepted =
                OscPacketReader.TryReadMessages(
                    oversized,
                    oversized.Length,
                    new List<OscMessage>());

            var excessiveMessages =
                new List<byte[]>(
                    OscPacketReader
                        .MaxMessagesPerPacket +
                    1);

            for (var i = 0;
                 i <=
                 OscPacketReader
                     .MaxMessagesPerPacket;
                 i++)
            {
                excessiveMessages.Add(
                    OscPacketWriter.WriteMessage(
                        "/x"));
            }

            var excessivePacket =
                OscPacketWriter.WriteBundle(
                    excessiveMessages);
            var excessiveDecoded =
                new List<OscMessage>();
            var excessiveAccepted =
                OscPacketReader.TryReadMessages(
                    excessivePacket,
                    excessivePacket.Length,
                    excessiveDecoded);

            var excessiveArguments =
                new OscArgument[
                    OscPacketReader
                        .MaxArgumentsPerMessage +
                    1];

            for (var i = 0;
                 i <
                 excessiveArguments.Length;
                 i++)
            {
                excessiveArguments[i] =
                    OscArgument.FromInt(i);
            }

            var excessiveArgumentPacket =
                OscPacketWriter.WriteMessage(
                    "/args",
                    excessiveArguments);
            var excessiveArgumentDecoded =
                new List<OscMessage>();
            var excessiveArgumentsAccepted =
                OscPacketReader.TryReadMessages(
                    excessiveArgumentPacket,
                    excessiveArgumentPacket.Length,
                    excessiveArgumentDecoded);

            var boundedAccumulator =
                new VmcFrameAccumulator(
                    "p0-vmc-custom-bounds");
            var boundedMessages =
                new List<OscMessage>(
                    VmcFrameAccumulator
                        .MaxCustomExpressions +
                    2);

            for (var i = 0;
                 i <=
                 VmcFrameAccumulator
                     .MaxCustomExpressions;
                 i++)
            {
                boundedMessages.Add(
                    new OscMessage(
                        "/VMC/Ext/Blend/Val",
                        new[]
                        {
                            OscArgument.FromString(
                                "custom-" + i),
                            OscArgument.FromFloat(
                                i /
                                (float)
                                VmcFrameAccumulator
                                    .MaxCustomExpressions)
                        }));
            }

            boundedMessages.Add(
                new OscMessage(
                    "/VMC/Ext/Blend/Apply",
                    Array.Empty<
                        OscArgument>()));

            var boundedProcessed =
                boundedAccumulator.Process(
                    boundedMessages,
                    arrivalTimestampUs:
                        2_500_000,
                    out var boundedFrame);

            var dropsAfterCapacity =
                boundedAccumulator
                    .DroppedCustomExpressionCount;

            var boundedUpdateMessages =
                new[]
                {
                    new OscMessage(
                        "/VMC/Ext/Blend/Val",
                        new[]
                        {
                            OscArgument.FromString(
                                "custom-0"),
                            OscArgument.FromFloat(
                                0.75f)
                        }),
                    new OscMessage(
                        "/VMC/Ext/Blend/Apply",
                        Array.Empty<
                            OscArgument>())
                };

            var boundedUpdateProcessed =
                boundedAccumulator.Process(
                    boundedUpdateMessages,
                    arrivalTimestampUs:
                        2_600_000,
                    out var boundedUpdateFrame);

            var oversizedCustomName =
                new string(
                    'x',
                    VmcFrameAccumulator
                        .MaxCustomExpressionNameCharacters +
                    1);

            var oversizedNameProcessed =
                boundedAccumulator.Process(
                    new[]
                    {
                        new OscMessage(
                            "/VMC/Ext/Blend/Val",
                            new[]
                            {
                                OscArgument.FromString(
                                    oversizedCustomName),
                                OscArgument.FromFloat(
                                    1f)
                            }),
                        new OscMessage(
                            "/VMC/Ext/Blend/Apply",
                            Array.Empty<
                                OscArgument>())
                    },
                    arrivalTimestampUs:
                        2_700_000,
                    out _);

            var customBoundsPass =
                boundedProcessed &&
                boundedFrame?.Expressions != null &&
                boundedAccumulator
                    .CustomExpressionCount ==
                    VmcFrameAccumulator
                        .MaxCustomExpressions &&
                boundedFrame.Expressions
                    .Custom.Length ==
                    VmcFrameAccumulator
                        .MaxCustomExpressions &&
                dropsAfterCapacity == 1 &&
                boundedUpdateProcessed &&
                boundedUpdateFrame?
                    .Expressions != null &&
                Mathf.Approximately(
                    GetCustomExpressionValue(
                        boundedUpdateFrame
                            .Expressions,
                        "custom-0"),
                    0.75f) &&
                boundedAccumulator
                    .DroppedCustomExpressionCount ==
                    dropsAfterCapacity + 1 &&
                oversizedNameProcessed;

            var sourceLifecyclePass =
                false;
            var lifecycleSource =
                new VmcTrackingSource(
                    "p0-vmc-lifecycle");

            try
            {
                lifecycleSource.Start();

                var activeAccepted =
                    lifecycleSource.Process(
                        decoded,
                        arrivalTimestampUs:
                            3_000_000);

                var lifecycleCustomAccepted =
                    lifecycleSource.Process(
                        new[]
                        {
                            new OscMessage(
                                "/VMC/Ext/Blend/Val",
                                new[]
                                {
                                    OscArgument.FromString(
                                        "session-custom"),
                                    OscArgument.FromFloat(
                                        0.5f)
                                }),
                            new OscMessage(
                                "/VMC/Ext/Blend/Apply",
                                Array.Empty<
                                    OscArgument>())
                        },
                        arrivalTimestampUs:
                            3_050_000);

                var customPresentBeforeStop =
                    lifecycleSource
                        .CustomExpressionCount == 1;

                lifecycleSource.Stop();

                var stoppedCleared =
                    !lifecycleSource.TryTakeLatest(
                        out _) &&
                    !lifecycleSource.TryTakeLatestPose(
                        out _) &&
                    !lifecycleSource.TryTakeLatestExpressions(
                        out _) &&
                    lifecycleSource
                        .CustomExpressionCount == 0 &&
                    !lifecycleSource
                        .LastSubjectDetected;

                var stoppedRejected =
                    !lifecycleSource.Process(
                        decoded,
                        arrivalTimestampUs:
                            3_100_000);

                lifecycleSource.Start();

                var rightUpperArmName =
                    HumanoidBoneNames
                        .GetCanonical(
                            HumanoidBoneId
                                .RightUpperArm);

                var restartAccepted =
                    lifecycleSource.Process(
                        new[]
                        {
                            new OscMessage(
                                "/VMC/Ext/Bone/Pos",
                                new[]
                                {
                                    OscArgument.FromString(
                                        rightUpperArmName),
                                    OscArgument.FromFloat(
                                        0.25f),
                                    OscArgument.FromFloat(
                                        0f),
                                    OscArgument.FromFloat(
                                        0f),
                                    OscArgument.FromFloat(
                                        0f),
                                    OscArgument.FromFloat(
                                        0f),
                                    OscArgument.FromFloat(
                                        0f),
                                    OscArgument.FromFloat(
                                        1f)
                                })
                        },
                        arrivalTimestampUs:
                            3_200_000);

                var restartPoseIsolated =
                    lifecycleSource
                        .TryTakeLatestPose(
                            out var restartPoseFrame) &&
                    restartPoseFrame?
                        .HumanoidPose != null &&
                    restartPoseFrame
                        .HumanoidPose
                        .TryGet(
                            HumanoidBoneId
                                .RightUpperArm,
                            out _) &&
                    !restartPoseFrame
                        .HumanoidPose
                        .TryGet(
                            HumanoidBoneId.Hips,
                            out _);

                lifecycleSource.Stop();
                lifecycleSource.Dispose();

                var disposedRejected =
                    false;

                try
                {
                    lifecycleSource.Start();
                }
                catch (ObjectDisposedException)
                {
                    disposedRejected = true;
                }

                sourceLifecyclePass =
                    activeAccepted &&
                    lifecycleCustomAccepted &&
                    customPresentBeforeStop &&
                    stoppedCleared &&
                    stoppedRejected &&
                    restartAccepted &&
                    restartPoseIsolated &&
                    disposedRejected;
            }
            finally
            {
                lifecycleSource.Dispose();
            }

            var pass =
                goldenWriterPass &&
                reusableWriterPass &&
                customBoundsPass &&
                sourceLifecyclePass &&
                decoded.Count == 6 &&
                frame.SubjectDetected &&
                frame.HumanoidPose.PoseSpace ==
                    HumanoidPoseSpace.OriginalLocal &&
                hasHips &&
                malformedRejected &&
                vrm0Alias &&
                vrm1Alias &&
                explicitCountParsed &&
                heartbeatParsed &&
                !heartbeatProducedFrame &&
                expressionParsed &&
                expressionProducedFrame &&
                expressionFrame != null &&
                expressionFrame.HumanoidPose == null &&
                expressionFrame.Expressions != null &&
                Mathf.Approximately(
                    expressionFrame.Expressions.Get(
                        StandardExpression.BlinkLeft),
                    0.8f) &&
                !oversizedAccepted &&
                !excessiveAccepted &&
                excessiveDecoded.Count == 0 &&
                !excessiveArgumentsAccepted &&
                excessiveArgumentDecoded.Count == 0 &&
                Mathf.Approximately(
                    hips.LocalPosition.Y,
                    0.9f) &&
                Mathf.Approximately(
                    frame.Expressions.Get(
                        StandardExpression.Happy),
                    0.6f);

            if (pass)
            {
                Debug.Log(
                    $"VCR P0 OSC/VMC codec: PASS ({packet.Length} bytes, {decoded.Count} messages; stale-pose/custom-expression/bounds checks passed)");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 OSC/VMC codec: FAIL (golden/reusable OSC bytes, codec, pose-space, values, restart isolation, custom-expression bounds, stale-pose, or packet-bounds mismatch)");
            }
        }

        [MenuItem("VCR/P0/Add VMC Receiver")]
        public static void AddReceiver()
        {
            var router =
                Object.FindFirstObjectByType<PriorityTrackingRouter>();

            if (router == null)
            {
                var root = new GameObject("P0 Tracking");
                Undo.RegisterCreatedObjectUndo(
                    root,
                    "Create P0 Tracking");

                router =
                    Undo.AddComponent<PriorityTrackingRouter>(root);

                var mediaPipe =
                    Object.FindFirstObjectByType<MediaPipeWebcamTrackingRunner>();

                if (mediaPipe != null)
                {
                    router.SetFallbackProvider(mediaPipe);
                }
            }

            var receiver =
                Object.FindFirstObjectByType<VmcUdpReceiver>();

            if (receiver == null)
            {
                receiver =
                    Undo.AddComponent<VmcUdpReceiver>(
                        router.gameObject);
            }

            router.SetExternalPoseProvider(receiver);

            Selection.activeGameObject =
                receiver.gameObject;
            EditorGUIUtility.PingObject(receiver);

            Debug.Log(
                "VCR P0: VMC receiver attached as optional full-body provider. " +
                "Default listen port is 39539 and sender filter is 127.0.0.1. " +
                "Set an explicit LAN sender IPv4 address before accepting remote VMC.");
        }

        [MenuItem("VCR/P0/Add VMC Sender to Selected VRM", true)]
        private static bool ValidateAddSender()
        {
            return FindSelectedVrm() != null;
        }

        [MenuItem("VCR/P0/Add VMC Sender to Selected VRM")]
        public static void AddSender()
        {
            var vrm = FindSelectedVrm();
            if (vrm == null)
            {
                Debug.LogError(
                    "VCR P0: select a GameObject inside a Vrm10Instance.");
                return;
            }

            var snapshot =
                vrm.GetComponent<Vrm10MotionSnapshotProvider>();

            if (snapshot == null)
            {
                snapshot =
                    Undo.AddComponent<Vrm10MotionSnapshotProvider>(
                        vrm.gameObject);
            }

            var sender =
                vrm.GetComponent<VmcUdpSender>();

            if (sender == null)
            {
                sender =
                    Undo.AddComponent<VmcUdpSender>(
                        vrm.gameObject);
            }

            sender.SetSnapshotProvider(snapshot);

            Selection.activeGameObject = vrm.gameObject;
            EditorGUIUtility.PingObject(sender);

            Debug.Log(
                "VCR P0: VMC sender attached. " +
                "Default destination is 127.0.0.1:39539 at 60 Hz with VRM0 expression names.");
        }

        private static float GetCustomExpressionValue(
            NormalizedExpressionState state,
            string name)
        {
            if (state == null ||
                string.IsNullOrEmpty(
                    name))
            {
                return 0f;
            }

            foreach (var item in
                     state.Custom)
            {
                if (string.Equals(
                        item.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return item.Value;
                }
            }

            return 0f;
        }

        private static bool BytesEqualPrefix(
            byte[] buffer,
            int length,
            byte[] expected)
        {
            if (buffer == null ||
                expected == null ||
                length !=
                    expected.Length ||
                length < 0 ||
                length >
                    buffer.Length)
            {
                return false;
            }

            for (var i = 0;
                 i < length;
                 i++)
            {
                if (buffer[i] !=
                    expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool BytesEqual(
            byte[] left,
            byte[] right)
        {
            if (ReferenceEquals(
                    left,
                    right))
            {
                return true;
            }

            if (left == null ||
                right == null ||
                left.Length !=
                    right.Length)
            {
                return false;
            }

            for (var i = 0;
                 i < left.Length;
                 i++)
            {
                if (left[i] !=
                    right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static Vrm10Instance FindSelectedVrm()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                return null;
            }

            return
                selected.GetComponentInParent<Vrm10Instance>() ??
                selected.GetComponentInChildren<Vrm10Instance>();
        }
    }
}
