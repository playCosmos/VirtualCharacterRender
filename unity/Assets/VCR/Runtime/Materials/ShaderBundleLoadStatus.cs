using System;

namespace VCR.Runtime.Materials
{
    public readonly struct ShaderBundleLoadStatus
    {
        public ShaderBundleLoadStatus(
            long sequence,
            string path,
            bool success,
            int registeredShaderCount,
            string[] shaderIds,
            string platform,
            string graphicsApi,
            string error)
        {
            Sequence = sequence;
            Path = path;
            Success = success;
            RegisteredShaderCount =
                registeredShaderCount;
            ShaderIds =
                shaderIds ??
                Array.Empty<string>();
            Platform = platform;
            GraphicsApi = graphicsApi;
            Error = error;
        }

        public long Sequence { get; }
        public string Path { get; }
        public bool Success { get; }
        public int RegisteredShaderCount { get; }
        public string[] ShaderIds { get; }
        public string Platform { get; }
        public string GraphicsApi { get; }
        public string Error { get; }
    }
}
