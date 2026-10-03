using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VCR.Runtime.Materials.Unity
{
    /// <summary>
    /// Transactional loader for declarative shader packages.
    ///
    /// Packages may contain precompiled platform shader bundles, material
    /// presets, textures, and previews. Executable code and runtime shader
    /// source are rejected by ShaderPackageManifestValidator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeShaderPackageLoader :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        private const string ManifestFileName =
            "manifest.json";

        private const long ManifestSizeLimit =
            1L * 1024L * 1024L;
        private const long BundleSizeLimit =
            512L * 1024L * 1024L;
        private const long PresetSizeLimit =
            4L * 1024L * 1024L;
        private const long TextureSizeLimit =
            64L * 1024L * 1024L;
        private const long PreviewSizeLimit =
            64L * 1024L * 1024L;

        private readonly Dictionary<string, Shader>
            _ownedShaders =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, Shader>
            _restoreShaders =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture>
            _ownedTextures =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture>
            _restoreTextures =
                new(StringComparer.Ordinal);

        private RuntimeShaderBundleLoader _bundleLoader;

        private string _activePackageRoot;
        private string _activePackageId;
        private string _activePackageVersion;
        private MaterialPresetDocument _loadedPresetDocument;

        private long _sequence;
        private long _loadAttempts;
        private long _loadSuccesses;
        private long _loadFailures;
        private double _lastLoadMilliseconds;

        public string ActivePackageRoot =>
            _activePackageRoot;
        public string ActivePackageId =>
            _activePackageId;
        public string ActivePackageVersion =>
            _activePackageVersion;
        public MaterialPresetDocument LoadedPresetDocument =>
            _loadedPresetDocument;
        public ShaderPackageLoadStatus Status { get; private set; }

        public bool TryLoadPackage(
            string packageRoot,
            out string error)
        {
            _loadAttempts++;

            var started =
                Stopwatch.GetTimestamp();

            ShaderPackageManifest manifest = null;
            string normalizedRoot = null;
            string manifestPath = null;
            string bundlePath = null;
            string presetPath = null;
            var stagedTextures =
                new Dictionary<string, Texture2D>(
                    StringComparer.Ordinal);

            var shaderSnapshot =
                RuntimeShaderRegistry
                    .CaptureRegistered();
            var textureSnapshot =
                RuntimeTextureRegistry
                    .CaptureRegistered();

            var previousOwnedTextures =
                new List<Texture>(
                    _ownedTextures.Values);

            try
            {
                if (!TryPrepareManifest(
                        packageRoot,
                        out normalizedRoot,
                        out manifestPath,
                        out manifest,
                        out error))
                {
                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                if (!string.IsNullOrWhiteSpace(
                        manifest.UnityVersion) &&
                    !string.Equals(
                        manifest.UnityVersion,
                        Application.unityVersion,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Shader package Unity version '{manifest.UnityVersion}' does not match runtime Unity version '{Application.unityVersion}'.";

                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                if (!ShaderPackageManifestValidator
                    .TryGetBundlePath(
                        manifest,
                        RuntimeShaderBundleLoader
                            .RuntimeTargetPlatformId,
                        out var bundleRelativePath,
                        out error))
                {
                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                if (!TryPreflightDeclaredFiles(
                        normalizedRoot,
                        manifest,
                        bundleRelativePath,
                        out bundlePath,
                        out presetPath,
                        out error))
                {
                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                MaterialPresetDocument stagedPreset = null;

                if (!string.IsNullOrWhiteSpace(
                        presetPath))
                {
                    var presetStore =
                        new MaterialPresetStore(
                            presetPath);

                    if (!presetStore.TryLoad(
                            out stagedPreset,
                            out error))
                    {
                        error =
                            "Shader package material preset is invalid: " +
                            error;

                        return Fail(
                            normalizedRoot,
                            manifestPath,
                            manifest,
                            bundlePath,
                            presetPath,
                            started,
                            error);
                    }
                }

                if (!TryStageTextures(
                        normalizedRoot,
                        manifest,
                        stagedTextures,
                        out error))
                {
                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                // Expose the registry state below the currently active package
                // so a replacement package records correct restore values.
                DetachActiveRegistrations();

                var nextRestoreShaders =
                    CaptureShaderRestoreMap(
                        manifest.ShaderIds);
                var nextRestoreTextures =
                    CaptureTextureRestoreMap(
                        stagedTextures.Keys);

                EnsureBundleLoader();

                if (!_bundleLoader.TryLoadFromFile(
                        bundlePath,
                        out var registeredCount,
                        out error))
                {
                    RestoreTransaction(
                        shaderSnapshot,
                        textureSnapshot,
                        stagedTextures);

                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        "Shader package bundle load failed: " +
                        error);
                }

                if (!TryValidateLoadedShaderSet(
                        manifest.ShaderIds,
                        _bundleLoader.Status.ShaderIds,
                        registeredCount,
                        out error))
                {
                    RestoreTransaction(
                        shaderSnapshot,
                        textureSnapshot,
                        stagedTextures);

                    return Fail(
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        started,
                        error);
                }

                var nextOwnedShaders =
                    new Dictionary<string, Shader>(
                        StringComparer.Ordinal);

                foreach (var shaderId in
                         manifest.ShaderIds)
                {
                    if (!RuntimeShaderRegistry
                        .TryGetRegistered(
                            shaderId,
                            out var shader))
                    {
                        error =
                            $"Shader package loaded shader '{shaderId}' but it is not present in the runtime registry.";

                        RestoreTransaction(
                            shaderSnapshot,
                            textureSnapshot,
                            stagedTextures);

                        return Fail(
                            normalizedRoot,
                            manifestPath,
                            manifest,
                            bundlePath,
                            presetPath,
                            started,
                            error);
                    }

                    nextOwnedShaders[shaderId] =
                        shader;
                }

                foreach (var item in
                         stagedTextures)
                {
                    if (!RuntimeTextureRegistry
                        .Register(
                            item.Key,
                            item.Value))
                    {
                        error =
                            $"Shader package texture '{item.Key}' could not be registered.";

                        RestoreTransaction(
                            shaderSnapshot,
                            textureSnapshot,
                            stagedTextures);

                        return Fail(
                            normalizedRoot,
                            manifestPath,
                            manifest,
                            bundlePath,
                            presetPath,
                            started,
                            error);
                    }
                }

                CommitOwnership(
                    nextOwnedShaders,
                    nextRestoreShaders,
                    stagedTextures,
                    nextRestoreTextures);

                DestroyTextures(
                    previousOwnedTextures);

                _activePackageRoot =
                    normalizedRoot;
                _activePackageId =
                    manifest.PackageId;
                _activePackageVersion =
                    manifest.PackageVersion;
                _loadedPresetDocument =
                    stagedPreset;

                _loadSuccesses++;
                _lastLoadMilliseconds =
                    ElapsedMilliseconds(
                        started);

                Status =
                    BuildStatus(
                        true,
                        normalizedRoot,
                        manifestPath,
                        manifest,
                        bundlePath,
                        presetPath,
                        _lastLoadMilliseconds,
                        null);

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                RestoreTransaction(
                    shaderSnapshot,
                    textureSnapshot,
                    stagedTextures);

                error =
                    "Shader package load failed: " +
                    exception.Message;

                return Fail(
                    normalizedRoot,
                    manifestPath,
                    manifest,
                    bundlePath,
                    presetPath,
                    started,
                    error);
            }
        }

        public bool TryReloadActive(
            out string error)
        {
            if (string.IsNullOrWhiteSpace(
                    _activePackageRoot))
            {
                error =
                    "No active shader package is available to reload.";
                return false;
            }

            return TryLoadPackage(
                _activePackageRoot,
                out error);
        }

        public void UnloadActivePackage()
        {
            RestoreOwnedRegistrations();

            _activePackageRoot = null;
            _activePackageId = null;
            _activePackageVersion = null;
            _loadedPresetDocument = null;

            Status = default;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "shader_package.active",
                    string.IsNullOrWhiteSpace(
                        _activePackageId)
                        ? 0.0
                        : 1.0,
                    "bool"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.load_attempts",
                    _loadAttempts,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.load_successes",
                    _loadSuccesses,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.load_failures",
                    _loadFailures,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.active_shaders",
                    _ownedShaders.Count,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.active_textures",
                    _ownedTextures.Count,
                    "count"));

            output.Add(
                new RuntimeMetric(
                    "shader_package.last_load_ms",
                    _lastLoadMilliseconds,
                    "ms"));
        }

        private bool TryPrepareManifest(
            string packageRoot,
            out string normalizedRoot,
            out string manifestPath,
            out ShaderPackageManifest manifest,
            out string error)
        {
            normalizedRoot = null;
            manifestPath = null;
            manifest = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    packageRoot))
            {
                error =
                    "Shader package root is required.";
                return false;
            }

            try
            {
                normalizedRoot =
                    Path.GetFullPath(
                        packageRoot);
            }
            catch (Exception exception)
            {
                error =
                    "Shader package root is invalid: " +
                    exception.Message;
                return false;
            }

            if (!Directory.Exists(
                    normalizedRoot))
            {
                error =
                    $"Shader package directory was not found: {normalizedRoot}";
                return false;
            }

            manifestPath =
                Path.Combine(
                    normalizedRoot,
                    ManifestFileName);

            if (!TryValidateFile(
                    manifestPath,
                    ManifestSizeLimit,
                    "Shader package manifest",
                    out error))
            {
                return false;
            }

            try
            {
                manifest =
                    JsonUtility.FromJson<
                        ShaderPackageManifest>(
                        File.ReadAllText(
                            manifestPath));
            }
            catch (Exception exception)
            {
                error =
                    "Shader package manifest load failed: " +
                    exception.Message;
                return false;
            }

            if (!ShaderPackageManifestValidator
                .TryValidate(
                    manifest,
                    out error))
            {
                return false;
            }

            return true;
        }

        private static bool TryPreflightDeclaredFiles(
            string packageRoot,
            ShaderPackageManifest manifest,
            string selectedBundle,
            out string bundlePath,
            out string presetPath,
            out string error)
        {
            bundlePath = null;
            presetPath = null;
            error = null;

            if (!TryResolvePackageFile(
                    packageRoot,
                    selectedBundle,
                    BundleSizeLimit,
                    "Selected shader bundle",
                    out bundlePath,
                    out error))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    manifest.WindowsBundle) &&
                !TryResolvePackageFile(
                    packageRoot,
                    manifest.WindowsBundle,
                    BundleSizeLimit,
                    "Windows shader bundle",
                    out _,
                    out error))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    manifest.MacOSBundle) &&
                !TryResolvePackageFile(
                    packageRoot,
                    manifest.MacOSBundle,
                    BundleSizeLimit,
                    "macOS shader bundle",
                    out _,
                    out error))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    manifest.MaterialPreset))
            {
                if (!TryResolvePackageFile(
                        packageRoot,
                        manifest.MaterialPreset,
                        PresetSizeLimit,
                        "Material preset",
                        out presetPath,
                        out error))
                {
                    return false;
                }
            }

            foreach (var texture in
                     manifest.Textures)
            {
                if (!TryResolvePackageFile(
                        packageRoot,
                        texture.Path,
                        TextureSizeLimit,
                        "Texture",
                        out _,
                        out error))
                {
                    return false;
                }
            }

            foreach (var preview in
                     manifest.PreviewFiles)
            {
                if (!TryResolvePackageFile(
                        packageRoot,
                        preview,
                        PreviewSizeLimit,
                        "Preview",
                        out _,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryStageTextures(
            string packageRoot,
            ShaderPackageManifest manifest,
            Dictionary<string, Texture2D> staged,
            out string error)
        {
            error = null;

            foreach (var textureResource in
                     manifest.Textures)
            {
                if (!TryResolvePackageFile(
                        packageRoot,
                        textureResource.Path,
                        TextureSizeLimit,
                        "Texture",
                        out var texturePath,
                        out error))
                {
                    DestroyTextures(
                        staged.Values);
                    staged.Clear();
                    return false;
                }

                Texture2D texture = null;

                try
                {
                    texture =
                        new Texture2D(
                            2,
                            2,
                            TextureFormat.RGBA32,
                            mipChain: false);

                    texture.name =
                        textureResource.TextureId;

                    if (!ImageConversion.LoadImage(
                            texture,
                            File.ReadAllBytes(
                                texturePath),
                            markNonReadable: false))
                    {
                        error =
                            $"Texture '{textureResource.TextureId}' could not be decoded.";

                        DestroyTexture(
                            texture);
                        DestroyTextures(
                            staged.Values);
                        staged.Clear();
                        return false;
                    }

                    staged.Add(
                        textureResource.TextureId,
                        texture);
                }
                catch (Exception exception)
                {
                    DestroyTexture(
                        texture);
                    DestroyTextures(
                        staged.Values);
                    staged.Clear();

                    error =
                        $"Texture '{textureResource.TextureId}' load failed: {exception.Message}";
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolvePackageFile(
            string packageRoot,
            string relativePath,
            long sizeLimit,
            string label,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;

            if (!ShaderPackageManifestValidator
                .IsSafeRelativeResourcePath(
                    relativePath))
            {
                error =
                    $"{label} path '{relativePath}' is unsafe.";
                return false;
            }

            try
            {
                var normalizedRoot =
                    Path.GetFullPath(
                        packageRoot);
                var rootWithSeparator =
                    normalizedRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;

                fullPath =
                    Path.GetFullPath(
                        Path.Combine(
                            normalizedRoot,
                            relativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                var comparison =
                    IsWindowsRuntime()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;

                if (!fullPath.StartsWith(
                        rootWithSeparator,
                        comparison))
                {
                    error =
                        $"{label} resolves outside the package root.";
                    return false;
                }

                if (HasReparsePoint(
                        normalizedRoot,
                        fullPath))
                {
                    error =
                        $"{label} path uses a symbolic/reparse-point segment.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error =
                    $"{label} path resolution failed: {exception.Message}";
                return false;
            }

            return TryValidateFile(
                fullPath,
                sizeLimit,
                label,
                out error);
        }

        private static bool TryValidateFile(
            string path,
            long sizeLimit,
            string label,
            out string error)
        {
            error = null;

            if (!File.Exists(path))
            {
                error =
                    $"{label} was not found: {path}";
                return false;
            }

            try
            {
                var length =
                    new FileInfo(path).Length;

                if (length < 0 ||
                    length > sizeLimit)
                {
                    error =
                        $"{label} exceeds the {sizeLimit} byte limit.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error =
                    $"{label} metadata could not be read: {exception.Message}";
                return false;
            }

            return true;
        }

        private static bool HasReparsePoint(
            string packageRoot,
            string fullPath)
        {
            var root =
                Path.GetFullPath(
                    packageRoot)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            var current =
                Path.GetDirectoryName(
                    fullPath);

            while (!string.IsNullOrEmpty(
                       current))
            {
                if ((File.GetAttributes(
                         current) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if (string.Equals(
                        current,
                        root,
                        IsWindowsRuntime()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    break;
                }

                current =
                    Path.GetDirectoryName(
                        current);
            }

            return
                (File.GetAttributes(
                     fullPath) &
                 FileAttributes.ReparsePoint) != 0;
        }

        private static bool TryValidateLoadedShaderSet(
            string[] declared,
            string[] loaded,
            int registeredCount,
            out string error)
        {
            error = null;

            var declaredSet =
                new HashSet<string>(
                    declared ??
                    Array.Empty<string>(),
                    StringComparer.Ordinal);
            var loadedSet =
                new HashSet<string>(
                    loaded ??
                    Array.Empty<string>(),
                    StringComparer.Ordinal);

            if (registeredCount !=
                    declaredSet.Count ||
                !declaredSet.SetEquals(
                    loadedSet))
            {
                error =
                    "Loaded shader set does not exactly match the package manifest.";
                return false;
            }

            return true;
        }

        private static Dictionary<string, Shader>
            CaptureShaderRestoreMap(
                IEnumerable<string> shaderIds)
        {
            var result =
                new Dictionary<string, Shader>(
                    StringComparer.Ordinal);

            foreach (var id in shaderIds)
            {
                if (RuntimeShaderRegistry
                    .TryGetRegistered(
                        id,
                        out var shader))
                {
                    result[id] =
                        shader;
                }
            }

            return result;
        }

        private static Dictionary<string, Texture>
            CaptureTextureRestoreMap(
                IEnumerable<string> textureIds)
        {
            var result =
                new Dictionary<string, Texture>(
                    StringComparer.Ordinal);

            foreach (var id in textureIds)
            {
                if (RuntimeTextureRegistry
                    .TryGetRegistered(
                        id,
                        out var texture))
                {
                    result[id] =
                        texture;
                }
            }

            return result;
        }

        private void DetachActiveRegistrations()
        {
            foreach (var item in
                     _ownedShaders)
            {
                if (RuntimeShaderRegistry
                    .TryGetRegistered(
                        item.Key,
                        out var current) &&
                    ReferenceEquals(
                        current,
                        item.Value))
                {
                    if (_restoreShaders.TryGetValue(
                            item.Key,
                            out var restore))
                    {
                        RuntimeShaderRegistry.Register(
                            item.Key,
                            restore);
                    }
                    else
                    {
                        RuntimeShaderRegistry.Unregister(
                            item.Key);
                    }
                }
            }

            foreach (var item in
                     _ownedTextures)
            {
                if (RuntimeTextureRegistry
                    .TryGetRegistered(
                        item.Key,
                        out var current) &&
                    ReferenceEquals(
                        current,
                        item.Value))
                {
                    if (_restoreTextures.TryGetValue(
                            item.Key,
                            out var restore))
                    {
                        RuntimeTextureRegistry.Register(
                            item.Key,
                            restore);
                    }
                    else
                    {
                        RuntimeTextureRegistry.Unregister(
                            item.Key);
                    }
                }
            }
        }

        private void CommitOwnership(
            Dictionary<string, Shader> shaders,
            Dictionary<string, Shader> restoreShaders,
            Dictionary<string, Texture2D> textures,
            Dictionary<string, Texture> restoreTextures)
        {
            _ownedShaders.Clear();
            _restoreShaders.Clear();
            _ownedTextures.Clear();
            _restoreTextures.Clear();

            foreach (var item in shaders)
            {
                _ownedShaders[item.Key] =
                    item.Value;
            }

            foreach (var item in restoreShaders)
            {
                _restoreShaders[item.Key] =
                    item.Value;
            }

            foreach (var item in textures)
            {
                _ownedTextures[item.Key] =
                    item.Value;
            }

            foreach (var item in restoreTextures)
            {
                _restoreTextures[item.Key] =
                    item.Value;
            }
        }

        private void RestoreOwnedRegistrations()
        {
            DetachActiveRegistrations();

            var textures =
                new List<Texture>(
                    _ownedTextures.Values);

            _ownedShaders.Clear();
            _restoreShaders.Clear();
            _ownedTextures.Clear();
            _restoreTextures.Clear();

            DestroyTextures(
                textures);
        }

        private static void RestoreTransaction(
            KeyValuePair<string, Shader>[] shaderSnapshot,
            KeyValuePair<string, Texture>[] textureSnapshot,
            Dictionary<string, Texture2D> stagedTextures)
        {
            RuntimeShaderRegistry
                .RestoreRegistered(
                    shaderSnapshot);
            RuntimeTextureRegistry
                .RestoreRegistered(
                    textureSnapshot);

            DestroyTextures(
                stagedTextures.Values);
            stagedTextures.Clear();
        }

        private void EnsureBundleLoader()
        {
            _bundleLoader ??=
                GetComponent<
                    RuntimeShaderBundleLoader>();

            if (_bundleLoader == null)
            {
                _bundleLoader =
                    gameObject.AddComponent<
                        RuntimeShaderBundleLoader>();
            }
        }

        private bool Fail(
            string packageRoot,
            string manifestPath,
            ShaderPackageManifest manifest,
            string bundlePath,
            string presetPath,
            long started,
            string error)
        {
            _loadFailures++;
            _lastLoadMilliseconds =
                ElapsedMilliseconds(
                    started);

            Status =
                BuildStatus(
                    false,
                    packageRoot,
                    manifestPath,
                    manifest,
                    bundlePath,
                    presetPath,
                    _lastLoadMilliseconds,
                    error);

            return false;
        }

        private ShaderPackageLoadStatus BuildStatus(
            bool success,
            string packageRoot,
            string manifestPath,
            ShaderPackageManifest manifest,
            string bundlePath,
            string presetPath,
            double loadMilliseconds,
            string error)
        {
            var textureIds =
                manifest?.Textures == null
                    ? Array.Empty<string>()
                    : GetTextureIds(
                        manifest.Textures);

            return new ShaderPackageLoadStatus(
                ++_sequence,
                packageRoot,
                manifestPath,
                success,
                manifest?.PackageId,
                manifest?.PackageVersion,
                RuntimeShaderBundleLoader
                    .RuntimeTargetPlatformId,
                bundlePath,
                presetPath,
                manifest?.ShaderIds,
                textureIds,
                manifest?.UnityVersion,
                manifest?.UrpVersion,
                loadMilliseconds,
                error);
        }

        private static string[] GetTextureIds(
            ShaderPackageTextureResource[] resources)
        {
            var ids =
                new string[
                    resources.Length];

            for (var i = 0;
                 i < resources.Length;
                 i++)
            {
                ids[i] =
                    resources[i]?.TextureId;
            }

            return ids;
        }

        private static bool IsWindowsRuntime()
        {
            return
                Application.platform ==
                    RuntimePlatform.WindowsPlayer ||
                Application.platform ==
                    RuntimePlatform.WindowsEditor;
        }

        private static double ElapsedMilliseconds(
            long started)
        {
            return
                (Stopwatch.GetTimestamp() -
                 started) *
                1000.0 /
                Stopwatch.Frequency;
        }

        private static void DestroyTextures(
            IEnumerable<Texture> textures)
        {
            if (textures == null)
            {
                return;
            }

            foreach (var texture in textures)
            {
                DestroyTexture(
                    texture);
            }
        }

        private static void DestroyTexture(
            Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(
                    texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(
                    texture);
            }
        }

        private void OnDestroy()
        {
            RestoreOwnedRegistrations();
        }
    }
}
