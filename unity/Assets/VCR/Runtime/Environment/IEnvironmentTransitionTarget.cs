namespace VCR.Runtime.Environment
{
    public interface IEnvironmentTransitionTarget
    {
        bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error);

        void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context);
    }
}
