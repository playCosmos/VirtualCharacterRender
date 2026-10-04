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

        public static bool TryAssignDependencyGroup(
            AppearanceTransitionPreset transition,
            int targetIndex,
            string groupName,
            bool includeSources,
            out int affectedSteps,
            out string error)
        {
            affectedSteps = 0;
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

            if (targetIndex < 0 ||
                targetIndex >=
                    steps.Length)
            {
                error =
                    "Dependency group target index is outside the transition step range.";
                return false;
            }

            var target =
                steps[targetIndex];

            if (target == null)
            {
                error =
                    "Dependency group target step is missing.";
                return false;
            }

            var dependencies =
                target.DependsOnStepIds ??
                Array.Empty<string>();

            if (dependencies.Length == 0)
            {
                error =
                    "Dependency group target has no dependency sources.";
                return false;
            }

            var resolvedSources =
                new List<
                    AppearanceTransitionStep>();
            var seen =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var dependencyId in
                     dependencies)
            {
                if (string.IsNullOrWhiteSpace(
                        dependencyId) ||
                    !seen.Add(
                        dependencyId))
                {
                    error =
                        "Dependency group contains an empty or duplicate source id.";
                    return false;
                }

                AppearanceTransitionStep
                    match = null;

                for (var i = 0;
                     i < targetIndex;
                     i++)
                {
                    var candidate =
                        steps[i];

                    if (candidate == null ||
                        candidate.Kind !=
                            AppearanceTransitionStepKind.Action ||
                        !string.Equals(
                            candidate.StepId,
                            dependencyId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    match =
                        candidate;
                    break;
                }

                if (match == null)
                {
                    error =
                        $"Dependency group source '{dependencyId}' does not resolve to an earlier Action step.";
                    return false;
                }

                resolvedSources.Add(
                    match);
            }

            var normalized =
                groupName?.Trim() ??
                string.Empty;

            target.AuthoringGroup =
                normalized;
            affectedSteps++;

            if (includeSources)
            {
                foreach (var source in
                         resolvedSources)
                {
                    source.AuthoringGroup =
                        normalized;
                    affectedSteps++;
                }
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
