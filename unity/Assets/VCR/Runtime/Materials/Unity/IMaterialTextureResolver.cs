using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    public interface IMaterialTextureResolver
    {
        bool TryResolve(
            string textureId,
            out Texture texture);
    }
}
