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
                registry.EnabledCount == 1;

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

            var pass =
                lazyBeforeEnable &&
                firstEnabled &&
                disabled &&
                secondEnabled &&
                disposed == 2 &&
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
