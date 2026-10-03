using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class EffectEventActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe,
        IRuntimeMetricsSource
    {
        [Serializable]
        public sealed class EffectBinding
        {
            public string EffectId;
            public GameObject Root;
            public ParticleSystem[] ParticleSystems =
                Array.Empty<ParticleSystem>();
            public bool RestartOnPlay = true;
            public bool DeactivateOnStop = false;
        }

        [SerializeField] private string handlerId =
            "effects.main";
        [SerializeField] private EffectBinding[] effects =
            Array.Empty<EffectBinding>();

        private readonly Dictionary<string, EffectBinding>
            _effects =
                new(StringComparer.Ordinal);

        private long _playCount;
        private long _stopCount;
        private long _failureCount;
        private string _lastError;

        public string HandlerId => handlerId;
        public string LastError => _lastError;

        private void Awake()
        {
            RebuildBindings(
                out _);
        }

        public void ConfigureBindings(
            params EffectBinding[] bindings)
        {
            effects =
                bindings ??
                Array.Empty<EffectBinding>();

            if (!RebuildBindings(
                    out var error))
            {
                _lastError = error;
            }
        }

        public bool RebuildBindings(
            out string error)
        {
            error = null;
            _effects.Clear();

            foreach (var binding in
                     effects ??
                     Array.Empty<EffectBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.EffectId))
                {
                    error =
                        "Every effect binding requires a non-empty effect id.";
                    return false;
                }

                if (binding.Root == null &&
                    (binding.ParticleSystems == null ||
                     binding.ParticleSystems.Length == 0))
                {
                    error =
                        $"Effect '{binding.EffectId}' requires a root or at least one ParticleSystem.";
                    return false;
                }

                if (!_effects.TryAdd(
                        binding.EffectId,
                        binding))
                {
                    error =
                        $"Duplicate effect id '{binding.EffectId}'.";
                    return false;
                }
            }

            _lastError = null;
            return true;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (command.ActionType !=
                    EventActionTypes.EffectPlay &&
                command.ActionType !=
                    EventActionTypes.EffectStop)
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    handlerId,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Effect action target is unavailable or does not match.";
                return Fail(error);
            }

            var effectId =
                command.Text;

            if (string.IsNullOrWhiteSpace(
                    effectId) ||
                !_effects.TryGetValue(
                    effectId,
                    out var binding))
            {
                error =
                    $"Unknown effect id '{effectId ?? "<null>"}'.";
                return Fail(error);
            }

            try
            {
                if (command.ActionType ==
                    EventActionTypes.EffectPlay)
                {
                    Play(binding);
                    _playCount++;
                }
                else
                {
                    Stop(binding);
                    _stopCount++;
                }

                _lastError = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"Effect '{effectId}' action failed: {exception.Message}";
                return Fail(error);
            }
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            if (!CanHandle(
                    command) ||
                string.IsNullOrWhiteSpace(
                    command.Text) ||
                !_effects.TryGetValue(
                    command.Text,
                    out var binding))
            {
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.EffectStop)
            {
                return true;
            }

            return binding.ParticleSystems != null &&
                   binding.ParticleSystems.Length > 0;
        }

        public bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (!CanTrackCompletion(
                    command))
            {
                error =
                    "Effect action completion cannot be tracked for this binding.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.EffectStop)
            {
                complete = true;
                return true;
            }

            var binding =
                _effects[
                    command.Text];

            complete = true;

            foreach (var system in
                     binding.ParticleSystems ??
                     Array.Empty<ParticleSystem>())
            {
                if (system != null &&
                    system.IsAlive(
                        withChildren: true))
                {
                    complete = false;
                    break;
                }
            }

            return true;
        }

        private static void Play(
            EffectBinding binding)
        {
            if (binding.Root != null &&
                !binding.Root.activeSelf)
            {
                binding.Root.SetActive(
                    true);
            }

            foreach (var system in
                     binding.ParticleSystems ??
                     Array.Empty<ParticleSystem>())
            {
                if (system == null)
                {
                    continue;
                }

                if (binding.RestartOnPlay)
                {
                    system.Stop(
                        withChildren: true,
                        stopBehavior:
                            ParticleSystemStopBehavior
                                .StopEmittingAndClear);
                }

                system.Play(
                    withChildren: true);
            }
        }

        private static void Stop(
            EffectBinding binding)
        {
            foreach (var system in
                     binding.ParticleSystems ??
                     Array.Empty<ParticleSystem>())
            {
                if (system == null)
                {
                    continue;
                }

                system.Stop(
                    withChildren: true,
                    stopBehavior:
                        ParticleSystemStopBehavior
                            .StopEmittingAndClear);
            }

            if (binding.DeactivateOnStop &&
                binding.Root != null)
            {
                binding.Root.SetActive(
                    false);
            }
        }

        private bool Fail(
            string error)
        {
            _failureCount++;
            _lastError = error;
            return false;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "effects.actions.play",
                    _playCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "effects.actions.stop",
                    _stopCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "effects.actions.failures",
                    _failureCount,
                    "count"));
        }
    }
}
