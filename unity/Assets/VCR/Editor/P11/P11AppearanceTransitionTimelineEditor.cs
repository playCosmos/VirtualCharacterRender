using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;

namespace VCR.Editor.P11
{
    public sealed class P11AppearanceTransitionTimelineEditor :
        EditorWindow
    {
        private static readonly string[] BuiltInActionTypes =
        {
            EventActionTypes.MotionPlay,
            EventActionTypes.MotionRelease,
            EventActionTypes.EffectPlay,
            EventActionTypes.EffectStop,
            EventActionTypes.AudioPlay,
            EventActionTypes.AudioStop,
            EventActionTypes.ExpressionSet,
            EventActionTypes.MaterialApplyPreset,
            EventActionTypes.EnvironmentSetState,
            EventActionTypes.CameraSetFieldOfView
        };

        private BasicCharacterAppearanceRuntime _runtime;
        private SerializedObject _serializedRuntime;
        private SerializedProperty _transitions;
        private int _selectedTransitionIndex;
        private Vector2 _scroll;
        private string _lastMessage;
        private MessageType _lastMessageType =
            MessageType.Info;

        [MenuItem("VCR/P11/Open Appearance Transition Timeline")]
        public static void Open()
        {
            GetWindow<
                    P11AppearanceTransitionTimelineEditor>(
                    "VCR Transition Timeline")
                .Show();
        }

        private void OnEnable()
        {
            if (_runtime == null)
            {
                _runtime =
                    Selection.activeGameObject != null
                        ? Selection.activeGameObject
                            .GetComponentInParent<
                                BasicCharacterAppearanceRuntime>()
                        : null;
            }

            Rebind();
        }

        private void OnSelectionChange()
        {
            if (_runtime != null)
            {
                return;
            }

            var selected =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject
                        .GetComponentInParent<
                            BasicCharacterAppearanceRuntime>()
                    : null;

            if (selected != null)
            {
                _runtime = selected;
                Rebind();
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawHeader();

            var nextRuntime =
                (BasicCharacterAppearanceRuntime)
                    EditorGUILayout.ObjectField(
                        "Appearance Runtime",
                        _runtime,
                        typeof(
                            BasicCharacterAppearanceRuntime),
                        true);

            if (!ReferenceEquals(
                    nextRuntime,
                    _runtime))
            {
                _runtime = nextRuntime;
                _selectedTransitionIndex = 0;
                Rebind();
            }

            if (_runtime == null ||
                _serializedRuntime == null ||
                _transitions == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a BasicCharacterAppearanceRuntime to author transition timelines.",
                    MessageType.Info);
                return;
            }

            _serializedRuntime.Update();

            DrawTransitionSelector();

            if (_transitions.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No authored transitions. Add one to begin.",
                    MessageType.Info);
                DrawFooterActions();
                return;
            }

            _selectedTransitionIndex =
                Mathf.Clamp(
                    _selectedTransitionIndex,
                    0,
                    _transitions.arraySize - 1);

            var transition =
                _transitions
                    .GetArrayElementAtIndex(
                        _selectedTransitionIndex);

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            DrawTransitionHeader(
                transition);
            DrawTimeline(
                transition);
            DrawSteps(
                transition.FindPropertyRelative(
                    "Steps"),
                cleanup:
                    false);
            DrawSteps(
                transition.FindPropertyRelative(
                    "CancellationSteps"),
                cleanup:
                    true);

            EditorGUILayout.EndScrollView();

            DrawFooterActions();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField(
                "Appearance Transition Timeline",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Author ordered quick-change actions around exactly one appearance.commit marker. Runtime definitions keep logical IDs only; scene objects, AnimationClip, ParticleSystem, and AudioSource references stay in their registered handlers.",
                MessageType.Info);

            if (!string.IsNullOrWhiteSpace(
                    _lastMessage))
            {
                EditorGUILayout.HelpBox(
                    _lastMessage,
                    _lastMessageType);
            }
        }

        private void DrawTransitionSelector()
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                var labels =
                    BuildTransitionLabels();

                if (labels.Length > 0)
                {
                    _selectedTransitionIndex =
                        EditorGUILayout.Popup(
                            "Transition",
                            Mathf.Clamp(
                                _selectedTransitionIndex,
                                0,
                                labels.Length - 1),
                            labels);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Transition",
                        "<none>");
                }

                if (GUILayout.Button(
                        "Add",
                        GUILayout.Width(64f)))
                {
                    AddTransition();
                }

                using (new EditorGUI.DisabledScope(
                           _transitions.arraySize ==
                           0))
                {
                    if (GUILayout.Button(
                            "Duplicate",
                            GUILayout.Width(80f)))
                    {
                        DuplicateTransition();
                    }

                    if (GUILayout.Button(
                            "Delete",
                            GUILayout.Width(64f)))
                    {
                        DeleteTransition();
                    }
                }
            }
        }

        private void DrawTransitionHeader(
            SerializedProperty transition)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Transition",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                transition.FindPropertyRelative(
                    "TransitionId"),
                new GUIContent(
                    "ID"));
            EditorGUILayout.PropertyField(
                transition.FindPropertyRelative(
                    "DurationSeconds"),
                new GUIContent(
                    "Duration (s)"));
            EditorGUILayout.PropertyField(
                transition.FindPropertyRelative(
                    "QueuePolicy"));
            EditorGUILayout.PropertyField(
                transition.FindPropertyRelative(
                    "FallbackPolicy"));

            var queue =
                (AppearanceTransitionQueuePolicy)
                transition
                    .FindPropertyRelative(
                        "QueuePolicy")
                    .enumValueIndex;

            if (queue ==
                AppearanceTransitionQueuePolicy
                    .Interrupt)
            {
                var cleanup =
                    transition.FindPropertyRelative(
                        "CancellationSteps");

                if (cleanup.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Interrupt requires at least one explicit cancellation cleanup action. Add motion.release, effect.stop, audio.stop, or another non-appearance cleanup action below.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawTimeline(
            SerializedProperty transition)
        {
            var steps =
                transition.FindPropertyRelative(
                    "Steps");
            var duration =
                Math.Max(
                    0.01f,
                    transition
                        .FindPropertyRelative(
                            "DurationSeconds")
                        .floatValue);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Timeline",
                EditorStyles.boldLabel);

            var rect =
                GUILayoutUtility.GetRect(
                    100f,
                    62f,
                    GUILayout.ExpandWidth(
                        true));

            EditorGUI.DrawRect(
                new Rect(
                    rect.x,
                    rect.center.y - 1f,
                    rect.width,
                    2f),
                EditorGUIUtility.isProSkin
                    ? new Color(
                        0.55f,
                        0.55f,
                        0.55f,
                        1f)
                    : new Color(
                        0.35f,
                        0.35f,
                        0.35f,
                        1f));

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);
                var time =
                    Mathf.Max(
                        0f,
                        step.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue);
                var t =
                    Mathf.Clamp01(
                        time /
                        duration);
                var x =
                    Mathf.Lerp(
                        rect.x,
                        rect.xMax,
                        t);
                var kind =
                    (AppearanceTransitionStepKind)
                    step.FindPropertyRelative(
                            "Kind")
                        .enumValueIndex;
                var markerRect =
                    new Rect(
                        x - 4f,
                        rect.center.y - 12f,
                        8f,
                        24f);

                EditorGUI.DrawRect(
                    markerRect,
                    kind ==
                        AppearanceTransitionStepKind
                            .Commit
                        ? new Color(
                            1f,
                            0.65f,
                            0.15f,
                            1f)
                        : new Color(
                            0.25f,
                            0.65f,
                            1f,
                            1f));

                var label =
                    kind ==
                        AppearanceTransitionStepKind
                            .Commit
                        ? "commit"
                        : ShortActionLabel(
                            step.FindPropertyRelative(
                                    "ActionType")
                                .stringValue);

                GUI.Label(
                    new Rect(
                        Mathf.Clamp(
                            x - 36f,
                            rect.x,
                            Math.Max(
                                rect.x,
                                rect.xMax - 72f)),
                        rect.y,
                        72f,
                        18f),
                    label,
                    EditorStyles.miniLabel);
            }

            GUI.Label(
                new Rect(
                    rect.x,
                    rect.yMax - 18f,
                    80f,
                    18f),
                "0.00s",
                EditorStyles.miniLabel);
            GUI.Label(
                new Rect(
                    rect.xMax - 80f,
                    rect.yMax - 18f,
                    80f,
                    18f),
                duration.ToString(
                    "0.00") +
                "s",
                new GUIStyle(
                    EditorStyles.miniLabel)
                {
                    alignment =
                        TextAnchor.MiddleRight
                });
        }

        private void DrawSteps(
            SerializedProperty steps,
            bool cleanup)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                cleanup
                    ? "Cancellation Cleanup"
                    : "Timeline Steps",
                EditorStyles.boldLabel);

            if (cleanup)
            {
                EditorGUILayout.HelpBox(
                    "Cleanup actions run immediately in authored order. appearance.* actions and commit markers are not allowed here.",
                    MessageType.None);
            }

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    DrawStepHeader(
                        steps,
                        step,
                        i,
                        cleanup);

                    if (cleanup)
                    {
                        step.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue = 0f;
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex =
                                (int)
                                AppearanceTransitionStepKind
                                    .Action;
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(
                            step.FindPropertyRelative(
                                "TimeSeconds"),
                            new GUIContent(
                                "Time (s)"));
                        EditorGUILayout.PropertyField(
                            step.FindPropertyRelative(
                                "Kind"));
                    }

                    var kind =
                        cleanup
                            ? AppearanceTransitionStepKind
                                .Action
                            : (AppearanceTransitionStepKind)
                                step
                                    .FindPropertyRelative(
                                        "Kind")
                                    .enumValueIndex;

                    if (kind ==
                        AppearanceTransitionStepKind
                            .Commit)
                    {
                        EditorGUILayout.HelpBox(
                            "Atomic appearance.commit. The requested outfit/accessory state becomes authoritative at this marker.",
                            MessageType.Info);
                    }
                    else
                    {
                        DrawActionStep(
                            step);
                    }
                }
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        cleanup
                            ? "Add Cleanup Action"
                            : "Add Action"))
                {
                    AddStep(
                        steps,
                        AppearanceTransitionStepKind
                            .Action,
                        cleanup);
                }

                if (!cleanup)
                {
                    using (new EditorGUI
                               .DisabledScope(
                                   HasCommit(
                                       steps)))
                    {
                        if (GUILayout.Button(
                                "Add Commit"))
                        {
                            AddStep(
                                steps,
                                AppearanceTransitionStepKind
                                    .Commit,
                                false);
                        }
                    }

                    using (new EditorGUI
                               .DisabledScope(
                                   steps.arraySize < 2))
                    {
                        if (GUILayout.Button(
                                "Sort by Time"))
                        {
                            SortStepsByTime(
                                steps);
                        }
                    }
                }
            }
        }

        private void DrawStepHeader(
            SerializedProperty array,
            SerializedProperty step,
            int index,
            bool cleanup)
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                var kind =
                    cleanup
                        ? AppearanceTransitionStepKind
                            .Action
                        : (AppearanceTransitionStepKind)
                            step
                                .FindPropertyRelative(
                                    "Kind")
                                .enumValueIndex;

                EditorGUILayout.LabelField(
                    cleanup
                        ? $"Cleanup {index + 1}"
                        : $"{index + 1}. {kind}",
                    EditorStyles.boldLabel);

                using (new EditorGUI
                           .DisabledScope(
                               index == 0))
                {
                    if (GUILayout.Button(
                            "↑",
                            GUILayout.Width(28f)))
                    {
                        array.MoveArrayElement(
                            index,
                            index - 1);
                    }
                }

                using (new EditorGUI
                           .DisabledScope(
                               index >=
                               array.arraySize - 1))
                {
                    if (GUILayout.Button(
                            "↓",
                            GUILayout.Width(28f)))
                    {
                        array.MoveArrayElement(
                            index,
                            index + 1);
                    }
                }

                if (GUILayout.Button(
                        "×",
                        GUILayout.Width(28f)))
                {
                    array.DeleteArrayElementAtIndex(
                        index);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawActionStep(
            SerializedProperty step)
        {
            var actionType =
                step.FindPropertyRelative(
                    "ActionType");
            var currentBuiltIn =
                Array.IndexOf(
                    BuiltInActionTypes,
                    actionType.stringValue);
            var popupIndex =
                currentBuiltIn >= 0
                    ? currentBuiltIn
                    : BuiltInActionTypes.Length;

            var labels =
                new string[
                    BuiltInActionTypes.Length + 1];

            Array.Copy(
                BuiltInActionTypes,
                labels,
                BuiltInActionTypes.Length);
            labels[labels.Length - 1] =
                "Custom...";

            var nextPopup =
                EditorGUILayout.Popup(
                    "Action",
                    popupIndex,
                    labels);

            if (nextPopup <
                BuiltInActionTypes.Length)
            {
                actionType.stringValue =
                    BuiltInActionTypes[
                        nextPopup];
            }
            else
            {
                EditorGUILayout.PropertyField(
                    actionType,
                    new GUIContent(
                        "Custom Action Type"));
            }

            EditorGUILayout.PropertyField(
                step.FindPropertyRelative(
                    "TargetId"),
                new GUIContent(
                    "Target Runtime"));
            EditorGUILayout.PropertyField(
                step.FindPropertyRelative(
                    "Name"));
            EditorGUILayout.PropertyField(
                step.FindPropertyRelative(
                    "Text"));
            EditorGUILayout.PropertyField(
                step.FindPropertyRelative(
                    "Required"));

            var hasValue =
                step.FindPropertyRelative(
                    "HasValue");
            EditorGUILayout.PropertyField(
                hasValue,
                new GUIContent(
                    "Use Numeric Value"));

            if (hasValue.boolValue)
            {
                EditorGUILayout.PropertyField(
                    step.FindPropertyRelative(
                        "Value"));
            }
        }

        private void DrawFooterActions()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateAndApply();
                }

                using (new EditorGUI.DisabledScope(
                           !EditorApplication.isPlaying ||
                           _transitions == null ||
                           _transitions.arraySize == 0))
                {
                    if (GUILayout.Button(
                            "Preview Current Appearance"))
                    {
                        PreviewSelected();
                    }
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Preview requires Play Mode. Authoring and validation work in Edit Mode.",
                    MessageType.None);
            }
        }

        private void AddTransition()
        {
            _serializedRuntime.Update();
            var index =
                _transitions.arraySize;
            _transitions.InsertArrayElementAtIndex(
                index);

            var transition =
                _transitions
                    .GetArrayElementAtIndex(
                        index);

            ResetTransition(
                transition,
                BuildUniqueTransitionId(
                    "new-transition"));

            _selectedTransitionIndex =
                index;
            ApplySerializedChanges(
                "Add Appearance Transition");
        }

        private void DuplicateTransition()
        {
            if (_transitions.arraySize == 0)
            {
                return;
            }

            _serializedRuntime.Update();

            var sourceIndex =
                Mathf.Clamp(
                    _selectedTransitionIndex,
                    0,
                    _transitions.arraySize - 1);
            var insertIndex =
                _transitions.arraySize;

            _transitions.InsertArrayElementAtIndex(
                insertIndex);

            CopyTransition(
                _transitions
                    .GetArrayElementAtIndex(
                        sourceIndex),
                _transitions
                    .GetArrayElementAtIndex(
                        insertIndex));

            var duplicated =
                _transitions
                    .GetArrayElementAtIndex(
                        insertIndex);
            var sourceId =
                duplicated
                    .FindPropertyRelative(
                        "TransitionId")
                    .stringValue;
            duplicated
                .FindPropertyRelative(
                    "TransitionId")
                .stringValue =
                    BuildUniqueTransitionId(
                        string.IsNullOrWhiteSpace(
                            sourceId)
                            ? "transition-copy"
                            : sourceId +
                              "-copy");

            _selectedTransitionIndex =
                insertIndex;
            ApplySerializedChanges(
                "Duplicate Appearance Transition");
        }

        private void DeleteTransition()
        {
            if (_transitions.arraySize == 0)
            {
                return;
            }

            _serializedRuntime.Update();
            _transitions.DeleteArrayElementAtIndex(
                Mathf.Clamp(
                    _selectedTransitionIndex,
                    0,
                    _transitions.arraySize - 1));

            _selectedTransitionIndex =
                Mathf.Clamp(
                    _selectedTransitionIndex,
                    0,
                    Math.Max(
                        0,
                        _transitions.arraySize - 1));

            ApplySerializedChanges(
                "Delete Appearance Transition");
        }

        private void AddStep(
            SerializedProperty steps,
            AppearanceTransitionStepKind kind,
            bool cleanup)
        {
            var index =
                steps.arraySize;
            steps.InsertArrayElementAtIndex(
                index);

            var step =
                steps.GetArrayElementAtIndex(
                    index);
            ResetStep(
                step,
                kind,
                cleanup);

            _serializedRuntime
                .ApplyModifiedProperties();
        }

        private static void ResetTransition(
            SerializedProperty transition,
            string id)
        {
            transition.FindPropertyRelative(
                    "TransitionId")
                .stringValue = id;
            transition.FindPropertyRelative(
                    "DurationSeconds")
                .floatValue = 1f;
            transition.FindPropertyRelative(
                    "QueuePolicy")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionQueuePolicy
                        .QueueLatest;
            transition.FindPropertyRelative(
                    "FallbackPolicy")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionFallbackPolicy
                        .Immediate;

            var steps =
                transition.FindPropertyRelative(
                    "Steps");
            steps.arraySize = 0;
            steps.InsertArrayElementAtIndex(
                0);
            ResetStep(
                steps.GetArrayElementAtIndex(
                    0),
                AppearanceTransitionStepKind
                    .Commit,
                false);
            steps.GetArrayElementAtIndex(
                    0)
                .FindPropertyRelative(
                    "TimeSeconds")
                .floatValue = 0.5f;

            transition.FindPropertyRelative(
                    "CancellationSteps")
                .arraySize = 0;
        }

        private static void ResetStep(
            SerializedProperty step,
            AppearanceTransitionStepKind kind,
            bool cleanup)
        {
            step.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    cleanup
                        ? 0f
                        : 0f;
            step.FindPropertyRelative(
                    "Kind")
                .enumValueIndex =
                    (int)kind;
            step.FindPropertyRelative(
                    "ActionType")
                .stringValue =
                    kind ==
                    AppearanceTransitionStepKind
                        .Action
                        ? EventActionTypes
                            .MotionPlay
                        : string.Empty;
            step.FindPropertyRelative(
                    "TargetId")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Name")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Text")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "Value")
                .doubleValue = 0.0;
            step.FindPropertyRelative(
                    "HasValue")
                .boolValue = false;
            step.FindPropertyRelative(
                    "Required")
                .boolValue = true;
        }

        private static void CopyTransition(
            SerializedProperty source,
            SerializedProperty destination)
        {
            destination.FindPropertyRelative(
                    "TransitionId")
                .stringValue =
                    source.FindPropertyRelative(
                            "TransitionId")
                        .stringValue;
            destination.FindPropertyRelative(
                    "DurationSeconds")
                .floatValue =
                    source.FindPropertyRelative(
                            "DurationSeconds")
                        .floatValue;
            destination.FindPropertyRelative(
                    "QueuePolicy")
                .enumValueIndex =
                    source.FindPropertyRelative(
                            "QueuePolicy")
                        .enumValueIndex;
            destination.FindPropertyRelative(
                    "FallbackPolicy")
                .enumValueIndex =
                    source.FindPropertyRelative(
                            "FallbackPolicy")
                        .enumValueIndex;

            CopyStepArray(
                source.FindPropertyRelative(
                    "Steps"),
                destination.FindPropertyRelative(
                    "Steps"));
            CopyStepArray(
                source.FindPropertyRelative(
                    "CancellationSteps"),
                destination.FindPropertyRelative(
                    "CancellationSteps"));
        }

        private static void CopyStepArray(
            SerializedProperty source,
            SerializedProperty destination)
        {
            destination.arraySize =
                source.arraySize;

            for (var i = 0;
                 i < source.arraySize;
                 i++)
            {
                var from =
                    source.GetArrayElementAtIndex(
                        i);
                var to =
                    destination
                        .GetArrayElementAtIndex(
                            i);

                to.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue =
                        from.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue;
                to.FindPropertyRelative(
                        "Kind")
                    .enumValueIndex =
                        from.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex;
                to.FindPropertyRelative(
                        "ActionType")
                    .stringValue =
                        from.FindPropertyRelative(
                                "ActionType")
                            .stringValue;
                to.FindPropertyRelative(
                        "TargetId")
                    .stringValue =
                        from.FindPropertyRelative(
                                "TargetId")
                            .stringValue;
                to.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        from.FindPropertyRelative(
                                "Name")
                            .stringValue;
                to.FindPropertyRelative(
                        "Text")
                    .stringValue =
                        from.FindPropertyRelative(
                                "Text")
                            .stringValue;
                to.FindPropertyRelative(
                        "Value")
                    .doubleValue =
                        from.FindPropertyRelative(
                                "Value")
                            .doubleValue;
                to.FindPropertyRelative(
                        "HasValue")
                    .boolValue =
                        from.FindPropertyRelative(
                                "HasValue")
                            .boolValue;
                to.FindPropertyRelative(
                        "Required")
                    .boolValue =
                        from.FindPropertyRelative(
                                "Required")
                            .boolValue;
            }
        }

        private static bool HasCommit(
            SerializedProperty steps)
        {
            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                if ((AppearanceTransitionStepKind)
                    steps.GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            "Kind")
                        .enumValueIndex ==
                    AppearanceTransitionStepKind
                        .Commit)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SortStepsByTime(
            SerializedProperty steps)
        {
            for (var target = 0;
                 target < steps.arraySize - 1;
                 target++)
            {
                var best =
                    target;
                var bestTime =
                    steps.GetArrayElementAtIndex(
                            best)
                        .FindPropertyRelative(
                            "TimeSeconds")
                        .floatValue;

                for (var candidate =
                         target + 1;
                     candidate <
                     steps.arraySize;
                     candidate++)
                {
                    var candidateTime =
                        steps.GetArrayElementAtIndex(
                                candidate)
                            .FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue;

                    if (candidateTime <
                        bestTime)
                    {
                        best =
                            candidate;
                        bestTime =
                            candidateTime;
                    }
                }

                if (best != target)
                {
                    steps.MoveArrayElement(
                        best,
                        target);
                }
            }
        }

        private void ValidateAndApply()
        {
            _serializedRuntime
                .ApplyModifiedProperties();

            EditorUtility.SetDirty(
                _runtime);

            if (_runtime
                .RebuildConfiguration(
                    out var error))
            {
                _lastMessage =
                    "Transition definitions validated and applied.";
                _lastMessageType =
                    MessageType.Info;
            }
            else
            {
                _lastMessage =
                    "Transition validation failed: " +
                    (error ?? "unknown error");
                _lastMessageType =
                    MessageType.Error;
            }

            Repaint();
        }

        private void PreviewSelected()
        {
            _serializedRuntime
                .ApplyModifiedProperties();

            if (!_runtime
                .RebuildConfiguration(
                    out var rebuildError))
            {
                _lastMessage =
                    "Preview blocked: " +
                    rebuildError;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            var transition =
                _transitions
                    .GetArrayElementAtIndex(
                        _selectedTransitionIndex);
            var transitionId =
                transition.FindPropertyRelative(
                        "TransitionId")
                    .stringValue;
            var current =
                _runtime.Current;

            bool requested;
            string error;

            if (!string.IsNullOrWhiteSpace(
                    current.PresetId))
            {
                requested =
                    _runtime.SetPreset(
                        current.PresetId,
                        transitionId,
                        out error);
            }
            else if (!string.IsNullOrWhiteSpace(
                         current.OutfitId))
            {
                requested =
                    _runtime.SetOutfit(
                        current.OutfitId,
                        transitionId,
                        out error);
            }
            else
            {
                requested = false;
                error =
                    "No current preset or outfit is active.";
            }

            _lastMessage =
                requested
                    ? $"Preview started: {transitionId}"
                    : "Preview failed: " +
                      (error ?? "unknown error");
            _lastMessageType =
                requested
                    ? MessageType.Info
                    : MessageType.Error;
        }

        private void ApplySerializedChanges(
            string undoName)
        {
            Undo.RecordObject(
                _runtime,
                undoName);
            _serializedRuntime
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _runtime);
            Repaint();
        }

        private string[] BuildTransitionLabels()
        {
            var labels =
                new string[
                    _transitions.arraySize];

            for (var i = 0;
                 i < labels.Length;
                 i++)
            {
                var id =
                    _transitions
                        .GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            "TransitionId")
                        .stringValue;

                labels[i] =
                    string.IsNullOrWhiteSpace(
                        id)
                        ? $"Transition {i + 1}"
                        : id;
            }

            return labels;
        }

        private string BuildUniqueTransitionId(
            string preferred)
        {
            var baseId =
                string.IsNullOrWhiteSpace(
                    preferred)
                    ? "transition"
                    : preferred.Trim();
            var candidate =
                baseId;
            var suffix = 2;

            while (ContainsTransitionId(
                       candidate))
            {
                candidate =
                    baseId +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private bool ContainsTransitionId(
            string candidate)
        {
            for (var i = 0;
                 i < _transitions.arraySize;
                 i++)
            {
                if (string.Equals(
                        _transitions
                            .GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "TransitionId")
                            .stringValue,
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void Rebind()
        {
            _serializedRuntime =
                _runtime != null
                    ? new SerializedObject(
                        _runtime)
                    : null;
            _transitions =
                _serializedRuntime?
                    .FindProperty(
                        "transitions");

            if (_transitions != null)
            {
                _selectedTransitionIndex =
                    Mathf.Clamp(
                        _selectedTransitionIndex,
                        0,
                        Math.Max(
                            0,
                            _transitions.arraySize - 1));
            }
        }

        private static string ShortActionLabel(
            string actionType)
        {
            if (string.IsNullOrWhiteSpace(
                    actionType))
            {
                return "action";
            }

            var separator =
                actionType.LastIndexOf(
                    '.');

            return separator >= 0 &&
                   separator <
                   actionType.Length - 1
                ? actionType.Substring(
                    separator + 1)
                : actionType;
        }
    }
}
