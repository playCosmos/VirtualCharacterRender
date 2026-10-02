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

                preferred.SourceId = "arkit-face";
                fallback.SourceId = "mediapipe-webcam";

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

                preferred.Presence =
                    CreatePresence(
                        MonotonicClock
                            .NowMicroseconds(),
                        faceAvailable: false,
                        faceEvidence: false);

                InvokeUpdate(router);

                var fallbackStatus =
                    router.RouteStatus;

                Expect(
                    !fallbackStatus.PreferredFaceActive,
                    "preferred face route must deactivate when its source becomes unavailable",
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
                        faceSwitches - 1.0) <
                    0.001,
                    "preferred-to-fallback transition must increment the face source-switch metric exactly once",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "tracking.route.fallback_face_inference_enabled",
                        out var fallbackEnabled) &&
                    fallbackEnabled > 0.5,
                    "routing metrics must report fallback face inference state",
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

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P4 tracking routing validation: PASS " +
                    "(preferred/fallback hot switching, face-inference suspension, route age, switch metrics)");
                return true;
            }

            Debug.LogError(
                "VCR P4 tracking routing validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));

            return false;
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
        IFaceTrackingActivationControl
    {
        public string SourceId { get; set; }
        public TrackingFrame FaceFrame { get; set; }

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
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }
    }
}
