namespace VCR.Runtime.Environment
{
    public readonly struct EnvironmentStateChange
    {
        public EnvironmentStateChange(
            string previousStateId,
            string stateId)
        {
            PreviousStateId = previousStateId;
            StateId = stateId;
        }

        public string PreviousStateId { get; }
        public string StateId { get; }
    }
}
