using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    public static class ApplicationUiActionPolicy
    {
        public static bool CanLoadCharacter(
            bool runtimeAvailable,
            SceneRuntimeState state,
            string path)
        {
            return
                runtimeAvailable &&
                IsOperationalActionState(state) &&
                !string.IsNullOrWhiteSpace(path);
        }

        public static bool CanReloadCharacter(
            bool runtimeAvailable,
            SceneRuntimeState state,
            bool hasCharacter,
            string currentPath)
        {
            return
                runtimeAvailable &&
                IsOperationalActionState(state) &&
                hasCharacter &&
                !string.IsNullOrWhiteSpace(
                    currentPath);
        }

        public static bool CanUnloadCharacter(
            bool runtimeAvailable,
            SceneRuntimeState state,
            bool hasCharacter)
        {
            return
                runtimeAvailable &&
                IsOperationalActionState(state) &&
                hasCharacter;
        }

        public static bool CanApplyBroadcastTarget(
            bool runtimeAvailable,
            SceneRuntimeState state)
        {
            return
                runtimeAvailable &&
                IsOperationalActionState(state);
        }

        public static bool IsOperationalActionState(
            SceneRuntimeState state)
        {
            return state !=
                    SceneRuntimeState.LoadingCharacter &&
                state !=
                    SceneRuntimeState.Suspended &&
                state !=
                    SceneRuntimeState.ShuttingDown &&
                state !=
                    SceneRuntimeState.Stopped;
        }
    }
}
