using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Presentation2D;
using VCR.Runtime.Tracking;

namespace VCR.Editor.P13
{
    internal static class P13SourceValidation
    {
        [MenuItem("VCR/P13/Run Source Validation")]
        private static void RunFromMenu()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            RunBackendHostChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P13 source validation: PASS " +
                    "(backend-neutral 2D host, model/backend validation, supported-domain polling, immutable-frame deduplication, failure metrics)");
                return true;
            }

            Debug.LogError(
                "VCR P13 source validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void RunBackendHostChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P13 2D Host Validation");
                var backend =
                    root.AddComponent<
                        P13FakeCharacter2DBackend>();
                var provider =
                    root.AddComponent<
                        P13FakeTrackingProvider>();
                var runtime =
                    root.AddComponent<
                        Character2DRuntime>();

                backend.SetSupportedInputs(
                    Character2DInputDomain.Face |
                    Character2DInputDomain.Expressions);

                runtime.Configure(
                    backend,
                    provider,
                    Character2DInputDomain.Face |
                    Character2DInputDomain.BodyHands |
                    Character2DInputDomain.HumanoidPose |
                    Character2DInputDomain.Expressions);

                Expect(
                    runtime.EffectiveInputs ==
                    (Character2DInputDomain.Face |
                     Character2DInputDomain.Expressions),
                    "2D host must intersect requested input domains with backend-supported domains",
                    failures);

                Expect(
                    !runtime.TryLoadModel(
                        new Character2DModelRequest(
                            "wrong-backend",
                            "model",
                            "/tmp/model"),
                        out var mismatchError) &&
                    mismatchError != null &&
                    mismatchError.IndexOf(
                        "does not match",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "2D host must reject model requests for a different backend id",
                    failures);

                Expect(
                    runtime.TryLoadModel(
                        new Character2DModelRequest(
                            backend.BackendId,
                            "model-a",
                            "/tmp/model-a"),
                        out var loadError) &&
                    backend.Status.State ==
                        Character2DBackendState.ModelLoaded &&
                    backend.Status.ModelId ==
                        "model-a",
                    "2D host must load a model through the active backend contract: " +
                    loadError,
                    failures);

                var faceA =
                    Frame(
                        1,
                        TrackingRegion.Face);
                var expressionA =
                    Frame(
                        1,
                        TrackingRegion.Expressions);
                provider.Face =
                    faceA;
                provider.Expressions =
                    expressionA;
                provider.BodyHands =
                    Frame(
                        1,
                        TrackingRegion.UpperBody);
                provider.HumanoidPose =
                    Frame(
                        1,
                        TrackingRegion.FullBody);

                Expect(
                    runtime.ProcessLatest(
                        out var firstApplyError) &&
                    backend.ApplyCount ==
                        1 &&
                    provider.FacePollCount ==
                        1 &&
                    provider.ExpressionPollCount ==
                        1 &&
                    provider.BodyHandsPollCount ==
                        0 &&
                    provider.HumanoidPosePollCount ==
                        0 &&
                    ReferenceEquals(
                        backend.LastSnapshot.Face,
                        faceA) &&
                    ReferenceEquals(
                        backend.LastSnapshot.Expressions,
                        expressionA),
                    "2D host must poll/apply only effective backend-supported domains: " +
                    firstApplyError,
                    failures);

                Expect(
                    !runtime.ProcessLatest(
                        out var unchangedError) &&
                    unchangedError == null &&
                    backend.ApplyCount ==
                        1 &&
                    provider.FacePollCount ==
                        2 &&
                    provider.ExpressionPollCount ==
                        2,
                    "2D host must skip backend apply when all immutable frame references are unchanged",
                    failures);

                var faceSameSequenceNewObject =
                    Frame(
                        1,
                        TrackingRegion.Face);
                provider.Face =
                    faceSameSequenceNewObject;

                Expect(
                    runtime.ProcessLatest(
                        out var sameSequenceError) &&
                    backend.ApplyCount ==
                        2 &&
                    ReferenceEquals(
                        backend.LastSnapshot.Face,
                        faceSameSequenceNewObject),
                    "2D host must treat a new immutable frame object as new input even when its sequence value is unchanged: " +
                    sameSequenceError,
                    failures);

                backend.FailNextApply =
                    true;
                var expressionB =
                    Frame(
                        2,
                        TrackingRegion.Expressions);
                provider.Expressions =
                    expressionB;

                Expect(
                    !runtime.ProcessLatest(
                        out var applyFailureError) &&
                    applyFailureError != null &&
                    runtime.LastError !=
                        null &&
                    backend.ApplyAttemptCount ==
                        3 &&
                    backend.ApplyCount ==
                        2,
                    "2D host must surface backend apply failure without accepting the failed frame as applied",
                    failures);

                Expect(
                    runtime.ProcessLatest(
                        out var retryError) &&
                    backend.ApplyCount ==
                        3 &&
                    ReferenceEquals(
                        backend.LastSnapshot.Expressions,
                        expressionB),
                    "2D host must retry the same frame after a transient backend apply failure because failed input was not cached: " +
                    retryError,
                    failures);

                runtime.UnloadModel();

                Expect(
                    backend.Status.State ==
                        Character2DBackendState.Ready,
                    "2D host unload must return the fake backend to Ready",
                    failures);

                Expect(
                    runtime.TryLoadModel(
                        new Character2DModelRequest(
                            backend.BackendId,
                            "model-b",
                            "/tmp/model-b"),
                        out var reloadError) &&
                    runtime.ProcessLatest(
                        out var postReloadApplyError) &&
                    backend.ApplyCount ==
                        4,
                    "2D host unload/reload must reset frame-reference cache so the current tracking snapshot is applied to the new model: " +
                    reloadError +
                    " / " +
                    postReloadApplyError,
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                runtime.CollectMetrics(
                    metrics);

                Expect(
                    Metric(
                        metrics,
                        "presentation2d.model.loaded") ==
                        1.0 &&
                    Metric(
                        metrics,
                        "presentation2d.apply.count") ==
                        4.0 &&
                    Metric(
                        metrics,
                        "presentation2d.apply.failures") ==
                        1.0 &&
                    Metric(
                        metrics,
                        "presentation2d.load.count") ==
                        2.0 &&
                    Metric(
                        metrics,
                        "presentation2d.load.failures") ==
                        1.0,
                    "2D host metrics must expose model/apply success and failure counts",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P13 2D host validation unexpected exception: " +
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

        private static TrackingFrame Frame(
            long sequence,
            TrackingRegion region) =>
                new(
                    sequence,
                    sequence * 1000,
                    region,
                    1f,
                    true,
                    sourceId:
                        "p13-validation",
                    runtimeTimestampUs:
                        sequence * 1000);

        private static double Metric(
            List<RuntimeMetric> metrics,
            string name)
        {
            foreach (var metric in metrics)
            {
                if (metric.Name ==
                    name)
                {
                    return metric.Value;
                }
            }

            return double.NaN;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }

    internal sealed class P13FakeCharacter2DBackend :
        MonoBehaviour,
        ICharacter2DBackend
    {
        private Character2DInputDomain _supportedInputs =
            Character2DInputDomain.Face |
            Character2DInputDomain.Expressions;
        private Character2DBackendStatus _status =
            new(
                Character2DBackendState.Ready,
                null,
                null);

        public string BackendId =>
            "p13.fake";
        public Character2DInputDomain SupportedInputs =>
            _supportedInputs;
        public Character2DBackendStatus Status =>
            _status;

        public bool FailNextApply;
        public int ApplyAttemptCount;
        public int ApplyCount;
        public Character2DInputSnapshot LastSnapshot;

        public void SetSupportedInputs(
            Character2DInputDomain value)
        {
            _supportedInputs =
                value;
        }

        public bool TryLoadModel(
            Character2DModelRequest request,
            out string error)
        {
            error = null;
            _status =
                new Character2DBackendStatus(
                    Character2DBackendState.ModelLoaded,
                    request.ModelId,
                    null);
            return true;
        }

        public void UnloadModel()
        {
            _status =
                new Character2DBackendStatus(
                    Character2DBackendState.Ready,
                    null,
                    null);
        }

        public bool TryApply(
            Character2DInputSnapshot snapshot,
            out string error)
        {
            ApplyAttemptCount++;

            if (FailNextApply)
            {
                FailNextApply =
                    false;
                error =
                    "synthetic apply failure";
                return false;
            }

            error = null;
            LastSnapshot =
                snapshot;
            ApplyCount++;
            return true;
        }
    }

    internal sealed class P13FakeTrackingProvider :
        MonoBehaviour,
        ITrackingFrameProvider
    {
        public TrackingFrame Face;
        public TrackingFrame BodyHands;
        public TrackingFrame HumanoidPose;
        public TrackingFrame Expressions;

        public int FacePollCount;
        public int BodyHandsPollCount;
        public int HumanoidPosePollCount;
        public int ExpressionPollCount;

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            FacePollCount++;
            frame =
                Face;
            return frame != null;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            BodyHandsPollCount++;
            frame =
                BodyHands;
            return frame != null;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            HumanoidPosePollCount++;
            frame =
                HumanoidPose;
            return frame != null;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            ExpressionPollCount++;
            frame =
                Expressions;
            return frame != null;
        }
    }
}
