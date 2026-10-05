using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.ArKit;
using VCR.Runtime.Tracking.ArKitUnity;
using VCR.Runtime.Tracking.AudioUnity;
using VCR.Runtime.Tracking.MediaPipe;

namespace VCR.Editor.P3
{
    public static class P3BuiltInTrackingValidation
    {
        [MenuItem("VCR/P3/Validate Built-in Tracking Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            Texture2D texture = null;
            ArKitFaceSource source = null;

            try
            {
                var coefficients =
                    new float[
                        (int)FaceCoefficient.Count];

                coefficients[
                    (int)FaceCoefficient.JawOpen] =
                    0.5f;

                var face =
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        TrackingVector3.Zero,
                        coefficients,
                        SnapshotArrayOwnership.Copy);

                source =
                    new ArKitFaceSource(
                        "p3-arkit-validation");

                Expect(
                    source.Health.State ==
                    TrackingSourceHealthState.Stopped,
                    "ARKit face source must begin stopped",
                    failures);

                source.Start();

                Expect(
                    source.Health.State ==
                    TrackingSourceHealthState.Starting,
                    "ARKit face source must enter Starting on start",
                    failures);

                source.Publish(
                    face,
                    timestampUs: 1000);

                Expect(
                    source.Health.State ==
                    TrackingSourceHealthState.Healthy &&
                    source.TryTakeLatest(
                        out var faceFrame) &&
                    faceFrame.SubjectDetected &&
                    faceFrame.Face != null &&
                    faceFrame.ValidRegions.HasFlag(
                        TrackingRegion.Face) &&
                    faceFrame.ValidRegions.HasFlag(
                        TrackingRegion.Head),
                    "ARKit face publish must produce healthy face/head evidence",
                    failures);

                source.PublishNoSubject(
                    timestampUs: 2000);

                Expect(
                    source.TryTakeLatest(
                        out var noSubjectFrame) &&
                    !noSubjectFrame.SubjectDetected &&
                    noSubjectFrame.Face == null &&
                    noSubjectFrame.ValidRegions ==
                        TrackingRegion.None,
                    "ARKit no-subject publish must not masquerade as face evidence",
                    failures);

                source.MarkSourceLost(
                    "validation loss");

                Expect(
                    source.Health.State ==
                    TrackingSourceHealthState.SourceLost &&
                    source.Health.Error ==
                    "validation loss",
                    "ARKit source loss must remain distinct from no-subject evidence",
                    failures);

                source.Stop();

                Expect(
                    source.Health.State ==
                    TrackingSourceHealthState.Stopped,
                    "ARKit face source must stop cleanly",
                    failures);

                ValidateSnapshotOwnership(
                    failures);

                var latestBuffer =
                    new LatestValueBuffer<
                        IFacialMocapFrame>();
                latestBuffer.Publish(
                    new IFacialMocapFrame(
                        coefficients,
                        hasHead: false,
                        headEulerXDegrees: 0f,
                        headEulerYDegrees: 0f,
                        headEulerZDegrees: 0f,
                        headPositionX: 0f,
                        headPositionY: 0f,
                        headPositionZ: 0f,
                        ownership:
                            SnapshotArrayOwnership.Copy));
                latestBuffer.Clear();

                Expect(
                    !latestBuffer.HasValue &&
                    latestBuffer.TakeLatest() == null,
                    "latest-value buffers must support explicit stale-frame discard across receiver lifecycles",
                    failures);

                var webcamStatus =
                    new MediaPipeWebcamStatus(
                        MediaPipeWebcamLifecycleState.Running,
                        "camera",
                        640,
                        480,
                        30,
                        faceEnabled: true,
                        WebcamPreprocessingMode.Disabled,
                        faceSubmitted: 10,
                        holisticSubmitted: 5,
                        facePoolDrops: 1,
                        holisticPoolDrops: 2,
                        readbackErrors: 0,
                        error: null);

                Expect(
                    webcamStatus.IsRunning &&
                    webcamStatus.PreprocessingMode ==
                        WebcamPreprocessingMode.Disabled &&
                    webcamStatus.FaceSubmitted == 10 &&
                    webcamStatus.HolisticPoolDrops == 2,
                    "MediaPipe webcam status must expose lifecycle and capture counters",
                    failures);

                var arkitStatus =
                    new ArKitReceiverStatus(
                        ArKitReceiverLifecycleState.SourceLost,
                        "192.0.2.1",
                        49983,
                        49983,
                        datagramCount: 20,
                        parsedFrameCount: 18,
                        rejectedSenderCount: 1,
                        parseFailureCount: 1,
                        handshakeCount: 2,
                        socketErrorCount: 0,
                        error: null);

                Expect(
                    arkitStatus.IsRunning &&
                    arkitStatus.State ==
                        ArKitReceiverLifecycleState.SourceLost &&
                    arkitStatus.DatagramCount == 20 &&
                    arkitStatus.ParsedFrameCount == 18,
                    "ARKit receiver status must retain transport counters during source loss",
                    failures);

                var rms =
                    AudioDrivenExpressionMath
                        .ComputeRms(
                            new[]
                            {
                                0.5f,
                                -0.5f,
                                0.5f,
                                -0.5f
                            });

                Expect(
                    Math.Abs(
                        rms -
                        0.5f) <
                    0.0001f,
                    "audio fallback RMS math must be deterministic",
                    failures);

                var normalizedAudio =
                    AudioDrivenExpressionMath
                        .NormalizeLevel(
                            rms: 0.06f,
                            threshold: 0.01f,
                            gain: 10f);

                Expect(
                    Math.Abs(
                        normalizedAudio -
                        0.5f) <
                    0.0001f,
                    "audio fallback level normalization must apply threshold and gain",
                    failures);

                var attack =
                    AudioDrivenExpressionMath
                        .Smooth(
                            current: 0f,
                            target: 1f,
                            deltaSeconds: 0.05f,
                            attackSeconds: 0.05f,
                            releaseSeconds: 0.2f);

                var release =
                    AudioDrivenExpressionMath
                        .Smooth(
                            current: 1f,
                            target: 0f,
                            deltaSeconds: 0.05f,
                            attackSeconds: 0.05f,
                            releaseSeconds: 0.2f);

                Expect(
                    attack > 0f &&
                    attack < 1f &&
                    release > 0f &&
                    release < 1f &&
                    attack >
                        (1f - release),
                    "audio fallback attack/release smoothing must use separate time constants",
                    failures);

                texture =
                    new Texture2D(
                        2,
                        2);

                var preprocessor =
                    new WebcamFramePreprocessor();

                try
                {
                    var prepared =
                        preprocessor.Prepare(
                            texture,
                            WebcamPreprocessingMode.Disabled,
                            exposure: 3f,
                            gamma: 2f);

                    Expect(
                        ReferenceEquals(
                            prepared,
                            texture),
                        "disabled webcam preprocessing must return the original texture without GPU preprocessing",
                        failures);
                }
                finally
                {
                    preprocessor.Dispose();
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                source?.Dispose();

                if (texture != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            texture);
                }
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P3 built-in tracking validation: PASS " +
                    "(snapshot array ownership/copy isolation, ARKit lifecycle/no-subject/source-loss distinction, capture status, audio fallback math, disabled preprocessing path)");
                return true;
            }

            Debug.LogError(
                "VCR P3 built-in tracking validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));

            return false;
        }

        private static void ValidateSnapshotOwnership(
            List<string> failures)
        {
            var faceSource =
                new float[
                    (int)FaceCoefficient.Count];
            faceSource[
                (int)FaceCoefficient.JawOpen] =
                    0.35f;

            var copiedFace =
                new NormalizedFaceState(
                    TrackingQuaternion.Identity,
                    TrackingVector3.Zero,
                    faceSource,
                    SnapshotArrayOwnership.Copy);

            faceSource[
                (int)FaceCoefficient.JawOpen] =
                    0.9f;

            Expect(
                Math.Abs(
                    copiedFace.Get(
                        FaceCoefficient.JawOpen) -
                    0.35f) <
                0.0001f,
                "copy-owned face snapshots must not change when the caller mutates its source array",
                failures);

            var bodySource =
                new TrackingPoint[
                    (int)UpperBodyJoint.Count];
            bodySource[
                (int)UpperBodyJoint.LeftShoulder] =
                    new TrackingPoint(
                        new TrackingVector3(
                            1f,
                            2f,
                            3f),
                        0.8f);

            var copiedBody =
                new NormalizedUpperBodyState(
                    bodySource,
                    SnapshotArrayOwnership.Copy);

            bodySource[
                (int)UpperBodyJoint.LeftShoulder] =
                    default;

            Expect(
                Math.Abs(
                    copiedBody.Get(
                        UpperBodyJoint.LeftShoulder)
                        .Position.X -
                    1f) <
                0.0001f &&
                copiedBody.Get(
                    (UpperBodyJoint)(-1))
                    .Confidence == 0f,
                "copy-owned upper-body snapshots must isolate caller mutation and tolerate invalid enum indices",
                failures);

            var handSource =
                new TrackingPoint[
                    (int)HandJoint.Count];
            handSource[
                (int)HandJoint.IndexTip] =
                    new TrackingPoint(
                        new TrackingVector3(
                            4f,
                            5f,
                            6f),
                        0.7f);

            var copiedHand =
                new NormalizedHandState(
                    isLeft: true,
                    joints: handSource,
                    ownership:
                        SnapshotArrayOwnership.Copy);

            handSource[
                (int)HandJoint.IndexTip] =
                    default;

            Expect(
                Math.Abs(
                    copiedHand.Get(
                        HandJoint.IndexTip)
                        .Position.X -
                    4f) <
                0.0001f &&
                copiedHand.Get(
                    (HandJoint)999)
                    .Confidence == 0f,
                "copy-owned hand snapshots must isolate caller mutation and tolerate invalid enum indices",
                failures);

            var standardSource =
                new float[
                    (int)StandardExpression.Count];
            standardSource[
                (int)StandardExpression.Happy] =
                    0.4f;
            var customSource =
                new[]
                {
                    new NamedExpressionValue(
                        "custom-copy",
                        0.6f)
                };

            var copiedExpressions =
                new NormalizedExpressionState(
                    standardSource,
                    customSource,
                    SnapshotArrayOwnership.Copy);

            standardSource[
                (int)StandardExpression.Happy] =
                    1f;
            customSource[0] =
                new NamedExpressionValue(
                    "custom-copy",
                    1f);

            Expect(
                Math.Abs(
                    copiedExpressions.Get(
                        StandardExpression.Happy) -
                    0.4f) <
                0.0001f &&
                copiedExpressions.Custom.Length == 1 &&
                Math.Abs(
                    copiedExpressions.Custom[0]
                        .Value -
                    0.6f) <
                0.0001f,
                "copy-owned expression snapshots must defensively copy both standard and custom arrays",
                failures);

            var poseBones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var posePresence =
                new bool[
                    (int)HumanoidBoneId.Count];
            var hipsIndex =
                (int)HumanoidBoneId.Hips;

            poseBones[hipsIndex] =
                new NormalizedBonePose(
                    new TrackingVector3(
                        7f,
                        0f,
                        0f),
                    TrackingQuaternion.Identity);
            posePresence[hipsIndex] =
                true;

            var copiedPose =
                new HumanoidPoseState(
                    HumanoidPoseSpace.NormalizedLocal,
                    TrackingVector3.Zero,
                    TrackingQuaternion.Identity,
                    poseBones,
                    posePresence,
                    SnapshotArrayOwnership.Copy);

            poseBones[hipsIndex] =
                default;
            posePresence[hipsIndex] =
                false;

            Expect(
                copiedPose.TryGet(
                    HumanoidBoneId.Hips,
                    out var copiedHips) &&
                Math.Abs(
                    copiedHips.LocalPosition.X -
                    7f) <
                0.0001f,
                "copy-owned humanoid poses must defensively copy both pose arrays",
                failures);

            var rawSource =
                new float[
                    (int)FaceCoefficient.Count];
            rawSource[
                (int)FaceCoefficient.JawOpen] =
                    0.45f;

            var rawFrame =
                new IFacialMocapFrame(
                    rawSource,
                    hasHead: false,
                    headEulerXDegrees: 0f,
                    headEulerYDegrees: 0f,
                    headEulerZDegrees: 0f,
                    headPositionX: 0f,
                    headPositionY: 0f,
                    headPositionZ: 0f,
                    ownership:
                        SnapshotArrayOwnership.Copy);

            rawSource[
                (int)FaceCoefficient.JawOpen] =
                    1f;

            var rawCopiedBeforeDetach =
                rawFrame.Coefficients[
                    (int)FaceCoefficient.JawOpen];
            var detached =
                rawFrame.DetachCoefficientOwnership();
            var secondDetachRejected =
                false;

            try
            {
                rawFrame
                    .DetachCoefficientOwnership();
            }
            catch (InvalidOperationException)
            {
                secondDetachRejected =
                    true;
            }

            Expect(
                Math.Abs(
                    rawCopiedBeforeDetach -
                    0.45f) <
                0.0001f &&
                rawFrame.Coefficients.Length == 0 &&
                Math.Abs(
                    detached[
                        (int)FaceCoefficient.JawOpen] -
                    0.45f) <
                0.0001f &&
                secondDetachRejected,
                "raw iFacialMocap buffers must be read-only to consumers and detachable exactly once",
                failures);
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }
}
