using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// Explicit one-shot loader for platform-specific shader AssetBundles.
    /// No Update/LateUpdate loop is used.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeShaderBundleLoader :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        private readonly List<AssetBundle> _bundles =
            new();

        private int _bundleLoadSuccessCount;
        private int _bundleLoadFailureCount;
        private int _registeredShaderCount;
        private string _lastError;

        public string LastError => _lastError;

        public bool TryLoad(
            string bundlePath,
            string idPrefix = null)
        {
            _lastError = null;

            if (string.IsNullOrWhiteSpace(bundlePath))
            {
                return Fail("Bundle path is required.");
            }

            bundlePath = Path.GetFullPath(bundlePath);

            if (!File.Exists(bundlePath))
            {
                return Fail(
                    "Shader bundle file was not found.");
            }

            AssetBundle bundle = null;

            try
            {
                bundle =
                    AssetBundle.LoadFromFile(bundlePath);

                if (bundle == null)
                {
                    return Fail(
                        "Unity failed to load the shader AssetBundle.");
                }

                var shaders =
                    bundle.LoadAllAssets<Shader>();

                if (shaders == null ||
                    shaders.Length == 0)
                {
                    bundle.Unload(
                        unloadAllLoadedObjects: true);
                    return Fail(
                        "Shader AssetBundle contains no Shader assets.");
                }

                foreach (var shader in shaders)
                {
                    if (shader == null)
                    {
                        continue;
                    }

                    var id =
                        string.IsNullOrWhiteSpace(idPrefix)
                            ? shader.name
                            : idPrefix + "/" + shader.name;

                    if (RuntimeShaderRegistry.Register(
                            id,
                            shader,
                            replace: true))
                    {
                        _registeredShaderCount++;
                    }
                }

                _bundles.Add(bundle);
                _bundleLoadSuccessCount++;
                return true;
            }
            catch (Exception exception)
            {
                if (bundle != null)
                {
                    bundle.Unload(
                        unloadAllLoadedObjects: true);
                }

                return Fail(exception.Message);
            }
        }

        [ContextMenu("Unload Shader Bundles")]
        public void UnloadAll()
        {
            foreach (var bundle in _bundles)
            {
                if (bundle != null)
                {
                    // Shader objects can still be referenced by runtime
                    // Materials; do not destroy loaded objects here.
                    bundle.Unload(
                        unloadAllLoadedObjects: false);
                }
            }

            _bundles.Clear();
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "render.shader.bundle_success",
                _bundleLoadSuccessCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.shader.bundle_failure",
                _bundleLoadFailureCount,
                "count"));

            output.Add(new RuntimeMetric(
                "render.shader.bundle_registered",
                _registeredShaderCount,
                "count"));
        }

        private bool Fail(string error)
        {
            _bundleLoadFailureCount++;
            _lastError =
                string.IsNullOrWhiteSpace(error)
                    ? "Unknown shader bundle failure."
                    : error;
            return false;
        }

        private void OnDestroy()
        {
            UnloadAll();
        }
    }
}
