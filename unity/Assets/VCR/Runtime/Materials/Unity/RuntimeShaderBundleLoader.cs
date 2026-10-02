using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// Loads precompiled shader assets from platform-specific AssetBundles.
    /// Loading is explicit and has no recurring frame cost after registration.
    /// Optional .vcr.json sidecar metadata is validated before AssetBundle load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeShaderBundleLoader : MonoBehaviour
    {
        private long _sequence;

        public int LoadedShaderCount { get; private set; }
        public string LastError { get; private set; }
        public ShaderBundleLoadStatus Status { get; private set; }

        public static string RuntimeTargetPlatformId =>
            Application.platform switch
            {
                RuntimePlatform.WindowsPlayer =>
                    ShaderBundleTargetPlatform.WindowsX64,
                RuntimePlatform.OSXPlayer =>
                    ShaderBundleTargetPlatform.MacOS,
                RuntimePlatform.WindowsEditor =>
                    ShaderBundleTargetPlatform.WindowsX64,
                RuntimePlatform.OSXEditor =>
                    ShaderBundleTargetPlatform.MacOS,
                _ =>
                    Application.platform.ToString()
            };

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

            ShaderBundleMetadata metadata = null;
            string metadataPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(
                        normalizedPath))
                {
                    return Fail(
                        normalizedPath,
                        "Shader bundle path is required.",
                        Array.Empty<string>(),
                        metadata,
                        metadataPath,
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
                        metadata,
                        metadataPath,
                        out error);
                }

                if (!TryLoadMetadata(
                        normalizedPath,
                        out metadata,
                        out metadataPath,
                        out var metadataError))
                {
                    return Fail(
                        normalizedPath,
                        metadataError,
                        Array.Empty<string>(),
                        metadata,
                        metadataPath,
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
                            metadata,
                            metadataPath,
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
                            metadata,
                            metadataPath,
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
                            metadata,
                            metadataPath,
                            out error);
                    }

                    if (!ValidateDeclaredShaders(
                            metadata,
                            shaderIds,
                            out var shaderDeclarationError))
                    {
                        foreach (var shaderId in
                                 shaderIds)
                        {
                            RuntimeShaderRegistry.Unregister(
                                shaderId);
                        }

                        registeredCount = 0;

                        return Fail(
                            normalizedPath,
                            shaderDeclarationError,
                            shaderIds.ToArray(),
                            metadata,
                            metadataPath,
                            out error);
                    }

                    LoadedShaderCount +=
                        registeredCount;

                    Status =
                        BuildStatus(
                            normalizedPath,
                            success: true,
                            registeredCount,
                            shaderIds.ToArray(),
                            error: null,
                            metadata,
                            metadataPath);

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
                    metadata,
                    metadataPath,
                    out error);
            }
        }

        private static bool TryLoadMetadata(
            string bundlePath,
            out ShaderBundleMetadata metadata,
            out string metadataPath,
            out string error)
        {
            metadata = null;
            metadataPath =
                bundlePath + ".vcr.json";
            error = null;

            if (!File.Exists(
                    metadataPath))
            {
                return true;
            }

            try
            {
                metadata =
                    JsonUtility.FromJson<
                        ShaderBundleMetadata>(
                        File.ReadAllText(
                            metadataPath));

                if (metadata == null)
                {
                    error =
                        "Shader bundle metadata is invalid JSON.";
                    return false;
                }

                if (metadata.FormatVersion !=
                    ShaderBundleMetadata.CurrentFormatVersion)
                {
                    error =
                        $"Shader bundle metadata format {metadata.FormatVersion} is unsupported; expected {ShaderBundleMetadata.CurrentFormatVersion}.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        metadata.TargetPlatform) &&
                    !string.Equals(
                        metadata.TargetPlatform,
                        RuntimeTargetPlatformId,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Shader bundle target '{metadata.TargetPlatform}' does not match runtime target '{RuntimeTargetPlatformId}'.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        metadata.UnityVersion) &&
                    !string.Equals(
                        metadata.UnityVersion,
                        Application.unityVersion,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Shader bundle Unity version '{metadata.UnityVersion}' does not match runtime Unity version '{Application.unityVersion}'.";
                    return false;
                }

                metadata.ShaderIds ??=
                    Array.Empty<string>();

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Shader bundle metadata load failed: " +
                    exception.Message;
                return false;
            }
        }

        private static bool ValidateDeclaredShaders(
            ShaderBundleMetadata metadata,
            List<string> loadedShaderIds,
            out string error)
        {
            error = null;

            if (metadata == null ||
                metadata.ShaderIds == null ||
                metadata.ShaderIds.Length == 0)
            {
                return true;
            }

            var loaded =
                new HashSet<string>(
                    loadedShaderIds,
                    StringComparer.Ordinal);

            foreach (var expected in
                     metadata.ShaderIds)
            {
                if (string.IsNullOrWhiteSpace(
                        expected))
                {
                    continue;
                }

                if (loaded.Contains(
                        expected))
                {
                    continue;
                }

                error =
                    $"Shader bundle metadata declares shader '{expected}', but the loaded bundle did not contain it.";
                return false;
            }

            return true;
        }

        private bool Fail(
            string path,
            string message,
            string[] shaderIds,
            ShaderBundleMetadata metadata,
            string metadataPath,
            out string error)
        {
            LastError =
                string.IsNullOrWhiteSpace(message)
                    ? "Shader bundle load failed."
                    : message;

            Status =
                BuildStatus(
                    path,
                    success: false,
                    registeredShaderCount: 0,
                    shaderIds,
                    LastError,
                    metadata,
                    metadataPath);

            error = LastError;
            return false;
        }

        private ShaderBundleLoadStatus BuildStatus(
            string path,
            bool success,
            int registeredShaderCount,
            string[] shaderIds,
            string error,
            ShaderBundleMetadata metadata,
            string metadataPath)
        {
            return new ShaderBundleLoadStatus(
                ++_sequence,
                path,
                success,
                registeredShaderCount,
                shaderIds,
                Application.platform.ToString(),
                SystemInfo.graphicsDeviceType.ToString(),
                error,
                metadataPresent:
                    metadata != null,
                metadataPath:
                    metadata != null
                        ? metadataPath
                        : null,
                bundleId:
                    metadata?.BundleId,
                targetPlatform:
                    metadata?.TargetPlatform,
                bundleUnityVersion:
                    metadata?.UnityVersion);
        }
    }
}
