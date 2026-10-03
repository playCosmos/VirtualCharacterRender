using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Appearance;

namespace VCR.Editor.P11
{
    internal static class
        P11AppearanceTransitionPackageUtility
    {
        public static AppearanceTransitionPackage
            CreatePackage(
                string packageId,
                IReadOnlyList<
                    AppearanceTransitionPreset> transitions)
        {
            var cloned =
                new AppearanceTransitionPreset[
                    transitions?.Count ?? 0];

            for (var i = 0;
                 i < cloned.Length;
                 i++)
            {
                cloned[i] =
                    CloneTransition(
                        transitions[i]);
            }

            return new AppearanceTransitionPackage
            {
                Version =
                    AppearanceTransitionPackage
                        .CurrentVersion,
                PackageId =
                    string.IsNullOrWhiteSpace(
                        packageId)
                        ? "appearance-transitions"
                        : packageId.Trim(),
                Transitions =
                    cloned
            };
        }

        public static bool TrySerialize(
            AppearanceTransitionPackage package,
            out string json,
            out string error)
        {
            json = null;

            if (!Validate(
                    package,
                    out error))
            {
                return false;
            }

            try
            {
                json =
                    JsonUtility.ToJson(
                        package,
                        prettyPrint:
                            true);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Transition package serialization failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryDeserialize(
            string json,
            out AppearanceTransitionPackage package,
            out string error)
        {
            package = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                error =
                    "Transition package JSON is empty.";
                return false;
            }

            try
            {
                package =
                    JsonUtility.FromJson<
                        AppearanceTransitionPackage>(
                        json);
            }
            catch (Exception exception)
            {
                error =
                    "Transition package JSON parse failed: " +
                    exception.Message;
                return false;
            }

            return Validate(
                package,
                out error);
        }

        public static bool Validate(
            AppearanceTransitionPackage package,
            out string error)
        {
            error = null;

            if (package == null)
            {
                error =
                    "Transition package document is missing.";
                return false;
            }

            if (package.Version !=
                AppearanceTransitionPackage
                    .CurrentVersion)
            {
                error =
                    package.Version >
                    AppearanceTransitionPackage
                        .CurrentVersion
                        ? $"Transition package version {package.Version} is newer than supported version {AppearanceTransitionPackage.CurrentVersion}."
                        : $"Transition package version {package.Version} is unsupported and has no migration path.";
                return false;
            }

            package.Transitions ??=
                Array.Empty<
                    AppearanceTransitionPreset>();

            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var transition in
                     package.Transitions)
            {
                if (transition == null ||
                    string.IsNullOrWhiteSpace(
                        transition.Id))
                {
                    error =
                        "Every packaged transition requires a non-empty id.";
                    return false;
                }

                if (!ids.Add(
                        transition.Id))
                {
                    error =
                        $"Transition package contains duplicate id '{transition.Id}'.";
                    return false;
                }

                if (transition.Markers == null)
                {
                    transition.Markers =
                        Array.Empty<
                            AppearanceTransitionMarker>();
                }

                if (transition.Steps == null)
                {
                    transition.Steps =
                        Array.Empty<
                            AppearanceTransitionStep>();
                }

                if (transition.CancellationSteps ==
                    null)
                {
                    transition.CancellationSteps =
                        Array.Empty<
                            AppearanceTransitionStep>();
                }
            }

            return true;
        }

        public static AppearanceTransitionPreset
            CloneTransition(
                AppearanceTransitionPreset source)
        {
            if (source == null)
            {
                return null;
            }

            return new AppearanceTransitionPreset
            {
                Id = source.Id,
                DurationSeconds =
                    source.DurationSeconds,
                QueuePolicy =
                    source.QueuePolicy,
                FallbackPolicy =
                    source.FallbackPolicy,
                Markers =
                    CloneMarkers(
                        source.Markers),
                Steps =
                    CloneSteps(
                        source.Steps),
                CancellationSteps =
                    CloneSteps(
                        source.CancellationSteps)
            };
        }

        private static AppearanceTransitionMarker[]
            CloneMarkers(
                AppearanceTransitionMarker[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceTransitionMarker>();

            var result =
                new AppearanceTransitionMarker[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var marker =
                    source[i];

                result[i] =
                    marker == null
                        ? null
                        : new AppearanceTransitionMarker
                        {
                            Name =
                                marker.Name,
                            TimeSeconds =
                                marker.TimeSeconds
                        };
            }

            return result;
        }

        private static AppearanceTransitionStep[]
            CloneSteps(
                AppearanceTransitionStep[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceTransitionStep>();

            var result =
                new AppearanceTransitionStep[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var step =
                    source[i];

                if (step == null)
                {
                    result[i] = null;
                    continue;
                }

                result[i] =
                    new AppearanceTransitionStep
                    {
                        TimeSeconds =
                            step.TimeSeconds,
                        TimingMode =
                            step.TimingMode,
                        MarkerName =
                            step.MarkerName,
                        MarkerOffsetSeconds =
                            step.MarkerOffsetSeconds,
                        StepId =
                            step.StepId,
                        DependencyMode =
                            step.DependencyMode,
                        DependsOnStepIds =
                            step.DependsOnStepIds != null
                                ? (string[])step.DependsOnStepIds.Clone()
                                : Array.Empty<string>(),
                        DependencyTimeoutSeconds =
                            step.DependencyTimeoutSeconds,
                        Kind =
                            step.Kind,
                        ActionType =
                            step.ActionType,
                        TargetId =
                            step.TargetId,
                        Name =
                            step.Name,
                        Text =
                            step.Text,
                        Value =
                            step.Value,
                        HasValue =
                            step.HasValue,
                        Required =
                            step.Required,
                        Blocking =
                            step.Blocking,
                        CompletionTimeoutSeconds =
                            step.CompletionTimeoutSeconds
                    };
            }

            return result;
        }
    }
}
