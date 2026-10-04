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

            string normalized;

            if (string.IsNullOrWhiteSpace(
                    groupName))
            {
                normalized =
                    string.Empty;
            }
            else if (!TryNormalizeGroupPath(
                         groupName,
                         out normalized,
                         out error))
            {
                return false;
            }

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

        public static bool TryNormalizeGroupPath(
            string groupPath,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    groupPath))
            {
                error =
                    "Graph group path is required.";
                return false;
            }

            var rawSegments =
                groupPath
                    .Trim()
                    .Split(
                        new[]
                        {
                            '/'
                        },
                        StringSplitOptions.None);
            var segments =
                new List<string>(
                    rawSegments.Length);

            foreach (var raw in rawSegments)
            {
                var segment =
                    raw?.Trim();

                if (string.IsNullOrWhiteSpace(
                        segment))
                {
                    error =
                        "Graph group path cannot contain empty segments.";
                    return false;
                }

                if (segment == "." ||
                    segment == "..")
                {
                    error =
                        "Graph group path cannot use '.' or '..' segments.";
                    return false;
                }

                segments.Add(
                    segment);
            }

            normalized =
                string.Join(
                    "/",
                    segments);
            return true;
        }

        public static bool TryValidateGroupMetadata(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            foreach (var step in
                     transition?.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    string.IsNullOrWhiteSpace(
                        step.AuthoringGroup))
                {
                    continue;
                }

                if (!TryNormalizeGroupPath(
                        step.AuthoringGroup,
                        out _,
                        out var groupError))
                {
                    error =
                        $"Graph group '{step.AuthoringGroup}' is invalid: {groupError}";
                    return false;
                }
            }

            foreach (var cleanup in
                     transition?.CancellationSteps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (cleanup != null &&
                    !string.IsNullOrWhiteSpace(
                        cleanup.AuthoringGroup))
                {
                    error =
                        "Cancellation cleanup steps cannot carry graph group metadata.";
                    return false;
                }
            }

            return true;
        }

        public static string[] CaptureGroupPaths(
            AppearanceTransitionPreset transition)
        {
            var result =
                new SortedSet<string>(
                    StringComparer.Ordinal);

            foreach (var step in
                     transition?.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    string.IsNullOrWhiteSpace(
                        step.AuthoringGroup) ||
                    !TryNormalizeGroupPath(
                        step.AuthoringGroup,
                        out var normalized,
                        out _))
                {
                    continue;
                }

                var segments =
                    normalized.Split('/');

                for (var i = 1;
                     i <= segments.Length;
                     i++)
                {
                    result.Add(
                        string.Join(
                            "/",
                            segments,
                            0,
                            i));
                }
            }

            var paths =
                new string[
                    result.Count];
            result.CopyTo(
                paths);
            return paths;
        }

        public static bool TryRewriteGroupHierarchy(
            AppearanceTransitionPreset transition,
            string sourceGroupPath,
            string destinationGroupPath,
            bool includeDescendants,
            out int affectedSteps,
            out string error)
        {
            affectedSteps = 0;
            error = null;

            if (!TryNormalizeGroupPath(
                    sourceGroupPath,
                    out var source,
                    out error))
            {
                return false;
            }

            if (!TryNormalizeGroupPath(
                    destinationGroupPath,
                    out var destination,
                    out error))
            {
                return false;
            }

            if (string.Equals(
                    source,
                    destination,
                    StringComparison.Ordinal))
            {
                error =
                    "Source and destination graph group paths are identical.";
                return false;
            }

            if (includeDescendants &&
                destination.StartsWith(
                    source + "/",
                    StringComparison.Ordinal))
            {
                error =
                    "A graph group hierarchy cannot be moved inside itself.";
                return false;
            }

            var steps =
                transition?.Steps;

            if (steps == null ||
                steps.Length == 0)
            {
                error =
                    "Transition has no authored steps.";
                return false;
            }

            foreach (var step in steps)
            {
                if (step == null ||
                    string.IsNullOrWhiteSpace(
                        step.AuthoringGroup) ||
                    !TryNormalizeGroupPath(
                        step.AuthoringGroup,
                        out var current,
                        out _))
                {
                    continue;
                }

                if (string.Equals(
                        current,
                        source,
                        StringComparison.Ordinal))
                {
                    step.AuthoringGroup =
                        destination;
                    affectedSteps++;
                    continue;
                }

                if (!includeDescendants ||
                    !current.StartsWith(
                        source + "/",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                step.AuthoringGroup =
                    destination +
                    current.Substring(
                        source.Length);
                affectedSteps++;
            }

            if (affectedSteps == 0)
            {
                error =
                    $"Graph group path '{source}' is not assigned to any transition step.";
                return false;
            }

            return true;
        }

        public static bool TryClearGroupHierarchy(
            AppearanceTransitionPreset transition,
            string sourceGroupPath,
            bool includeDescendants,
            out int affectedSteps,
            out string error)
        {
            affectedSteps = 0;
            error = null;

            if (!TryNormalizeGroupPath(
                    sourceGroupPath,
                    out var source,
                    out error))
            {
                return false;
            }

            var steps =
                transition?.Steps;

            if (steps == null ||
                steps.Length == 0)
            {
                error =
                    "Transition has no authored steps.";
                return false;
            }

            foreach (var step in steps)
            {
                if (step == null ||
                    string.IsNullOrWhiteSpace(
                        step.AuthoringGroup) ||
                    !TryNormalizeGroupPath(
                        step.AuthoringGroup,
                        out var current,
                        out _))
                {
                    continue;
                }

                var exact =
                    string.Equals(
                        current,
                        source,
                        StringComparison.Ordinal);
                var descendant =
                    includeDescendants &&
                    current.StartsWith(
                        source + "/",
                        StringComparison.Ordinal);

                if (!exact &&
                    !descendant)
                {
                    continue;
                }

                step.AuthoringGroup =
                    string.Empty;
                affectedSteps++;
            }

            if (affectedSteps == 0)
            {
                error =
                    $"Graph group path '{source}' is not assigned to any transition step.";
                return false;
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
