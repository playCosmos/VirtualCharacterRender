namespace VCR.Runtime.Environment
{
    /// <summary>
    /// Two-phase environment-space target.
    ///
    /// Validation must not mutate scene state. The runtime validates all
    /// targets first, then applies the mode to all targets so a bad anchor
    /// cannot partially move an environment.
    /// </summary>
    public interface IEnvironmentSpaceTarget
    {
        bool ValidateEnvironmentSpace(
            EnvironmentSpaceMode mode,
            out string error);

        void ApplyEnvironmentSpace(
            EnvironmentSpaceMode mode);
    }
}
