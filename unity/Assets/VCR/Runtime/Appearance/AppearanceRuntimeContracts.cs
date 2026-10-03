using System;
using System.Collections.Generic;

namespace VCR.Runtime.Appearance
{
    public enum AppearanceRuntimeState
    {
        Unconfigured = 0,
        Ready = 1,
        Transitioning = 2,
        Committing = 3,
        Faulted = 4
    }

    public enum AppearanceTransitionStepKind
    {
        Action = 0,
        Commit = 1
    }

    public enum AppearanceTransitionQueuePolicy
    {
        QueueLatest = 0,
        QueueAll = 1,
        IgnoreWhileBusy = 2,
        Interrupt = 3
    }

    public enum AppearanceTransitionFallbackPolicy
    {
        Immediate = 0,
        Fail = 1,
        SkipOptionalSteps = 2
    }

    [Serializable]
    public sealed class AppearanceAccessorySelection
    {
        public string SlotId;
        public string AccessoryId;

        public AppearanceAccessorySelection Clone() =>
            new()
            {
                SlotId = SlotId,
                AccessoryId = AccessoryId
            };
    }

    [Serializable]
    public sealed class AppearancePreset
    {
        public string Id;
        public string OutfitId;
        public string PreferredTransitionId;
        public AppearanceAccessorySelection[] Accessories =
            Array.Empty<AppearanceAccessorySelection>();
    }

    [Serializable]
    public sealed class AppearanceTransitionStep
    {
        public double TimeSeconds;
        public AppearanceTransitionStepKind Kind =
            AppearanceTransitionStepKind.Action;
        public string ActionType;
        public string TargetId;
        public string Name;
        public string Text;
        public double Value;
        public bool HasValue;
        public bool Required = true;
    }

    [Serializable]
    public sealed class AppearanceTransitionPreset
    {
        public string Id;
        public double DurationSeconds;
        public AppearanceTransitionQueuePolicy QueuePolicy =
            AppearanceTransitionQueuePolicy.QueueLatest;
        public AppearanceTransitionFallbackPolicy FallbackPolicy =
            AppearanceTransitionFallbackPolicy.Immediate;
        public AppearanceTransitionStep[] Steps =
            Array.Empty<AppearanceTransitionStep>();
    }

    public readonly struct AppearanceStateSnapshot
    {
        public AppearanceStateSnapshot(
            string presetId,
            string outfitId,
            AppearanceAccessorySelection[] accessories)
        {
            PresetId = presetId;
            OutfitId = outfitId;
            Accessories =
                accessories ??
                Array.Empty<AppearanceAccessorySelection>();
        }

        public string PresetId { get; }
        public string OutfitId { get; }
        public AppearanceAccessorySelection[] Accessories { get; }
    }

    public readonly struct AppearanceRuntimeStatus
    {
        public AppearanceRuntimeStatus(
            string runtimeId,
            AppearanceRuntimeState state,
            string currentPresetId,
            string currentOutfitId,
            string activeTransitionId,
            bool transitionCommitted,
            string lastError)
        {
            RuntimeId = runtimeId;
            State = state;
            CurrentPresetId = currentPresetId;
            CurrentOutfitId = currentOutfitId;
            ActiveTransitionId = activeTransitionId;
            TransitionCommitted = transitionCommitted;
            LastError = lastError;
        }

        public string RuntimeId { get; }
        public AppearanceRuntimeState State { get; }
        public string CurrentPresetId { get; }
        public string CurrentOutfitId { get; }
        public string ActiveTransitionId { get; }
        public bool TransitionCommitted { get; }
        public string LastError { get; }
        public bool Busy =>
            State == AppearanceRuntimeState.Transitioning ||
            State == AppearanceRuntimeState.Committing;
    }

    public interface IAppearanceRuntime
    {
        AppearanceRuntimeStatus Status { get; }
        AppearanceStateSnapshot Current { get; }
        IReadOnlyList<string> PresetIds { get; }
        IReadOnlyList<string> TransitionIds { get; }

        event Action<AppearanceRuntimeStatus> StatusChanged;
        event Action<AppearanceStateSnapshot> AppearanceChanged;

        bool SetPreset(
            string presetId,
            string transitionId,
            out string error);

        bool SetOutfit(
            string outfitId,
            string transitionId,
            out string error);

        bool SetAccessory(
            string slotId,
            string accessoryId,
            string transitionId,
            out string error);

        bool ClearAccessory(
            string slotId,
            string transitionId,
            out string error);

        bool RestoreDefault(
            string transitionId,
            out string error);
    }

    public interface IAppearanceUserPresetRegistry
    {
        IReadOnlyList<string> UserPresetIds { get; }

        bool SaveCurrentAsUserPreset(
            string presetId,
            string preferredTransitionId,
            out AppearancePreset preset,
            out string error);

        bool ReplaceUserPresets(
            IReadOnlyList<AppearancePreset> presets,
            out string error);

        bool RemoveUserPreset(
            string presetId,
            out string error);

        AppearancePreset[] CaptureUserPresets();
    }

    public interface IAppearanceTransitionStepExecutor
    {
        bool CanExecute(
            AppearanceTransitionStep step);

        bool TryExecute(
            AppearanceTransitionStep step,
            out string error);
    }
}
