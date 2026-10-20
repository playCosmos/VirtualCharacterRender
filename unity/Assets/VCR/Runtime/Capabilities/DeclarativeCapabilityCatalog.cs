using System;
using System.Collections.Generic;

namespace VCR.Runtime.Capabilities
{
    /// <summary>
    /// Metadata-only catalog for declarative packages.
    ///
    /// This is intentionally separate from CapabilityRegistry. The executable
    /// registry owns service factories/lifetimes; this catalog only advertises
    /// which already-loaded declarative provider supplies which capability.
    /// </summary>
    public sealed class DeclarativeCapabilityCatalog
    {
        private readonly object _sync = new();

        private readonly Dictionary<string, DeclarativeCapabilityRegistration>
            _providers =
                new(StringComparer.Ordinal);

        public static DeclarativeCapabilityCatalog Shared { get; } =
            new();

        public int RegisteredProviderCount
        {
            get
            {
                lock (_sync)
                {
                    return _providers.Count;
                }
            }
        }

        public DeclarativeCapabilityRegistration CaptureOwner(
            object owner)
        {
            if (owner == null)
            {
                return null;
            }

            lock (_sync)
            {
                foreach (var registration in
                         _providers.Values)
                {
                    if (ReferenceEquals(
                            registration.Owner,
                            owner))
                    {
                        return new DeclarativeCapabilityRegistration(
                            owner,
                            registration.ProviderId,
                            registration.ProviderVersion,
                            registration.CloneCapabilityIds());
                    }
                }
            }

            return null;
        }

        public bool TryReplace(
            object owner,
            string providerId,
            string providerVersion,
            IReadOnlyList<string> capabilityIds,
            out string error)
        {
            error = null;

            if (owner == null)
            {
                error =
                    "Declarative capability registration requires an owner.";
                return false;
            }

            if (!IsSafeIdentifier(
                    providerId))
            {
                error =
                    "Declarative capability provider id is invalid.";
                return false;
            }

            if (!IsSafeVersion(
                    providerVersion))
            {
                error =
                    "Declarative capability provider version is invalid.";
                return false;
            }

            if (!TryNormalizeCapabilities(
                    capabilityIds,
                    out var normalized,
                    out error))
            {
                return false;
            }

            lock (_sync)
            {
                if (_providers.TryGetValue(
                        providerId,
                        out var existingById) &&
                    !ReferenceEquals(
                        existingById.Owner,
                        owner))
                {
                    error =
                        $"Declarative capability provider '{providerId}' is already registered by another owner.";
                    return false;
                }

                RemoveOwnerLocked(
                    owner);

                _providers[providerId] =
                    new DeclarativeCapabilityRegistration(
                        owner,
                        providerId,
                        providerVersion,
                        normalized);

                return true;
            }
        }

        public bool RestoreOwner(
            object owner,
            DeclarativeCapabilityRegistration snapshot,
            out string error)
        {
            error = null;

            if (owner == null)
            {
                error =
                    "Declarative capability restoration requires an owner.";
                return false;
            }

            lock (_sync)
            {
                RemoveOwnerLocked(
                    owner);

                if (snapshot == null)
                {
                    return true;
                }

                if (_providers.TryGetValue(
                        snapshot.ProviderId,
                        out var conflicting) &&
                    !ReferenceEquals(
                        conflicting.Owner,
                        owner))
                {
                    error =
                        $"Declarative capability provider '{snapshot.ProviderId}' was claimed while rollback was in progress.";
                    return false;
                }

                _providers[snapshot.ProviderId] =
                    new DeclarativeCapabilityRegistration(
                        owner,
                        snapshot.ProviderId,
                        snapshot.ProviderVersion,
                        snapshot.CloneCapabilityIds());

                return true;
            }
        }

        public bool UnregisterOwner(
            object owner)
        {
            if (owner == null)
            {
                return false;
            }

            lock (_sync)
            {
                return RemoveOwnerLocked(
                    owner);
            }
        }

        public bool TryGetProvider(
            string providerId,
            out DeclarativeCapabilityRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(
                    providerId))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                if (!_providers.TryGetValue(
                        providerId,
                        out var existing))
                {
                    registration = null;
                    return false;
                }

                registration =
                    new DeclarativeCapabilityRegistration(
                        existing.Owner,
                        existing.ProviderId,
                        existing.ProviderVersion,
                        existing.CloneCapabilityIds());

                return true;
            }
        }

        public int GetProviderCount(
            string capabilityId)
        {
            if (string.IsNullOrWhiteSpace(
                    capabilityId))
            {
                return 0;
            }

            lock (_sync)
            {
                var count = 0;

                foreach (var registration in
                         _providers.Values)
                {
                    foreach (var candidate in
                             registration.CapabilityIds)
                    {
                        if (string.Equals(
                                candidate,
                                capabilityId,
                                StringComparison.Ordinal))
                        {
                            count++;
                            break;
                        }
                    }
                }

                return count;
            }
        }

        public bool IsProvided(
            string capabilityId)
        {
            return
                GetProviderCount(
                    capabilityId) > 0;
        }

        private bool RemoveOwnerLocked(
            object owner)
        {
            string removeId = null;

            foreach (var item in
                     _providers)
            {
                if (ReferenceEquals(
                        item.Value.Owner,
                        owner))
                {
                    removeId = item.Key;
                    break;
                }
            }

            return
                removeId != null &&
                _providers.Remove(
                    removeId);
        }

        private static bool TryNormalizeCapabilities(
            IReadOnlyList<string> capabilityIds,
            out string[] normalized,
            out string error)
        {
            normalized = null;
            error = null;

            if (capabilityIds == null ||
                capabilityIds.Count == 0)
            {
                error =
                    "Declarative capability registration requires at least one capability.";
                return false;
            }

            var unique =
                new HashSet<string>(
                    StringComparer.Ordinal);
            var result =
                new string[
                    capabilityIds.Count];

            for (var i = 0;
                 i < capabilityIds.Count;
                 i++)
            {
                var capabilityId =
                    capabilityIds[i];

                if (!IsSafeIdentifier(
                        capabilityId))
                {
                    error =
                        $"Declarative capability id '{capabilityId}' is invalid.";
                    return false;
                }

                if (!unique.Add(
                        capabilityId))
                {
                    error =
                        $"Declarative capability id '{capabilityId}' is duplicated.";
                    return false;
                }

                result[i] =
                    capabilityId;
            }

            normalized = result;
            return true;
        }

        private static bool IsSafeIdentifier(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length > 128 ||
                value == "." ||
                value == "..")
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch) ||
                    ch == '.' ||
                    ch == '_' ||
                    ch == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsSafeVersion(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length > 64)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch) ||
                    ch == '.' ||
                    ch == '+' ||
                    ch == '_' ||
                    ch == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }
}
