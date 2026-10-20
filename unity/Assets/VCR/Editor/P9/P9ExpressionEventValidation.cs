using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P9
{
    internal static class P9ExpressionEventValidation
    {
        public static void RunChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P9 Expression Event Validation");

                var route =
                    root.AddComponent<
                        P9ExpressionFakeRouteProvider>();
                var source =
                    root.AddComponent<
                        ManualExpressionLayerSource>();
                var mixer =
                    root.AddComponent<
                        MotionExpressionMixer>();
                var handler =
                    root.AddComponent<
                        ExpressionEventActionHandler>();

                var nowUs =
                    VCR.Runtime.Core.MonotonicClock
                        .NowMicroseconds();

                route.ExpressionFrame =
                    CreateExpressionFrame(
                        "route-expression",
                        sequence: 1,
                        nowUs,
                        aa: 0.6f,
                        happy: 0f);

                mixer.SetRoutedProvider(
                    route);
                mixer.SetExpressionLayerProvider(
                    source);
                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Maximum,
                    weight: 1f,
                    deadzone: 0f,
                    smoothing: 0f);

                handler.SetExpressionSource(
                    source,
                    "expression.event");

                Expect(
                    !((object)source is
                        ITrackingPresenceProvider),
                    "manual expression layer must not become performer-presence evidence",
                    failures);

                Expect(
                    typeof(ManualExpressionLayerSource)
                        .GetMethod(
                            "Update",
                            BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic) ==
                    null,
                    "manual expression layer must not add an always-on Update loop",
                    failures);

                var command =
                    new EventActionCommand(
                        "expression-rule",
                        EventActionTypes.ExpressionSet,
                        "expression.event",
                        "Joy",
                        null,
                        0.75,
                        true,
                        100);

                Expect(
                    handler.CanHandle(
                        command) &&
                    handler.TryExecute(
                        command,
                        out var error) &&
                    string.IsNullOrEmpty(
                        error),
                    "expression.set must accept standard expression aliases through the application-level handler",
                    failures);

                var sourceSequence =
                    source.Sequence;

                Expect(
                    handler.TryExecute(
                        command,
                        out var repeatError) &&
                    string.IsNullOrEmpty(
                        repeatError) &&
                    source.Sequence ==
                        sourceSequence,
                    "repeating the same expression value must not publish a redundant frame",
                    failures);

                InvokeMixerUpdate(
                    mixer);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var mixed) &&
                    mixed?.Expressions != null,
                    "event expression source must feed MotionExpressionMixer",
                    failures);

                ExpectClose(
                    mixed?.Expressions?.Get(
                        StandardExpression.Happy) ??
                    -1f,
                    0.75f,
                    "Joy alias must drive the normalized Happy expression",
                    failures);

                ExpectClose(
                    mixed?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.6f,
                    "Maximum event-expression blending must preserve routed lip-sync channels",
                    failures);

                var invalid =
                    new EventActionCommand(
                        "expression-rule",
                        EventActionTypes.ExpressionSet,
                        "expression.event",
                        "happy",
                        null,
                        1.5,
                        true,
                        101);

                Expect(
                    !handler.TryExecute(
                        invalid,
                        out var invalidError) &&
                    !string.IsNullOrWhiteSpace(
                        invalidError),
                    "expression.set must reject values outside the normalized 0..1 range",
                    failures);

                var unknown =
                    new EventActionCommand(
                        "expression-rule",
                        EventActionTypes.ExpressionSet,
                        "expression.event",
                        "definitely-not-an-expression",
                        null,
                        0.5,
                        true,
                        102);

                Expect(
                    !handler.TryExecute(
                        unknown,
                        out var unknownError) &&
                    !string.IsNullOrWhiteSpace(
                        unknownError),
                    "expression.set must reject unknown expression names",
                    failures);

                var wrongTarget =
                    new EventActionCommand(
                        "expression-rule",
                        EventActionTypes.ExpressionSet,
                        "expression.other",
                        "happy",
                        null,
                        0.5,
                        true,
                        103);

                Expect(
                    !handler.CanHandle(
                        wrongTarget),
                    "expression handler must honor its logical layer target id",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "expression event unexpected exception: " +
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

        private static TrackingFrame
            CreateExpressionFrame(
                string sourceId,
                long sequence,
                long runtimeTimestampUs,
                float aa,
                float happy)
        {
            var standard =
                new float[
                    (int)StandardExpression.Count];

            standard[
                (int)StandardExpression.Aa] =
                aa;
            standard[
                (int)StandardExpression.Happy] =
                happy;

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
                    new NormalizedExpressionState(
                        standard),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static void InvokeMixerUpdate(
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

    internal sealed class P9ExpressionFakeRouteProvider :
        MonoBehaviour,
        ITrackingRouteProvider
    {
        public TrackingFrame ExpressionFrame
        {
            get;
            set;
        }

        public TrackingPresenceSnapshot Presence =>
            default;

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
