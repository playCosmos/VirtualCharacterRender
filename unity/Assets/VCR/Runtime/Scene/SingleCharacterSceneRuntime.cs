using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;
using VCR.Runtime.Capabilities;
using VCR.Runtime.Character;
using VCR.Runtime.Core;
using VCR.Runtime.Environment;
using VCR.Runtime.Output;
using VCR.Runtime.Rendering;

namespace VCR.Runtime.Scene
{
    /// <summary>
    /// P1 one-character scene lifecycle coordinator.
    ///
    /// The component owns orchestration only. Character loading remains in the
    /// character subsystem and graphics configuration remains in rendering.
    /// It has no per-frame Update loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SingleCharacterSceneRuntime :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [SerializeField] private Vrm10CharacterLoader characterLoader;
        [SerializeField] private DesktopRenderBootstrap renderBootstrap;
        [SerializeField] private PrimaryCameraController cameraController;
        [SerializeField] private PrimaryLightController lightController;
        [SerializeField] private MonoBehaviour overlayOutputBehaviour;
        [SerializeField] private MonoBehaviour environmentRuntimeBehaviour;
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool unloadCharacterOnShutdown = true;

        private CancellationTokenSource _operationCancellation;
        private int _operationGeneration;
        private SceneRuntimeState _state = SceneRuntimeState.Uninitialized;
        private string _lastError;
        private bool _applicationQuitting;
        private IOverlayOutputAdapter _overlayOutput;
        private IEnvironmentRuntime _environmentRuntime;
        private CapabilityRegistry _capabilities;
        private OverlayOutputConfiguration _overlayConfiguration =
            OverlayOutputConfiguration.Default;

        public SceneRuntimeState State => _state;
        public Vrm10Instance CurrentCharacter => characterLoader?.Current;
        public PrimaryCameraController CameraController => cameraController;
        public PrimaryLightController LightController => lightController;
        public IOverlayOutputAdapter OverlayOutput => _overlayOutput;
        public IEnvironmentRuntime EnvironmentRuntime => _environmentRuntime;
        public CapabilityRegistry Capabilities => _capabilities;
        public string CurrentCharacterPath => characterLoader?.CurrentPath;

        public SceneRuntimeConfiguration Configuration =>
            CaptureConfiguration();

        public SceneRuntimeStatus Status =>
            new(
                _state,
                CurrentCharacter != null,
                CurrentCharacterPath,
                _lastError,
                _operationGeneration);

        public event Action<SceneRuntimeStatus> StatusChanged;

        private void Awake()
        {
            if (initializeOnAwake)
            {
                Initialize();
            }
        }

        public bool Initialize()
        {
            if (_state == SceneRuntimeState.ShuttingDown ||
                _state == SceneRuntimeState.Stopped)
            {
                return false;
            }

            ResolveDependencies();

            if (characterLoader == null)
            {
                SetFault(
                    "VCR P1 scene runtime requires a Vrm10CharacterLoader.");
                return false;
            }

            if (renderBootstrap == null)
            {
                SetFault(
                    "VCR P1 scene runtime requires a DesktopRenderBootstrap.");
                return false;
            }

            _capabilities ??=
                new CapabilityRegistry();

            renderBootstrap.Apply();
            cameraController?.Apply();
            lightController?.Apply();

            _lastError = null;
            SetState(
                characterLoader.Current != null
                    ? SceneRuntimeState.CharacterReady
                    : SceneRuntimeState.Ready);
            return true;
        }

        public async Task<Vrm10Instance> LoadCharacterAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            EnsureOperational();

            var generation = BeginOperation(cancellationToken);
            var operation = _operationCancellation;

            _lastError = null;
            SetState(SceneRuntimeState.LoadingCharacter);

            try
            {
                var loaded = await characterLoader.LoadAsync(
                    path,
                    operation.Token);

                if (generation != _operationGeneration ||
                    operation.IsCancellationRequested)
                {
                    return null;
                }

                if (loaded == null)
                {
                    SetState(
                        characterLoader.Current != null
                            ? SceneRuntimeState.CharacterReady
                            : SceneRuntimeState.Ready);
                    return null;
                }

                SetState(SceneRuntimeState.CharacterReady);
                return loaded;
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                if (generation == _operationGeneration)
                {
                    _lastError = exception.Message;
                    SetState(
                        characterLoader.Current != null
                            ? SceneRuntimeState.CharacterReady
                            : SceneRuntimeState.Faulted);
                }

                throw;
            }
            finally
            {
                EndOperation(operation, generation);
            }
        }

        public Task<Vrm10Instance> ReloadCharacterAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureOperational();

            var path = characterLoader.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    "No active character path is available to reload.");
            }

            return LoadCharacterAsync(path, cancellationToken);
        }

        public void UnloadCharacter()
        {
            EnsureOperational();
            CancelActiveOperation();

            characterLoader.Unload();
            _lastError = null;
            SetState(SceneRuntimeState.Ready);
        }

        public void ApplyRenderSettings()
        {
            EnsureOperational();
            renderBootstrap.Apply();
        }

        public void ApplySceneViewSettings()
        {
            EnsureOperational();
            cameraController?.Apply();
            lightController?.Apply();
        }

        public SceneRuntimeConfiguration CaptureConfiguration()
        {
            if (renderBootstrap == null)
            {
                ResolveDependencies();
            }

            return new SceneRuntimeConfiguration
            {
                Rendering =
                    renderBootstrap != null
                        ? renderBootstrap.CaptureSettings()
                        : RenderRuntimeSettings.Default1080p,
                Camera =
                    cameraController != null
                        ? cameraController.Settings
                        : SceneCameraSettings.Default,
                Light =
                    lightController != null
                        ? lightController.Settings
                        : SceneLightSettings.DefaultDirectional,
                Overlay =
                    _overlayConfiguration,
                EnvironmentStateId =
                    _environmentRuntime?.Status.StateId
            };
        }

        public void ApplyConfiguration(
            SceneRuntimeConfiguration configuration)
        {
            EnsureOperational();

            if (_environmentRuntime != null &&
                !string.IsNullOrWhiteSpace(
                    configuration.EnvironmentStateId) &&
                !_environmentRuntime.SetState(
                    configuration.EnvironmentStateId,
                    out var environmentError))
            {
                throw new InvalidOperationException(
                    "Environment state apply failed: " +
                    environmentError);
            }

            renderBootstrap.Apply(
                configuration.Rendering);

            if (cameraController != null)
            {
                cameraController.Configure(
                    cameraController.TargetCamera,
                    configuration.Camera);
            }

            if (lightController != null)
            {
                lightController.Configure(
                    lightController.TargetLight,
                    configuration.Light);
            }

            _overlayConfiguration =
                configuration.Overlay;

            if (_overlayOutput != null)
            {
                _overlayOutput.Apply(
                    _overlayConfiguration.ToSettings());
            }
        }

        public bool SetEnvironmentState(
            string stateId,
            out string error)
        {
            EnsureOperational();

            if (_environmentRuntime == null)
            {
                error =
                    "No environment runtime is configured.";
                return false;
            }

            return _environmentRuntime.SetState(
                stateId,
                out error);
        }

        public void ApplyOverlayOutput(
            OverlayOutputSettings settings)
        {
            EnsureOperational();

            if (_overlayOutput == null)
            {
                throw new InvalidOperationException(
                    "No overlay output adapter is configured.");
            }

            _overlayConfiguration =
                OverlayOutputConfiguration
                    .FromSettings(settings);

            _overlayOutput.Apply(settings);
        }

        public void Shutdown()
        {
            if (_state == SceneRuntimeState.Stopped ||
                _state == SceneRuntimeState.ShuttingDown)
            {
                return;
            }

            SetState(SceneRuntimeState.ShuttingDown);
            CancelActiveOperation();

            _capabilities?.Dispose();
            _capabilities = null;

            if (unloadCharacterOnShutdown &&
                characterLoader != null)
            {
                characterLoader.Unload();
            }

            _overlayOutput?.Shutdown();
            lightController?.Restore();
            cameraController?.Restore();
            renderBootstrap?.RestoreRuntimeOverrides();

            _lastError = null;
            SetState(SceneRuntimeState.Stopped);
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "scene.state",
                (int)_state,
                "enum"));

            output.Add(new RuntimeMetric(
                "scene.character.loaded",
                CurrentCharacter != null ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.operation_generation",
                _operationGeneration,
                "count"));

            output.Add(new RuntimeMetric(
                "scene.camera.configured",
                cameraController != null ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.light.configured",
                lightController != null ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.output.configured",
                _overlayOutput != null ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.environment.configured",
                _environmentRuntime != null ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.capabilities.registered",
                _capabilities?.RegisteredCount ?? 0,
                "count"));

            output.Add(new RuntimeMetric(
                "scene.capabilities.enabled",
                _capabilities?.EnabledCount ?? 0,
                "count"));
        }

        private int BeginOperation(
            CancellationToken cancellationToken)
        {
            CancelActiveOperation();
            _operationGeneration++;

            _operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            return _operationGeneration;
        }

        private void EndOperation(
            CancellationTokenSource operation,
            int generation)
        {
            if (!ReferenceEquals(
                    _operationCancellation,
                    operation) ||
                generation != _operationGeneration)
            {
                operation.Dispose();
                return;
            }

            _operationCancellation = null;
            operation.Dispose();
        }

        private void CancelActiveOperation()
        {
            var operation = _operationCancellation;
            _operationCancellation = null;

            if (operation == null)
            {
                return;
            }

            try
            {
                operation.Cancel();
            }
            finally
            {
                operation.Dispose();
            }
        }

        private void ResolveDependencies()
        {
            if (characterLoader == null)
            {
                characterLoader =
                    GetComponentInChildren<Vrm10CharacterLoader>(true) ??
                    FindFirstObjectByType<Vrm10CharacterLoader>();
            }

            if (renderBootstrap == null)
            {
                renderBootstrap =
                    GetComponent<DesktopRenderBootstrap>() ??
                    FindFirstObjectByType<DesktopRenderBootstrap>();
            }

            if (cameraController == null)
            {
                cameraController =
                    GetComponentInChildren<PrimaryCameraController>(true) ??
                    FindFirstObjectByType<PrimaryCameraController>();
            }

            if (lightController == null)
            {
                lightController =
                    GetComponentInChildren<PrimaryLightController>(true) ??
                    FindFirstObjectByType<PrimaryLightController>();
            }

            ResolveOverlayOutput();
            ResolveEnvironmentRuntime();
        }

        private void ResolveEnvironmentRuntime()
        {
            if (environmentRuntimeBehaviour is
                IEnvironmentRuntime configured)
            {
                _environmentRuntime = configured;
                return;
            }

            var localBehaviours =
                GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var behaviour in localBehaviours)
            {
                if (behaviour is
                    IEnvironmentRuntime runtime)
                {
                    environmentRuntimeBehaviour = behaviour;
                    _environmentRuntime = runtime;
                    return;
                }
            }

            _environmentRuntime = null;
        }

        private void ResolveOverlayOutput()
        {
            if (overlayOutputBehaviour is
                IOverlayOutputAdapter configured)
            {
                _overlayOutput = configured;
                return;
            }

            var localBehaviours =
                GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var behaviour in localBehaviours)
            {
                if (behaviour is
                    IOverlayOutputAdapter adapter)
                {
                    overlayOutputBehaviour = behaviour;
                    _overlayOutput = adapter;
                    return;
                }
            }

            _overlayOutput = null;
        }

        private void EnsureOperational()
        {
            if (_state == SceneRuntimeState.Uninitialized)
            {
                if (!Initialize())
                {
                    throw new InvalidOperationException(
                        _lastError ??
                        "Scene runtime initialization failed.");
                }
            }

            if (_state == SceneRuntimeState.ShuttingDown ||
                _state == SceneRuntimeState.Stopped ||
                _applicationQuitting)
            {
                throw new InvalidOperationException(
                    "Scene runtime is shutting down or stopped.");
            }

            if (characterLoader == null ||
                renderBootstrap == null)
            {
                throw new InvalidOperationException(
                    "Scene runtime dependencies are unavailable.");
            }
        }

        private void SetFault(string message)
        {
            _lastError = message;
            Debug.LogError(message, this);
            SetState(SceneRuntimeState.Faulted);
        }

        private void SetState(SceneRuntimeState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StatusChanged?.Invoke(Status);
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            Shutdown();
        }

        private void OnDestroy()
        {
            CancelActiveOperation();

            _capabilities?.Dispose();
            _capabilities = null;
        }
    }
}
