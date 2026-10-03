namespace VCR.Runtime.Protocols.WebSocket
{
    public interface IWebSocketTextMessageHandler
    {
        bool TryHandleText(
            string message,
            out string error);
    }
}
