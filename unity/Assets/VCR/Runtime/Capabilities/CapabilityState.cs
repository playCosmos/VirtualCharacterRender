namespace VCR.Runtime.Capabilities
{
    public enum CapabilityState
    {
        Disabled = 0,
        Enabled = 1,
        Faulted = 2
    }

    public readonly struct CapabilityStatusSnapshot
    {
        public CapabilityStatusSnapshot(
            string id,
            CapabilityState state,
            string error)
        {
            Id = id;
            State = state;
            Error = error;
        }

        public string Id { get; }
        public CapabilityState State { get; }
        public string Error { get; }
    }
}
