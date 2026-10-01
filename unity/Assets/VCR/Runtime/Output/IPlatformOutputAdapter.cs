namespace VCR.Runtime.Output
{
    /// <summary>
    /// Platform-native window/compositor adapter.
    /// Native handles never cross this boundary.
    /// </summary>
    public interface IPlatformOutputAdapter
    {
        string AdapterId { get; }
        bool IsSupported { get; }

        OutputApplyResult Apply(OutputWindowRequest request);
        void Reset();
    }
}
