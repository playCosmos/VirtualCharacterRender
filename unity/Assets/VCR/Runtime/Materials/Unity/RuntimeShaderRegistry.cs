using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// Runtime registry for already-compiled shader assets.
    ///
    /// Built-player shaders may resolve through Shader.Find. External shader
    /// packages register Shader objects loaded from platform-specific bundles.
    /// Raw HLSL source compilation is intentionally outside this runtime path.
    /// </summary>
    public static class RuntimeShaderRegistry
    {
        private static readonly Dictionary<string, Shader> Shaders =
            new(StringComparer.Ordinal);

        public static bool Register(
            string shaderId,
            Shader shader)
        {
            if (string.IsNullOrWhiteSpace(shaderId) ||
                shader == null)
            {
                return false;
            }

            Shaders[shaderId] = shader;
            return true;
        }

        public static bool Register(Shader shader)
        {
            return
                shader != null &&
                Register(shader.name, shader);
        }

        public static bool Unregister(string shaderId)
        {
            return
                !string.IsNullOrWhiteSpace(shaderId) &&
                Shaders.Remove(shaderId);
        }

        public static bool TryResolve(
            string shaderId,
            out Shader shader)
        {
            shader = null;

            if (string.IsNullOrWhiteSpace(shaderId))
            {
                return false;
            }

            if (Shaders.TryGetValue(
                    shaderId,
                    out shader) &&
                shader != null)
            {
                return true;
            }

            shader = Shader.Find(shaderId);
            return shader != null;
        }

        public static string[] GetRegisteredIds()
        {
            var ids =
                new string[
                    Shaders.Count];

            Shaders.Keys.CopyTo(
                ids,
                0);

            Array.Sort(
                ids,
                StringComparer.Ordinal);

            return ids;
        }

        public static void ClearRegistered()
        {
            Shaders.Clear();
        }
    }
}
