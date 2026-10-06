using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Capabilities;

namespace VCR.Editor.P0
{
    public static class P0CapabilityMenu
    {
        private sealed class ProbeService : IDisposable
        {
            private readonly Action _onDispose;

            public ProbeService(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose?.Invoke();
            }
        }

        private sealed class TransientFailingDisposeService :
            IDisposable
        {
            private readonly Action _onDisposeAttempt;
            private bool _failNextDispose;

            public TransientFailingDisposeService(
                Action onDisposeAttempt,
                bool failFirstDispose = true)
            {
                _onDisposeAttempt =
                    onDisposeAttempt;
                _failNextDispose =
                    failFirstDispose;
            }

            public void Dispose()
            {
                _onDisposeAttempt?.Invoke();

                if (_failNextDispose)
                {
                    _failNextDispose = false;
                    throw new InvalidOperationException(
                        "synthetic capability dispose failure");
                }
            }
        }

        [MenuItem("VCR/P0/Validate Lazy Capability Registry")]
        public static void Validate()
        {
            var created = 0;
            var disposed = 0;

            using var registry =
                new CapabilityRegistry();

            var registered =
                registry.Register(
                    CapabilityIds.ProtocolVmc,
                    () =>
                    {
                        created++;
                        return new ProbeService(
                            () => disposed++);
                    });

            var secondaryRegistered =
                registry.Register(
                    CapabilityIds.Environment3D,
                    () =>
                        new ProbeService(
                            () => { }));

            var sortedStatusIndex =
                secondaryRegistered &&
                registry.StatusCount == 2 &&
                registry.TryGetStatusAt(
                    0,
                    out var firstStatus) &&
                firstStatus.Id ==
                    CapabilityIds.Environment3D &&
                firstStatus.State ==
                    CapabilityState.Disabled &&
                registry.TryGetStatusAt(
                    1,
                    out var secondStatus) &&
                secondStatus.Id ==
                    CapabilityIds.ProtocolVmc &&
                secondStatus.State ==
                    CapabilityState.Disabled &&
                !registry.TryGetStatusAt(
                    -1,
                    out _) &&
                !registry.TryGetStatusAt(
                    2,
                    out _);

            var lazyBeforeEnable =
                registered &&
                created == 0 &&
                disposed == 0 &&
                !registry.IsInstantiated(
                    CapabilityIds.ProtocolVmc) &&
                registry.GetState(
                    CapabilityIds.ProtocolVmc) ==
                    CapabilityState.Disabled;

            var firstEnabled =
                registry.Enable(
                    CapabilityIds.ProtocolVmc,
                    out var firstError) &&
                created == 1 &&
                disposed == 0 &&
                registry.IsInstantiated(
                    CapabilityIds.ProtocolVmc) &&
                registry.EnabledCount == 1 &&
                registry.TryGetStatusAt(
                    1,
                    out var enabledStatus) &&
                enabledStatus.Id ==
                    CapabilityIds.ProtocolVmc &&
                enabledStatus.State ==
                    CapabilityState.Enabled;

            var disabled =
                registry.Disable(
                    CapabilityIds.ProtocolVmc) &&
                created == 1 &&
                disposed == 1 &&
                !registry.IsInstantiated(
                    CapabilityIds.ProtocolVmc) &&
                registry.EnabledCount == 0;

            var secondEnabled =
                registry.Enable(
                    CapabilityIds.ProtocolVmc,
                    out var secondError) &&
                created == 2 &&
                disposed == 1 &&
                registry.EnabledCount == 1;

            registry.Disable(
                CapabilityIds.ProtocolVmc);

            var retryDisposeAttempts = 0;
            var retryRegistry =
                new CapabilityRegistry();

            retryRegistry.Register(
                "capability.retry-dispose",
                () =>
                    new TransientFailingDisposeService(
                        () =>
                            retryDisposeAttempts++));

            var retryEnabled =
                retryRegistry.Enable(
                    "capability.retry-dispose",
                    out var retryEnableError);
            var firstRetryDisable =
                retryRegistry.Disable(
                    "capability.retry-dispose");
            var retainedAfterFailure =
                !firstRetryDisable &&
                retryRegistry.IsInstantiated(
                    "capability.retry-dispose") &&
                retryRegistry.GetState(
                    "capability.retry-dispose") ==
                    CapabilityState.Faulted &&
                retryDisposeAttempts == 1;
            var secondRetryDisable =
                retryRegistry.Disable(
                    "capability.retry-dispose");
            var releasedAfterRetry =
                secondRetryDisable &&
                !retryRegistry.IsInstantiated(
                    "capability.retry-dispose") &&
                retryRegistry.GetState(
                    "capability.retry-dispose") ==
                    CapabilityState.Disabled &&
                retryDisposeAttempts == 2;
            retryRegistry.Dispose();

            var registryDisposeAttempts = 0;
            var disposeRegistry =
                new CapabilityRegistry();

            disposeRegistry.Register(
                "capability.dispose-retry",
                () =>
                    new TransientFailingDisposeService(
                        () =>
                            registryDisposeAttempts++));
            var disposeRegistryEnabled =
                disposeRegistry.Enable(
                    "capability.dispose-retry",
                    out var disposeRegistryEnableError);
            var firstRegistryDisposeFailed =
                false;

            try
            {
                disposeRegistry.Dispose();
            }
            catch (AggregateException)
            {
                firstRegistryDisposeFailed =
                    true;
            }

            var registryRetainedAfterFailure =
                firstRegistryDisposeFailed &&
                disposeRegistry.RegisteredCount == 1 &&
                disposeRegistry.IsInstantiated(
                    "capability.dispose-retry") &&
                disposeRegistry.GetState(
                    "capability.dispose-retry") ==
                    CapabilityState.Faulted &&
                registryDisposeAttempts == 1;

            var secondRegistryDisposeSucceeded =
                true;

            try
            {
                disposeRegistry.Dispose();
            }
            catch
            {
                secondRegistryDisposeSucceeded =
                    false;
            }

            secondRegistryDisposeSucceeded =
                secondRegistryDisposeSucceeded &&
                disposeRegistry.RegisteredCount == 0 &&
                registryDisposeAttempts == 2;

            var pass =
                sortedStatusIndex &&
                lazyBeforeEnable &&
                firstEnabled &&
                disabled &&
                secondEnabled &&
                disposed == 2 &&
                retryEnabled &&
                string.IsNullOrEmpty(
                    retryEnableError) &&
                retainedAfterFailure &&
                releasedAfterRetry &&
                disposeRegistryEnabled &&
                string.IsNullOrEmpty(
                    disposeRegistryEnableError) &&
                registryRetainedAfterFailure &&
                secondRegistryDisposeSucceeded &&
                string.IsNullOrEmpty(firstError) &&
                string.IsNullOrEmpty(secondError);

            if (pass)
            {
                Debug.Log(
                    "VCR P0 capability registry: PASS - disabled service was not instantiated; enable/disable lifecycle is lazy and repeatable.");
            }
            else
            {
                Debug.LogError(
                    $"VCR P0 capability registry: FAIL - created={created}, disposed={disposed}, enabled={registry.EnabledCount}, firstError='{firstError}', secondError='{secondError}'.");
            }
        }
    }
}
