using VCR.Runtime.Materials;

namespace VCR.Runtime.Materials.Unity
{
    public interface IMaterialPresetResolver
    {
        bool TryResolvePreset(
            string presetId,
            out MaterialOverridePreset preset);
    }
}
