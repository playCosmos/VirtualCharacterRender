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

        [Header("Space targets")]
        [Tooltip("Explicit components implementing IEnvironmentSpaceTarget.")]
        [SerializeField] private MonoBehaviour[] spaceTargetBehaviours =
            Array.Empty<MonoBehaviour>();

        [Header("Lighting")]
        [Tooltip("Explicit components implementing IEnvironmentLightingTarget.")]
        [SerializeField] private MonoBehaviour[] lightingTargetBehaviours =
            Array.Empty<MonoBehaviour>();

        [Header("Transitions")]
        [SerializeField] private EnvironmentTransitionMode defaultTransitionMode =
            EnvironmentTransitionMode.Cut;
        [SerializeField, Min(0f)] private float defaultTransitionDuration = 0.35f;
        [Tooltip("Optional components implementing IEnvironmentTransitionTarget. Non-Cut transitions require at least one target.")]
        [SerializeField] private MonoBehaviour[] transitionTargetBehaviours =
            Array.Empty<MonoBehaviour>();

        private readonly EnvironmentUpdateScheduler _scheduler =
            new();

        private IEnvironmentUpdateTarget[] _updateTargets =
            Array.Empty<IEnvironmentUpdateTarget>();

        private IEnvironmentSpaceTarget[] _spaceTargets =
            Array.Empty<IEnvironmentSpaceTarget>();

        private IEnvironmentTransitionTarget[] _transitionTargets =
            Array.Empty<IEnvironmentTransitionTarget>();

        private IEnvironmentLightingTarget[] _lightingTargets =
            Array.Empty<IEnvironmentLightingTarget>();

        private EnvironmentLightingProfile _lightingProfile =
            EnvironmentLightingProfile.Neutral;

        private EnvironmentUpdateDriver _updateDriver;
        private EnvironmentTransitionDriver _transitionDriver;
        private EnvironmentTransitionStatus _transitionStatus;

        private long _stateChangeCount;
        private long _stateDispatchCount;
        private long _scheduledDispatchCount;
        private long _manualDispatchCount;
        private long _updateFailureCount;
        private long _updateSequence;
        private long _transitionCount;
        private long _transitionTickCount;
        private long _transitionFailureCount;
        private long _transitionSequence;
        private long _transitionLastTickUs;
        private long _transitionDispatchStopwatchTicks;
        private long _lightingFailureCount;
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

        public EnvironmentTransitionStatus TransitionStatus =>
            _transitionStatus;

        public EnvironmentLightingProfile LightingProfile =>
            _lightingProfile;

        public long StateChangeCount =>
            _stateChangeCount;

        public long StateDispatchCount =>
            _stateDispatchCount;

        public long ScheduledDispatchCount =>
            _scheduledDispatchCount;

        public long ManualDispatchCount =>
            _manualDispatchCount;

        public long UpdateFailureCount =>
            _updateFailureCount;

        public int UpdateTargetCount =>
            _updateTargets?.Length ?? 0;

        public int SpaceTargetCount =>
            _spaceTargets?.Length ?? 0;

        public int TransitionTargetCount =>
            _transitionTargets?.Length ?? 0;

        public int LightingTargetCount =>
            _lightingTargets?.Length ?? 0;

        public bool RecurringUpdatesActive =>
            _updateDriver != null &&
            _updateDriver.enabled &&
            _scheduler.HasRecurringUpdates;

        private void Awake()
        {
            RebuildUpdateTargets();
            RebuildSpaceTargets();
            RebuildTransitionTargets();
            RebuildLightingTargets();
            ApplyCurrentStateBinding();
            ApplyCurrentLighting();
            ApplyCurrentSpaceMode();
            ConfigureScheduler(
                MonotonicClock.NowMicroseconds());
        }

        private void OnEnable()
        {
            RebuildUpdateTargets();
            RebuildSpaceTargets();
            RebuildTransitionTargets();
            RebuildLightingTargets();
            ApplyCurrentStateBinding();
            ApplyCurrentLighting();
            ApplyCurrentSpaceMode();
            ConfigureScheduler(
                MonotonicClock.NowMicroseconds());
        }

        private void OnDisable()
        {
            if (_updateDriver != null)
            {
                _updateDriver.enabled = false;
            }

            if (_transitionStatus.Active)
            {
                TryApplyStateBinding(
                    stateId,
                    out _);
                _transitionStatus = default;
            }

            if (_transitionDriver != null)
            {
                _transitionDriver.enabled = false;
            }

            ApplyLightingProfileToTargets(
                EnvironmentLightingProfile.Neutral);
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
            _lastError = null;

            RebuildUpdateTargets();
            RebuildSpaceTargets();
            RebuildTransitionTargets();
            RebuildLightingTargets();
            ApplyCurrentStateBinding();
            ApplyCurrentLighting();

            if (!SetSpaceMode(
                    mode,
                    out var spaceError))
            {
                _lastError = spaceError;
            }

            ConfigureScheduler(
                MonotonicClock.NowMicroseconds());
        }

        public bool ConfigureSpaceTargets(
            MonoBehaviour[] targets,
            out string error)
        {
            error = null;

            var nextBehaviours =
                targets == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        targets.Clone();

            var nextTargets =
                BuildSpaceTargets(
                    nextBehaviours);

            if (!ValidateSpaceTargets(
                    nextTargets,
                    spaceMode,
                    out error))
            {
                _lastError = error;
                return false;
            }

            spaceTargetBehaviours =
                nextBehaviours;
            _spaceTargets =
                nextTargets;

            ApplySpaceTargets(
                _spaceTargets,
                spaceMode);

            _lastError = null;
            return true;
        }

        public void SetSpaceTargets(
            params MonoBehaviour[] targets)
        {
            ConfigureSpaceTargets(
                targets,
                out _);
        }

        public bool SetSpaceMode(
            EnvironmentSpaceMode mode,
            out string error)
        {
            error = null;

            if (!ValidateSpaceTargets(
                    _spaceTargets,
                    mode,
                    out error))
            {
                _lastError = error;
                return false;
            }

            ApplySpaceTargets(
                _spaceTargets,
                mode);

            spaceMode = mode;
            _lastError = null;
            return true;
        }

        public bool ConfigureStateBindings(
            EnvironmentStateBinding[] bindings,
            out string error)
        {
            error = null;

            var next =
                bindings == null
                    ? Array.Empty<
                        EnvironmentStateBinding>()
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

        public void SetStateBindings(
            params EnvironmentStateBinding[] bindings)
        {
            ConfigureStateBindings(
                bindings,
                out _);
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

        public void RegisterUpdateTarget(
            IEnvironmentUpdateTarget target)
        {
            if (target == null)
            {
                return;
            }

            for (var i = 0;
                 i < _updateTargets.Length;
                 i++)
            {
                if (ReferenceEquals(
                        _updateTargets[i],
                        target))
                {
                    return;
                }
            }

            var next =
                new IEnvironmentUpdateTarget[
                    _updateTargets.Length + 1];

            Array.Copy(
                _updateTargets,
                next,
                _updateTargets.Length);

            next[next.Length - 1] =
                target;
            _updateTargets = next;
        }

        public void UnregisterUpdateTarget(
            IEnvironmentUpdateTarget target)
        {
            if (target == null ||
                _updateTargets.Length == 0)
            {
                return;
            }

            var index = -1;

            for (var i = 0;
                 i < _updateTargets.Length;
                 i++)
            {
                if (ReferenceEquals(
                        _updateTargets[i],
                        target))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            if (_updateTargets.Length == 1)
            {
                _updateTargets =
                    Array.Empty<
                        IEnvironmentUpdateTarget>();
                return;
            }

            var next =
                new IEnvironmentUpdateTarget[
                    _updateTargets.Length - 1];

            if (index > 0)
            {
                Array.Copy(
                    _updateTargets,
                    0,
                    next,
                    0,
                    index);
            }

            if (index < next.Length)
            {
                Array.Copy(
                    _updateTargets,
                    index + 1,
                    next,
                    index,
                    next.Length - index);
            }

            _updateTargets = next;
        }

        public bool SetState(
            string nextStateId,
            out string error)
        {
            return SetState(
                nextStateId,
                new EnvironmentTransitionSpec(
                    defaultTransitionMode,
                    defaultTransitionDuration),
                out error);
        }

        public bool SetState(
            string nextStateId,
            EnvironmentTransitionSpec transition,
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

            if (!ValidateStateBindingTarget(
                    nextStateId,
                    out error))
            {
                _lastError = error;
                return false;
            }

            var nowUs =
                MonotonicClock.NowMicroseconds();

            if (_transitionStatus.Active)
            {
                CompleteTransition(
                    nowUs);
            }

            var previous =
                stateId;

            if (transition.IsImmediate)
            {
                if (!TryApplyStateBinding(
                        nextStateId,
                        out error))
                {
                    _lastError = error;
                    return false;
                }

                CommitStateChange(
                    previous,
                    nextStateId,
                    nowUs);

                ApplyImmediateTransitionTargets(
                    previous,
                    nextStateId,
                    nowUs);

                return true;
            }

            if (!ValidateTransitionTargets(
                    transition,
                    previous,
                    nextStateId,
                    out error))
            {
                _lastError = error;
                return false;
            }

            if (!TryPrepareTransitionBindings(
                    previous,
                    nextStateId,
                    out error))
            {
                _lastError = error;
                return false;
            }

            stateId =
                nextStateId;
            _lastError = null;
            _stateChangeCount++;
            _transitionCount++;
            _transitionLastTickUs =
                nowUs;

            _transitionStatus =
                new EnvironmentTransitionStatus(
                    true,
                    transition.Mode,
                    previous,
                    nextStateId,
                    nowUs,
                    transition.DurationSeconds,
                    0f);

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    stateId));

            DispatchStateChanged(
                nowUs);
            ApplyTransitionTargets(
                CreateTransitionContext(
                    nowUs,
                    progress: 0f,
                    deltaSeconds: 0f));

            EnsureTransitionDriver();
            return true;
        }

        public bool ConfigureLightingTargets(
            MonoBehaviour[] targets,
            out string error)
        {
            error = null;

            var nextBehaviours =
                targets == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        targets.Clone();

            var nextTargets =
                BuildLightingTargets(
                    nextBehaviours);

            if (!ValidateLightingTargets(
                    nextTargets,
                    _lightingProfile,
                    out error))
            {
                _lastError = error;
                return false;
            }

            lightingTargetBehaviours =
                nextBehaviours;
            _lightingTargets =
                nextTargets;

            ApplyLightingProfileToTargets(
                _lightingProfile);

            _lastError = null;
            return true;
        }

        public void SetLightingTargets(
            params MonoBehaviour[] targets)
        {
            ConfigureLightingTargets(
                targets,
                out _);
        }

        public bool SetLightingProfile(
            EnvironmentLightingProfile profile,
            out string error)
        {
            error = null;

            if (!ValidateLightingTargets(
                    _lightingTargets,
                    profile,
                    out error))
            {
                _lastError = error;
                return false;
            }

            _lightingProfile =
                profile;

            ApplyLightingProfileToTargets(
                _lightingProfile);

            _lastError = null;
            return true;
        }

        public bool ConfigureTransitionTargets(
            MonoBehaviour[] targets,
            out string error)
        {
            error = null;

            var nextBehaviours =
                targets == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        targets.Clone();

            var nextTargets =
                BuildTransitionTargets(
                    nextBehaviours);

            transitionTargetBehaviours =
                nextBehaviours;
            _transitionTargets =
                nextTargets;
            _lastError = null;
            return true;
        }

        public void SetTransitionTargets(
            params MonoBehaviour[] targets)
        {
            ConfigureTransitionTargets(
                targets,
                out _);
        }

        public void ConfigureDefaultTransition(
            EnvironmentTransitionMode mode,
            float durationSeconds)
        {
            defaultTransitionMode = mode;
            defaultTransitionDuration =
                Math.Max(
                    0f,
                    durationSeconds);
        }

        public bool TickTransition(
            long nowUs)
        {
            if (!isActiveAndEnabled ||
                !_transitionStatus.Active)
            {
                return false;
            }

            var durationUs =
                Math.Max(
                    1L,
                    (long)(
                        _transitionStatus
                            .DurationSeconds *
                        1_000_000.0));

            var progress =
                (float)Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        (nowUs -
                         _transitionStatus
                             .StartedAtTimestampUs) /
                        (double)durationUs));

            var deltaSeconds =
                _transitionLastTickUs <= 0
                    ? 0f
                    : (float)Math.Max(
                        0.0,
                        (nowUs -
                         _transitionLastTickUs) /
                        1_000_000.0);

            _transitionLastTickUs =
                nowUs;

            _transitionStatus =
                new EnvironmentTransitionStatus(
                    progress < 1f,
                    _transitionStatus.Mode,
                    _transitionStatus
                        .PreviousStateId,
                    _transitionStatus.StateId,
                    _transitionStatus
                        .StartedAtTimestampUs,
                    _transitionStatus
                        .DurationSeconds,
                    progress);

            ApplyTransitionTargets(
                CreateTransitionContext(
                    nowUs,
                    progress,
                    deltaSeconds));

            _transitionTickCount++;

            if (progress >= 1f)
            {
                CompleteTransitionRoots();
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
                "environment.space_targets",
                SpaceTargetCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.space_mode",
                (int)spaceMode,
                "enum"));

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
                "environment.update_failures",
                _updateFailureCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.recurring_updates_active",
                RecurringUpdatesActive ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "environment.lighting_targets",
                LightingTargetCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.lighting_weight",
                _lightingProfile.Weight,
                "ratio"));

            output.Add(new RuntimeMetric(
                "environment.lighting_intensity_multiplier",
                _lightingProfile.IntensityMultiplier,
                "ratio"));

            output.Add(new RuntimeMetric(
                "environment.lighting_failures",
                _lightingFailureCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.transition_targets",
                TransitionTargetCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.transition_default_mode",
                (int)defaultTransitionMode,
                "enum"));

            output.Add(new RuntimeMetric(
                "environment.transition_active",
                _transitionStatus.Active ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "environment.transition_progress",
                _transitionStatus.Progress,
                "ratio"));

            output.Add(new RuntimeMetric(
                "environment.transition_count",
                _transitionCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.transition_ticks",
                _transitionTickCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.transition_failures",
                _transitionFailureCount,
                "count"));

            output.Add(new RuntimeMetric(
                "environment.transition_dispatch_ms",
                _transitionDispatchStopwatchTicks *
                    1000.0 /
                    System.Diagnostics.Stopwatch.Frequency,
                "ms"));
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

        private void RebuildLightingTargets()
        {
            _lightingTargets =
                BuildLightingTargets(
                    lightingTargetBehaviours);
        }

        private static IEnvironmentLightingTarget[]
            BuildLightingTargets(
                MonoBehaviour[] behaviours)
        {
            if (behaviours == null ||
                behaviours.Length == 0)
            {
                return Array.Empty<
                    IEnvironmentLightingTarget>();
            }

            var targets =
                new IEnvironmentLightingTarget[
                    behaviours.Length];
            var count = 0;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null ||
                    behaviour is not
                        IEnvironmentLightingTarget target)
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
                return Array.Empty<
                    IEnvironmentLightingTarget>();
            }

            if (count != targets.Length)
            {
                Array.Resize(
                    ref targets,
                    count);
            }

            return targets;
        }

        private static bool ValidateLightingTargets(
            IEnvironmentLightingTarget[] targets,
            EnvironmentLightingProfile profile,
            out string error)
        {
            error = null;

            if (targets == null)
            {
                return true;
            }

            foreach (var target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                if (!target.ValidateEnvironmentLighting(
                        profile,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyCurrentLighting()
        {
            ApplyLightingProfileToTargets(
                _lightingProfile);
        }

        private void ApplyLightingProfileToTargets(
            EnvironmentLightingProfile profile)
        {
            if (_lightingTargets == null ||
                _lightingTargets.Length == 0)
            {
                return;
            }

            foreach (var target in
                     _lightingTargets)
            {
                if (target == null)
                {
                    continue;
                }

                try
                {
                    target.ApplyEnvironmentLighting(
                        profile);
                }
                catch (Exception exception)
                {
                    _lightingFailureCount++;
                    _lastError =
                        "Environment lighting target failed: " +
                        exception.Message;

                    Debug.LogWarning(
                        _lastError,
                        this);
                }
            }
        }

        private void EnsureTransitionDriver()
        {
            _transitionDriver ??=
                GetComponent<
                    EnvironmentTransitionDriver>();

            if (_transitionDriver == null)
            {
                _transitionDriver =
                    gameObject.AddComponent<
                        EnvironmentTransitionDriver>();
            }

            _transitionDriver.Bind(this);
            _transitionDriver.enabled =
                isActiveAndEnabled &&
                _transitionStatus.Active;
        }

        private void RebuildTransitionTargets()
        {
            _transitionTargets =
                BuildTransitionTargets(
                    transitionTargetBehaviours);
        }

        private static IEnvironmentTransitionTarget[]
            BuildTransitionTargets(
                MonoBehaviour[] behaviours)
        {
            if (behaviours == null ||
                behaviours.Length == 0)
            {
                return Array.Empty<
                    IEnvironmentTransitionTarget>();
            }

            var targets =
                new IEnvironmentTransitionTarget[
                    behaviours.Length];
            var count = 0;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null ||
                    behaviour is not
                        IEnvironmentTransitionTarget target)
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
                return Array.Empty<
                    IEnvironmentTransitionTarget>();
            }

            if (count != targets.Length)
            {
                Array.Resize(
                    ref targets,
                    count);
            }

            return targets;
        }

        private bool ValidateTransitionTargets(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error)
        {
            error = null;

            if (transition.IsImmediate)
            {
                return true;
            }

            if (_transitionTargets == null ||
                _transitionTargets.Length == 0)
            {
                error =
                    $"Environment transition '{transition.Mode}' requires at least one transition target.";
                return false;
            }

            foreach (var target in
                     _transitionTargets)
            {
                if (target == null)
                {
                    continue;
                }

                if (!target
                    .ValidateEnvironmentTransition(
                        transition,
                        previousStateId,
                        nextStateId,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyImmediateTransitionTargets(
            string previousStateId,
            string nextStateId,
            long timestampUs)
        {
            if (_transitionTargets == null ||
                _transitionTargets.Length == 0)
            {
                return;
            }

            ApplyTransitionTargets(
                new EnvironmentTransitionContext(
                    ++_transitionSequence,
                    timestampUs,
                    previousStateId,
                    nextStateId,
                    EnvironmentTransitionMode.Cut,
                    progress: 1f,
                    deltaSeconds: 0f));
        }

        private void ApplyTransitionTargets(
            EnvironmentTransitionContext context)
        {
            if (_transitionTargets == null ||
                _transitionTargets.Length == 0)
            {
                return;
            }

            var started =
                System.Diagnostics.Stopwatch
                    .GetTimestamp();

            foreach (var target in
                     _transitionTargets)
            {
                if (target == null)
                {
                    continue;
                }

                try
                {
                    target.ApplyEnvironmentTransition(
                        context);
                }
                catch (Exception exception)
                {
                    _transitionFailureCount++;
                    _lastError =
                        "Environment transition target failed: " +
                        exception.Message;

                    Debug.LogWarning(
                        _lastError,
                        this);
                }
            }

            _transitionDispatchStopwatchTicks +=
                System.Diagnostics.Stopwatch
                    .GetTimestamp() -
                started;
        }

        private EnvironmentTransitionContext
            CreateTransitionContext(
                long timestampUs,
                float progress,
                float deltaSeconds)
        {
            return new EnvironmentTransitionContext(
                ++_transitionSequence,
                timestampUs,
                _transitionStatus
                    .PreviousStateId,
                _transitionStatus.StateId,
                _transitionStatus.Mode,
                progress,
                Math.Max(
                    0f,
                    deltaSeconds));
        }

        private void CompleteTransition(
            long nowUs)
        {
            if (!_transitionStatus.Active)
            {
                return;
            }

            _transitionStatus =
                new EnvironmentTransitionStatus(
                    false,
                    _transitionStatus.Mode,
                    _transitionStatus
                        .PreviousStateId,
                    _transitionStatus.StateId,
                    _transitionStatus
                        .StartedAtTimestampUs,
                    _transitionStatus
                        .DurationSeconds,
                    1f);

            ApplyTransitionTargets(
                CreateTransitionContext(
                    nowUs,
                    progress: 1f,
                    deltaSeconds: 0f));

            CompleteTransitionRoots();
        }

        private void CompleteTransitionRoots()
        {
            if (!TryApplyStateBinding(
                    stateId,
                    out var error))
            {
                _lastError = error;
            }

            if (_transitionDriver != null)
            {
                _transitionDriver.enabled = false;
            }
        }

        private bool TryPrepareTransitionBindings(
            string previousStateId,
            string nextStateId,
            out string error)
        {
            error = null;

            if (stateBindings == null ||
                stateBindings.Length == 0)
            {
                return true;
            }

            if (!ValidateStateBindings(
                    stateBindings,
                    out error))
            {
                return false;
            }

            if (!ContainsState(
                    stateBindings,
                    nextStateId))
            {
                error =
                    $"Environment state '{nextStateId}' has no binding.";
                return false;
            }

            foreach (var binding in
                     stateBindings)
            {
                var active =
                    string.Equals(
                        binding.StateId,
                        previousStateId,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        binding.StateId,
                        nextStateId,
                        StringComparison.Ordinal);

                if (binding.Root.activeSelf !=
                    active)
                {
                    binding.Root.SetActive(
                        active);
                }
            }

            return true;
        }

        private bool ValidateStateBindingTarget(
            string nextStateId,
            out string error)
        {
            error = null;

            if (stateBindings == null ||
                stateBindings.Length == 0)
            {
                return true;
            }

            if (!ValidateStateBindings(
                    stateBindings,
                    out error))
            {
                return false;
            }

            if (!ContainsState(
                    stateBindings,
                    nextStateId))
            {
                error =
                    $"Environment state '{nextStateId}' has no binding.";
                return false;
            }

            return true;
        }

        private void CommitStateChange(
            string previousStateId,
            string nextStateId,
            long nowUs)
        {
            stateId =
                nextStateId;
            _lastError = null;
            _stateChangeCount++;

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previousStateId,
                    stateId));

            DispatchStateChanged(
                nowUs);
        }

        private void DispatchStateChanged(
            long nowUs)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            DispatchUpdate(
                EnvironmentUpdateReason.StateChanged,
                nowUs,
                deltaSeconds: 0f);
            _stateDispatchCount++;
        }

        private void ApplyCurrentSpaceMode()
        {
            if (!SetSpaceMode(
                    spaceMode,
                    out var error))
            {
                _lastError = error;
            }
        }

        private void RebuildSpaceTargets()
        {
            _spaceTargets =
                BuildSpaceTargets(
                    spaceTargetBehaviours);
        }

        private static IEnvironmentSpaceTarget[]
            BuildSpaceTargets(
                MonoBehaviour[] behaviours)
        {
            if (behaviours == null ||
                behaviours.Length == 0)
            {
                return Array.Empty<
                    IEnvironmentSpaceTarget>();
            }

            var targets =
                new IEnvironmentSpaceTarget[
                    behaviours.Length];
            var count = 0;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null ||
                    behaviour is not
                        IEnvironmentSpaceTarget target)
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
                return Array.Empty<
                    IEnvironmentSpaceTarget>();
            }

            if (count != targets.Length)
            {
                Array.Resize(
                    ref targets,
                    count);
            }

            return targets;
        }

        private static bool ValidateSpaceTargets(
            IEnvironmentSpaceTarget[] targets,
            EnvironmentSpaceMode mode,
            out string error)
        {
            error = null;

            if (targets == null)
            {
                return true;
            }

            foreach (var target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                if (!target.ValidateEnvironmentSpace(
                        mode,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplySpaceTargets(
            IEnvironmentSpaceTarget[] targets,
            EnvironmentSpaceMode mode)
        {
            if (targets == null)
            {
                return;
            }

            foreach (var target in targets)
            {
                target?.ApplyEnvironmentSpace(
                    mode);
            }
        }

        private void ApplyCurrentStateBinding()
        {
            if (!TryApplyStateBinding(
                    stateId,
                    out var error))
            {
                _lastError = error;
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

            if (!ValidateStateBindings(
                    stateBindings,
                    out error))
            {
                return false;
            }

            if (!ContainsState(
                    stateBindings,
                    nextStateId))
            {
                error =
                    $"Environment state '{nextStateId}' has no binding.";
                return false;
            }

            foreach (var binding in
                     stateBindings)
            {
                var active =
                    string.Equals(
                        binding.StateId,
                        nextStateId,
                        StringComparison.Ordinal);

                if (binding.Root.activeSelf !=
                    active)
                {
                    binding.Root.SetActive(
                        active);
                }
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
    }
}
