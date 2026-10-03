using System;

namespace VCR.Runtime.Environment
{
    public interface IEnvironmentRuntime
    {
        EnvironmentRuntimeStatus Status { get; }
        EnvironmentSpaceMode SpaceMode { get; }

        event Action<EnvironmentStateChange> StateChanged;

        bool SetState(
            string stateId,
            out string error);
    }
}
