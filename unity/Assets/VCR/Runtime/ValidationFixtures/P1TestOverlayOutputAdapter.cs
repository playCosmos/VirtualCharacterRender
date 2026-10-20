using System;
using UnityEngine;
using VCR.Runtime.Output;

namespace VCR.Runtime.ValidationFixtures
{
    public sealed class P1TestOverlayOutputAdapter :
        MonoBehaviour,
        IOverlayOutputAdapter
    {
        public int ApplyCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public bool ThrowOnApply { get; set; }
        public bool ThrowOnShutdown { get; set; }
        public bool ThrowOnSettingsRead { get; set; }
        public bool ThrowOnStatusRead { get; set; }
        public OverlayOutputSettings LastSettings { get; private set; }

        public OverlayOutputSettings Settings
        {
            get
            {
                if (ThrowOnSettingsRead)
                {
                    throw new InvalidOperationException(
                        "P1 overlay settings read failure");
                }

                return LastSettings;
            }
        }

        public OverlayOutputStatus Status
        {
            get
            {
                if (ThrowOnStatusRead)
                {
                    throw new InvalidOperationException(
                        "P1 overlay status read failure");
                }

                return new OverlayOutputStatus(
                    supported: true,
                    active: ApplyCount > ShutdownCount,
                    adapterId: "p1-test",
                    lastError: null,
                    clientWidth: 0,
                    clientHeight: 0);
            }
        }

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
