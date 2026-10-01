namespace VCR.Runtime.Materials
{
    public readonly struct MaterialOverrideStatus
    {
        public MaterialOverrideStatus(
            string slotId,
            MaterialOverrideHealth health,
            string shaderId,
            string error)
        {
            SlotId = slotId;
            Health = health;
            ShaderId = shaderId;
            Error = error;
        }

        public string SlotId { get; }
        public MaterialOverrideHealth Health { get; }
        public string ShaderId { get; }
        public string Error { get; }
    }
}
