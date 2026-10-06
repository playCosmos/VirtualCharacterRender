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
        [SerializeField, Range(1, 256)] private int maxQueuedTransitions = 32;

        private readonly Dictionary<string, AppearanceOutfitBinding>
            _outfits =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, AppearanceAccessoryBinding>
            _accessories =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AppearanceAccessoryBinding>>
            _accessoriesBySlot =
                new(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform>
            _resolvedAccessoryAnchors =
                new(StringComparer.Ordinal);
        private readonly Dictionary<GameObject, AccessoryTransformState>
            _accessoryOriginalTransforms =
                new();
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

        private const double TransitionExecutorDiscoveryRetrySeconds = 1.0;

        private IAppearanceTransitionStepExecutor[] _executors =
            Array.Empty<IAppearanceTransitionStepExecutor>();
        private double _nextTransitionExecutorResolveAt;

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
        private long _transitionQueueRejectedCount;
        private long _transitionInterruptedCount;
        private long _transitionCancelledCount;
        private long _appearanceSubscriberFailureCount;
        private long _statusSubscriberFailureCount;
        private long _executorProbeFailureCount;

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

        public int MaxQueuedTransitions =>
            Mathf.Clamp(
                maxQueuedTransitions,
                1,
                256);

        public int PendingTransitionCount =>
            _pending.Count;

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
            var snapshot =
                CaptureConfigurationSnapshot();

            try
            {
                if (RebuildConfigurationCore(
                        out error))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                error =
                    "Appearance configuration rebuild failed: " +
                    exception.Message;
            }

            RestoreConfigurationSnapshot(
                snapshot);
            return false;
        }

        private bool RebuildConfigurationCore(
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
            _resolvedAccessoryAnchors.Clear();
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
                var resolvedBinding =
                    binding?.Clone();

                if (resolvedBinding == null ||
                    string.IsNullOrWhiteSpace(
                        resolvedBinding.OutfitId))
                {
                    error =
                        "Every outfit binding requires a non-empty id.";
                    return false;
                }

                if (!_outfits.TryAdd(
                        resolvedBinding.OutfitId,
                        resolvedBinding))
                {
                    error =
                        $"Duplicate outfit id '{resolvedBinding.OutfitId}'.";
                    return false;
                }

                foreach (var root in
                         resolvedBinding.Roots ??
                         Array.Empty<GameObject>())
                {
                    if (root == null)
                    {
                        error =
                            $"Outfit '{resolvedBinding.OutfitId}' contains a null root.";
                        return false;
                    }

                    if (rootOwners.TryGetValue(
                            root,
                            out var owner))
                    {
                        error =
                            $"Appearance root '{root.name}' is shared by '{owner}' and outfit '{resolvedBinding.OutfitId}'.";
                        return false;
                    }

                    rootOwners[root] =
                        "outfit:" +
                        resolvedBinding.OutfitId;
                }
            }

            foreach (var binding in
                     accessories ??
                     Array.Empty<AppearanceAccessoryBinding>())
            {
                var resolvedBinding =
                    binding?.Clone();

                if (resolvedBinding == null ||
                    string.IsNullOrWhiteSpace(
                        resolvedBinding.SlotId) ||
                    string.IsNullOrWhiteSpace(
                        resolvedBinding.AccessoryId) ||
                    resolvedBinding.Root == null)
                {
                    error =
                        "Every accessory binding requires slot id, accessory id, and root.";
                    return false;
                }

                var key =
                    AccessoryKey(
                        resolvedBinding.SlotId,
                        resolvedBinding.AccessoryId);

                if (!_accessories.TryAdd(
                        key,
                        resolvedBinding))
                {
                    error =
                        $"Duplicate accessory '{resolvedBinding.SlotId}/{resolvedBinding.AccessoryId}'.";
                    return false;
                }

                if (!_accessoriesBySlot.TryGetValue(
                        resolvedBinding.SlotId,
                        out var slot))
                {
                    slot =
                        new List<AppearanceAccessoryBinding>();
                    _accessoriesBySlot[
                        resolvedBinding.SlotId] =
                            slot;
                }

                slot.Add(
                    resolvedBinding);

                if (rootOwners.TryGetValue(
                        resolvedBinding.Root,
                        out var owner))
                {
                    error =
                        $"Appearance root '{resolvedBinding.Root.name}' is shared by '{owner}' and accessory '{resolvedBinding.SlotId}/{resolvedBinding.AccessoryId}'.";
                    return false;
                }

                rootOwners[
                    resolvedBinding.Root] =
                        "accessory:" +
                        resolvedBinding.SlotId +
                        "/" +
                        resolvedBinding.AccessoryId;

                if (!ValidateAccessoryAnchorPose(
                        resolvedBinding,
                        out error))
                {
                    error =
                        $"Accessory '{resolvedBinding.SlotId}/{resolvedBinding.AccessoryId}' anchor pose is invalid: {error}";
                    return false;
                }

                if (!_accessoryOriginalTransforms.ContainsKey(
                        resolvedBinding.Root))
                {
                    _accessoryOriginalTransforms[
                        resolvedBinding.Root] =
                            AccessoryTransformState.Capture(
                                resolvedBinding.Root.transform);
                }

                if (!TryResolveAccessoryAnchor(
                        resolvedBinding,
                        out var anchor,
                        out error))
                {
                    error =
                        $"Accessory '{resolvedBinding.SlotId}/{resolvedBinding.AccessoryId}' anchor is invalid: {error}";
                    return false;
                }

                if (anchor != null)
                {
                    _resolvedAccessoryAnchors[
                        key] =
                            anchor;
                }
            }

            PruneAccessoryTransformStates();

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

                if (!AppearanceTransitionDefinitionValidator.Validate(
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

        private static AppearanceOutfitBinding[]
            CloneOutfitBindings(
                AppearanceOutfitBinding[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    AppearanceOutfitBinding>();
            }

            var clones =
                new AppearanceOutfitBinding[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                clones[i] =
                    source[i]?.Clone();
            }

            return clones;
        }

        private static AppearanceAccessoryBinding[]
            CloneAccessoryBindings(
                AppearanceAccessoryBinding[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    AppearanceAccessoryBinding>();
            }

            var clones =
                new AppearanceAccessoryBinding[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                clones[i] =
                    source[i]?.Clone();
            }

            return clones;
        }

        private static AppearancePresetBinding[]
            ClonePresetBindings(
                AppearancePresetBinding[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    AppearancePresetBinding>();
            }

            var clones =
                new AppearancePresetBinding[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                clones[i] =
                    source[i]?.Clone();
            }

            return clones;
        }

        private static AppearanceTransitionBinding[]
            CloneTransitionBindings(
                AppearanceTransitionBinding[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    AppearanceTransitionBinding>();
            }

            var clones =
                new AppearanceTransitionBinding[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                clones[i] =
                    source[i]?.Clone();
            }

            return clones;
        }

        private AppearanceConfigurationSnapshot
            CaptureConfigurationSnapshot()
        {
            return new AppearanceConfigurationSnapshot
            {
                Outfits =
                    outfits,
                Accessories =
                    accessories,
                Presets =
                    presets,
                Transitions =
                    transitions,
                TransitionExecutorBehaviours =
                    transitionExecutorBehaviours,
                DefaultPresetId =
                    defaultPresetId,
                ResolvedOutfits =
                    new Dictionary<string, AppearanceOutfitBinding>(
                        _outfits,
                        StringComparer.Ordinal),
                ResolvedAccessories =
                    new Dictionary<string, AppearanceAccessoryBinding>(
                        _accessories,
                        StringComparer.Ordinal),
                ResolvedAccessoriesBySlot =
                    CloneAccessorySlotMap(
                        _accessoriesBySlot),
                ResolvedAccessoryAnchors =
                    new Dictionary<string, Transform>(
                        _resolvedAccessoryAnchors,
                        StringComparer.Ordinal),
                AccessoryOriginalTransforms =
                    new Dictionary<GameObject, AccessoryTransformState>(
                        _accessoryOriginalTransforms),
                ResolvedPresets =
                    new Dictionary<string, AppearancePreset>(
                        _presets,
                        StringComparer.Ordinal),
                AuthoredPresetIds =
                    new HashSet<string>(
                        _authoredPresetIds,
                        StringComparer.Ordinal),
                ResolvedTransitions =
                    new Dictionary<string, AppearanceTransitionPreset>(
                        _transitions,
                        StringComparer.Ordinal),
                PresetIds =
                    new List<string>(
                        _presetIds),
                TransitionIds =
                    new List<string>(
                        _transitionIds),
                Executors =
                    _executors == null
                        ? Array.Empty<IAppearanceTransitionStepExecutor>()
                        : (IAppearanceTransitionStepExecutor[])
                            _executors.Clone(),
                NextTransitionExecutorResolveAt =
                    _nextTransitionExecutorResolveAt,
                State =
                    _state,
                LastError =
                    _lastError
            };
        }

        private void RestoreConfigurationSnapshot(
            AppearanceConfigurationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            outfits =
                snapshot.Outfits;
            accessories =
                snapshot.Accessories;
            presets =
                snapshot.Presets;
            transitions =
                snapshot.Transitions;
            transitionExecutorBehaviours =
                snapshot.TransitionExecutorBehaviours;
            defaultPresetId =
                snapshot.DefaultPresetId;

            RestoreDictionary(
                _outfits,
                snapshot.ResolvedOutfits);
            RestoreDictionary(
                _accessories,
                snapshot.ResolvedAccessories);
            RestoreAccessorySlotMap(
                snapshot.ResolvedAccessoriesBySlot);
            RestoreDictionary(
                _resolvedAccessoryAnchors,
                snapshot.ResolvedAccessoryAnchors);
            RestoreDictionary(
                _accessoryOriginalTransforms,
                snapshot.AccessoryOriginalTransforms);
            RestoreDictionary(
                _presets,
                snapshot.ResolvedPresets);

            _authoredPresetIds.Clear();
            _authoredPresetIds.UnionWith(
                snapshot.AuthoredPresetIds);

            RestoreDictionary(
                _transitions,
                snapshot.ResolvedTransitions);

            _presetIds.Clear();
            _presetIds.AddRange(
                snapshot.PresetIds);
            _transitionIds.Clear();
            _transitionIds.AddRange(
                snapshot.TransitionIds);

            _executors =
                snapshot.Executors;
            _nextTransitionExecutorResolveAt =
                snapshot.NextTransitionExecutorResolveAt;
            _state =
                snapshot.State;
            _lastError =
                snapshot.LastError;
        }

        private static Dictionary<
            string,
            List<AppearanceAccessoryBinding>>
            CloneAccessorySlotMap(
                Dictionary<
                    string,
                    List<AppearanceAccessoryBinding>>
                    source)
        {
            var clone =
                new Dictionary<
                    string,
                    List<AppearanceAccessoryBinding>>(
                        StringComparer.Ordinal);

            foreach (var pair in source)
            {
                clone[pair.Key] =
                    new List<AppearanceAccessoryBinding>(
                        pair.Value);
            }

            return clone;
        }

        private void RestoreAccessorySlotMap(
            Dictionary<
                string,
                List<AppearanceAccessoryBinding>>
                snapshot)
        {
            _accessoriesBySlot.Clear();

            foreach (var pair in snapshot)
            {
                _accessoriesBySlot[pair.Key] =
                    new List<AppearanceAccessoryBinding>(
                        pair.Value);
            }
        }

        private static void RestoreDictionary<TKey, TValue>(
            Dictionary<TKey, TValue> target,
            Dictionary<TKey, TValue> snapshot)
        {
            target.Clear();

            foreach (var pair in snapshot)
            {
                target.Add(
                    pair.Key,
                    pair.Value);
            }
        }

        public bool TrySetMaxQueuedTransitions(
            int value,
            out string error)
        {
            if (value < 1 || value > 256)
            {
                error =
                    "Max queued appearance transitions must be in the 1..256 range.";
                return false;
            }

            maxQueuedTransitions =
                value;
            error = null;
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
            var previousOutfits =
                outfits;
            var previousAccessories =
                accessories;
            var previousPresets =
                presets;
            var previousTransitions =
                transitions;
            var previousExecutors =
                transitionExecutorBehaviours;
            var previousDefaultPresetId =
                defaultPresetId;

            outfits =
                CloneOutfitBindings(
                    nextOutfits);
            accessories =
                CloneAccessoryBindings(
                    nextAccessories);
            presets =
                ClonePresetBindings(
                    nextPresets);
            transitions =
                CloneTransitionBindings(
                    nextTransitions);
            transitionExecutorBehaviours =
                executors == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        executors.Clone();

            if (nextDefaultPresetId != null)
            {
                defaultPresetId =
                    nextDefaultPresetId;
            }

            if (!RebuildConfiguration(
                    out var error))
            {
                outfits =
                    previousOutfits;
                accessories =
                    previousAccessories;
                presets =
                    previousPresets;
                transitions =
                    previousTransitions;
                transitionExecutorBehaviours =
                    previousExecutors;
                defaultPresetId =
                    previousDefaultPresetId;

                SetState(
                    _state,
                    error);
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
                (!_presets.TryGetValue(
                     _currentPresetId,
                     out var currentPreset) ||
                 !CurrentAppearanceMatchesPreset(
                     currentPreset)))
            {
                ClearCurrentPresetAndNotify();
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
            if (!CanEditUserPresets(
                    out error))
            {
                return false;
            }

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
                ClearCurrentPresetAndNotify();
            }

            SetState(
                _state,
                _lastError);
            return true;
        }

        public bool RenameUserPreset(
            string presetId,
            string newPresetId,
            out AppearancePreset preset,
            out string error)
        {
            preset = null;

            if (!CanEditUserPresets(
                    out error))
            {
                return false;
            }

            var sourceId =
                presetId?.Trim();
            var targetId =
                newPresetId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sourceId) ||
                !_userPresets.ContainsKey(
                    sourceId))
            {
                error =
                    $"Unknown user appearance preset '{presetId ?? "<null>"}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    targetId))
            {
                error =
                    "New user appearance preset id is required.";
                return false;
            }

            if (string.Equals(
                    sourceId,
                    targetId,
                    StringComparison.Ordinal))
            {
                preset =
                    ClonePreset(
                        _userPresets[
                            sourceId]);
                return true;
            }

            if (_authoredPresetIds.Contains(
                    targetId))
            {
                error =
                    $"User preset '{targetId}' cannot replace an authored appearance preset.";
                return false;
            }

            if (_userPresets.ContainsKey(
                    targetId))
            {
                error =
                    $"User appearance preset '{targetId}' already exists.";
                return false;
            }

            var next =
                CaptureUserPresets();
            var renamedIndex =
                Array.FindIndex(
                    next,
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.Id,
                            sourceId,
                            StringComparison.Ordinal));

            if (renamedIndex < 0)
            {
                error =
                    $"User appearance preset '{sourceId}' could not be captured for rename.";
                return false;
            }

            next[
                renamedIndex].Id =
                    targetId;
            var wasCurrent =
                string.Equals(
                    _currentPresetId,
                    sourceId,
                    StringComparison.Ordinal);

            if (wasCurrent)
            {
                _currentPresetId =
                    targetId;
            }

            if (!ReplaceUserPresets(
                    next,
                    out error))
            {
                if (wasCurrent)
                {
                    _currentPresetId =
                        sourceId;
                }

                return false;
            }

            if (wasCurrent)
            {
                NotifyAppearanceChanged(
                    Current);
            }

            preset =
                ClonePreset(
                    _userPresets[
                        targetId]);
            return true;
        }

        public bool DuplicateUserPreset(
            string presetId,
            string newPresetId,
            out AppearancePreset preset,
            out string error)
        {
            preset = null;

            if (!CanEditUserPresets(
                    out error))
            {
                return false;
            }

            var sourceId =
                presetId?.Trim();
            var targetId =
                newPresetId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sourceId) ||
                !_userPresets.TryGetValue(
                    sourceId,
                    out var source))
            {
                error =
                    $"Unknown user appearance preset '{presetId ?? "<null>"}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    targetId))
            {
                error =
                    "Duplicate user appearance preset id is required.";
                return false;
            }

            if (_authoredPresetIds.Contains(
                    targetId))
            {
                error =
                    $"User preset '{targetId}' cannot replace an authored appearance preset.";
                return false;
            }

            if (_userPresets.ContainsKey(
                    targetId))
            {
                error =
                    $"User appearance preset '{targetId}' already exists.";
                return false;
            }

            var previous =
                CaptureUserPresets();
            var sourceIndex =
                Array.FindIndex(
                    previous,
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.Id,
                            sourceId,
                            StringComparison.Ordinal));

            if (sourceIndex < 0)
            {
                error =
                    $"User appearance preset '{sourceId}' could not be captured for duplication.";
                return false;
            }

            var next =
                new AppearancePreset[
                    previous.Length + 1];

            for (var i = 0;
                 i <= sourceIndex;
                 i++)
            {
                next[i] =
                    previous[i];
            }

            var duplicate =
                ClonePreset(
                    source);
            duplicate.Id =
                targetId;
            next[
                sourceIndex + 1] =
                    duplicate;

            for (var i = sourceIndex + 1;
                 i < previous.Length;
                 i++)
            {
                next[
                    i + 1] =
                        previous[i];
            }

            if (!ReplaceUserPresets(
                    next,
                    out error))
            {
                return false;
            }

            preset =
                ClonePreset(
                    _userPresets[
                        targetId]);
            SetState(
                _state,
                _lastError);
            return true;
        }

        public bool MoveUserPreset(
            string presetId,
            int offset,
            out string error)
        {
            if (!CanEditUserPresets(
                    out error))
            {
                return false;
            }

            var id =
                presetId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    id))
            {
                error =
                    "User appearance preset id is required for reorder.";
                return false;
            }

            var next =
                CaptureUserPresets();
            var sourceIndex =
                Array.FindIndex(
                    next,
                    candidate =>
                        candidate != null &&
                        string.Equals(
                            candidate.Id,
                            id,
                            StringComparison.Ordinal));

            if (sourceIndex < 0)
            {
                error =
                    $"Unknown user appearance preset '{presetId ?? "<null>"}'.";
                return false;
            }

            if (offset == 0)
            {
                return true;
            }

            var targetIndex =
                Math.Max(
                    0,
                    Math.Min(
                        next.Length - 1,
                        sourceIndex +
                        offset));

            if (targetIndex ==
                sourceIndex)
            {
                error =
                    offset < 0
                        ? $"User preset '{id}' is already first."
                        : $"User preset '{id}' is already last.";
                return false;
            }

            var moved =
                next[
                    sourceIndex];

            if (targetIndex >
                sourceIndex)
            {
                for (var i = sourceIndex;
                     i < targetIndex;
                     i++)
                {
                    next[i] =
                        next[
                            i + 1];
                }
            }
            else
            {
                for (var i = sourceIndex;
                     i > targetIndex;
                     i--)
                {
                    next[i] =
                        next[
                            i - 1];
                }
            }

            next[
                targetIndex] =
                    moved;

            return ReplaceUserPresets(
                next,
                out error);
        }

        private bool CanEditUserPresets(
            out string error)
        {
            error = null;

            if (_state ==
                    AppearanceRuntimeState.Transitioning ||
                _state ==
                    AppearanceRuntimeState.Committing)
            {
                error =
                    "User appearance presets cannot be modified during an appearance transition.";
                return false;
            }

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
                    if (_pending.Count >=
                        MaxQueuedTransitions)
                    {
                        _transitionQueueRejectedCount++;
                        error =
                            $"Appearance transition queue is full ({MaxQueuedTransitions}).";
                        return false;
                    }

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

            RefreshExecutorsIfNeeded();

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

            if (!AppearanceTransitionDefinitionValidator.TryBuildMarkerTimes(
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
            Dictionary<
                AppearanceTransitionStep,
                IAppearanceTransitionStepCompletionProbe>
                completionProbes = null;

            foreach (var step in steps)
            {
                if (!AppearanceTransitionDefinitionValidator.TryResolveStepTime(
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
                                ref completionProbes,
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
                                $"Transition step '{AppearanceTransitionDefinitionValidator.DescribeStep(step)}' dependencies did not satisfy within {step.DependencyTimeoutSeconds:0.###} seconds.";

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
                            ref completionProbes,
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

            NotifyAppearanceChanged(
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
            var previousTransforms =
                new Dictionary<GameObject, AccessoryTransformState>();

            try
            {
                foreach (var pair in desired)
                {
                    previous[
                        pair.Key] =
                            pair.Key.activeSelf;
                }

                foreach (var binding in
                         _accessories.Values)
                {
                    if (binding?.Root != null)
                    {
                        previousTransforms[
                            binding.Root] =
                                AccessoryTransformState.Capture(
                                    binding.Root.transform);
                    }
                }

                foreach (var binding in
                         _outfits.Values)
                {
                    foreach (var root in
                             binding?.Roots ??
                             Array.Empty<GameObject>())
                    {
                        if (root != null &&
                            desired.TryGetValue(
                                root,
                                out var active))
                        {
                            root.SetActive(
                                active);
                        }
                    }
                }

                var selectedKeys =
                    new HashSet<string>(
                        StringComparer.Ordinal);

                foreach (var selection in
                         request.Accessories ??
                         Array.Empty<
                             AppearanceAccessorySelection>())
                {
                    if (selection == null)
                    {
                        continue;
                    }

                    selectedKeys.Add(
                        AccessoryKey(
                            selection.SlotId,
                            selection.AccessoryId));
                }

                foreach (var pair in
                         _accessories)
                {
                    var binding =
                        pair.Value;
                    var active =
                        selectedKeys.Contains(
                            pair.Key);

                    if (active)
                    {
                        if (!ApplyAccessoryAnchor(
                                pair.Key,
                                binding,
                                out error))
                        {
                            throw new InvalidOperationException(
                                error);
                        }

                        binding.Root.SetActive(
                            true);
                    }
                    else
                    {
                        binding.Root.SetActive(
                            false);

                        if (binding
                            .RestoreOriginalTransformWhenInactive)
                        {
                            RestoreOriginalAccessoryTransform(
                                binding.Root);
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                foreach (var pair in previousTransforms)
                {
                    if (pair.Key != null)
                    {
                        pair.Value.Apply(
                            pair.Key.transform);
                    }
                }

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

        private static bool ValidateAccessoryAnchorPose(
            AppearanceAccessoryBinding binding,
            out string error)
        {
            error = null;

            if (binding == null)
            {
                error =
                    "Accessory binding is missing.";
                return false;
            }

            if (!IsFinite(
                    binding.LocalPosition))
            {
                error =
                    "LocalPosition must contain only finite values.";
                return false;
            }

            if (!IsFinite(
                    binding.LocalEulerAngles))
            {
                error =
                    "LocalEulerAngles must contain only finite values.";
                return false;
            }

            if (binding.OverrideLocalScale &&
                !IsFinite(
                    binding.LocalScale))
            {
                error =
                    "LocalScale must contain only finite values when scale override is enabled.";
                return false;
            }

            return true;
        }

        private static bool IsFinite(
            Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(
            float value)
        {
            return
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private bool TryResolveAccessoryAnchor(
            AppearanceAccessoryBinding binding,
            out Transform anchor,
            out string error)
        {
            anchor = null;
            error = null;

            if (binding == null ||
                binding.Root == null)
            {
                error =
                    "Accessory binding/root is missing.";
                return false;
            }

            switch (binding.AnchorMode)
            {
                case AppearanceAccessoryAnchorMode.None:
                    return true;

                case AppearanceAccessoryAnchorMode.Transform:
                    anchor =
                        binding.AnchorTransform;

                    if (anchor == null)
                    {
                        error =
                            "Transform anchor mode requires AnchorTransform.";
                        return false;
                    }

                    break;

                case AppearanceAccessoryAnchorMode.HumanoidBone:
                    if (binding.AnchorBone ==
                        HumanBodyBones.LastBone)
                    {
                        error =
                            "Humanoid bone anchor requires a concrete HumanBodyBones value.";
                        return false;
                    }

                    var animator =
                        binding.AnchorAnimator;

                    if (animator == null)
                    {
                        animator =
                            binding.Root
                                .GetComponentInParent<Animator>();
                    }

                    if (animator == null)
                    {
                        animator =
                            GetComponentInParent<Animator>();
                    }

                    if (animator == null)
                    {
                        animator =
                            GetComponentInChildren<Animator>(
                                true);
                    }

                    if (animator == null ||
                        animator.avatar == null ||
                        !animator.isHuman)
                    {
                        error =
                            "Humanoid bone anchor requires a humanoid Animator.";
                        return false;
                    }

                    anchor =
                        animator.GetBoneTransform(
                            binding.AnchorBone);

                    if (anchor == null)
                    {
                        error =
                            $"Humanoid Animator does not expose bone '{binding.AnchorBone}'.";
                        return false;
                    }

                    break;

                default:
                    error =
                        $"Unsupported accessory anchor mode '{binding.AnchorMode}'.";
                    return false;
            }

            if (ReferenceEquals(
                    anchor,
                    binding.Root.transform) ||
                anchor.IsChildOf(
                    binding.Root.transform))
            {
                error =
                    "Accessory anchor cannot be the accessory root or one of its descendants.";
                return false;
            }

            return true;
        }

        private bool ApplyAccessoryAnchor(
            string key,
            AppearanceAccessoryBinding binding,
            out string error)
        {
            error = null;

            if (binding == null ||
                binding.Root == null)
            {
                error =
                    "Accessory binding/root is missing.";
                return false;
            }

            if (binding.AnchorMode ==
                AppearanceAccessoryAnchorMode.None)
            {
                RestoreOriginalAccessoryTransform(
                    binding.Root);
                return true;
            }

            if (!_resolvedAccessoryAnchors.TryGetValue(
                    key,
                    out var anchor) ||
                anchor == null)
            {
                if (!TryResolveAccessoryAnchor(
                        binding,
                        out anchor,
                        out error))
                {
                    return false;
                }

                if (anchor != null)
                {
                    _resolvedAccessoryAnchors[
                        key] =
                            anchor;
                }
            }

            binding.Root.transform.SetParent(
                anchor,
                false);
            binding.Root.transform.localPosition =
                binding.LocalPosition;
            binding.Root.transform.localRotation =
                Quaternion.Euler(
                    binding.LocalEulerAngles);

            if (binding.OverrideLocalScale)
            {
                binding.Root.transform.localScale =
                    binding.LocalScale;
            }

            return true;
        }

        private void RestoreOriginalAccessoryTransform(
            GameObject root)
        {
            if (root == null ||
                !_accessoryOriginalTransforms.TryGetValue(
                    root,
                    out var state))
            {
                return;
            }

            state.Apply(
                root.transform);
        }

        private void PruneAccessoryTransformStates()
        {
            var registered =
                new HashSet<GameObject>();

            foreach (var binding in
                     _accessories.Values)
            {
                if (binding?.Root != null)
                {
                    registered.Add(
                        binding.Root);
                }
            }

            var stale =
                new List<GameObject>();

            foreach (var pair in
                     _accessoryOriginalTransforms)
            {
                if (pair.Key == null ||
                    !registered.Contains(
                        pair.Key))
                {
                    stale.Add(
                        pair.Key);
                }
            }

            foreach (var root in stale)
            {
                _accessoryOriginalTransforms.Remove(
                    root);
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
                if (step == null)
                {
                    return false;
                }

                if (step.Required &&
                    (!TryCountExecutors(
                         step,
                         out var executorCount,
                         out _) ||
                     executorCount != 1))
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

                if (!TryCountExecutors(
                        step,
                        out var count,
                        out var executorProbeError))
                {
                    error =
                        executorProbeError;
                    return false;
                }

                if (count != 1)
                {
                    error =
                        count == 0
                            ? $"No transition executor handles '{step.ActionType}'."
                            : $"Multiple transition executors handle '{step.ActionType}'.";
                    return false;
                }

                if (step.Blocking)
                {
                    if (!TryCountCompletionProbes(
                            step,
                            out var completionCount,
                            out var completionProbeError))
                    {
                        error =
                            completionProbeError;
                        return false;
                    }

                    if (completionCount != 1)
                    {
                        error =
                            $"Blocking transition action '{step.ActionType}' requires exactly one completion probe.";
                        return false;
                    }
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

                    if (!TryCountExecutors(
                            dependencyStep,
                            out var executorCount,
                            out var dependencyExecutorError))
                    {
                        error =
                            dependencyExecutorError;
                        return false;
                    }

                    if (!TryCountCompletionProbes(
                            dependencyStep,
                            out var completionCount,
                            out var dependencyCompletionError))
                    {
                        error =
                            dependencyCompletionError;
                        return false;
                    }

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

                if (!TryCountExecutors(
                        step,
                        out var count,
                        out var cancellationProbeError))
                {
                    error =
                        cancellationProbeError;
                    return false;
                }

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
                if (!IsExecutorAlive(executor))
                {
                    continue;
                }

                if (!TryCanExecute(
                        executor,
                        step,
                        out var canExecute,
                        out error))
                {
                    return false;
                }

                if (!canExecute)
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
            ref Dictionary<
                AppearanceTransitionStep,
                IAppearanceTransitionStepCompletionProbe>
                completionProbes,
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
                        $"Dependency action '{dependencyId}' was not successfully started before step '{AppearanceTransitionDefinitionValidator.DescribeStep(waitingStep)}'.";
                    return false;
                }

                if (!TryIsStepComplete(
                        dependencyStep,
                        ref completionProbes,
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

                    if (waitingStep.DependencyMode ==
                        AppearanceTransitionDependencyMode.Any)
                    {
                        satisfied = true;
                        return true;
                    }
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
                    AppearanceTransitionDependencyMode.All &&
                completedCount ==
                    dependencyIds.Length;
            return true;
        }

        private bool TryIsStepComplete(
            AppearanceTransitionStep step,
            ref Dictionary<
                AppearanceTransitionStep,
                IAppearanceTransitionStepCompletionProbe>
                completionProbes,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (completionProbes != null &&
                completionProbes.TryGetValue(
                    step,
                    out var cachedProbe))
            {
                if (!TryIsCompletionProbeUsable(
                        step,
                        cachedProbe,
                        out var usable,
                        out error))
                {
                    return false;
                }

                if (usable)
                {
                    return TryPollCompletionProbe(
                        cachedProbe,
                        step,
                        out complete,
                        out error);
                }

                completionProbes.Remove(
                    step);
            }

            IAppearanceTransitionStepCompletionProbe
                selected = null;
            var count = 0;

            foreach (var executor in
                     _executors)
            {
                if (!IsExecutorAlive(executor) ||
                    executor is not
                        IAppearanceTransitionStepCompletionProbe
                            probe)
                {
                    continue;
                }

                if (!TryCanExecute(
                        executor,
                        step,
                        out var canExecute,
                        out error))
                {
                    return false;
                }

                if (!canExecute)
                {
                    continue;
                }

                if (!TryCanTrackCompletion(
                        probe,
                        step,
                        out var canTrack,
                        out error))
                {
                    return false;
                }

                if (!canTrack)
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

            completionProbes ??=
                new Dictionary<
                    AppearanceTransitionStep,
                    IAppearanceTransitionStepCompletionProbe>();
            completionProbes[
                step] =
                    selected;

            return TryPollCompletionProbe(
                selected,
                step,
                out complete,
                out error);
        }

        private bool TryIsCompletionProbeUsable(
            AppearanceTransitionStep step,
            IAppearanceTransitionStepCompletionProbe probe,
            out bool usable,
            out string error)
        {
            usable = false;
            error = null;

            if (probe is not
                    IAppearanceTransitionStepExecutor executor ||
                !IsExecutorAlive(executor))
            {
                return true;
            }

            if (!TryCanExecute(
                    executor,
                    step,
                    out var canExecute,
                    out error))
            {
                return false;
            }

            if (!canExecute)
            {
                return true;
            }

            if (!TryCanTrackCompletion(
                    probe,
                    step,
                    out var canTrack,
                    out error))
            {
                return false;
            }

            usable =
                canTrack;
            return true;
        }

        private static bool TryPollCompletionProbe(
            IAppearanceTransitionStepCompletionProbe probe,
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            try
            {
                return probe.TryIsComplete(
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

        private bool TryCountCompletionProbes(
            AppearanceTransitionStep step,
            out int count,
            out string error)
        {
            count = 0;
            error = null;

            foreach (var executor in
                     _executors)
            {
                if (!IsExecutorAlive(executor) ||
                    executor is not
                        IAppearanceTransitionStepCompletionProbe
                            probe)
                {
                    continue;
                }

                if (!TryCanExecute(
                        executor,
                        step,
                        out var canExecute,
                        out error))
                {
                    return false;
                }

                if (!canExecute)
                {
                    continue;
                }

                if (!TryCanTrackCompletion(
                        probe,
                        step,
                        out var canTrack,
                        out error))
                {
                    return false;
                }

                if (canTrack)
                {
                    count++;
                }
            }

            return true;
        }

        private bool TryCountExecutors(
            AppearanceTransitionStep step,
            out int count,
            out string error)
        {
            count = 0;
            error = null;

            foreach (var executor in
                     _executors)
            {
                if (!IsExecutorAlive(executor))
                {
                    continue;
                }

                if (!TryCanExecute(
                        executor,
                        step,
                        out var canExecute,
                        out error))
                {
                    return false;
                }

                if (canExecute)
                {
                    count++;
                }
            }

            return true;
        }

        private bool TryCanExecute(
            IAppearanceTransitionStepExecutor executor,
            AppearanceTransitionStep step,
            out bool canExecute,
            out string error)
        {
            canExecute = false;
            error = null;

            try
            {
                canExecute =
                    executor.CanExecute(
                        step);
                return true;
            }
            catch (Exception exception)
            {
                _executorProbeFailureCount++;
                error =
                    "Transition executor CanExecute failed: " +
                    exception.Message;
                return false;
            }
        }

        private bool TryCanTrackCompletion(
            IAppearanceTransitionStepCompletionProbe probe,
            AppearanceTransitionStep step,
            out bool canTrack,
            out string error)
        {
            canTrack = false;
            error = null;

            try
            {
                canTrack =
                    probe.CanTrackCompletion(
                        step);
                return true;
            }
            catch (Exception exception)
            {
                _executorProbeFailureCount++;
                error =
                    "Transition completion probe CanTrackCompletion failed: " +
                    exception.Message;
                return false;
            }
        }

        private void RefreshExecutorsIfNeeded()
        {
            if (!autoFindTransitionExecutors)
            {
                return;
            }

            var needsRebuild =
                _executors.Length == 0;

            if (!needsRebuild)
            {
                foreach (var executor in _executors)
                {
                    if (!IsExecutorAlive(executor))
                    {
                        needsRebuild = true;
                        break;
                    }
                }
            }

            if (!needsRebuild)
            {
                _nextTransitionExecutorResolveAt = 0d;
                return;
            }

            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now <
                _nextTransitionExecutorResolveAt)
            {
                return;
            }

            _nextTransitionExecutorResolveAt =
                now +
                TransitionExecutorDiscoveryRetrySeconds;
            RebuildExecutors();

            if (_executors.Length > 0)
            {
                _nextTransitionExecutorResolveAt = 0d;
            }
        }

        private static bool IsExecutorAlive(
            IAppearanceTransitionStepExecutor executor)
        {
            if (executor == null)
            {
                return false;
            }

            return executor is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
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
                if (behaviour != null &&
                    behaviour is
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

        private void ClearCurrentPresetAndNotify()
        {
            if (string.IsNullOrWhiteSpace(
                    _currentPresetId))
            {
                return;
            }

            _currentPresetId = null;
            NotifyAppearanceChanged(
                Current);
        }

        private void SetState(
            AppearanceRuntimeState state,
            string error)
        {
            _state = state;
            _lastError = error;

            NotifyStatusChanged(
                Status);
        }

        private void NotifyAppearanceChanged(
            AppearanceStateSnapshot snapshot)
        {
            var subscribers =
                AppearanceChanged;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action<AppearanceStateSnapshot> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(snapshot);
                }
                catch
                {
                    _appearanceSubscriberFailureCount++;
                }
            }
        }

        private void NotifyStatusChanged(
            AppearanceRuntimeStatus status)
        {
            var subscribers =
                StatusChanged;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action<AppearanceRuntimeStatus> subscriber in
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

        private bool CurrentAppearanceMatchesPreset(
            AppearancePreset preset)
        {
            if (preset == null ||
                !string.Equals(
                    preset.OutfitId,
                    _currentOutfitId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var selections =
                preset.Accessories ??
                Array.Empty<
                    AppearanceAccessorySelection>();

            if (selections.Length !=
                _currentAccessories.Count)
            {
                return false;
            }

            foreach (var selection in
                     selections)
            {
                if (selection == null ||
                    string.IsNullOrWhiteSpace(
                        selection.SlotId) ||
                    !_currentAccessories.TryGetValue(
                        selection.SlotId,
                        out var accessoryId) ||
                    !string.Equals(
                        accessoryId,
                        selection.AccessoryId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private AppearanceAccessorySelection[]
            CaptureCurrentAccessories()
        {
            return ToSelections(
                _currentAccessories);
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
                IReadOnlyDictionary<string, string> map)
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
                    "appearance.transition.queue_depth",
                    _pending.Count,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.queue_limit",
                    MaxQueuedTransitions,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.queue_rejected",
                    _transitionQueueRejectedCount,
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
                    "appearance.subscriber.appearance_failures",
                    _appearanceSubscriberFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.subscriber.status_failures",
                    _statusSubscriberFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.executor_probe_failures",
                    _executorProbeFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.progress",
                    Status.TransitionProgress01,
                    "ratio"));
        }

        private sealed class AppearanceConfigurationSnapshot
        {
            public AppearanceOutfitBinding[] Outfits;
            public AppearanceAccessoryBinding[] Accessories;
            public AppearancePresetBinding[] Presets;
            public AppearanceTransitionBinding[] Transitions;
            public MonoBehaviour[] TransitionExecutorBehaviours;
            public string DefaultPresetId;
            public Dictionary<string, AppearanceOutfitBinding>
                ResolvedOutfits;
            public Dictionary<string, AppearanceAccessoryBinding>
                ResolvedAccessories;
            public Dictionary<
                string,
                List<AppearanceAccessoryBinding>>
                ResolvedAccessoriesBySlot;
            public Dictionary<string, Transform>
                ResolvedAccessoryAnchors;
            public Dictionary<GameObject, AccessoryTransformState>
                AccessoryOriginalTransforms;
            public Dictionary<string, AppearancePreset>
                ResolvedPresets;
            public HashSet<string>
                AuthoredPresetIds;
            public Dictionary<string, AppearanceTransitionPreset>
                ResolvedTransitions;
            public List<string>
                PresetIds;
            public List<string>
                TransitionIds;
            public IAppearanceTransitionStepExecutor[]
                Executors;
            public double NextTransitionExecutorResolveAt;
            public AppearanceRuntimeState State;
            public string LastError;
        }

        private readonly struct AccessoryTransformState
        {
            private AccessoryTransformState(
                Transform parent,
                Vector3 localPosition,
                Quaternion localRotation,
                Vector3 localScale,
                int siblingIndex)
            {
                Parent = parent;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
                SiblingIndex = siblingIndex;
            }

            public Transform Parent { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
            public int SiblingIndex { get; }

            public static AccessoryTransformState Capture(
                Transform transform)
            {
                return new AccessoryTransformState(
                    transform.parent,
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale,
                    transform.GetSiblingIndex());
            }

            public void Apply(
                Transform transform)
            {
                if (transform == null)
                {
                    return;
                }

                transform.SetParent(
                    Parent,
                    false);
                transform.localPosition =
                    LocalPosition;
                transform.localRotation =
                    LocalRotation;
                transform.localScale =
                    LocalScale;

                if (Parent != null &&
                    Parent.childCount > 0)
                {
                    transform.SetSiblingIndex(
                        Math.Max(
                            0,
                            Math.Min(
                                SiblingIndex,
                                Parent.childCount - 1)));
                }
            }
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
