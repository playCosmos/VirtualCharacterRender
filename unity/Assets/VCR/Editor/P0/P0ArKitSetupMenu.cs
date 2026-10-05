using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.ArKit;
using VCR.Runtime.Tracking.ArKitUnity;
using VCR.Runtime.Tracking.MediaPipe;
using VCR.Runtime.Tracking.Routing;

namespace VCR.Editor.P0
{
    public static class P0ArKitSetupMenu
    {
        [MenuItem("VCR/P0/Add iFacialMocap ARKit Receiver")]
        public static void AddReceiver()
        {
            var router = Object.FindFirstObjectByType<PriorityTrackingRouter>();
            if (router == null)
            {
                var routerObject = new GameObject("P0 Tracking Router");
                Undo.RegisterCreatedObjectUndo(
                    routerObject,
                    "Create P0 Tracking Router");

                router = Undo.AddComponent<PriorityTrackingRouter>(
                    routerObject);

                var mediaPipe =
                    Object.FindFirstObjectByType<MediaPipeWebcamTrackingRunner>();
                if (mediaPipe != null)
                {
                    router.SetFallbackProvider(mediaPipe);
                }
            }

            var receiver =
                Object.FindFirstObjectByType<IFacialMocapUdpReceiver>();

            if (receiver == null)
            {
                var receiverObject =
                    new GameObject("P0 ARKit - iFacialMocap");
                Undo.RegisterCreatedObjectUndo(
                    receiverObject,
                    "Create iFacialMocap Receiver");

                receiver = Undo.AddComponent<IFacialMocapUdpReceiver>(
                    receiverObject);
            }

            router.SetPreferredFaceProvider(receiver);

            Selection.activeGameObject = receiver.gameObject;
            EditorGUIUtility.PingObject(receiver);

            Debug.Log(
                "VCR P0: iFacialMocap receiver connected to the tracking router. " +
                "Set the iPhone/iPad IPv4 address in the receiver Inspector, then enter Play mode.");
        }

        [MenuItem("VCR/P0/Validate iFacialMocap Parser")]
        public static void ValidateParser()
        {
            const string sample =
                "eyeBlink_L&25|eyeBlink_R&75|jawOpen&50|" +
                "tongueOut&10|=head#10,-20,30,0.1,-0.2,-0.3|";

            if (!IFacialMocapFrameParser.TryParse(
                sample,
                out var frame))
            {
                Debug.LogError(
                    "VCR P0 iFacialMocap parser: FAIL (sample not parsed)");
                return;
            }

            var leftBlink =
                frame.Coefficients[(int)FaceCoefficient.EyeBlinkLeft];
            var rightBlink =
                frame.Coefficients[(int)FaceCoefficient.EyeBlinkRight];
            var jaw =
                frame.Coefficients[(int)FaceCoefficient.JawOpen];
            var tongue =
                frame.Coefficients[(int)FaceCoefficient.TongueOut];

            var pass =
                Mathf.Approximately(leftBlink, 0.25f) &&
                Mathf.Approximately(rightBlink, 0.75f) &&
                Mathf.Approximately(jaw, 0.50f) &&
                Mathf.Approximately(tongue, 0.10f) &&
                frame.HasHead &&
                Mathf.Approximately(frame.HeadEulerXDegrees, 10f) &&
                Mathf.Approximately(frame.HeadEulerYDegrees, -20f) &&
                Mathf.Approximately(frame.HeadEulerZDegrees, 30f);

            var oversizedText =
                new string(
                    'x',
                    IFacialMocapFrameParser
                        .MaxTextCharacters +
                    1);

            var excessiveParts =
                new StringBuilder();

            for (var i = 0;
                 i <=
                 IFacialMocapFrameParser
                     .MaxParts;
                 i++)
            {
                excessiveParts.Append(
                    "jawOpen&1|");
            }

            var parserBoundsPass =
                !IFacialMocapFrameParser.TryParse(
                    "jawOpen&NaN|",
                    out _) &&
                !IFacialMocapFrameParser.TryParse(
                    "=head#0,0,Infinity,0,0,0|",
                    out _) &&
                !IFacialMocapFrameParser.TryParse(
                    oversizedText,
                    out _) &&
                !IFacialMocapFrameParser.TryParse(
                    excessiveParts.ToString(),
                    out _);

            var sourceLifecyclePass =
                false;
            var source =
                new ArKitFaceSource(
                    "p0-arkit-lifecycle");

            try
            {
                source.Start();
                source.Publish(
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        TrackingVector3.Zero,
                        frame.Coefficients),
                    timestampUs: 1);

                var published =
                    source.TryTakeLatest(
                        out var publishedFrame) &&
                    publishedFrame != null;

                source.Publish(
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        TrackingVector3.Zero,
                        frame.Coefficients),
                    timestampUs: 2);

                source.Stop();

                var stoppedCleared =
                    !source.TryTakeLatest(
                        out _);

                var stoppedRejected =
                    false;

                try
                {
                    source.Publish(
                        new NormalizedFaceState(
                            TrackingQuaternion.Identity,
                            TrackingVector3.Zero,
                            frame.Coefficients),
                        timestampUs: 3);
                }
                catch (InvalidOperationException)
                {
                    stoppedRejected = true;
                }

                source.Dispose();

                var disposedRejected =
                    false;

                try
                {
                    source.Start();
                }
                catch (ObjectDisposedException)
                {
                    disposedRejected = true;
                }

                sourceLifecyclePass =
                    published &&
                    stoppedCleared &&
                    stoppedRejected &&
                    disposedRejected;
            }
            finally
            {
                source.Dispose();
            }

            pass =
                pass &&
                parserBoundsPass &&
                sourceLifecyclePass;

            if (pass)
            {
                Debug.Log("VCR P0 iFacialMocap parser: PASS");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 iFacialMocap parser: FAIL (mapping, bounds, finite-value, or lifecycle mismatch)");
            }
        }
    }
}
