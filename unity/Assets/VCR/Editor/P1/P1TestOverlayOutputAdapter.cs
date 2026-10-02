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
        public OverlayOutputSettings LastSettings { get; private set; }

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
            LastSettings = settings;
            ApplyCount++;
        }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }
}
