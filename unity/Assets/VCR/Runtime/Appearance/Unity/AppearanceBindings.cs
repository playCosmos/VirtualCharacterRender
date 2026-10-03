using System;
using UnityEngine;

namespace VCR.Runtime.Appearance.Unity
{
    [Serializable]
    public sealed class AppearanceOutfitBinding
    {
        public string OutfitId;
        public GameObject[] Roots =
            Array.Empty<GameObject>();
    }

    [Serializable]
    public sealed class AppearanceAccessoryBinding
    {
        public string SlotId;
        public string AccessoryId;
        public GameObject Root;
    }

    [Serializable]
    public sealed class AppearanceAccessorySelectionBinding
    {
        public string SlotId;
        public string AccessoryId;

        public AppearanceAccessorySelection ToSelection() =>
            new()
            {
                SlotId = SlotId,
                AccessoryId = AccessoryId
            };
    }

    [Serializable]
    public sealed class AppearancePresetBinding
    {
        public string PresetId;
        public string OutfitId;
        public string PreferredTransitionId;
        public AppearanceAccessorySelectionBinding[] Accessories =
            Array.Empty<AppearanceAccessorySelectionBinding>();

        public AppearancePreset ToPreset()
        {
            var selections =
                new AppearanceAccessorySelection[
                    Accessories?.Length ?? 0];

            for (var i = 0; i < selections.Length; i++)
            {
                selections[i] =
                    Accessories[i]?.ToSelection() ??
                    new AppearanceAccessorySelection();
            }

            return new AppearancePreset
            {
                Id = PresetId,
                OutfitId = OutfitId,
                PreferredTransitionId =
                    PreferredTransitionId,
                Accessories = selections
            };
        }
    }

    [Serializable]
    public sealed class AppearanceTransitionMarkerBinding
    {
        public string Name;
        [Min(0f)] public float TimeSeconds;

        public AppearanceTransitionMarker ToMarker() =>
            new()
            {
                Name = Name,
                TimeSeconds = TimeSeconds
            };
    }

    [Serializable]
    public sealed class AppearanceTransitionStepBinding
    {
        [Min(0f)] public float TimeSeconds;
        public AppearanceTransitionTimingMode TimingMode =
            AppearanceTransitionTimingMode.AbsoluteTime;
        public string MarkerName;
        public float MarkerOffsetSeconds;
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
        [Min(0.01f)] public float CompletionTimeoutSeconds = 5f;

        public AppearanceTransitionStep ToStep() =>
            new()
            {
                TimeSeconds = TimeSeconds,
                TimingMode = TimingMode,
                MarkerName = MarkerName,
                MarkerOffsetSeconds = MarkerOffsetSeconds,
                Kind = Kind,
                ActionType = ActionType,
                TargetId = TargetId,
                Name = Name,
                Text = Text,
                Value = Value,
                HasValue = HasValue,
                Required = Required,
                Blocking = Blocking,
                CompletionTimeoutSeconds =
                    CompletionTimeoutSeconds
            };
    }

    [Serializable]
    public sealed class AppearanceTransitionBinding
    {
        public string TransitionId;
        [Min(0f)] public float DurationSeconds;
        public AppearanceTransitionQueuePolicy QueuePolicy =
            AppearanceTransitionQueuePolicy.QueueLatest;
        public AppearanceTransitionFallbackPolicy FallbackPolicy =
            AppearanceTransitionFallbackPolicy.Immediate;
        public AppearanceTransitionMarkerBinding[] Markers =
            Array.Empty<AppearanceTransitionMarkerBinding>();
        public AppearanceTransitionStepBinding[] Steps =
            Array.Empty<AppearanceTransitionStepBinding>();
        public AppearanceTransitionStepBinding[] CancellationSteps =
            Array.Empty<AppearanceTransitionStepBinding>();

        public AppearanceTransitionPreset ToPreset()
        {
            var markers =
                ConvertMarkers(
                    Markers);
            var steps =
                ConvertSteps(
                    Steps);
            var cancellationSteps =
                ConvertSteps(
                    CancellationSteps);

            return new AppearanceTransitionPreset
            {
                Id = TransitionId,
                DurationSeconds = DurationSeconds,
                QueuePolicy = QueuePolicy,
                FallbackPolicy = FallbackPolicy,
                Markers = markers,
                Steps = steps,
                CancellationSteps =
                    cancellationSteps
            };
        }

        private static AppearanceTransitionMarker[]
            ConvertMarkers(
                AppearanceTransitionMarkerBinding[] source)
        {
            var markers =
                new AppearanceTransitionMarker[
                    source?.Length ?? 0];

            for (var i = 0; i < markers.Length; i++)
            {
                markers[i] =
                    source[i]?.ToMarker() ??
                    new AppearanceTransitionMarker();
            }

            return markers;
        }

        private static AppearanceTransitionStep[]
            ConvertSteps(
                AppearanceTransitionStepBinding[] source)
        {
            var steps =
                new AppearanceTransitionStep[
                    source?.Length ?? 0];

            for (var i = 0; i < steps.Length; i++)
            {
                steps[i] =
                    source[i]?.ToStep() ??
                    new AppearanceTransitionStep();
            }

            return steps;
        }
    }
}
