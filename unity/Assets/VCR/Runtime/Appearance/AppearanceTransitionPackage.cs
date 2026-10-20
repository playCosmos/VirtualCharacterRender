using System;

namespace VCR.Runtime.Appearance
{
    [Serializable]
    public sealed class AppearanceTransitionPackage
    {
        public const int CurrentVersion = 3;

        public int Version =
            CurrentVersion;
        public string PackageId;
        public AppearanceTransitionPreset[] Transitions =
            Array.Empty<AppearanceTransitionPreset>();
    }
}
