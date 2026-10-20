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
            providerBehaviour != null
                ? providerBehaviour as
                    ITrackingFrameProvider
                : null;

        public HumanoidPoseLayerSettings Settings =>
            RuntimeSettings.Clone();

        internal HumanoidPoseLayerSettings RuntimeSettings =>
            settings ??=
                new HumanoidPoseLayerSettings();

        public HumanoidPoseLayerSlot Clone()
        {
            var clone =
                new HumanoidPoseLayerSlot();
            clone.Configure(
                providerBehaviour,
                RuntimeSettings);
            return clone;
        }

        public void Configure(
            MonoBehaviour provider,
            HumanoidPoseLayerSettings layerSettings)
        {
            providerBehaviour = provider;
            settings =
                layerSettings?.Clone() ??
                new HumanoidPoseLayerSettings();
        }
    }
}
