using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Environment;
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
            ValidateUnityDispatch(failures);
            ValidateMaterialAction(failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P9 event runtime validation: PASS " +
                    "(filter, condition, state mutation, numeric transform, cooldown, action cap, environment/camera/material action dispatch, unhandled diagnostics)");
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
                "donation below filter threshold must not mutate state or emit actions",
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

        private static void ValidateMaterialAction(
            List<string> failures)
        {
            GameObject root = null;
            Material source = null;

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

                for (var i = 0;
                     i < shader.GetPropertyCount();
                     i++)
                {
                    var type =
                        shader.GetPropertyType(i);

                    if (type ==
                            ShaderPropertyType.Float ||
                        type ==
                            ShaderPropertyType.Range)
                    {
                        floatProperty =
                            shader.GetPropertyName(i);
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(
                        floatProperty))
                {
                    failures.Add(
                        "URP Unlit exposed no float/range property for material action validation.");
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

    internal sealed class P9FakeEnvironmentRuntime :
        MonoBehaviour,
        IEnvironmentRuntime
    {
        private string _environmentId =
            "environment.fake";

        private string _stateId =
            "default";

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

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    _stateId));

            return true;
        }
    }
}
