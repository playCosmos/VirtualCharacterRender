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
            if (UnityEngine.Application.isEditor)
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

            var startupConfigurationStore =
                new RuntimeConfigurationStore(
                    ResolveConfigurationPath(options));

            if (!sceneRuntime.Initialize())
            {
                throw new InvalidOperationException(
                    "Scene runtime initialization failed.");
            }

            var startupBaseline =
                sceneRuntime.CaptureConfiguration();

            try
            {
                if (loadSavedConfiguration)
                {
                    if (startupConfigurationStore.TryLoad(
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

                _configurationStore =
                    startupConfigurationStore;
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
            catch (Exception startupException)
                when (!_quitting)
            {
                _started = false;

                if (!TryRestoreStartupBaseline(
                        startupBaseline,
                        out var rollbackError))
                {
                    throw new InvalidOperationException(
                        "Application runtime startup failed and rollback was incomplete: " +
                        rollbackError,
                        startupException);
                }

                throw;
            }
        }

        private bool TryRestoreStartupBaseline(
            SceneRuntimeConfiguration startupBaseline,
            out string error)
        {
            error = null;

            if (sceneRuntime == null)
            {
                error =
                    "Scene runtime is unavailable.";
                return false;
            }

            try
            {
                sceneRuntime.ApplyConfiguration(
                    startupBaseline);

                if (sceneRuntime.State ==
                        SceneRuntimeState.Faulted &&
                    !sceneRuntime.Initialize())
                {
                    error =
                        sceneRuntime.Status.LastError ??
                        "Scene runtime recovery initialization failed.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Startup baseline restore failed: " +
                    exception.Message;
                return false;
            }
        }

        public bool ReloadSavedConfiguration(
            out string error)
        {
            error = null;

            ResolveSceneRuntime();

            if (sceneRuntime == null)
            {
                error =
                    "Scene runtime is unavailable.";
                return false;
            }

            try
            {
                _configurationStore ??=
                    new RuntimeConfigurationStore(
                        ResolveConfigurationPath(
                            ApplicationLaunchOptions.Parse(
                                Environment.GetCommandLineArgs())));

                if (!_configurationStore.TryLoad(
                        out var configuration,
                        out error))
                {
                    return false;
                }

                sceneRuntime.ApplyConfiguration(
                    configuration);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Runtime configuration load/apply failed: " +
                    exception.Message;
                return false;
            }
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

            try
            {
                _configurationStore ??=
                    new RuntimeConfigurationStore(
                        ResolveConfigurationPath(
                            ApplicationLaunchOptions.Parse(
                                Environment.GetCommandLineArgs())));

                var configuration =
                    sceneRuntime.CaptureConfiguration();

                return _configurationStore.TrySave(
                    configuration,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    "Runtime configuration capture/save failed: " +
                    exception.Message;
                return false;
            }
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
                UnityEngine.Application.persistentDataPath,
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

            var firstShutdown =
                !_quitting;
            _quitting = true;

            var succeeded = true;

            if (firstShutdown &&
                saveConfiguration &&
                _started &&
                !SaveConfiguration(
                    out error))
            {
                succeeded = false;
            }

            if (sceneRuntime != null)
            {
                sceneRuntime.Shutdown();

                var sceneError =
                    sceneRuntime.Status.LastError;

                if (!string.IsNullOrWhiteSpace(
                        sceneError))
                {
                    succeeded = false;
                    error =
                        string.IsNullOrWhiteSpace(
                            error)
                            ? sceneError
                            : error +
                              " | " +
                              sceneError;
                }
            }

            _started = false;
            return succeeded;
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
                    "VCR application shutdown completed with errors: " +
                    error,
                    this);
            }
        }
    }
}
