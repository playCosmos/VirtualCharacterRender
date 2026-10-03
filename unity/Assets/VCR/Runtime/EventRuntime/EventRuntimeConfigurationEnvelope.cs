using System;

namespace VCR.Runtime.EventRuntime
{
    [Serializable]
    public sealed class EventRuntimeConfigurationEnvelope
    {
        public int Version;
        public int MaxCommandsPerEvent = 32;
        public EventRuntimeRule[] Rules =
            Array.Empty<EventRuntimeRule>();
    }
}
