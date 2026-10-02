using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    public static class RuntimeTextureRegistry
    {
        private static readonly Dictionary<string, Texture> Textures =
            new(StringComparer.Ordinal);

        public static bool Register(
            string textureId,
            Texture texture)
        {
            if (string.IsNullOrWhiteSpace(textureId) ||
                texture == null)
            {
                return false;
            }

            Textures[textureId] = texture;
            return true;
        }

        public static bool Unregister(
            string textureId)
        {
            return
                !string.IsNullOrWhiteSpace(textureId) &&
                Textures.Remove(textureId);
        }

        public static bool TryResolve(
            string textureId,
            out Texture texture)
        {
            texture = null;

            return
                !string.IsNullOrWhiteSpace(textureId) &&
                Textures.TryGetValue(
                    textureId,
                    out texture) &&
                texture != null;
        }

        public static void ClearRegistered()
        {
            Textures.Clear();
        }
    }
}
