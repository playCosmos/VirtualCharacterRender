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
            : this(
                ruleId,
                actionType,
                targetId,
                name,
                text,
                value,
                0.0,
                0.0,
                0.0,
                hasValue,
                eventSequence)
        {
        }

        public EventActionCommand(
            string ruleId,
            string actionType,
            string targetId,
            string name,
            string text,
            double value,
            double valueY,
            double valueZ,
            double valueW,
            bool hasValue,
            long eventSequence)
        {
            RuleId = ruleId;
            ActionType = actionType;
            TargetId = targetId;
            Name = name;
            Text = text;
            Value = value;
            ValueY = valueY;
            ValueZ = valueZ;
            ValueW = valueW;
            HasValue = hasValue;
            EventSequence = eventSequence;
        }

        public string RuleId { get; }
        public string ActionType { get; }
        public string TargetId { get; }
        public string Name { get; }
        public string Text { get; }
        public double Value { get; }
        public double ValueY { get; }
        public double ValueZ { get; }
        public double ValueW { get; }
        public bool HasValue { get; }
        public long EventSequence { get; }
    }
}
