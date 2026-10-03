using VCR.Runtime.Appearance;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    public static class ApplicationUiActionPolicy
    {
        public static bool CanBrowseCharacterFile(
            bool runtimeAvailable,
            SceneRuntimeState state,
            bool adapterSupported)
        {
            return
                runtimeAvailable &&
                adapterSupported &&
                IsOperationalActionState(
                    state);
        }

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

        public static bool CanApplyAppearancePreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    presetId);
        }

        public static bool CanApplyAppearanceOutfit(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string outfitId)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    outfitId);
        }

        public static bool CanSetAppearanceAccessory(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string slotId,
            string accessoryId)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    slotId) &&
                !string.IsNullOrWhiteSpace(
                    accessoryId);
        }

        public static bool CanClearAppearanceAccessory(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string slotId)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    slotId);
        }

        public static bool CanSaveAppearanceUserPreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId,
            string characterPath)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    presetId) &&
                !string.IsNullOrWhiteSpace(
                    characterPath);
        }

        public static bool CanDeleteAppearanceUserPreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId,
            string characterPath)
        {
            return
                CanSaveAppearanceUserPreset(
                    runtimeAvailable,
                    state,
                    presetId,
                    characterPath);
        }

        public static bool CanRenameAppearanceUserPreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId,
            string newPresetId,
            string characterPath)
        {
            return
                CanSaveAppearanceUserPreset(
                    runtimeAvailable,
                    state,
                    presetId,
                    characterPath) &&
                !string.IsNullOrWhiteSpace(
                    newPresetId);
        }

        public static bool CanDuplicateAppearanceUserPreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId,
            string newPresetId,
            string characterPath)
        {
            return
                CanRenameAppearanceUserPreset(
                    runtimeAvailable,
                    state,
                    presetId,
                    newPresetId,
                    characterPath);
        }

        public static bool CanMoveAppearanceUserPreset(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string presetId,
            string characterPath)
        {
            return
                CanDeleteAppearanceUserPreset(
                    runtimeAvailable,
                    state,
                    presetId,
                    characterPath);
        }

        public static bool CanCancelAppearanceTransition(
            bool runtimeAvailable,
            AppearanceRuntimeStatus status)
        {
            return
                runtimeAvailable &&
                status.Busy &&
                status.CanCancelTransition;
        }

        public static bool CanPreviewAppearanceTransition(
            bool runtimeAvailable,
            AppearanceRuntimeState state,
            string transitionId,
            string currentOutfitId)
        {
            return
                CanMutateAppearance(
                    runtimeAvailable,
                    state) &&
                !string.IsNullOrWhiteSpace(
                    transitionId) &&
                !string.Equals(
                    transitionId,
                    "Immediate",
                    System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(
                    currentOutfitId);
        }

        public static bool CanMutateAppearance(
            bool runtimeAvailable,
            AppearanceRuntimeState state)
        {
            return
                runtimeAvailable &&
                state != AppearanceRuntimeState.Unconfigured &&
                state != AppearanceRuntimeState.Transitioning &&
                state != AppearanceRuntimeState.Committing &&
                state != AppearanceRuntimeState.Faulted;
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
