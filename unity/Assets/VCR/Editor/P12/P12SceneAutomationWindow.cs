using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Environment.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Editor.P12
{
    public sealed class P12SceneAutomationWindow :
        EditorWindow
    {
        private PropEventActionHandler _propHandler;
        private EffectEventActionHandler _effectHandler;
        private SceneSequenceEventActionHandler _sequenceHandler;
        private BasicEnvironmentRuntime _environmentRuntime;

        private SerializedObject _serializedProps;
        private SerializedObject _serializedEffects;
        private SerializedObject _serializedSequences;
        private SerializedObject _serializedEnvironment;
        private SerializedProperty _props;
        private SerializedProperty _effects;
        private SerializedProperty _sequences;
        private SerializedProperty _environmentStates;

        private string _lastValidPropJson;
        private string _lastValidEffectJson;
        private string _lastValidSequenceJson;
        private bool _propPending;
        private bool _effectPending;
        private bool _sequencePending;
        private Vector2 _scroll;
        private string _message;
        private MessageType _messageType =
            MessageType.Info;

        [MenuItem("VCR/P12/Open Scene Automation")]
        public static void Open()
        {
            GetWindow<
                    P12SceneAutomationWindow>(
                    "VCR Scene Automation")
                .Show();
        }

        private void OnEnable()
        {
            ResolveFromSelection();
            RebindAll();
        }

        private void OnSelectionChange()
        {
            ResolveFromSelection();
            RebindAll();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Scene Automation Authoring",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Authors logical Prop and Effect bindings used by EventRuntime actions. Environment state bindings are shown read-only so event-rule placeholders can reference stable logical ids without duplicating P6 environment authoring.",
                MessageType.Info);

            DrawRuntimeSelectors();

            if (!string.IsNullOrWhiteSpace(
                    _message))
            {
                EditorGUILayout.HelpBox(
                    _message,
                    _messageType);
            }

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            DrawPropAuthoring();
            EditorGUILayout.Space();
            DrawEffectAuthoring();
            EditorGUILayout.Space();
            DrawSequenceAuthoring();
            EditorGUILayout.Space();
            DrawEnvironmentReference();

            EditorGUILayout.EndScrollView();

            ApplyPendingSerializedChanges();
        }

        private void DrawRuntimeSelectors()
        {
            var nextProp =
                (PropEventActionHandler)
                EditorGUILayout.ObjectField(
                    "Prop Handler",
                    _propHandler,
                    typeof(
                        PropEventActionHandler),
                    true);
            var nextEffect =
                (EffectEventActionHandler)
                EditorGUILayout.ObjectField(
                    "Effect Handler",
                    _effectHandler,
                    typeof(
                        EffectEventActionHandler),
                    true);
            var nextSequence =
                (SceneSequenceEventActionHandler)
                EditorGUILayout.ObjectField(
                    "Sequence Handler",
                    _sequenceHandler,
                    typeof(
                        SceneSequenceEventActionHandler),
                    true);
            var nextEnvironment =
                (BasicEnvironmentRuntime)
                EditorGUILayout.ObjectField(
                    "Environment Runtime",
                    _environmentRuntime,
                    typeof(
                        BasicEnvironmentRuntime),
                    true);

            if (!ReferenceEquals(
                    nextProp,
                    _propHandler) ||
                !ReferenceEquals(
                    nextEffect,
                    _effectHandler) ||
                !ReferenceEquals(
                    nextSequence,
                    _sequenceHandler) ||
                !ReferenceEquals(
                    nextEnvironment,
                    _environmentRuntime))
            {
                _propHandler =
                    nextProp;
                _effectHandler =
                    nextEffect;
                _sequenceHandler =
                    nextSequence;
                _environmentRuntime =
                    nextEnvironment;
                RebindAll();
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Resolve From Selection"))
                {
                    ResolveFromSelection(
                        forceSceneSearch:
                            false);
                    RebindAll();
                }

                if (GUILayout.Button(
                        "Resolve From Scene"))
                {
                    ResolveFromSelection(
                        forceSceneSearch:
                            true);
                    RebindAll();
                }

                if (GUILayout.Button(
                        "Open Event Nodes"))
                {
                    var host =
                        UnityEngine.Object
                            .FindFirstObjectByType<
                                EventRuntimeHost>();

                    if (host != null)
                    {
                        P12EventNodeEditorWindow
                            .OpenWithHost(
                                host);
                    }
                    else
                    {
                        SetMessage(
                            "No EventRuntimeHost is available in the open scene.",
                            MessageType.Warning);
                    }
                }
            }
        }

        private void DrawPropAuthoring()
        {
            EditorGUILayout.LabelField(
                "Props",
                EditorStyles.boldLabel);

            if (_propHandler == null ||
                _serializedProps == null ||
                _props == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a PropEventActionHandler. P11/P12 runtime-scene generation adds one to the application bootstrap by default.",
                    MessageType.None);
                return;
            }

            _serializedProps.Update();

            if (_propPending)
            {
                EditorGUILayout.HelpBox(
                    "Prop bindings contain pending edits. Validate & Apply before treating them as runtime-ready.",
                    MessageType.Warning);
            }

            for (var i = 0;
                 i < _props.arraySize;
                 i++)
            {
                var binding =
                    _props.GetArrayElementAtIndex(
                        i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            binding.FindPropertyRelative(
                                "PropId"),
                            new GUIContent(
                                $"Prop {i + 1} ID"));

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    64f)))
                        {
                            Undo.RecordObject(
                                _propHandler,
                                "Delete Prop Binding");
                            _props
                                .DeleteArrayElementAtIndex(
                                    i);
                            _serializedProps
                                .ApplyModifiedProperties();
                            EditorUtility.SetDirty(
                                _propHandler);
                            _propPending = true;
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative(
                            "Roots"),
                        includeChildren:
                            true);
                }
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Prop"))
                {
                    AddEmptyProp();
                }

                using (new EditorGUI.DisabledScope(
                           Selection.activeGameObject ==
                           null))
                {
                    if (GUILayout.Button(
                            "Add Selected As Prop"))
                    {
                        AddSelectedProp();
                    }
                }

                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidatePropBindings();
                }
            }
        }

        private void DrawEffectAuthoring()
        {
            EditorGUILayout.LabelField(
                "Effects",
                EditorStyles.boldLabel);

            if (_effectHandler == null ||
                _serializedEffects == null ||
                _effects == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an EffectEventActionHandler to author logical particle/effect ids.",
                    MessageType.None);
                return;
            }

            _serializedEffects.Update();

            if (_effectPending)
            {
                EditorGUILayout.HelpBox(
                    "Effect bindings contain pending edits. Validate & Apply before treating them as runtime-ready.",
                    MessageType.Warning);
            }

            for (var i = 0;
                 i < _effects.arraySize;
                 i++)
            {
                var binding =
                    _effects.GetArrayElementAtIndex(
                        i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            binding.FindPropertyRelative(
                                "EffectId"),
                            new GUIContent(
                                $"Effect {i + 1} ID"));

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    64f)))
                        {
                            Undo.RecordObject(
                                _effectHandler,
                                "Delete Effect Binding");
                            _effects
                                .DeleteArrayElementAtIndex(
                                    i);
                            _serializedEffects
                                .ApplyModifiedProperties();
                            EditorUtility.SetDirty(
                                _effectHandler);
                            _effectPending = true;
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative(
                            "Root"));
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative(
                            "ParticleSystems"),
                        includeChildren:
                            true);
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative(
                            "RestartOnPlay"));
                    EditorGUILayout.PropertyField(
                        binding.FindPropertyRelative(
                            "DeactivateOnStop"));
                }
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Effect"))
                {
                    AddEmptyEffect();
                }

                using (new EditorGUI.DisabledScope(
                           Selection.activeGameObject ==
                           null))
                {
                    if (GUILayout.Button(
                            "Add Selected As Effect"))
                    {
                        AddSelectedEffect();
                    }
                }

                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateEffectBindings();
                }
            }
        }

        private void DrawSequenceAuthoring()
        {
            EditorGUILayout.LabelField(
                "Timed Scene Sequences",
                EditorStyles.boldLabel);

            if (_sequenceHandler == null ||
                _serializedSequences == null ||
                _sequences == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a SceneSequenceEventActionHandler. Runtime-scene generation adds one to the application bootstrap by default.",
                    MessageType.None);
                return;
            }

            _serializedSequences.Update();

            if (_sequencePending)
            {
                EditorGUILayout.HelpBox(
                    "Scene sequences contain pending edits. Validate & Apply before treating them as runtime-ready.",
                    MessageType.Warning);
            }

            for (var i = 0;
                 i < _sequences.arraySize;
                 i++)
            {
                var sequence =
                    _sequences.GetArrayElementAtIndex(
                        i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            sequence.FindPropertyRelative(
                                "SequenceId"),
                            new GUIContent(
                                $"Sequence {i + 1} ID"));

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    64f)))
                        {
                            Undo.RecordObject(
                                _sequenceHandler,
                                "Delete Scene Sequence");
                            _sequences
                                .DeleteArrayElementAtIndex(
                                    i);
                            _serializedSequences
                                .ApplyModifiedProperties();
                            EditorUtility.SetDirty(
                                _sequenceHandler);
                            _sequencePending = true;
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        sequence.FindPropertyRelative(
                            "Steps"),
                        includeChildren:
                            true);
                    EditorGUILayout.PropertyField(
                        sequence.FindPropertyRelative(
                            "CancellationSteps"),
                        new GUIContent(
                            "Cancellation Cleanup"),
                        includeChildren:
                            true);
                }
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Sequence"))
                {
                    AddEmptySequence();
                }

                if (GUILayout.Button(
                        "Add Scene Change Starter"))
                {
                    AddSceneChangeStarterSequence();
                }

                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateSequenceBindings();
                }
            }

            EditorGUILayout.HelpBox(
                "scene.sequence_play runs one authored sequence at a time using unscaled time. Required-step failure executes cancellation cleanup. scene.sequence_cancel is accepted only when the active sequence defines explicit cleanup actions.",
                MessageType.None);
        }

        private void AddEmptySequence()
        {
            Undo.RecordObject(
                _sequenceHandler,
                "Add Scene Sequence");

            var index =
                _sequences.arraySize;
            _sequences.arraySize =
                index + 1;
            var sequence =
                _sequences.GetArrayElementAtIndex(
                    index);
            sequence.FindPropertyRelative(
                    "SequenceId")
                .stringValue =
                    BuildUniqueLogicalId(
                        "scene-sequence",
                        _sequences,
                        "SequenceId",
                        index);
            sequence.FindPropertyRelative(
                    "Steps")
                .arraySize = 0;
            sequence.FindPropertyRelative(
                    "CancellationSteps")
                .arraySize = 0;
            _sequencePending = true;
        }

        private void AddSceneChangeStarterSequence()
        {
            AddEmptySequence();

            var index =
                _sequences.arraySize - 1;
            var sequence =
                _sequences.GetArrayElementAtIndex(
                    index);
            sequence.FindPropertyRelative(
                    "SequenceId")
                .stringValue =
                    BuildUniqueLogicalId(
                        "scene-change",
                        _sequences,
                        "SequenceId",
                        index);

            var steps =
                sequence.FindPropertyRelative(
                    "Steps");
            steps.arraySize = 3;
            ConfigureSequenceStep(
                steps.GetArrayElementAtIndex(
                    0),
                0f,
                EventActionTypes
                    .EnvironmentSetState,
                null,
                "Fade",
                "state-id",
                0.5,
                true,
                true);
            ConfigureSequenceStep(
                steps.GetArrayElementAtIndex(
                    1),
                0.2f,
                EventActionTypes
                    .PropSetActive,
                "props.main",
                null,
                "prop-id",
                1.0,
                true,
                true);
            ConfigureSequenceStep(
                steps.GetArrayElementAtIndex(
                    2),
                0.35f,
                EventActionTypes
                    .EffectPlay,
                "effects.main",
                null,
                "effect-id",
                0.0,
                false,
                false);

            var cleanup =
                sequence.FindPropertyRelative(
                    "CancellationSteps");
            cleanup.arraySize = 2;
            ConfigureSequenceStep(
                cleanup.GetArrayElementAtIndex(
                    0),
                0f,
                EventActionTypes
                    .EffectStop,
                "effects.main",
                null,
                "effect-id",
                0.0,
                false,
                false);
            ConfigureSequenceStep(
                cleanup.GetArrayElementAtIndex(
                    1),
                0f,
                EventActionTypes
                    .PropSetActive,
                "props.main",
                null,
                "prop-id",
                0.0,
                true,
                false);

            _sequencePending = true;
        }

        private static void ConfigureSequenceStep(
            SerializedProperty step,
            float timeSeconds,
            string actionType,
            string targetId,
            string name,
            string text,
            double value,
            bool hasValue,
            bool required)
        {
            step.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
            step.FindPropertyRelative(
                    "ActionType")
                .stringValue =
                    actionType ??
                    string.Empty;
            step.FindPropertyRelative(
                    "TargetId")
                .stringValue =
                    targetId ??
                    string.Empty;
            step.FindPropertyRelative(
                    "Name")
                .stringValue =
                    name ??
                    string.Empty;
            step.FindPropertyRelative(
                    "Text")
                .stringValue =
                    text ??
                    string.Empty;
            step.FindPropertyRelative(
                    "Value")
                .doubleValue =
                    value;
            step.FindPropertyRelative(
                    "HasValue")
                .boolValue =
                    hasValue;
            step.FindPropertyRelative(
                    "Required")
                .boolValue =
                    required;
        }

        private void ValidateSequenceBindings()
        {
            _serializedSequences
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _sequenceHandler);

            if (_sequenceHandler.RebuildBindings(
                    out var error))
            {
                _lastValidSequenceJson =
                    EditorJsonUtility.ToJson(
                        _sequenceHandler,
                        prettyPrint:
                            false);
                _sequencePending = false;
                SetMessage(
                    $"Scene sequences validated: {_sequences.arraySize}.",
                    MessageType.Info);
                RebindSequences();
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    _lastValidSequenceJson))
            {
                EditorJsonUtility
                    .FromJsonOverwrite(
                        _lastValidSequenceJson,
                        _sequenceHandler);
                _sequenceHandler.RebuildBindings(
                    out _);
                EditorUtility.SetDirty(
                    _sequenceHandler);
                RebindSequences();
                _sequencePending = false;
                SetMessage(
                    "Scene sequence validation failed and edits were rolled back: " +
                    error,
                    MessageType.Error);
                return;
            }

            SetMessage(
                "Scene sequence validation failed: " +
                error,
                MessageType.Error);
        }

        private void DrawEnvironmentReference()
        {
            EditorGUILayout.LabelField(
                "Environment State IDs",
                EditorStyles.boldLabel);

            if (_environmentRuntime == null ||
                _serializedEnvironment == null ||
                _environmentStates == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a BasicEnvironmentRuntime to inspect logical environment state ids.",
                    MessageType.None);
                return;
            }

            _serializedEnvironment.Update();

            EditorGUILayout.LabelField(
                "Environment ID",
                _environmentRuntime.Status
                    .EnvironmentId ??
                "<none>");
            EditorGUILayout.LabelField(
                "Current State",
                _environmentRuntime.Status
                    .StateId ??
                "<none>");

            if (_environmentStates.arraySize == 0)
            {
                EditorGUILayout.LabelField(
                    "<no explicit state bindings>");
                return;
            }

            for (var i = 0;
                 i < _environmentStates.arraySize;
                 i++)
            {
                var binding =
                    _environmentStates
                        .GetArrayElementAtIndex(
                            i);
                var stateId =
                    binding
                        .FindPropertyRelative(
                            "stateId")
                        .stringValue;
                var root =
                    binding
                        .FindPropertyRelative(
                            "root")
                        .objectReferenceValue as
                        GameObject;

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(
                            stateId)
                            ? "<blank>"
                            : stateId,
                        root != null
                            ? root.name
                            : "<no root>");

                    if (GUILayout.Button(
                            "Copy ID",
                            GUILayout.Width(
                                66f)))
                    {
                        EditorGUIUtility
                            .systemCopyBuffer =
                                stateId ??
                                string.Empty;
                        SetMessage(
                            $"Environment state id '{stateId}' copied.",
                            MessageType.Info);
                    }
                }
            }
        }

        private void AddEmptyProp()
        {
            Undo.RecordObject(
                _propHandler,
                "Add Prop Binding");

            var index =
                _props.arraySize;
            _props.arraySize =
                index + 1;
            var binding =
                _props.GetArrayElementAtIndex(
                    index);
            binding.FindPropertyRelative(
                    "PropId")
                .stringValue =
                    BuildUniqueLogicalId(
                        "prop",
                        _props,
                        "PropId",
                        index);
            binding.FindPropertyRelative(
                    "Roots")
                .arraySize = 0;
            _propPending = true;
        }

        private void AddSelectedProp()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            AddEmptyProp();

            var index =
                _props.arraySize - 1;
            var binding =
                _props.GetArrayElementAtIndex(
                    index);
            binding.FindPropertyRelative(
                    "PropId")
                .stringValue =
                    BuildUniqueLogicalId(
                        SanitizeLogicalId(
                            selected.name),
                        _props,
                        "PropId",
                        index);
            var roots =
                binding.FindPropertyRelative(
                    "Roots");
            roots.arraySize = 1;
            roots.GetArrayElementAtIndex(
                    0)
                .objectReferenceValue =
                    selected;
        }

        private void AddEmptyEffect()
        {
            Undo.RecordObject(
                _effectHandler,
                "Add Effect Binding");

            var index =
                _effects.arraySize;
            _effects.arraySize =
                index + 1;
            var binding =
                _effects.GetArrayElementAtIndex(
                    index);
            binding.FindPropertyRelative(
                    "EffectId")
                .stringValue =
                    BuildUniqueLogicalId(
                        "effect",
                        _effects,
                        "EffectId",
                        index);
            binding.FindPropertyRelative(
                    "Root")
                .objectReferenceValue =
                    null;
            binding.FindPropertyRelative(
                    "ParticleSystems")
                .arraySize = 0;
            binding.FindPropertyRelative(
                    "RestartOnPlay")
                .boolValue =
                    true;
            binding.FindPropertyRelative(
                    "DeactivateOnStop")
                .boolValue =
                    false;
            _effectPending = true;
        }

        private void AddSelectedEffect()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            AddEmptyEffect();

            var index =
                _effects.arraySize - 1;
            var binding =
                _effects.GetArrayElementAtIndex(
                    index);
            binding.FindPropertyRelative(
                    "EffectId")
                .stringValue =
                    BuildUniqueLogicalId(
                        SanitizeLogicalId(
                            selected.name),
                        _effects,
                        "EffectId",
                        index);
            binding.FindPropertyRelative(
                    "Root")
                .objectReferenceValue =
                    selected;

            var systems =
                selected.GetComponentsInChildren<
                    ParticleSystem>(
                    includeInactive:
                        true);
            var particleProperty =
                binding.FindPropertyRelative(
                    "ParticleSystems");
            particleProperty.arraySize =
                systems.Length;

            for (var i = 0;
                 i < systems.Length;
                 i++)
            {
                particleProperty
                    .GetArrayElementAtIndex(
                        i)
                    .objectReferenceValue =
                        systems[i];
            }
        }

        private void ValidatePropBindings()
        {
            _serializedProps
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _propHandler);

            if (_propHandler.RebuildBindings(
                    out var error))
            {
                _lastValidPropJson =
                    EditorJsonUtility.ToJson(
                        _propHandler,
                        prettyPrint:
                            false);
                _propPending = false;
                SetMessage(
                    $"Prop bindings validated: {_props.arraySize}.",
                    MessageType.Info);
                RebindProps();
                return;
            }

            RestorePropSnapshot(
                error);
        }

        private void ValidateEffectBindings()
        {
            _serializedEffects
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _effectHandler);

            if (_effectHandler.RebuildBindings(
                    out var error))
            {
                _lastValidEffectJson =
                    EditorJsonUtility.ToJson(
                        _effectHandler,
                        prettyPrint:
                            false);
                _effectPending = false;
                SetMessage(
                    $"Effect bindings validated: {_effects.arraySize}.",
                    MessageType.Info);
                RebindEffects();
                return;
            }

            RestoreEffectSnapshot(
                error);
        }

        private void RestorePropSnapshot(
            string error)
        {
            if (!string.IsNullOrWhiteSpace(
                    _lastValidPropJson))
            {
                EditorJsonUtility
                    .FromJsonOverwrite(
                        _lastValidPropJson,
                        _propHandler);
                _propHandler.RebuildBindings(
                    out _);
                EditorUtility.SetDirty(
                    _propHandler);
                RebindProps();
                _propPending = false;
                SetMessage(
                    "Prop binding validation failed and edits were rolled back: " +
                    error,
                    MessageType.Error);
                return;
            }

            SetMessage(
                "Prop binding validation failed: " +
                error,
                MessageType.Error);
        }

        private void RestoreEffectSnapshot(
            string error)
        {
            if (!string.IsNullOrWhiteSpace(
                    _lastValidEffectJson))
            {
                EditorJsonUtility
                    .FromJsonOverwrite(
                        _lastValidEffectJson,
                        _effectHandler);
                _effectHandler.RebuildBindings(
                    out _);
                EditorUtility.SetDirty(
                    _effectHandler);
                RebindEffects();
                _effectPending = false;
                SetMessage(
                    "Effect binding validation failed and edits were rolled back: " +
                    error,
                    MessageType.Error);
                return;
            }

            SetMessage(
                "Effect binding validation failed: " +
                error,
                MessageType.Error);
        }

        private void ApplyPendingSerializedChanges()
        {
            if (_serializedProps != null &&
                _serializedProps
                    .ApplyModifiedProperties())
            {
                _propPending = true;
                EditorUtility.SetDirty(
                    _propHandler);
            }

            if (_serializedEffects != null &&
                _serializedEffects
                    .ApplyModifiedProperties())
            {
                _effectPending = true;
                EditorUtility.SetDirty(
                    _effectHandler);
            }

            if (_serializedSequences != null &&
                _serializedSequences
                    .ApplyModifiedProperties())
            {
                _sequencePending = true;
                EditorUtility.SetDirty(
                    _sequenceHandler);
            }
        }

        private void ResolveFromSelection(
            bool forceSceneSearch = false)
        {
            var selected =
                Selection.activeGameObject;

            if (!forceSceneSearch &&
                selected != null)
            {
                _propHandler =
                    selected.GetComponentInParent<
                        PropEventActionHandler>() ??
                    selected.GetComponentInChildren<
                        PropEventActionHandler>(
                            true) ??
                    _propHandler;
                _effectHandler =
                    selected.GetComponentInParent<
                        EffectEventActionHandler>() ??
                    selected.GetComponentInChildren<
                        EffectEventActionHandler>(
                            true) ??
                    _effectHandler;
                _sequenceHandler =
                    selected.GetComponentInParent<
                        SceneSequenceEventActionHandler>() ??
                    selected.GetComponentInChildren<
                        SceneSequenceEventActionHandler>(
                            true) ??
                    _sequenceHandler;
                _environmentRuntime =
                    selected.GetComponentInParent<
                        BasicEnvironmentRuntime>() ??
                    selected.GetComponentInChildren<
                        BasicEnvironmentRuntime>(
                            true) ??
                    _environmentRuntime;
                return;
            }

            _propHandler =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        PropEventActionHandler>(
                        FindObjectsInactive.Include);
            _effectHandler =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        EffectEventActionHandler>(
                        FindObjectsInactive.Include);
            _sequenceHandler =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        SceneSequenceEventActionHandler>(
                        FindObjectsInactive.Include);
            _environmentRuntime =
                UnityEngine.Object
                    .FindFirstObjectByType<
                        BasicEnvironmentRuntime>(
                        FindObjectsInactive.Include);
        }

        private void RebindAll()
        {
            RebindProps();
            RebindEffects();
            RebindSequences();
            RebindEnvironment();
        }

        private void RebindProps()
        {
            _serializedProps =
                _propHandler != null
                    ? new SerializedObject(
                        _propHandler)
                    : null;
            _props =
                _serializedProps?.FindProperty(
                    "props");
            _propPending = false;
            _lastValidPropJson = null;

            if (_propHandler != null &&
                _propHandler.RebuildBindings(
                    out var error))
            {
                _lastValidPropJson =
                    EditorJsonUtility.ToJson(
                        _propHandler,
                        prettyPrint:
                            false);
            }
            else if (_propHandler != null)
            {
                SetMessage(
                    "Current prop bindings are invalid: " +
                    error,
                    MessageType.Warning);
            }
        }

        private void RebindEffects()
        {
            _serializedEffects =
                _effectHandler != null
                    ? new SerializedObject(
                        _effectHandler)
                    : null;
            _effects =
                _serializedEffects?.FindProperty(
                    "effects");
            _effectPending = false;
            _lastValidEffectJson = null;

            if (_effectHandler != null &&
                _effectHandler.RebuildBindings(
                    out var error))
            {
                _lastValidEffectJson =
                    EditorJsonUtility.ToJson(
                        _effectHandler,
                        prettyPrint:
                            false);
            }
            else if (_effectHandler != null)
            {
                SetMessage(
                    "Current effect bindings are invalid: " +
                    error,
                    MessageType.Warning);
            }
        }

        private void RebindSequences()
        {
            _serializedSequences =
                _sequenceHandler != null
                    ? new SerializedObject(
                        _sequenceHandler)
                    : null;
            _sequences =
                _serializedSequences?.FindProperty(
                    "sequences");
            _sequencePending = false;
            _lastValidSequenceJson = null;

            if (_sequenceHandler != null &&
                _sequenceHandler.RebuildBindings(
                    out var error))
            {
                _lastValidSequenceJson =
                    EditorJsonUtility.ToJson(
                        _sequenceHandler,
                        prettyPrint:
                            false);
            }
            else if (_sequenceHandler != null)
            {
                SetMessage(
                    "Current scene sequences are invalid: " +
                    error,
                    MessageType.Warning);
            }
        }

        private void RebindEnvironment()
        {
            _serializedEnvironment =
                _environmentRuntime != null
                    ? new SerializedObject(
                        _environmentRuntime)
                    : null;
            _environmentStates =
                _serializedEnvironment?.FindProperty(
                    "stateBindings");
        }

        private void SetMessage(
            string value,
            MessageType type)
        {
            _message =
                value;
            _messageType =
                type;
        }

        private static string BuildUniqueLogicalId(
            string preferred,
            SerializedProperty array,
            string idPropertyName,
            int ignoreIndex)
        {
            var baseId =
                string.IsNullOrWhiteSpace(
                    preferred)
                    ? "item"
                    : preferred.Trim();
            var candidate =
                baseId;
            var suffix = 2;

            while (ContainsLogicalId(
                       array,
                       idPropertyName,
                       candidate,
                       ignoreIndex))
            {
                candidate =
                    baseId +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private static bool ContainsLogicalId(
            SerializedProperty array,
            string idPropertyName,
            string candidate,
            int ignoreIndex)
        {
            for (var i = 0;
                 i < array.arraySize;
                 i++)
            {
                if (i == ignoreIndex)
                {
                    continue;
                }

                var value =
                    array.GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            idPropertyName)
                        .stringValue;

                if (string.Equals(
                        value,
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeLogicalId(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "item";
            }

            var chars =
                value.Trim()
                    .ToLowerInvariant()
                    .ToCharArray();

            for (var i = 0;
                 i < chars.Length;
                 i++)
            {
                if (!char.IsLetterOrDigit(
                        chars[i]) &&
                    chars[i] != '-' &&
                    chars[i] != '_')
                {
                    chars[i] =
                        '-';
                }
            }

            return new string(
                chars);
        }
    }
}
