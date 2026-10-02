using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// Loads precompiled shader assets from platform-specific AssetBundles.
    /// Loading is explicit and has no recurring frame cost after registration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeShaderBundleLoader : MonoBehaviour
    {
        private long _sequence;

        public int LoadedShaderCount { get; private set; }
        public string LastError { get; private set; }
        public ShaderBundleLoadStatus Status { get; private set; }

        public bool TryLoadFromFile(
            string path,
            out int registeredCount,
            out string error)
        {
            registeredCount = 0;
            error = null;
            LastError = null;

            var normalizedPath =
                string.IsNullOrWhiteSpace(path)
                    ? null
                    : path;

            try
            {
                if (string.IsNullOrWhiteSpace(
                        normalizedPath))
                {
                    return Fail(
                        normalizedPath,
                        "Shader bundle path is required.",
                        Array.Empty<string>(),
                        out error);
                }

                normalizedPath =
                    Path.GetFullPath(
                        normalizedPath);

                if (!File.Exists(
                        normalizedPath))
                {
                    return Fail(
                        normalizedPath,
                        $"Shader bundle was not found: {normalizedPath}",
                        Array.Empty<string>(),
                        out error);
                }

                AssetBundle bundle = null;

                try
                {
                    bundle =
                        AssetBundle.LoadFromFile(
                            normalizedPath);

                    if (bundle == null)
                    {
                        return Fail(
                            normalizedPath,
                            "Unity could not load the shader bundle. " +
                            $"platform={Application.platform}, " +
                            $"graphicsApi={SystemInfo.graphicsDeviceType}. " +
                            "Verify the bundle target platform, Unity version, URP version, and shader variants.",
                            Array.Empty<string>(),
                            out error);
                    }

                    var shaders =
                        bundle.LoadAllAssets<Shader>();

                    if (shaders == null ||
                        shaders.Length == 0)
                    {
                        return Fail(
                            normalizedPath,
                            "Shader bundle contains no Shader assets.",
                            Array.Empty<string>(),
                            out error);
                    }

                    var shaderIds =
                        new List<string>(
                            shaders.Length);

                    foreach (var shader in shaders)
                    {
                        if (shader == null)
                        {
                            continue;
                        }

                        if (!RuntimeShaderRegistry.Register(
                                shader))
                        {
                            continue;
                        }

                        registeredCount++;
                        shaderIds.Add(
                            shader.name);
                    }

                    if (registeredCount == 0)
                    {
                        return Fail(
                            normalizedPath,
                            "No shader assets could be registered.",
                            Array.Empty<string>(),
                            out error);
                    }

                    LoadedShaderCount +=
                        registeredCount;

                    Status =
                        new ShaderBundleLoadStatus(
                            ++_sequence,
                            normalizedPath,
                            success: true,
                            registeredCount,
                            shaderIds.ToArray(),
                            Application.platform.ToString(),
                            SystemInfo.graphicsDeviceType.ToString(),
                            null);

                    return true;
                }
                finally
                {
                    // Loaded Shader objects remain alive because the registry
                    // holds references. Release only bundle container metadata.
                    bundle?.Unload(
                        unloadAllLoadedObjects: false);
                }
            }
            catch (Exception exception)
            {
                return Fail(
                    normalizedPath,
                    exception.Message,
                    Array.Empty<string>(),
                    out error);
            }
        }

        private bool Fail(
            string path,
            string message,
            string[] shaderIds,
            out string error)
        {
            LastError =
                string.IsNullOrWhiteSpace(message)
                    ? "Shader bundle load failed."
                    : message;

            Status =
                new ShaderBundleLoadStatus(
                    ++_sequence,
                    path,
                    success: false,
                    registeredShaderCount: 0,
                    shaderIds,
                    Application.platform.ToString(),
                    SystemInfo.graphicsDeviceType.ToString(),
                    LastError);

            error = LastError;
            return false;
        }
    }
}
