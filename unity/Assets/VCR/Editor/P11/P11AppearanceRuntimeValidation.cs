using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Editor.P11
{
    internal static class P11AppearanceRuntimeValidation
    {
        public static void RunChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Appearance Validation");

                var casual =
                    new GameObject(
                        "Outfit Casual");
                casual.transform.SetParent(
                    root.transform,
                    false);

                var formal =
                    new GameObject(
                        "Outfit Formal");
                formal.transform.SetParent(
                    root.transform,
                    false);

                var hat =
                    new GameObject(
                        "Accessory Hat");
                hat.transform.SetParent(
                    root.transform,
                    false);

                var crown =
                    new GameObject(
                        "Accessory Crown");
                crown.transform.SetParent(
                    root.transform,
                    false);

                casual.SetActive(true);
                formal.SetActive(false);
                hat.SetActive(false);
                crown.SetActive(false);

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                runtime.ConfigureBindings(
                    new[]
                    {
                        new AppearanceOutfitBinding
                        {
                            OutfitId = "casual",
                            Roots = new[]
                            {
                                casual
                            }
                        },
                        new AppearanceOutfitBinding
                        {
                            OutfitId = "formal",
                            Roots = new[]
                            {
                                formal
                            }
                        }
                    },
                    new[]
                    {
                        new AppearanceAccessoryBinding
                        {
                            SlotId = "head",
                            AccessoryId = "hat",
                            Root = hat
                        },
                        new AppearanceAccessoryBinding
                        {
                            SlotId = "head",
                            AccessoryId = "crown",
                            Root = crown
                        }
                    },
                    new[]
                    {
                        new AppearancePresetBinding
                        {
                            PresetId = "casual-hat",
                            OutfitId = "casual",
                            Accessories =
                                new[]
                                {
                                    new AppearanceAccessorySelectionBinding
                                    {
                                        SlotId = "head",
                                        AccessoryId = "hat"
                                    }
                                }
                        },
                        new AppearancePresetBinding
                        {
                            PresetId = "formal-crown",
                            OutfitId = "formal",
                            PreferredTransitionId =
                                "spin-confetti",
                            Accessories =
                                new[]
                                {
                                    new AppearanceAccessorySelectionBinding
                                    {
                                        SlotId = "head",
                                        AccessoryId = "crown"
                                    }
                                }
                        }
                    },
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "spin-confetti",
                            DurationSeconds =
                                1.2f,
                            FallbackPolicy =
                                AppearanceTransitionFallbackPolicy
                                    .Immediate,
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds = 0f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play",
                                        Text =
                                            "confetti",
                                        Required = true
                                    },
                                    new AppearanceTransitionStepBinding
                                    {
                                        TimeSeconds = 0.55f,
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Commit
                                    }
                                }
                        }
                    },
                    executors:
                        Array.Empty<MonoBehaviour>(),
                    nextDefaultPresetId:
                        "casual-hat");

                Expect(
                    runtime.RebuildConfiguration(
                        out var configError),
                    "appearance configuration must validate: " +
                    configError,
                    failures);

                Expect(
                    runtime.PresetIds.Count == 2 &&
                    runtime.PresetIds[0] ==
                        "casual-hat" &&
                    runtime.PresetIds[1] ==
                        "formal-crown",
                    "appearance quick-change order must preserve authoring order",
                    failures);

                Expect(
                    runtime.SetPreset(
                        "casual-hat",
                        "Immediate",
                        out var applyError),
                    "immediate default appearance must apply: " +
                    applyError,
                    failures);

                Expect(
                    casual.activeSelf &&
                    !formal.activeSelf &&
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "immediate preset must atomically select the configured outfit and accessory",
                    failures);

                Expect(
                    runtime.SetPreset(
                        "formal-crown",
                        "spin-confetti",
                        out var transitionError),
                    "transition with Immediate fallback must be accepted when its required effect executor is unavailable: " +
                    transitionError,
                    failures);

                Expect(
                    !casual.activeSelf &&
                    formal.activeSelf &&
                    !hat.activeSelf &&
                    crown.activeSelf &&
                    runtime.Status.CurrentPresetId ==
                        "formal-crown" &&
                    !runtime.Status.Busy,
                    "missing required transition executor with Immediate fallback must commit the target appearance synchronously",
                    failures);

                Expect(
                    runtime.SetAccessory(
                        "head",
                        "hat",
                        "Immediate",
                        out var accessoryError),
                    "direct accessory quick change must apply: " +
                    accessoryError,
                    failures);

                Expect(
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "single-selection accessory slot must disable the previous registered accessory",
                    failures);

                Expect(
                    runtime.ClearAccessory(
                        "head",
                        "Immediate",
                        out var clearError),
                    "accessory clear must apply: " +
                    clearError,
                    failures);

                Expect(
                    !hat.activeSelf &&
                    !crown.activeSelf,
                    "clearing a slot must disable every registered accessory in that slot",
                    failures);

                Expect(
                    runtime.RestoreDefault(
                        "Immediate",
                        out var restoreError),
                    "default appearance restore must apply: " +
                    restoreError,
                    failures);

                Expect(
                    casual.activeSelf &&
                    !formal.activeSelf &&
                    hat.activeSelf &&
                    !crown.activeSelf,
                    "default restore must return the complete outfit/accessory state",
                    failures);

                var previous =
                    runtime.Current;

                Expect(
                    !runtime.SetOutfit(
                        "missing-outfit",
                        "Immediate",
                        out _),
                    "unknown outfit must be rejected",
                    failures);

                Expect(
                    runtime.Current.OutfitId ==
                        previous.OutfitId &&
                    casual.activeSelf &&
                    !formal.activeSelf,
                    "rejected appearance request must leave the previous appearance intact",
                    failures);

                var appearanceHandler =
                    root.AddComponent<
                        AppearanceEventActionHandler>();
                appearanceHandler.SetAppearanceRuntime(
                    runtime);

                var eventCommand =
                    new EventActionCommand(
                        ruleId:
                            "appearance-validation",
                        actionType:
                            EventActionTypes
                                .AppearanceSetPreset,
                        targetId:
                            runtime.Status.RuntimeId,
                        name:
                            "Immediate",
                        text:
                            "formal-crown",
                        value:
                            0.0,
                        hasValue:
                            false,
                        eventSequence:
                            1);

                Expect(
                    appearanceHandler.CanHandle(
                        eventCommand) &&
                    appearanceHandler.TryExecute(
                        eventCommand,
                        out var eventError),
                    "event runtime appearance.set_preset must reach the same appearance runtime contract: " +
                    eventError,
                    failures);

                Expect(
                    runtime.Status.CurrentPresetId ==
                        "formal-crown" &&
                    formal.activeSelf &&
                    crown.activeSelf,
                    "event-driven appearance preset change must produce the same atomic outfit/accessory state",
                    failures);

                var fakeAction =
                    root.AddComponent<
                        P11FakeTransitionActionHandler>();
                var transitionExecutor =
                    root.AddComponent<
                        AppearanceTransitionActionExecutor>();
                transitionExecutor.SetActionHandlers(
                    fakeAction);

                var effectStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            "effect.play",
                        Text =
                            "confetti"
                    };

                Expect(
                    transitionExecutor.CanExecute(
                        effectStep) &&
                    transitionExecutor.TryExecute(
                        effectStep,
                        out var effectError) &&
                    fakeAction.ExecutionCount == 1 &&
                    fakeAction.LastText ==
                        "confetti",
                    "appearance transition action executor must reuse exactly one registered application action handler: " +
                    effectError,
                    failures);

                var recursiveStep =
                    new AppearanceTransitionStep
                    {
                        Kind =
                            AppearanceTransitionStepKind
                                .Action,
                        ActionType =
                            EventActionTypes
                                .AppearanceSetPreset,
                        Text =
                            "casual-hat"
                    };

                Expect(
                    !transitionExecutor.CanExecute(
                        recursiveStep) &&
                    !transitionExecutor.TryExecute(
                        recursiveStep,
                        out _),
                    "appearance transition executor must reject recursive appearance.* actions",
                    failures);

                var invalidRoot =
                    new GameObject(
                        "Invalid Appearance Runtime");
                invalidRoot.transform.SetParent(
                    root.transform,
                    false);

                var invalid =
                    invalidRoot.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                invalid.ConfigureBindings(
                    Array.Empty<
                        AppearanceOutfitBinding>(),
                    Array.Empty<
                        AppearanceAccessoryBinding>(),
                    Array.Empty<
                        AppearancePresetBinding>(),
                    new[]
                    {
                        new AppearanceTransitionBinding
                        {
                            TransitionId =
                                "missing-commit",
                            Steps =
                                new[]
                                {
                                    new AppearanceTransitionStepBinding
                                    {
                                        Kind =
                                            AppearanceTransitionStepKind
                                                .Action,
                                        ActionType =
                                            "effect.play",
                                        Required = false
                                    }
                                }
                        }
                    },
                    Array.Empty<MonoBehaviour>());

                Expect(
                    !invalid.RebuildConfiguration(
                        out var invalidError) &&
                    invalidError != null &&
                    invalidError.Contains(
                        "exactly one",
                        StringComparison.OrdinalIgnoreCase),
                    "transition definition without exactly one appearance commit must fail closed",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "appearance validation unexpected exception: " +
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

    internal sealed class P11FakeTransitionActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        public int ExecutionCount { get; private set; }
        public string LastText { get; private set; }

        public bool CanHandle(
            EventActionCommand command)
        {
            return command.ActionType ==
                "effect.play";
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Unsupported fake transition action.";
                return false;
            }

            ExecutionCount++;
            LastText =
                command.Text;
            return true;
        }
    }
}
