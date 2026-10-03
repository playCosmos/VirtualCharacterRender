using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P5
{
    public static class P5ExpressionMixerValidation
    {
        [MenuItem("VCR/P5/Validate Expression Mixer")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateMath(failures);
            ValidateComponent(failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P5 expression mixer validation: PASS " +
                    "(blend modes, deadzone, custom expressions, base passthrough, overlay blend, presence isolation)");
                return true;
            }

            Debug.LogError(
                "VCR P5 expression mixer validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateMath(
            List<string> failures)
        {
            var baseState =
                CreateExpressionState(
                    aa: 0.2f,
                    new NamedExpressionValue(
                        "customA",
                        0.2f));

            var layerState =
                CreateExpressionState(
                    aa: 0.6f,
                    new NamedExpressionValue(
                        "customA",
                        0.6f),
                    new NamedExpressionValue(
                        "customB",
                        0.8f));

            var overrideState =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Override);

            ExpectClose(
                overrideState.Get(
                    StandardExpression.Aa),
                0.4f,
                "override blend must lerp standard expressions",
                failures);

            ExpectClose(
                GetCustom(
                    overrideState,
                    "customA"),
                0.4f,
                "override blend must lerp matching custom expressions",
                failures);

            ExpectClose(
                GetCustom(
                    overrideState,
                    "customB"),
                0.4f,
                "override blend must weight layer-only custom expressions",
                failures);

            var additive =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Additive);

            ExpectClose(
                additive.Get(
                    StandardExpression.Aa),
                0.5f,
                "additive blend must add weighted layer value",
                failures);

            var maximum =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Maximum);

            ExpectClose(
                maximum.Get(
                    StandardExpression.Aa),
                0.3f,
                "maximum blend must compare base with weighted layer value",
                failures);

            ExpectClose(
                ExpressionMixerMath.ApplyDeadzone(
                    0.05f,
                    0.1f),
                0f,
                "deadzone must suppress values below threshold",
                failures);

            ExpectClose(
                ExpressionMixerMath.ApplyDeadzone(
                    0.55f,
                    0.1f),
                0.5f,
                "deadzone must rescale surviving values",
                failures);

            var alpha =
                ExpressionMixerMath.SmoothAlpha(
                    smoothingRate: 10f,
                    deltaSeconds: 0.1f);

            Expect(
                alpha > 0f &&
                alpha < 1f,
                "positive smoothing must produce an interpolation alpha inside (0, 1)",
                failures);
        }

        private static void ValidateComponent(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P5 Expression Mixer Validation");

                var route =
                    root.AddComponent<
                        P5FakeRouteProvider>();
                var layer =
                    root.AddComponent<
                        P5FakeExpressionProvider>();
                var mixer =
                    root.AddComponent<
                        MotionExpressionMixer>();

                var nowUs =
                    MonotonicClock
                        .NowMicroseconds();

                route.FaceFrame =
                    CreateFaceFrame(
                        "route-face",
                        sequence: 1,
                        nowUs);
                route.ExpressionFrame =
                    CreateExpressionFrame(
                        "route-expression",
                        sequence: 1,
                        nowUs,
                        aa: 0.2f);
                route.Presence =
                    CreatePresence(
                        nowUs);

                layer.ExpressionFrame =
                    CreateExpressionFrame(
                        "overlay-expression",
                        sequence: 1,
                        nowUs,
                        aa: 0.6f);

                mixer.SetRoutedProvider(
                    route);
                mixer.SetExpressionLayerProvider(
                    layer);
                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Additive,
                    weight: 0.5f,
                    deadzone: 0f,
                    smoothing: 0f);

                InvokeUpdate(mixer);

                Expect(
                    mixer is ITrackingMixProvider &&
                    mixer is ITrackingRouteProvider,
                    "mixer must satisfy final mix and routed provider contracts",
                    failures);

                Expect(
                    mixer.Presence.SubjectState ==
                    route.Presence.SubjectState,
                    "mixer presence must pass through routed visual presence",
                    failures);

                Expect(
                    mixer.TryGetLatestFace(
                        out var face) &&
                    ReferenceEquals(
                        face,
                        route.FaceFrame),
                    "face frames must pass through unchanged in the first P5 mixer slice",
                    failures);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var mixed) &&
                    mixed?.Expressions != null,
                    "mixer must emit an expression frame",
                    failures);

                ExpectClose(
                    mixed?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.5f,
                    "component additive blend must combine routed base and overlay",
                    failures);

                mixer.SetExpressionLayerProvider(
                    null);
                InvokeUpdate(mixer);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var baseOnly) &&
                    baseOnly?.Expressions != null,
                    "base expressions must remain available when no overlay is configured",
                    failures);

                ExpectClose(
                    baseOnly?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.2f,
                    "missing overlay must preserve routed base expressions exactly",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                mixer.CollectMetrics(metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "mixer.expression.weight",
                        out var weight) &&
                    Math.Abs(weight - 0.5) <
                    0.001,
                    "mixer diagnostics must expose expression layer weight",
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
        }

        private static NormalizedExpressionState
            CreateExpressionState(
                float aa,
                params NamedExpressionValue[] custom)
        {
            var standard =
                new float[
                    (int)StandardExpression.Count];

            standard[
                (int)StandardExpression.Aa] =
                aa;

            return new NormalizedExpressionState(
                standard,
                custom);
        }

        private static TrackingFrame
            CreateExpressionFrame(
                string sourceId,
                long sequence,
                long runtimeTimestampUs,
                float aa)
        {
            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Expressions,
                confidence:
                    1f,
                subjectDetected:
                    false,
                expressions:
                    CreateExpressionState(
                        aa),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame CreateFaceFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs)
        {
            var coefficients =
                new float[
                    (int)FaceCoefficient.Count];

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Face |
                    TrackingRegion.Head,
                confidence:
                    1f,
                subjectDetected:
                    true,
                face:
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        TrackingVector3.Zero,
                        coefficients),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingPresenceSnapshot
            CreatePresence(
                long timestampUs)
        {
            return new TrackingPresenceSnapshot(
                sequence:
                    timestampUs,
                timestampUs:
                    timestampUs,
                subjectState:
                    SubjectPresenceState.Present,
                faceSourceAvailable:
                    true,
                bodyHandsSourceAvailable:
                    false,
                fullBodySourceAvailable:
                    false,
                faceSubjectEvidence:
                    true,
                bodyHandsSubjectEvidence:
                    false,
                fullBodySubjectEvidence:
                    false,
                anySourceAvailable:
                    true,
                subjectEvidence:
                    true,
                events:
                    TrackingPresenceEvents.None);
        }

        private static void InvokeUpdate(
            MotionExpressionMixer mixer)
        {
            var method =
                typeof(MotionExpressionMixer)
                    .GetMethod(
                        "Update",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(MotionExpressionMixer)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                mixer,
                null);
        }

        private static float GetCustom(
            NormalizedExpressionState state,
            string name)
        {
            if (state == null)
            {
                return 0f;
            }

            foreach (var item in state.Custom)
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

        private static void ExpectClose(
            float actual,
            float expected,
            string message,
            List<string> failures)
        {
            if (Math.Abs(
                    actual - expected) >
                0.001f)
            {
                failures.Add(
                    message +
                    $" (expected={expected:F3}, actual={actual:F3})");
            }
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

    internal sealed class P5FakeRouteProvider :
        MonoBehaviour,
        ITrackingRouteProvider
    {
        public TrackingFrame FaceFrame { get; set; }
        public TrackingFrame ExpressionFrame { get; set; }
        public TrackingPresenceSnapshot Presence { get; set; }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = FaceFrame;
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
            frame = ExpressionFrame;
            return frame != null;
        }
    }

    internal sealed class P5FakeExpressionProvider :
        MonoBehaviour,
        ITrackingFrameProvider
    {
        public TrackingFrame ExpressionFrame { get; set; }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
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
            frame = ExpressionFrame;
            return frame != null;
        }
    }
}
