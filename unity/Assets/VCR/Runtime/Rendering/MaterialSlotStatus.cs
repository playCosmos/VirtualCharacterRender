namespace VCR.Runtime.Rendering
{
    public enum MaterialOverrideHealth
    {
        Source = 0,
        Active = 1,
        Failed = 2,
    }

    public readonly struct MaterialSlotStatus
    {
        public MaterialSlotStatus(
            string slotId,
            string rendererPath,
            int materialIndex,
            string sourceMaterialName,
            string sourceShaderName,
            MaterialOverrideHealth health,
            string overrideShaderId,
            string lastError)
        {
            SlotId = slotId;
            RendererPath = rendererPath;
            MaterialIndex = materialIndex;
            SourceMaterialName = sourceMaterialName;
            SourceShaderName = sourceShaderName;
            Health = health;
            OverrideShaderId = overrideShaderId;
            LastError = lastError;
        }

        public string SlotId { get; }
        public string RendererPath { get; }
        public int MaterialIndex { get; }
        public string SourceMaterialName { get; }
        public string SourceShaderName { get; }
        public MaterialOverrideHealth Health { get; }
        public string OverrideShaderId { get; }
        public string LastError { get; }
    }
}
