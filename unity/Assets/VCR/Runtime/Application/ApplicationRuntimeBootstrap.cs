using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VCR.Runtime.Scene;

namespace VCR.Runtime.Application
{
    /// <summary>
    /// Product application bootstrap for P1 and later.
    ///
    /// Startup order:
    /// configuration -> scene runtime -> optional character.
    ///
    /// Platform pause only suspends relaunch-sensitive presentation resources.
    /// The active character and capability registrations remain intact.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class ApplicationRuntimeBootstrap :
        MonoBehaviour
    {
        private const string VrmOption = "vcr-vrm";
        private const string ConfigOption = "vcr-config";

        [SerializeField] private SingleCharacterSceneRuntime sceneRuntime;
        [SerializeField] private bool loadSavedConfiguration = true;
        [SerializeField] private bool saveConfigurationOnQuit = true;
        [SerializeField] private string configurationFileName =
            "vcr-runtime-config.json";
        [SerializeField] private bool logStartup = true;

        private RuntimeConfigurationStore _configurationStore;
        private bool _started;
        private bool _quitting;

        public SingleCharacterSceneRuntime SceneRuntime =>
            sceneRuntime;

        public string ConfigurationPath =>
            _configurationStore?.Path;

        public bool IsStarted =>
            _started;

        private async void Start()
        {
            if (Application.isEditor)
            {
                return;
            }

            try
            {
                await StartRuntimeAsync(
                    ApplicationLaunchOptions.Parse(
                        Environment.GetCommandLineArgs()));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR application startup failed: " +
                    exception.Message,
                    this);
                Debug.LogException(
                    exception,
                    this);
            }
        }

        public async Task<bool> StartRuntimeAsync(
            ApplicationLaunchOptions options)
        {
            if (_started)
            {
                return true;
            }

            ResolveSceneRuntime();

            if (sceneRuntime == null)
            {
                throw new InvalidOperationException(
                    "SingleCharacterSceneRuntime was not found.");
            }

            options ??=
                ApplicationLaunchOptions.Parse(null);

            _configurationStore =
                new RuntimeConfigurationStore(
                    ResolveConfigurationPath(options));

            if (!sceneRuntime.Initialize())
            {
                throw new InvalidOperationException(
                    "Scene runtime initialization failed.");
            }

            if (loadSavedConfiguration &&
                !_configurationStore.TryLoad(
                    out var configuration,
                    out var loadError))
            {
                Debug.LogWarning(
                    "VCR configuration was not applied: " +
                    loadError,
                    this);
            }
            else if (loadSavedConfiguration)
            {
                sceneRuntime.ApplyConfiguration(
                    configuration);
            }

            var vrmPath =
                options.GetOrDefault(
                    VrmOption);

            if (!string.IsNullOrWhiteSpace(
                    vrmPath))
            {
                vrmPath =
                    Path.GetFullPath(vrmPath);

                var loaded =
                    await sceneRuntime.LoadCharacterAsync(
                        vrmPath);

                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        "VRM load returned no active character.");
                }
            }

            _started = true;

            if (logStartup)
            {
                Debug.Log(
                    "VCR application runtime started. " +
                    $"config='{_configurationStore.Path}', " +
                    $"character='{sceneRuntime.CurrentCharacterPath ?? "<none>"}'.",
                    this);
            }

            return true;
        }

        public bool SaveConfiguration(
            out string error)
        {
            error = null;

            if (sceneRuntime == null)
            {
                error =
                    "Scene runtime is unavailable.";
                return false;
            }

            _configurationStore ??=
                new RuntimeConfigurationStore(
                    ResolveConfigurationPath(
                        ApplicationLaunchOptions.Parse(
                            Environment.GetCommandLineArgs())));

            return _configurationStore.TrySave(
                sceneRuntime.CaptureConfiguration(),
                out error);
        }

        public void Suspend()
        {
            if (!_started ||
                _quitting)
            {
                return;
            }

            sceneRuntime?.Suspend();
        }

        public void Resume()
        {
            if (!_started ||
                _quitting)
            {
                return;
            }

            sceneRuntime?.Resume();
        }

        private string ResolveConfigurationPath(
            ApplicationLaunchOptions options)
        {
            var explicitPath =
                options.GetOrDefault(
                    ConfigOption);

            if (!string.IsNullOrWhiteSpace(
                    explicitPath))
            {
                return Path.GetFullPath(
                    explicitPath);
            }

            var fileName =
                string.IsNullOrWhiteSpace(
                    configurationFileName)
                    ? "vcr-runtime-config.json"
                    : configurationFileName;

            return Path.Combine(
                Application.persistentDataPath,
                fileName);
        }

        private void ResolveSceneRuntime()
        {
            if (sceneRuntime == null)
            {
                sceneRuntime =
                    GetComponent<
                        SingleCharacterSceneRuntime>() ??
                    FindFirstObjectByType<
                        SingleCharacterSceneRuntime>();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Suspend();
            }
            else
            {
                Resume();
            }
        }

        public bool Shutdown(
            bool saveConfiguration,
            out string error)
        {
            error = null;

            if (_quitting)
            {
                return true;
            }

            _quitting = true;

            if (saveConfiguration &&
                _started &&
                !SaveConfiguration(
                    out error))
            {
                _quitting = false;
                return false;
            }

            sceneRuntime?.Shutdown();
            _started = false;
            return true;
        }

        private void OnApplicationQuit()
        {
            if (!Shutdown(
                    saveConfigurationOnQuit,
                    out var error) &&
                !string.IsNullOrWhiteSpace(
                    error))
            {
                Debug.LogWarning(
                    "VCR configuration save on quit failed: " +
                    error,
                    this);

                sceneRuntime?.Shutdown();
                _started = false;
                _quitting = true;
            }
        }
    }
}
