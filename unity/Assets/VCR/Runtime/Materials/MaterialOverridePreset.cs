using System;

namespace VCR.Runtime.Materials
{
    [Serializable]
    public sealed class MaterialOverridePreset
    {
        public string PresetId;
        public string ShaderId;
        public MaterialParameterOverride[] Parameters =
            Array.Empty<MaterialParameterOverride>();

        public bool PreserveSourceShader =>
            string.IsNullOrWhiteSpace(ShaderId);
    }
}
