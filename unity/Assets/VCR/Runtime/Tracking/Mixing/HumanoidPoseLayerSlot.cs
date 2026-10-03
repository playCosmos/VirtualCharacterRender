using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Mixing
{
    /// <summary>
    /// Serialized provider/settings pair for an ordered humanoid-pose layer.
    /// The mixer applies slots in array order after the routed base and the
    /// legacy primary pose-layer slot.
    /// </summary>
    [Serializable]
    public sealed class HumanoidPoseLayerSlot
    {
        [SerializeField]
        private MonoBehaviour providerBehaviour;

        [SerializeField]
        private HumanoidPoseLayerSettings settings =
            new HumanoidPoseLayerSettings();

        public MonoBehaviour ProviderBehaviour =>
            providerBehaviour;

        public ITrackingFrameProvider Provider =>
            providerBehaviour as
                ITrackingFrameProvider;

        public HumanoidPoseLayerSettings Settings =>
            settings ??=
                new HumanoidPoseLayerSettings();

        public void Configure(
            MonoBehaviour provider,
            HumanoidPoseLayerSettings layerSettings)
        {
            providerBehaviour = provider;
            settings =
                layerSettings ??
                new HumanoidPoseLayerSettings();
        }
    }
}
