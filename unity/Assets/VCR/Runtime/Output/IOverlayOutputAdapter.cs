namespace VCR.Runtime.Output
{
    // Optional lifecycle hook for process exit. Native window styles do not
    // need to be reverted on a window the OS is about to destroy.
    public interface IProcessExitOverlayOutputAdapter
    {
        void ShutdownForProcessExit();
    }

    public interface IOverlayOutputAdapter
    {
        OverlayOutputStatus Status { get; }
        OverlayOutputSettings Settings { get; }
        void Apply(OverlayOutputSettings settings);
        void Shutdown();
    }
}
