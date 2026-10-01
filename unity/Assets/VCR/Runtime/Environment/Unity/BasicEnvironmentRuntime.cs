using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Minimal first-class environment runtime for P0.
    ///
    /// Static/EventDriven environments have no Update method here and therefore
    /// no recurring per-frame controller cost. Higher-rate environment systems
    /// are added as explicit capabilities later.
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

        private long _stateChangeCount;
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

            StateChanged?.Invoke(
                new EnvironmentStateChange(
                    previous,
                    stateId));

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
        }
    }
}
