using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class AudioEventActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe,
        IRuntimeMetricsSource
    {
        [Serializable]
        public sealed class AudioBinding
        {
            public string AudioId;
            public AudioSource Source;
            public AudioClip Clip;
            public bool RestartOnPlay = true;
            public bool Loop = false;
        }

        [SerializeField] private string handlerId =
            "audio.main";
        [SerializeField] private AudioBinding[] audio =
            Array.Empty<AudioBinding>();

        private readonly Dictionary<string, AudioBinding>
            _audio =
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
            params AudioBinding[] bindings)
        {
            audio =
                bindings ??
                Array.Empty<AudioBinding>();

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
            _audio.Clear();

            foreach (var binding in
                     audio ??
                     Array.Empty<AudioBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.AudioId) ||
                    binding.Source == null)
                {
                    error =
                        "Every audio binding requires a non-empty audio id and AudioSource.";
                    return false;
                }

                if (binding.Clip == null &&
                    binding.Source.clip == null)
                {
                    error =
                        $"Audio '{binding.AudioId}' requires either a binding clip or an AudioSource clip.";
                    return false;
                }

                if (!_audio.TryAdd(
                        binding.AudioId,
                        binding))
                {
                    error =
                        $"Duplicate audio id '{binding.AudioId}'.";
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
                    EventActionTypes.AudioPlay &&
                command.ActionType !=
                    EventActionTypes.AudioStop)
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

            if (!CanHandle(
                    command))
            {
                error =
                    "Audio action target is unavailable or does not match.";
                return Fail(
                    error);
            }

            var audioId =
                command.Text;

            if (string.IsNullOrWhiteSpace(
                    audioId) ||
                !_audio.TryGetValue(
                    audioId,
                    out var binding))
            {
                error =
                    $"Unknown audio id '{audioId ?? "<null>"}'.";
                return Fail(
                    error);
            }

            if (command.HasValue &&
                (double.IsNaN(
                     command.Value) ||
                 double.IsInfinity(
                     command.Value) ||
                 command.Value >
                     float.MaxValue ||
                 command.Value <
                     -float.MaxValue))
            {
                error =
                    "audio.play volume override requires a finite float-range value.";
                return Fail(
                    error);
            }

            try
            {
                if (command.ActionType ==
                    EventActionTypes.AudioPlay)
                {
                    Play(
                        binding,
                        command);
                    _playCount++;
                }
                else
                {
                    binding.Source.Stop();
                    _stopCount++;
                }

                _lastError = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"Audio '{audioId}' action failed: {exception.Message}";
                return Fail(
                    error);
            }
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            return
                CanHandle(
                    command) &&
                !string.IsNullOrWhiteSpace(
                    command.Text) &&
                _audio.ContainsKey(
                    command.Text);
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
                    "Audio action completion cannot be tracked for this binding.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.AudioStop)
            {
                complete = true;
                return true;
            }

            complete =
                !_audio[
                    command.Text]
                    .Source
                    .isPlaying;
            return true;
        }

        private static void Play(
            AudioBinding binding,
            EventActionCommand command)
        {
            if (binding.RestartOnPlay)
            {
                binding.Source.Stop();
            }

            if (binding.Clip != null)
            {
                binding.Source.clip =
                    binding.Clip;
            }

            binding.Source.loop =
                binding.Loop;

            if (command.HasValue)
            {
                binding.Source.volume =
                    Mathf.Clamp01(
                        (float)command.Value);
            }

            binding.Source.Play();
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
                    "audio.actions.play",
                    _playCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "audio.actions.stop",
                    _stopCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "audio.actions.failures",
                    _failureCount,
                    "count"));
        }
    }
}
