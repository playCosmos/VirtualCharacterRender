namespace VCR.Runtime.Materials
{
    public readonly struct MaterialSlotDescriptor
    {
        public MaterialSlotDescriptor(
            string id,
            string rendererPath,
            int slotIndex,
            string sourceMaterialName,
            string sourceShaderName)
        {
            Id = id;
            RendererPath = rendererPath;
            SlotIndex = slotIndex;
            SourceMaterialName = sourceMaterialName;
            SourceShaderName = sourceShaderName;
        }

        public string Id { get; }
        public string RendererPath { get; }
        public int SlotIndex { get; }
        public string SourceMaterialName { get; }
        public string SourceShaderName { get; }
    }
}
