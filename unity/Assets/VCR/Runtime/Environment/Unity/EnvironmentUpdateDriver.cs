using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Recurring environment tick bridge.
    ///
    /// BasicEnvironmentRuntime creates/enables this only for Hz10, Hz30, or
    /// EveryFrame policies. Static/EventDriven environments need no Update
    /// callback at all.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(7000)]
    public sealed class EnvironmentUpdateDriver :
        MonoBehaviour
    {
        private BasicEnvironmentRuntime _runtime;

        public void Bind(
            BasicEnvironmentRuntime runtime)
        {
            _runtime = runtime;
        }

        private void Update()
        {
            if (_runtime == null)
            {
                enabled = false;
                return;
            }

            _runtime.TickScheduled(
                MonotonicClock.NowMicroseconds());
        }
    }
}
