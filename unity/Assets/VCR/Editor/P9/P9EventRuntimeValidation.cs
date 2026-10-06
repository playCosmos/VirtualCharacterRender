using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.Events;
using VCR.Runtime.Events.Unity;
using VCR.Runtime.Materials;
using VCR.Runtime.Materials.Unity;
using VCR.Runtime.Scene;

namespace VCR.Editor.P9
{
    public static class P9EventRuntimeValidation
    {
        [MenuItem("VCR/P9/Validate Event Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateEngine(failures);
            ValidateRuleBounds(failures);
            ValidateRulePersistence(failures);
            ValidateUnityDispatch(failures);
            ValidateEventHubReplacement(failures);
            ValidateDestroyedHandlerDependencies(failures);
            ValidateThrowingHandlerProbeIsolation(failures);
            ValidateMaterialAction(failures);
            P9ExpressionEventValidation.RunChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P9 event runtime validation: PASS " +
                    "(filter, condition, state mutation, numeric transform, cooldown, window rate limit, text transform, bounded rule fanout, versioned rule persistence, rule diagnostics/opt-in tracing, action cap, environment transition/camera/material scalar/vector/expression action dispatch, unhandled/ambiguous diagnostics)");
                return true;
            }

            Debug.LogError(
                "VCR P9 event runtime validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateEngine(
            List<string> failures)
        {
            var engine =
                new EventRuntimeEngine();

            var donationRule =
                new EventRuntimeRule
                {
                    Id =
                        "donation-celebrate",
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .BroadcastDonation,
                            RequireAmount =
                                true,
                            HasMinimumAmount =
                                true,
                            MinimumAmount =
                                10.0
                        },
                    StateMutations =
                        new[]
                        {
                            new EventStateMutation
                            {
                                Kind =
                                    EventStateMutationKind
                                        .AddNumber,
                                Key =
                                    "donation.total",
                                NumericSource =
                                    EventNumericValueSource
                                        .EventAmount
                            }
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "celebrate"),
                            new EventActionTemplate
                            {
                                ActionType =
                                    "test.transformed_amount",
                                HasValue =
                                    true,
                                NumericSource =
                                    EventNumericValueSource
                                        .EventAmount,
                                NumericScale =
                                    2.0,
                                NumericOffset =
                                    1.0
                            }
                        }
                };

            var unlockedChatRule =
                new EventRuntimeRule
                {
                    Id =
                        "chat-after-donation",
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .BroadcastChatMessage,
                            TextContains =
                                "go"
                        },
                    Conditions =
                        new[]
                        {
                            new EventStateCondition
                            {
                                Kind =
                                    EventStateConditionKind
                                        .NumberGreaterOrEqual,
                                Key =
                                    "donation.total",
                                NumberValue =
                                    10.0
                            }
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "party")
                        }
                };

            var missingNumericRule =
                new EventRuntimeRule
                {
                    Id =
                        "missing-number-must-not-default-zero",
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .LocalManual
                        },
                    Conditions =
                        new[]
                        {
                            new EventStateCondition
                            {
                                Kind =
                                    EventStateConditionKind
                                        .NumberLessOrEqual,
                                Key =
                                    "missing.key",
                                NumberValue =
                                    0.0
                            }
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "invalid")
                        }
                };

            engine.SetRules(
                donationRule,
                unlockedChatRule,
                missingNumericRule);

            donationRule.Id =
                "mutated-source-rule";
            donationRule.Filter.MinimumAmount =
                0.0;
            donationRule.Actions[0].ActionType =
                "mutated.source.action";
            donationRule.StateMutations[0].Key =
                "mutated.source.state";

            var output =
                new List<EventActionCommand>();

            var smallDonation =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastDonation,
                    "soop.validation",
                    1,
                    amount:
                        5.0,
                    currency:
                        "SOOP_STAR_BALLOON",
                    hasAmount:
                        true,
                    sequence:
                        1);

            engine.Process(
                smallDonation,
                output);

            Expect(
                output.Count == 0 &&
                !engine.State.Contains(
                    "donation.total"),
                "event engine must deep-clone caller-owned rules: mutating the original rule/filter/action/state graph after SetRules must not weaken the installed donation threshold or change live behavior",
                failures);

            var nonFiniteFilter =
                new EventRuleFilter
                {
                    RequireAmount =
                        true,
                    HasMinimumAmount =
                        true,
                    MinimumAmount =
                        double.NaN
                };

            Expect(
                !nonFiniteFilter.Matches(
                    smallDonation),
                "non-finite event amount thresholds must fail closed instead of weakening the filter",
                failures);

            var qualifyingDonation =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastDonation,
                    "soop.validation",
                    2,
                    amount:
                        12.0,
                    currency:
                        "SOOP_STAR_BALLOON",
                    hasAmount:
                        true,
                    sequence:
                        2);

            engine.Process(
                qualifyingDonation,
                output);

            Expect(
                output.Count == 2 &&
                output[0].ActionType ==
                    EventActionTypes
                        .EnvironmentSetState &&
                output[0].TargetId ==
                    "environment.main" &&
                output[0].Text ==
                    "celebrate",
                "qualifying donation must produce the configured application-level environment command",
                failures);

            ExpectClose(
                output[1].Value,
                25.0,
                "numeric action transform must apply event amount scale and offset",
                failures);

            ExpectClose(
                engine.State.GetNumber(
                    "donation.total"),
                12.0,
                "qualifying donation must add event amount into runtime state",
                failures);

            var unlockedChat =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastChatMessage,
                    "soop.validation",
                    3,
                    text:
                        "GO now",
                    sequence:
                        3);

            engine.Process(
                unlockedChat,
                output);

            Expect(
                output.Count == 1 &&
                output[0].Text ==
                    "party",
                "state condition plus case-insensitive text filter must unlock the second rule",
                failures);

            var manual =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "local.validation",
                    4,
                    sequence:
                        4);

            engine.Process(
                manual,
                output);

            Expect(
                output.Count == 0,
                "numeric conditions must fail when their state key is missing instead of treating it as zero",
                failures);

            var transformedTextRule =
                new EventRuntimeRule
                {
                    Id =
                        "text-transform",
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .BroadcastChatMessage
                        },
                    StateMutations =
                        new[]
                        {
                            new EventStateMutation
                            {
                                Kind =
                                    EventStateMutationKind
                                        .SetText,
                                Key =
                                    "chat.normalized",
                                TextSource =
                                    EventTextValueSource
                                        .EventText,
                                TextTransforms =
                                    EventTextTransformFlags
                                        .Trim |
                                    EventTextTransformFlags
                                        .ToLowerInvariant,
                                TextPrefix =
                                    "msg:",
                                TextSuffix =
                                    ":end"
                            }
                        },
                    Actions =
                        new[]
                        {
                            new EventActionTemplate
                            {
                                ActionType =
                                    "test.text_transform",
                                TextSource =
                                    EventTextValueSource
                                        .EventActorName,
                                TextTransforms =
                                    EventTextTransformFlags
                                        .Trim |
                                    EventTextTransformFlags
                                        .ToUpperInvariant,
                                TextPrefix =
                                    "[",
                                TextSuffix =
                                    "]"
                            }
                        }
                };

            engine.SetRules(
                transformedTextRule);

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastChatMessage,
                    "soop.validation",
                    900_000,
                    text:
                        "  Hello World  ",
                    actorName:
                        "  Streamer  ",
                    sequence:
                        9),
                output);

            Expect(
                output.Count == 1 &&
                output[0].Text ==
                    "[STREAMER]" &&
                engine.State.GetText(
                    "chat.normalized") ==
                    "msg:hello world:end",
                "text transforms must support deterministic trim/case/prefix/suffix mapping for state and actions",
                failures);

            var unchangedText =
                "already-normalized";

            Expect(
                ReferenceEquals(
                    unchangedText,
                    EventTextTransform.Apply(
                        unchangedText,
                        EventTextTransformFlags.None,
                        null,
                        null)) &&
                EventTextTransform.Apply(
                    null,
                    EventTextTransformFlags.None,
                    "[",
                    "]") ==
                    "[]",
                "text transform must preserve the original string when no work is configured and combine both affixes without changing null-value semantics",
                failures);

            var cooldownRule =
                new EventRuntimeRule
                {
                    Id =
                        "cooldown",
                    CooldownSeconds =
                        1.0,
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .LocalManual
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "cooldown")
                        }
                };

            engine.SetRules(
                cooldownRule);
            engine.MaxCommandsPerEvent = 32;

            var cooldownFirst =
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    1_000_000,
                    sequence: 10);
            var cooldownSecond =
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    1_500_000,
                    sequence: 11);
            var cooldownThird =
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    2_100_000,
                    sequence: 12);

            engine.Process(
                cooldownFirst,
                output);
            var firstCount =
                output.Count;

            var suppressedBefore =
                engine.CooldownSuppressedRules;
            engine.Process(
                cooldownSecond,
                output);
            var secondCount =
                output.Count;

            engine.Process(
                cooldownThird,
                output);
            var thirdCount =
                output.Count;

            Expect(
                firstCount == 1 &&
                secondCount == 0 &&
                thirdCount == 1 &&
                engine.CooldownSuppressedRules ==
                    suppressedBefore + 1,
                "rule cooldown must suppress burst repeats until the monotonic interval expires",
                failures);

            var rateLimitedRule =
                new EventRuntimeRule
                {
                    Id =
                        "window-rate-limit",
                    RateLimitWindowSeconds =
                        1.0,
                    RateLimitMaxExecutions =
                        2,
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .LocalManual
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "rate-limited")
                        }
                };

            engine.SetRules(
                rateLimitedRule);

            var rateSuppressedBefore =
                engine.RateLimitSuppressedRules;

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    3_000_000,
                    sequence: 20),
                output);
            var rateFirstCount =
                output.Count;

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    3_100_000,
                    sequence: 21),
                output);
            var rateSecondCount =
                output.Count;

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    3_200_000,
                    sequence: 22),
                output);
            var rateThirdCount =
                output.Count;

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes.LocalManual,
                    "local.validation",
                    4_000_000,
                    sequence: 23),
                output);
            var rateNextWindowCount =
                output.Count;

            Expect(
                rateFirstCount == 1 &&
                rateSecondCount == 1 &&
                rateThirdCount == 0 &&
                rateNextWindowCount == 1 &&
                engine.RateLimitSuppressedRules ==
                    rateSuppressedBefore + 1,
                "windowed rule rate limit must allow the configured burst, suppress excess matches, and reset at the next window",
                failures);

            var diagnosticsRule =
                new EventRuntimeRule
                {
                    Id =
                        "rule-diagnostics",
                    RateLimitWindowSeconds =
                        1.0,
                    RateLimitMaxExecutions =
                        1,
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .LocalManual
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "diagnostics")
                        }
                };

            engine.SetRules(
                diagnosticsRule);

            var traceCount = 0;
            var lastTrace =
                default(EventRuntimeTraceEntry);

            engine.TraceEmitted +=
                _ =>
                    throw new InvalidOperationException(
                        "synthetic trace subscriber failure");
            engine.TraceEmitted +=
                entry =>
                {
                    traceCount++;
                    lastTrace = entry;
                };

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastChatMessage,
                    "local.validation",
                    5_000_000,
                    sequence:
                        30),
                output);

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "local.validation",
                    5_100_000,
                    sequence:
                        31),
                output);

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "local.validation",
                    5_200_000,
                    sequence:
                        32),
                output);

            var ruleDiagnostics =
                engine.GetRuleDiagnostics();

            Expect(
                traceCount == 0,
                "rule tracing must remain disabled by default",
                failures);

            Expect(
                ruleDiagnostics.Length == 1 &&
                ruleDiagnostics[0].RuleId ==
                    "rule-diagnostics" &&
                ruleDiagnostics[0]
                    .EvaluatedEvents == 3 &&
                ruleDiagnostics[0]
                    .FilterRejectedEvents == 1 &&
                ruleDiagnostics[0]
                    .MatchedEvents == 1 &&
                ruleDiagnostics[0]
                    .RateLimitSuppressedEvents == 1 &&
                ruleDiagnostics[0]
                    .EmittedCommands == 1,
                "rule diagnostics must report per-rule evaluation, rejection, match, suppression, and command counts",
                failures);

            engine.TraceEnabled = true;

            engine.Process(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "local.validation",
                    6_200_000,
                    sequence:
                        33),
                output);

            Expect(
                traceCount == 1 &&
                lastTrace.RuleId ==
                    "rule-diagnostics" &&
                lastTrace.Outcome ==
                    EventRuntimeTraceOutcome.Matched &&
                lastTrace.EmittedCommands == 1 &&
                engine.TraceSubscriberFailureCount ==
                    1,
                "opt-in tracing must isolate a failing subscriber and still emit a structured rule outcome to later subscribers",
                failures);

            engine.TraceEnabled = false;

            var cappedRule =
                new EventRuntimeRule
                {
                    Id =
                        "bounded-actions",
                    Filter =
                        new EventRuleFilter
                        {
                            Type =
                                NormalizedEventTypes
                                    .LocalManual
                        },
                    Actions =
                        new[]
                        {
                            EnvironmentAction(
                                "environment.main",
                                "one"),
                            EnvironmentAction(
                                "environment.main",
                                "two")
                        }
                };

            engine.SetRules(
                cappedRule);
            engine.MaxCommandsPerEvent = 1;

            var droppedBefore =
                engine.DroppedCommands;

            engine.Process(
                manual,
                output);

            Expect(
                output.Count == 1 &&
                engine.DroppedCommands ==
                    droppedBefore + 1,
                "event action output must be bounded per input event",
                failures);
        }

        private static void ValidateRuleBounds(
            List<string> failures)
        {
            var engine =
                new EventRuntimeEngine();
            var baseline =
                new EventRuntimeRule
                {
                    Id =
                        "bounded-baseline"
                };

            engine.SetRules(
                baseline);

            var excessiveRules =
                new EventRuntimeRule[
                    EventRuntimeRuleSetBounds
                        .MaxRules +
                    1];

            Expect(
                !engine.TrySetRules(
                    excessiveRules,
                    out var ruleCountError) &&
                !string.IsNullOrWhiteSpace(
                    ruleCountError) &&
                engine.GetRuleDiagnostics()
                    .Length == 1 &&
                engine.GetRuleDiagnostics()[0]
                    .RuleId ==
                    "bounded-baseline",
                "event engine must reject excessive rule fanout without replacing the active rule set",
                failures);

            var excessiveActions =
                new EventRuntimeRule
                {
                    Id =
                        "too-many-actions",
                    Actions =
                        new EventActionTemplate[
                            EventRuntimeRuleSetBounds
                                .MaxActionsPerRule +
                            1]
                };

            Expect(
                !engine.TrySetRules(
                    new[]
                    {
                        excessiveActions
                    },
                    out var actionCountError) &&
                !string.IsNullOrWhiteSpace(
                    actionCountError) &&
                engine.GetRuleDiagnostics()
                    .Length == 1,
                "event engine must reject excessive per-rule action fanout transactionally",
                failures);
        }

        private static void ValidateRulePersistence(
            List<string> failures)
        {
            var path =
                Path.Combine(
                    Application.temporaryCachePath,
                    "vcr-p9-rules-" +
                    Guid.NewGuid().ToString("N") +
                    ".json");

            try
            {
                var store =
                    new EventRuntimeConfigurationStore(
                        path);

                var rule =
                    new EventRuntimeRule
                    {
                        Id =
                            "persisted-rule",
                        RateLimitWindowSeconds =
                            2.0,
                        RateLimitMaxExecutions =
                            5,
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .BroadcastChatMessage,
                                TextContains =
                                    "hello"
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .MaterialSetVector,
                                    TargetId =
                                        "slot.0",
                                    Name =
                                        "_Vector",
                                    HasValue =
                                        true,
                                    ConstantNumber =
                                        1.0,
                                    ConstantNumberY =
                                        2.0,
                                    ConstantNumberZ =
                                        3.0,
                                    ConstantNumberW =
                                        4.0,
                                    TextTransforms =
                                        EventTextTransformFlags
                                            .Trim |
                                        EventTextTransformFlags
                                            .ToUpperInvariant
                                }
                            }
                    };

                Expect(
                    store.TrySave(
                        new[]
                        {
                            rule
                        },
                        maxCommandsPerEvent: 17,
                        out var saveError) &&
                    string.IsNullOrEmpty(
                        saveError),
                    "P9 rule configuration must save through a versioned atomic store",
                    failures);

                Expect(
                    store.TryLoad(
                        out var loadedRules,
                        out var loadedMaxCommands,
                        out var loadError) &&
                    string.IsNullOrEmpty(
                        loadError),
                    "P9 rule configuration must reload the current format version",
                    failures);

                Expect(
                    loadedMaxCommands == 17 &&
                    loadedRules.Length == 1 &&
                    loadedRules[0].Id ==
                        "persisted-rule" &&
                    loadedRules[0]
                        .RateLimitMaxExecutions ==
                        5 &&
                    loadedRules[0].Actions.Length ==
                        1 &&
                    Math.Abs(
                        loadedRules[0]
                            .Actions[0]
                            .ConstantNumberW -
                        4.0) <
                        0.000001 &&
                    loadedRules[0]
                        .Actions[0]
                        .TextTransforms ==
                        (EventTextTransformFlags.Trim |
                         EventTextTransformFlags
                             .ToUpperInvariant),
                    "P9 versioned rule persistence must preserve rate-limit, multi-value action, and text-transform fields",
                    failures);

                var excessivePersistedRules =
                    new EventRuntimeRule[
                        EventRuntimeRuleSetBounds
                            .MaxRules +
                        1];

                Expect(
                    !store.TrySave(
                        excessivePersistedRules,
                        maxCommandsPerEvent: 32,
                        out var excessiveRuleSaveError) &&
                    !string.IsNullOrWhiteSpace(
                        excessiveRuleSaveError) &&
                    !File.Exists(
                        path + ".tmp"),
                    "P9 persistence must reject excessive rule fanout before writing a temporary file",
                    failures);

                File.WriteAllText(
                    path,
                    "{\"Version\":999,\"MaxCommandsPerEvent\":32,\"Rules\":[]}");

                Expect(
                    !store.TryLoad(
                        out _,
                        out _,
                        out var futureError) &&
                    !string.IsNullOrWhiteSpace(
                        futureError),
                    "P9 rule configuration must reject a newer unsupported persisted version",
                    failures);

                using (var stream =
                       new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.SetLength(
                        16L * 1024L * 1024L +
                        1L);
                }

                Expect(
                    !store.TryLoad(
                        out var oversizedRules,
                        out var oversizedMaxCommands,
                        out var oversizedError) &&
                    oversizedRules.Length == 0 &&
                    oversizedMaxCommands == 32 &&
                    !string.IsNullOrWhiteSpace(
                        oversizedError),
                    "P9 rule configuration must reject files larger than the bounded persistence limit before JSON allocation",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "rule persistence unexpected exception: " +
                    exception);
            }
            finally
            {
                TryDelete(
                    path);
                TryDelete(
                    path + ".tmp");
                TryDelete(
                    path + ".bak");
            }
        }

        private static void TryDelete(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Validation cleanup must not hide the actual assertion.
            }
        }

        private static void ValidateUnityDispatch(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P9 Event Runtime Validation");

                var hub =
                    root.AddComponent<
                        NormalizedEventHub>();

                var environment =
                    root.AddComponent<
                        P9FakeEnvironmentRuntime>();

                environment.Configure(
                    "environment.main",
                    "default");

                var handler =
                    root.AddComponent<
                        EnvironmentStateEventActionHandler>();

                handler.SetEnvironmentRuntime(
                    environment);

                var cameraObject =
                    new GameObject(
                        "P9 Camera");
                cameraObject.transform
                    .SetParent(
                        root.transform,
                        false);

                var camera =
                    cameraObject
                        .AddComponent<Camera>();
                var cameraController =
                    cameraObject
                        .AddComponent<
                            PrimaryCameraController>();
                var cameraHandler =
                    cameraObject
                        .AddComponent<
                            CameraFieldOfViewEventActionHandler>();

                cameraHandler.SetCameraController(
                    cameraController,
                    "camera.primary");

                var host =
                    root.AddComponent<
                        EventRuntimeHost>();

                host.SetEventHub(hub);
                host.SetActionHandlers(
                    handler,
                    cameraHandler);
                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "manual-environment",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "party")
                            }
                    });

                var indexedRule =
                    host.GetRuleAt(0);
                var directLookup =
                    host.TryGetRule(
                        "manual-environment",
                        out var directRule);
                var indexedSummaryLookup =
                    host.TryGetRuleSummaryAt(
                        0,
                        out var indexedSummary);
                var idSummaryLookup =
                    host.TryGetRuleSummary(
                        "manual-environment",
                        out var idSummary);

                Expect(
                    host.RuleCount == 1 &&
                    indexedRule?.Id ==
                        "manual-environment" &&
                    host.GetRuleAt(-1) == null &&
                    host.GetRuleAt(1) == null &&
                    directLookup &&
                    directRule?.Id ==
                        "manual-environment" &&
                    !ReferenceEquals(
                        directRule,
                        indexedRule) &&
                    indexedSummaryLookup &&
                    indexedSummary.Id ==
                        "manual-environment" &&
                    indexedSummary.Enabled &&
                    idSummaryLookup &&
                    idSummary.Id ==
                        "manual-environment" &&
                    idSummary.Enabled &&
                    !host.TryGetRule(
                        "missing-rule",
                        out _) &&
                    !host.TryGetRuleSummary(
                        "missing-rule",
                        out _),
                    "event runtime host must expose allocation-free readonly summaries while mutable rule lookup returns defensive clones",
                    failures);

                indexedRule.Id =
                    "mutated-indexed-rule";
                directRule.Filter.Type =
                    "mutated-direct-filter";

                Expect(
                    host.TryGetRuleSummaryAt(
                        0,
                        out var postMutationSummary) &&
                    postMutationSummary.Id ==
                        "manual-environment" &&
                    postMutationSummary.Enabled &&
                    host.GetRuleAt(0)?
                        .Filter.Type ==
                        NormalizedEventTypes
                            .LocalManual,
                    "mutating rules returned by GetRuleAt/TryGetRule must not mutate the live host rule graph",
                    failures);

                var validHostRules =
                    host.CaptureRules();
                var previousMaxCommands =
                    host.MaxCommandsPerEvent;
                var invalidHostRule =
                    new EventRuntimeRule
                    {
                        Id =
                            "invalid-host-rule",
                        Enabled =
                            true
                    };
                var invalidHostRules =
                    new EventRuntimeRule[
                        EventRuntimeRuleSetBounds.MaxRules +
                        1];
                invalidHostRules[0] =
                    invalidHostRule;

                typeof(EventRuntimeHost)
                    .GetField(
                        "rules",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    ?.SetValue(
                        host,
                        invalidHostRules);

                var invalidToggleResult =
                    host.TrySetRuleEnabled(
                        "invalid-host-rule",
                        false,
                        out var invalidToggleError);

                Expect(
                    !invalidToggleResult &&
                    !string.IsNullOrWhiteSpace(
                        invalidToggleError) &&
                    host.RuleCount ==
                        invalidHostRules.Length &&
                    invalidHostRule.Enabled &&
                    host.Engine
                        .GetRuleDiagnostics()
                        .Length == 1,
                    "failed host rule apply must preserve the previous engine rules, keep the host rule set intact, and rollback the requested enabled mutation",
                    failures);

                var invalidMaxResult =
                    host.TrySetMaxCommandsPerEvent(
                        64,
                        out var invalidMaxError);

                Expect(
                    !invalidMaxResult &&
                    !string.IsNullOrWhiteSpace(
                        invalidMaxError) &&
                    host.MaxCommandsPerEvent ==
                        previousMaxCommands &&
                    host.RuleCount ==
                        invalidHostRules.Length,
                    "failed max-command apply must rollback the host limit instead of reporting success or clearing rules",
                    failures);

                host.SetRules(
                    validHostRules);

                Expect(
                    host.RuleCount == 1 &&
                    host.GetRuleAt(0)?.Id ==
                        "manual-environment",
                    "event runtime host validation must restore the valid rule set after failure injection",
                    failures);

                var capturedRuleIsolation =
                    host.CaptureRules();
                capturedRuleIsolation[0].Id =
                    "mutated-capture";
                capturedRuleIsolation[0]
                    .Filter.Type =
                        "mutated.filter";
                capturedRuleIsolation[0]
                    .Actions[0]
                    .ActionType =
                        "mutated.action";

                Expect(
                    host.GetRuleAt(0)?.Id ==
                        "manual-environment" &&
                    host.GetRuleAt(0)?
                        .Filter.Type ==
                        NormalizedEventTypes
                            .LocalManual &&
                    host.GetRuleAt(0)?
                        .Actions[0]
                        .ActionType ==
                        EventActionTypes
                            .EnvironmentSetState,
                    "CaptureRules must deep-clone nested rule objects so persistence/UI edits cannot mutate the live host rule graph",
                    failures);

                var externalRuleInput =
                    host.CaptureRules();

                host.SetRules(
                    externalRuleInput);

                externalRuleInput[0].Id =
                    "mutated-input";
                externalRuleInput[0]
                    .Filter.Type =
                        "mutated.input.filter";
                externalRuleInput[0]
                    .Actions[0]
                    .ActionType =
                        "mutated.input.action";

                Expect(
                    host.GetRuleAt(0)?.Id ==
                        "manual-environment" &&
                    host.GetRuleAt(0)?
                        .Filter.Type ==
                        NormalizedEventTypes
                            .LocalManual &&
                    host.GetRuleAt(0)?
                        .Actions[0]
                        .ActionType ==
                        EventActionTypes
                            .EnvironmentSetState,
                    "SetRules must deep-clone nested caller-owned rule objects before installing them into the live host/engine",
                    failures);

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        10));

                InvokeUpdate(hub);

                Expect(
                    environment.Status.StateId ==
                        "party" &&
                    host.ExecutedActions == 1 &&
                    host.FailedActions == 0,
                    "main-thread hub dispatch must execute environment.set_state through the application-level handler",
                    failures);

                var ambiguousBeforeDuplicateReference =
                    host.AmbiguousActions;

                host.SetActionHandlers(
                    handler,
                    handler,
                    cameraHandler);
                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "duplicate-handler-reference",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "duplicate-reference-ok")
                            }
                    });

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        10));

                InvokeUpdate(hub);

                Expect(
                    environment.Status.StateId ==
                        "duplicate-reference-ok" &&
                    host.AmbiguousActions ==
                        ambiguousBeforeDuplicateReference,
                    "registering the same event action handler instance more than once must be deduplicated instead of reported as ambiguous",
                    failures);

                host.SetActionHandlers(
                    handler,
                    cameraHandler);

                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "manual-environment-transition",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .EnvironmentSetState,
                                    TargetId =
                                        "environment.main",
                                    TextSource =
                                        EventTextValueSource
                                            .Constant,
                                    ConstantText =
                                        "night",
                                    Name =
                                        "Fade",
                                    HasValue =
                                        true,
                                    ConstantNumber =
                                        0.75
                                }
                            }
                    });

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        10));

                InvokeUpdate(hub);

                Expect(
                    environment.Status.StateId ==
                        "night" &&
                    environment.LastTransition.Mode ==
                        EnvironmentTransitionMode.Fade,
                    "environment.set_state must map transition mode through the environment runtime contract",
                    failures);

                ExpectClose(
                    environment.LastTransition.DurationSeconds,
                    0.75,
                    "environment.set_state must pass transition duration without exposing concrete environment components",
                    failures);

                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "manual-camera",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .CameraSetFieldOfView,
                                    TargetId =
                                        "camera.primary",
                                    HasValue =
                                        true,
                                    ConstantNumber =
                                        70.0
                                }
                            }
                    });

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        10));

                InvokeUpdate(hub);

                ExpectClose(
                    camera.fieldOfView,
                    70.0,
                    "camera.set_fov must apply a numeric application command through PrimaryCameraController",
                    failures);

                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "unknown-action",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        "test.unknown",
                                    ConstantText =
                                        "ignored"
                                }
                            }
                    });

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        11));

                InvokeUpdate(hub);

                Expect(
                    host.UnhandledActions == 1 &&
                    !string.IsNullOrWhiteSpace(
                        host.LastError),
                    "unknown application action types must be contained and reported instead of invoking scene objects directly",
                    failures);

                var duplicateHandler =
                    root.AddComponent<
                        EnvironmentStateEventActionHandler>();
                duplicateHandler.SetEnvironmentRuntime(
                    environment);

                host.SetActionHandlers(
                    handler,
                    duplicateHandler,
                    cameraHandler);

                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "ambiguous-environment",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "must-not-apply")
                            }
                    });

                var stateBeforeAmbiguous =
                    environment.Status.StateId;

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        12));

                InvokeUpdate(hub);

                Expect(
                    host.AmbiguousActions == 1 &&
                    environment.Status.StateId ==
                        stateBeforeAmbiguous,
                    "multiple matching action handlers must fail closed without mutating the target",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    handler);
                UnityEngine.Object.DestroyImmediate(
                    duplicateHandler);

                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "destroyed-environment-handlers",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "must-stay-unchanged")
                            }
                    });

                var unhandledBeforeDestroyed =
                    host.UnhandledActions;
                var stateBeforeDestroyed =
                    environment.Status.StateId;

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        13));

                InvokeUpdate(hub);

                Expect(
                    host.UnhandledActions ==
                        unhandledBeforeDestroyed + 1 &&
                    environment.Status.StateId ==
                        stateBeforeDestroyed,
                    "destroyed event action handlers cached through interfaces must be ignored and fail closed as unhandled",
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

        private static void ValidateEventHubReplacement(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P9 Event Hub Replacement Validation");

                var oldHub =
                    root.AddComponent<
                        NormalizedEventHub>();
                var environment =
                    root.AddComponent<
                        P9FakeEnvironmentRuntime>();
                environment.Configure(
                    "environment.main",
                    "default");

                var handler =
                    root.AddComponent<
                        EnvironmentStateEventActionHandler>();
                handler.SetEnvironmentRuntime(
                    environment);

                var host =
                    root.AddComponent<
                        EventRuntimeHost>();
                host.SetEventHub(
                    oldHub);
                host.SetActionHandlers(
                    handler);
                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "replacement-hub",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "replacement-ok")
                            }
                    });

                UnityEngine.Object.DestroyImmediate(
                    oldHub);

                var replacementHub =
                    root.AddComponent<
                        NormalizedEventHub>();

                InvokeEventHubRefresh(
                    host);

                replacementHub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        20));

                InvokeUpdate(
                    replacementHub);

                Expect(
                    environment.Status.StateId ==
                        "replacement-ok" &&
                    host.ExecutedActions == 1,
                    "event runtime must rebind to a replacement NormalizedEventHub after the previously subscribed hub is destroyed",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "event hub replacement validation unexpected exception: " +
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

        private static void ValidateDestroyedHandlerDependencies(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P9 Destroyed Handler Dependency Validation");

                var oldEnvironment =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                oldEnvironment.Configure(
                    "environment.main",
                    "default",
                    EnvironmentUpdatePolicy.Static,
                    EnvironmentSpaceMode.World);

                var environmentHandler =
                    root.AddComponent<
                        EnvironmentStateEventActionHandler>();
                environmentHandler.SetEnvironmentRuntime(
                    oldEnvironment);

                var environmentCommand =
                    new EventActionCommand(
                        "dependency-recovery",
                        EventActionTypes
                            .EnvironmentSetState,
                        "environment.main",
                        null,
                        "replacement",
                        0.0,
                        false,
                        30);

                Expect(
                    environmentHandler.CanHandle(
                        environmentCommand),
                    "environment handler recovery validation must start with the configured runtime",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldEnvironment);

                var replacementEnvironment =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                replacementEnvironment.Configure(
                    "environment.main",
                    "default",
                    EnvironmentUpdatePolicy.Static,
                    EnvironmentSpaceMode.World);

                Expect(
                    environmentHandler.CanHandle(
                        environmentCommand) &&
                    environmentHandler.TryExecute(
                        environmentCommand,
                        out var environmentRecoveryError) &&
                    replacementEnvironment.Status.StateId ==
                        "replacement",
                    "environment action handler must discard a destroyed cached runtime and auto-discover its replacement: " +
                    environmentRecoveryError,
                    failures);

                var controller =
                    root.AddComponent<
                        MaterialOverrideController>();
                var oldResolver =
                    root.AddComponent<
                        P9FakeMaterialPresetResolver>();
                var presetHandler =
                    root.AddComponent<
                        MaterialPresetEventActionHandler>();

                presetHandler.SetMaterialController(
                    controller);
                presetHandler.SetPresetResolver(
                    oldResolver);

                var presetCommand =
                    new EventActionCommand(
                        "dependency-recovery",
                        EventActionTypes
                            .MaterialApplyPreset,
                        "slot.0",
                        null,
                        "preset",
                        0.0,
                        false,
                        31);

                Expect(
                    presetHandler.CanHandle(
                        presetCommand),
                    "material preset handler recovery validation must start with live dependencies",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldResolver);

                var replacementResolver =
                    root.AddComponent<
                        P9FakeMaterialPresetResolver>();

                Expect(
                    presetHandler.CanHandle(
                        presetCommand),
                    "material preset handler must discard a destroyed cached resolver and auto-discover a live replacement",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    controller);

                var replacementController =
                    root.AddComponent<
                        MaterialOverrideController>();

                Expect(
                    presetHandler.CanHandle(
                        presetCommand) &&
                    replacementController != null,
                    "material preset handler must re-resolve a replacement controller after the previous Unity component is destroyed",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed event-handler dependency recovery unexpected exception: " +
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

        private static void ValidateThrowingHandlerProbeIsolation(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P9 Handler Probe Isolation Validation");

                var hub =
                    root.AddComponent<
                        NormalizedEventHub>();
                var environment =
                    root.AddComponent<
                        P9FakeEnvironmentRuntime>();
                environment.Configure(
                    "environment.main",
                    "default");

                var throwing =
                    root.AddComponent<
                        P9ThrowingCanHandleActionHandler>();
                var environmentHandler =
                    root.AddComponent<
                        EnvironmentStateEventActionHandler>();
                environmentHandler.SetEnvironmentRuntime(
                    environment);

                var host =
                    root.AddComponent<
                        EventRuntimeHost>();
                host.SetEventHub(
                    hub);
                host.SetActionHandlers(
                    throwing,
                    environmentHandler);
                host.SetRules(
                    new EventRuntimeRule
                    {
                        Id =
                            "probe-isolation",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                EnvironmentAction(
                                    "environment.main",
                                    "probe-ok")
                            }
                    });

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "local.validation",
                        50));

                InvokeUpdate(
                    hub);

                Expect(
                    environment.Status.StateId ==
                        "probe-ok" &&
                    host.ExecutedActions == 1 &&
                    host.HandlerProbeFailureCount == 1 &&
                    host.FailedActions == 0,
                    "a throwing CanHandle implementation must be isolated so another valid handler can still execute the same action",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "handler probe isolation validation unexpected exception: " +
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

        private static void ValidateMaterialAction(
            List<string> failures)
        {
            GameObject root = null;
            Material source = null;
            Texture2D runtimeTexture = null;
            var shaderRegistrySnapshot =
                RuntimeShaderRegistry
                    .CaptureRegistered();
            var textureRegistrySnapshot =
                RuntimeTextureRegistry
                    .CaptureRegistered();

            try
            {
                var shader =
                    Shader.Find(
                        "Universal Render Pipeline/Unlit");

                if (shader == null)
                {
                    failures.Add(
                        "P9 material action validation requires the URP Unlit shader.");
                    return;
                }

                string floatProperty = null;
                string textureProperty = null;

                for (var i = 0;
                     i < shader.GetPropertyCount();
                     i++)
                {
                    var type =
                        shader.GetPropertyType(i);

                    if (floatProperty == null &&
                        (type ==
                            ShaderPropertyType.Float ||
                         type ==
                            ShaderPropertyType.Range))
                    {
                        floatProperty =
                            shader.GetPropertyName(i);
                    }

                    if (textureProperty == null &&
                        type ==
                            ShaderPropertyType.Texture)
                    {
                        textureProperty =
                            shader.GetPropertyName(i);
                    }
                }

                if (string.IsNullOrWhiteSpace(
                        floatProperty))
                {
                    failures.Add(
                        "URP Unlit exposed no float/range property for material action validation.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(
                        textureProperty))
                {
                    failures.Add(
                        "URP Unlit exposed no texture property for material action validation.");
                    return;
                }

                root =
                    new GameObject(
                        "P9 Material Action Validation");

                var child =
                    GameObject.CreatePrimitive(
                        PrimitiveType.Quad);
                child.transform.SetParent(
                    root.transform,
                    false);

                source =
                    new Material(shader)
                    {
                        name =
                            "P9 Source Material"
                    };

                source.SetFloat(
                    floatProperty,
                    0.11f);

                var renderer =
                    child.GetComponent<
                        MeshRenderer>();
                renderer.sharedMaterial =
                    source;

                var controller =
                    root.AddComponent<
                        MaterialOverrideController>();
                controller.RefreshSlots();

                var slots =
                    controller.GetSlots();

                Expect(
                    slots.Length == 1,
                    "P9 material action validation must discover exactly one material slot",
                    failures);

                if (slots.Length != 1)
                {
                    return;
                }

                var preset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p9.event-material"
                    };

                Expect(
                    controller.TryApplyPreset(
                        slots[0].Id,
                        preset,
                        out var report,
                        out var applyError) &&
                    report.Compatible &&
                    string.IsNullOrEmpty(
                        applyError),
                    "P9 material action validation must create an active non-destructive runtime override",
                    failures);

                var handler =
                    root.AddComponent<
                        MaterialFloatEventActionHandler>();
                handler.SetMaterialController(
                    controller);

                var propertyHandler =
                    root.AddComponent<
                        MaterialPropertyEventActionHandler>();
                propertyHandler.SetMaterialController(
                    controller);

                var presetResolver =
                    root.AddComponent<
                        P9FakeMaterialPresetResolver>();
                presetResolver.Preset =
                    new MaterialOverridePreset
                    {
                        PresetId =
                            "p9.validation.preset"
                    };

                var presetHandler =
                    root.AddComponent<
                        MaterialPresetEventActionHandler>();
                presetHandler.SetMaterialController(
                    controller);
                presetHandler.SetPresetResolver(
                    presetResolver);

                var command =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetFloat,
                        slots[0].Id,
                        floatProperty,
                        null,
                        0.37,
                        true,
                        20);

                Expect(
                    handler.CanHandle(
                        command) &&
                    handler.TryExecute(
                        command,
                        out var actionError) &&
                    string.IsNullOrEmpty(
                        actionError),
                    "material.set_float must execute through MaterialOverrideController",
                    failures);

                ExpectClose(
                    source.GetFloat(
                        floatProperty),
                    0.11,
                    "material event action must not mutate the source material",
                    failures);

                ExpectClose(
                    renderer.sharedMaterial
                        .GetFloat(
                            floatProperty),
                    0.37,
                    "material event action must update only the active runtime override",
                    failures);

                var invalidInt =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetInt,
                        slots[0].Id,
                        floatProperty,
                        null,
                        1.5,
                        true,
                        21);

                Expect(
                    propertyHandler.CanHandle(
                        invalidInt) &&
                    !propertyHandler.TryExecute(
                        invalidInt,
                        out var invalidIntError) &&
                    !string.IsNullOrWhiteSpace(
                        invalidIntError),
                    "material.set_int must reject fractional values before touching the material",
                    failures);

                var invalidBool =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetBool,
                        slots[0].Id,
                        floatProperty,
                        null,
                        2.0,
                        true,
                        22);

                Expect(
                    propertyHandler.CanHandle(
                        invalidBool) &&
                    !propertyHandler.TryExecute(
                        invalidBool,
                        out var invalidBoolError) &&
                    !string.IsNullOrWhiteSpace(
                        invalidBoolError),
                    "material.set_bool must accept only explicit 0/1 values",
                    failures);

                var vectorCommand =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetVector,
                        slots[0].Id,
                        floatProperty,
                        null,
                        1.0,
                        2.0,
                        3.0,
                        4.0,
                        true,
                        23);

                Expect(
                    propertyHandler.CanHandle(
                        vectorCommand),
                    "material.set_vector must be claimed by the generalized material property handler",
                    failures);

                const string shaderId =
                    "p9.validation.shader";
                const string textureId =
                    "p9.validation.texture";

                runtimeTexture =
                    new Texture2D(
                        1,
                        1);

                Expect(
                    RuntimeShaderRegistry.Register(
                        shaderId,
                        shader) &&
                    RuntimeTextureRegistry.Register(
                        textureId,
                        runtimeTexture),
                    "material event validation must register deterministic shader/texture resource ids",
                    failures);

                var shaderCommand =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetShader,
                        slots[0].Id,
                        null,
                        shaderId,
                        0.0,
                        false,
                        24);

                Expect(
                    propertyHandler.TryExecute(
                        shaderCommand,
                        out var shaderActionError) &&
                    string.IsNullOrEmpty(
                        shaderActionError) &&
                    renderer.sharedMaterial.shader ==
                        shader,
                    "material.set_shader must resolve a registered shader id through MaterialOverrideController",
                    failures);

                var textureCommand =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialSetTexture,
                        slots[0].Id,
                        textureProperty,
                        textureId,
                        0.0,
                        false,
                        25);

                Expect(
                    propertyHandler.TryExecute(
                        textureCommand,
                        out var textureActionError) &&
                    string.IsNullOrEmpty(
                        textureActionError) &&
                    ReferenceEquals(
                        renderer.sharedMaterial
                            .GetTexture(
                                textureProperty),
                        runtimeTexture),
                    "material.set_texture must resolve a registered texture id without exposing Texture objects to rules",
                    failures);

                var presetCommand =
                    new EventActionCommand(
                        "material-rule",
                        EventActionTypes
                            .MaterialApplyPreset,
                        slots[0].Id,
                        null,
                        "p9.validation.preset",
                        0.0,
                        false,
                        26);

                Expect(
                    presetHandler.CanHandle(
                        presetCommand) &&
                    presetHandler.TryExecute(
                        presetCommand,
                        out var presetActionError) &&
                    string.IsNullOrEmpty(
                        presetActionError) &&
                    controller.TryGetStatus(
                        slots[0].Id,
                        out var presetStatus) &&
                    presetStatus.PresetId ==
                        "p9.validation.preset",
                    "material.apply_preset must resolve a logical preset id without putting preset file paths or objects in rules",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "material action unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(root);
                }

                RuntimeShaderRegistry
                    .RestoreRegistered(
                        shaderRegistrySnapshot);
                RuntimeTextureRegistry
                    .RestoreRegistered(
                        textureRegistrySnapshot);

                if (runtimeTexture != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            runtimeTexture);
                }

                if (source != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(source);
                }
            }
        }

        private static EventActionTemplate
            EnvironmentAction(
                string targetId,
                string stateId)
        {
            return new EventActionTemplate
            {
                ActionType =
                    EventActionTypes
                        .EnvironmentSetState,
                TargetId =
                    targetId,
                TextSource =
                    EventTextValueSource
                        .Constant,
                ConstantText =
                    stateId
            };
        }

        private static void InvokeEventHubRefresh(
            EventRuntimeHost host)
        {
            var method =
                typeof(EventRuntimeHost)
                    .GetMethod(
                        "RefreshEventHubSubscription",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(EventRuntimeHost)
                        .FullName,
                    "RefreshEventHubSubscription");
            }

            method.Invoke(
                host,
                null);
        }

        private static void InvokeUpdate(
            NormalizedEventHub hub)
        {
            var method =
                typeof(NormalizedEventHub)
                    .GetMethod(
                        "Update",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(NormalizedEventHub)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                hub,
                null);
        }

        private static void ExpectClose(
            double actual,
            double expected,
            string message,
            List<string> failures)
        {
            if (Math.Abs(
                    actual - expected) >
                0.000001)
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

    internal sealed class P9ThrowingCanHandleActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        public bool CanHandle(
            EventActionCommand command)
        {
            throw new InvalidOperationException(
                "synthetic CanHandle failure");
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error =
                "should not execute";
            return false;
        }
    }

    internal sealed class P9FakeMaterialPresetResolver :
        MonoBehaviour,
        IMaterialPresetResolver
    {
        public MaterialOverridePreset Preset
        {
            get;
            set;
        }

        public bool TryResolvePreset(
            string presetId,
            out MaterialOverridePreset preset)
        {
            preset =
                Preset != null &&
                string.Equals(
                    Preset.PresetId,
                    presetId,
                    StringComparison.Ordinal)
                    ? Preset
                    : null;

            return preset != null;
        }
    }

    internal sealed class P9FakeEnvironmentRuntime :
        MonoBehaviour,
        IEnvironmentRuntime
    {
        private string _environmentId =
            "environment.fake";

        private string _stateId =
            "default";

        public EnvironmentTransitionSpec LastTransition
        {
            get;
            private set;
        } =
            EnvironmentTransitionSpec.Cut;

        public EnvironmentRuntimeStatus Status =>
            new(
                _environmentId,
                _stateId,
                EnvironmentUpdatePolicy.Static,
                true,
                null);

        public EnvironmentSpaceMode SpaceMode =>
            EnvironmentSpaceMode.World;

        public EnvironmentTransitionStatus TransitionStatus =>
            default;

        public event Action<EnvironmentStateChange>
            StateChanged;

        public void Configure(
            string environmentId,
            string stateId)
        {
            _environmentId =
                environmentId;
            _stateId =
                stateId;
        }

        public bool SetState(
            string stateId,
            out string error)
        {
            return SetState(
                stateId,
                default,
                out error);
        }

        public bool SetState(
            string stateId,
            EnvironmentTransitionSpec transition,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    stateId))
            {
                error =
                    "state id is required";
                return false;
            }

            var previous =
                _stateId;
            _stateId =
                stateId;
            LastTransition =
                transition;

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    _stateId));

            return true;
        }
    }
}
