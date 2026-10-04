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
            RunParameterMappingChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P13 source validation: PASS " +
                    "(backend-neutral 2D host, parameter mapping validation/evaluation, model/backend validation, supported-domain polling, immutable-frame deduplication, failure metrics)");
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

        private static void RunParameterMappingChecks(
            List<string> failures)
        {
            Character2DParameterMappingProfile profile =
                null;
            GameObject root =
                null;

            try
            {
                profile =
                    ScriptableObject.CreateInstance<
                        Character2DParameterMappingProfile>();

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamMouthOpen",
                        SourceKind =
                            Character2DParameterSourceKind
                                .FaceCoefficient,
                        FaceCoefficient =
                            FaceCoefficient.JawOpen,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = -1f,
                        OutputMax = 1f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            false
                    },
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamHappy",
                        SourceKind =
                            Character2DParameterSourceKind
                                .StandardExpression,
                        StandardExpression =
                            StandardExpression.Happy,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = 0f,
                        OutputMax = 2f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            true,
                        DefaultInputValue = 0.25f
                    },
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamHeadX",
                        SourceKind =
                            Character2DParameterSourceKind
                                .HeadPositionX,
                        InputMin = -1f,
                        InputMax = 1f,
                        OutputMin = -10f,
                        OutputMax = 10f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            false
                    });

                var faceCoefficients =
                    new float[
                        (int)FaceCoefficient.Count];
                faceCoefficients[
                        (int)FaceCoefficient.JawOpen] =
                    0.75f;

                var standardExpressions =
                    new float[
                        (int)StandardExpression.Count];
                standardExpressions[
                        (int)StandardExpression.Happy] =
                    0.4f;

                var face =
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        new TrackingVector3(
                            0.5f,
                            0f,
                            0f),
                        faceCoefficients);
                var expressions =
                    new NormalizedExpressionState(
                        standardExpressions);

                var faceFrame =
                    new TrackingFrame(
                        10,
                        10000,
                        TrackingRegion.Face,
                        1f,
                        true,
                        face:
                            face,
                        sourceId:
                            "p13-mapping");
                var expressionFrame =
                    new TrackingFrame(
                        11,
                        11000,
                        TrackingRegion.Expressions,
                        1f,
                        true,
                        expressions:
                            expressions,
                        sourceId:
                            "p13-mapping");

                var snapshot =
                    new Character2DInputSnapshot(
                        faceFrame,
                        null,
                        null,
                        expressionFrame);

                Expect(
                    Character2DParameterMapper
                        .TryValidate(
                            profile,
                            out var validationError),
                    "2D parameter mapping profile must accept a valid backend-bound mapping: " +
                    validationError,
                    failures);

                Expect(
                    Character2DParameterMapper
                        .TryEvaluate(
                            profile,
                            "p13.fake",
                            snapshot,
                            out var values,
                            out var evaluateError) &&
                    values.Length == 3 &&
                    values[0].ParameterId ==
                        "ParamMouthOpen" &&
                    Mathf.Approximately(
                        values[0].Value,
                        0.5f) &&
                    values[1].ParameterId ==
                        "ParamHappy" &&
                    Mathf.Approximately(
                        values[1].Value,
                        0.8f) &&
                    values[2].ParameterId ==
                        "ParamHeadX" &&
                    Mathf.Approximately(
                        values[2].Value,
                        5f),
                    "2D parameter mapper must evaluate face/expression/head-position bindings with configured ranges: " +
                    evaluateError,
                    failures);

                root =
                    new GameObject(
                        "P13 2D Mapping Validation");
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
                provider.Face =
                    faceFrame;
                provider.Expressions =
                    expressionFrame;

                runtime.Configure(
                    backend,
                    provider,
                    Character2DInputDomain.Face |
                    Character2DInputDomain.Expressions);

                profile.Configure(
                    "wrong.backend",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamMouthOpen",
                        SourceKind =
                            Character2DParameterSourceKind
                                .FaceCoefficient,
                        FaceCoefficient =
                            FaceCoefficient.JawOpen
                    });

                runtime.ConfigureParameterMapping(
                    profile);

                Expect(
                    !runtime.TryLoadModel(
                        new Character2DModelRequest(
                            backend.BackendId,
                            "mapped-model-invalid",
                            "/tmp/mapped-model-invalid"),
                        out var hostMismatchError) &&
                    hostMismatchError != null &&
                    hostMismatchError.IndexOf(
                        "does not match",
                        StringComparison.OrdinalIgnoreCase) >=
                        0 &&
                    backend.Status.State ==
                        Character2DBackendState.Ready,
                    "2D host must fail model load before backend activation when the configured mapping profile targets another backend",
                    failures);

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamMouthOpen",
                        SourceKind =
                            Character2DParameterSourceKind
                                .FaceCoefficient,
                        FaceCoefficient =
                            FaceCoefficient.JawOpen,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = -1f,
                        OutputMax = 1f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            false
                    },
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamHappy",
                        SourceKind =
                            Character2DParameterSourceKind
                                .StandardExpression,
                        StandardExpression =
                            StandardExpression.Happy,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = 0f,
                        OutputMax = 2f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            true,
                        DefaultInputValue = 0.25f
                    });

                Expect(
                    runtime.TryLoadModel(
                        new Character2DModelRequest(
                            backend.BackendId,
                            "mapped-model",
                            "/tmp/mapped-model"),
                        out var mappedLoadError) &&
                    runtime.ProcessLatest(
                        out var mappedApplyError) &&
                    backend.ApplyCount == 0 &&
                    backend.ParameterApplyCount == 1 &&
                    backend.LastParameterValues !=
                        null &&
                    backend.LastParameterValues.Length ==
                        2 &&
                    backend.LastParameterValues[0]
                            .ParameterId ==
                        "ParamMouthOpen" &&
                    Mathf.Approximately(
                        backend.LastParameterValues[0]
                            .Value,
                        0.5f) &&
                    backend.LastParameterValues[1]
                            .ParameterId ==
                        "ParamHappy" &&
                    Mathf.Approximately(
                        backend.LastParameterValues[1]
                            .Value,
                        0.8f),
                    "2D host must route a configured mapping profile through ICharacter2DParameterSink instead of the raw snapshot path: " +
                    mappedLoadError +
                    " / " +
                    mappedApplyError,
                    failures);

                var faceOnlySnapshot =
                    new Character2DInputSnapshot(
                        faceFrame,
                        null,
                        null,
                        null);

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamHappy",
                        SourceKind =
                            Character2DParameterSourceKind
                                .StandardExpression,
                        StandardExpression =
                            StandardExpression.Happy,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = 0f,
                        OutputMax = 2f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            true,
                        DefaultInputValue = 0.25f
                    });

                Expect(
                    Character2DParameterMapper
                        .TryEvaluate(
                            profile,
                            "p13.fake",
                            faceOnlySnapshot,
                            out var fallbackValues,
                            out var fallbackError) &&
                    fallbackValues.Length == 1 &&
                    Mathf.Approximately(
                        fallbackValues[0].Value,
                        0.5f),
                    "2D parameter mapper must use authored defaults when a source domain is unavailable: " +
                    fallbackError,
                    failures);

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "ParamHappy",
                        SourceKind =
                            Character2DParameterSourceKind
                                .StandardExpression,
                        StandardExpression =
                            StandardExpression.Happy,
                        InputMin = 0f,
                        InputMax = 1f,
                        OutputMin = 0f,
                        OutputMax = 1f,
                        ClampInput = true,
                        UseDefaultWhenUnavailable =
                            false
                    });

                Expect(
                    Character2DParameterMapper
                        .TryEvaluate(
                            profile,
                            "p13.fake",
                            faceOnlySnapshot,
                            out var skippedValues,
                            out var skippedError) &&
                    skippedValues.Length == 0,
                    "2D parameter mapper must skip unavailable sources when no default is requested: " +
                    skippedError,
                    failures);

                Expect(
                    !Character2DParameterMapper
                        .TryEvaluate(
                            profile,
                            "other.backend",
                            faceOnlySnapshot,
                            out _,
                            out var backendError) &&
                    backendError != null &&
                    backendError.IndexOf(
                        "does not match",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "2D parameter mapper must reject a profile authored for another backend",
                    failures);

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "Duplicate"
                    },
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "Duplicate"
                    });

                Expect(
                    !Character2DParameterMapper
                        .TryValidate(
                            profile,
                            out var duplicateError) &&
                    duplicateError != null &&
                    duplicateError.IndexOf(
                        "Duplicate",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "2D parameter mapping validation must reject duplicate target parameter ids",
                    failures);

                profile.Configure(
                    "p13.fake",
                    new Character2DParameterBinding
                    {
                        TargetParameterId =
                            "InvalidExpression",
                        SourceKind =
                            Character2DParameterSourceKind
                                .StandardExpression,
                        StandardExpression =
                            (StandardExpression)999
                    });

                Expect(
                    !Character2DParameterMapper
                        .TryValidate(
                            profile,
                            out var expressionError) &&
                    expressionError != null &&
                    expressionError.IndexOf(
                        "invalid standard expression",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "2D parameter mapping validation must reject out-of-range standard-expression enum values",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P13 2D parameter mapping validation unexpected exception: " +
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

                if (profile != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            profile);
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
        ICharacter2DBackend,
        ICharacter2DParameterSink
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
        public int ParameterApplyCount;
        public Character2DParameterValue[] LastParameterValues;

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

        public bool TryApplyParameters(
            Character2DParameterValue[] values,
            out string error)
        {
            error = null;
            LastParameterValues =
                values;
            ParameterApplyCount++;
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
