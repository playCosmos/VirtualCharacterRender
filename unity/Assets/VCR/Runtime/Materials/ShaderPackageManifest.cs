using System;

namespace VCR.Runtime.Materials
{
    [Serializable]
    public sealed class ShaderPackageManifest
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion =
            CurrentFormatVersion;
        public string PackageId;
        public string PackageVersion;
        public string UnityVersion;
        public string UrpVersion;
        public string WindowsBundle;
        public string MacOSBundle;
        public string MaterialPreset;
        public string[] ShaderIds =
            Array.Empty<string>();
        public string[] TextureFiles =
            Array.Empty<string>();
        public string[] PreviewFiles =
            Array.Empty<string>();
    }
}
