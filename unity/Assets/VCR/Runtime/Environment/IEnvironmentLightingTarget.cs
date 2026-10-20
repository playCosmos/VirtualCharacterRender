namespace VCR.Runtime.Environment
{
    public interface IEnvironmentLightingTarget
    {
        bool ValidateEnvironmentLighting(
            EnvironmentLightingProfile profile,
            out string error);

        void ApplyEnvironmentLighting(
            EnvironmentLightingProfile profile);
    }
}
