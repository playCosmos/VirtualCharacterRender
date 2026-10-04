using System;
using System.Collections.Generic;
using VCR.Runtime.Appearance;

namespace VCR.Editor.P12
{
    internal static class P12TransitionDependencyAuthoringUtility
    {
        public static bool TryAddDependency(
            AppearanceTransitionPreset transition,
            int sourceIndex,
            int targetIndex,
            AppearanceTransitionDependencyMode mode,
            out string error)
        {
            error = null;

            if (!TryResolveEndpoints(
                    transition,
                    sourceIndex,
                    targetIndex,
                    out var source,
                    out var target,
                    out error))
            {
                return false;
            }

            if (mode !=
                    AppearanceTransitionDependencyMode.All &&
                mode !=
                    AppearanceTransitionDependencyMode.Any)
            {
                error =
                    "Dependency edge authoring requires All or Any mode.";
                return false;
            }

            var sourceId =
                source.StepId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sourceId))
            {
                error =
                    "Dependency source Action requires a non-empty StepId.";
                return false;
            }

            var dependencies =
                new List<string>(
                    target.DependsOnStepIds ??
                    Array.Empty<string>());

            foreach (var dependency in dependencies)
            {
                if (string.Equals(
                        dependency,
                        sourceId,
                        StringComparison.Ordinal))
                {
                    target.DependencyMode =
                        mode;
                    return true;
                }
            }

            dependencies.Add(
                sourceId);
            target.DependsOnStepIds =
                dependencies.ToArray();
            target.DependencyMode =
                mode;

            if (target.DependencyTimeoutSeconds <=
                    0.0 ||
                double.IsNaN(
                    target.DependencyTimeoutSeconds) ||
                double.IsInfinity(
                    target.DependencyTimeoutSeconds))
            {
                target.DependencyTimeoutSeconds =
                    5.0;
            }

            return true;
        }

        public static bool TryRemoveDependency(
            AppearanceTransitionPreset transition,
            int sourceIndex,
            int targetIndex,
            out string error)
        {
            error = null;

            if (!TryResolveEndpoints(
                    transition,
                    sourceIndex,
                    targetIndex,
                    out var source,
                    out var target,
                    out error))
            {
                return false;
            }

            var sourceId =
                source.StepId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sourceId))
            {
                error =
                    "Dependency source Action requires a non-empty StepId.";
                return false;
            }

            var dependencies =
                new List<string>(
                    target.DependsOnStepIds ??
                    Array.Empty<string>());
            var removed =
                dependencies.RemoveAll(
                    dependency =>
                        string.Equals(
                            dependency,
                            sourceId,
                            StringComparison.Ordinal));

            if (removed == 0)
            {
                error =
                    $"Dependency '{sourceId}' is not connected to target step {targetIndex + 1}.";
                return false;
            }

            target.DependsOnStepIds =
                dependencies.ToArray();

            if (dependencies.Count == 0)
            {
                target.DependencyMode =
                    AppearanceTransitionDependencyMode.None;
            }

            return true;
        }

        public static bool HasDependency(
            AppearanceTransitionPreset transition,
            int sourceIndex,
            int targetIndex)
        {
            if (!TryResolveEndpoints(
                    transition,
                    sourceIndex,
                    targetIndex,
                    out var source,
                    out var target,
                    out _))
            {
                return false;
            }

            var sourceId =
                source.StepId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sourceId))
            {
                return false;
            }

            foreach (var dependency in
                     target.DependsOnStepIds ??
                     Array.Empty<string>())
            {
                if (string.Equals(
                        dependency,
                        sourceId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveEndpoints(
            AppearanceTransitionPreset transition,
            int sourceIndex,
            int targetIndex,
            out AppearanceTransitionStep source,
            out AppearanceTransitionStep target,
            out string error)
        {
            source = null;
            target = null;
            error = null;

            var steps =
                transition?.Steps;

            if (steps == null ||
                steps.Length == 0)
            {
                error =
                    "Transition has no authored steps.";
                return false;
            }

            if (sourceIndex < 0 ||
                sourceIndex >=
                    steps.Length ||
                targetIndex < 0 ||
                targetIndex >=
                    steps.Length)
            {
                error =
                    "Dependency source/target index is outside the transition step range.";
                return false;
            }

            if (sourceIndex >=
                targetIndex)
            {
                error =
                    "Dependency source must be an earlier step than its target.";
                return false;
            }

            source =
                steps[sourceIndex];
            target =
                steps[targetIndex];

            if (source == null ||
                source.Kind !=
                    AppearanceTransitionStepKind.Action)
            {
                error =
                    "Dependency source must be an Action step.";
                return false;
            }

            if (target == null)
            {
                error =
                    "Dependency target step is missing.";
                return false;
            }

            return true;
        }
    }
}
