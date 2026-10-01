using System;
using System.Collections.Generic;
using UnityEngine;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// Runtime registry for precompiled Shader assets.
    ///
    /// The registry owns no materials and performs no recurring frame work.
    /// </summary>
    public static class RuntimeShaderRegistry
    {
        private static readonly Dictionary<string, Shader> Shaders =
            new(StringComparer.Ordinal);

        public static int Count => Shaders.Count;

        public static bool Register(
            string shaderId,
            Shader shader,
            bool replace = true)
        {
            if (string.IsNullOrWhiteSpace(shaderId) ||
                shader == null)
            {
                return false;
            }

            if (!replace &&
                Shaders.ContainsKey(shaderId))
            {
                return false;
            }

            Shaders[shaderId] = shader;
            return true;
        }

        public static bool Unregister(string shaderId)
        {
            if (string.IsNullOrWhiteSpace(shaderId))
            {
                return false;
            }

            return Shaders.Remove(shaderId);
        }

        public static void Clear()
        {
            Shaders.Clear();
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
    }
}
