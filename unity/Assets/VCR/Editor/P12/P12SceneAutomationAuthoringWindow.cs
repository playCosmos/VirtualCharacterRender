using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Environment.Unity;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Editor.P12
{
    public sealed class P12SceneAutomationAuthoringWindow :
        EditorWindow
    {
        private BasicEnvironmentRuntime _environment;
        private PropEventActionHandler _props;
        private EffectEventActionHandler _effects;
        private P12EffectPresetAsset _effectPreset;
        private string _newEffectPresetId =
            "effect";

        private SerializedObject _environmentSerialized;
        private SerializedObject _propsSerialized;
        private SerializedObject _effectsSerialized;

        private string _lastValidEnvironmentJson;
        private string _lastValidPropsJson;
        private string _lastValidEffectsJson;
        private string _message;
        private MessageType _messageType =
            MessageType.Info;
        private Vector2 _scroll;

        [MenuItem("VCR/P12/Open Scene Automation Authoring")]
        public static void Open()
        {
            GetWindow<
                    P12SceneAutomationAuthoringWindow>(
                    "VCR Scene Automation")
                .Show();
        }

        public static void OpenWithEffectPreset(
            P12EffectPresetAsset preset)
        {
            var window =
                GetWindow<
                    P12SceneAutomationAuthoringWindow>(
                    "VCR Scene Automation");
            window._effectPreset =
                preset;
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            ResolveFromSelection();
            Rebind();
            TryRecordLastValid();
        }

        private void OnSelectionChange()
        {
            if (_environment != null ||
                _props != null ||
                _effects != null)
            {
                return;
            }

            ResolveFromSelection();
            Rebind();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Scene Automation Authoring",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Author logical Environment / Prop / Effect bindings used by Event Runtime actions. This window does not add a second automation engine; event rules and appearance transitions continue to execute the registered logical ids.",
                MessageType.Info);

            if (!string.IsNullOrWhiteSpace(
                    _message))
            {
                EditorGUILayout.HelpBox(
                    _message,
                    _messageType);
            }

            DrawTargets();

            if (_environment == null &&
                _props == null &&
                _effects == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign at least one BasicEnvironmentRuntime, PropEventActionHandler, or EffectEventActionHandler.",
                    MessageType.Warning);
                return;
            }

            UpdateSerializedObjects();

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            DrawEnvironment();
            DrawProps();
            DrawEffects();

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateAndApply();
                }

                if (GUILayout.Button(
                        "Restore Last Valid"))
                {
                    RestoreLastValid(
                        "Restored the last valid scene automation bindings.");
                }
            }

            ApplyModifiedProperties();
        }

        private void DrawTargets()
        {
            var nextEnvironment =
                (BasicEnvironmentRuntime)
                EditorGUILayout.ObjectField(
                    "Environment Runtime",
                    _environment,
                    typeof(
                        BasicEnvironmentRuntime),
                    true);
            var nextProps =
                (PropEventActionHandler)
                EditorGUILayout.ObjectField(
                    "Prop Handler",
                    _props,
                    typeof(
                        PropEventActionHandler),
                    true);
            var nextEffects =
                (EffectEventActionHandler)
                EditorGUILayout.ObjectField(
                    "Effect Handler",
                    _effects,
                    typeof(
                        EffectEventActionHandler),
                    true);

            if (!ReferenceEquals(
                    nextEnvironment,
                    _environment) ||
                !ReferenceEquals(
                    nextProps,
                    _props) ||
                !ReferenceEquals(
                    nextEffects,
                    _effects))
            {
                _environment =
                    nextEnvironment;
                _props =
                    nextProps;
                _effects =
                    nextEffects;
                Rebind();
                TryRecordLastValid();
            }
        }

        private void DrawEnvironment()
        {
            if (_environmentSerialized == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Environment States",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _environmentSerialized
                    .FindProperty(
                        "environmentId"));
            EditorGUILayout.PropertyField(
                _environmentSerialized
                    .FindProperty(
                        "stateId"),
                new GUIContent(
                    "Active / Initial State"));
            EditorGUILayout.PropertyField(
                _environmentSerialized
                    .FindProperty(
                        "defaultTransitionMode"));
            EditorGUILayout.PropertyField(
                _environmentSerialized
                    .FindProperty(
                        "defaultTransitionDuration"));

            var states =
                _environmentSerialized
                    .FindProperty(
                        "stateBindings");

            EditorGUILayout.PropertyField(
                states,
                includeChildren:
                    true);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add State From Selection"))
                {
                    AddEnvironmentStateFromSelection(
                        states);
                }

                if (GUILayout.Button(
                        "Add Empty State"))
                {
                    AddEmptyArrayElement(
                        states,
                        "stateId",
                        "state",
                        "Add Environment State");
                }
            }
        }

        private void DrawProps()
        {
            if (_propsSerialized == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Props",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _propsSerialized.FindProperty(
                    "handlerId"));

            var props =
                _propsSerialized.FindProperty(
                    "props");

            EditorGUILayout.PropertyField(
                props,
                includeChildren:
                    true);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Prop From Selection"))
                {
                    AddPropFromSelection(
                        props);
                }

                if (GUILayout.Button(
                        "Add Empty Prop"))
                {
                    AddEmptyArrayElement(
                        props,
                        "PropId",
                        "prop",
                        "Add Prop Binding");
                }
            }
        }

        private void DrawEffects()
        {
            if (_effectsSerialized == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Effects",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _effectsSerialized.FindProperty(
                    "handlerId"));

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                _effectPreset =
                    (P12EffectPresetAsset)
                    EditorGUILayout.ObjectField(
                        "Effect Preset",
                        _effectPreset,
                        typeof(
                            P12EffectPresetAsset),
                        false);

                _newEffectPresetId =
                    EditorGUILayout.TextField(
                        "New Preset ID",
                        _newEffectPresetId);

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    using (new EditorGUI
                               .DisabledScope(
                                   Selection.activeGameObject ==
                                       null))
                    {
                        if (GUILayout.Button(
                                "Create Preset From Selection"))
                        {
                            CreateEffectPresetFromSelection();
                        }
                    }

                    using (new EditorGUI
                               .DisabledScope(
                                   _effectPreset == null))
                    {
                        if (GUILayout.Button(
                                "Install Effect Preset"))
                        {
                            InstallEffectPreset();
                        }
                    }

                    if (GUILayout.Button(
                            "Open Preset Library"))
                    {
                        P12EffectPresetLibraryWindow
                            .Open();
                    }
                }

                EditorGUILayout.HelpBox(
                    "Effect Preset v1 is project-local and accepts only prefab hierarchies made of Transform, ParticleSystem, and ParticleSystemRenderer components. Create saves a reusable prefab + preset asset under Assets/VCR/EffectPresets; Install creates one scene instance and registers it through the existing EffectEventActionHandler binding.",
                    MessageType.None);
            }

            var effects =
                _effectsSerialized.FindProperty(
                    "effects");

            EditorGUILayout.PropertyField(
                effects,
                includeChildren:
                    true);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Effect From Selection"))
                {
                    AddEffectFromSelection(
                        effects);
                }

                if (GUILayout.Button(
                        "Add Empty Effect"))
                {
                    AddEmptyArrayElement(
                        effects,
                        "EffectId",
                        "effect",
                        "Add Effect Binding");
                }
            }
        }

        private void AddEnvironmentStateFromSelection(
            SerializedProperty states)
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                SetMessage(
                    "Select a GameObject to use as the environment state root.",
                    MessageType.Warning);
                return;
            }

            Undo.RecordObject(
                _environment,
                "Add Environment State From Selection");

            var index =
                states.arraySize;
            states.arraySize =
                index + 1;
            var element =
                states.GetArrayElementAtIndex(
                    index);
            element.FindPropertyRelative(
                    "stateId")
                .stringValue =
                    BuildUniqueId(
                        states,
                        "stateId",
                        selected.name);
            element.FindPropertyRelative(
                    "root")
                .objectReferenceValue =
                    selected;
            SetMessage(
                $"Added environment state '{element.FindPropertyRelative("stateId").stringValue}' from '{selected.name}'.",
                MessageType.Info);
        }

        private void AddPropFromSelection(
            SerializedProperty props)
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                SetMessage(
                    "Select a GameObject to use as the prop root.",
                    MessageType.Warning);
                return;
            }

            Undo.RecordObject(
                _props,
                "Add Prop From Selection");

            var index =
                props.arraySize;
            props.arraySize =
                index + 1;
            var element =
                props.GetArrayElementAtIndex(
                    index);
            element.FindPropertyRelative(
                    "PropId")
                .stringValue =
                    BuildUniqueId(
                        props,
                        "PropId",
                        selected.name);

            var roots =
                element.FindPropertyRelative(
                    "Roots");
            roots.arraySize = 1;
            roots.GetArrayElementAtIndex(
                    0)
                .objectReferenceValue =
                    selected;

            SetMessage(
                $"Added prop '{element.FindPropertyRelative("PropId").stringValue}' from '{selected.name}'.",
                MessageType.Info);
        }

        private void CreateEffectPresetFromSelection()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                SetMessage(
                    "Select a ParticleSystem root in the scene to create an effect preset.",
                    MessageType.Warning);
                return;
            }

            if (!P12EffectPresetUtility
                .TryCreateFromSceneRoot(
                    selected,
                    _newEffectPresetId,
                    P12EffectPresetUtility
                        .DefaultPresetFolder,
                    out var preset,
                    out var error))
            {
                SetMessage(
                    "Effect preset creation failed: " +
                    (error ?? "unknown error"),
                    MessageType.Error);
                return;
            }

            _effectPreset =
                preset;
            Selection.activeObject =
                preset;
            EditorGUIUtility.PingObject(
                preset);

            SetMessage(
                $"Created effect preset '{preset.EffectId}' from '{selected.name}'.",
                MessageType.Info);
        }

        private void InstallEffectPreset()
        {
            if (_effects == null)
            {
                SetMessage(
                    "Assign an EffectEventActionHandler before installing an effect preset.",
                    MessageType.Warning);
                return;
            }

            if (_effectPreset == null)
            {
                SetMessage(
                    "Select an effect preset asset to install.",
                    MessageType.Warning);
                return;
            }

            ApplyModifiedProperties();

            if (!TryValidateCurrent(
                    out var pendingError))
            {
                RestoreLastValid(
                    "Effect preset installation was blocked because current scene-automation edits are invalid: " +
                    (pendingError ?? "unknown error"),
                    MessageType.Error);
                return;
            }

            RecordLastValid();

            if (!P12EffectPresetUtility
                .TryInstall(
                    _effectPreset,
                    _effects,
                    out var result,
                    out var error))
            {
                RebindSerializedOnly();
                SetMessage(
                    "Effect preset installation failed: " +
                    (error ?? "unknown error"),
                    MessageType.Error);
                return;
            }

            RebindSerializedOnly();
            RecordLastValid();

            Selection.activeObject =
                result.Instance;
            EditorGUIUtility.PingObject(
                result.Instance);

            SetMessage(
                $"Installed effect preset '{result.EffectId}' with {result.ParticleSystemCount} ParticleSystem(s).",
                MessageType.Info);
        }

        private void AddEffectFromSelection(
            SerializedProperty effects)
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                SetMessage(
                    "Select a GameObject to use as the effect root.",
                    MessageType.Warning);
                return;
            }

            Undo.RecordObject(
                _effects,
                "Add Effect From Selection");

            var index =
                effects.arraySize;
            effects.arraySize =
                index + 1;
            var element =
                effects.GetArrayElementAtIndex(
                    index);
            element.FindPropertyRelative(
                    "EffectId")
                .stringValue =
                    BuildUniqueId(
                        effects,
                        "EffectId",
                        selected.name);
            element.FindPropertyRelative(
                    "Root")
                .objectReferenceValue =
                    selected;

            var systems =
                selected.GetComponentsInChildren<
                    ParticleSystem>(
                    includeInactive:
                        true);
            var serializedSystems =
                element.FindPropertyRelative(
                    "ParticleSystems");
            serializedSystems.arraySize =
                systems.Length;

            for (var i = 0;
                 i < systems.Length;
                 i++)
            {
                serializedSystems
                    .GetArrayElementAtIndex(
                        i)
                    .objectReferenceValue =
                        systems[i];
            }

            element.FindPropertyRelative(
                    "RestartOnPlay")
                .boolValue = true;

            SetMessage(
                $"Added effect '{element.FindPropertyRelative("EffectId").stringValue}' with {systems.Length} ParticleSystem(s) from '{selected.name}'.",
                MessageType.Info);
        }

        private void AddEmptyArrayElement(
            SerializedProperty array,
            string idField,
            string preferred,
            string undoLabel)
        {
            if (array == null)
            {
                return;
            }

            var owner =
                ReferenceEquals(
                    array.serializedObject,
                    _environmentSerialized)
                    ? (UnityEngine.Object)
                        _environment
                    : ReferenceEquals(
                          array.serializedObject,
                          _propsSerialized)
                        ? _props
                        : _effects;

            if (owner != null)
            {
                Undo.RecordObject(
                    owner,
                    undoLabel);
            }

            var index =
                array.arraySize;
            array.arraySize =
                index + 1;
            var element =
                array.GetArrayElementAtIndex(
                    index);
            var id =
                element.FindPropertyRelative(
                    idField);

            if (id != null)
            {
                id.stringValue =
                    BuildUniqueId(
                        array,
                        idField,
                        preferred);
            }
        }

        private void ValidateAndApply()
        {
            ApplyModifiedProperties();

            if (!TryValidateCurrent(
                    out var error))
            {
                RestoreLastValid(
                    "Scene automation validation failed and serialized bindings were rolled back: " +
                    (error ?? "unknown error"),
                    MessageType.Error);
                return;
            }

            RecordLastValid();
            SetMessage(
                "Scene automation bindings validated and applied.",
                MessageType.Info);
            RebindSerializedOnly();
        }

        private bool TryValidateCurrent(
            out string error)
        {
            error = null;

            if (_environment != null &&
                !_environment.RebuildStateBindings(
                    out error))
            {
                return false;
            }

            if (_props != null &&
                !_props.RebuildBindings(
                    out error))
            {
                return false;
            }

            if (_effects != null &&
                !_effects.RebuildBindings(
                    out error))
            {
                return false;
            }

            return true;
        }

        private void TryRecordLastValid()
        {
            if (_environment == null &&
                _props == null &&
                _effects == null)
            {
                return;
            }

            if (TryValidateCurrent(
                    out _))
            {
                RecordLastValid();
            }
        }

        private void RecordLastValid()
        {
            _lastValidEnvironmentJson =
                _environment != null
                    ? EditorJsonUtility.ToJson(
                        _environment,
                        prettyPrint:
                            false)
                    : null;
            _lastValidPropsJson =
                _props != null
                    ? EditorJsonUtility.ToJson(
                        _props,
                        prettyPrint:
                            false)
                    : null;
            _lastValidEffectsJson =
                _effects != null
                    ? EditorJsonUtility.ToJson(
                        _effects,
                        prettyPrint:
                            false)
                    : null;
        }

        private void RestoreLastValid(
            string message,
            MessageType type =
                MessageType.Info)
        {
            if (_environment != null &&
                !string.IsNullOrWhiteSpace(
                    _lastValidEnvironmentJson))
            {
                EditorJsonUtility.FromJsonOverwrite(
                    _lastValidEnvironmentJson,
                    _environment);
            }

            if (_props != null &&
                !string.IsNullOrWhiteSpace(
                    _lastValidPropsJson))
            {
                EditorJsonUtility.FromJsonOverwrite(
                    _lastValidPropsJson,
                    _props);
            }

            if (_effects != null &&
                !string.IsNullOrWhiteSpace(
                    _lastValidEffectsJson))
            {
                EditorJsonUtility.FromJsonOverwrite(
                    _lastValidEffectsJson,
                    _effects);
            }

            _environment?.RebuildStateBindings(
                out _);
            _props?.RebuildBindings(
                out _);
            _effects?.RebuildBindings(
                out _);

            RebindSerializedOnly();
            SetMessage(
                message,
                type);
            Repaint();
        }

        private void ResolveFromSelection()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            _environment ??=
                selected.GetComponentInParent<
                    BasicEnvironmentRuntime>();
            _props ??=
                selected.GetComponentInParent<
                    PropEventActionHandler>();
            _effects ??=
                selected.GetComponentInParent<
                    EffectEventActionHandler>();

            _environment ??=
                selected.GetComponent<
                    BasicEnvironmentRuntime>();
            _props ??=
                selected.GetComponent<
                    PropEventActionHandler>();
            _effects ??=
                selected.GetComponent<
                    EffectEventActionHandler>();
        }

        private void Rebind()
        {
            _environmentSerialized =
                _environment != null
                    ? new SerializedObject(
                        _environment)
                    : null;
            _propsSerialized =
                _props != null
                    ? new SerializedObject(
                        _props)
                    : null;
            _effectsSerialized =
                _effects != null
                    ? new SerializedObject(
                        _effects)
                    : null;
        }

        private void RebindSerializedOnly()
        {
            Rebind();
            UpdateSerializedObjects();
        }

        private void UpdateSerializedObjects()
        {
            _environmentSerialized?.Update();
            _propsSerialized?.Update();
            _effectsSerialized?.Update();
        }

        private void ApplyModifiedProperties()
        {
            _environmentSerialized
                ?.ApplyModifiedProperties();
            _propsSerialized
                ?.ApplyModifiedProperties();
            _effectsSerialized
                ?.ApplyModifiedProperties();
        }

        private static string BuildUniqueId(
            SerializedProperty array,
            string idField,
            string preferred)
        {
            var baseId =
                SanitizeId(
                    preferred);
            var candidate =
                baseId;
            var suffix = 2;

            while (ContainsId(
                       array,
                       idField,
                       candidate))
            {
                candidate =
                    baseId +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private static bool ContainsId(
            SerializedProperty array,
            string idField,
            string candidate)
        {
            for (var i = 0;
                 i < array.arraySize;
                 i++)
            {
                var id =
                    array
                        .GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            idField);

                if (id != null &&
                    string.Equals(
                        id.stringValue,
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeId(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "item";
            }

            var chars =
                value
                    .Trim()
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
                    chars[i] = '-';
                }
            }

            return new string(
                chars);
        }

        private void SetMessage(
            string message,
            MessageType type)
        {
            _message =
                message;
            _messageType =
                type;
        }
    }
}
