using Leap;
using UnityEngine;

namespace VCR.Runtime.Tracking.Ultraleap
{
    /// <summary>
    /// Prevents the vendored Ultraleap hint manager from opening an unused
    /// LeapC service connection during Player startup.
    /// </summary>
    internal static class VcrUltraleapRuntimePolicy
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisableUnusedLeapCHintConnection()
        {
            // HandTrackingHintManager.StartupHints() runs after scene load and
            // connects to the Leap service even when no hints are configured.
            // VCR does not use Ultraleap hints; the hand provider opens its own
            // controller only when the user enables Ultraleap tracking.
            HandTrackingSourceUtility.LeapOpenXRHintingAvailable = true;
        }
    }
}
