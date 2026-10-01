using System;

namespace VCR.Runtime.Protocols.Osc
{
    public sealed class OscMessage
    {
        public OscMessage(string address, OscArgument[] arguments)
        {
            Address = address ??
                throw new ArgumentNullException(nameof(address));
            Arguments = arguments ?? Array.Empty<OscArgument>();
        }

        public string Address { get; }
        public OscArgument[] Arguments { get; }
    }
}
