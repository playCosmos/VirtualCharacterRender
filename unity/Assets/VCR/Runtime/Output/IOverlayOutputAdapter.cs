namespace VCR.Runtime.Output
{
    public interface IOverlayOutputAdapter
    {
        OverlayOutputStatus Status { get; }
        OverlayOutputSettings Settings { get; }
        void Apply(OverlayOutputSettings settings);
        void Shutdown();
    }
}
