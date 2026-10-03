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

        public static bool TryGetRegistered(
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

        public static KeyValuePair<string, Texture>[]
            CaptureRegistered()
        {
            var snapshot =
                new KeyValuePair<string, Texture>[
                    Textures.Count];

            var index = 0;
            foreach (var item in Textures)
            {
                snapshot[index++] =
                    item;
            }

            return snapshot;
        }

        public static void RestoreRegistered(
            KeyValuePair<string, Texture>[] snapshot)
        {
            Textures.Clear();

            if (snapshot == null)
            {
                return;
            }

            foreach (var item in snapshot)
            {
                if (!string.IsNullOrWhiteSpace(
                        item.Key) &&
                    item.Value != null)
                {
                    Textures[item.Key] =
                        item.Value;
                }
            }
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
