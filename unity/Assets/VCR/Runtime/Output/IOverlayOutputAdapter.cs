namespace VCR.Runtime.Output
{
    public interface IOverlayOutputAdapter
    {
        OverlayOutputStatus Status { get; }
        void Apply(OverlayOutputSettings settings);
        void Shutdown();
    }
}
