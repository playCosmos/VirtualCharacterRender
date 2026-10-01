using System;
using System.Collections.Generic;

namespace VCR.Runtime.Capabilities
{
    /// <summary>
    /// Factory-based lifecycle registry.
    ///
    /// Registering a capability does not instantiate its service. Enabling it
    /// creates the service; disabling disposes and releases it.
    /// </summary>
    public sealed class CapabilityRegistry : IDisposable
    {
        private sealed class Entry
        {
            public Func<IDisposable> Factory;
            public IDisposable Instance;
            public CapabilityState State;
            public string Error;
        }

        private readonly Dictionary<string, Entry> _entries =
            new(StringComparer.Ordinal);

        public int RegisteredCount => _entries.Count;

        public int EnabledCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries.Values)
                {
                    if (entry.State ==
                        CapabilityState.Enabled)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool Register(
            string capabilityId,
            Func<IDisposable> factory)
        {
            if (string.IsNullOrWhiteSpace(
                    capabilityId) ||
                factory == null ||
                _entries.ContainsKey(capabilityId))
            {
                return false;
            }

            _entries.Add(
                capabilityId,
                new Entry
                {
                    Factory = factory,
                    State =
                        CapabilityState.Disabled
                });

            return true;
        }

        public bool Enable(
            string capabilityId,
            out string error)
        {
            error = null;

            if (!_entries.TryGetValue(
                    capabilityId,
                    out var entry))
            {
                error =
                    $"Capability '{capabilityId}' is not registered.";
                return false;
            }

            if (entry.State ==
                    CapabilityState.Enabled &&
                entry.Instance != null)
            {
                return true;
            }

            DisposeInstance(entry);

            try
            {
                var instance = entry.Factory();
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Capability factory returned null.");
                }

                entry.Instance = instance;
                entry.State =
                    CapabilityState.Enabled;
                entry.Error = null;
                return true;
            }
            catch (Exception exception)
            {
                entry.State =
                    CapabilityState.Faulted;
                entry.Error = exception.Message;
                error = entry.Error;
                return false;
            }
        }

        public bool Disable(
            string capabilityId)
        {
            if (!_entries.TryGetValue(
                    capabilityId,
                    out var entry))
            {
                return false;
            }

            DisposeInstance(entry);
            entry.State =
                CapabilityState.Disabled;
            entry.Error = null;
            return true;
        }

        public CapabilityState GetState(
            string capabilityId)
        {
            return
                _entries.TryGetValue(
                    capabilityId,
                    out var entry)
                    ? entry.State
                    : CapabilityState.Disabled;
        }

        public string GetError(
            string capabilityId)
        {
            return
                _entries.TryGetValue(
                    capabilityId,
                    out var entry)
                    ? entry.Error
                    : null;
        }

        public bool IsInstantiated(
            string capabilityId)
        {
            return
                _entries.TryGetValue(
                    capabilityId,
                    out var entry) &&
                entry.Instance != null;
        }

        private static void DisposeInstance(
            Entry entry)
        {
            if (entry.Instance == null)
            {
                return;
            }

            try
            {
                entry.Instance.Dispose();
            }
            finally
            {
                entry.Instance = null;
            }
        }

        public void Dispose()
        {
            foreach (var entry in _entries.Values)
            {
                DisposeInstance(entry);
                entry.State =
                    CapabilityState.Disabled;
            }

            _entries.Clear();
        }
    }
}
