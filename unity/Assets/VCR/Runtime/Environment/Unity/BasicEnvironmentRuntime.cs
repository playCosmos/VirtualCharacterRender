using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// One-environment runtime with explicit state roots and update classes.
    ///
    /// Static/EventDriven policies keep this component free of Update().
    /// Recurring work is delegated to EnvironmentUpdateDriver, which is created
    /// only when a Hz10/Hz30/EveryFrame policy is selected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasicEnvironmentRuntime :
        MonoBehaviour,
        IEnvironmentRuntime,
        IRuntimeMetricsSource
    {
        [SerializeField] private string environmentId =
            "environment.p0.basic";

        [SerializeField] private string stateId =
            "default";

        [SerializeField] private EnvironmentUpdatePolicy updatePolicy =
            EnvironmentUpdatePolicy.Static;

        [SerializeField] private EnvironmentSpaceMode spaceMode =
            EnvironmentSpaceMode.World;

        [Header("State bindings")]
        [SerializeField] private EnvironmentStateBinding[] stateBindings =
            Array.Empty<EnvironmentStateBinding>();

        [Header("Update targets")]
        [SerializeField] private MonoBehaviour[] updateTargetBehaviours =
            Array.Empty<MonoBehaviour>();

        private readonly List<IEnvironmentUpdateTarget> _updateTargets =
            new();
        private readonly EnvironmentUpdateScheduler _scheduler =
            new();

        private EnvironmentUpdateDriver _updateDriver;
        private long _stateChangeCount;
        private long _updateSequence;
        private long _scheduledUpdateCount;
        private long _eventUpdateCount;
        private long _manualUpdateCount;
        private long _updateFailureCount;
        private string _lastError;

        public event Action<EnvironmentStateChange> StateChanged;

        public EnvironmentRuntimeStatus Status =>
            new EnvironmentRuntimeStatus(
                environmentId,
                stateId,
                updatePolicy,
                isActiveAndEnabled,
                _lastError);

        public EnvironmentSpaceMode SpaceMode =>
            spaceMode;

        public long StateChangeCount =>
            _stateChangeCount;

        public long ScheduledUpdateCount =>
            _scheduledUpdateCount;

        public int UpdateTargetCount =>
            _updateTargets.Count;

        private void OnEnable()
        {
            ResolveUpdateTargets();

            if (!TryApplyStateBinding(
                    stateId,
                    out var bindingError))
            {
                _lastError = bindingError;
            }

            ApplyUpdatePolicy(
                MonotonicClock.NowMicroseconds());
        }

        private void OnDisable()
        {
            if (_updateDriver != null)
            {
                _updateDriver.enabled = false;
            }
        }

        public void Configure(
            string id,
            string initialState,
            EnvironmentUpdatePolicy policy =
                EnvironmentUpdatePolicy.Static,
            EnvironmentSpaceMode mode =
                EnvironmentSpaceMode.World)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                environmentId = id;
            }

            if (!string.IsNullOrWhiteSpace(
                    initialState))
            {
                stateId = initialState;
            }

            updatePolicy = policy;
            spaceMode = mode;
            _lastError = null;

            if (!TryApplyStateBinding(
                    stateId,
                    out var bindingError))
            {
                _lastError = bindingError;
            }

            ApplyUpdatePolicy(
                MonotonicClock.NowMicroseconds());
        }

        public bool ConfigureStateBindings(
            EnvironmentStateBinding[] bindings,
            out string error)
        {
            error = null;

            var next =
                bindings == null
                    ? Array.Empty<EnvironmentStateBinding>()
                    : (EnvironmentStateBinding[])
                        bindings.Clone();

            if (!ValidateStateBindings(
                    next,
                    out error))
            {
                _lastError = error;
                return false;
            }

            if (next.Length > 0 &&
                !ContainsState(
                    next,
                    stateId))
            {
                error =
                    $"Environment state '{stateId}' has no binding.";
                _lastError = error;
                return false;
            }

            stateBindings = next;

            if (!TryApplyStateBinding(
                    stateId,
                    out error))
            {
                _lastError = error;
                return false;
            }

            _lastError = null;
            return true;
        }

        public void ConfigureUpdateTargets(
            params MonoBehaviour[] targets)
        {
            updateTargetBehaviours =
                targets == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])targets.Clone();

            ResolveUpdateTargets();
        }

        public void RegisterUpdateTarget(
            IEnvironmentUpdateTarget target)
        {
            if (target == null ||
                _updateTargets.Contains(target))
            {
                return;
            }

            _updateTargets.Add(target);
        }

        public void UnregisterUpdateTarget(
            IEnvironmentUpdateTarget target)
        {
            if (target == null)
            {
                return;
            }

            _updateTargets.Remove(target);
        }

        public bool SetState(
            string nextStateId,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    nextStateId))
            {
                _lastError =
                    "Environment state id is required.";
                error = _lastError;
                return false;
            }

            if (string.Equals(
                    stateId,
                    nextStateId,
                    StringComparison.Ordinal))
            {
                _lastError = null;
                return true;
            }

            if (!TryApplyStateBinding(
                    nextStateId,
                    out error))
            {
                _lastError = error;
                return false;
            }

            var previous = stateId;
            stateId = nextStateId;
            _lastError = null;
            _stateChangeCount++;

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    stateId));

            DispatchUpdate(
                EnvironmentUpdateReason.StateChanged,
                MonotonicClock.NowMicroseconds(),
                0f);

            return true;
        }

        public void RequestUpdate()
        {
            DispatchUpdate(
                EnvironmentUpdateReason.Manual,
                MonotonicClock.NowMicroseconds(),
                0f);
        }

        internal void TickScheduled(
            long nowUs)
        {
            if (!_scheduler.IsDue(nowUs))
            {
                return;
            }

            var deltaSeconds =
                _scheduler.MarkDispatched(
                    nowUs);

            DispatchUpdate(
                EnvironmentUpdateReason.Scheduled,
                nowUs,
                deltaSeconds);
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "environment.active",
                isActiveAndEnabled ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "environment.state_changes",
                _stateChangeCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.update_policy",
                (int)updatePolicy,
                "enum"));

            output.Add(new RuntimeMetric(
                "environment.update_targets",
                _updateTargets.Count,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.scheduled_updates",
                _scheduledUpdateCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.event_updates",
                _eventUpdateCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.manual_updates",
                _manualUpdateCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.update_failures",
                _updateFailureCount,
                "count"));
        }

        private void ApplyUpdatePolicy(
            long nowUs)
        {
            _scheduler.Configure(
                updatePolicy,
                nowUs);

            if (!_scheduler.HasRecurringUpdates)
            {
                if (_updateDriver != null)
                {
                    _updateDriver.enabled = false;
                }

                return;
            }

            if (_updateDriver == null)
            {
                _updateDriver =
                    GetComponent<
                        EnvironmentUpdateDriver>();

                if (_updateDriver == null)
                {
                    _updateDriver =
                        gameObject.AddComponent<
                            EnvironmentUpdateDriver>();
                }
            }

            _updateDriver.Bind(this);
            _updateDriver.enabled =
                isActiveAndEnabled;
        }

        private void ResolveUpdateTargets()
        {
            _updateTargets.Clear();

            if (updateTargetBehaviours == null)
            {
                return;
            }

            foreach (var behaviour in
                     updateTargetBehaviours)
            {
                if (behaviour is
                        IEnvironmentUpdateTarget target &&
                    !_updateTargets.Contains(target))
                {
                    _updateTargets.Add(target);
                }
            }
        }

        private void DispatchUpdate(
            EnvironmentUpdateReason reason,
            long timestampUs,
            float deltaSeconds)
        {
            switch (reason)
            {
                case EnvironmentUpdateReason.Scheduled:
                    _scheduledUpdateCount++;
                    break;
                case EnvironmentUpdateReason.StateChanged:
                    _eventUpdateCount++;
                    break;
                case EnvironmentUpdateReason.Manual:
                    _manualUpdateCount++;
                    break;
            }

            var context =
                new EnvironmentUpdateContext(
                    ++_updateSequence,
                    timestampUs,
                    deltaSeconds,
                    reason,
                    stateId);

            for (var i = 0;
                 i < _updateTargets.Count;
                 i++)
            {
                var target =
                    _updateTargets[i];

                if (target == null)
                {
                    continue;
                }

                try
                {
                    target.UpdateEnvironment(
                        context);
                }
                catch (Exception exception)
                {
                    _updateFailureCount++;
                    _lastError =
                        "Environment update target failed: " +
                        exception.Message;

                    Debug.LogWarning(
                        _lastError,
                        this);
                }
            }
        }

        private bool TryApplyStateBinding(
            string nextStateId,
            out string error)
        {
            error = null;

            if (stateBindings == null ||
                stateBindings.Length == 0)
            {
                return true;
            }

            var found = false;

            for (var i = 0;
                 i < stateBindings.Length;
                 i++)
            {
                var binding =
                    stateBindings[i];

                if (binding == null)
                {
                    continue;
                }

                var isTarget =
                    string.Equals(
                        binding.StateId,
                        nextStateId,
                        StringComparison.Ordinal);

                if (isTarget)
                {
                    found = true;
                }

                var root =
                    binding.Root;

                if (root == null)
                {
                    continue;
                }

                if (transform.IsChildOf(
                        root.transform))
                {
                    error =
                        $"Environment state root '{root.name}' contains the environment runtime and cannot be toggled safely.";
                    return false;
                }

                if (root.activeSelf != isTarget)
                {
                    root.SetActive(
                        isTarget);
                }
            }

            if (!found)
            {
                error =
                    $"Environment state '{nextStateId}' has no binding.";
                return false;
            }

            return true;
        }

        private bool ValidateStateBindings(
            EnvironmentStateBinding[] bindings,
            out string error)
        {
            error = null;
            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var binding in bindings)
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.StateId))
                {
                    error =
                        "Environment state bindings require a non-empty state id.";
                    return false;
                }

                if (binding.Root == null)
                {
                    error =
                        $"Environment state '{binding.StateId}' requires a root GameObject.";
                    return false;
                }

                if (!ids.Add(
                        binding.StateId))
                {
                    error =
                        $"Duplicate environment state binding '{binding.StateId}'.";
                    return false;
                }

                if (transform.IsChildOf(
                        binding.Root.transform))
                {
                    error =
                        $"Environment state root '{binding.Root.name}' contains the environment runtime and cannot be toggled safely.";
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsState(
            EnvironmentStateBinding[] bindings,
            string id)
        {
            foreach (var binding in bindings)
            {
                if (binding != null &&
                    string.Equals(
                        binding.StateId,
                        id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
