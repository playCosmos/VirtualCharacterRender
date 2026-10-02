using System;

namespace VCR.Runtime.Materials
{
    public sealed class MaterialCompatibilityReport
    {
        public MaterialCompatibilityReport(
            string slotId,
            string presetId,
            string shaderId,
            MaterialCompatibilityIssue[] issues)
        {
            SlotId = slotId;
            PresetId = presetId;
            ShaderId = shaderId;
            Issues = issues ??
                Array.Empty<MaterialCompatibilityIssue>();
        }

        public string SlotId { get; }
        public string PresetId { get; }
        public string ShaderId { get; }
        public MaterialCompatibilityIssue[] Issues { get; }

        public bool Compatible =>
            Issues.Length == 0;
    }
}
