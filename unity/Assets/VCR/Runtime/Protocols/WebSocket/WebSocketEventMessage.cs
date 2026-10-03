using System;

namespace VCR.Runtime.Protocols.WebSocket
{
    /// <summary>
    /// JSON DTO for the versioned WebSocket event-ingress protocol.
    /// Field names intentionally match the wire format.
    /// </summary>
    [Serializable]
    public sealed class WebSocketEventMessage
    {
        public int version = 1;
        public string op;
        public string type;
        public string actorId;
        public string actorName;
        public string text;
        public double amount;
        public string currency;
        public bool hasAmount;
    }
}
