using System;

namespace VCR.Runtime.Materials
{
    [Serializable]
    public sealed class ShaderBundleMetadata
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion =
            CurrentFormatVersion;
        public string BundleId;
        public string TargetPlatform;
        public string UnityVersion;
        public string[] ShaderIds =
            Array.Empty<string>();
    }
}
