using System;
using System.IO;
using System.Threading.Tasks;
using UniVRM10;
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
        private Task<bool> _startupTask;
        private bool _started;
        private bool _quitting;

        public SingleCharacterSceneRuntime SceneRuntime =>
            sceneRuntime != null
                ? sceneRuntime
                : null;

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

        public Task<bool> StartRuntimeAsync(
            ApplicationLaunchOptions options)
        {
            if (_started)
            {
                return Task.FromResult(true);
            }

            if (_quitting)
            {
                return Task.FromResult(false);
            }

            if (_startupTask != null &&
                !_startupTask.IsCompleted)
            {
                return _startupTask;
            }

            _startupTask =
                StartRuntimeCoreAsync(
                    options);
            return _startupTask;
        }

        private async Task<bool> StartRuntimeCoreAsync(
            ApplicationLaunchOptions options)
        {
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

            if (loadSavedConfiguration)
            {
                if (_configurationStore.TryLoad(
                        out var configuration,
                        out var loadError))
                {
                    sceneRuntime.ApplyConfiguration(
                        configuration);
                }
                else
                {
                    Debug.LogWarning(
                        "VCR configuration was not applied: " +
                        loadError,
                        this);
                }
            }

            var vrmPath =
                options.GetOrDefault(
                    VrmOption);

            if (!string.IsNullOrWhiteSpace(
                    vrmPath))
            {
                vrmPath =
                    Path.GetFullPath(vrmPath);

                Vrm10Instance loaded;

                try
                {
                    loaded =
                        await sceneRuntime.LoadCharacterAsync(
                            vrmPath);
                }
                catch (OperationCanceledException)
                    when (_quitting)
                {
                    return false;
                }

                if (_quitting)
                {
                    return false;
                }

                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        "VRM load returned no active character.");
                }
            }

            if (_quitting)
            {
                return false;
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

        public bool Suspend()
        {
            if (!_started ||
                _quitting ||
                sceneRuntime == null)
            {
                return false;
            }

            return sceneRuntime.Suspend();
        }

        public bool Resume()
        {
            if (!_started ||
                _quitting ||
                sceneRuntime == null)
            {
                return false;
            }

            return sceneRuntime.Resume();
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

            var saveSucceeded = true;

            if (saveConfiguration &&
                _started &&
                !SaveConfiguration(
                    out error))
            {
                saveSucceeded = false;
            }

            if (sceneRuntime != null)
            {
                sceneRuntime.Shutdown();
            }

            _started = false;
            return saveSucceeded;
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
            }
        }
    }
}
