using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class PropEventActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe,
        IRuntimeMetricsSource
    {
        [Serializable]
        public sealed class PropBinding
        {
            public string PropId;
            public GameObject[] Roots =
                Array.Empty<GameObject>();
        }

        [SerializeField] private string handlerId =
            "props.main";
        [SerializeField] private PropBinding[] props =
            Array.Empty<PropBinding>();

        private readonly Dictionary<string, PropBinding>
            _props =
                new(StringComparer.Ordinal);

        private long _setCount;
        private long _toggleCount;
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
            params PropBinding[] bindings)
        {
            props =
                bindings ??
                Array.Empty<PropBinding>();

            if (!RebuildBindings(
                    out var error))
            {
                _lastError =
                    error;
            }
        }

        public bool RebuildBindings(
            out string error)
        {
            error = null;
            _props.Clear();
            var rootOwners =
                new Dictionary<int, string>();

            foreach (var binding in
                     props ??
                     Array.Empty<PropBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.PropId))
                {
                    error =
                        "Every prop binding requires a non-empty prop id.";
                    return false;
                }

                var id =
                    binding.PropId.Trim();
                var roots =
                    binding.Roots ??
                    Array.Empty<GameObject>();

                if (roots.Length == 0)
                {
                    error =
                        $"Prop '{id}' requires at least one root.";
                    return false;
                }

                var seen =
                    new HashSet<int>();

                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        error =
                            $"Prop '{id}' contains a null root.";
                        return false;
                    }

                    var instanceId =
                        root.GetInstanceID();

                    if (!seen.Add(
                            instanceId))
                    {
                        error =
                            $"Prop '{id}' contains the same root more than once.";
                        return false;
                    }

                    if (rootOwners.TryGetValue(
                            instanceId,
                            out var owner))
                    {
                        error =
                            $"Prop root '{root.name}' is shared by '{owner}' and '{id}'.";
                        return false;
                    }

                    rootOwners.Add(
                        instanceId,
                        id);
                }

                if (!_props.TryAdd(
                        id,
                        binding))
                {
                    error =
                        $"Duplicate prop id '{id}'.";
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
                    EventActionTypes.PropSetActive &&
                command.ActionType !=
                    EventActionTypes.PropToggle)
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
                    "Prop action target is unavailable or does not match.";
                return Fail(
                    error);
            }

            var propId =
                command.Text?.Trim();

            if (string.IsNullOrWhiteSpace(
                    propId) ||
                !_props.TryGetValue(
                    propId,
                    out var binding))
            {
                error =
                    $"Unknown prop id '{propId ?? "<null>"}'.";
                return Fail(
                    error);
            }

            try
            {
                if (command.ActionType ==
                    EventActionTypes.PropSetActive)
                {
                    if (!command.HasValue)
                    {
                        error =
                            $"Prop '{propId}' set_active requires a boolean numeric value.";
                        return Fail(
                            error);
                    }

                    var active =
                        command.Value >=
                        0.5;

                    SetActive(
                        binding,
                        active);
                    _setCount++;
                }
                else
                {
                    if (!TryReadUniformState(
                            binding,
                            out var active,
                            out error))
                    {
                        return Fail(
                            error);
                    }

                    SetActive(
                        binding,
                        !active);
                    _toggleCount++;
                }

                _lastError = null;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"Prop '{propId}' action failed: {exception.Message}";
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
                _props.ContainsKey(
                    command.Text.Trim());
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
                    "Prop action completion cannot be tracked for this binding.";
                return false;
            }

            complete = true;
            return true;
        }

        private static void SetActive(
            PropBinding binding,
            bool active)
        {
            foreach (var root in
                     binding.Roots ??
                     Array.Empty<GameObject>())
            {
                if (root != null &&
                    root.activeSelf !=
                        active)
                {
                    root.SetActive(
                        active);
                }
            }
        }

        private static bool TryReadUniformState(
            PropBinding binding,
            out bool active,
            out string error)
        {
            active = false;
            error = null;
            var roots =
                binding.Roots ??
                Array.Empty<GameObject>();

            if (roots.Length == 0 ||
                roots[0] == null)
            {
                error =
                    $"Prop '{binding.PropId}' has no readable root state.";
                return false;
            }

            active =
                roots[0].activeSelf;

            for (var i = 1;
                 i < roots.Length;
                 i++)
            {
                if (roots[i] == null)
                {
                    error =
                        $"Prop '{binding.PropId}' contains a null root.";
                    return false;
                }

                if (roots[i].activeSelf !=
                    active)
                {
                    error =
                        $"Prop '{binding.PropId}' roots are in mixed active states; toggle is ambiguous.";
                    return false;
                }
            }

            return true;
        }

        private bool Fail(
            string error)
        {
            _failureCount++;
            _lastError =
                error;
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
                    "props.actions.set",
                    _setCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "props.actions.toggle",
                    _toggleCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "props.actions.failures",
                    _failureCount,
                    "count"));
        }
    }
}
