using System;
using VCR.Runtime.Events;

namespace VCR.Runtime.EventRuntime
{
    [Serializable]
    public sealed class EventActionTemplate
    {
        public string ActionType;
        public string TargetId;
        public string Name;
        public bool HasValue;
        public EventNumericValueSource NumericSource;
        public double ConstantNumber;
        public double ConstantNumberY;
        public double ConstantNumberZ;
        public double ConstantNumberW;
        public double NumericScale = 1.0;
        public double NumericOffset;
        public EventTextValueSource TextSource;
        public string ConstantText;
        public EventTextTransformFlags TextTransforms;
        public string TextPrefix;
        public string TextSuffix;

        public EventActionCommand Build(
            string ruleId,
            NormalizedEvent value)
        {
            var rawNumber =
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
            var transformed =
                rawNumber * scale + offset;
            var number =
                IsFinite(transformed)
                    ? transformed
                    : 0.0;

            var rawText =
                TextSource switch
                {
                    EventTextValueSource.EventText => value.Text,
                    EventTextValueSource.EventActorId => value.ActorId,
                    EventTextValueSource.EventActorName => value.ActorName,
                    EventTextValueSource.EventType => value.Type,
                    EventTextValueSource.EventSourceId => value.SourceId,
                    _ => ConstantText
                };

            var text =
                EventTextTransform.Apply(
                    rawText,
                    TextTransforms,
                    TextPrefix,
                    TextSuffix);

            return new EventActionCommand(
                ruleId,
                ActionType,
                TargetId,
                Name,
                text,
                number,
                FiniteOrZero(
                    ConstantNumberY),
                FiniteOrZero(
                    ConstantNumberZ),
                FiniteOrZero(
                    ConstantNumberW),
                HasValue,
                value.Sequence);
        }

        private static double FiniteOrZero(
            double value)
        {
            return IsFinite(value)
                ? value
                : 0.0;
        }

        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value) &&
                !double.IsInfinity(value);
        }
    }
}
