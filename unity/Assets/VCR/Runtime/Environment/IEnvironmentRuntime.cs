using System;

namespace VCR.Runtime.Environment
{
    public interface IEnvironmentRuntime
    {
        EnvironmentRuntimeStatus Status { get; }
        EnvironmentSpaceMode SpaceMode { get; }
        EnvironmentTransitionStatus TransitionStatus { get; }

        event Action<EnvironmentStateChange> StateChanged;

        bool SetState(
            string stateId,
            out string error);

        bool SetState(
            string stateId,
            EnvironmentTransitionSpec transition,
            out string error);
    }
}
