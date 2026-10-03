using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;

namespace VCR.Editor.P11
{
    internal static class
        P11AppearanceTransitionTimelineValidation
    {
        public static void RunChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P11 Transition Timeline Validation");

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();
                var serialized =
                    new SerializedObject(
                        runtime);
                var transitions =
                    serialized.FindProperty(
                        "transitions");

                Expect(
                    transitions != null,
                    "transition timeline editor requires serialized BasicCharacterAppearanceRuntime.transitions",
                    failures);

                if (transitions == null)
                {
                    return;
                }

                transitions.arraySize = 1;
                var transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);

                var id =
                    transition.FindPropertyRelative(
                        "TransitionId");
                var duration =
                    transition.FindPropertyRelative(
                        "DurationSeconds");
                var queue =
                    transition.FindPropertyRelative(
                        "QueuePolicy");
                var fallback =
                    transition.FindPropertyRelative(
                        "FallbackPolicy");
                var steps =
                    transition.FindPropertyRelative(
                        "Steps");
                var cleanup =
                    transition.FindPropertyRelative(
                        "CancellationSteps");

                Expect(
                    id != null &&
                    duration != null &&
                    queue != null &&
                    fallback != null &&
                    steps != null &&
                    cleanup != null,
                    "transition timeline editor serialized property paths must remain stable",
                    failures);

                if (id == null ||
                    duration == null ||
                    queue == null ||
                    fallback == null ||
                    steps == null ||
                    cleanup == null)
                {
                    return;
                }

                id.stringValue =
                    "timeline-validation";
                duration.floatValue =
                    1f;
                queue.enumValueIndex =
                    (int)
                    AppearanceTransitionQueuePolicy
                        .QueueLatest;
                fallback.enumValueIndex =
                    (int)
                    AppearanceTransitionFallbackPolicy
                        .Immediate;

                steps.arraySize = 3;

                ConfigureAction(
                    steps.GetArrayElementAtIndex(
                        0),
                    0f,
                    EventActionTypes.MotionPlay,
                    "motion.quickchange",
                    "spin",
                    required:
                        false);

                ConfigureCommit(
                    steps.GetArrayElementAtIndex(
                        1),
                    0.5f);

                ConfigureAction(
                    steps.GetArrayElementAtIndex(
                        2),
                    0.9f,
                    EventActionTypes.MotionRelease,
                    "motion.quickchange",
                    "spin",
                    required:
                        false);

                cleanup.arraySize = 0;

                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    runtime.RebuildConfiguration(
                        out var timelineError),
                    "timeline-authored transition must rebuild through the same runtime contract: " +
                    timelineError,
                    failures);

                serialized.Update();
                transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);
                queue =
                    transition.FindPropertyRelative(
                        "QueuePolicy");
                cleanup =
                    transition.FindPropertyRelative(
                        "CancellationSteps");

                queue.enumValueIndex =
                    (int)
                    AppearanceTransitionQueuePolicy
                        .Interrupt;
                cleanup.arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    !runtime.RebuildConfiguration(
                        out var missingCleanupError) &&
                    missingCleanupError != null &&
                    missingCleanupError.Contains(
                        "cleanup",
                        StringComparison.OrdinalIgnoreCase),
                    "timeline-authored Interrupt must fail closed until cleanup actions exist",
                    failures);

                serialized.Update();
                transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);
                cleanup =
                    transition.FindPropertyRelative(
                        "CancellationSteps");
                cleanup.arraySize = 1;

                ConfigureAction(
                    cleanup.GetArrayElementAtIndex(
                        0),
                    0f,
                    EventActionTypes.MotionRelease,
                    "motion.quickchange",
                    "spin",
                    required:
                        false);

                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    runtime.RebuildConfiguration(
                        out var interruptError),
                    "timeline-authored Interrupt with cleanup must rebuild: " +
                    interruptError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "transition timeline validation unexpected exception: " +
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

        private static void ConfigureCommit(
            SerializedProperty step,
            float timeSeconds)
        {
            step.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
            step.FindPropertyRelative(
                    "Kind")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionStepKind
                        .Commit;
            step.FindPropertyRelative(
                    "ActionType")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "TargetId")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Name")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Text")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Value")
                .doubleValue = 0.0;
            step.FindPropertyRelative(
                    "HasValue")
                .boolValue = false;
            step.FindPropertyRelative(
                    "Required")
                .boolValue = true;
        }

        private static void ConfigureAction(
            SerializedProperty step,
            float timeSeconds,
            string actionType,
            string targetId,
            string text,
            bool required)
        {
            step.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
            step.FindPropertyRelative(
                    "Kind")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionStepKind
                        .Action;
            step.FindPropertyRelative(
                    "ActionType")
                .stringValue =
                    actionType;
            step.FindPropertyRelative(
                    "TargetId")
                .stringValue =
                    targetId;
            step.FindPropertyRelative(
                    "Name")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Text")
                .stringValue =
                    text;
            step.FindPropertyRelative(
                    "Value")
                .doubleValue = 0.0;
            step.FindPropertyRelative(
                    "HasValue")
                .boolValue = false;
            step.FindPropertyRelative(
                    "Required")
                .boolValue =
                    required;
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
}
