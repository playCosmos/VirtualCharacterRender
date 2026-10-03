namespace VCR.Runtime.Environment
{
    public interface IEnvironmentTransitionTarget
    {
        bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            out string error);

        void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context);
    }
}
