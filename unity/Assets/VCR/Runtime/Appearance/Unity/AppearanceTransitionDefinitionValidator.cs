using System;
using System.Collections.Generic;

namespace VCR.Runtime.Appearance.Unity
{
    internal static class AppearanceTransitionDefinitionValidator
    {
        public static bool Validate(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            if (transition == null ||
                string.IsNullOrWhiteSpace(
                    transition.Id))
            {
                error =
                    "Every appearance transition requires a non-empty id.";
                return false;
            }

            if (double.IsNaN(
                    transition.DurationSeconds) ||
                double.IsInfinity(
                    transition.DurationSeconds) ||
                transition.DurationSeconds < 0.0)
            {
                error =
                    $"Transition '{transition.Id}' has an invalid duration.";
                return false;
            }

            if (!TryBuildMarkerTimes(
                    transition,
                    out var markerTimes,
                    out error))
            {
                return false;
            }

            var commitCount = 0;
            var hasBlockingStep = false;
            var hasDependencyStep = false;
            var authoredActionSteps =
                new Dictionary<string, AppearanceTransitionStep>(
                    StringComparer.Ordinal);
            var previousTime = 0.0;
            var hasPreviousStep = false;
            var lastStepTime = 0.0;

            foreach (var step in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    !TryResolveStepTime(
                        step,
                        markerTimes,
                        out var resolvedTime,
                        out error))
                {
                    error =
                        $"Transition '{transition.Id}' contains invalid step timing: {error}";
                    return false;
                }

                if (hasPreviousStep &&
                    resolvedTime <
                        previousTime)
                {
                    error =
                        $"Transition '{transition.Id}' steps must be ordered by non-decreasing resolved time.";
                    return false;
                }

                previousTime =
                    resolvedTime;
                lastStepTime =
                    resolvedTime;
                hasPreviousStep =
                    true;

                if (!ValidateStepDependencies(
                        transition,
                        step,
                        authoredActionSteps,
                        out error))
                {
                    return false;
                }

                if (step.DependencyMode !=
                    AppearanceTransitionDependencyMode.None)
                {
                    hasDependencyStep =
                        true;
                }

                if (step.Blocking)
                {
                    hasBlockingStep = true;

                    if (step.Kind !=
                        AppearanceTransitionStepKind.Action)
                    {
                        error =
                            $"Transition '{transition.Id}' commit steps cannot be blocking.";
                        return false;
                    }

                    if (double.IsNaN(
                            step.CompletionTimeoutSeconds) ||
                        double.IsInfinity(
                            step.CompletionTimeoutSeconds) ||
                        step.CompletionTimeoutSeconds <= 0.0)
                    {
                        error =
                            $"Transition '{transition.Id}' blocking action '{step.ActionType ?? "<none>"}' requires a finite positive completion timeout.";
                        return false;
                    }
                }

                if (step.Kind ==
                    AppearanceTransitionStepKind.Commit)
                {
                    commitCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        step.ActionType))
                {
                    error =
                        $"Transition '{transition.Id}' contains an action step without an action type.";
                    return false;
                }

                if (step.Kind !=
                        AppearanceTransitionStepKind.Commit &&
                    step.ActionType.StartsWith(
                        "appearance.",
                        StringComparison.Ordinal))
                {
                    error =
                        $"Transition '{transition.Id}' cannot recursively execute appearance actions.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        step.StepId))
                {
                    if (!authoredActionSteps.TryAdd(
                            step.StepId,
                            step))
                    {
                        error =
                            $"Transition '{transition.Id}' contains duplicate action StepId '{step.StepId}'.";
                        return false;
                    }
                }
            }

            if (commitCount != 1)
            {
                error =
                    $"Transition '{transition.Id}' must contain exactly one appearance commit step.";
                return false;
            }

            if (transition.DurationSeconds <
                lastStepTime)
            {
                error =
                    $"Transition '{transition.Id}' duration cannot end before its last step.";
                return false;
            }

            if ((hasBlockingStep ||
                 hasDependencyStep) &&
                (transition.CancellationSteps == null ||
                 transition.CancellationSteps.Length == 0))
            {
                error =
                    $"Transition '{transition.Id}' contains completion waits and requires explicit cancellation cleanup steps.";
                return false;
            }

            return ValidateCancellationDefinition(
                transition,
                out error);
        }

        public static bool TryBuildMarkerTimes(
            AppearanceTransitionPreset transition,
            out Dictionary<string, double> markerTimes,
            out string error)
        {
            markerTimes =
                new Dictionary<string, double>(
                    StringComparer.Ordinal);
            error = null;

            foreach (var marker in
                     transition.Markers ??
                     Array.Empty<
                         AppearanceTransitionMarker>())
            {
                if (marker == null ||
                    string.IsNullOrWhiteSpace(
                        marker.Name))
                {
                    error =
                        $"Transition '{transition.Id}' contains a marker without a name.";
                    return false;
                }

                if (double.IsNaN(
                        marker.TimeSeconds) ||
                    double.IsInfinity(
                        marker.TimeSeconds) ||
                    marker.TimeSeconds < 0.0 ||
                    marker.TimeSeconds >
                        transition.DurationSeconds)
                {
                    error =
                        $"Transition '{transition.Id}' marker '{marker.Name}' must be within transition duration.";
                    return false;
                }

                if (!markerTimes.TryAdd(
                        marker.Name,
                        marker.TimeSeconds))
                {
                    error =
                        $"Transition '{transition.Id}' contains duplicate marker '{marker.Name}'.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryResolveStepTime(
            AppearanceTransitionStep step,
            IReadOnlyDictionary<string, double> markerTimes,
            out double resolvedTime,
            out string error)
        {
            resolvedTime = 0.0;
            error = null;

            if (step == null)
            {
                error =
                    "Transition step is null.";
                return false;
            }

            switch (step.TimingMode)
            {
                case AppearanceTransitionTimingMode.AbsoluteTime:
                    resolvedTime =
                        step.TimeSeconds;
                    break;

                case AppearanceTransitionTimingMode.Marker:
                    if (string.IsNullOrWhiteSpace(
                            step.MarkerName) ||
                        markerTimes == null ||
                        !markerTimes.TryGetValue(
                            step.MarkerName,
                            out var markerTime))
                    {
                        error =
                            $"Unknown transition marker '{step.MarkerName ?? "<null>"}'.";
                        return false;
                    }

                    if (double.IsNaN(
                            step.MarkerOffsetSeconds) ||
                        double.IsInfinity(
                            step.MarkerOffsetSeconds))
                    {
                        error =
                            $"Marker offset for '{step.MarkerName}' must be finite.";
                        return false;
                    }

                    resolvedTime =
                        markerTime +
                        step.MarkerOffsetSeconds;
                    break;

                default:
                    error =
                        $"Unsupported transition timing mode '{step.TimingMode}'.";
                    return false;
            }

            if (double.IsNaN(
                    resolvedTime) ||
                double.IsInfinity(
                    resolvedTime) ||
                resolvedTime < 0.0)
            {
                error =
                    "Resolved transition step time must be finite and non-negative.";
                return false;
            }

            return true;
        }

        public static string DescribeStep(
            AppearanceTransitionStep step)
        {
            if (step == null)
            {
                return "<null>";
            }

            if (!string.IsNullOrWhiteSpace(
                    step.StepId))
            {
                return step.StepId;
            }

            return step.Kind ==
                AppearanceTransitionStepKind.Commit
                ? "appearance.commit"
                : step.ActionType ??
                  "<action>";
        }

        private static bool ValidateCancellationDefinition(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            var cleanup =
                transition.CancellationSteps ??
                Array.Empty<
                    AppearanceTransitionStep>();

            if (transition.QueuePolicy ==
                    AppearanceTransitionQueuePolicy.Interrupt &&
                cleanup.Length == 0)
            {
                error =
                    $"Transition '{transition.Id}' uses Interrupt but has no explicit cancellation cleanup steps.";
                return false;
            }

            foreach (var step in cleanup)
            {
                if (step == null ||
                    step.Kind !=
                        AppearanceTransitionStepKind.Action)
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup may contain action steps only.";
                    return false;
                }

                if (step.TimingMode !=
                        AppearanceTransitionTimingMode.AbsoluteTime ||
                    step.TimeSeconds != 0.0 ||
                    step.Blocking ||
                    step.DependencyMode !=
                        AppearanceTransitionDependencyMode.None ||
                    (step.DependsOnStepIds != null &&
                     step.DependsOnStepIds.Length > 0))
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup steps execute immediately, must use absolute time 0, cannot block, and cannot declare dependencies.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(
                        step.ActionType) ||
                    step.ActionType.StartsWith(
                        "appearance.",
                        StringComparison.Ordinal))
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup requires non-appearance action types.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateStepDependencies(
            AppearanceTransitionPreset transition,
            AppearanceTransitionStep step,
            IReadOnlyDictionary<string, AppearanceTransitionStep>
                previousActionSteps,
            out string error)
        {
            error = null;
            var dependencyIds =
                step.DependsOnStepIds ??
                Array.Empty<string>();

            if (step.DependencyMode ==
                AppearanceTransitionDependencyMode.None)
            {
                if (dependencyIds.Length > 0)
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' lists dependencies but DependencyMode is None.";
                    return false;
                }

                return true;
            }

            if (step.DependencyMode !=
                    AppearanceTransitionDependencyMode.All &&
                step.DependencyMode !=
                    AppearanceTransitionDependencyMode.Any)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' has unsupported dependency mode '{step.DependencyMode}'.";
                return false;
            }

            if (dependencyIds.Length == 0)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' requires at least one dependency.";
                return false;
            }

            if (double.IsNaN(
                    step.DependencyTimeoutSeconds) ||
                double.IsInfinity(
                    step.DependencyTimeoutSeconds) ||
                step.DependencyTimeoutSeconds <= 0.0)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' requires a finite positive dependency timeout.";
                return false;
            }

            var seen =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var dependencyId in dependencyIds)
            {
                if (string.IsNullOrWhiteSpace(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' contains an empty dependency id.";
                    return false;
                }

                if (!seen.Add(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' repeats dependency '{dependencyId}'.";
                    return false;
                }

                if (!previousActionSteps.ContainsKey(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' dependency '{dependencyId}' must reference an earlier Action step with a StepId.";
                    return false;
                }
            }

            return true;
        }
    }
}
