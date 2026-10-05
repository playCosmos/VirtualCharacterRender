using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Routing;

namespace VCR.Editor.P4
{
    public static class P4TrackingRoutingValidation
    {
        [MenuItem("VCR/P4/Validate Tracking Routing")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures = new List<string>();
            GameObject root = null;

            try
            {
                root = new GameObject("P4 Tracking Routing Validation");

                var preferred =
                    root.AddComponent<P4FakeTrackingProvider>();
                var fallback =
                    root.AddComponent<P4FakeTrackingProvider>();
                var router =
                    root.AddComponent<PriorityTrackingRouter>();
                var externalExpressions =
                    root.AddComponent<P4FakeTrackingProvider>();
                var audioExpressions =
                    root.AddComponent<P4FakeTrackingProvider>();

                preferred.SourceId = "arkit-face";
                preferred.Kind =
                    TrackingSourceKind.ArKitFace;
                preferred.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                preferred.HealthState =
                    TrackingSourceHealthState.Healthy;

                fallback.SourceId = "mediapipe-webcam";
                fallback.Kind =
                    TrackingSourceKind.MediaPipeFaceWebcam;
                fallback.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                fallback.HealthState =
                    TrackingSourceHealthState.Healthy;

                externalExpressions.SourceId =
                    "vmc-expressions";
                externalExpressions.Kind =
                    TrackingSourceKind.Vmc;
                externalExpressions.Regions =
                    TrackingRegion.Expressions;
                externalExpressions.HealthState =
                    TrackingSourceHealthState.Healthy;

                audioExpressions.SourceId =
                    "audio-mouth-fallback";
                audioExpressions.Kind =
                    TrackingSourceKind.AudioFallback;
                audioExpressions.Regions =
                    TrackingRegion.Expressions;
                audioExpressions.HealthState =
                    TrackingSourceHealthState.Healthy;

                var nowUs =
                    MonotonicClock.NowMicroseconds();

                preferred.FaceFrame =
                    CreateFaceFrame(
                        preferred.SourceId,
                        sequence: 1,
                        nowUs);

                fallback.FaceFrame =
                    CreateFaceFrame(
                        fallback.SourceId,
                        sequence: 1,
                        nowUs);

                externalExpressions.ExpressionFrame =
                    CreateExpressionFrame(
                        externalExpressions.SourceId,
                        sequence: 1,
                        nowUs,
                        value: 0.8f);

                audioExpressions.ExpressionFrame =
                    CreateExpressionFrame(
                        audioExpressions.SourceId,
                        sequence: 1,
                        nowUs,
                        value: 0.4f);

                preferred.Presence =
                    CreatePresence(
                        nowUs,
                        faceAvailable: true,
                        faceEvidence: true);

                fallback.Presence =
                    CreatePresence(
                        nowUs,
                        faceAvailable: true,
                        faceEvidence: true);

                router.SetPreferredFaceProvider(
                    preferred);
                router.SetFallbackProvider(
                    fallback);
                router.SetExternalPoseProvider(
                    externalExpressions);
                router.SetExpressionFallbackProvider(
                    audioExpressions);

                InvokeUpdate(router);

                var preferredStatus =
                    router.RouteStatus;

                Expect(
                    preferredStatus.PreferredFaceActive,
                    "preferred face source must become active while healthy",
                    failures);

                Expect(
                    preferredStatus.FaceSourceId ==
                    preferred.SourceId,
                    "router must expose the preferred face source id",
                    failures);

                Expect(
                    !fallback.FaceTrackingEnabled &&
                    !preferredStatus
                        .FallbackFaceInferenceEnabled,
                    "healthy preferred face input must suspend fallback face inference",
                    failures);

                Expect(
                    !double.IsNaN(
                        preferredStatus.FaceAgeMs) &&
                    preferredStatus.FaceAgeMs >= 0.0,
                    "route status must expose a non-negative face age",
                    failures);

                var fallbackFirst =
                    TrackingRoutePolicy.CreateDefault();
                fallbackFirst.SetFacePriorityOrder(
                    TrackingSourceKind.MediaPipeFaceWebcam,
                    TrackingSourceKind.ArKitFace);

                router.SetRoutePolicy(
                    fallbackFirst);
                InvokeUpdate(router);

                var policyStatus =
                    router.RouteStatus;

                Expect(
                    policyStatus.FaceSourceId ==
                    fallback.SourceId &&
                    !policyStatus.PreferredFaceActive,
                    "explicit face priority policy must be able to select MediaPipe ahead of ARKit without rewiring providers",
                    failures);

                Expect(
                    fallback.FaceTrackingEnabled,
                    "policy-selected MediaPipe face source must be re-enabled before route selection",
                    failures);

                router.SetRoutePolicy(
                    TrackingRoutePolicy.CreateDefault());
                InvokeUpdate(router);

                var restoredPolicyStatus =
                    router.RouteStatus;

                Expect(
                    restoredPolicyStatus.FaceSourceId ==
                    preferred.SourceId &&
                    restoredPolicyStatus.PreferredFaceActive,
                    "restoring default policy must return face ownership to the healthy ARKit source",
                    failures);

                Expect(
                    restoredPolicyStatus.ExpressionSourceId ==
                    externalExpressions.SourceId,
                    "default expression routing must prefer VMC over audio fallback",
                    failures);

                externalExpressions.HealthState =
                    TrackingSourceHealthState.SourceLost;

                InvokeUpdate(router);

                var expressionFallbackStatus =
                    router.RouteStatus;

                Expect(
                    expressionFallbackStatus.ExpressionSourceId ==
                    audioExpressions.SourceId,
                    "expression routing must select audio fallback when VMC expression health is lost",
                    failures);

                preferred.HealthState =
                    TrackingSourceHealthState.SourceLost;

                InvokeUpdate(router);

                var fallbackStatus =
                    router.RouteStatus;

                Expect(
                    !fallbackStatus.PreferredFaceActive,
                    "preferred face route must deactivate when common source health reports SourceLost",
                    failures);

                Expect(
                    fallbackStatus.FaceSourceId ==
                    fallback.SourceId,
                    "router must hot-switch face routing to the fallback source",
                    failures);

                Expect(
                    fallback.FaceTrackingEnabled &&
                    fallbackStatus
                        .FallbackFaceInferenceEnabled,
                    "fallback face inference must resume after preferred-source loss",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();

                router.CollectMetrics(metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "tracking.route.face_switches",
                        out var faceSwitches) &&
                    Math.Abs(
                        faceSwitches - 3.0) <
                    0.001,
                    "policy reversal, policy restore, and preferred-source loss must produce exactly three face source switches",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "tracking.route.expression_switches",
                        out var expressionSwitches) &&
                    Math.Abs(
                        expressionSwitches - 1.0) <
                    0.001,
                    "VMC-to-audio fallback must produce exactly one expression source switch",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "tracking.route.fallback_face_inference_enabled",
                        out var fallbackEnabled) &&
                    fallbackEnabled > 0.5,
                    "routing metrics must report fallback face inference state",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "tracking.route.preferred_face_health",
                        out var preferredHealth) &&
                    Math.Abs(
                        preferredHealth -
                        (int)TrackingSourceHealthState.SourceLost) <
                    0.001,
                    "routing metrics must expose common source health state",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(root);
                }
            }

            ValidateDestroyedProviderRecovery(
                failures);
            ValidateSameSequenceRestartRecovery(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P4 tracking routing validation: PASS " +
                    "(data-driven priority, face hot switching, VMC-to-audio expression fallback, common source health, inference suspension, route age, switch metrics)");
                return true;
            }

            Debug.LogError(
                "VCR P4 tracking routing validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));

            return false;
        }

        private static void ValidateDestroyedProviderRecovery(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P4 Destroyed Provider Recovery");

                var preferred =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                var fallback =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                var router =
                    root.AddComponent<
                        PriorityTrackingRouter>();

                preferred.SourceId =
                    "arkit-face";
                preferred.Kind =
                    TrackingSourceKind.ArKitFace;
                preferred.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                preferred.HealthState =
                    TrackingSourceHealthState.Healthy;

                fallback.SourceId =
                    "mediapipe-webcam";
                fallback.Kind =
                    TrackingSourceKind.MediaPipeFaceWebcam;
                fallback.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                fallback.HealthState =
                    TrackingSourceHealthState.Healthy;

                var nowUs =
                    MonotonicClock.NowMicroseconds();

                preferred.FaceFrame =
                    CreateFaceFrame(
                        preferred.SourceId,
                        sequence: 1,
                        nowUs);
                preferred.Presence =
                    CreatePresence(
                        nowUs,
                        faceAvailable: true,
                        faceEvidence: true);

                fallback.FaceFrame =
                    CreateFaceFrame(
                        fallback.SourceId,
                        sequence: 1,
                        nowUs);
                fallback.Presence =
                    CreatePresence(
                        nowUs,
                        faceAvailable: true,
                        faceEvidence: true);

                router.SetPreferredFaceProvider(
                    preferred);
                router.SetFallbackProvider(
                    fallback);
                InvokeUpdate(
                    router);

                Expect(
                    router.RouteStatus.PreferredFaceActive &&
                    router.RouteStatus.FaceSourceId ==
                        preferred.SourceId,
                    "router recovery validation must start on the healthy preferred provider",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    preferred);

                InvokeUpdate(
                    router);

                Expect(
                    !router.RouteStatus.PreferredFaceActive &&
                    router.RouteStatus.FaceSourceId ==
                        fallback.SourceId,
                    "router must discard a destroyed preferred provider and continue through the live fallback without invoking stale interfaces",
                    failures);

                router.TryGetLatestFace(
                    out var stableFallbackFrame);
                InvokeUpdate(
                    router);
                router.TryGetLatestFace(
                    out var repeatedFallbackFrame);

                Expect(
                    ReferenceEquals(
                        stableFallbackFrame,
                        repeatedFallbackFrame),
                    "an absent optional preferred provider must not reset the active fallback selection and republish an unchanged frame every Update",
                    failures);

                var replacement =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                replacement.SourceId =
                    "arkit-face";
                replacement.Kind =
                    TrackingSourceKind.ArKitFace;
                replacement.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                replacement.HealthState =
                    TrackingSourceHealthState.Healthy;
                replacement.FaceFrame =
                    CreateFaceFrame(
                        replacement.SourceId,
                        sequence: 1,
                        nowUs + 1);
                replacement.Presence =
                    CreatePresence(
                        nowUs + 1,
                        faceAvailable: true,
                        faceEvidence: true);

                router.SetPreferredFaceProvider(
                    replacement);
                InvokeUpdate(
                    router);

                Expect(
                    router.RouteStatus.PreferredFaceActive &&
                    router.RouteStatus.FaceSourceId ==
                        replacement.SourceId,
                    "router must reset source-selection caches when a replacement provider reuses the previous source id and sequence",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    fallback);

                InvokeUpdate(
                    router);

                Expect(
                    router.RouteStatus.PreferredFaceActive &&
                    router.RouteStatus.FaceSourceId ==
                        replacement.SourceId,
                    "router must ignore a destroyed fallback activation/provider interface while the preferred route remains healthy",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed routing provider recovery unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateSameSequenceRestartRecovery(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P4 Same Sequence Restart Recovery");

                var faceProvider =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                var bodyProvider =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                var poseProvider =
                    root.AddComponent<
                        P4FakeTrackingProvider>();
                var router =
                    root.AddComponent<
                        PriorityTrackingRouter>();

                var nowUs =
                    MonotonicClock.NowMicroseconds();

                faceProvider.SourceId =
                    "restart-face";
                faceProvider.Kind =
                    TrackingSourceKind.ArKitFace;
                faceProvider.Regions =
                    TrackingRegion.Face |
                    TrackingRegion.Head;
                faceProvider.HealthState =
                    TrackingSourceHealthState.Healthy;
                faceProvider.FaceFrame =
                    CreateFaceFrame(
                        faceProvider.SourceId,
                        sequence: 7,
                        nowUs);
                faceProvider.Presence =
                    CreatePresence(
                        nowUs,
                        faceAvailable: true,
                        faceEvidence: true);

                bodyProvider.SourceId =
                    "restart-body";
                bodyProvider.Kind =
                    TrackingSourceKind.MediaPipeHolisticWebcam;
                bodyProvider.Regions =
                    TrackingRegion.UpperBody;
                bodyProvider.HealthState =
                    TrackingSourceHealthState.Healthy;
                bodyProvider.BodyHandsFrame =
                    CreateBodyFrame(
                        bodyProvider.SourceId,
                        sequence: 11,
                        nowUs);

                poseProvider.SourceId =
                    "restart-pose";
                poseProvider.Kind =
                    TrackingSourceKind.Vmc;
                poseProvider.Regions =
                    TrackingRegion.FullBody;
                poseProvider.HealthState =
                    TrackingSourceHealthState.Healthy;
                poseProvider.HumanoidPoseFrame =
                    CreateHumanoidPoseFrame(
                        poseProvider.SourceId,
                        sequence: 13,
                        nowUs);
                poseProvider.Presence =
                    CreateFullBodyPresence(
                        nowUs,
                        available: true,
                        evidence: true);

                router.SetPreferredFaceProvider(
                    faceProvider);
                router.SetFallbackProvider(
                    bodyProvider);
                router.SetExternalPoseProvider(
                    poseProvider);

                InvokeUpdate(
                    router);

                Expect(
                    router.TryGetLatestFace(
                        out var initialFace) &&
                    initialFace != null &&
                    router.TryGetLatestBodyHands(
                        out var initialBody) &&
                    initialBody != null &&
                    router.TryGetLatestHumanoidPose(
                        out var initialPose) &&
                    initialPose != null,
                    "same-sequence restart validation must establish initial face/body/full-body snapshots",
                    failures);

                faceProvider.FaceFrame = null;
                bodyProvider.BodyHandsFrame = null;
                poseProvider.HumanoidPoseFrame = null;

                InvokeUpdate(
                    router);

                Expect(
                    !router.TryGetLatestFace(
                        out _) &&
                    !router.TryGetLatestBodyHands(
                        out _) &&
                    !router.TryGetLatestHumanoidPose(
                        out _),
                    "router must clear face/body/full-body outputs when live providers temporarily stop returning frames",
                    failures);

                faceProvider.FaceFrame =
                    CreateFaceFrame(
                        faceProvider.SourceId,
                        sequence: 7,
                        nowUs + 1);
                bodyProvider.BodyHandsFrame =
                    CreateBodyFrame(
                        bodyProvider.SourceId,
                        sequence: 11,
                        nowUs + 1);
                poseProvider.HumanoidPoseFrame =
                    CreateHumanoidPoseFrame(
                        poseProvider.SourceId,
                        sequence: 13,
                        nowUs + 1);

                InvokeUpdate(
                    router);

                Expect(
                    router.TryGetLatestFace(
                        out var recoveredFace) &&
                    recoveredFace != null &&
                    router.TryGetLatestBodyHands(
                        out var recoveredBody) &&
                    recoveredBody != null &&
                    router.TryGetLatestHumanoidPose(
                        out var recoveredPose) &&
                    recoveredPose != null,
                    "router must republish face/body/full-body frames when a restarted provider reuses the previous source id and sequence",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "same-sequence tracking restart recovery unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static TrackingFrame CreateFaceFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs)
        {
            var coefficients =
                new float[
                    (int)FaceCoefficient.Count];

            var face =
                new NormalizedFaceState(
                    TrackingQuaternion.Identity,
                    TrackingVector3.Zero,
                    coefficients);

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Face |
                    TrackingRegion.Head,
                confidence: 1f,
                subjectDetected: true,
                face: face,
                sourceId: sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame CreateExpressionFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs,
            float value)
        {
            var standard =
                new float[
                    (int)StandardExpression.Count];

            standard[
                (int)StandardExpression.Aa] =
                value;

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Expressions,
                confidence:
                    value,
                subjectDetected:
                    false,
                expressions:
                    new NormalizedExpressionState(
                        standard),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame CreateBodyFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs)
        {
            var joints =
                new TrackingPoint[
                    (int)UpperBodyJoint.Count];

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.UpperBody,
                confidence:
                    1f,
                subjectDetected:
                    true,
                upperBody:
                    new NormalizedUpperBodyState(
                        joints),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame CreateHumanoidPoseFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs)
        {
            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var hasBone =
                new bool[
                    (int)HumanoidBoneId.Count];

            var hips =
                (int)HumanoidBoneId.Hips;
            bones[hips] =
                new NormalizedBonePose(
                    TrackingVector3.Zero,
                    TrackingQuaternion.Identity);
            hasBone[hips] =
                true;

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.FullBody,
                confidence:
                    1f,
                subjectDetected:
                    true,
                humanoidPose:
                    new HumanoidPoseState(
                        HumanoidPoseSpace
                            .NormalizedLocal,
                        TrackingVector3.Zero,
                        TrackingQuaternion.Identity,
                        bones,
                        hasBone),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingPresenceSnapshot
            CreateFullBodyPresence(
                long timestampUs,
                bool available,
                bool evidence)
        {
            return new TrackingPresenceSnapshot(
                sequence:
                    timestampUs,
                timestampUs:
                    timestampUs,
                subjectState:
                    evidence
                        ? SubjectPresenceState.Present
                        : SubjectPresenceState.Unknown,
                faceSourceAvailable:
                    false,
                bodyHandsSourceAvailable:
                    false,
                fullBodySourceAvailable:
                    available,
                faceSubjectEvidence:
                    false,
                bodyHandsSubjectEvidence:
                    false,
                fullBodySubjectEvidence:
                    evidence,
                anySourceAvailable:
                    available,
                subjectEvidence:
                    evidence,
                events:
                    TrackingPresenceEvents.None);
        }

        private static TrackingPresenceSnapshot
            CreatePresence(
                long timestampUs,
                bool faceAvailable,
                bool faceEvidence)
        {
            return new TrackingPresenceSnapshot(
                sequence: timestampUs,
                timestampUs: timestampUs,
                subjectState:
                    faceEvidence
                        ? SubjectPresenceState.Present
                        : SubjectPresenceState.Unknown,
                faceSourceAvailable:
                    faceAvailable,
                bodyHandsSourceAvailable:
                    false,
                fullBodySourceAvailable:
                    false,
                faceSubjectEvidence:
                    faceEvidence,
                bodyHandsSubjectEvidence:
                    false,
                fullBodySubjectEvidence:
                    false,
                anySourceAvailable:
                    faceAvailable,
                subjectEvidence:
                    faceEvidence,
                events:
                    TrackingPresenceEvents.None);
        }

        private static void InvokeUpdate(
            PriorityTrackingRouter router)
        {
            var method =
                typeof(PriorityTrackingRouter)
                    .GetMethod(
                        "Update",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(PriorityTrackingRouter)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                router,
                null);
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in metrics)
            {
                if (string.Equals(
                    metric.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    value = metric.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
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

    internal sealed class P4FakeTrackingProvider :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingPresenceProvider,
        ITrackingSourceHealthProvider,
        IFaceTrackingActivationControl
    {
        public string SourceId { get; set; }
        public TrackingSourceKind Kind { get; set; }
        public TrackingRegion Regions { get; set; }
        public TrackingSourceHealthState HealthState { get; set; }
        public TrackingFrame FaceFrame { get; set; }
        public TrackingFrame BodyHandsFrame { get; set; }
        public TrackingFrame HumanoidPoseFrame { get; set; }
        public TrackingFrame ExpressionFrame { get; set; }

        public TrackingPresenceSnapshot Presence
        {
            get;
            set;
        }

        public bool FaceTrackingEnabled
        {
            get;
            private set;
        } = true;

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            if ((Regions & region) == 0)
            {
                snapshot = default;
                return false;
            }

            TrackingFrame latest;

            if ((region &
                 TrackingRegion.Expressions) != 0)
            {
                latest =
                    ExpressionFrame;
            }
            else if ((region &
                      TrackingRegion.FullBody) != 0)
            {
                latest =
                    HumanoidPoseFrame;
            }
            else if ((region &
                      TrackingRegion.UpperBody) != 0)
            {
                latest =
                    BodyHandsFrame;
            }
            else
            {
                latest =
                    FaceFrame;
            }

            snapshot =
                new TrackingSourceHealthSnapshot(
                    SourceId,
                    Kind,
                    Regions,
                    new TrackingSourceHealth(
                        HealthState,
                        latest?.SourceTimestampUs ?? 0,
                        latest?.Confidence ?? float.NaN,
                        null),
                    latest?.RuntimeTimestampUs ?? 0);
            return true;
        }

        public void SetFaceTrackingEnabled(
            bool enabled)
        {
            FaceTrackingEnabled = enabled;
        }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame =
                FaceTrackingEnabled
                    ? FaceFrame
                    : null;

            return frame != null;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame =
                BodyHandsFrame;
            return frame != null;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame =
                HumanoidPoseFrame;
            return frame != null;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = ExpressionFrame;
            return frame != null;
        }
    }
}
