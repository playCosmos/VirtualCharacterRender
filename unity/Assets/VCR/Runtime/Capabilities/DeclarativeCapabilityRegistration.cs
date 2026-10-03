using System;
using System.Collections.Generic;

namespace VCR.Runtime.Capabilities
{
    /// <summary>
    /// Immutable metadata for a declarative package/provider.
    ///
    /// This type describes advertised capabilities only. It does not contain
    /// factories, callbacks, reflection targets, or executable plugin code.
    /// </summary>
    public sealed class DeclarativeCapabilityRegistration
    {
        private readonly string[] _capabilityIds;

        internal DeclarativeCapabilityRegistration(
            object owner,
            string providerId,
            string providerVersion,
            string[] capabilityIds)
        {
            Owner = owner;
            ProviderId = providerId;
            ProviderVersion = providerVersion;
            _capabilityIds =
                capabilityIds ??
                Array.Empty<string>();
        }

        internal object Owner { get; }

        public string ProviderId { get; }
        public string ProviderVersion { get; }

        public IReadOnlyList<string> CapabilityIds =>
            _capabilityIds;

        internal string[] CloneCapabilityIds()
        {
            return (string[])
                _capabilityIds.Clone();
        }
    }
}
