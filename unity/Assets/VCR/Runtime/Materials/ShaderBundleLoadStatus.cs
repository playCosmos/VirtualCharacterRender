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
            string error,
            bool metadataPresent = false,
            string metadataPath = null,
            string bundleId = null,
            string targetPlatform = null,
            string bundleUnityVersion = null)
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
            MetadataPresent = metadataPresent;
            MetadataPath = metadataPath;
            BundleId = bundleId;
            TargetPlatform = targetPlatform;
            BundleUnityVersion = bundleUnityVersion;
        }

        public long Sequence { get; }
        public string Path { get; }
        public bool Success { get; }
        public int RegisteredShaderCount { get; }
        public string[] ShaderIds { get; }
        public string Platform { get; }
        public string GraphicsApi { get; }
        public string Error { get; }

        public bool MetadataPresent { get; }
        public string MetadataPath { get; }
        public string BundleId { get; }
        public string TargetPlatform { get; }
        public string BundleUnityVersion { get; }
    }
}
