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
                var markers =
                    transition.FindPropertyRelative(
                        "Markers");
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
                    markers != null &&
                    steps != null &&
                    cleanup != null,
                    "transition timeline editor serialized property paths must remain stable",
                    failures);

                if (id == null ||
                    duration == null ||
                    queue == null ||
                    fallback == null ||
                    markers == null ||
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

                markers.arraySize = 2;
                ConfigureMarker(
                    markers.GetArrayElementAtIndex(
                        0),
                    "swap",
                    0.5f);
                ConfigureMarker(
                    markers.GetArrayElementAtIndex(
                        1),
                    "spin-end",
                    0.9f);

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

                ConfigureMarkerCommit(
                    steps.GetArrayElementAtIndex(
                        1),
                    "swap",
                    0f);

                ConfigureMarkerAction(
                    steps.GetArrayElementAtIndex(
                        2),
                    "spin-end",
                    0f,
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
                    "timeline-authored transition with named marker timing must rebuild through the same runtime contract: " +
                    timelineError,
                    failures);

                serialized.Update();
                transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);
                steps =
                    transition.FindPropertyRelative(
                        "Steps");

                steps.GetArrayElementAtIndex(
                        1)
                    .FindPropertyRelative(
                        "MarkerName")
                    .stringValue =
                        "missing-marker";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    !runtime.RebuildConfiguration(
                        out var markerError) &&
                    markerError != null &&
                    markerError.Contains(
                        "marker",
                        StringComparison.OrdinalIgnoreCase),
                    "timeline-authored step must reject an unknown named marker",
                    failures);

                serialized.Update();
                transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);
                steps =
                    transition.FindPropertyRelative(
                        "Steps");
                steps.GetArrayElementAtIndex(
                        1)
                    .FindPropertyRelative(
                        "MarkerName")
                    .stringValue =
                        "swap";
                steps.GetArrayElementAtIndex(
                        0)
                    .FindPropertyRelative(
                        "Blocking")
                    .boolValue =
                        true;
                steps.GetArrayElementAtIndex(
                        0)
                    .FindPropertyRelative(
                        "CompletionTimeoutSeconds")
                    .floatValue =
                        0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    !runtime.RebuildConfiguration(
                        out var timeoutError) &&
                    timeoutError != null &&
                    timeoutError.Contains(
                        "timeout",
                        StringComparison.OrdinalIgnoreCase),
                    "blocking timeline action must require a finite positive completion timeout",
                    failures);

                serialized.Update();
                transition =
                    transitions
                        .GetArrayElementAtIndex(
                            0);
                steps =
                    transition.FindPropertyRelative(
                        "Steps");
                steps.GetArrayElementAtIndex(
                        0)
                    .FindPropertyRelative(
                        "Blocking")
                    .boolValue =
                        false;
                steps.GetArrayElementAtIndex(
                        0)
                    .FindPropertyRelative(
                        "CompletionTimeoutSeconds")
                    .floatValue =
                        5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Expect(
                    runtime.RebuildConfiguration(
                        out var restoredTimelineError),
                    "timeline must rebuild after marker/timeout validation repair: " +
                    restoredTimelineError,
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

                var package =
                    P11AppearanceTransitionPackageUtility
                        .CreatePackage(
                            "timeline-validation",
                            new[]
                            {
                                new AppearanceTransitionPreset
                                {
                                    Id =
                                        "portable-transition",
                                    DurationSeconds =
                                        1.2,
                                    QueuePolicy =
                                        AppearanceTransitionQueuePolicy
                                            .Interrupt,
                                    FallbackPolicy =
                                        AppearanceTransitionFallbackPolicy
                                            .Immediate,
                                    Markers =
                                        new[]
                                        {
                                            new AppearanceTransitionMarker
                                            {
                                                Name =
                                                    "swap",
                                                TimeSeconds =
                                                    0.55
                                            }
                                        },
                                    Steps =
                                        new[]
                                        {
                                            new AppearanceTransitionStep
                                            {
                                                TimeSeconds =
                                                    0.0,
                                                Kind =
                                                    AppearanceTransitionStepKind
                                                        .Action,
                                                ActionType =
                                                    EventActionTypes
                                                        .MotionPlay,
                                                TargetId =
                                                    "motion.quickchange",
                                                StepId =
                                                    "spin-start",
                                                Text =
                                                    "spin",
                                                Required =
                                                    false,
                                                Blocking =
                                                    true,
                                                CompletionTimeoutSeconds =
                                                    2.5
                                            },
                                            new AppearanceTransitionStep
                                            {
                                                TimingMode =
                                                    AppearanceTransitionTimingMode
                                                        .Marker,
                                                MarkerName =
                                                    "swap",
                                                DependencyMode =
                                                    AppearanceTransitionDependencyMode
                                                        .All,
                                                DependsOnStepIds =
                                                    new[]
                                                    {
                                                        "spin-start"
                                                    },
                                                DependencyTimeoutSeconds =
                                                    3.0,
                                                Kind =
                                                    AppearanceTransitionStepKind
                                                        .Commit
                                            }
                                        },
                                    CancellationSteps =
                                        new[]
                                        {
                                            new AppearanceTransitionStep
                                            {
                                                TimeSeconds =
                                                    0.0,
                                                Kind =
                                                    AppearanceTransitionStepKind
                                                        .Action,
                                                ActionType =
                                                    EventActionTypes
                                                        .MotionRelease,
                                                TargetId =
                                                    "motion.quickchange",
                                                Text =
                                                    "spin",
                                                Required =
                                                    false
                                            }
                                        }
                                }
                            });

                var serializedPackage =
                    P11AppearanceTransitionPackageUtility
                        .TrySerialize(
                            package,
                            out var packageJson,
                            out var packageSaveError);
                AppearanceTransitionPackage
                    packageRoundTrip = null;
                string packageLoadError = null;
                var loadedPackage =
                    serializedPackage &&
                    P11AppearanceTransitionPackageUtility
                        .TryDeserialize(
                            packageJson,
                            out packageRoundTrip,
                            out packageLoadError);

                Expect(
                    serializedPackage &&
                    loadedPackage &&
                    packageRoundTrip != null &&
                    packageRoundTrip.Version ==
                        AppearanceTransitionPackage
                            .CurrentVersion &&
                    packageRoundTrip.Transitions.Length ==
                        1 &&
                    packageRoundTrip.Transitions[0]
                        .Id ==
                        "portable-transition" &&
                    packageRoundTrip.Transitions[0]
                        .Markers.Length ==
                        1 &&
                    packageRoundTrip.Transitions[0]
                        .Markers[0]
                        .Name ==
                        "swap" &&
                    packageRoundTrip.Transitions[0]
                        .Steps[0]
                        .Blocking &&
                    Math.Abs(
                        packageRoundTrip.Transitions[0]
                            .Steps[0]
                            .CompletionTimeoutSeconds -
                        2.5) <
                        0.001 &&
                    packageRoundTrip.Transitions[0]
                        .Steps[1]
                        .TimingMode ==
                        AppearanceTransitionTimingMode
                            .Marker &&
                    packageRoundTrip.Transitions[0]
                        .Steps[1]
                        .MarkerName ==
                        "swap" &&
                    packageRoundTrip.Transitions[0]
                        .Steps[0]
                        .StepId ==
                        "spin-start" &&
                    packageRoundTrip.Transitions[0]
                        .Steps[1]
                        .DependencyMode ==
                        AppearanceTransitionDependencyMode
                            .All &&
                    packageRoundTrip.Transitions[0]
                        .Steps[1]
                        .DependsOnStepIds.Length ==
                        1 &&
                    packageRoundTrip.Transitions[0]
                        .Steps[1]
                        .DependsOnStepIds[0] ==
                        "spin-start" &&
                    Math.Abs(
                        packageRoundTrip.Transitions[0]
                            .Steps[1]
                            .DependencyTimeoutSeconds -
                        3.0) <
                        0.001 &&
                    packageRoundTrip.Transitions[0]
                        .CancellationSteps.Length ==
                        1 &&
                    packageRoundTrip.Transitions[0]
                        .CancellationSteps[0]
                        .ActionType ==
                        EventActionTypes.MotionRelease,
                    "transition package JSON must preserve action, commit, cleanup, queue, and version data: " +
                    packageSaveError +
                    " / " +
                    packageLoadError,
                    failures);

                package.Version =
                    AppearanceTransitionPackage
                        .CurrentVersion +
                    1;

                Expect(
                    !P11AppearanceTransitionPackageUtility
                        .Validate(
                            package,
                            out var versionError) &&
                    versionError != null &&
                    versionError.Contains(
                        "newer",
                        StringComparison.OrdinalIgnoreCase),
                    "transition package validator must reject unsupported newer versions",
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

        private static void ConfigureMarker(
            SerializedProperty marker,
            string name,
            float timeSeconds)
        {
            marker.FindPropertyRelative(
                    "Name")
                .stringValue =
                    name;
            marker.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
        }

        private static void ConfigureMarkerCommit(
            SerializedProperty step,
            string markerName,
            float offsetSeconds)
        {
            ConfigureCommit(
                step,
                0f);
            ConfigureMarkerTiming(
                step,
                markerName,
                offsetSeconds);
        }

        private static void ConfigureMarkerAction(
            SerializedProperty step,
            string markerName,
            float offsetSeconds,
            string actionType,
            string targetId,
            string text,
            bool required)
        {
            ConfigureAction(
                step,
                0f,
                actionType,
                targetId,
                text,
                required);
            ConfigureMarkerTiming(
                step,
                markerName,
                offsetSeconds);
        }

        private static void ConfigureMarkerTiming(
            SerializedProperty step,
            string markerName,
            float offsetSeconds)
        {
            step.FindPropertyRelative(
                    "TimingMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionTimingMode
                        .Marker;
            step.FindPropertyRelative(
                    "MarkerName")
                .stringValue =
                    markerName;
            step.FindPropertyRelative(
                    "MarkerOffsetSeconds")
                .floatValue =
                    offsetSeconds;
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
                    "TimingMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionTimingMode
                        .AbsoluteTime;
            step.FindPropertyRelative(
                    "MarkerName")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "MarkerOffsetSeconds")
                .floatValue = 0f;
            step.FindPropertyRelative(
                    "StepId")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "DependencyMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionDependencyMode
                        .None;
            step.FindPropertyRelative(
                    "DependsOnStepIds")
                .arraySize = 0;
            step.FindPropertyRelative(
                    "DependencyTimeoutSeconds")
                .floatValue = 5f;
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
            step.FindPropertyRelative(
                    "Blocking")
                .boolValue = false;
            step.FindPropertyRelative(
                    "CompletionTimeoutSeconds")
                .floatValue = 5f;
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
                    "TimingMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionTimingMode
                        .AbsoluteTime;
            step.FindPropertyRelative(
                    "MarkerName")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "MarkerOffsetSeconds")
                .floatValue = 0f;
            step.FindPropertyRelative(
                    "StepId")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "DependencyMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionDependencyMode
                        .None;
            step.FindPropertyRelative(
                    "DependsOnStepIds")
                .arraySize = 0;
            step.FindPropertyRelative(
                    "DependencyTimeoutSeconds")
                .floatValue = 5f;
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
            step.FindPropertyRelative(
                    "Blocking")
                .boolValue = false;
            step.FindPropertyRelative(
                    "CompletionTimeoutSeconds")
                .floatValue = 5f;
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
