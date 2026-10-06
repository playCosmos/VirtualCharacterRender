using System;
using UnityEngine;
using VCR.Runtime.Output;

namespace VCR.Editor.P1
{
    public sealed class P1TestOverlayOutputAdapter :
        MonoBehaviour,
        IOverlayOutputAdapter
    {
        public int ApplyCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public bool ThrowOnApply { get; set; }
        public bool ThrowOnShutdown { get; set; }
        public OverlayOutputSettings LastSettings { get; private set; }

        public OverlayOutputSettings Settings =>
            LastSettings;

        public OverlayOutputStatus Status =>
            new(
                supported: true,
                active: ApplyCount > ShutdownCount,
                adapterId: "p1-test",
                lastError: null,
                clientWidth: 0,
                clientHeight: 0);

        public void Apply(OverlayOutputSettings settings)
        {
            if (ThrowOnApply)
            {
                throw new InvalidOperationException(
                    "P1 overlay apply failure");
            }

            LastSettings = settings;
            ApplyCount++;
        }

        public void Shutdown()
        {
            if (ThrowOnShutdown)
            {
                throw new InvalidOperationException(
                    "P1 overlay shutdown failure");
            }

            ShutdownCount++;
        }
    }
}
