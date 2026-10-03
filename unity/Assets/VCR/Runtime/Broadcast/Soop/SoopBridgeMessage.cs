using System;

namespace VCR.Runtime.Broadcast.Soop
{
    /// <summary>
    /// VCR-owned bridge schema. A platform connector keeps SOOP credentials,
    /// reconnect logic, and raw platform payloads outside the core runtime and
    /// forwards only normalized bridge fields.
    /// </summary>
    [Serializable]
    public sealed class SoopBridgeMessage
    {
        public int version = 1;
        public string type;
        public string eventId;
        public string userId;
        public string nickname;
        public string text;
        public long count;
    }
}
