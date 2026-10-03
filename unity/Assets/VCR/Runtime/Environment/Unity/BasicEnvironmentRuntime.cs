using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// One-environment runtime controller.
    ///
    /// State changes are event-driven. Recurring work is delegated to a small
    /// driver that is enabled only for Hz10, Hz30, or EveryFrame policies, so
    /// Static/EventDriven environments have no recurring Update callback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasicEnvironmentRuntime :
        MonoBehaviour,
        IEnvironmentRuntime,
        IRuntimeMetricsSource
    {
        [SerializeField] private string environmentId =
            "environment.basic";

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
        [Tooltip("Explicit components implementing IEnvironmentUpdateTarget. Static/EventDriven environments do not poll these every frame.")]
        [SerializeField] private MonoBehaviour[] updateTargetBehaviours =
            Array.Empty<MonoBehaviour>();

        private readonly EnvironmentUpdateScheduler _scheduler =
            new();

        private IEnvironmentUpdateTarget[] _updateTargets =
            Array.Empty<IEnvironmentUpdateTarget>();

        private EnvironmentUpdateDriver _updateDriver;

        private long _stateChangeCount;
        private long _stateDispatchCount;
        private long _scheduledDispatchCount;
        private long _manualDispatchCount;
        private long _updateSequence;
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

        public long StateDispatchCount =>
            _stateDispatchCount;

        public long ScheduledDispatchCount =>
            _scheduledDispatchCount;

        public long ManualDispatchCount =>
            _manualDispatchCount;

        public int UpdateTargetCount =>
            _updateTargets?.Length ?? 0;

        public bool RecurringUpdatesActive =>
            _updateDriver != null &&
            _updateDriver.enabled &&
            _scheduler.HasRecurringUpdates;

        private void Awake()
        {
            RebuildUpdateTargets();
            ApplyStateBindings();
            ConfigureScheduler(
                MonotonicClock.NowMicroseconds());
        }

        private void OnEnable()
        {
            RebuildUpdateTargets();
            ApplyStateBindings();
            ConfigureScheduler(
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

            RebuildUpdateTargets();
            ApplyStateBindings();
            ConfigureScheduler(
                MonotonicClock.NowMicroseconds());
        }

        public void SetStateBindings(
            params EnvironmentStateBinding[] bindings)
        {
            stateBindings =
                bindings == null
                    ? Array.Empty<
                        EnvironmentStateBinding>()
                    : (EnvironmentStateBinding[])
                        bindings.Clone();

            ApplyStateBindings();
        }

        public void SetUpdateTargets(
            params MonoBehaviour[] targets)
        {
            updateTargetBehaviours =
                targets == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        targets.Clone();

            RebuildUpdateTargets();
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

            var previous = stateId;
            stateId = nextStateId;
            _lastError = null;
            _stateChangeCount++;

            ApplyStateBindings();

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    stateId));

            if (isActiveAndEnabled)
            {
                DispatchUpdate(
                    EnvironmentUpdateReason.StateChanged,
                    MonotonicClock.NowMicroseconds(),
                    deltaSeconds: 0f);
                _stateDispatchCount++;
            }

            return true;
        }

        public bool RequestManualUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            DispatchUpdate(
                EnvironmentUpdateReason.Manual,
                MonotonicClock.NowMicroseconds(),
                deltaSeconds: 0f);
            _manualDispatchCount++;
            return true;
        }

        /// <summary>
        /// Called by EnvironmentUpdateDriver. Returns true only when a recurring
        /// update was actually due and dispatched.
        /// </summary>
        public bool TickScheduled(long nowUs)
        {
            if (!isActiveAndEnabled ||
                !_scheduler.IsDue(nowUs))
            {
                return false;
            }

            var deltaSeconds =
                _scheduler.MarkDispatched(
                    nowUs);

            DispatchUpdate(
                EnvironmentUpdateReason.Scheduled,
                nowUs,
                deltaSeconds);

            _scheduledDispatchCount++;
            return true;
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
                UpdateTargetCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.state_dispatches",
                _stateDispatchCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.scheduled_dispatches",
                _scheduledDispatchCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.manual_dispatches",
                _manualDispatchCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.recurring_updates_active",
                RecurringUpdatesActive ? 1 : 0,
                "bool"));
        }

        private void ConfigureScheduler(
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

            _updateDriver ??=
                GetComponent<
                    EnvironmentUpdateDriver>();

            if (_updateDriver == null)
            {
                _updateDriver =
                    gameObject.AddComponent<
                        EnvironmentUpdateDriver>();
            }

            _updateDriver.Bind(this);
            _updateDriver.enabled =
                isActiveAndEnabled;
        }

        private void ApplyStateBindings()
        {
            if (stateBindings == null)
            {
                return;
            }

            foreach (var binding in stateBindings)
            {
                if (binding?.Root == null ||
                    ReferenceEquals(
                        binding.Root,
                        gameObject))
                {
                    continue;
                }

                var active =
                    string.Equals(
                        binding.StateId,
                        stateId,
                        StringComparison.Ordinal);

                if (binding.Root.activeSelf !=
                    active)
                {
                    binding.Root.SetActive(
                        active);
                }
            }
        }

        private void RebuildUpdateTargets()
        {
            if (updateTargetBehaviours == null ||
                updateTargetBehaviours.Length == 0)
            {
                _updateTargets =
                    Array.Empty<
                        IEnvironmentUpdateTarget>();
                return;
            }

            var targets =
                new IEnvironmentUpdateTarget[
                    updateTargetBehaviours.Length];
            var count = 0;

            foreach (var behaviour in
                     updateTargetBehaviours)
            {
                if (behaviour == null ||
                    behaviour is not
                        IEnvironmentUpdateTarget target ||
                    ReferenceEquals(
                        behaviour,
                        this))
                {
                    continue;
                }

                var duplicate = false;

                for (var i = 0;
                     i < count;
                     i++)
                {
                    if (ReferenceEquals(
                            targets[i],
                            target))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                targets[count++] =
                    target;
            }

            if (count == 0)
            {
                _updateTargets =
                    Array.Empty<
                        IEnvironmentUpdateTarget>();
                return;
            }

            if (count != targets.Length)
            {
                Array.Resize(
                    ref targets,
                    count);
            }

            _updateTargets = targets;
        }

        private void DispatchUpdate(
            EnvironmentUpdateReason reason,
            long timestampUs,
            float deltaSeconds)
        {
            if (_updateTargets == null ||
                _updateTargets.Length == 0)
            {
                return;
            }

            var context =
                new EnvironmentUpdateContext(
                    ++_updateSequence,
                    timestampUs,
                    Math.Max(
                        0f,
                        deltaSeconds),
                    reason,
                    stateId);

            foreach (var target in
                     _updateTargets)
            {
                target?.UpdateEnvironment(
                    context);
            }
        }
    }
}
