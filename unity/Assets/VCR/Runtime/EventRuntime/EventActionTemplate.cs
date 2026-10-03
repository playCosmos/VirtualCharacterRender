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
        public EventTextValueSource TextSource;
        public string ConstantText;

        public EventActionCommand Build(
            string ruleId,
            NormalizedEvent value)
        {
            var number =
                NumericSource == EventNumericValueSource.EventAmount &&
                value.HasAmount
                    ? value.Amount
                    : ConstantNumber;

            var text =
                TextSource switch
                {
                    EventTextValueSource.EventText => value.Text,
                    EventTextValueSource.EventActorId => value.ActorId,
                    EventTextValueSource.EventActorName => value.ActorName,
                    EventTextValueSource.EventType => value.Type,
                    EventTextValueSource.EventSourceId => value.SourceId,
                    _ => ConstantText
                };

            return new EventActionCommand(
                ruleId,
                ActionType,
                TargetId,
                Name,
                text,
                number,
                HasValue,
                value.Sequence);
        }
    }
}
