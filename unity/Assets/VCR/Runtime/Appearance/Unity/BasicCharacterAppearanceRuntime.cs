using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Appearance.Unity
{
    [DisallowMultipleComponent]
    public sealed class BasicCharacterAppearanceRuntime :
        MonoBehaviour,
        IAppearanceRuntime,
        IAppearanceUserPresetRegistry,
        IRuntimeMetricsSource
    {
        [SerializeField] private string runtimeId =
            "appearance.main";

        [SerializeField] private string defaultPresetId = "";
        [SerializeField] private bool applyDefaultOnAwake = false;

        [Header("Convention discovery")]
        [SerializeField] private bool autoDiscoverHierarchy = true;
        [SerializeField] private string appearanceRootName = "VCRAppearance";
        [SerializeField] private string outfitsRootName = "Outfits";
        [SerializeField] private string accessoriesRootName = "Accessories";

        [Header("Registered appearance")]
        [SerializeField] private AppearanceOutfitBinding[] outfits =
            Array.Empty<AppearanceOutfitBinding>();
        [SerializeField] private AppearanceAccessoryBinding[] accessories =
            Array.Empty<AppearanceAccessoryBinding>();
        [SerializeField] private AppearancePresetBinding[] presets =
            Array.Empty<AppearancePresetBinding>();

        [Header("Transitions")]
        [SerializeField] private AppearanceTransitionBinding[] transitions =
            Array.Empty<AppearanceTransitionBinding>();
        [Tooltip("Components implementing IAppearanceTransitionStepExecutor.")]
        [SerializeField] private MonoBehaviour[] transitionExecutorBehaviours =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool autoFindTransitionExecutors = true;

        private readonly Dictionary<string, AppearanceOutfitBinding>
            _outfits =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, AppearanceAccessoryBinding>
            _accessories =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AppearanceAccessoryBinding>>
            _accessoriesBySlot =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, AppearancePreset>
            _presets =
                new(StringComparer.Ordinal);
        private readonly HashSet<string> _authoredPresetIds =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, AppearancePreset>
            _userPresets =
                new(StringComparer.Ordinal);
        private readonly List<string> _userPresetIds =
            new();
        private readonly Dictionary<string, AppearanceTransitionPreset>
            _transitions =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, string>
            _currentAccessories =
                new(StringComparer.Ordinal);
        private readonly List<string> _presetIds = new();
        private readonly List<string> _transitionIds = new();
        private readonly Queue<AppearanceChangeRequest> _pending =
            new();

        private IAppearanceTransitionStepExecutor[] _executors =
            Array.Empty<IAppearanceTransitionStepExecutor>();

        private AppearanceRuntimeState _state =
            AppearanceRuntimeState.Unconfigured;
        private string _currentPresetId;
        private string _currentOutfitId;
        private string _activeTransitionId;
        private string _lastError;
        private bool _transitionCommitted;
        private double _transitionStartedAt;
        private Coroutine _transitionCoroutine;
        private AppearanceTransitionPreset _activeTransition;
        private AppearanceChangeRequest _activeRequest;

        private long _appearanceChangeCount;
        private long _transitionStartCount;
        private long _transitionCommitCount;
        private long _transitionFailureCount;
        private long _transitionQueuedCount;
        private long _transitionInterruptedCount;
        private long _transitionCancelledCount;

        public AppearanceRuntimeStatus Status
        {
            get
            {
                var duration =
                    _activeTransition?.DurationSeconds ??
                    0.0;
                var elapsed =
                    _activeTransition != null &&
                    _transitionStartedAt > 0.0
                        ? Math.Max(
                            0.0,
                            Time.unscaledTimeAsDouble -
                            _transitionStartedAt)
                        : 0.0;
                var progress =
                    _activeTransition == null
                        ? 0.0
                        : duration <= 0.0
                            ? 1.0
                            : Math.Max(
                                0.0,
                                Math.Min(
                                    1.0,
                                    elapsed /
                                    duration));

                return new AppearanceRuntimeStatus(
                    runtimeId,
                    _state,
                    _currentPresetId,
                    _currentOutfitId,
                    _activeTransitionId,
                    _transitionCommitted,
                    elapsed,
                    duration,
                    progress,
                    CanCancel(
                        _activeTransition),
                    _lastError);
            }
        }

        public AppearanceStateSnapshot Current =>
            new(
                _currentPresetId,
                _currentOutfitId,
                CaptureCurrentAccessories());

        public IReadOnlyList<string> PresetIds =>
            _presetIds;

        public IReadOnlyList<string> TransitionIds =>
            _transitionIds;

        public IReadOnlyList<string> UserPresetIds =>
            _userPresetIds;

        public event Action<AppearanceRuntimeStatus>
            StatusChanged;
        public event Action<AppearanceStateSnapshot>
            AppearanceChanged;

        private void Awake()
        {
            RebuildConfiguration(
                out var error);

            if (!string.IsNullOrWhiteSpace(error))
            {
                SetFault(error);
                return;
            }

            if (applyDefaultOnAwake &&
                !string.IsNullOrWhiteSpace(
                    defaultPresetId))
            {
                if (!SetPreset(
                        defaultPresetId,
                        null,
                        out error))
                {
                    SetFault(error);
                }
            }
        }

        private void OnDisable()
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(
                    _transitionCoroutine);
                _transitionCoroutine = null;

                TryRunCancellationCleanup(
                    _activeTransition,
                    out _);
            }

            _pending.Clear();
            ClearActiveTransition();

            if (_state !=
                AppearanceRuntimeState.Unconfigured)
            {
                SetState(
                    AppearanceRuntimeState.Ready,
                    _lastError);
            }
        }

        public bool RebuildConfiguration(
            out string error)
        {
            error = null;

            if (autoDiscoverHierarchy &&
                IsBindingConfigurationEmpty())
            {
                TryDiscoverConventionBindings(
                    out _);
            }

            _outfits.Clear();
            _accessories.Clear();
            _accessoriesBySlot.Clear();
            _presets.Clear();
            _authoredPresetIds.Clear();
            _transitions.Clear();
            _presetIds.Clear();
            _transitionIds.Clear();

            var rootOwners =
                new Dictionary<GameObject, string>();

            foreach (var binding in
                     outfits ??
                     Array.Empty<AppearanceOutfitBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.OutfitId))
                {
                    error =
                        "Every outfit binding requires a non-empty id.";
                    return false;
                }

                if (!_outfits.TryAdd(
                        binding.OutfitId,
                        binding))
                {
                    error =
                        $"Duplicate outfit id '{binding.OutfitId}'.";
                    return false;
                }

                foreach (var root in
                         binding.Roots ??
                         Array.Empty<GameObject>())
                {
                    if (root == null)
                    {
                        error =
                            $"Outfit '{binding.OutfitId}' contains a null root.";
                        return false;
                    }

                    if (rootOwners.TryGetValue(
                            root,
                            out var owner))
                    {
                        error =
                            $"Appearance root '{root.name}' is shared by '{owner}' and outfit '{binding.OutfitId}'.";
                        return false;
                    }

                    rootOwners[root] =
                        "outfit:" +
                        binding.OutfitId;
                }
            }

            foreach (var binding in
                     accessories ??
                     Array.Empty<AppearanceAccessoryBinding>())
            {
                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.SlotId) ||
                    string.IsNullOrWhiteSpace(
                        binding.AccessoryId) ||
                    binding.Root == null)
                {
                    error =
                        "Every accessory binding requires slot id, accessory id, and root.";
                    return false;
                }

                var key =
                    AccessoryKey(
                        binding.SlotId,
                        binding.AccessoryId);

                if (!_accessories.TryAdd(
                        key,
                        binding))
                {
                    error =
                        $"Duplicate accessory '{binding.SlotId}/{binding.AccessoryId}'.";
                    return false;
                }

                if (!_accessoriesBySlot.TryGetValue(
                        binding.SlotId,
                        out var slot))
                {
                    slot =
                        new List<AppearanceAccessoryBinding>();
                    _accessoriesBySlot[
                        binding.SlotId] =
                            slot;
                }

                slot.Add(binding);

                if (rootOwners.TryGetValue(
                        binding.Root,
                        out var owner))
                {
                    error =
                        $"Appearance root '{binding.Root.name}' is shared by '{owner}' and accessory '{binding.SlotId}/{binding.AccessoryId}'.";
                    return false;
                }

                rootOwners[binding.Root] =
                    "accessory:" +
                    binding.SlotId +
                    "/" +
                    binding.AccessoryId;
            }

            foreach (var binding in
                     presets ??
                     Array.Empty<AppearancePresetBinding>())
            {
                if (binding == null)
                {
                    continue;
                }

                var preset =
                    binding.ToPreset();

                if (string.IsNullOrWhiteSpace(
                        preset.Id))
                {
                    error =
                        "Every appearance preset requires a non-empty id.";
                    return false;
                }

                if (!ValidateTarget(
                        preset.OutfitId,
                        preset.Accessories,
                        out error))
                {
                    error =
                        $"Preset '{preset.Id}' is invalid: {error}";
                    return false;
                }

                if (!_presets.TryAdd(
                        preset.Id,
                        preset))
                {
                    error =
                        $"Duplicate appearance preset id '{preset.Id}'.";
                    return false;
                }

                _presetIds.Add(
                    preset.Id);
                _authoredPresetIds.Add(
                    preset.Id);
            }

            if (!string.IsNullOrWhiteSpace(
                    defaultPresetId) &&
                !_presets.ContainsKey(
                    defaultPresetId))
            {
                error =
                    $"Default appearance preset '{defaultPresetId}' is not registered.";
                return false;
            }

            foreach (var binding in
                     transitions ??
                     Array.Empty<AppearanceTransitionBinding>())
            {
                if (binding == null)
                {
                    continue;
                }

                var transition =
                    binding.ToPreset();

                if (!ValidateTransitionDefinition(
                        transition,
                        out error))
                {
                    return false;
                }

                if (!_transitions.TryAdd(
                        transition.Id,
                        transition))
                {
                    error =
                        $"Duplicate appearance transition id '{transition.Id}'.";
                    return false;
                }

                _transitionIds.Add(
                    transition.Id);
            }

            foreach (var preset in
                     _presets.Values)
            {
                if (!ValidatePreferredTransition(
                        preset,
                        out error))
                {
                    return false;
                }
            }

            foreach (var presetId in
                     _userPresetIds)
            {
                if (!_userPresets.TryGetValue(
                        presetId,
                        out var userPreset))
                {
                    continue;
                }

                if (_presets.ContainsKey(
                        presetId))
                {
                    error =
                        $"User appearance preset '{presetId}' conflicts with an authored preset.";
                    return false;
                }

                if (!ValidateUserPreset(
                        userPreset,
                        out error))
                {
                    error =
                        $"User preset '{presetId}' is invalid: {error}";
                    return false;
                }

                _presets.Add(
                    presetId,
                    ClonePreset(
                        userPreset));
                _presetIds.Add(
                    presetId);
            }

            // Preserve authoring order for previous/next quick-change UI.
            RebuildExecutors();

            var configured =
                _outfits.Count > 0 ||
                _accessories.Count > 0 ||
                _presets.Count > 0;

            SetState(
                configured
                    ? AppearanceRuntimeState.Ready
                    : AppearanceRuntimeState.Unconfigured,
                null);

            return true;
        }

        public void ConfigureBindings(
            AppearanceOutfitBinding[] nextOutfits,
            AppearanceAccessoryBinding[] nextAccessories,
            AppearancePresetBinding[] nextPresets,
            AppearanceTransitionBinding[] nextTransitions,
            MonoBehaviour[] executors,
            string nextDefaultPresetId = null)
        {
            outfits =
                nextOutfits ??
                Array.Empty<AppearanceOutfitBinding>();
            accessories =
                nextAccessories ??
                Array.Empty<AppearanceAccessoryBinding>();
            presets =
                nextPresets ??
                Array.Empty<AppearancePresetBinding>();
            transitions =
                nextTransitions ??
                Array.Empty<AppearanceTransitionBinding>();
            transitionExecutorBehaviours =
                executors ??
                Array.Empty<MonoBehaviour>();

            if (nextDefaultPresetId != null)
            {
                defaultPresetId =
                    nextDefaultPresetId;
            }

            if (!RebuildConfiguration(
                    out var error))
            {
                SetFault(error);
            }
        }

        public bool SaveCurrentAsUserPreset(
            string presetId,
            string preferredTransitionId,
            out AppearancePreset preset,
            out string error)
        {
            preset = null;
            error = null;

            if (_state !=
                AppearanceRuntimeState.Ready)
            {
                error =
                    "Appearance runtime must be ready before saving a user preset.";
                return false;
            }

            var id =
                presetId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    id))
            {
                error =
                    "User appearance preset id is required.";
                return false;
            }

            if (_authoredPresetIds.Contains(
                    id))
            {
                error =
                    $"User preset '{id}' cannot replace an authored appearance preset.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    _currentOutfitId) &&
                _currentAccessories.Count == 0)
            {
                error =
                    "No active appearance is available to save.";
                return false;
            }

            var next =
                new AppearancePreset
                {
                    Id = id,
                    OutfitId =
                        _currentOutfitId,
                    PreferredTransitionId =
                        NormalizePreferredTransition(
                            preferredTransitionId),
                    Accessories =
                        CaptureCurrentAccessories()
                };

            if (!ValidateUserPreset(
                    next,
                    out error))
            {
                return false;
            }

            UpsertUserPreset(
                next);

            preset =
                ClonePreset(
                    next);

            SetState(
                _state,
                _lastError);
            return true;
        }

        public bool ReplaceUserPresets(
            IReadOnlyList<AppearancePreset> nextPresets,
            out string error)
        {
            error = null;

            if (_state ==
                    AppearanceRuntimeState.Transitioning ||
                _state ==
                    AppearanceRuntimeState.Committing)
            {
                error =
                    "User presets cannot be replaced during an appearance transition.";
                return false;
            }

            var validated =
                new Dictionary<string, AppearancePreset>(
                    StringComparer.Ordinal);
            var order =
                new List<string>();

            if (nextPresets != null)
            {
                for (var i = 0;
                     i < nextPresets.Count;
                     i++)
                {
                    var candidate =
                        nextPresets[i];

                    if (!ValidateUserPreset(
                            candidate,
                            out error))
                    {
                        return false;
                    }

                    if (_authoredPresetIds.Contains(
                            candidate.Id))
                    {
                        error =
                            $"User preset '{candidate.Id}' cannot replace an authored appearance preset.";
                        return false;
                    }

                    if (!validated.TryAdd(
                            candidate.Id,
                            ClonePreset(
                                candidate)))
                    {
                        error =
                            $"Duplicate user appearance preset id '{candidate.Id}'.";
                        return false;
                    }

                    order.Add(
                        candidate.Id);
                }
            }

            foreach (var id in
                     _userPresetIds)
            {
                _presets.Remove(
                    id);
                _presetIds.Remove(
                    id);
            }

            _userPresets.Clear();
            _userPresetIds.Clear();

            foreach (var id in order)
            {
                var candidate =
                    validated[id];

                _userPresets.Add(
                    id,
                    candidate);
                _userPresetIds.Add(
                    id);
                _presets.Add(
                    id,
                    ClonePreset(
                        candidate));
                _presetIds.Add(
                    id);
            }

            if (!string.IsNullOrWhiteSpace(
                    _currentPresetId) &&
                !_presets.ContainsKey(
                    _currentPresetId))
            {
                _currentPresetId = null;
            }

            SetState(
                _state ==
                    AppearanceRuntimeState.Unconfigured &&
                _presets.Count > 0
                    ? AppearanceRuntimeState.Ready
                    : _state,
                _lastError);
            return true;
        }

        public bool RemoveUserPreset(
            string presetId,
            out string error)
        {
            error = null;
            var id =
                presetId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    id) ||
                !_userPresets.Remove(
                    id))
            {
                error =
                    $"Unknown user appearance preset '{presetId ?? "<null>"}'.";
                return false;
            }

            _userPresetIds.Remove(
                id);
            _presets.Remove(
                id);
            _presetIds.Remove(
                id);

            if (string.Equals(
                    _currentPresetId,
                    id,
                    StringComparison.Ordinal))
            {
                _currentPresetId = null;
                AppearanceChanged?.Invoke(
                    Current);
            }

            SetState(
                _state,
                _lastError);
            return true;
        }

        public AppearancePreset[] CaptureUserPresets()
        {
            var result =
                new AppearancePreset[
                    _userPresetIds.Count];

            for (var i = 0;
                 i < _userPresetIds.Count;
                 i++)
            {
                result[i] =
                    ClonePreset(
                        _userPresets[
                            _userPresetIds[i]]);
            }

            return result;
        }

        public bool SetPreset(
            string presetId,
            string transitionId,
            out string error)
        {
            error = null;

            if (!_presets.TryGetValue(
                    presetId ?? string.Empty,
                    out var preset))
            {
                error =
                    $"Unknown appearance preset '{presetId ?? "<null>"}'.";
                return false;
            }

            var selectedTransition =
                string.IsNullOrWhiteSpace(
                    transitionId)
                    ? preset.PreferredTransitionId
                    : transitionId;

            return Submit(
                new AppearanceChangeRequest(
                    preset.Id,
                    preset.OutfitId,
                    CopySelections(
                        preset.Accessories),
                    selectedTransition),
                out error);
        }

        public bool SetOutfit(
            string outfitId,
            string transitionId,
            out string error)
        {
            if (!_outfits.ContainsKey(
                    outfitId ?? string.Empty))
            {
                error =
                    $"Unknown outfit '{outfitId ?? "<null>"}'.";
                return false;
            }

            return Submit(
                new AppearanceChangeRequest(
                    null,
                    outfitId,
                    CaptureCurrentAccessories(),
                    transitionId),
                out error);
        }

        public bool SetAccessory(
            string slotId,
            string accessoryId,
            string transitionId,
            out string error)
        {
            error = null;

            if (!_accessories.ContainsKey(
                    AccessoryKey(
                        slotId,
                        accessoryId)))
            {
                error =
                    $"Unknown accessory '{slotId ?? "<null>"}/{accessoryId ?? "<null>"}'.";
                return false;
            }

            var selections =
                CaptureCurrentAccessoryMap();
            selections[slotId] =
                accessoryId;

            return Submit(
                new AppearanceChangeRequest(
                    null,
                    _currentOutfitId,
                    ToSelections(selections),
                    transitionId),
                out error);
        }

        public bool ClearAccessory(
            string slotId,
            string transitionId,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    slotId) ||
                !_accessoriesBySlot.ContainsKey(
                    slotId))
            {
                error =
                    $"Unknown accessory slot '{slotId ?? "<null>"}'.";
                return false;
            }

            var selections =
                CaptureCurrentAccessoryMap();
            selections.Remove(slotId);

            return Submit(
                new AppearanceChangeRequest(
                    null,
                    _currentOutfitId,
                    ToSelections(selections),
                    transitionId),
                out error);
        }

        public bool RestoreDefault(
            string transitionId,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(
                    defaultPresetId))
            {
                error =
                    "No default appearance preset is configured.";
                return false;
            }

            return SetPreset(
                defaultPresetId,
                transitionId,
                out error);
        }

        public bool CancelTransition(
            out string error)
        {
            return CancelActiveTransition(
                interrupted: false,
                clearPending: true,
                out error);
        }

        private bool Submit(
            AppearanceChangeRequest request,
            out string error)
        {
            error = null;

            if (_state ==
                AppearanceRuntimeState.Unconfigured)
            {
                error =
                    "Appearance runtime is not configured.";
                return false;
            }

            if (!ValidateTarget(
                    request.OutfitId,
                    request.Accessories,
                    out error))
            {
                return false;
            }

            if (_transitionCoroutine != null)
            {
                return QueueWhileBusy(
                    request,
                    out error);
            }

            return StartRequest(
                request,
                out error);
        }

        private bool QueueWhileBusy(
            AppearanceChangeRequest request,
            out string error)
        {
            error = null;

            var policy =
                _activeTransition?.QueuePolicy ??
                AppearanceTransitionQueuePolicy
                    .QueueLatest;

            switch (policy)
            {
                case AppearanceTransitionQueuePolicy
                    .IgnoreWhileBusy:
                    error =
                        "Appearance transition is busy.";
                    return false;

                case AppearanceTransitionQueuePolicy
                    .QueueAll:
                    _pending.Enqueue(request);
                    _transitionQueuedCount++;
                    return true;

                case AppearanceTransitionQueuePolicy
                    .Interrupt:
                    if (!CancelActiveTransition(
                            interrupted: true,
                            clearPending: true,
                            out error))
                    {
                        return false;
                    }

                    return StartRequest(
                        request,
                        out error);

                default:
                    _pending.Clear();
                    _pending.Enqueue(request);
                    _transitionQueuedCount++;
                    return true;
            }
        }

        private bool StartRequest(
            AppearanceChangeRequest request,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    request.TransitionId) ||
                string.Equals(
                    request.TransitionId,
                    "Immediate",
                    StringComparison.OrdinalIgnoreCase))
            {
                return CommitRequest(
                    request,
                    out error);
            }

            if (!_transitions.TryGetValue(
                    request.TransitionId,
                    out var transition))
            {
                error =
                    $"Unknown appearance transition '{request.TransitionId}'.";
                return false;
            }

            if (!ValidateRequiredExecutors(
                    transition,
                    out error))
            {
                if (transition.FallbackPolicy ==
                    AppearanceTransitionFallbackPolicy
                        .Immediate)
                {
                    return CommitRequest(
                        request,
                        out error);
                }

                return false;
            }

            _activeRequest = request;
            _activeTransition = transition;
            _activeTransitionId =
                transition.Id;
            _transitionCommitted = false;
            _transitionStartedAt =
                Time.unscaledTimeAsDouble;
            _lastError = null;
            _transitionStartCount++;

            SetState(
                AppearanceRuntimeState.Transitioning,
                null);

            _transitionCoroutine =
                StartCoroutine(
                    RunTransition(
                        request,
                        transition));
            return true;
        }

        private IEnumerator RunTransition(
            AppearanceChangeRequest request,
            AppearanceTransitionPreset transition)
        {
            var steps =
                (AppearanceTransitionStep[])
                    transition.Steps.Clone();

            if (!TryBuildMarkerTimes(
                    transition,
                    out var markerTimes,
                    out var markerError))
            {
                _transitionFailureCount++;
                FinishTransition(
                    markerError);
                yield break;
            }

            var started =
                Time.unscaledTimeAsDouble;
            var executedActions =
                new Dictionary<string, AppearanceTransitionStep>(
                    StringComparer.Ordinal);

            foreach (var step in steps)
            {
                if (!TryResolveStepTime(
                        step,
                        markerTimes,
                        out var scheduledTime,
                        out var timingError))
                {
                    _transitionFailureCount++;
                    FinishTransition(
                        timingError);
                    yield break;
                }

                while (Time.unscaledTimeAsDouble -
                       started <
                       scheduledTime)
                {
                    yield return null;
                }

                if (step.DependencyMode !=
                    AppearanceTransitionDependencyMode.None)
                {
                    var dependencyStarted =
                        Time.unscaledTimeAsDouble;

                    while (true)
                    {
                        if (!TryEvaluateDependencies(
                                step,
                                executedActions,
                                out var dependenciesSatisfied,
                                out var dependencyError))
                        {
                            if (HandleTransitionStepFailure(
                                    request,
                                    transition,
                                    step,
                                    dependencyError))
                            {
                                yield break;
                            }

                            break;
                        }

                        if (dependenciesSatisfied)
                        {
                            break;
                        }

                        if (Time.unscaledTimeAsDouble -
                                dependencyStarted >=
                            step.DependencyTimeoutSeconds)
                        {
                            var timeoutError =
                                $"Transition step '{DescribeStep(step)}' dependencies did not satisfy within {step.DependencyTimeoutSeconds:0.###} seconds.";

                            if (HandleTransitionStepFailure(
                                    request,
                                    transition,
                                    step,
                                    timeoutError))
                            {
                                yield break;
                            }

                            break;
                        }

                        yield return null;
                    }
                }

                if (step.Kind ==
                    AppearanceTransitionStepKind.Commit)
                {
                    SetState(
                        AppearanceRuntimeState.Committing,
                        null);

                    if (!CommitRequest(
                            request,
                            out var commitError,
                            preserveTransition: true))
                    {
                        _transitionFailureCount++;
                        FinishTransition(
                            commitError);
                        yield break;
                    }

                    _transitionCommitted = true;
                    _transitionCommitCount++;

                    SetState(
                        AppearanceRuntimeState.Transitioning,
                        null);
                    continue;
                }

                if (!TryExecuteStep(
                        step,
                        out var stepError))
                {
                    if (HandleTransitionStepFailure(
                            request,
                            transition,
                            step,
                            stepError))
                    {
                        yield break;
                    }

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(
                        step.StepId))
                {
                    executedActions[
                        step.StepId] =
                            step;
                }

                if (!step.Blocking)
                {
                    continue;
                }

                var completionStarted =
                    Time.unscaledTimeAsDouble;

                while (true)
                {
                    if (!TryIsStepComplete(
                            step,
                            out var complete,
                            out var completionError))
                    {
                        if (HandleTransitionStepFailure(
                                request,
                                transition,
                                step,
                                completionError))
                        {
                            yield break;
                        }

                        break;
                    }

                    if (complete)
                    {
                        break;
                    }

                    if (Time.unscaledTimeAsDouble -
                            completionStarted >=
                        step.CompletionTimeoutSeconds)
                    {
                        var timeoutError =
                            $"Transition action '{step.ActionType}' did not complete within {step.CompletionTimeoutSeconds:0.###} seconds.";

                        if (HandleTransitionStepFailure(
                                request,
                                transition,
                                step,
                                timeoutError))
                        {
                            yield break;
                        }

                        break;
                    }

                    yield return null;
                }
            }

            var remaining =
                transition.DurationSeconds -
                (Time.unscaledTimeAsDouble -
                 started);

            while (remaining > 0.0)
            {
                yield return null;
                remaining =
                    transition.DurationSeconds -
                    (Time.unscaledTimeAsDouble -
                     started);
            }

            FinishTransition(
                _lastError);
        }

        private bool HandleTransitionStepFailure(
            AppearanceChangeRequest request,
            AppearanceTransitionPreset transition,
            AppearanceTransitionStep step,
            string stepError)
        {
            if (!step.Required)
            {
                _lastError =
                    stepError;
                return false;
            }

            _transitionFailureCount++;

            var cleanupSucceeded =
                TryRunCancellationCleanup(
                    transition,
                    out var cleanupError);

            if (!cleanupSucceeded)
            {
                FinishTransition(
                    cleanupError);
                return true;
            }

            if (!_transitionCommitted &&
                transition.FallbackPolicy ==
                    AppearanceTransitionFallbackPolicy
                        .Immediate)
            {
                if (!CommitRequest(
                        request,
                        out var fallbackError,
                        preserveTransition: true))
                {
                    FinishTransition(
                        fallbackError);
                    return true;
                }

                _transitionCommitted = true;
                _transitionCommitCount++;
                FinishTransition(
                    stepError);
                return true;
            }

            FinishTransition(
                stepError);
            return true;
        }

        private bool CommitRequest(
            AppearanceChangeRequest request,
            out string error,
            bool preserveTransition = false)
        {
            error = null;

            if (!TryApplyTarget(
                    request,
                    out error))
            {
                SetFault(error);
                return false;
            }

            _currentPresetId =
                request.PresetId;
            _currentOutfitId =
                request.OutfitId;

            _currentAccessories.Clear();

            foreach (var selection in
                     request.Accessories ??
                     Array.Empty<
                         AppearanceAccessorySelection>())
            {
                if (selection == null ||
                    string.IsNullOrWhiteSpace(
                        selection.SlotId) ||
                    string.IsNullOrWhiteSpace(
                        selection.AccessoryId))
                {
                    continue;
                }

                _currentAccessories[
                    selection.SlotId] =
                        selection.AccessoryId;
            }

            _appearanceChangeCount++;
            _lastError = null;

            AppearanceChanged?.Invoke(
                Current);

            if (!preserveTransition)
            {
                SetState(
                    AppearanceRuntimeState.Ready,
                    null);
            }

            return true;
        }

        private bool TryApplyTarget(
            AppearanceChangeRequest request,
            out string error)
        {
            error = null;

            if (!ValidateTarget(
                    request.OutfitId,
                    request.Accessories,
                    out error))
            {
                return false;
            }

            var desired =
                new Dictionary<GameObject, bool>();

            foreach (var binding in
                     _outfits.Values)
            {
                foreach (var root in
                         binding.Roots ??
                         Array.Empty<GameObject>())
                {
                    desired[root] = false;
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    request.OutfitId))
            {
                foreach (var root in
                         _outfits[
                             request.OutfitId]
                             .Roots ??
                         Array.Empty<GameObject>())
                {
                    desired[root] = true;
                }
            }

            foreach (var binding in
                     _accessories.Values)
            {
                desired[
                    binding.Root] =
                        false;
            }

            foreach (var selection in
                     request.Accessories ??
                     Array.Empty<
                         AppearanceAccessorySelection>())
            {
                if (selection == null)
                {
                    continue;
                }

                if (_accessories.TryGetValue(
                        AccessoryKey(
                            selection.SlotId,
                            selection.AccessoryId),
                        out var binding))
                {
                    desired[
                        binding.Root] =
                            true;
                }
            }

            var previous =
                new Dictionary<GameObject, bool>(
                    desired.Count);

            try
            {
                foreach (var pair in desired)
                {
                    previous[
                        pair.Key] =
                            pair.Key.activeSelf;
                    pair.Key.SetActive(
                        pair.Value);
                }

                return true;
            }
            catch (Exception exception)
            {
                foreach (var pair in previous)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetActive(
                            pair.Value);
                    }
                }

                error =
                    "Appearance apply failed and was rolled back: " +
                    exception.Message;
                return false;
            }
        }

        public bool TryDiscoverConventionBindings(
            out string error)
        {
            error = null;

            var appearanceRoot =
                transform.Find(
                    appearanceRootName);

            if (appearanceRoot == null)
            {
                error =
                    $"Convention appearance root '{appearanceRootName}' was not found.";
                return false;
            }

            var discoveredOutfits =
                new List<AppearanceOutfitBinding>();
            var discoveredAccessories =
                new List<AppearanceAccessoryBinding>();
            var discoveredPresets =
                new List<AppearancePresetBinding>();

            var outfitRoot =
                appearanceRoot.Find(
                    outfitsRootName);

            if (outfitRoot != null)
            {
                for (var i = 0;
                     i < outfitRoot.childCount;
                     i++)
                {
                    var child =
                        outfitRoot.GetChild(i);

                    if (string.IsNullOrWhiteSpace(
                            child.name))
                    {
                        continue;
                    }

                    discoveredOutfits.Add(
                        new AppearanceOutfitBinding
                        {
                            OutfitId = child.name,
                            Roots =
                                new[]
                                {
                                    child.gameObject
                                }
                        });

                    discoveredPresets.Add(
                        new AppearancePresetBinding
                        {
                            PresetId = child.name,
                            OutfitId = child.name,
                            Accessories =
                                Array.Empty<
                                    AppearanceAccessorySelectionBinding>()
                        });

                    if (string.IsNullOrWhiteSpace(
                            defaultPresetId) &&
                        child.gameObject.activeSelf)
                    {
                        defaultPresetId =
                            child.name;
                    }
                }
            }

            var accessoryRoot =
                appearanceRoot.Find(
                    accessoriesRootName);

            if (accessoryRoot != null)
            {
                for (var slotIndex = 0;
                     slotIndex < accessoryRoot.childCount;
                     slotIndex++)
                {
                    var slot =
                        accessoryRoot.GetChild(
                            slotIndex);

                    if (string.IsNullOrWhiteSpace(
                            slot.name))
                    {
                        continue;
                    }

                    for (var itemIndex = 0;
                         itemIndex < slot.childCount;
                         itemIndex++)
                    {
                        var item =
                            slot.GetChild(
                                itemIndex);

                        if (string.IsNullOrWhiteSpace(
                                item.name))
                        {
                            continue;
                        }

                        discoveredAccessories.Add(
                            new AppearanceAccessoryBinding
                            {
                                SlotId = slot.name,
                                AccessoryId = item.name,
                                Root = item.gameObject
                            });
                    }
                }
            }

            if (discoveredOutfits.Count == 0 &&
                discoveredAccessories.Count == 0)
            {
                error =
                    $"No convention bindings were found under '{appearanceRootName}'.";
                return false;
            }

            outfits =
                discoveredOutfits.ToArray();
            accessories =
                discoveredAccessories.ToArray();
            presets =
                discoveredPresets.ToArray();

            if (string.IsNullOrWhiteSpace(
                    defaultPresetId) &&
                discoveredPresets.Count > 0)
            {
                defaultPresetId =
                    discoveredPresets[0]
                        .PresetId;
            }

            return true;
        }

        private bool IsBindingConfigurationEmpty()
        {
            return
                (outfits == null ||
                 outfits.Length == 0) &&
                (accessories == null ||
                 accessories.Length == 0) &&
                (presets == null ||
                 presets.Length == 0);
        }

        private bool ValidateUserPreset(
            AppearancePreset preset,
            out string error)
        {
            error = null;

            if (preset == null ||
                string.IsNullOrWhiteSpace(
                    preset.Id))
            {
                error =
                    "User appearance preset requires a non-empty id.";
                return false;
            }

            if (!ValidateTarget(
                    preset.OutfitId,
                    preset.Accessories,
                    out error))
            {
                return false;
            }

            return ValidatePreferredTransition(
                preset,
                out error);
        }

        private bool ValidatePreferredTransition(
            AppearancePreset preset,
            out string error)
        {
            error = null;

            if (preset == null ||
                string.IsNullOrWhiteSpace(
                    preset.PreferredTransitionId) ||
                string.Equals(
                    preset.PreferredTransitionId,
                    "Immediate",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_transitions.ContainsKey(
                    preset.PreferredTransitionId))
            {
                return true;
            }

            error =
                $"Preset '{preset.Id}' references unknown transition '{preset.PreferredTransitionId}'.";
            return false;
        }

        private void UpsertUserPreset(
            AppearancePreset preset)
        {
            var clone =
                ClonePreset(
                    preset);

            if (_userPresets.ContainsKey(
                    clone.Id))
            {
                _userPresets[
                    clone.Id] =
                        clone;
                _presets[
                    clone.Id] =
                        ClonePreset(
                            clone);
                return;
            }

            _userPresets.Add(
                clone.Id,
                clone);
            _userPresetIds.Add(
                clone.Id);
            _presets.Add(
                clone.Id,
                ClonePreset(
                    clone));
            _presetIds.Add(
                clone.Id);
        }

        private static string NormalizePreferredTransition(
            string transitionId)
        {
            if (string.IsNullOrWhiteSpace(
                    transitionId) ||
                string.Equals(
                    transitionId,
                    "Immediate",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Immediate";
            }

            return transitionId.Trim();
        }

        private static AppearancePreset ClonePreset(
            AppearancePreset source)
        {
            if (source == null)
            {
                return null;
            }

            return new AppearancePreset
            {
                Id = source.Id,
                OutfitId = source.OutfitId,
                PreferredTransitionId =
                    source.PreferredTransitionId,
                Accessories =
                    CopySelections(
                        source.Accessories)
            };
        }

        private bool ValidateTarget(
            string outfitId,
            AppearanceAccessorySelection[] selections,
            out string error)
        {
            error = null;

            if (!string.IsNullOrWhiteSpace(
                    outfitId) &&
                !_outfits.ContainsKey(
                    outfitId))
            {
                error =
                    $"Unknown outfit '{outfitId}'.";
                return false;
            }

            var seenSlots =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var selection in
                     selections ??
                     Array.Empty<
                         AppearanceAccessorySelection>())
            {
                if (selection == null ||
                    string.IsNullOrWhiteSpace(
                        selection.SlotId) ||
                    string.IsNullOrWhiteSpace(
                        selection.AccessoryId))
                {
                    error =
                        "Accessory selections require non-empty slot and accessory ids.";
                    return false;
                }

                if (!seenSlots.Add(
                        selection.SlotId))
                {
                    error =
                        $"Accessory slot '{selection.SlotId}' appears more than once.";
                    return false;
                }

                if (!_accessories.ContainsKey(
                        AccessoryKey(
                            selection.SlotId,
                            selection.AccessoryId)))
                {
                    error =
                        $"Unknown accessory '{selection.SlotId}/{selection.AccessoryId}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateTransitionDefinition(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            if (transition == null ||
                string.IsNullOrWhiteSpace(
                    transition.Id))
            {
                error =
                    "Every appearance transition requires a non-empty id.";
                return false;
            }

            if (double.IsNaN(
                    transition.DurationSeconds) ||
                double.IsInfinity(
                    transition.DurationSeconds) ||
                transition.DurationSeconds < 0.0)
            {
                error =
                    $"Transition '{transition.Id}' has an invalid duration.";
                return false;
            }

            if (!TryBuildMarkerTimes(
                    transition,
                    out var markerTimes,
                    out error))
            {
                return false;
            }

            var commitCount = 0;
            var hasBlockingStep = false;
            var hasDependencyStep = false;
            var authoredActionSteps =
                new Dictionary<string, AppearanceTransitionStep>(
                    StringComparer.Ordinal);
            var previousTime = 0.0;
            var hasPreviousStep = false;
            var lastStepTime = 0.0;

            foreach (var step in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    !TryResolveStepTime(
                        step,
                        markerTimes,
                        out var resolvedTime,
                        out error))
                {
                    error =
                        $"Transition '{transition.Id}' contains invalid step timing: {error}";
                    return false;
                }

                if (hasPreviousStep &&
                    resolvedTime <
                        previousTime)
                {
                    error =
                        $"Transition '{transition.Id}' steps must be ordered by non-decreasing resolved time.";
                    return false;
                }

                previousTime =
                    resolvedTime;
                lastStepTime =
                    resolvedTime;
                hasPreviousStep =
                    true;

                if (!ValidateStepDependencies(
                        transition,
                        step,
                        authoredActionSteps,
                        out error))
                {
                    return false;
                }

                if (step.DependencyMode !=
                    AppearanceTransitionDependencyMode.None)
                {
                    hasDependencyStep =
                        true;
                }

                if (step.Blocking)
                {
                    hasBlockingStep = true;

                    if (step.Kind !=
                        AppearanceTransitionStepKind.Action)
                    {
                        error =
                            $"Transition '{transition.Id}' commit steps cannot be blocking.";
                        return false;
                    }

                    if (double.IsNaN(
                            step.CompletionTimeoutSeconds) ||
                        double.IsInfinity(
                            step.CompletionTimeoutSeconds) ||
                        step.CompletionTimeoutSeconds <= 0.0)
                    {
                        error =
                            $"Transition '{transition.Id}' blocking action '{step.ActionType ?? "<none>"}' requires a finite positive completion timeout.";
                        return false;
                    }
                }

                if (step.Kind ==
                    AppearanceTransitionStepKind.Commit)
                {
                    commitCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        step.ActionType))
                {
                    error =
                        $"Transition '{transition.Id}' contains an action step without an action type.";
                    return false;
                }

                if (step.Kind !=
                        AppearanceTransitionStepKind.Commit &&
                    step.ActionType.StartsWith(
                        "appearance.",
                        StringComparison.Ordinal))
                {
                    error =
                        $"Transition '{transition.Id}' cannot recursively execute appearance actions.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        step.StepId))
                {
                    if (!authoredActionSteps.TryAdd(
                            step.StepId,
                            step))
                    {
                        error =
                            $"Transition '{transition.Id}' contains duplicate action StepId '{step.StepId}'.";
                        return false;
                    }
                }

            }

            if (commitCount != 1)
            {
                error =
                    $"Transition '{transition.Id}' must contain exactly one appearance commit step.";
                return false;
            }

            if (transition.DurationSeconds <
                lastStepTime)
            {
                error =
                    $"Transition '{transition.Id}' duration cannot end before its last step.";
                return false;
            }

            if ((hasBlockingStep ||
                 hasDependencyStep) &&
                (transition.CancellationSteps == null ||
                 transition.CancellationSteps.Length == 0))
            {
                error =
                    $"Transition '{transition.Id}' contains completion waits and requires explicit cancellation cleanup steps.";
                return false;
            }

            if (!ValidateCancellationDefinition(
                    transition,
                    out error))
            {
                return false;
            }

            return true;
        }

        private static bool ValidateCancellationDefinition(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            var cleanup =
                transition.CancellationSteps ??
                Array.Empty<
                    AppearanceTransitionStep>();

            if (transition.QueuePolicy ==
                    AppearanceTransitionQueuePolicy.Interrupt &&
                cleanup.Length == 0)
            {
                error =
                    $"Transition '{transition.Id}' uses Interrupt but has no explicit cancellation cleanup steps.";
                return false;
            }

            foreach (var step in cleanup)
            {
                if (step == null ||
                    step.Kind !=
                        AppearanceTransitionStepKind.Action)
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup may contain action steps only.";
                    return false;
                }

                if (step.TimingMode !=
                        AppearanceTransitionTimingMode.AbsoluteTime ||
                    step.TimeSeconds != 0.0 ||
                    step.Blocking ||
                    step.DependencyMode !=
                        AppearanceTransitionDependencyMode.None ||
                    (step.DependsOnStepIds != null &&
                     step.DependsOnStepIds.Length > 0))
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup steps execute immediately, must use absolute time 0, cannot block, and cannot declare dependencies.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(
                        step.ActionType) ||
                    step.ActionType.StartsWith(
                        "appearance.",
                        StringComparison.Ordinal))
                {
                    error =
                        $"Transition '{transition.Id}' cancellation cleanup requires non-appearance action types.";
                    return false;
                }
            }

            return true;
        }

        private bool CanCancel(
            AppearanceTransitionPreset transition)
        {
            if (transition == null ||
                transition.CancellationSteps == null ||
                transition.CancellationSteps.Length == 0)
            {
                return false;
            }

            foreach (var step in
                     transition.CancellationSteps)
            {
                if (step == null ||
                    (step.Required &&
                     CountExecutors(step) != 1))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CancelActiveTransition(
            bool interrupted,
            bool clearPending,
            out string error)
        {
            error = null;

            if (_transitionCoroutine == null ||
                _activeTransition == null)
            {
                error =
                    "No appearance transition is active.";
                return false;
            }

            if (!CanCancel(
                    _activeTransition))
            {
                error =
                    $"Transition '{_activeTransition.Id}' has no executable cancellation cleanup contract.";
                return false;
            }

            StopCoroutine(
                _transitionCoroutine);
            _transitionCoroutine = null;

            var cleanupSucceeded =
                TryRunCancellationCleanup(
                    _activeTransition,
                    out var cleanupError);

            if (clearPending)
            {
                _pending.Clear();
            }

            ClearActiveTransition();

            if (!cleanupSucceeded)
            {
                _transitionFailureCount++;
                error =
                    cleanupError;
                SetState(
                    AppearanceRuntimeState.Faulted,
                    cleanupError);
                return false;
            }

            if (interrupted)
            {
                _transitionInterruptedCount++;
            }
            else
            {
                _transitionCancelledCount++;
            }

            SetState(
                AppearanceRuntimeState.Ready,
                cleanupError);
            return true;
        }

        private bool TryRunCancellationCleanup(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

            if (transition == null)
            {
                return true;
            }

            string firstRequiredError = null;
            string firstOptionalError = null;

            foreach (var step in
                     transition.CancellationSteps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (TryExecuteStep(
                        step,
                        out var stepError))
                {
                    continue;
                }

                if (step.Required)
                {
                    firstRequiredError ??=
                        stepError;
                }
                else
                {
                    firstOptionalError ??=
                        stepError;
                }
            }

            error =
                firstRequiredError ??
                firstOptionalError;
            return
                firstRequiredError == null;
        }

        private void ClearActiveTransition()
        {
            _transitionCoroutine = null;
            _activeTransition = null;
            _activeRequest = null;
            _activeTransitionId = null;
            _transitionCommitted = false;
            _transitionStartedAt = 0.0;
        }

        private bool ValidateRequiredExecutors(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;
            var actionStepsById =
                new Dictionary<string, AppearanceTransitionStep>(
                    StringComparer.Ordinal);

            foreach (var candidate in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (candidate != null &&
                    candidate.Kind ==
                        AppearanceTransitionStepKind.Action &&
                    !string.IsNullOrWhiteSpace(
                        candidate.StepId))
                {
                    actionStepsById[
                        candidate.StepId] =
                            candidate;
                }
            }

            foreach (var step in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    step.Kind ==
                        AppearanceTransitionStepKind.Commit ||
                    !step.Required)
                {
                    continue;
                }

                var count =
                    CountExecutors(step);

                if (count != 1)
                {
                    error =
                        count == 0
                            ? $"No transition executor handles '{step.ActionType}'."
                            : $"Multiple transition executors handle '{step.ActionType}'.";
                    return false;
                }

                if (step.Blocking &&
                    CountCompletionProbes(
                        step) != 1)
                {
                    error =
                        $"Blocking transition action '{step.ActionType}' requires exactly one completion probe.";
                    return false;
                }
            }

            foreach (var waitingStep in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (waitingStep == null ||
                    waitingStep.DependencyMode ==
                        AppearanceTransitionDependencyMode.None)
                {
                    continue;
                }

                foreach (var dependencyId in
                         waitingStep.DependsOnStepIds ??
                         Array.Empty<string>())
                {
                    if (!actionStepsById.TryGetValue(
                            dependencyId,
                            out var dependencyStep))
                    {
                        error =
                            $"Dependency source '{dependencyId}' is unavailable.";
                        return false;
                    }

                    var executorCount =
                        CountExecutors(
                            dependencyStep);
                    var completionCount =
                        CountCompletionProbes(
                            dependencyStep);

                    if (executorCount != 1 ||
                        completionCount != 1)
                    {
                        error =
                            $"Dependency source '{dependencyId}' requires exactly one executor and one completion probe.";
                        return false;
                    }
                }
            }

            if (transition.QueuePolicy !=
                AppearanceTransitionQueuePolicy.Interrupt)
            {
                return true;
            }

            foreach (var step in
                     transition.CancellationSteps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    !step.Required)
                {
                    continue;
                }

                var count =
                    CountExecutors(
                        step);

                if (count != 1)
                {
                    error =
                        count == 0
                            ? $"No transition executor handles cancellation action '{step.ActionType}'."
                            : $"Multiple transition executors handle cancellation action '{step.ActionType}'.";
                    return false;
                }
            }

            return true;
        }

        private bool TryExecuteStep(
            AppearanceTransitionStep step,
            out string error)
        {
            error = null;
            IAppearanceTransitionStepExecutor
                selected = null;
            var count = 0;

            foreach (var executor in
                     _executors)
            {
                if (executor == null ||
                    !executor.CanExecute(step))
                {
                    continue;
                }

                selected = executor;
                count++;
            }

            if (count == 0)
            {
                error =
                    $"No transition executor handles '{step.ActionType}'.";
                return false;
            }

            if (count > 1)
            {
                error =
                    $"Multiple transition executors handle '{step.ActionType}'.";
                return false;
            }

            try
            {
                return selected.TryExecute(
                    step,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    exception.Message;
                return false;
            }
        }

        private bool TryEvaluateDependencies(
            AppearanceTransitionStep waitingStep,
            IReadOnlyDictionary<string, AppearanceTransitionStep>
                executedActions,
            out bool satisfied,
            out string error)
        {
            satisfied = false;
            error = null;

            var dependencyIds =
                waitingStep.DependsOnStepIds ??
                Array.Empty<string>();

            if (waitingStep.DependencyMode ==
                    AppearanceTransitionDependencyMode.None ||
                dependencyIds.Length == 0)
            {
                satisfied = true;
                return true;
            }

            var completedCount = 0;

            foreach (var dependencyId in dependencyIds)
            {
                if (!executedActions.TryGetValue(
                        dependencyId,
                        out var dependencyStep))
                {
                    error =
                        $"Dependency action '{dependencyId}' was not successfully started before step '{DescribeStep(waitingStep)}'.";
                    return false;
                }

                if (!TryIsStepComplete(
                        dependencyStep,
                        out var complete,
                        out var completionError))
                {
                    error =
                        $"Dependency action '{dependencyId}' completion check failed: {completionError}";
                    return false;
                }

                if (complete)
                {
                    completedCount++;
                }
                else if (waitingStep.DependencyMode ==
                         AppearanceTransitionDependencyMode.All)
                {
                    satisfied = false;
                    return true;
                }
            }

            satisfied =
                waitingStep.DependencyMode ==
                    AppearanceTransitionDependencyMode.All
                    ? completedCount ==
                      dependencyIds.Length
                    : completedCount > 0;
            return true;
        }

        private static bool ValidateStepDependencies(
            AppearanceTransitionPreset transition,
            AppearanceTransitionStep step,
            IReadOnlyDictionary<string, AppearanceTransitionStep>
                previousActionSteps,
            out string error)
        {
            error = null;
            var dependencyIds =
                step.DependsOnStepIds ??
                Array.Empty<string>();

            if (step.DependencyMode ==
                AppearanceTransitionDependencyMode.None)
            {
                if (dependencyIds.Length > 0)
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' lists dependencies but DependencyMode is None.";
                    return false;
                }

                return true;
            }

            if (step.DependencyMode !=
                    AppearanceTransitionDependencyMode.All &&
                step.DependencyMode !=
                    AppearanceTransitionDependencyMode.Any)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' has unsupported dependency mode '{step.DependencyMode}'.";
                return false;
            }

            if (dependencyIds.Length == 0)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' requires at least one dependency.";
                return false;
            }

            if (double.IsNaN(
                    step.DependencyTimeoutSeconds) ||
                double.IsInfinity(
                    step.DependencyTimeoutSeconds) ||
                step.DependencyTimeoutSeconds <= 0.0)
            {
                error =
                    $"Transition '{transition.Id}' step '{DescribeStep(step)}' requires a finite positive dependency timeout.";
                return false;
            }

            var seen =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var dependencyId in dependencyIds)
            {
                if (string.IsNullOrWhiteSpace(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' contains an empty dependency id.";
                    return false;
                }

                if (!seen.Add(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' repeats dependency '{dependencyId}'.";
                    return false;
                }

                if (!previousActionSteps.ContainsKey(
                        dependencyId))
                {
                    error =
                        $"Transition '{transition.Id}' step '{DescribeStep(step)}' dependency '{dependencyId}' must reference an earlier Action step with a StepId.";
                    return false;
                }
            }

            return true;
        }

        private static string DescribeStep(
            AppearanceTransitionStep step)
        {
            if (step == null)
            {
                return "<null>";
            }

            if (!string.IsNullOrWhiteSpace(
                    step.StepId))
            {
                return step.StepId;
            }

            return step.Kind ==
                AppearanceTransitionStepKind.Commit
                ? "appearance.commit"
                : step.ActionType ??
                  "<action>";
        }

        private bool TryIsStepComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;
            IAppearanceTransitionStepCompletionProbe
                selected = null;
            var count = 0;

            foreach (var executor in
                     _executors)
            {
                if (executor is not
                        IAppearanceTransitionStepCompletionProbe
                            probe ||
                    !executor.CanExecute(
                        step) ||
                    !probe.CanTrackCompletion(
                        step))
                {
                    continue;
                }

                selected =
                    probe;
                count++;
            }

            if (count == 0)
            {
                error =
                    $"No completion probe tracks transition action '{step.ActionType}'.";
                return false;
            }

            if (count > 1)
            {
                error =
                    $"Multiple completion probes track transition action '{step.ActionType}'.";
                return false;
            }

            try
            {
                return selected.TryIsComplete(
                    step,
                    out complete,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    exception.Message;
                return false;
            }
        }

        private int CountCompletionProbes(
            AppearanceTransitionStep step)
        {
            var count = 0;

            foreach (var executor in
                     _executors)
            {
                if (executor is
                        IAppearanceTransitionStepCompletionProbe
                            probe &&
                    executor.CanExecute(
                        step) &&
                    probe.CanTrackCompletion(
                        step))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryBuildMarkerTimes(
            AppearanceTransitionPreset transition,
            out Dictionary<string, double> markerTimes,
            out string error)
        {
            markerTimes =
                new Dictionary<string, double>(
                    StringComparer.Ordinal);
            error = null;

            foreach (var marker in
                     transition.Markers ??
                     Array.Empty<
                         AppearanceTransitionMarker>())
            {
                if (marker == null ||
                    string.IsNullOrWhiteSpace(
                        marker.Name))
                {
                    error =
                        $"Transition '{transition.Id}' contains a marker without a name.";
                    return false;
                }

                if (double.IsNaN(
                        marker.TimeSeconds) ||
                    double.IsInfinity(
                        marker.TimeSeconds) ||
                    marker.TimeSeconds < 0.0 ||
                    marker.TimeSeconds >
                        transition.DurationSeconds)
                {
                    error =
                        $"Transition '{transition.Id}' marker '{marker.Name}' must be within transition duration.";
                    return false;
                }

                if (!markerTimes.TryAdd(
                        marker.Name,
                        marker.TimeSeconds))
                {
                    error =
                        $"Transition '{transition.Id}' contains duplicate marker '{marker.Name}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveStepTime(
            AppearanceTransitionStep step,
            IReadOnlyDictionary<string, double> markerTimes,
            out double resolvedTime,
            out string error)
        {
            resolvedTime = 0.0;
            error = null;

            if (step == null)
            {
                error =
                    "Transition step is null.";
                return false;
            }

            switch (step.TimingMode)
            {
                case AppearanceTransitionTimingMode
                    .AbsoluteTime:
                    resolvedTime =
                        step.TimeSeconds;
                    break;

                case AppearanceTransitionTimingMode
                    .Marker:
                    if (string.IsNullOrWhiteSpace(
                            step.MarkerName) ||
                        markerTimes == null ||
                        !markerTimes.TryGetValue(
                            step.MarkerName,
                            out var markerTime))
                    {
                        error =
                            $"Unknown transition marker '{step.MarkerName ?? "<null>"}'.";
                        return false;
                    }

                    if (double.IsNaN(
                            step.MarkerOffsetSeconds) ||
                        double.IsInfinity(
                            step.MarkerOffsetSeconds))
                    {
                        error =
                            $"Marker offset for '{step.MarkerName}' must be finite.";
                        return false;
                    }

                    resolvedTime =
                        markerTime +
                        step.MarkerOffsetSeconds;
                    break;

                default:
                    error =
                        $"Unsupported transition timing mode '{step.TimingMode}'.";
                    return false;
            }

            if (double.IsNaN(
                    resolvedTime) ||
                double.IsInfinity(
                    resolvedTime) ||
                resolvedTime < 0.0)
            {
                error =
                    "Resolved transition step time must be finite and non-negative.";
                return false;
            }

            return true;
        }

        private int CountExecutors(
            AppearanceTransitionStep step)
        {
            var count = 0;

            foreach (var executor in
                     _executors)
            {
                if (executor != null &&
                    executor.CanExecute(step))
                {
                    count++;
                }
            }

            return count;
        }

        private void RebuildExecutors()
        {
            var list =
                new List<
                    IAppearanceTransitionStepExecutor>();

            foreach (var behaviour in
                     transitionExecutorBehaviours ??
                     Array.Empty<MonoBehaviour>())
            {
                if (behaviour is
                    IAppearanceTransitionStepExecutor
                        executor &&
                    !list.Contains(executor))
                {
                    list.Add(executor);
                }
            }

            if (autoFindTransitionExecutors)
            {
                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var behaviour in behaviours)
                {
                    if (ReferenceEquals(
                            behaviour,
                            this) ||
                        behaviour is not
                            IAppearanceTransitionStepExecutor
                                executor ||
                        list.Contains(executor))
                    {
                        continue;
                    }

                    list.Add(executor);
                }
            }

            _executors =
                list.ToArray();
        }

        private void FinishTransition(
            string error)
        {
            _lastError =
                string.IsNullOrWhiteSpace(error)
                    ? null
                    : error;

            ClearActiveTransition();

            SetState(
                AppearanceRuntimeState.Ready,
                _lastError);

            if (_pending.Count == 0)
            {
                return;
            }

            var next =
                _pending.Dequeue();

            if (!StartRequest(
                    next,
                    out var nextError))
            {
                SetFault(
                    nextError);
            }
        }

        private void SetFault(
            string error)
        {
            _transitionFailureCount++;
            SetState(
                AppearanceRuntimeState.Faulted,
                error);
        }

        private void SetState(
            AppearanceRuntimeState state,
            string error)
        {
            _state = state;
            _lastError = error;

            StatusChanged?.Invoke(
                Status);
        }

        private AppearanceAccessorySelection[]
            CaptureCurrentAccessories()
        {
            return ToSelections(
                CaptureCurrentAccessoryMap());
        }

        private Dictionary<string, string>
            CaptureCurrentAccessoryMap()
        {
            return new Dictionary<string, string>(
                _currentAccessories,
                StringComparer.Ordinal);
        }

        private static AppearanceAccessorySelection[]
            ToSelections(
                Dictionary<string, string> map)
        {
            if (map == null ||
                map.Count == 0)
            {
                return Array.Empty<
                    AppearanceAccessorySelection>();
            }

            var keys =
                new List<string>(
                    map.Keys);
            keys.Sort(
                StringComparer.Ordinal);

            var result =
                new AppearanceAccessorySelection[
                    keys.Count];

            for (var i = 0;
                 i < keys.Count;
                 i++)
            {
                var slot =
                    keys[i];

                result[i] =
                    new AppearanceAccessorySelection
                    {
                        SlotId = slot,
                        AccessoryId =
                            map[slot]
                    };
            }

            return result;
        }

        private static AppearanceAccessorySelection[]
            CopySelections(
                AppearanceAccessorySelection[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    AppearanceAccessorySelection>();
            }

            var result =
                new AppearanceAccessorySelection[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                result[i] =
                    source[i]?.Clone();
            }

            return result;
        }

        private static string AccessoryKey(
            string slotId,
            string accessoryId)
        {
            return
                (slotId ?? string.Empty) +
                "
" +
                (accessoryId ?? string.Empty);
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
                    "appearance.state",
                    (int)_state,
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "appearance.changes",
                    _appearanceChangeCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.starts",
                    _transitionStartCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.commits",
                    _transitionCommitCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.failures",
                    _transitionFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.queued",
                    _transitionQueuedCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.interrupted",
                    _transitionInterruptedCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.cancelled",
                    _transitionCancelledCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.progress",
                    Status.TransitionProgress01,
                    "ratio"));
        }

        private sealed class AppearanceChangeRequest
        {
            public AppearanceChangeRequest(
                string presetId,
                string outfitId,
                AppearanceAccessorySelection[] accessories,
                string transitionId)
            {
                PresetId = presetId;
                OutfitId = outfitId;
                Accessories =
                    accessories ??
                    Array.Empty<
                        AppearanceAccessorySelection>();
                TransitionId = transitionId;
            }

            public string PresetId { get; }
            public string OutfitId { get; }
            public AppearanceAccessorySelection[]
                Accessories { get; }
            public string TransitionId { get; }
        }
    }
}
