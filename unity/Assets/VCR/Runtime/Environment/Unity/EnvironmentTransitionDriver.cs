using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    [DisallowMultipleComponent]
    internal sealed class EnvironmentTransitionDriver :
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
            if (_runtime == null ||
                !_runtime.TransitionStatus.Active)
            {
                enabled = false;
                return;
            }

            _runtime.TickTransition(
                MonotonicClock.NowMicroseconds());
        }
    }
}
