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
        private SceneRuntimeState _stateBeforeSuspend =
            SceneRuntimeState.Ready;
        private const double OptionalServiceDiscoveryRetrySeconds = 1.0;
        private const double RenderBootstrapDiscoveryRetrySeconds = 1.0;

        private readonly List<MonoBehaviour>
            _optionalServiceBehaviourScratch =
                new();

        private IOverlayOutputAdapter _overlayOutput;
        private IEnvironmentRuntime _environmentRuntime;
        private CapabilityRegistry _capabilities;
        private OverlayOutputConfiguration _overlayConfiguration =
            OverlayOutputConfiguration.Default;
        private double _nextOverlayOutputResolveAt;
        private double _nextEnvironmentRuntimeResolveAt;
        private double _nextRenderBootstrapResolveAt;
        private long _statusSubscriberFailureCount;

        public SceneRuntimeState State => _state;
        public Vrm10Instance CurrentCharacter =>
            characterLoader != null
                ? characterLoader.Current
                : null;
        public PrimaryCameraController CameraController =>
            cameraController != null
                ? cameraController
                : null;
        public PrimaryLightController LightController =>
            lightController != null
                ? lightController
                : null;
        public IOverlayOutputAdapter OverlayOutput
        {
            get
            {
                ResolveOverlayOutput();

                return IsServiceAlive(_overlayOutput)
                    ? _overlayOutput
                    : null;
            }
        }

        public OverlayCaptureReadiness OverlayCaptureReadiness
        {
            get
            {
                ResolveOverlayOutput();

                if (!IsServiceAlive(_overlayOutput))
                {
                    return new OverlayCaptureReadiness(
                        false,
                        OverlayCaptureReadinessFailure.NotActive,
                        "No overlay output adapter is configured.");
                }

                return OverlayCaptureReadinessEvaluator.Evaluate(
                    _overlayOutput.Status,
                    _overlayOutput.Settings);
            }
        }

        public BroadcastCaptureReadiness EvaluateBroadcastCaptureTarget(
            BroadcastCaptureTarget target)
        {
            ResolveRenderBootstrap();

            if (renderBootstrap == null)
            {
                return new BroadcastCaptureReadiness(
                    false,
                    BroadcastCaptureReadinessFailure.InvalidTarget,
                    "Render bootstrap is unavailable.");
            }

            var render =
                renderBootstrap.CaptureSettings();

            return BroadcastCaptureReadinessEvaluator.Evaluate(
                target,
                OverlayCaptureReadiness,
                render.Width,
                render.Height,
                render.TargetFrameRate,
                render.RunInBackground);
        }

        public IEnvironmentRuntime EnvironmentRuntime
        {
            get
            {
                ResolveEnvironmentRuntime();

                return IsServiceAlive(_environmentRuntime)
                    ? _environmentRuntime
                    : null;
            }
        }

        public CapabilityRegistry Capabilities => _capabilities;
        public string CurrentCharacterPath =>
            characterLoader != null
                ? characterLoader.CurrentPath
                : null;

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
            if (cameraController != null)
            {
                cameraController.Apply();
            }
            if (lightController != null)
            {
                lightController.Apply();
            }

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
            var operationToken =
                operation.Token;

            _lastError = null;
            SetState(SceneRuntimeState.LoadingCharacter);

            try
            {
                var loaded = await characterLoader.LoadAsync(
                    path,
                    operationToken);

                if (generation != _operationGeneration ||
                    operationToken.IsCancellationRequested)
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
            catch (OperationCanceledException)
            {
                RestoreStateAfterCancelledCharacterLoad(
                    generation);
                throw;
            }
            catch (Exception exception)
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

        public RenderRuntimeSettings CaptureRenderSettings()
        {
            TryCaptureRenderSettings(
                out var settings);
            return settings;
        }

        public bool TryCaptureRenderSettings(
            out RenderRuntimeSettings settings)
        {
            ResolveRenderBootstrap();

            if (renderBootstrap == null)
            {
                settings =
                    RenderRuntimeSettings.Default1080p;
                return false;
            }

            settings =
                renderBootstrap.CaptureSettings();
            return true;
        }

        public void ApplyRenderSettings(
            RenderRuntimeSettings settings)
        {
            EnsureOperational();

            if (renderBootstrap == null)
            {
                throw new InvalidOperationException(
                    "Render bootstrap is unavailable.");
            }

            renderBootstrap.Apply(
                settings);
        }

        public void ApplySceneViewSettings()
        {
            EnsureOperational();
            if (cameraController != null)
            {
                cameraController.Apply();
            }
            if (lightController != null)
            {
                lightController.Apply();
            }
        }

        public SceneRuntimeConfiguration CaptureConfiguration()
        {
            ResolveDependencies();

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
                    IsServiceAlive(_environmentRuntime)
                        ? _environmentRuntime.Status.StateId
                        : null
            };
        }

        public void ApplyConfiguration(
            SceneRuntimeConfiguration configuration)
        {
            EnsureOperational();
            ResolveDependencies();

            var previousConfiguration =
                CaptureConfiguration();
            var nextOverlayConfiguration =
                configuration.Overlay;

            try
            {
                if (IsServiceAlive(_environmentRuntime) &&
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

                if (IsServiceAlive(_overlayOutput))
                {
                    _overlayOutput.Apply(
                        nextOverlayConfiguration.ToSettings());
                }

                _overlayConfiguration =
                    nextOverlayConfiguration;
            }
            catch (Exception exception)
            {
                var rollbackFailures =
                    new List<string>();

                RollbackConfiguration(
                    previousConfiguration,
                    rollbackFailures);

                if (rollbackFailures.Count == 0)
                {
                    throw;
                }

                throw new InvalidOperationException(
                    "Scene configuration apply failed and rollback was incomplete: " +
                    string.Join(
                        " | ",
                        rollbackFailures),
                    exception);
            }
        }

        private void RollbackConfiguration(
            SceneRuntimeConfiguration configuration,
            List<string> failures)
        {
            RunRollbackStep(
                "overlay",
                () =>
                {
                    if (IsServiceAlive(_overlayOutput))
                    {
                        _overlayOutput.Apply(
                            configuration.Overlay.ToSettings());
                    }

                    _overlayConfiguration =
                        configuration.Overlay;
                },
                failures);

            RunRollbackStep(
                "light",
                () =>
                {
                    if (lightController != null)
                    {
                        lightController.Configure(
                            lightController.TargetLight,
                            configuration.Light);
                    }
                },
                failures);

            RunRollbackStep(
                "camera",
                () =>
                {
                    if (cameraController != null)
                    {
                        cameraController.Configure(
                            cameraController.TargetCamera,
                            configuration.Camera);
                    }
                },
                failures);

            RunRollbackStep(
                "render",
                () =>
                    renderBootstrap?.Apply(
                        configuration.Rendering),
                failures);

            RunRollbackStep(
                "environment",
                () =>
                {
                    if (IsServiceAlive(_environmentRuntime) &&
                        !string.IsNullOrWhiteSpace(
                            configuration.EnvironmentStateId) &&
                        !_environmentRuntime.SetState(
                            configuration.EnvironmentStateId,
                            out var environmentError))
                    {
                        throw new InvalidOperationException(
                            environmentError ??
                            "environment rollback failed");
                    }
                },
                failures);
        }

        private static void RunRollbackStep(
            string label,
            Action action,
            List<string> failures)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures?.Add(
                    label + ": " +
                    exception.Message);
            }
        }

        public bool SetEnvironmentState(
            string stateId,
            out string error)
        {
            EnsureOperational();
            ResolveEnvironmentRuntime();

            if (!IsServiceAlive(_environmentRuntime))
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
            ResolveOverlayOutput();

            if (!IsServiceAlive(_overlayOutput))
            {
                throw new InvalidOperationException(
                    "No overlay output adapter is configured.");
            }

            var nextConfiguration =
                OverlayOutputConfiguration
                    .FromSettings(settings);

            _overlayOutput.Apply(settings);
            _overlayConfiguration =
                nextConfiguration;
        }

        public bool TryRecoverOverlayOutput(
            out string error)
        {
            EnsureOperational();
            ResolveOverlayOutput();

            return OverlayOutputRecovery.TryRestart(
                _overlayOutput,
                out error);
        }

        public bool TryApplyBroadcastCaptureTarget(
            BroadcastCaptureTarget target,
            out string error)
        {
            error = null;

            try
            {
                EnsureOperational();
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (renderBootstrap == null)
            {
                error =
                    "Render bootstrap is unavailable.";
                return false;
            }

            if (target.Width < 320 ||
                target.Height < 240 ||
                target.FramesPerSecond < 30 ||
                target.FramesPerSecond > 240)
            {
                error =
                    "Broadcast target must be at least 320x240 and use 30..240 FPS.";
                return false;
            }

            var settings =
                renderBootstrap.CaptureSettings();

            settings.ResolutionPreset =
                target.Tier switch
                {
                    BroadcastCaptureTargetTier.Minimum720p60
                        when target.Width == 1280 &&
                             target.Height == 720 =>
                            RenderResolutionPreset.Minimum720p,

                    BroadcastCaptureTargetTier.Recommended1080p60
                        when target.Width == 1920 &&
                             target.Height == 1080 =>
                            RenderResolutionPreset.Recommended1080p,

                    _ =>
                        RenderResolutionPreset.Custom
                };

            settings.Width = target.Width;
            settings.Height = target.Height;
            settings.TargetFrameRate =
                target.FramesPerSecond;
            settings.RunInBackground = true;

            renderBootstrap.Apply(
                settings);

            return true;
        }

        public bool Suspend()
        {
            if (_state == SceneRuntimeState.Suspended)
            {
                return true;
            }

            if (_state == SceneRuntimeState.ShuttingDown ||
                _state == SceneRuntimeState.Stopped ||
                _applicationQuitting)
            {
                return false;
            }

            if (_state == SceneRuntimeState.Uninitialized &&
                !Initialize())
            {
                return false;
            }

            _stateBeforeSuspend =
                _state switch
                {
                    SceneRuntimeState.CharacterReady =>
                        SceneRuntimeState.CharacterReady,
                    SceneRuntimeState.Faulted =>
                        SceneRuntimeState.Faulted,
                    _ =>
                        SceneRuntimeState.Ready
                };

            CancelActiveOperation();
            ResolveOverlayOutput();

            if (IsServiceAlive(_overlayOutput))
            {
                _overlayOutput.Shutdown();
            }

            SetState(SceneRuntimeState.Suspended);
            return true;
        }

        public bool Resume()
        {
            if (_state != SceneRuntimeState.Suspended ||
                _applicationQuitting)
            {
                return false;
            }

            ResolveDependencies();

            if (renderBootstrap != null)
            {
                renderBootstrap.Apply();
            }
            if (cameraController != null)
            {
                cameraController.Apply();
            }
            if (lightController != null)
            {
                lightController.Apply();
            }

            if (IsServiceAlive(_overlayOutput))
            {
                _overlayOutput.Apply(
                    _overlayConfiguration.ToSettings());
            }

            _lastError = null;
            SetState(
                CurrentCharacter != null
                    ? SceneRuntimeState.CharacterReady
                    : _stateBeforeSuspend);
            return true;
        }

        public void Shutdown()
        {
            if (_state == SceneRuntimeState.Stopped ||
                _state == SceneRuntimeState.ShuttingDown)
            {
                return;
            }

            SetState(SceneRuntimeState.ShuttingDown);

            var failures =
                new List<string>();

            RunShutdownStep(
                "active operation cancellation",
                CancelActiveOperation,
                failures);

            var capabilities =
                _capabilities;
            _capabilities = null;

            if (capabilities != null)
            {
                RunShutdownStep(
                    "capability disposal",
                    capabilities.Dispose,
                    failures);
            }

            if (unloadCharacterOnShutdown &&
                characterLoader != null)
            {
                RunShutdownStep(
                    "character unload",
                    characterLoader.Unload,
                    failures);
            }

            if (IsServiceAlive(_overlayOutput))
            {
                RunShutdownStep(
                    "overlay output shutdown",
                    _overlayOutput.Shutdown,
                    failures);
            }

            if (lightController != null)
            {
                RunShutdownStep(
                    "light restore",
                    lightController.Restore,
                    failures);
            }

            if (cameraController != null)
            {
                RunShutdownStep(
                    "camera restore",
                    cameraController.Restore,
                    failures);
            }

            if (renderBootstrap != null)
            {
                RunShutdownStep(
                    "render override restore",
                    renderBootstrap.RestoreRuntimeOverrides,
                    failures);
            }

            _lastError =
                failures.Count == 0
                    ? null
                    : "Shutdown completed with cleanup errors: " +
                      string.Join(
                          " | ",
                          failures);

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
                IsServiceAlive(_overlayOutput) ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.environment.configured",
                IsServiceAlive(_environmentRuntime) ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "scene.capabilities.registered",
                _capabilities?.RegisteredCount ?? 0,
                "count"));

            output.Add(new RuntimeMetric(
                "scene.capabilities.enabled",
                _capabilities?.EnabledCount ?? 0,
                "count"));

            output.Add(new RuntimeMetric(
                "scene.status_subscriber_failures",
                _statusSubscriberFailureCount,
                "count"));
        }

        private void RestoreStateAfterCancelledCharacterLoad(
            int generation)
        {
            if (generation !=
                    _operationGeneration ||
                _state !=
                    SceneRuntimeState.LoadingCharacter)
            {
                return;
            }

            _lastError = null;
            SetState(
                characterLoader != null &&
                characterLoader.Current != null
                    ? SceneRuntimeState.CharacterReady
                    : SceneRuntimeState.Ready);
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

            ResolveRenderBootstrap();

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

        private void ResolveRenderBootstrap()
        {
            if (renderBootstrap != null)
            {
                _nextRenderBootstrapResolveAt = 0d;
                return;
            }

            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now <
                _nextRenderBootstrapResolveAt)
            {
                return;
            }

            _nextRenderBootstrapResolveAt =
                now +
                RenderBootstrapDiscoveryRetrySeconds;

            renderBootstrap =
                GetComponent<DesktopRenderBootstrap>() ??
                FindFirstObjectByType<DesktopRenderBootstrap>();

            if (renderBootstrap != null)
            {
                _nextRenderBootstrapResolveAt = 0d;
            }
        }

        private void ResolveEnvironmentRuntime()
        {
            if (environmentRuntimeBehaviour != null &&
                environmentRuntimeBehaviour is
                    IEnvironmentRuntime configured)
            {
                _environmentRuntime = configured;
                _nextEnvironmentRuntimeResolveAt = 0d;
                return;
            }

            if (!CanRetryOptionalServiceDiscovery(
                    ref _nextEnvironmentRuntimeResolveAt))
            {
                return;
            }

            _optionalServiceBehaviourScratch.Clear();
            GetComponentsInChildren<MonoBehaviour>(
                true,
                _optionalServiceBehaviourScratch);

            MonoBehaviour matchedBehaviour = null;
            IEnvironmentRuntime matchedRuntime = null;

            foreach (var behaviour in
                     _optionalServiceBehaviourScratch)
            {
                if (behaviour is
                    IEnvironmentRuntime runtime)
                {
                    matchedBehaviour = behaviour;
                    matchedRuntime = runtime;
                    break;
                }
            }

            _optionalServiceBehaviourScratch.Clear();

            if (matchedRuntime != null)
            {
                environmentRuntimeBehaviour =
                    matchedBehaviour;
                _environmentRuntime =
                    matchedRuntime;
                _nextEnvironmentRuntimeResolveAt = 0d;
                return;
            }

            environmentRuntimeBehaviour = null;
            _environmentRuntime = null;
        }

        private void ResolveOverlayOutput()
        {
            if (overlayOutputBehaviour != null &&
                overlayOutputBehaviour is
                    IOverlayOutputAdapter configured)
            {
                _overlayOutput = configured;
                _overlayConfiguration =
                    OverlayOutputConfiguration.FromSettings(
                        configured.Settings);
                _nextOverlayOutputResolveAt = 0d;
                return;
            }

            if (!CanRetryOptionalServiceDiscovery(
                    ref _nextOverlayOutputResolveAt))
            {
                return;
            }

            _optionalServiceBehaviourScratch.Clear();
            GetComponentsInChildren<MonoBehaviour>(
                true,
                _optionalServiceBehaviourScratch);

            MonoBehaviour matchedBehaviour = null;
            IOverlayOutputAdapter matchedAdapter = null;

            foreach (var behaviour in
                     _optionalServiceBehaviourScratch)
            {
                if (behaviour is
                    IOverlayOutputAdapter adapter)
                {
                    matchedBehaviour = behaviour;
                    matchedAdapter = adapter;
                    break;
                }
            }

            _optionalServiceBehaviourScratch.Clear();

            if (matchedAdapter != null)
            {
                overlayOutputBehaviour =
                    matchedBehaviour;
                _overlayOutput =
                    matchedAdapter;
                _overlayConfiguration =
                    OverlayOutputConfiguration.FromSettings(
                        matchedAdapter.Settings);
                _nextOverlayOutputResolveAt = 0d;
                return;
            }

            overlayOutputBehaviour = null;
            _overlayOutput = null;
        }

        private static bool CanRetryOptionalServiceDiscovery(
            ref double nextRetryAt)
        {
            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now < nextRetryAt)
            {
                return false;
            }

            nextRetryAt =
                now + OptionalServiceDiscoveryRetrySeconds;
            return true;
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
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

            if (_state == SceneRuntimeState.Suspended ||
                _state == SceneRuntimeState.ShuttingDown ||
                _state == SceneRuntimeState.Stopped ||
                _applicationQuitting)
            {
                throw new InvalidOperationException(
                    "Scene runtime is suspended, shutting down, or stopped.");
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
            NotifyStatusChanged(
                Status);
        }

        private void NotifyStatusChanged(
            SceneRuntimeStatus status)
        {
            var subscribers =
                StatusChanged;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action<SceneRuntimeStatus> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(status);
                }
                catch
                {
                    _statusSubscriberFailureCount++;
                }
            }
        }

        private void RunShutdownStep(
            string label,
            Action action,
            List<string> failures)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures?.Add(
                    label + ": " +
                    exception.Message);

                Debug.LogException(
                    exception,
                    this);
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            Shutdown();
        }

        private void OnDestroy()
        {
            try
            {
                CancelActiveOperation();
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception,
                    this);
            }

            var capabilities =
                _capabilities;
            _capabilities = null;

            if (capabilities == null)
            {
                return;
            }

            try
            {
                capabilities.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(
                    exception,
                    this);
            }
        }
    }
}
