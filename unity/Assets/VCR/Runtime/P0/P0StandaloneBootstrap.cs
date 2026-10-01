using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Materials.Unity;

namespace VCR.Runtime.P0
{
    /// <summary>
    /// Standalone-only P0 command-line bootstrap.
    ///
    /// Allows evidence/performance players to load a real VRM and, optionally,
    /// a precompiled custom-shader bundle without adding an application UI.
    /// There is no per-frame Update method.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P0StandaloneBootstrap : MonoBehaviour
    {
        private const string VrmOption = "vcr-vrm";
        private const string ShaderBundleOption =
            "vcr-shader-bundle";
        private const string ShaderIdOption =
            "vcr-shader-id";
        private const string MaterialSlotOption =
            "vcr-material-slot";

        [SerializeField] private Vrm10CharacterLoader characterLoader;
        [SerializeField] private bool logArguments = true;

        public void Configure(
            Vrm10CharacterLoader loader)
        {
            characterLoader = loader;
        }

        private async void Start()
        {
            if (Application.isEditor)
            {
                return;
            }

            var options =
                ParseOptions(
                    Environment.GetCommandLineArgs());

            if (logArguments)
            {
                Debug.Log(
                    "VCR P0 standalone options: " +
                    DescribeOptions(options),
                    this);
            }

            if (!options.TryGetValue(
                    VrmOption,
                    out var vrmPath) ||
                string.IsNullOrWhiteSpace(vrmPath))
            {
                Debug.LogWarning(
                    "VCR P0 standalone: no VRM path supplied. " +
                    "Use --vcr-vrm=<absolute path> for end-to-end evidence.",
                    this);
                return;
            }

            if (characterLoader == null)
            {
                characterLoader =
                    FindFirstObjectByType<
                        Vrm10CharacterLoader>();
            }

            if (characterLoader == null)
            {
                Debug.LogError(
                    "VCR P0 standalone: Vrm10CharacterLoader was not found.",
                    this);
                return;
            }

            try
            {
                vrmPath =
                    Path.GetFullPath(vrmPath);

                var loaded =
                    await characterLoader.LoadAsync(
                        vrmPath);

                if (loaded == null)
                {
                    Debug.LogError(
                        "VCR P0 standalone: VRM load returned no active instance.",
                        this);
                    return;
                }

                Debug.Log(
                    $"VCR P0 standalone: VRM loaded '{vrmPath}'.",
                    loaded);

                ApplyOptionalShader(
                    loaded.gameObject,
                    options);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"VCR P0 standalone: startup failed: {exception.Message}",
                    this);
                Debug.LogException(
                    exception,
                    this);
            }
        }

        private void ApplyOptionalShader(
            GameObject character,
            IReadOnlyDictionary<string, string> options)
        {
            if (!options.TryGetValue(
                    ShaderIdOption,
                    out var shaderId) ||
                string.IsNullOrWhiteSpace(shaderId))
            {
                return;
            }

            if (options.TryGetValue(
                    ShaderBundleOption,
                    out var bundlePath) &&
                !string.IsNullOrWhiteSpace(bundlePath))
            {
                var loader =
                    GetComponent<
                        RuntimeShaderBundleLoader>() ??
                    gameObject.AddComponent<
                        RuntimeShaderBundleLoader>();

                if (!loader.TryLoadFromFile(
                        bundlePath,
                        out var registered,
                        out var bundleError))
                {
                    Debug.LogError(
                        "VCR P0 standalone shader bundle: FAIL - " +
                        bundleError,
                        this);
                    return;
                }

                Debug.Log(
                    $"VCR P0 standalone shader bundle: loaded {registered} shader(s) from '{Path.GetFullPath(bundlePath)}'.",
                    this);
            }

            var controller =
                character.GetComponent<
                    MaterialOverrideController>();

            if (controller == null)
            {
                Debug.LogError(
                    "VCR P0 standalone shader: material override controller is missing.",
                    character);
                return;
            }

            controller.RefreshSlots();
            var slots =
                controller.GetSlots();

            if (slots.Length == 0)
            {
                Debug.LogError(
                    "VCR P0 standalone shader: loaded VRM exposes no material slots.",
                    character);
                return;
            }

            var slotId =
                options.TryGetValue(
                    MaterialSlotOption,
                    out var requestedSlot) &&
                !string.IsNullOrWhiteSpace(
                    requestedSlot)
                    ? requestedSlot
                    : slots[0].Id;

            if (!controller.TryApplyShaderId(
                    slotId,
                    shaderId,
                    out var error))
            {
                Debug.LogError(
                    $"VCR P0 standalone shader: FAIL slot='{slotId}', shader='{shaderId}', error='{error}'. Source material fallback remains active.",
                    character);
                return;
            }

            Debug.Log(
                $"VCR P0 standalone shader: PASS slot='{slotId}', shader='{shaderId}'.",
                character);
        }

        private static Dictionary<string, string>
            ParseOptions(string[] args)
        {
            var result =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            if (args == null)
            {
                return result;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i];
                if (string.IsNullOrWhiteSpace(raw) ||
                    !raw.StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var body = raw.Substring(2);
                var equals =
                    body.IndexOf('=');

                if (equals > 0)
                {
                    var key =
                        body.Substring(
                            0,
                            equals);
                    var value =
                        body.Substring(
                            equals + 1);

                    result[key] = value;
                    continue;
                }

                if (i + 1 < args.Length &&
                    !args[i + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    result[body] =
                        args[++i];
                }
                else
                {
                    result[body] =
                        string.Empty;
                }
            }

            return result;
        }

        private static string DescribeOptions(
            IReadOnlyDictionary<string, string> options)
        {
            var vrm =
                GetDisplayOption(
                    options,
                    VrmOption);
            var shaderBundle =
                GetDisplayOption(
                    options,
                    ShaderBundleOption);
            var shader =
                GetDisplayOption(
                    options,
                    ShaderIdOption);
            var slot =
                GetDisplayOption(
                    options,
                    MaterialSlotOption);

            return
                $"vrm={vrm}, shaderBundle={shaderBundle}, shader={shader}, materialSlot={slot}";
        }

        private static string GetDisplayOption(
            IReadOnlyDictionary<string, string> options,
            string key)
        {
            return
                options.TryGetValue(
                    key,
                    out var value) &&
                !string.IsNullOrWhiteSpace(value)
                    ? "'" + value + "'"
                    : "<none>";
        }
    }
}
