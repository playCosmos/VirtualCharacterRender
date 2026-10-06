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
        private readonly List<string> _sortedIds =
            new();

        public int RegisteredCount => _entries.Count;
        public int StatusCount => _sortedIds.Count;

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

            var insertIndex =
                _sortedIds.BinarySearch(
                    capabilityId,
                    StringComparer.Ordinal);

            if (insertIndex < 0)
            {
                insertIndex =
                    ~insertIndex;
            }

            _sortedIds.Insert(
                insertIndex,
                capabilityId);

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

            var cleanupError =
                TryDisposeInstance(entry);

            if (cleanupError != null)
            {
                entry.State =
                    CapabilityState.Faulted;
                entry.Error =
                    "Capability cleanup failed before enable: " +
                    cleanupError.Message;
                error = entry.Error;
                return false;
            }

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

            var cleanupError =
                TryDisposeInstance(entry);

            if (cleanupError != null)
            {
                entry.State =
                    CapabilityState.Faulted;
                entry.Error =
                    "Capability cleanup failed while disabling: " +
                    cleanupError.Message;
                return false;
            }

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

        public bool TryGetStatusAt(
            int index,
            out CapabilityStatusSnapshot status)
        {
            if (index < 0 ||
                index >= _sortedIds.Count)
            {
                status = default;
                return false;
            }

            var id =
                _sortedIds[index];
            var entry =
                _entries[id];

            status =
                new CapabilityStatusSnapshot(
                    id,
                    entry.State,
                    entry.Error);
            return true;
        }

        public CapabilityStatusSnapshot[]
            CaptureStatuses()
        {
            var result =
                new CapabilityStatusSnapshot[
                    _sortedIds.Count];

            for (var i = 0;
                 i < _sortedIds.Count;
                 i++)
            {
                TryGetStatusAt(
                    i,
                    out result[i]);
            }

            return result;
        }

        private static Exception TryDisposeInstance(
            Entry entry)
        {
            var instance =
                entry.Instance;

            entry.Instance = null;

            if (instance == null)
            {
                return null;
            }

            try
            {
                instance.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        public void Dispose()
        {
            List<Exception> failures = null;

            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                var cleanupError =
                    TryDisposeInstance(entry);

                entry.State =
                    CapabilityState.Disabled;

                if (cleanupError == null)
                {
                    entry.Error = null;
                    continue;
                }

                entry.Error =
                    "Capability cleanup failed while disposing: " +
                    cleanupError.Message;

                failures ??=
                    new List<Exception>();

                failures.Add(
                    new InvalidOperationException(
                        $"Capability '{pair.Key}' cleanup failed.",
                        cleanupError));
            }

            _entries.Clear();
            _sortedIds.Clear();

            if (failures != null)
            {
                throw new AggregateException(
                    "One or more capabilities failed to dispose.",
                    failures);
            }
        }
    }
}
