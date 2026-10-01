namespace VCR.Runtime.Output
{
    public readonly struct OverlayOutputStatus
    {
        public OverlayOutputStatus(
            bool supported,
            bool active,
            string adapterId,
            string lastError,
            int clientWidth,
            int clientHeight)
        {
            Supported = supported;
            Active = active;
            AdapterId = adapterId;
            LastError = lastError;
            ClientWidth = clientWidth;
            ClientHeight = clientHeight;
        }

        public bool Supported { get; }
        public bool Active { get; }
        public string AdapterId { get; }
        public string LastError { get; }
        public int ClientWidth { get; }
        public int ClientHeight { get; }
    }
}
