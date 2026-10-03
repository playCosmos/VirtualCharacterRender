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
        IRuntimeMetricsSource
    {
        [SerializeField] private string runtimeId =
            "appearance.main";

        [SerializeField] private string defaultPresetId = "";
        [SerializeField] private bool applyDefaultOnAwake = false;

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
        private Coroutine _transitionCoroutine;
        private AppearanceTransitionPreset _activeTransition;
        private AppearanceChangeRequest _activeRequest;

        private long _appearanceChangeCount;
        private long _transitionStartCount;
        private long _transitionCommitCount;
        private long _transitionFailureCount;
        private long _transitionQueuedCount;
        private long _transitionInterruptedCount;

        public AppearanceRuntimeStatus Status =>
            new(
                runtimeId,
                _state,
                _currentPresetId,
                _currentOutfitId,
                _activeTransitionId,
                _transitionCommitted,
                _lastError);

        public AppearanceStateSnapshot Current =>
            new(
                _currentPresetId,
                _currentOutfitId,
                CaptureCurrentAccessories());

        public IReadOnlyList<string> PresetIds =>
            _presetIds;

        public IReadOnlyList<string> TransitionIds =>
            _transitionIds;

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
            }

            _pending.Clear();
            _activeTransition = null;
            _activeRequest = null;
            _activeTransitionId = null;
            _transitionCommitted = false;

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

            _outfits.Clear();
            _accessories.Clear();
            _accessoriesBySlot.Clear();
            _presets.Clear();
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

            _presetIds.Sort(
                StringComparer.Ordinal);
            _transitionIds.Sort(
                StringComparer.Ordinal);

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

            RebuildConfiguration(
                out _);
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
                    StopCoroutine(
                        _transitionCoroutine);
                    _transitionCoroutine = null;
                    _activeTransition = null;
                    _activeRequest = null;
                    _activeTransitionId = null;
                    _transitionCommitted = false;
                    _transitionInterruptedCount++;
                    SetState(
                        AppearanceRuntimeState.Ready,
                        null);
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

            Array.Sort(
                steps,
                (left, right) =>
                    left.TimeSeconds.CompareTo(
                        right.TimeSeconds));

            var started =
                Time.unscaledTimeAsDouble;

            foreach (var step in steps)
            {
                while (Time.unscaledTimeAsDouble -
                       started <
                       step.TimeSeconds)
                {
                    yield return null;
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

                if (TryExecuteStep(
                        step,
                        out var stepError))
                {
                    continue;
                }

                if (!step.Required)
                {
                    _lastError =
                        stepError;
                    continue;
                }

                _transitionFailureCount++;

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
                        yield break;
                    }

                    _transitionCommitted = true;
                    _transitionCommitCount++;
                    FinishTransition(
                        stepError);
                    yield break;
                }

                FinishTransition(
                    stepError);
                yield break;
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

            var commitCount = 0;

            foreach (var step in
                     transition.Steps ??
                     Array.Empty<
                         AppearanceTransitionStep>())
            {
                if (step == null ||
                    double.IsNaN(
                        step.TimeSeconds) ||
                    double.IsInfinity(
                        step.TimeSeconds) ||
                    step.TimeSeconds < 0.0)
                {
                    error =
                        $"Transition '{transition.Id}' contains an invalid step time.";
                    return false;
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

                if (step.ActionType.StartsWith(
                        "appearance.",
                        StringComparison.Ordinal))
                {
                    error =
                        $"Transition '{transition.Id}' cannot recursively execute appearance actions.";
                    return false;
                }
            }

            if (commitCount != 1)
            {
                error =
                    $"Transition '{transition.Id}' must contain exactly one appearance commit step.";
                return false;
            }

            return true;
        }

        private bool ValidateRequiredExecutors(
            AppearanceTransitionPreset transition,
            out string error)
        {
            error = null;

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
                        executor)
                {
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

            _transitionCoroutine = null;
            _activeTransition = null;
            _activeRequest = null;
            _activeTransitionId = null;
            _transitionCommitted = false;

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
