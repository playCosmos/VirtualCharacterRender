using System;

namespace VCR.Runtime.EventRuntime
{
    public enum EventStateConditionKind
    {
        Exists = 0,
        Missing = 1,
        NumberGreaterOrEqual = 2,
        NumberLessOrEqual = 3,
        NumberEqual = 4,
        TextEqual = 5
    }

    [Serializable]
    public sealed class EventStateCondition
    {
        public EventStateConditionKind Kind;
        public string Key;
        public double NumberValue;
        public string TextValue;

        public bool Evaluate(EventRuntimeStateStore state)
        {
            if (state == null)
            {
                return false;
            }

            return Kind switch
            {
                EventStateConditionKind.Exists =>
                    state.Contains(Key),
                EventStateConditionKind.Missing =>
                    !state.Contains(Key),
                EventStateConditionKind.NumberGreaterOrEqual =>
                    state.GetNumber(Key) >= NumberValue,
                EventStateConditionKind.NumberLessOrEqual =>
                    state.GetNumber(Key) <= NumberValue,
                EventStateConditionKind.NumberEqual =>
                    Math.Abs(state.GetNumber(Key) - NumberValue) <= 0.000001,
                EventStateConditionKind.TextEqual =>
                    string.Equals(
                        state.GetText(Key),
                        TextValue,
                        StringComparison.Ordinal),
                _ => false
            };
        }
    }
}
