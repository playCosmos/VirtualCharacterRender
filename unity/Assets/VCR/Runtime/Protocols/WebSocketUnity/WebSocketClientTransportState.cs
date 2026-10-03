namespace VCR.Runtime.Protocols.WebSocketUnity
{
    public enum WebSocketClientTransportState
    {
        Stopped = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Faulted = 4
    }
}
