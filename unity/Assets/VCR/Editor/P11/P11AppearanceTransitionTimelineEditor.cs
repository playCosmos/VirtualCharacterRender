using System;
using System.IO;
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
            DrawMarkers(
                transition);
            DrawTimeline(
                transition);
            DrawSteps(
                transition,
                transition.FindPropertyRelative(
                    "Steps"),
                cleanup:
                    false);
            DrawSteps(
                transition,
                transition.FindPropertyRelative(
                    "CancellationSteps"),
                cleanup:
                    true);

            EditorGUILayout.EndScrollView();

            DrawFooterActions();

            if (_serializedRuntime
                .ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(
                    _runtime);
            }
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

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Spin + Confetti Template"))
                {
                    ApplySpinConfettiTemplate(
                        transition);
                }

                if (GUILayout.Button(
                        "Add Interrupt Cleanup"))
                {
                    ApplyInterruptCleanupTemplate(
                        transition);
                }
            }

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

        private void DrawMarkers(
            SerializedProperty transition)
        {
            var markers =
                transition.FindPropertyRelative(
                    "Markers");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Named Markers",
                EditorStyles.boldLabel);

            if (markers.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Markers are optional. Add names such as 'swap' or 'spin-end' and let steps reference them instead of duplicating absolute times.",
                    MessageType.None);
            }

            for (var i = 0;
                 i < markers.arraySize;
                 i++)
            {
                var marker =
                    markers.GetArrayElementAtIndex(
                        i);

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    EditorGUILayout.PropertyField(
                        marker.FindPropertyRelative(
                            "Name"),
                        GUIContent.none);
                    EditorGUILayout.PropertyField(
                        marker.FindPropertyRelative(
                            "TimeSeconds"),
                        GUIContent.none,
                        GUILayout.Width(
                            100f));

                    if (GUILayout.Button(
                            "×",
                            GUILayout.Width(28f)))
                    {
                        Undo.RecordObject(
                            _runtime,
                            "Delete Transition Marker");
                        markers.DeleteArrayElementAtIndex(
                            i);
                        _serializedRuntime
                            .ApplyModifiedProperties();
                        EditorUtility.SetDirty(
                            _runtime);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (GUILayout.Button(
                    "Add Marker"))
            {
                Undo.RecordObject(
                    _runtime,
                    "Add Transition Marker");

                var index =
                    markers.arraySize;
                markers.arraySize =
                    index + 1;
                var marker =
                    markers.GetArrayElementAtIndex(
                        index);
                marker.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        BuildUniqueMarkerName(
                            markers,
                            "marker",
                            index);
                marker.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue = 0f;
            }
        }

        private void DrawTimeline(
            SerializedProperty transition)
        {
            var steps =
                transition.FindPropertyRelative(
                    "Steps");
            var markers =
                transition.FindPropertyRelative(
                    "Markers");
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

            for (var markerIndex = 0;
                 markerIndex < markers.arraySize;
                 markerIndex++)
            {
                var marker =
                    markers.GetArrayElementAtIndex(
                        markerIndex);
                var markerTime =
                    Mathf.Max(
                        0f,
                        marker.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue);
                var markerT =
                    Mathf.Clamp01(
                        markerTime /
                        duration);
                var markerX =
                    Mathf.Lerp(
                        rect.x,
                        rect.xMax,
                        markerT);

                EditorGUI.DrawRect(
                    new Rect(
                        markerX - 1f,
                        rect.y + 18f,
                        2f,
                        rect.height - 36f),
                    new Color(
                        0.55f,
                        0.85f,
                        0.55f,
                        0.85f));

                GUI.Label(
                    new Rect(
                        Mathf.Clamp(
                            markerX - 42f,
                            rect.x,
                            Math.Max(
                                rect.x,
                                rect.xMax - 84f)),
                        rect.y + 18f,
                        84f,
                        18f),
                    marker.FindPropertyRelative(
                            "Name")
                        .stringValue,
                    EditorStyles.miniLabel);
            }

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
                        ResolveSerializedStepTime(
                            transition,
                            step));
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
            SerializedProperty transition,
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
                                "TimingMode")
                            .enumValueIndex =
                                (int)
                                AppearanceTransitionTimingMode
                                    .AbsoluteTime;
                        step.FindPropertyRelative(
                                "MarkerName")
                            .stringValue =
                                string.Empty;
                        step.FindPropertyRelative(
                                "MarkerOffsetSeconds")
                            .floatValue = 0f;
                        step.FindPropertyRelative(
                                "Blocking")
                            .boolValue = false;
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex =
                                (int)
                                AppearanceTransitionStepKind
                                    .Action;
                    }
                    else
                    {
                        DrawStepTiming(
                            transition,
                            step);
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
                                "Sort by Resolved Time"))
                        {
                            Undo.RecordObject(
                                _runtime,
                                "Sort Transition Steps");
                            SortStepsByResolvedTime(
                                transition,
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
                        Undo.RecordObject(
                            _runtime,
                            "Move Transition Step");
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
                        Undo.RecordObject(
                            _runtime,
                            "Move Transition Step");
                        array.MoveArrayElement(
                            index,
                            index + 1);
                    }
                }

                if (GUILayout.Button(
                        "×",
                        GUILayout.Width(28f)))
                {
                    Undo.RecordObject(
                        _runtime,
                        cleanup
                            ? "Delete Transition Cleanup Action"
                            : "Delete Transition Step");
                    array.DeleteArrayElementAtIndex(
                        index);
                    _serializedRuntime
                        .ApplyModifiedProperties();
                    EditorUtility.SetDirty(
                        _runtime);
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

            var blocking =
                step.FindPropertyRelative(
                    "Blocking");
            EditorGUILayout.PropertyField(
                blocking,
                new GUIContent(
                    "Blocking"));

            if (blocking.boolValue)
            {
                EditorGUILayout.PropertyField(
                    step.FindPropertyRelative(
                        "CompletionTimeoutSeconds"),
                    new GUIContent(
                        "Completion Timeout (s)"));
                EditorGUILayout.HelpBox(
                    "The next timeline step waits until this action reports completion. A positive timeout is mandatory.",
                    MessageType.None);
            }
        }

        private void DrawStepTiming(
            SerializedProperty transition,
            SerializedProperty step)
        {
            var timingMode =
                step.FindPropertyRelative(
                    "TimingMode");

            EditorGUILayout.PropertyField(
                timingMode,
                new GUIContent(
                    "Timing"));

            var mode =
                (AppearanceTransitionTimingMode)
                timingMode.enumValueIndex;

            if (mode ==
                AppearanceTransitionTimingMode
                    .Marker)
            {
                var markers =
                    transition.FindPropertyRelative(
                        "Markers");
                var markerName =
                    step.FindPropertyRelative(
                        "MarkerName");
                var names =
                    BuildMarkerLabels(
                        markers);
                var selected =
                    Array.IndexOf(
                        names,
                        markerName.stringValue);

                if (names.Length > 0)
                {
                    selected =
                        EditorGUILayout.Popup(
                            "Marker",
                            Mathf.Max(
                                0,
                                selected),
                            names);
                    markerName.stringValue =
                        names[
                            selected];
                }
                else
                {
                    EditorGUILayout.PropertyField(
                        markerName,
                        new GUIContent(
                            "Marker"));
                    EditorGUILayout.HelpBox(
                        "This step references a marker but the transition has no markers.",
                        MessageType.Warning);
                }

                EditorGUILayout.PropertyField(
                    step.FindPropertyRelative(
                        "MarkerOffsetSeconds"),
                    new GUIContent(
                        "Marker Offset (s)"));
            }
            else
            {
                EditorGUILayout.PropertyField(
                    step.FindPropertyRelative(
                        "TimeSeconds"),
                    new GUIContent(
                        "Time (s)"));
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

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           _transitions == null ||
                           _transitions.arraySize == 0))
                {
                    if (GUILayout.Button(
                            "Export Selected JSON"))
                    {
                        ExportTransitions(
                            selectedOnly:
                                true);
                    }

                    if (GUILayout.Button(
                            "Export All JSON"))
                    {
                        ExportTransitions(
                            selectedOnly:
                                false);
                    }
                }

                if (GUILayout.Button(
                        "Import JSON"))
                {
                    ImportTransitions();
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
            Undo.RecordObject(
                _runtime,
                "Add Appearance Transition");
            _serializedRuntime.Update();
            var index =
                _transitions.arraySize;
            _transitions.arraySize =
                index + 1;

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
            ApplySerializedChanges();
        }

        private void DuplicateTransition()
        {
            if (_transitions.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Duplicate Appearance Transition");
            _serializedRuntime.Update();

            var sourceIndex =
                Mathf.Clamp(
                    _selectedTransitionIndex,
                    0,
                    _transitions.arraySize - 1);
            var insertIndex =
                _transitions.arraySize;

            _transitions.arraySize =
                insertIndex + 1;

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
            ApplySerializedChanges();
        }

        private void DeleteTransition()
        {
            if (_transitions.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Delete Appearance Transition");
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

            ApplySerializedChanges();
        }

        private void AddStep(
            SerializedProperty steps,
            AppearanceTransitionStepKind kind,
            bool cleanup)
        {
            Undo.RecordObject(
                _runtime,
                cleanup
                    ? "Add Transition Cleanup Action"
                    : "Add Transition Step");
            var index =
                steps.arraySize;
            steps.arraySize =
                index + 1;

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
                    "Markers")
                .arraySize = 0;
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
                .floatValue = 0f;
            step.FindPropertyRelative(
                    "TimingMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionTimingMode
                        .AbsoluteTime;
            step.FindPropertyRelative(
                    "MarkerName")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "MarkerOffsetSeconds")
                .floatValue = 0f;
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
            step.FindPropertyRelative(
                    "Blocking")
                .boolValue = false;
            step.FindPropertyRelative(
                    "CompletionTimeoutSeconds")
                .floatValue = 5f;
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

            CopyMarkerArray(
                source.FindPropertyRelative(
                    "Markers"),
                destination.FindPropertyRelative(
                    "Markers"));

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
                        "TimingMode")
                    .enumValueIndex =
                        from.FindPropertyRelative(
                                "TimingMode")
                            .enumValueIndex;
                to.FindPropertyRelative(
                        "MarkerName")
                    .stringValue =
                        from.FindPropertyRelative(
                                "MarkerName")
                            .stringValue;
                to.FindPropertyRelative(
                        "MarkerOffsetSeconds")
                    .floatValue =
                        from.FindPropertyRelative(
                                "MarkerOffsetSeconds")
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
                to.FindPropertyRelative(
                        "Blocking")
                    .boolValue =
                        from.FindPropertyRelative(
                                "Blocking")
                            .boolValue;
                to.FindPropertyRelative(
                        "CompletionTimeoutSeconds")
                    .floatValue =
                        from.FindPropertyRelative(
                                "CompletionTimeoutSeconds")
                            .floatValue;
            }
        }

        private static void CopyMarkerArray(
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
                    destination.GetArrayElementAtIndex(
                        i);

                to.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        from.FindPropertyRelative(
                                "Name")
                            .stringValue;
                to.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue =
                        from.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue;
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

        private void ApplySpinConfettiTemplate(
            SerializedProperty transition)
        {
            Undo.RecordObject(
                _runtime,
                "Apply Spin Confetti Transition Template");

            transition.FindPropertyRelative(
                    "DurationSeconds")
                .floatValue = 1.2f;
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

            var markers =
                transition.FindPropertyRelative(
                    "Markers");
            markers.arraySize = 2;
            ConfigureMarker(
                markers.GetArrayElementAtIndex(
                    0),
                "swap",
                0.55f);
            ConfigureMarker(
                markers.GetArrayElementAtIndex(
                    1),
                "spin-end",
                0.90f);

            var steps =
                transition.FindPropertyRelative(
                    "Steps");
            steps.arraySize = 5;

            ConfigureActionStep(
                steps.GetArrayElementAtIndex(
                    0),
                0f,
                EventActionTypes.MotionPlay,
                "motion.quickchange",
                "spin",
                required:
                    true);

            ConfigureActionStep(
                steps.GetArrayElementAtIndex(
                    1),
                0.10f,
                EventActionTypes.EffectPlay,
                "effects.main",
                "confetti",
                required:
                    false);

            ResetStep(
                steps.GetArrayElementAtIndex(
                    2),
                AppearanceTransitionStepKind
                    .Commit,
                false);
            ConfigureMarkerStepTiming(
                steps.GetArrayElementAtIndex(
                    2),
                "swap",
                0f);

            ConfigureMarkerActionStep(
                steps.GetArrayElementAtIndex(
                    3),
                "swap",
                0f,
                EventActionTypes.EffectPlay,
                "effects.main",
                "sparkle-burst",
                required:
                    false);

            ConfigureMarkerActionStep(
                steps.GetArrayElementAtIndex(
                    4),
                "spin-end",
                0f,
                EventActionTypes.MotionRelease,
                "motion.quickchange",
                "spin",
                required:
                    true);

            transition.FindPropertyRelative(
                    "CancellationSteps")
                .arraySize = 0;

            ApplySerializedChanges();
            _lastMessage =
                "Applied spin + confetti template. Effect IDs are optional placeholders and can be replaced with registered effect IDs.";
            _lastMessageType =
                MessageType.Info;
        }

        private void ApplyInterruptCleanupTemplate(
            SerializedProperty transition)
        {
            Undo.RecordObject(
                _runtime,
                "Add Interrupt Cleanup Template");

            transition.FindPropertyRelative(
                    "QueuePolicy")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionQueuePolicy
                        .Interrupt;

            var cleanup =
                transition.FindPropertyRelative(
                    "CancellationSteps");
            cleanup.arraySize = 4;

            ConfigureActionStep(
                cleanup.GetArrayElementAtIndex(
                    0),
                0f,
                EventActionTypes.MotionRelease,
                "motion.quickchange",
                "spin",
                required:
                    false);
            ConfigureActionStep(
                cleanup.GetArrayElementAtIndex(
                    1),
                0f,
                EventActionTypes.EffectStop,
                "effects.main",
                "confetti",
                required:
                    false);
            ConfigureActionStep(
                cleanup.GetArrayElementAtIndex(
                    2),
                0f,
                EventActionTypes.EffectStop,
                "effects.main",
                "sparkle-burst",
                required:
                    false);
            ConfigureActionStep(
                cleanup.GetArrayElementAtIndex(
                    3),
                0f,
                EventActionTypes.AudioStop,
                "audio.main",
                "wardrobe-chime",
                required:
                    false);

            ApplySerializedChanges();
            _lastMessage =
                "Interrupt cleanup template added. Remove unused cleanup actions or replace their logical IDs before validation.";
            _lastMessageType =
                MessageType.Info;
        }

        private static void ConfigureMarker(
            SerializedProperty marker,
            string name,
            float timeSeconds)
        {
            marker.FindPropertyRelative(
                    "Name")
                .stringValue =
                    name;
            marker.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
        }

        private static void ConfigureMarkerStepTiming(
            SerializedProperty step,
            string markerName,
            float offsetSeconds)
        {
            step.FindPropertyRelative(
                    "TimingMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionTimingMode
                        .Marker;
            step.FindPropertyRelative(
                    "MarkerName")
                .stringValue =
                    markerName;
            step.FindPropertyRelative(
                    "MarkerOffsetSeconds")
                .floatValue =
                    offsetSeconds;
        }

        private static void ConfigureMarkerActionStep(
            SerializedProperty step,
            string markerName,
            float markerOffsetSeconds,
            string actionType,
            string targetId,
            string text,
            bool required)
        {
            ConfigureActionStep(
                step,
                0f,
                actionType,
                targetId,
                text,
                required);
            ConfigureMarkerStepTiming(
                step,
                markerName,
                markerOffsetSeconds);
        }

        private static void ConfigureActionStep(
            SerializedProperty step,
            float timeSeconds,
            string actionType,
            string targetId,
            string text,
            bool required)
        {
            ResetStep(
                step,
                AppearanceTransitionStepKind
                    .Action,
                false);
            step.FindPropertyRelative(
                    "TimeSeconds")
                .floatValue =
                    timeSeconds;
            step.FindPropertyRelative(
                    "ActionType")
                .stringValue =
                    actionType;
            step.FindPropertyRelative(
                    "TargetId")
                .stringValue =
                    targetId ?? string.Empty;
            step.FindPropertyRelative(
                    "Text")
                .stringValue =
                    text ?? string.Empty;
            step.FindPropertyRelative(
                    "Required")
                .boolValue =
                    required;
        }

        private static void SortStepsByResolvedTime(
            SerializedProperty transition,
            SerializedProperty steps)
        {
            for (var target = 0;
                 target < steps.arraySize - 1;
                 target++)
            {
                var best =
                    target;
                var bestTime =
                    ResolveSerializedStepTime(
                        transition,
                        steps.GetArrayElementAtIndex(
                            best));

                for (var candidate =
                         target + 1;
                     candidate <
                     steps.arraySize;
                     candidate++)
                {
                    var candidateTime =
                        ResolveSerializedStepTime(
                            transition,
                            steps.GetArrayElementAtIndex(
                                candidate));

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

        private static float ResolveSerializedStepTime(
            SerializedProperty transition,
            SerializedProperty step)
        {
            var mode =
                (AppearanceTransitionTimingMode)
                step.FindPropertyRelative(
                        "TimingMode")
                    .enumValueIndex;

            if (mode !=
                AppearanceTransitionTimingMode
                    .Marker)
            {
                return step.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue;
            }

            var markerName =
                step.FindPropertyRelative(
                        "MarkerName")
                    .stringValue;
            var offset =
                step.FindPropertyRelative(
                        "MarkerOffsetSeconds")
                    .floatValue;
            var markers =
                transition.FindPropertyRelative(
                    "Markers");

            for (var i = 0;
                 i < markers.arraySize;
                 i++)
            {
                var marker =
                    markers.GetArrayElementAtIndex(
                        i);

                if (string.Equals(
                        marker.FindPropertyRelative(
                                "Name")
                            .stringValue,
                        markerName,
                        StringComparison.Ordinal))
                {
                    return marker.FindPropertyRelative(
                            "TimeSeconds")
                        .floatValue +
                        offset;
                }
            }

            return float.PositiveInfinity;
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

        private void ExportTransitions(
            bool selectedOnly)
        {
            _serializedRuntime
                .ApplyModifiedProperties();

            if (!_runtime
                .RebuildConfiguration(
                    out var validationError))
            {
                _lastMessage =
                    "Transition export blocked by runtime validation: " +
                    validationError;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            var transitions =
                selectedOnly
                    ? new[]
                    {
                        CaptureTransition(
                            _transitions
                                .GetArrayElementAtIndex(
                                    Mathf.Clamp(
                                        _selectedTransitionIndex,
                                        0,
                                        _transitions.arraySize - 1)))
                    }
                    : CaptureAllTransitions();

            var packageId =
                selectedOnly &&
                transitions.Length == 1
                    ? transitions[0].Id
                    : "appearance-transitions";

            var package =
                P11AppearanceTransitionPackageUtility
                    .CreatePackage(
                        packageId,
                        transitions);

            if (!P11AppearanceTransitionPackageUtility
                .TrySerialize(
                    package,
                    out var json,
                    out var error))
            {
                _lastMessage =
                    "Transition export failed: " +
                    error;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            var path =
                EditorUtility.SaveFilePanel(
                    "Export Appearance Transitions",
                    Application.dataPath,
                    SanitizeFileName(
                        package.PackageId) +
                    ".json",
                    "json");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    path,
                    json);
                _lastMessage =
                    $"Exported {transitions.Length} transition(s) to {path}.";
                _lastMessageType =
                    MessageType.Info;
            }
            catch (Exception exception)
            {
                _lastMessage =
                    "Transition export failed: " +
                    exception.Message;
                _lastMessageType =
                    MessageType.Error;
            }
        }

        private void ImportTransitions()
        {
            var path =
                EditorUtility.OpenFilePanel(
                    "Import Appearance Transitions",
                    Application.dataPath,
                    "json");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            string json;

            try
            {
                json =
                    File.ReadAllText(
                        path);
            }
            catch (Exception exception)
            {
                _lastMessage =
                    "Transition import failed: " +
                    exception.Message;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            if (!P11AppearanceTransitionPackageUtility
                .TryDeserialize(
                    json,
                    out var package,
                    out var error))
            {
                _lastMessage =
                    "Transition import failed: " +
                    error;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            _serializedRuntime.Update();
            var before =
                CaptureAllTransitions();
            var collisions =
                CountImportCollisions(
                    package.Transitions);

            if (collisions > 0 &&
                !EditorUtility.DisplayDialog(
                    "Replace Transition IDs?",
                    $"{collisions} imported transition id(s) already exist. Replace the existing definitions with the imported versions?",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Import Appearance Transitions");

            foreach (var transition in
                     package.Transitions)
            {
                var index =
                    FindTransitionIndex(
                        transition.Id);

                if (index < 0)
                {
                    index =
                        _transitions.arraySize;
                    _transitions.arraySize =
                        index + 1;
                }

                WriteTransition(
                    _transitions
                        .GetArrayElementAtIndex(
                            index),
                    transition);
            }

            _serializedRuntime
                .ApplyModifiedProperties();

            if (!_runtime
                .RebuildConfiguration(
                    out error))
            {
                _serializedRuntime.Update();
                WriteAllTransitions(
                    before);
                _serializedRuntime
                    .ApplyModifiedProperties();
                _runtime.RebuildConfiguration(
                    out _);

                _lastMessage =
                    "Transition import was rolled back because runtime validation failed: " +
                    error;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            EditorUtility.SetDirty(
                _runtime);

            if (package.Transitions.Length > 0)
            {
                _serializedRuntime.Update();
                _selectedTransitionIndex =
                    Math.Max(
                        0,
                        FindTransitionIndex(
                            package.Transitions[0]
                                .Id));
            }

            _lastMessage =
                $"Imported {package.Transitions.Length} transition(s) from package '{package.PackageId}'.";
            _lastMessageType =
                MessageType.Info;
            Repaint();
        }

        private AppearanceTransitionPreset[]
            CaptureAllTransitions()
        {
            var result =
                new AppearanceTransitionPreset[
                    _transitions.arraySize];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                result[i] =
                    CaptureTransition(
                        _transitions
                            .GetArrayElementAtIndex(
                                i));
            }

            return result;
        }

        private static AppearanceTransitionPreset
            CaptureTransition(
                SerializedProperty transition)
        {
            return new AppearanceTransitionPreset
            {
                Id =
                    transition.FindPropertyRelative(
                            "TransitionId")
                        .stringValue,
                DurationSeconds =
                    transition.FindPropertyRelative(
                            "DurationSeconds")
                        .floatValue,
                QueuePolicy =
                    (AppearanceTransitionQueuePolicy)
                    transition.FindPropertyRelative(
                            "QueuePolicy")
                        .enumValueIndex,
                FallbackPolicy =
                    (AppearanceTransitionFallbackPolicy)
                    transition.FindPropertyRelative(
                            "FallbackPolicy")
                        .enumValueIndex,
                Steps =
                    CaptureSteps(
                        transition.FindPropertyRelative(
                            "Steps")),
                CancellationSteps =
                    CaptureSteps(
                        transition.FindPropertyRelative(
                            "CancellationSteps"))
            };
        }

        private static AppearanceTransitionStep[]
            CaptureSteps(
                SerializedProperty steps)
        {
            var result =
                new AppearanceTransitionStep[
                    steps.arraySize];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);

                result[i] =
                    new AppearanceTransitionStep
                    {
                        TimeSeconds =
                            step.FindPropertyRelative(
                                    "TimeSeconds")
                                .floatValue,
                        Kind =
                            (AppearanceTransitionStepKind)
                            step.FindPropertyRelative(
                                    "Kind")
                                .enumValueIndex,
                        ActionType =
                            step.FindPropertyRelative(
                                    "ActionType")
                                .stringValue,
                        TargetId =
                            step.FindPropertyRelative(
                                    "TargetId")
                                .stringValue,
                        Name =
                            step.FindPropertyRelative(
                                    "Name")
                                .stringValue,
                        Text =
                            step.FindPropertyRelative(
                                    "Text")
                                .stringValue,
                        Value =
                            step.FindPropertyRelative(
                                    "Value")
                                .doubleValue,
                        HasValue =
                            step.FindPropertyRelative(
                                    "HasValue")
                                .boolValue,
                        Required =
                            step.FindPropertyRelative(
                                    "Required")
                                .boolValue
                    };
            }

            return result;
        }

        private static void WriteTransition(
            SerializedProperty destination,
            AppearanceTransitionPreset source)
        {
            destination.FindPropertyRelative(
                    "TransitionId")
                .stringValue =
                    source?.Id ??
                    string.Empty;
            destination.FindPropertyRelative(
                    "DurationSeconds")
                .floatValue =
                    (float)(
                        source?.DurationSeconds ??
                        0.0);
            destination.FindPropertyRelative(
                    "QueuePolicy")
                .enumValueIndex =
                    (int)(
                        source?.QueuePolicy ??
                        AppearanceTransitionQueuePolicy
                            .QueueLatest);
            destination.FindPropertyRelative(
                    "FallbackPolicy")
                .enumValueIndex =
                    (int)(
                        source?.FallbackPolicy ??
                        AppearanceTransitionFallbackPolicy
                            .Immediate);

            WriteSteps(
                destination.FindPropertyRelative(
                    "Steps"),
                source?.Steps);
            WriteSteps(
                destination.FindPropertyRelative(
                    "CancellationSteps"),
                source?.CancellationSteps);
        }

        private static void WriteSteps(
            SerializedProperty destination,
            AppearanceTransitionStep[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceTransitionStep>();

            destination.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var step =
                    source[i] ??
                    new AppearanceTransitionStep();
                var property =
                    destination
                        .GetArrayElementAtIndex(
                            i);

                property.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue =
                        (float)step.TimeSeconds;
                property.FindPropertyRelative(
                        "Kind")
                    .enumValueIndex =
                        (int)step.Kind;
                property.FindPropertyRelative(
                        "ActionType")
                    .stringValue =
                        step.ActionType ??
                        string.Empty;
                property.FindPropertyRelative(
                        "TargetId")
                    .stringValue =
                        step.TargetId ??
                        string.Empty;
                property.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        step.Name ??
                        string.Empty;
                property.FindPropertyRelative(
                        "Text")
                    .stringValue =
                        step.Text ??
                        string.Empty;
                property.FindPropertyRelative(
                        "Value")
                    .doubleValue =
                        step.Value;
                property.FindPropertyRelative(
                        "HasValue")
                    .boolValue =
                        step.HasValue;
                property.FindPropertyRelative(
                        "Required")
                    .boolValue =
                        step.Required;
            }
        }

        private void WriteAllTransitions(
            AppearanceTransitionPreset[] transitions)
        {
            transitions ??=
                Array.Empty<
                    AppearanceTransitionPreset>();

            _transitions.arraySize =
                transitions.Length;

            for (var i = 0;
                 i < transitions.Length;
                 i++)
            {
                WriteTransition(
                    _transitions
                        .GetArrayElementAtIndex(
                            i),
                    transitions[i]);
            }
        }

        private int CountImportCollisions(
            AppearanceTransitionPreset[] transitions)
        {
            var count = 0;

            foreach (var transition in
                     transitions ??
                     Array.Empty<
                         AppearanceTransitionPreset>())
            {
                if (transition != null &&
                    FindTransitionIndex(
                        transition.Id) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int FindTransitionIndex(
            string id)
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
                        id,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string SanitizeFileName(
            string value)
        {
            value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "appearance-transitions"
                    : value.Trim();

            foreach (var invalid in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return value;
        }

        private void ApplySerializedChanges()
        {
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
