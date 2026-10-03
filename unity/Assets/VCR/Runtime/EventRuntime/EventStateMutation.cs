using System;
using VCR.Runtime.Events;

namespace VCR.Runtime.EventRuntime
{
    public enum EventStateMutationKind
    {
        SetNumber = 0,
        AddNumber = 1,
        SetText = 2,
        Remove = 3
    }

    public enum EventNumericValueSource
    {
        Constant = 0,
        EventAmount = 1
    }

    public enum EventTextValueSource
    {
        Constant = 0,
        EventText = 1,
        EventActorId = 2,
        EventActorName = 3,
        EventType = 4,
        EventSourceId = 5
    }

    [Serializable]
    public sealed class EventStateMutation
    {
        public EventStateMutationKind Kind;
        public string Key;
        public EventNumericValueSource NumericSource;
        public double ConstantNumber;
        public double NumericScale = 1.0;
        public double NumericOffset;
        public EventTextValueSource TextSource;
        public string ConstantText;
        public EventTextTransformFlags TextTransforms;
        public string TextPrefix;
        public string TextSuffix;

        public void Apply(
            NormalizedEvent value,
            EventRuntimeStateStore state)
        {
            if (state == null ||
                string.IsNullOrWhiteSpace(Key))
            {
                return;
            }

            switch (Kind)
            {
                case EventStateMutationKind.SetNumber:
                    state.SetNumber(Key, ResolveNumber(value));
                    break;

                case EventStateMutationKind.AddNumber:
                    state.AddNumber(Key, ResolveNumber(value));
                    break;

                case EventStateMutationKind.SetText:
                    state.SetText(Key, ResolveText(value));
                    break;

                case EventStateMutationKind.Remove:
                    state.Remove(Key);
                    break;
            }
        }

        private double ResolveNumber(
            NormalizedEvent value)
        {
            var raw =
                NumericSource ==
                    EventNumericValueSource.EventAmount &&
                value.HasAmount
                    ? value.Amount
                    : ConstantNumber;

            var scale =
                IsFinite(NumericScale)
                    ? NumericScale
                    : 1.0;

            var offset =
                IsFinite(NumericOffset)
                    ? NumericOffset
                    : 0.0;

            var result =
                raw * scale + offset;

            return
                IsFinite(result)
                    ? result
                    : 0.0;
        }

        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value) &&
                !double.IsInfinity(value);
        }

        private string ResolveText(
            NormalizedEvent value)
        {
            var raw =
                TextSource switch
                {
                    EventTextValueSource.EventText => value.Text,
                    EventTextValueSource.EventActorId => value.ActorId,
                    EventTextValueSource.EventActorName => value.ActorName,
                    EventTextValueSource.EventType => value.Type,
                    EventTextValueSource.EventSourceId => value.SourceId,
                    _ => ConstantText
                };

            return EventTextTransform.Apply(
                raw,
                TextTransforms,
                TextPrefix,
                TextSuffix);
        }
    }
}
