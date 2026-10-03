using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.Tracking.Mixing;

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
        private AnimationClip _markerClip;
        private BakedMotionCueAsset _markerCueAsset;
        private float _markerSnapThresholdSeconds = 0.08f;
        private bool _showDependencyOverview = true;
        private string _lastMessage;
        private MessageType _lastMessageType =
            MessageType.Info;
        private AppearanceTransitionPackage
            _pendingPackage;
        private string _pendingPackageSource;

        [MenuItem("VCR/P11/Open Appearance Transition Timeline")]
        public static void Open()
        {
            GetWindow<
                    P11AppearanceTransitionTimelineEditor>(
                    "VCR Transition Timeline")
                .Show();
        }

        public static void OpenWithMarkerClip(
            AnimationClip clip)
        {
            var window =
                GetWindow<
                    P11AppearanceTransitionTimelineEditor>(
                    "VCR Transition Timeline");
            window._markerClip =
                clip;
            window.Show();
            window.Repaint();
        }

        public static void OpenWithMarkerCue(
            BakedMotionCueAsset cueAsset)
        {
            var window =
                GetWindow<
                    P11AppearanceTransitionTimelineEditor>(
                    "VCR Transition Timeline");
            window._markerCueAsset =
                cueAsset;
            window.Show();
            window.Repaint();
        }

        public static void OpenWithPackage(
            AppearanceTransitionPackage package,
            string sourceLabel)
        {
            var window =
                GetWindow<
                    P11AppearanceTransitionTimelineEditor>(
                    "VCR Transition Timeline");
            window._pendingPackage =
                package;
            window._pendingPackageSource =
                sourceLabel;
            window.Show();
            window.Repaint();
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

            DrawPendingPackageImport();
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
            DrawDependencyOverview(
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

        private void DrawPendingPackageImport()
        {
            if (_pendingPackage == null)
            {
                return;
            }

            var transitionCount =
                _pendingPackage.Transitions?.Length ??
                0;
            var source =
                string.IsNullOrWhiteSpace(
                    _pendingPackageSource)
                    ? "<library>"
                    : _pendingPackageSource;

            EditorGUILayout.HelpBox(
                $"Pending package '{_pendingPackage.PackageId}' from {source}\nVersion: {_pendingPackage.Version}    Transitions: {transitionCount}",
                MessageType.Info);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Import Pending Package"))
                {
                    var package =
                        _pendingPackage;
                    var sourceLabel =
                        _pendingPackageSource;
                    _pendingPackage =
                        null;
                    _pendingPackageSource =
                        null;
                    ImportPackage(
                        package,
                        sourceLabel);
                }

                if (GUILayout.Button(
                        "Discard Pending Package"))
                {
                    _pendingPackage =
                        null;
                    _pendingPackageSource =
                        null;
                    _lastMessage =
                        "Pending transition package discarded.";
                    _lastMessageType =
                        MessageType.Info;
                }
            }

            EditorGUILayout.Space();
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

                if (GUILayout.Button(
                        "Generate Step IDs"))
                {
                    GenerateMissingStepIds(
                        transition);
                }
            }

            var queue =
                (AppearanceTransitionQueuePolicy)
                transition
                    .FindPropertyRelative(
                        "QueuePolicy")
                    .enumValueIndex;

            var cleanup =
                transition.FindPropertyRelative(
                    "CancellationSteps");

            if (queue ==
                    AppearanceTransitionQueuePolicy
                        .Interrupt &&
                cleanup.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Interrupt requires at least one explicit cancellation cleanup action. Add motion.release, effect.stop, audio.stop, or another non-appearance cleanup action below.",
                    MessageType.Warning);
            }

            if (HasBlockingStep(
                    transition.FindPropertyRelative(
                        "Steps")) &&
                cleanup.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Blocking actions also require cancellation cleanup so timeout/failure cannot leave motion, particles, or audio running.",
                    MessageType.Warning);
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

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                _markerClip =
                    (AnimationClip)
                    EditorGUILayout.ObjectField(
                        "AnimationClip Source",
                        _markerClip,
                        typeof(AnimationClip),
                        false);
                _markerCueAsset =
                    (BakedMotionCueAsset)
                    EditorGUILayout.ObjectField(
                        "Baked Cue Source",
                        _markerCueAsset,
                        typeof(BakedMotionCueAsset),
                        false);
                _markerSnapThresholdSeconds =
                    Mathf.Max(
                        0f,
                        EditorGUILayout.FloatField(
                            "Snap Threshold (s)",
                            _markerSnapThresholdSeconds));

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    using (new EditorGUI
                               .DisabledScope(
                                   _markerClip == null))
                    {
                        if (GUILayout.Button(
                                "Import Clip Markers"))
                        {
                            ImportAnimationClipMarkers(
                                transition);
                        }
                    }

                    using (new EditorGUI
                               .DisabledScope(
                                   _markerCueAsset == null))
                    {
                        if (GUILayout.Button(
                                "Import Baked Markers"))
                        {
                            ImportBakedCueMarkers(
                                transition);
                        }
                    }

                    using (new EditorGUI
                               .DisabledScope(
                                   markers.arraySize == 0))
                    {
                        if (GUILayout.Button(
                                "Snap Absolute Steps"))
                        {
                            SnapAbsoluteStepsToMarkers(
                                transition);
                        }
                    }
                }
            }

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

        private void ImportAnimationClipMarkers(
            SerializedProperty transition)
        {
            if (!P11MotionMarkerUtility
                .TryExtractFromAnimationClip(
                    _markerClip,
                    out var extracted,
                    out var error))
            {
                _lastMessage =
                    "AnimationClip marker import failed: " +
                    error;
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            ImportMarkers(
                transition,
                P11MotionMarkerUtility
                    .ToAppearanceMarkers(
                        extracted),
                _markerClip != null
                    ? _markerClip.length
                    : 0f,
                _markerClip != null
                    ? _markerClip.name
                    : "AnimationClip");
        }

        private void ImportBakedCueMarkers(
            SerializedProperty transition)
        {
            var cue =
                _markerCueAsset?.Cue;

            if (cue == null)
            {
                _lastMessage =
                    "Baked cue marker import failed: cue asset is empty.";
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            ImportMarkers(
                transition,
                P11MotionMarkerUtility
                    .ToAppearanceMarkers(
                        cue.Markers),
                cue.DurationSeconds,
                cue.CueId ??
                    _markerCueAsset.name);
        }

        private void ImportMarkers(
            SerializedProperty transition,
            AppearanceTransitionMarker[] imported,
            float sourceDurationSeconds,
            string sourceLabel)
        {
            imported ??=
                Array.Empty<
                    AppearanceTransitionMarker>();

            if (imported.Length == 0)
            {
                _lastMessage =
                    $"No explicit VCR markers were found in '{sourceLabel}'.";
                _lastMessageType =
                    MessageType.Warning;
                return;
            }

            if (!ValidateImportedMarkers(
                    imported,
                    sourceDurationSeconds,
                    out var markerError))
            {
                _lastMessage =
                    $"Marker import from '{sourceLabel}' failed: {markerError}";
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            var markers =
                transition.FindPropertyRelative(
                    "Markers");
            var collisions = 0;

            foreach (var marker in imported)
            {
                if (marker != null &&
                    FindMarkerIndex(
                        markers,
                        marker.Name) >= 0)
                {
                    collisions++;
                }
            }

            if (collisions > 0 &&
                !EditorUtility.DisplayDialog(
                    "Replace Marker Times?",
                    $"{collisions} marker name(s) already exist. Replace those marker times with values from '{sourceLabel}'?",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Import Transition Markers");

            foreach (var marker in imported)
            {
                if (marker == null ||
                    string.IsNullOrWhiteSpace(
                        marker.Name))
                {
                    continue;
                }

                var index =
                    FindMarkerIndex(
                        markers,
                        marker.Name);

                if (index < 0)
                {
                    index =
                        markers.arraySize;
                    markers.arraySize =
                        index + 1;
                }

                var property =
                    markers.GetArrayElementAtIndex(
                        index);
                property.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        marker.Name;
                property.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue =
                        (float)marker.TimeSeconds;
            }

            SortMarkersByTime(
                markers);

            var duration =
                transition.FindPropertyRelative(
                    "DurationSeconds");
            var previousDuration =
                duration.floatValue;
            var latestMarkerTime =
                0f;

            foreach (var marker in imported)
            {
                if (marker != null)
                {
                    latestMarkerTime =
                        Mathf.Max(
                            latestMarkerTime,
                            (float)marker.TimeSeconds);
                }
            }

            duration.floatValue =
                Mathf.Max(
                    previousDuration,
                    latestMarkerTime);

            _serializedRuntime
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _runtime);

            _lastMessage =
                $"Imported {imported.Length} marker(s) from '{sourceLabel}'." +
                (duration.floatValue >
                 previousDuration
                    ? $" Transition duration expanded to {duration.floatValue:0.###}s."
                    : string.Empty);
            _lastMessageType =
                MessageType.Info;
        }

        private void SnapAbsoluteStepsToMarkers(
            SerializedProperty transition)
        {
            var markers =
                transition.FindPropertyRelative(
                    "Markers");
            var steps =
                transition.FindPropertyRelative(
                    "Steps");

            if (markers.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Snap Transition Steps To Markers");

            var snapped = 0;

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);
                var timing =
                    (AppearanceTransitionTimingMode)
                    step.FindPropertyRelative(
                            "TimingMode")
                        .enumValueIndex;

                if (timing !=
                    AppearanceTransitionTimingMode
                        .AbsoluteTime)
                {
                    continue;
                }

                var stepTime =
                    step.FindPropertyRelative(
                            "TimeSeconds")
                        .floatValue;
                var nearestIndex = -1;
                var nearestDistance =
                    float.PositiveInfinity;

                for (var markerIndex = 0;
                     markerIndex < markers.arraySize;
                     markerIndex++)
                {
                    var marker =
                        markers.GetArrayElementAtIndex(
                            markerIndex);
                    var markerTime =
                        marker.FindPropertyRelative(
                                "TimeSeconds")
                            .floatValue;
                    var distance =
                        Mathf.Abs(
                            markerTime -
                            stepTime);

                    if (distance <
                        nearestDistance)
                    {
                        nearestDistance =
                            distance;
                        nearestIndex =
                            markerIndex;
                    }
                }

                if (nearestIndex < 0 ||
                    nearestDistance >
                        _markerSnapThresholdSeconds)
                {
                    continue;
                }

                var nearest =
                    markers.GetArrayElementAtIndex(
                        nearestIndex);
                step.FindPropertyRelative(
                        "TimingMode")
                    .enumValueIndex =
                        (int)
                        AppearanceTransitionTimingMode
                            .Marker;
                step.FindPropertyRelative(
                        "MarkerName")
                    .stringValue =
                        nearest.FindPropertyRelative(
                                "Name")
                            .stringValue;
                step.FindPropertyRelative(
                        "MarkerOffsetSeconds")
                    .floatValue = 0f;
                snapped++;
            }

            _serializedRuntime
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _runtime);

            _lastMessage =
                snapped > 0
                    ? $"Snapped {snapped} absolute-time step(s) to markers within {_markerSnapThresholdSeconds:0.###}s."
                    : $"No absolute-time steps were within {_markerSnapThresholdSeconds:0.###}s of a marker.";
            _lastMessageType =
                snapped > 0
                    ? MessageType.Info
                    : MessageType.Warning;
        }

        private static bool ValidateImportedMarkers(
            AppearanceTransitionMarker[] markers,
            float sourceDurationSeconds,
            out string error)
        {
            error = null;
            var names =
                new System.Collections.Generic
                    .HashSet<string>(
                        StringComparer.Ordinal);

            foreach (var marker in
                     markers ??
                     Array.Empty<
                         AppearanceTransitionMarker>())
            {
                if (marker == null ||
                    string.IsNullOrWhiteSpace(
                        marker.Name))
                {
                    error =
                        "Imported marker requires a non-empty name.";
                    return false;
                }

                if (double.IsNaN(
                        marker.TimeSeconds) ||
                    double.IsInfinity(
                        marker.TimeSeconds) ||
                    marker.TimeSeconds < 0.0 ||
                    (sourceDurationSeconds >= 0f &&
                     marker.TimeSeconds >
                         sourceDurationSeconds +
                         0.0001f))
                {
                    error =
                        $"Marker '{marker.Name}' is outside the source duration.";
                    return false;
                }

                if (!names.Add(
                        marker.Name))
                {
                    error =
                        $"Imported marker set contains duplicate name '{marker.Name}'.";
                    return false;
                }
            }

            return true;
        }

        private static int FindMarkerIndex(
            SerializedProperty markers,
            string markerName)
        {
            for (var i = 0;
                 i < markers.arraySize;
                 i++)
            {
                if (string.Equals(
                        markers
                            .GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "Name")
                            .stringValue,
                        markerName,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SortMarkersByTime(
            SerializedProperty markers)
        {
            for (var target = 0;
                 target < markers.arraySize - 1;
                 target++)
            {
                var best =
                    target;
                var bestTime =
                    markers.GetArrayElementAtIndex(
                            target)
                        .FindPropertyRelative(
                            "TimeSeconds")
                        .floatValue;

                for (var candidate =
                         target + 1;
                     candidate <
                     markers.arraySize;
                     candidate++)
                {
                    var candidateTime =
                        markers.GetArrayElementAtIndex(
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
                    markers.MoveArrayElement(
                        best,
                        target);
                }
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

        private void DrawDependencyOverview(
            SerializedProperty transition)
        {
            var steps =
                transition.FindPropertyRelative(
                    "Steps");

            _showDependencyOverview =
                EditorGUILayout.Foldout(
                    _showDependencyOverview,
                    "Dependency Overview",
                    toggleOnLabelClick:
                        true);

            if (!_showDependencyOverview)
            {
                return;
            }

            DrawDependencyGraphPreview(
                transition,
                steps);

            EditorGUILayout.Space();

            var knownActionIds =
                new System.Collections.Generic
                    .HashSet<string>(
                        StringComparer.Ordinal);
            var allGroups = 0;
            var anyGroups = 0;
            var edges = 0;
            var invalidEdges = 0;

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                for (var i = 0;
                     i < steps.arraySize;
                     i++)
                {
                    var step =
                        steps.GetArrayElementAtIndex(
                            i);
                    var kind =
                        (AppearanceTransitionStepKind)
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex;
                    var stepId =
                        step.FindPropertyRelative(
                                "StepId")
                            .stringValue;
                    var nodeLabel =
                        kind ==
                            AppearanceTransitionStepKind
                                .Commit
                            ? "appearance.commit"
                            : !string.IsNullOrWhiteSpace(
                                  stepId)
                                ? stepId
                                : $"action#{i + 1}";

                    var mode =
                        (AppearanceTransitionDependencyMode)
                        step.FindPropertyRelative(
                                "DependencyMode")
                            .enumValueIndex;
                    var dependencies =
                        step.FindPropertyRelative(
                            "DependsOnStepIds");

                    if (mode !=
                        AppearanceTransitionDependencyMode
                            .None)
                    {
                        if (mode ==
                            AppearanceTransitionDependencyMode
                                .All)
                        {
                            allGroups++;
                        }
                        else if (mode ==
                                 AppearanceTransitionDependencyMode
                                     .Any)
                        {
                            anyGroups++;
                        }

                        EditorGUILayout.LabelField(
                            $"{i + 1}. {nodeLabel} waits {mode}",
                            EditorStyles.boldLabel);

                        if (dependencies.arraySize == 0)
                        {
                            invalidEdges++;
                            EditorGUILayout.HelpBox(
                                "Dependency mode is enabled but no source Step ID is selected.",
                                MessageType.Warning);
                        }

                        for (var dependencyIndex = 0;
                             dependencyIndex <
                             dependencies.arraySize;
                             dependencyIndex++)
                        {
                            var dependencyId =
                                dependencies
                                    .GetArrayElementAtIndex(
                                        dependencyIndex)
                                    .stringValue;
                            var valid =
                                !string.IsNullOrWhiteSpace(
                                    dependencyId) &&
                                knownActionIds.Contains(
                                    dependencyId);

                            edges++;

                            if (!valid)
                            {
                                invalidEdges++;
                            }

                            EditorGUILayout.LabelField(
                                valid
                                    ? $"    ← {dependencyId}"
                                    : $"    ← {dependencyId ?? "<empty>"}  [missing/forward]",
                                valid
                                    ? EditorStyles.miniLabel
                                    : EditorStyles
                                        .miniBoldLabel);
                        }

                        EditorGUILayout.LabelField(
                            $"    timeout {step.FindPropertyRelative("DependencyTimeoutSeconds").floatValue:0.###}s",
                            EditorStyles.miniLabel);
                    }

                    if (kind ==
                            AppearanceTransitionStepKind
                                .Action &&
                        !string.IsNullOrWhiteSpace(
                            stepId))
                    {
                        if (!knownActionIds.Add(
                                stepId))
                        {
                            invalidEdges++;
                            EditorGUILayout.HelpBox(
                                $"Duplicate action Step ID '{stepId}'.",
                                MessageType.Warning);
                        }
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    $"Edges: {edges}    All groups: {allGroups}    Any groups: {anyGroups}    Invalid: {invalidEdges}",
                    invalidEdges == 0
                        ? EditorStyles.miniLabel
                        : EditorStyles.miniBoldLabel);

                if (edges == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No cross-step completion dependencies are authored. Blocking on an individual action can still be used independently.",
                        MessageType.None);
                }
                else if (invalidEdges == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Dependency references are structurally ordered. Validate & Apply still checks executor/completion-probe availability.",
                        MessageType.Info);
                }
            }
        }

        private static void DrawDependencyGraphPreview(
            SerializedProperty transition,
            SerializedProperty steps)
        {
            if (steps == null ||
                steps.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No transition steps are available for dependency visualization.",
                    MessageType.None);
                return;
            }

            var nodeHeight =
                22f;
            var nodeWidth =
                136f;
            var rowGap =
                8f;
            var canvasHeight =
                Mathf.Clamp(
                    28f +
                    steps.arraySize *
                    (nodeHeight + rowGap),
                    110f,
                    460f);
            var canvas =
                GUILayoutUtility.GetRect(
                    100f,
                    canvasHeight,
                    GUILayout.ExpandWidth(
                        true));
            GUI.Box(
                canvas,
                GUIContent.none,
                EditorStyles.helpBox);

            var inner =
                new Rect(
                    canvas.x + 10f,
                    canvas.y + 10f,
                    Mathf.Max(
                        1f,
                        canvas.width - 20f),
                    Mathf.Max(
                        1f,
                        canvas.height - 20f));
            var duration =
                Mathf.Max(
                    0.001f,
                    transition.FindPropertyRelative(
                            "DurationSeconds")
                        .floatValue);
            var nodeRects =
                new Rect[
                    steps.arraySize];
            var idRects =
                new System.Collections.Generic
                    .Dictionary<string, Rect>(
                        StringComparer.Ordinal);
            var idIndices =
                new System.Collections.Generic
                    .Dictionary<string, int>(
                        StringComparer.Ordinal);

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);
                var resolved =
                    ResolveSerializedStepTime(
                        transition,
                        step);

                if (float.IsNaN(
                        resolved) ||
                    float.IsInfinity(
                        resolved))
                {
                    resolved =
                        duration *
                        (i /
                         Mathf.Max(
                             1f,
                             steps.arraySize - 1f));
                }

                var normalized =
                    Mathf.Clamp01(
                        resolved /
                        duration);
                var x =
                    Mathf.Lerp(
                        inner.x,
                        Mathf.Max(
                            inner.x,
                            inner.xMax -
                            nodeWidth),
                        normalized);
                var y =
                    inner.y +
                    i *
                    (nodeHeight + rowGap);

                nodeRects[
                    i] =
                        new Rect(
                            x,
                            y,
                            nodeWidth,
                            nodeHeight);

                var kind =
                    (AppearanceTransitionStepKind)
                    step.FindPropertyRelative(
                            "Kind")
                        .enumValueIndex;
                var stepId =
                    step.FindPropertyRelative(
                            "StepId")
                        .stringValue;

                if (kind ==
                        AppearanceTransitionStepKind
                            .Action &&
                    !string.IsNullOrWhiteSpace(
                        stepId) &&
                    !idRects.ContainsKey(
                        stepId))
                {
                    idRects.Add(
                        stepId,
                        nodeRects[
                            i]);
                    idIndices.Add(
                        stepId,
                        i);
                }
            }

            Handles.BeginGUI();

            for (var targetIndex = 0;
                 targetIndex < steps.arraySize;
                 targetIndex++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        targetIndex);
                var dependencies =
                    step.FindPropertyRelative(
                        "DependsOnStepIds");

                for (var dependencyIndex = 0;
                     dependencyIndex <
                     dependencies.arraySize;
                     dependencyIndex++)
                {
                    var dependencyId =
                        dependencies
                            .GetArrayElementAtIndex(
                                dependencyIndex)
                            .stringValue;

                    if (string.IsNullOrWhiteSpace(
                            dependencyId) ||
                        !idRects.TryGetValue(
                            dependencyId,
                            out var sourceRect) ||
                        !idIndices.TryGetValue(
                            dependencyId,
                            out var sourceIndex) ||
                        sourceIndex >=
                            targetIndex)
                    {
                        continue;
                    }

                    var targetRect =
                        nodeRects[
                            targetIndex];
                    var from =
                        new Vector3(
                            sourceRect.xMax,
                            sourceRect.center.y,
                            0f);
                    var to =
                        new Vector3(
                            targetRect.x,
                            targetRect.center.y,
                            0f);
                    var tangent =
                        Mathf.Max(
                            28f,
                            Mathf.Abs(
                                to.x -
                                from.x) *
                            0.45f);

                    Handles.DrawBezier(
                        from,
                        to,
                        from +
                        Vector3.right *
                        tangent,
                        to +
                        Vector3.left *
                        tangent,
                        EditorGUIUtility.isProSkin
                            ? new Color(
                                0.65f,
                                0.72f,
                                0.82f,
                                0.9f)
                            : new Color(
                                0.25f,
                                0.32f,
                                0.42f,
                                0.9f),
                        null,
                        2f);
                }
            }

            Handles.EndGUI();

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);
                var kind =
                    (AppearanceTransitionStepKind)
                    step.FindPropertyRelative(
                            "Kind")
                        .enumValueIndex;
                var stepId =
                    step.FindPropertyRelative(
                            "StepId")
                        .stringValue;
                var actionType =
                    step.FindPropertyRelative(
                            "ActionType")
                        .stringValue;
                var mode =
                    (AppearanceTransitionDependencyMode)
                    step.FindPropertyRelative(
                            "DependencyMode")
                        .enumValueIndex;
                var blocking =
                    step.FindPropertyRelative(
                            "Blocking")
                        .boolValue;
                var resolved =
                    ResolveSerializedStepTime(
                        transition,
                        step);
                var baseLabel =
                    kind ==
                        AppearanceTransitionStepKind.Commit
                        ? "commit"
                        : !string.IsNullOrWhiteSpace(
                              stepId)
                            ? stepId
                            : ShortActionLabel(
                                actionType);
                var flags =
                    string.Empty;

                if (blocking)
                {
                    flags +=
                        " B";
                }

                if (mode ==
                    AppearanceTransitionDependencyMode.All)
                {
                    flags +=
                        " ALL";
                }
                else if (mode ==
                         AppearanceTransitionDependencyMode.Any)
                {
                    flags +=
                        " ANY";
                }

                var label =
                    float.IsInfinity(
                        resolved)
                        ? $"{baseLabel}{flags}"
                        : $"{baseLabel}{flags}  {resolved:0.##}s";

                GUI.Label(
                    nodeRects[
                        i],
                    label,
                    EditorStyles.miniButton);
            }

            GUI.Label(
                new Rect(
                    inner.x,
                    inner.yMax - 14f,
                    90f,
                    14f),
                "0s",
                EditorStyles.miniLabel);
            GUI.Label(
                new Rect(
                    inner.xMax - 90f,
                    inner.yMax - 14f,
                    90f,
                    14f),
                duration.ToString(
                    "0.##") +
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
                                "StepId")
                            .stringValue =
                                string.Empty;
                        step.FindPropertyRelative(
                                "DependencyMode")
                            .enumValueIndex =
                                (int)
                                AppearanceTransitionDependencyMode
                                    .None;
                        step.FindPropertyRelative(
                                "DependsOnStepIds")
                            .arraySize = 0;
                        step.FindPropertyRelative(
                                "DependencyTimeoutSeconds")
                            .floatValue = 5f;
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
                        if (!cleanup)
                        {
                            EditorGUILayout.PropertyField(
                                step.FindPropertyRelative(
                                    "StepId"),
                                new GUIContent(
                                    "Step ID"));
                        }

                        DrawActionStep(
                            step);
                    }

                    if (!cleanup)
                    {
                        DrawDependencies(
                            steps,
                            i,
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

        private void DrawDependencies(
            SerializedProperty steps,
            int stepIndex,
            SerializedProperty step)
        {
            var mode =
                step.FindPropertyRelative(
                    "DependencyMode");

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(
                mode,
                new GUIContent(
                    "Wait For"));

            if ((AppearanceTransitionDependencyMode)
                    mode.enumValueIndex ==
                AppearanceTransitionDependencyMode.None)
            {
                step.FindPropertyRelative(
                        "DependsOnStepIds")
                    .arraySize = 0;
                return;
            }

            var eligible =
                BuildPreviousActionStepIds(
                    steps,
                    stepIndex);

            if (eligible.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No earlier Action step has a Step ID. Dependencies may only reference earlier actions.",
                    MessageType.Warning);
            }

            var dependencies =
                step.FindPropertyRelative(
                    "DependsOnStepIds");

            for (var i = 0;
                 i < dependencies.arraySize;
                 i++)
            {
                var dependency =
                    dependencies
                        .GetArrayElementAtIndex(
                            i);

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    var current =
                        Array.IndexOf(
                            eligible,
                            dependency.stringValue);

                    if (eligible.Length > 0)
                    {
                        var next =
                            EditorGUILayout.Popup(
                                i == 0
                                    ? "Dependencies"
                                    : GUIContent.none.text,
                                Mathf.Max(
                                    0,
                                    current),
                                eligible);
                        dependency.stringValue =
                            eligible[
                                next];
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(
                            dependency,
                            i == 0
                                ? new GUIContent(
                                    "Dependencies")
                                : GUIContent.none);
                    }

                    if (GUILayout.Button(
                            "×",
                            GUILayout.Width(28f)))
                    {
                        dependencies
                            .DeleteArrayElementAtIndex(
                                i);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(
                       eligible.Length == 0 ||
                       dependencies.arraySize >=
                           eligible.Length))
            {
                if (GUILayout.Button(
                        "Add Dependency"))
                {
                    var nextId =
                        FindFirstUnusedDependency(
                            eligible,
                            dependencies);

                    if (nextId != null)
                    {
                        var index =
                            dependencies.arraySize;
                        dependencies.arraySize =
                            index + 1;
                        dependencies
                            .GetArrayElementAtIndex(
                                index)
                            .stringValue =
                                nextId;
                    }
                }
            }

            EditorGUILayout.PropertyField(
                step.FindPropertyRelative(
                    "DependencyTimeoutSeconds"),
                new GUIContent(
                    "Dependency Timeout (s)"));

            EditorGUILayout.HelpBox(
                (AppearanceTransitionDependencyMode)
                    mode.enumValueIndex ==
                AppearanceTransitionDependencyMode.All
                    ? "This step waits until every referenced earlier action reports completion."
                    : "This step waits until any referenced earlier action reports completion.",
                MessageType.None);
        }

        private static string[] BuildPreviousActionStepIds(
            SerializedProperty steps,
            int stepIndex)
        {
            var ids =
                new System.Collections.Generic
                    .List<string>();

            for (var i = 0;
                 i < stepIndex;
                 i++)
            {
                var candidate =
                    steps.GetArrayElementAtIndex(
                        i);

                if ((AppearanceTransitionStepKind)
                        candidate.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex !=
                    AppearanceTransitionStepKind.Action)
                {
                    continue;
                }

                var id =
                    candidate.FindPropertyRelative(
                            "StepId")
                        .stringValue;

                if (!string.IsNullOrWhiteSpace(
                        id) &&
                    !ids.Contains(
                        id))
                {
                    ids.Add(
                        id);
                }
            }

            return ids.ToArray();
        }

        private static string FindFirstUnusedDependency(
            string[] eligible,
            SerializedProperty dependencies)
        {
            foreach (var id in eligible)
            {
                var used = false;

                for (var i = 0;
                     i < dependencies.arraySize;
                     i++)
                {
                    if (string.Equals(
                            dependencies
                                .GetArrayElementAtIndex(
                                    i)
                                .stringValue,
                            id,
                            StringComparison.Ordinal))
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                {
                    return id;
                }
            }

            return null;
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

            if (!cleanup &&
                kind ==
                    AppearanceTransitionStepKind.Action)
            {
                step.FindPropertyRelative(
                        "StepId")
                    .stringValue =
                        BuildUniqueActionStepId(
                            steps,
                            "action",
                            index);
            }

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
                    "StepId")
                .stringValue =
                    string.Empty;
            step.FindPropertyRelative(
                    "DependencyMode")
                .enumValueIndex =
                    (int)
                    AppearanceTransitionDependencyMode
                        .None;
            step.FindPropertyRelative(
                    "DependsOnStepIds")
                .arraySize = 0;
            step.FindPropertyRelative(
                    "DependencyTimeoutSeconds")
                .floatValue = 5f;
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
                        "StepId")
                    .stringValue =
                        from.FindPropertyRelative(
                                "StepId")
                            .stringValue;
                to.FindPropertyRelative(
                        "DependencyMode")
                    .enumValueIndex =
                        from.FindPropertyRelative(
                                "DependencyMode")
                            .enumValueIndex;
                CopyStringArray(
                    from.FindPropertyRelative(
                        "DependsOnStepIds"),
                    to.FindPropertyRelative(
                        "DependsOnStepIds"));
                to.FindPropertyRelative(
                        "DependencyTimeoutSeconds")
                    .floatValue =
                        from.FindPropertyRelative(
                                "DependencyTimeoutSeconds")
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

        private static void CopyStringArray(
            SerializedProperty source,
            SerializedProperty destination)
        {
            destination.arraySize =
                source.arraySize;

            for (var i = 0;
                 i < source.arraySize;
                 i++)
            {
                destination
                    .GetArrayElementAtIndex(
                        i)
                    .stringValue =
                        source
                            .GetArrayElementAtIndex(
                                i)
                            .stringValue;
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

        private static bool HasBlockingStep(
            SerializedProperty steps)
        {
            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);

                if ((AppearanceTransitionStepKind)
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex ==
                    AppearanceTransitionStepKind
                        .Action &&
                    step.FindPropertyRelative(
                            "Blocking")
                        .boolValue)
                {
                    return true;
                }
            }

            return false;
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

        private void GenerateMissingStepIds(
            SerializedProperty transition)
        {
            var steps =
                transition.FindPropertyRelative(
                    "Steps");
            var changed = 0;

            Undo.RecordObject(
                _runtime,
                "Generate Transition Step IDs");

            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                var step =
                    steps.GetArrayElementAtIndex(
                        i);

                if ((AppearanceTransitionStepKind)
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex !=
                    AppearanceTransitionStepKind.Action)
                {
                    continue;
                }

                var id =
                    step.FindPropertyRelative(
                        "StepId");

                if (!string.IsNullOrWhiteSpace(
                        id.stringValue))
                {
                    continue;
                }

                var actionType =
                    step.FindPropertyRelative(
                            "ActionType")
                        .stringValue;
                var text =
                    step.FindPropertyRelative(
                            "Text")
                        .stringValue;
                var preferred =
                    ShortActionLabel(
                        actionType);

                if (!string.IsNullOrWhiteSpace(
                        text))
                {
                    preferred +=
                        "-" +
                        SanitizeLogicalId(
                            text);
                }

                id.stringValue =
                    BuildUniqueActionStepId(
                        steps,
                        preferred,
                        i);
                changed++;
            }

            _serializedRuntime
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _runtime);

            _lastMessage =
                changed > 0
                    ? $"Generated {changed} missing action Step ID(s)."
                    : "All action steps already have Step IDs.";
            _lastMessageType =
                MessageType.Info;
        }

        private static string SanitizeLogicalId(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "action";
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
                    chars[i] = '-';
                }
            }

            return new string(
                chars);
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
            SetStepId(
                steps.GetArrayElementAtIndex(
                    0),
                "spin-start");

            ConfigureActionStep(
                steps.GetArrayElementAtIndex(
                    1),
                0.10f,
                EventActionTypes.EffectPlay,
                "effects.main",
                "confetti",
                required:
                    false);
            SetStepId(
                steps.GetArrayElementAtIndex(
                    1),
                "confetti-start");

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
            SetStepId(
                steps.GetArrayElementAtIndex(
                    3),
                "sparkle-start");

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
            SetStepId(
                steps.GetArrayElementAtIndex(
                    4),
                "spin-release");

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

        private static void SetStepId(
            SerializedProperty step,
            string stepId)
        {
            step.FindPropertyRelative(
                    "StepId")
                .stringValue =
                    stepId ??
                    string.Empty;
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

            ImportPackage(
                package,
                path);
        }

        private void ImportPackage(
            AppearanceTransitionPackage package,
            string sourceLabel)
        {
            if (!P11AppearanceTransitionPackageUtility
                .Validate(
                    package,
                    out var error))
            {
                _lastMessage =
                    "Transition import failed: " +
                    (error ??
                     "package validation failed.");
                _lastMessageType =
                    MessageType.Error;
                return;
            }

            if (_runtime == null ||
                _serializedRuntime == null ||
                _transitions == null)
            {
                _pendingPackage =
                    package;
                _pendingPackageSource =
                    sourceLabel;
                _lastMessage =
                    "Select a BasicCharacterAppearanceRuntime, then import the pending package.";
                _lastMessageType =
                    MessageType.Info;
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
                $"Imported {package.Transitions.Length} transition(s) from package '{package.PackageId}'" +
                (string.IsNullOrWhiteSpace(
                    sourceLabel)
                    ? "."
                    : $" ({sourceLabel}).");
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
                Markers =
                    CaptureMarkers(
                        transition.FindPropertyRelative(
                            "Markers")),
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

        private static AppearanceTransitionMarker[]
            CaptureMarkers(
                SerializedProperty markers)
        {
            var result =
                new AppearanceTransitionMarker[
                    markers.arraySize];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                var marker =
                    markers.GetArrayElementAtIndex(
                        i);

                result[i] =
                    new AppearanceTransitionMarker
                    {
                        Name =
                            marker.FindPropertyRelative(
                                    "Name")
                                .stringValue,
                        TimeSeconds =
                            marker.FindPropertyRelative(
                                    "TimeSeconds")
                                .floatValue
                    };
            }

            return result;
        }

        private static string[] CaptureStringArray(
            SerializedProperty array)
        {
            var result =
                new string[
                    array?.arraySize ?? 0];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                result[i] =
                    array.GetArrayElementAtIndex(
                            i)
                        .stringValue;
            }

            return result;
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
                        TimingMode =
                            (AppearanceTransitionTimingMode)
                            step.FindPropertyRelative(
                                    "TimingMode")
                                .enumValueIndex,
                        MarkerName =
                            step.FindPropertyRelative(
                                    "MarkerName")
                                .stringValue,
                        MarkerOffsetSeconds =
                            step.FindPropertyRelative(
                                    "MarkerOffsetSeconds")
                                .floatValue,
                        StepId =
                            step.FindPropertyRelative(
                                    "StepId")
                                .stringValue,
                        DependencyMode =
                            (AppearanceTransitionDependencyMode)
                            step.FindPropertyRelative(
                                    "DependencyMode")
                                .enumValueIndex,
                        DependsOnStepIds =
                            CaptureStringArray(
                                step.FindPropertyRelative(
                                    "DependsOnStepIds")),
                        DependencyTimeoutSeconds =
                            step.FindPropertyRelative(
                                    "DependencyTimeoutSeconds")
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
                                .boolValue,
                        Blocking =
                            step.FindPropertyRelative(
                                    "Blocking")
                                .boolValue,
                        CompletionTimeoutSeconds =
                            step.FindPropertyRelative(
                                    "CompletionTimeoutSeconds")
                                .floatValue
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

            WriteMarkers(
                destination.FindPropertyRelative(
                    "Markers"),
                source?.Markers);

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
                        "TimingMode")
                    .enumValueIndex =
                        (int)step.TimingMode;
                property.FindPropertyRelative(
                        "MarkerName")
                    .stringValue =
                        step.MarkerName ??
                        string.Empty;
                property.FindPropertyRelative(
                        "MarkerOffsetSeconds")
                    .floatValue =
                        (float)step.MarkerOffsetSeconds;
                property.FindPropertyRelative(
                        "StepId")
                    .stringValue =
                        step.StepId ??
                        string.Empty;
                property.FindPropertyRelative(
                        "DependencyMode")
                    .enumValueIndex =
                        (int)step.DependencyMode;
                WriteStringArray(
                    property.FindPropertyRelative(
                        "DependsOnStepIds"),
                    step.DependsOnStepIds);
                property.FindPropertyRelative(
                        "DependencyTimeoutSeconds")
                    .floatValue =
                        (float)step.DependencyTimeoutSeconds;
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
                property.FindPropertyRelative(
                        "Blocking")
                    .boolValue =
                        step.Blocking;
                property.FindPropertyRelative(
                        "CompletionTimeoutSeconds")
                    .floatValue =
                        (float)step.CompletionTimeoutSeconds;
            }
        }

        private static void WriteStringArray(
            SerializedProperty destination,
            string[] source)
        {
            source ??=
                Array.Empty<string>();
            destination.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                destination
                    .GetArrayElementAtIndex(
                        i)
                    .stringValue =
                        source[i] ??
                        string.Empty;
            }
        }

        private static void WriteMarkers(
            SerializedProperty destination,
            AppearanceTransitionMarker[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceTransitionMarker>();

            destination.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var marker =
                    source[i] ??
                    new AppearanceTransitionMarker();
                var property =
                    destination.GetArrayElementAtIndex(
                        i);

                property.FindPropertyRelative(
                        "Name")
                    .stringValue =
                        marker.Name ??
                        string.Empty;
                property.FindPropertyRelative(
                        "TimeSeconds")
                    .floatValue =
                        (float)marker.TimeSeconds;
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

        private static string BuildUniqueActionStepId(
            SerializedProperty steps,
            string preferred,
            int ignoreIndex)
        {
            var baseId =
                string.IsNullOrWhiteSpace(
                    preferred)
                    ? "action"
                    : preferred.Trim();
            var candidate =
                baseId;
            var suffix = 2;

            while (ContainsActionStepId(
                       steps,
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

        private static bool ContainsActionStepId(
            SerializedProperty steps,
            string candidate,
            int ignoreIndex)
        {
            for (var i = 0;
                 i < steps.arraySize;
                 i++)
            {
                if (i == ignoreIndex)
                {
                    continue;
                }

                var step =
                    steps.GetArrayElementAtIndex(
                        i);

                if ((AppearanceTransitionStepKind)
                        step.FindPropertyRelative(
                                "Kind")
                            .enumValueIndex ==
                        AppearanceTransitionStepKind.Action &&
                    string.Equals(
                        step.FindPropertyRelative(
                                "StepId")
                            .stringValue,
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] BuildMarkerLabels(
            SerializedProperty markers)
        {
            var labels =
                new string[
                    markers?.arraySize ?? 0];

            for (var i = 0;
                 i < labels.Length;
                 i++)
            {
                labels[i] =
                    markers
                        .GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            "Name")
                        .stringValue;
            }

            return labels;
        }

        private static string BuildUniqueMarkerName(
            SerializedProperty markers,
            string preferred,
            int ignoreIndex)
        {
            var baseName =
                string.IsNullOrWhiteSpace(
                    preferred)
                    ? "marker"
                    : preferred.Trim();
            var candidate =
                baseName;
            var suffix = 2;

            while (ContainsMarkerName(
                       markers,
                       candidate,
                       ignoreIndex))
            {
                candidate =
                    baseName +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private static bool ContainsMarkerName(
            SerializedProperty markers,
            string candidate,
            int ignoreIndex)
        {
            for (var i = 0;
                 i < markers.arraySize;
                 i++)
            {
                if (i == ignoreIndex)
                {
                    continue;
                }

                if (string.Equals(
                        markers
                            .GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "Name")
                            .stringValue,
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
