using System;
using System.Collections.Generic;
using System.Reflection;
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
            GameObject arKitReceiverRoot = null;
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
                ValidatePendingSubmissionTracker(
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

                var invalidNormalizedAudio =
                    AudioDrivenExpressionMath
                        .NormalizeLevel(
                            rms: 0.06f,
                            threshold:
                                float.NaN,
                            gain:
                                float.PositiveInfinity);
                var invalidSmoothedAudio =
                    AudioDrivenExpressionMath
                        .Smooth(
                            current:
                                float.NaN,
                            target:
                                float.PositiveInfinity,
                            deltaSeconds:
                                float.NaN,
                            attackSeconds:
                                float.NaN,
                            releaseSeconds:
                                float.NegativeInfinity);

                Expect(
                    float.IsFinite(
                        invalidNormalizedAudio) &&
                    Mathf.Approximately(
                        invalidNormalizedAudio,
                        0f) &&
                    float.IsFinite(
                        invalidSmoothedAudio) &&
                    Mathf.Approximately(
                        invalidSmoothedAudio,
                        0f),
                    "audio fallback math must contain non-finite configuration/input values instead of publishing NaN",
                    failures);

                ValidateAudioSnapshotSuppression(
                    failures);

                var mediaPipeSecondsToMicroseconds =
                    typeof(
                        MediaPipeWebcamTrackingRunner)
                    .GetMethod(
                        "SecondsToMicroseconds",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);

                if (mediaPipeSecondsToMicroseconds == null)
                {
                    failures.Add(
                        "MediaPipe presence timing conversion helper was not found");
                }
                else
                {
                    var invalidTimingUs =
                        (long)
                            mediaPipeSecondsToMicroseconds
                                .Invoke(
                                    null,
                                    new object[]
                                    {
                                        float.NaN
                                    });
                    var saturatedTimingUs =
                        (long)
                            mediaPipeSecondsToMicroseconds
                                .Invoke(
                                    null,
                                    new object[]
                                    {
                                        float.MaxValue
                                    });

                    Expect(
                        invalidTimingUs == 0L &&
                        saturatedTimingUs ==
                            long.MaxValue,
                        "MediaPipe presence timing conversion must reject non-finite seconds and saturate oversized finite values",
                        failures);
                }

                var arKitType =
                    typeof(IFacialMocapUdpReceiver);
                var arKitSecondsToMicroseconds =
                    arKitType.GetMethod(
                        "SecondsToMicroseconds",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);
                var sanitizeArKitConfiguration =
                    arKitType.GetMethod(
                        "SanitizeReceiverConfiguration",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

                arKitReceiverRoot =
                    new GameObject(
                        "VCR P3 ARKit numeric validation");
                var arKitReceiver =
                    arKitReceiverRoot.AddComponent<
                        IFacialMocapUdpReceiver>();

                arKitType.GetField(
                        "handshakeRetrySeconds",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        float.NaN);
                arKitType.GetField(
                        "sourceStaleSeconds",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        float.PositiveInfinity);
                arKitType.GetField(
                        "restoreStabilitySeconds",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        float.NegativeInfinity);
                arKitType.GetField(
                        "headPositionScale",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        float.NaN);
                arKitType.GetField(
                        "headEulerSigns",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        new Vector3(
                            float.NaN,
                            float.PositiveInfinity,
                            1f));
                arKitType.GetField(
                        "headPositionSigns",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        arKitReceiver,
                        new Vector3(
                            1f,
                            float.NegativeInfinity,
                            float.NaN));

                sanitizeArKitConfiguration?.Invoke(
                    arKitReceiver,
                    null);

                var sanitizedHandshake =
                    (float)
                        arKitType.GetField(
                                "handshakeRetrySeconds",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);
                var sanitizedStale =
                    (float)
                        arKitType.GetField(
                                "sourceStaleSeconds",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);
                var sanitizedRestore =
                    (float)
                        arKitType.GetField(
                                "restoreStabilitySeconds",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);
                var sanitizedScale =
                    (float)
                        arKitType.GetField(
                                "headPositionScale",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);
                var sanitizedEulerSigns =
                    (Vector3)
                        arKitType.GetField(
                                "headEulerSigns",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);
                var sanitizedPositionSigns =
                    (Vector3)
                        arKitType.GetField(
                                "headPositionSigns",
                                BindingFlags.Instance |
                                BindingFlags.NonPublic)
                            ?.GetValue(
                                arKitReceiver);

                var invalidArKitTimingUs =
                    arKitSecondsToMicroseconds == null
                        ? -1L
                        : (long)
                            arKitSecondsToMicroseconds
                                .Invoke(
                                    null,
                                    new object[]
                                    {
                                        float.NaN
                                    });
                var saturatedArKitTimingUs =
                    arKitSecondsToMicroseconds == null
                        ? -1L
                        : (long)
                            arKitSecondsToMicroseconds
                                .Invoke(
                                    null,
                                    new object[]
                                    {
                                        float.MaxValue
                                    });

                Expect(
                    sanitizeArKitConfiguration != null &&
                    arKitSecondsToMicroseconds != null &&
                    Mathf.Approximately(
                        sanitizedHandshake,
                        2f) &&
                    Mathf.Approximately(
                        sanitizedStale,
                        1f) &&
                    Mathf.Approximately(
                        sanitizedRestore,
                        0.15f) &&
                    Mathf.Approximately(
                        sanitizedScale,
                        1f) &&
                    sanitizedEulerSigns ==
                        new Vector3(
                            -1f,
                            -1f,
                            1f) &&
                    sanitizedPositionSigns ==
                        new Vector3(
                            1f,
                            1f,
                            -1f) &&
                    invalidArKitTimingUs == 0L &&
                    saturatedArKitTimingUs ==
                        long.MaxValue,
                    "ARKit receiver configuration must contain non-finite timing/pose values and saturate oversized presence timing conversion",
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

                if (arKitReceiverRoot != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            arKitReceiverRoot);
                }
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P3 built-in tracking validation: PASS " +
                    "(snapshot ownership/copy isolation, borrowed pose/expression/motion lifetime semantics, selective motion-domain requests, bounded MediaPipe submission correlation, ARKit lifecycle/no-subject/source-loss distinction, capture status, audio fallback math/unchanged-snapshot suppression, disabled preprocessing path)");
                return true;
            }

            Debug.LogError(
                "VCR P3 built-in tracking validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));

            return false;
        }

        private static void ValidateAudioSnapshotSuppression(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P3 Audio Snapshot Suppression");
                var source =
                    root.AddComponent<
                        AudioDrivenExpressionSource>();

                var sourceType =
                    typeof(
                        AudioDrivenExpressionSource);
                var sanitizeConfiguration =
                    sourceType.GetMethod(
                        "SanitizeConfiguration",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                var noiseThresholdField =
                    sourceType.GetField(
                        "noiseThreshold",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                var gainField =
                    sourceType.GetField(
                        "gain",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                var attackField =
                    sourceType.GetField(
                        "attackSeconds",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                var releaseField =
                    sourceType.GetField(
                        "releaseSeconds",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

                if (sanitizeConfiguration == null ||
                    noiseThresholdField == null ||
                    gainField == null ||
                    attackField == null ||
                    releaseField == null)
                {
                    failures.Add(
                        "audio fallback numeric-sanitization reflection contract is incomplete");
                    return;
                }

                noiseThresholdField.SetValue(
                    source,
                    float.NaN);
                gainField.SetValue(
                    source,
                    float.PositiveInfinity);
                attackField.SetValue(
                    source,
                    float.NaN);
                releaseField.SetValue(
                    source,
                    float.NegativeInfinity);

                sanitizeConfiguration.Invoke(
                    source,
                    null);

                Expect(
                    Mathf.Approximately(
                        (float)noiseThresholdField.GetValue(
                            source),
                        0.01f) &&
                    Mathf.Approximately(
                        (float)gainField.GetValue(
                            source),
                        18f) &&
                    Mathf.Approximately(
                        (float)attackField.GetValue(
                            source),
                        0.035f) &&
                    Mathf.Approximately(
                        (float)releaseField.GetValue(
                            source),
                        0.12f),
                    "audio fallback component must restore safe finite defaults for non-finite serialized configuration",
                    failures);

                var updateValue =
                    sourceType.GetMethod(
                        "UpdateValue",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

                var valueField =
                    typeof(AudioDrivenExpressionSource)
                        .GetField(
                            "_value",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);

                if (updateValue == null ||
                    valueField == null)
                {
                    failures.Add(
                        "audio fallback snapshot-suppression reflection contract is incomplete");
                    return;
                }

                updateValue.Invoke(
                    source,
                    new object[]
                    {
                        0f
                    });

                var firstPublished =
                    source.TryGetLatestExpressions(
                        out var first) &&
                    first?.Expressions != null &&
                    Mathf.Approximately(
                        first.Expressions.Get(
                            StandardExpression.Aa),
                        0f);

                updateValue.Invoke(
                    source,
                    new object[]
                    {
                        0f
                    });

                var stableReference =
                    source.TryGetLatestExpressions(
                        out var second) &&
                    ReferenceEquals(
                        first,
                        second);

                valueField.SetValue(
                    source,
                    1f);
                updateValue.Invoke(
                    source,
                    new object[]
                    {
                        1f
                    });

                var changedPublished =
                    source.TryGetLatestExpressions(
                        out var changed) &&
                    changed?.Expressions != null &&
                    !ReferenceEquals(
                        second,
                        changed) &&
                    Mathf.Approximately(
                        changed.Expressions.Get(
                            StandardExpression.Aa),
                        1f);

                Expect(
                    firstPublished &&
                    stableReference &&
                    changedPublished,
                    "audio fallback must preserve the last immutable frame while the mouth value is unchanged and publish again at a changed boundary value",
                    failures);

                Expect(
                    source.TryGetSourceHealth(
                        TrackingRegion.Expressions,
                        out var health) &&
                    health.Health
                        .LastUpdateTimestampUs > 0 &&
                    health.LastFrameRuntimeTimestampUs > 0,
                    "audio fallback health sampling must remain live when unchanged frame publication is suppressed",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "audio fallback snapshot-suppression validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }
            }
        }

        private static void ValidatePendingSubmissionTracker(
            List<string> failures)
        {
            try
            {
                var trackerType =
                    typeof(MediaPipeFaceSource)
                        .Assembly
                        .GetType(
                            "VCR.Runtime.Tracking.MediaPipe.PendingSubmissionTracker",
                            throwOnError: false);

                if (trackerType == null)
                {
                    failures.Add(
                        "MediaPipe pending submission tracker type was not found");
                    return;
                }

                var tracker =
                    Activator.CreateInstance(
                        trackerType,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic,
                        binder: null,
                        args:
                            new object[]
                            {
                                3
                            },
                        culture: null);

                var record =
                    trackerType.GetMethod(
                        "Record",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);
                var tryComplete =
                    trackerType.GetMethod(
                        "TryComplete",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);
                var clear =
                    trackerType.GetMethod(
                        "Clear",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);
                var count =
                    trackerType.GetProperty(
                        "Count",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);
                var evictions =
                    trackerType.GetProperty(
                        "EvictionCount",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);

                if (tracker == null ||
                    record == null ||
                    tryComplete == null ||
                    clear == null ||
                    count == null ||
                    evictions == null)
                {
                    failures.Add(
                        "MediaPipe pending submission tracker reflection contract is incomplete");
                    return;
                }

                record.Invoke(
                    tracker,
                    new object[]
                    {
                        1L,
                        10L
                    });
                record.Invoke(
                    tracker,
                    new object[]
                    {
                        2L,
                        20L
                    });

                var completedArgs =
                    new object[]
                    {
                        1L,
                        0L
                    };
                var firstCompleted =
                    (bool)tryComplete.Invoke(
                        tracker,
                        completedArgs);

                record.Invoke(
                    tracker,
                    new object[]
                    {
                        3L,
                        30L
                    });
                record.Invoke(
                    tracker,
                    new object[]
                    {
                        4L,
                        40L
                    });
                record.Invoke(
                    tracker,
                    new object[]
                    {
                        5L,
                        50L
                    });

                var evictedArgs =
                    new object[]
                    {
                        2L,
                        0L
                    };
                var oldestStillPresent =
                    (bool)tryComplete.Invoke(
                        tracker,
                        evictedArgs);

                var retainedArgs =
                    new object[]
                    {
                        3L,
                        0L
                    };
                var retainedCompleted =
                    (bool)tryComplete.Invoke(
                        tracker,
                        retainedArgs);

                var countBeforeClear =
                    (int)count.GetValue(
                        tracker);
                var evictionCount =
                    (long)evictions.GetValue(
                        tracker);

                clear.Invoke(
                    tracker,
                    null);

                var countAfterClear =
                    (int)count.GetValue(
                        tracker);

                Expect(
                    firstCompleted &&
                    (long)completedArgs[1] == 10L &&
                    !oldestStillPresent &&
                    retainedCompleted &&
                    (long)retainedArgs[1] == 30L &&
                    countBeforeClear == 2 &&
                    evictionCount == 1L &&
                    countAfterClear == 0,
                    "MediaPipe pending timestamp correlation must stay fixed-capacity and evict only the oldest still-pending submission",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "MediaPipe pending submission tracker validation unexpected exception: " +
                    exception);
            }
        }

        private static void ValidateSnapshotOwnership(
            List<string> failures)
        {
            var borrowedBones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var borrowedPresence =
                new bool[
                    (int)HumanoidBoneId.Count];
            var borrowedHips =
                (int)HumanoidBoneId.Hips;

            borrowedBones[borrowedHips] =
                new NormalizedBonePose(
                    new TrackingVector3(
                        1f,
                        0f,
                        0f),
                    TrackingQuaternion.Identity);
            borrowedPresence[borrowedHips] =
                true;

            var borrowedPose =
                new BorrowedHumanoidPose(
                    HumanoidPoseSpace.OriginalLocal,
                    TrackingVector3.Zero,
                    TrackingQuaternion.Identity,
                    borrowedBones,
                    borrowedPresence);

            var borrowedInitial =
                borrowedPose.TryGet(
                    HumanoidBoneId.Hips,
                    out var initialBorrowedHips) &&
                Math.Abs(
                    initialBorrowedHips
                        .LocalPosition.X -
                    1f) <
                0.0001f;

            borrowedBones[borrowedHips] =
                new NormalizedBonePose(
                    new TrackingVector3(
                        2f,
                        0f,
                        0f),
                    TrackingQuaternion.Identity);

            var borrowedReflectsReuse =
                borrowedPose.TryGet(
                    HumanoidBoneId.Hips,
                    out var reusedBorrowedHips) &&
                Math.Abs(
                    reusedBorrowedHips
                        .LocalPosition.X -
                    2f) <
                0.0001f;

            Expect(
                borrowedPose.IsValid &&
                borrowedInitial &&
                borrowedReflectsReuse &&
                !borrowedPose.TryGet(
                    (HumanoidBoneId)(-1),
                    out _) &&
                !default(BorrowedHumanoidPose)
                    .IsValid,
                "borrowed humanoid poses must be explicit non-owning views whose backing data may change on provider reuse",
                failures);

            var borrowedStandard =
                new float[
                    (int)StandardExpression.Count];
            borrowedStandard[
                (int)StandardExpression.Happy] =
                    0.25f;
            var borrowedCustom =
                new[]
                {
                    new NamedExpressionValue(
                        "borrowed-a",
                        0.5f),
                    new NamedExpressionValue(
                        "stale-hidden",
                        1f)
                };

            var borrowedExpressions =
                new BorrowedExpressionState(
                    borrowedStandard,
                    borrowedCustom,
                    customCount: 1);

            var borrowedMotion =
                new BorrowedMotionSample(
                    TrackingRegion.FullBody |
                    TrackingRegion.Expressions,
                    borrowedPose,
                    borrowedExpressions);

            borrowedStandard[
                (int)StandardExpression.Happy] =
                    0.75f;

            Expect(
                borrowedExpressions.IsValid &&
                borrowedExpressions.Custom.Length == 1 &&
                string.Equals(
                    borrowedExpressions.Custom[0].Name,
                    "borrowed-a",
                    StringComparison.Ordinal) &&
                Math.Abs(
                    borrowedExpressions.Get(
                        StandardExpression.Happy) -
                    0.75f) <
                0.0001f &&
                borrowedMotion.HasHumanoidPose &&
                borrowedMotion.HasExpressions &&
                !default(BorrowedExpressionState)
                    .IsValid &&
                !default(BorrowedMotionSample)
                    .HasExpressions,
                "borrowed expression/motion views must expose only their active ranges and remain explicitly non-owning",
                failures);

            var poseOnlyRequest =
                new NormalizedMotionSnapshotRequest(
                    includeHumanoidPose: true,
                    includeExpressions: false);
            var expressionOnlyRequest =
                new NormalizedMotionSnapshotRequest(
                    includeHumanoidPose: false,
                    includeExpressions: true);
            var emptyRequest =
                new NormalizedMotionSnapshotRequest(
                    includeHumanoidPose: false,
                    includeExpressions: false);

            Expect(
                poseOnlyRequest.HasAnyDomain &&
                poseOnlyRequest.IncludeHumanoidPose &&
                !poseOnlyRequest.IncludeExpressions &&
                expressionOnlyRequest.HasAnyDomain &&
                !expressionOnlyRequest.IncludeHumanoidPose &&
                expressionOnlyRequest.IncludeExpressions &&
                !emptyRequest.HasAnyDomain &&
                NormalizedMotionSnapshotRequest
                    .Full
                    .IncludeHumanoidPose &&
                NormalizedMotionSnapshotRequest
                    .Full
                    .IncludeExpressions,
                "selective motion snapshot requests must preserve independent pose/expression domain flags",
                failures);

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
