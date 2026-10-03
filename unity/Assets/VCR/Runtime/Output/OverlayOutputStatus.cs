namespace VCR.Runtime.Output
{
    public enum OverlayOutputState
    {
        Inactive = 0,
        Configured = 1,
        PendingNativeApply = 2,
        Active = 3,
        Unsupported = 4,
        Faulted = 5
    }

    public readonly struct OverlayOutputStatus
    {
        public OverlayOutputStatus(
            bool supported,
            bool active,
            string adapterId,
            string lastError,
            int clientWidth,
            int clientHeight)
            : this(
                !supported
                    ? OverlayOutputState.Unsupported
                    : !string.IsNullOrEmpty(lastError)
                        ? OverlayOutputState.Faulted
                        : active
                            ? OverlayOutputState.Active
                            : OverlayOutputState.Inactive,
                adapterId,
                lastError,
                clientWidth,
                clientHeight)
        {
        }

        public OverlayOutputStatus(
            OverlayOutputState state,
            string adapterId,
            string lastError,
            int clientWidth,
            int clientHeight)
        {
            State = state;
            AdapterId = adapterId;
            LastError = lastError;
            ClientWidth = clientWidth;
            ClientHeight = clientHeight;
        }

        public OverlayOutputState State { get; }
        public bool Supported =>
            State != OverlayOutputState.Unsupported;
        public bool Active =>
            State == OverlayOutputState.Active;
        public bool Pending =>
            State == OverlayOutputState.PendingNativeApply;
        public bool Faulted =>
            State == OverlayOutputState.Faulted;
        public string AdapterId { get; }
        public string LastError { get; }
        public int ClientWidth { get; }
        public int ClientHeight { get; }
    }
}
