using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Presentation2D
{
    [DisallowMultipleComponent]
    public sealed class Character2DRuntime :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [SerializeField] private MonoBehaviour backendBehaviour;
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private Character2DInputDomain requestedInputs =
            Character2DInputDomain.Face |
            Character2DInputDomain.Expressions;
        [SerializeField] private string modelId =
            "character-2d";
        [SerializeField] private string modelPath;
        [SerializeField] private bool loadOnEnable = false;
        [SerializeField] private bool unloadOnDisable = true;

        private ICharacter2DBackend _backend;
        private ITrackingFrameProvider _trackingProvider;

        private TrackingFrame _lastFace;
        private TrackingFrame _lastBodyHands;
        private TrackingFrame _lastHumanoidPose;
        private TrackingFrame _lastExpressions;

        private long _applyCount;
        private long _applyFailureCount;
        private long _loadCount;
        private long _loadFailureCount;
        private string _lastError;

        public ICharacter2DBackend Backend =>
            _backend;
        public ITrackingFrameProvider TrackingProvider =>
            _trackingProvider;
        public Character2DInputDomain RequestedInputs =>
            requestedInputs;
        public Character2DInputDomain EffectiveInputs =>
            _backend != null
                ? requestedInputs &
                  _backend.SupportedInputs
                : Character2DInputDomain.None;
        public string LastError =>
            _lastError;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();

            if (loadOnEnable &&
                _backend != null &&
                _backend.Status.State !=
                    Character2DBackendState.ModelLoaded &&
                !string.IsNullOrWhiteSpace(
                    modelPath))
            {
                TryLoadConfiguredModel(
                    out _);
            }

            RefreshUpdateState();
        }

        private void OnDisable()
        {
            if (!unloadOnDisable ||
                _backend == null)
            {
                return;
            }

            try
            {
                _backend.UnloadModel();
            }
            catch (Exception exception)
            {
                _lastError =
                    "2D backend unload failed: " +
                    exception.Message;
            }

            ResetFrameCache();
        }

        private void Update()
        {
            ProcessLatest(
                out _);
        }

        public void Configure(
            MonoBehaviour backend,
            MonoBehaviour trackingProvider,
            Character2DInputDomain inputs)
        {
            backendBehaviour =
                backend;
            trackingProviderBehaviour =
                trackingProvider;
            requestedInputs =
                inputs;
            ResolveDependencies();
            ResetFrameCache();
            RefreshUpdateState();
        }

        public void ConfigureModel(
            string id,
            string path,
            bool loadWhenEnabled = false)
        {
            modelId =
                id;
            modelPath =
                path;
            loadOnEnable =
                loadWhenEnabled;
        }

        public bool TryLoadConfiguredModel(
            out string error)
        {
            return TryLoadModel(
                new Character2DModelRequest(
                    _backend?.BackendId,
                    modelId,
                    modelPath),
                out error);
        }

        public bool TryLoadModel(
            Character2DModelRequest request,
            out string error)
        {
            error = null;
            ResolveDependencies();

            if (_backend == null)
            {
                error =
                    "2D backend is unavailable.";
                return FailLoad(
                    error);
            }

            if (string.IsNullOrWhiteSpace(
                    request.BackendId) ||
                !string.Equals(
                    request.BackendId,
                    _backend.BackendId,
                    StringComparison.Ordinal))
            {
                error =
                    $"2D model request backend '{request.BackendId ?? "<null>"}' does not match active backend '{_backend.BackendId}'.";
                return FailLoad(
                    error);
            }

            if (string.IsNullOrWhiteSpace(
                    request.ModelId) ||
                string.IsNullOrWhiteSpace(
                    request.ModelPath))
            {
                error =
                    "2D model request requires a non-empty model id and path.";
                return FailLoad(
                    error);
            }

            try
            {
                if (!_backend.TryLoadModel(
                        request,
                        out error))
                {
                    return FailLoad(
                        error ??
                        "2D backend rejected the model request.");
                }
            }
            catch (Exception exception)
            {
                error =
                    "2D backend model load failed: " +
                    exception.Message;
                return FailLoad(
                    error);
            }

            modelId =
                request.ModelId;
            modelPath =
                request.ModelPath;
            _loadCount++;
            _lastError = null;
            ResetFrameCache();

            if (!enabled)
            {
                enabled = true;
            }

            RefreshUpdateState();
            return true;
        }

        public void UnloadModel()
        {
            ResolveDependencies();

            if (_backend != null)
            {
                try
                {
                    _backend.UnloadModel();
                }
                catch (Exception exception)
                {
                    _lastError =
                        "2D backend unload failed: " +
                        exception.Message;
                }
            }

            ResetFrameCache();
            RefreshUpdateState();
        }

        /// <summary>
        /// Pulls only the domains requested by this runtime and supported by the
        /// backend. The backend is not called when every returned frame is the
        /// same immutable snapshot object as the previous successful apply.
        /// </summary>
        public bool ProcessLatest(
            out string error)
        {
            error = null;
            ResolveDependencies();

            if (_backend == null ||
                _trackingProvider == null ||
                _backend.Status.State !=
                    Character2DBackendState.ModelLoaded)
            {
                return false;
            }

            var inputs =
                EffectiveInputs;

            if (inputs ==
                Character2DInputDomain.None)
            {
                return false;
            }

            TrackingFrame face = null;
            TrackingFrame bodyHands = null;
            TrackingFrame humanoidPose = null;
            TrackingFrame expressions = null;

            if ((inputs &
                 Character2DInputDomain.Face) !=
                0)
            {
                _trackingProvider
                    .TryGetLatestFace(
                        out face);
            }

            if ((inputs &
                 Character2DInputDomain.BodyHands) !=
                0)
            {
                _trackingProvider
                    .TryGetLatestBodyHands(
                        out bodyHands);
            }

            if ((inputs &
                 Character2DInputDomain.HumanoidPose) !=
                0)
            {
                _trackingProvider
                    .TryGetLatestHumanoidPose(
                        out humanoidPose);
            }

            if ((inputs &
                 Character2DInputDomain.Expressions) !=
                0)
            {
                _trackingProvider
                    .TryGetLatestExpressions(
                        out expressions);
            }

            var snapshot =
                new Character2DInputSnapshot(
                    face,
                    bodyHands,
                    humanoidPose,
                    expressions);

            if (!snapshot.HasAny ||
                IsUnchanged(
                    snapshot,
                    inputs))
            {
                return false;
            }

            try
            {
                if (!_backend.TryApply(
                        snapshot,
                        out error))
                {
                    return FailApply(
                        error ??
                        "2D backend rejected the input snapshot.");
                }
            }
            catch (Exception exception)
            {
                error =
                    "2D backend input apply failed: " +
                    exception.Message;
                return FailApply(
                    error);
            }

            Remember(
                snapshot,
                inputs);
            _applyCount++;
            _lastError = null;
            return true;
        }

        private void ResolveDependencies()
        {
            _backend =
                backendBehaviour as
                    ICharacter2DBackend;
            _trackingProvider =
                trackingProviderBehaviour as
                    ITrackingFrameProvider;
        }

        private void RefreshUpdateState()
        {
            var shouldRun =
                isActiveAndEnabled &&
                _backend != null &&
                _trackingProvider != null &&
                _backend.Status.State ==
                    Character2DBackendState.ModelLoaded &&
                EffectiveInputs !=
                    Character2DInputDomain.None;

            if (enabled != shouldRun)
            {
                enabled =
                    shouldRun;
            }
        }

        private bool IsUnchanged(
            Character2DInputSnapshot snapshot,
            Character2DInputDomain inputs)
        {
            if ((inputs &
                 Character2DInputDomain.Face) !=
                    0 &&
                !ReferenceEquals(
                    snapshot.Face,
                    _lastFace))
            {
                return false;
            }

            if ((inputs &
                 Character2DInputDomain.BodyHands) !=
                    0 &&
                !ReferenceEquals(
                    snapshot.BodyHands,
                    _lastBodyHands))
            {
                return false;
            }

            if ((inputs &
                 Character2DInputDomain.HumanoidPose) !=
                    0 &&
                !ReferenceEquals(
                    snapshot.HumanoidPose,
                    _lastHumanoidPose))
            {
                return false;
            }

            if ((inputs &
                 Character2DInputDomain.Expressions) !=
                    0 &&
                !ReferenceEquals(
                    snapshot.Expressions,
                    _lastExpressions))
            {
                return false;
            }

            return true;
        }

        private void Remember(
            Character2DInputSnapshot snapshot,
            Character2DInputDomain inputs)
        {
            if ((inputs &
                 Character2DInputDomain.Face) !=
                0)
            {
                _lastFace =
                    snapshot.Face;
            }

            if ((inputs &
                 Character2DInputDomain.BodyHands) !=
                0)
            {
                _lastBodyHands =
                    snapshot.BodyHands;
            }

            if ((inputs &
                 Character2DInputDomain.HumanoidPose) !=
                0)
            {
                _lastHumanoidPose =
                    snapshot.HumanoidPose;
            }

            if ((inputs &
                 Character2DInputDomain.Expressions) !=
                0)
            {
                _lastExpressions =
                    snapshot.Expressions;
            }
        }

        private void ResetFrameCache()
        {
            _lastFace = null;
            _lastBodyHands = null;
            _lastHumanoidPose = null;
            _lastExpressions = null;
        }

        private bool FailApply(
            string error)
        {
            _applyFailureCount++;
            _lastError =
                error;
            return false;
        }

        private bool FailLoad(
            string error)
        {
            _loadFailureCount++;
            _lastError =
                error;
            RefreshUpdateState();
            return false;
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
                    "presentation2d.model.loaded",
                    _backend != null &&
                    _backend.Status.State ==
                        Character2DBackendState.ModelLoaded
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "presentation2d.apply.count",
                    _applyCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "presentation2d.apply.failures",
                    _applyFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "presentation2d.load.count",
                    _loadCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "presentation2d.load.failures",
                    _loadFailureCount,
                    "count"));
        }
    }
}
