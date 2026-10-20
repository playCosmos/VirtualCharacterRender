using System;

namespace VCR.Runtime.Materials
{
    public sealed class MaterialCompatibilityReport
    {
        public MaterialCompatibilityReport(
            string slotId,
            string presetId,
            string shaderId,
            MaterialCompatibilityIssue[] issues,
            string platform = null,
            string graphicsApi = null)
        {
            SlotId = slotId;
            PresetId = presetId;
            ShaderId = shaderId;
            Issues = issues ??
                Array.Empty<MaterialCompatibilityIssue>();
            Platform = platform;
            GraphicsApi = graphicsApi;
        }

        public string SlotId { get; }
        public string PresetId { get; }
        public string ShaderId { get; }
        public MaterialCompatibilityIssue[] Issues { get; }
        public string Platform { get; }
        public string GraphicsApi { get; }

        public bool Compatible =>
            Issues.Length == 0;
    }
}
