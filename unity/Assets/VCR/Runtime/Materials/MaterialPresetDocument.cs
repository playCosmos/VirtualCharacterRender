using System;

namespace VCR.Runtime.Materials
{
    [Serializable]
    public sealed class MaterialPresetDocument
    {
        public int Version = 1;
        public MaterialOverridePreset[] Presets =
            Array.Empty<MaterialOverridePreset>();
    }
}
