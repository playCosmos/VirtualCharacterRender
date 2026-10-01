using System;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// P0 loader for precompiled shader assets delivered in a platform-specific
    /// AssetBundle. Loading is explicit and synchronous; there is no recurring
    /// frame work after registration. P7 may replace this with package/async
    /// orchestration without changing MaterialOverrideController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeShaderBundleLoader : MonoBehaviour
    {
        public int LoadedShaderCount { get; private set; }
        public string LastError { get; private set; }

        public bool TryLoadFromFile(
            string path,
            out int registeredCount,
            out string error)
        {
            registeredCount = 0;
            error = null;
            LastError = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                return Fail(
                    "Shader bundle path is required.",
                    out error);
            }

            path = Path.GetFullPath(path);

            if (!File.Exists(path))
            {
                return Fail(
                    $"Shader bundle was not found: {path}",
                    out error);
            }

            AssetBundle bundle = null;

            try
            {
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    return Fail(
                        "Unity could not load the shader bundle. " +
                        "Verify target platform and Unity/URP compatibility.",
                        out error);
                }

                var shaders =
                    bundle.LoadAllAssets<Shader>();

                if (shaders == null ||
                    shaders.Length == 0)
                {
                    return Fail(
                        "Shader bundle contains no Shader assets.",
                        out error);
                }

                foreach (var shader in shaders)
                {
                    if (shader == null)
                    {
                        continue;
                    }

                    if (RuntimeShaderRegistry.Register(shader))
                    {
                        registeredCount++;
                    }
                }

                if (registeredCount == 0)
                {
                    return Fail(
                        "No shader assets could be registered.",
                        out error);
                }

                LoadedShaderCount += registeredCount;
                return true;
            }
            catch (Exception exception)
            {
                return Fail(
                    exception.Message,
                    out error);
            }
            finally
            {
                // Loaded Shader objects remain alive because the registry holds
                // references. Release only the AssetBundle container metadata.
                bundle?.Unload(
                    unloadAllLoadedObjects: false);
            }
        }

        private bool Fail(
            string message,
            out string error)
        {
            LastError =
                string.IsNullOrWhiteSpace(message)
                    ? "Shader bundle load failed."
                    : message;

            error = LastError;
            return false;
        }
    }
}
