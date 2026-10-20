namespace VCR.Runtime.Events
{
    public readonly struct NormalizedEvent
    {
        public NormalizedEvent(
            string type,
            string sourceId,
            long timestampUs,
            string actorId = null,
            string text = null,
            double amount = 0.0,
            string currency = null,
            bool hasAmount = false,
            long sequence = 0,
            string actorName = null)
        {
            Type = type;
            SourceId = sourceId;
            TimestampUs = timestampUs;
            ActorId = actorId;
            Text = text;
            Amount = amount;
            Currency = currency;
            HasAmount = hasAmount;
            Sequence = sequence;
            ActorName = actorName;
        }

        public string Type { get; }
        public string SourceId { get; }
        public long TimestampUs { get; }
        public string ActorId { get; }
        public string ActorName { get; }
        public string Text { get; }
        public double Amount { get; }
        public string Currency { get; }
        public bool HasAmount { get; }
        public long Sequence { get; }

        public NormalizedEvent WithSequence(long sequence)
        {
            return new NormalizedEvent(
                Type,
                SourceId,
                TimestampUs,
                ActorId,
                Text,
                Amount,
                Currency,
                HasAmount,
                sequence,
                ActorName);
        }
    }
}
