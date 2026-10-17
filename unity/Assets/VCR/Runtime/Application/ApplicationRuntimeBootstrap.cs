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
        private bool _lastShutdownSucceeded;
        private string _lastShutdownError;
        private bool _suppressAutomaticConfigurationSave;
        private string _configurationLoadError;
        private System.Diagnostics.Stopwatch _processExitTimer;

        private void Awake()
        {
            if (!UnityEngine.Application.isEditor)
            {
                UnityEngine.Application.wantsToQuit += OnQuitRequested;
            }
        }

        private bool OnQuitRequested()
        {
            if (_processExitTimer == null)
            {
                _processExitTimer =
                    System.Diagnostics.Stopwatch.StartNew();
                Debug.Log("VCR exit: quit requested (t=0 ms)", this);
            }

            // Observation only: another wantsToQuit subscriber may still
            // veto this request. Mark the scene as actually quitting only
            // from OnApplicationQuit, never from this cancelable event.
            return true;
        }

        public SingleCharacterSceneRuntime SceneRuntime =>
            sceneRuntime != null
                ? sceneRuntime
                : null;

        public string ConfigurationPath =>
            _configurationStore?.Path;

        // Surfaces a successful fallback to a prior configuration without
        // treating recovery as an error or hiding it from the operator.
        public string LastConfigurationLoadWarning =>
            _configurationStore?.LastLoadWarning;
        public string LastConfigurationLoadError => _configurationLoadError;
        public bool AutomaticConfigurationSaveSuppressed =>
            _suppressAutomaticConfigurationSave;

        public bool IsStarted =>
            _started;

        // Distinguish a successful normal quit from a Stopped scene that
        // nevertheless failed to save its configuration or release resources.
        public bool LastShutdownSucceeded => _lastShutdownSucceeded;
        public string LastShutdownError => _lastShutdownError;
        public long ExitElapsedMilliseconds =>
            _processExitTimer?.ElapsedMilliseconds ?? -1;

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
            _suppressAutomaticConfigurationSave = false;
            _configurationLoadError = null;

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
                        _suppressAutomaticConfigurationSave = true;
                        _configurationLoadError = loadError;
                        Debug.LogWarning(
                            "VCR configuration was not applied: " +
                            loadError +
                            ". Automatic save is disabled until the user " +
                            "explicitly saves or successfully reloads " +
                            "configuration; existing recovery files are preserved.",
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
                    _suppressAutomaticConfigurationSave = true;
                    _configurationLoadError = error;
                    return false;
                }

                sceneRuntime.ApplyConfiguration(
                    configuration);
                _suppressAutomaticConfigurationSave = false;
                _configurationLoadError = null;
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

                if (!_configurationStore.TrySave(
                        configuration,
                        out error))
                {
                    return false;
                }

                // This is an explicit save command; the operator has chosen
                // to commit current values after any failed disk recovery.
                _suppressAutomaticConfigurationSave = false;
                _configurationLoadError = null;
                return true;
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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log("VCR shutdown: begin (first=" + firstShutdown + ")", this);

            // Save only once, but always retry scene cleanup if a preceding
            // attempt reported failure. Cleanup errors must reach the caller.
            if (firstShutdown &&
                saveConfiguration &&
                _started)
            {
                if (_suppressAutomaticConfigurationSave)
                {
                    succeeded = false;
                    error =
                        "Automatic configuration save suppressed because " +
                        "the previous configuration could not be loaded. " +
                        "The original file and backup were not overwritten.";
                    Debug.LogWarning("VCR: " + error, this);
                }
                else
                {
                    try
                    {
                        if (!SaveConfiguration(out error))
                        {
                            succeeded = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        succeeded = false;
                        error = "Shutdown configuration save: " + exception;
                        Debug.LogException(exception, this);
                    }
                }
                Debug.Log("VCR shutdown: save finished at " +
                          stopwatch.ElapsedMilliseconds + " ms", this);
            }

            if (sceneRuntime != null)
            {
                try
                {
                    Debug.Log("VCR shutdown: scene teardown begin at " +
                              stopwatch.ElapsedMilliseconds + " ms", this);
                    sceneRuntime.Shutdown();

                var sceneError =
                    sceneRuntime.Status.LastError;
                    if (!string.IsNullOrWhiteSpace(sceneError))
                    {
                        succeeded = false;
                        error = string.IsNullOrWhiteSpace(error)
                            ? sceneError
                            : error + " | " + sceneError;
                    }
                }
                catch (Exception exception)
                {
                    succeeded = false;
                    error = string.IsNullOrWhiteSpace(error)
                        ? "Scene shutdown: " + exception
                        : error + " | Scene shutdown: " + exception;
                    Debug.LogException(exception, this);
                }
                finally
                {
                    Debug.Log("VCR shutdown: scene teardown ended at " +
                              stopwatch.ElapsedMilliseconds + " ms", this);
                }
            }

            _started = false;
            _lastShutdownSucceeded = succeeded;
            _lastShutdownError = error;
            Debug.Log("VCR shutdown: end at " +
                      stopwatch.ElapsedMilliseconds + " ms, success=" +
                      succeeded, this);
            return succeeded;
        }

        private void OnApplicationQuit()
        {
            if (_processExitTimer == null)
            {
                _processExitTimer =
                    System.Diagnostics.Stopwatch.StartNew();
                Debug.Log(
                    "VCR exit: OnApplicationQuit without wantsToQuit " +
                    "(t=0 ms)", this);
            }
            else
            {
                Debug.Log(
                    "VCR exit: OnApplicationQuit at " +
                    _processExitTimer.ElapsedMilliseconds +
                    " ms", this);
            }

            // Unity does not guarantee OnApplicationQuit ordering between
            // this bootstrap and the scene runtime. Signal before teardown.
            if (sceneRuntime != null)
            {
                sceneRuntime.PrepareForApplicationQuit();
            }

            try
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
            finally
            {
                Debug.Log(
                    "VCR exit: OnApplicationQuit finished at " +
                    _processExitTimer.ElapsedMilliseconds +
                    " ms", this);
            }
        }

        private void OnDestroy()
        {
            UnityEngine.Application.wantsToQuit -= OnQuitRequested;

            if (_processExitTimer != null)
            {
                Debug.Log(
                    "VCR exit: runtime bootstrap destroyed at " +
                    _processExitTimer.ElapsedMilliseconds +
                    " ms", this);
            }
        }
    }
}
