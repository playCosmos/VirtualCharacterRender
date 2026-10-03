namespace VCR.Runtime.EventRuntime
{
    public readonly struct EventActionCommand
    {
        public EventActionCommand(
            string ruleId,
            string actionType,
            string targetId,
            string name,
            string text,
            double value,
            bool hasValue,
            long eventSequence)
        {
            RuleId = ruleId;
            ActionType = actionType;
            TargetId = targetId;
            Name = name;
            Text = text;
            Value = value;
            HasValue = hasValue;
            EventSequence = eventSequence;
        }

        public string RuleId { get; }
        public string ActionType { get; }
        public string TargetId { get; }
        public string Name { get; }
        public string Text { get; }
        public double Value { get; }
        public bool HasValue { get; }
        public long EventSequence { get; }
    }
}
