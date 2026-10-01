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
                Debug.LogError("VCR P0 OSC/VMC codec: FAIL (VMC normalization)");
                return;
            }

            var hasHips =
                frame.HumanoidPose.TryGet(
                    HumanoidBoneId.Hips,
                    out var hips);

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

            var pass =
                decoded.Count == 6 &&
                frame.SubjectDetected &&
                hasHips &&
                malformedRejected &&
                vrm0Alias &&
                vrm1Alias &&
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
                    $"VCR P0 OSC/VMC codec: PASS ({packet.Length} bytes, {decoded.Count} messages)");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 OSC/VMC codec: FAIL (normalized values mismatch)");
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
