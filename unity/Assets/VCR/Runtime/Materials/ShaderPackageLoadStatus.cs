using System;

namespace VCR.Runtime.Materials
{
    public readonly struct ShaderPackageLoadStatus
    {
        public ShaderPackageLoadStatus(
            long sequence,
            string packageRoot,
            string manifestPath,
            bool success,
            string packageId,
            string packageVersion,
            string targetPlatform,
            string bundlePath,
            string materialPresetPath,
            string[] shaderIds,
            string[] textureIds,
            string declaredUnityVersion,
            string declaredUrpVersion,
            double loadMilliseconds,
            string error)
        {
            Sequence = sequence;
            PackageRoot = packageRoot;
            ManifestPath = manifestPath;
            Success = success;
            PackageId = packageId;
            PackageVersion = packageVersion;
            TargetPlatform = targetPlatform;
            BundlePath = bundlePath;
            MaterialPresetPath = materialPresetPath;
            ShaderIds = shaderIds ?? Array.Empty<string>();
            TextureIds = textureIds ?? Array.Empty<string>();
            DeclaredUnityVersion = declaredUnityVersion;
            DeclaredUrpVersion = declaredUrpVersion;
            LoadMilliseconds = loadMilliseconds;
            Error = error;
        }

        public long Sequence { get; }
        public string PackageRoot { get; }
        public string ManifestPath { get; }
        public bool Success { get; }
        public string PackageId { get; }
        public string PackageVersion { get; }
        public string TargetPlatform { get; }
        public string BundlePath { get; }
        public string MaterialPresetPath { get; }
        public string[] ShaderIds { get; }
        public string[] TextureIds { get; }
        public string DeclaredUnityVersion { get; }
        public string DeclaredUrpVersion { get; }
        public double LoadMilliseconds { get; }
        public string Error { get; }
    }
}
