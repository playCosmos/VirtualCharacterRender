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

    public enum AppearanceTransitionTimingMode
    {
        AbsoluteTime = 0,
        Marker = 1
    }

    public enum AppearanceTransitionDependencyMode
    {
        None = 0,
        All = 1,
        Any = 2
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
    public sealed class AppearanceTransitionMarker
    {
        public string Name;
        public double TimeSeconds;
    }

    [Serializable]
    public sealed class AppearanceTransitionStep
    {
        public double TimeSeconds;
        public AppearanceTransitionTimingMode TimingMode =
            AppearanceTransitionTimingMode.AbsoluteTime;
        public string MarkerName;
        public double MarkerOffsetSeconds;
        public string StepId;
        public AppearanceTransitionDependencyMode DependencyMode =
            AppearanceTransitionDependencyMode.None;
        public string[] DependsOnStepIds =
            Array.Empty<string>();
        public double DependencyTimeoutSeconds = 5.0;
        public AppearanceTransitionStepKind Kind =
            AppearanceTransitionStepKind.Action;
        public string ActionType;
        public string TargetId;
        public string Name;
        public string Text;
        public double Value;
        public bool HasValue;
        public bool Required = true;
        public bool Blocking = false;
        public double CompletionTimeoutSeconds = 5.0;
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
        public AppearanceTransitionMarker[] Markers =
            Array.Empty<AppearanceTransitionMarker>();
        public AppearanceTransitionStep[] Steps =
            Array.Empty<AppearanceTransitionStep>();
        public AppearanceTransitionStep[] CancellationSteps =
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
            : this(
                runtimeId,
                state,
                currentPresetId,
                currentOutfitId,
                activeTransitionId,
                transitionCommitted,
                0.0,
                0.0,
                0.0,
                false,
                lastError)
        {
        }

        public AppearanceRuntimeStatus(
            string runtimeId,
            AppearanceRuntimeState state,
            string currentPresetId,
            string currentOutfitId,
            string activeTransitionId,
            bool transitionCommitted,
            double transitionElapsedSeconds,
            double transitionDurationSeconds,
            double transitionProgress01,
            bool canCancelTransition,
            string lastError)
        {
            RuntimeId = runtimeId;
            State = state;
            CurrentPresetId = currentPresetId;
            CurrentOutfitId = currentOutfitId;
            ActiveTransitionId = activeTransitionId;
            TransitionCommitted = transitionCommitted;
            TransitionElapsedSeconds =
                transitionElapsedSeconds;
            TransitionDurationSeconds =
                transitionDurationSeconds;
            TransitionProgress01 =
                transitionProgress01;
            CanCancelTransition =
                canCancelTransition;
            LastError = lastError;
        }

        public string RuntimeId { get; }
        public AppearanceRuntimeState State { get; }
        public string CurrentPresetId { get; }
        public string CurrentOutfitId { get; }
        public string ActiveTransitionId { get; }
        public bool TransitionCommitted { get; }
        public double TransitionElapsedSeconds { get; }
        public double TransitionDurationSeconds { get; }
        public double TransitionProgress01 { get; }
        public bool CanCancelTransition { get; }
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

        bool CancelTransition(
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

        bool RenameUserPreset(
            string presetId,
            string newPresetId,
            out AppearancePreset preset,
            out string error);

        bool DuplicateUserPreset(
            string presetId,
            string newPresetId,
            out AppearancePreset preset,
            out string error);

        bool MoveUserPreset(
            string presetId,
            int offset,
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

    public interface IAppearanceTransitionStepCompletionProbe
    {
        bool CanTrackCompletion(
            AppearanceTransitionStep step);

        bool TryIsComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error);
    }
}
