namespace VCR.Runtime.Environment
{
    public interface IEnvironmentUpdateTarget
    {
        void UpdateEnvironment(
            EnvironmentUpdateContext context);
    }
}
