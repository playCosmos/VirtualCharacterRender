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
    public sealed class AppearanceTransitionStepBinding
    {
        [Min(0f)] public float TimeSeconds;
        public AppearanceTransitionStepKind Kind =
            AppearanceTransitionStepKind.Action;
        public string ActionType;
        public string TargetId;
        public string Name;
        public string Text;
        public double Value;
        public bool HasValue;
        public bool Required = true;

        public AppearanceTransitionStep ToStep() =>
            new()
            {
                TimeSeconds = TimeSeconds,
                Kind = Kind,
                ActionType = ActionType,
                TargetId = TargetId,
                Name = Name,
                Text = Text,
                Value = Value,
                HasValue = HasValue,
                Required = Required
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
        public AppearanceTransitionStepBinding[] Steps =
            Array.Empty<AppearanceTransitionStepBinding>();

        public AppearanceTransitionPreset ToPreset()
        {
            var steps =
                new AppearanceTransitionStep[
                    Steps?.Length ?? 0];

            for (var i = 0; i < steps.Length; i++)
            {
                steps[i] =
                    Steps[i]?.ToStep() ??
                    new AppearanceTransitionStep();
            }

            return new AppearanceTransitionPreset
            {
                Id = TransitionId,
                DurationSeconds = DurationSeconds,
                QueuePolicy = QueuePolicy,
                FallbackPolicy = FallbackPolicy,
                Steps = steps
            };
        }
    }
}
