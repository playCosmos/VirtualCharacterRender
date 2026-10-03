using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Lightweight transition target for screen/UI environment states.
    ///
    /// Fade and Crossfade are supported without shader mutation. Dissolve is
    /// intentionally rejected so shader-driven dissolve remains an explicit,
    /// separately profiled capability.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CanvasGroupEnvironmentTransitionTarget :
        MonoBehaviour,
        IEnvironmentTransitionTarget
    {
        [SerializeField]
        private EnvironmentCanvasGroupBinding[] stateGroups =
            Array.Empty<EnvironmentCanvasGroupBinding>();

        public void Configure(
            params EnvironmentCanvasGroupBinding[] bindings)
        {
            stateGroups =
                bindings == null
                    ? Array.Empty<
                        EnvironmentCanvasGroupBinding>()
                    : (EnvironmentCanvasGroupBinding[])
                        bindings.Clone();
        }

        public bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error)
        {
            error = null;

            if (transition.Mode ==
                EnvironmentTransitionMode.Dissolve)
            {
                error =
                    "CanvasGroup transition target does not support Dissolve.";
                return false;
            }

            if (transition.Mode !=
                    EnvironmentTransitionMode.Fade &&
                transition.Mode !=
                    EnvironmentTransitionMode.Crossfade)
            {
                return true;
            }

            if (!ValidateBindings(
                    out error))
            {
                return false;
            }

            if (!TryGetGroup(
                    previousStateId,
                    out var previous))
            {
                error =
                    $"CanvasGroup transition binding '{previousStateId}' was not found.";
                return false;
            }

            if (!TryGetGroup(
                    nextStateId,
                    out var next))
            {
                error =
                    $"CanvasGroup transition binding '{nextStateId}' was not found.";
                return false;
            }

            if (ReferenceEquals(
                    previous,
                    next))
            {
                error =
                    "Environment transition states must not share the same CanvasGroup.";
                return false;
            }

            return true;
        }

        public void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context)
        {
            if (context.Mode ==
                EnvironmentTransitionMode.Cut)
            {
                ApplyCut(
                    context.StateId);
                return;
            }

            if (!TryGetGroup(
                    context.PreviousStateId,
                    out var previous) ||
                !TryGetGroup(
                    context.StateId,
                    out var next))
            {
                return;
            }

            var progress =
                Mathf.Clamp01(
                    context.Progress);

            if (context.Mode ==
                EnvironmentTransitionMode.Crossfade)
            {
                previous.alpha =
                    1f - progress;
                next.alpha =
                    progress;
                return;
            }

            if (context.Mode ==
                EnvironmentTransitionMode.Fade)
            {
                if (progress <= 0.5f)
                {
                    previous.alpha =
                        1f -
                        progress * 2f;
                    next.alpha = 0f;
                }
                else
                {
                    previous.alpha = 0f;
                    next.alpha =
                        (progress - 0.5f) *
                        2f;
                }
            }
        }

        private void ApplyCut(
            string activeStateId)
        {
            if (stateGroups == null)
            {
                return;
            }

            foreach (var binding in
                     stateGroups)
            {
                if (binding?.CanvasGroup == null)
                {
                    continue;
                }

                binding.CanvasGroup.alpha =
                    string.Equals(
                        binding.StateId,
                        activeStateId,
                        StringComparison.Ordinal)
                        ? 1f
                        : 0f;
            }
        }

        private bool ValidateBindings(
            out string error)
        {
            error = null;

            if (stateGroups == null ||
                stateGroups.Length == 0)
            {
                error =
                    "CanvasGroup transition target requires state bindings.";
                return false;
            }

            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var binding in
                     stateGroups)
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.StateId) ||
                    binding.CanvasGroup == null)
                {
                    error =
                        "CanvasGroup transition bindings require a state id and CanvasGroup.";
                    return false;
                }

                if (!ids.Add(
                        binding.StateId))
                {
                    error =
                        $"Duplicate CanvasGroup transition binding '{binding.StateId}'.";
                    return false;
                }
            }

            return true;
        }

        private bool TryGetGroup(
            string state,
            out CanvasGroup group)
        {
            if (stateGroups != null)
            {
                foreach (var binding in
                         stateGroups)
                {
                    if (binding?.CanvasGroup != null &&
                        string.Equals(
                            binding.StateId,
                            state,
                            StringComparison.Ordinal))
                    {
                        group =
                            binding.CanvasGroup;
                        return true;
                    }
                }
            }

            group = null;
            return false;
        }
    }
}
