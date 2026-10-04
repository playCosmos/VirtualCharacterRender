using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Editor.P12
{
    public sealed class P12EventNodeEditorWindow :
        EditorWindow
    {
        private enum GraphStage
        {
            Filter = 0,
            Conditions = 1,
            Mutations = 2,
            Actions = 3
        }

        private EventRuntimeHost _host;
        private SerializedObject _serializedHost;
        private SerializedProperty _rules;
        private int _ruleIndex;
        private GraphStage _selectedStage =
            GraphStage.Filter;
        private int _selectedItemIndex = -1;
        private bool _hasPendingChanges;
        private string _lastValidJson;
        private string _message;
        private MessageType _messageType =
            MessageType.Info;
        private Vector2 _scroll;
        private P12EventRuleLibraryPackage
            _pendingLibraryPackage;
        private string _pendingLibrarySource;
        private bool _showLibraryExportMetadata;
        private string _libraryExportDescription;
        private string _libraryExportTags;
        private int _libraryExportRevision = 1;

        [MenuItem("VCR/P12/Open Event Node Editor")]
        public static void Open()
        {
            GetWindow<
                    P12EventNodeEditorWindow>(
                    "VCR Event Nodes")
                .Show();
        }

        public static void OpenWithHost(
            EventRuntimeHost host)
        {
            var window =
                GetWindow<
                    P12EventNodeEditorWindow>(
                    "VCR Event Nodes");
            window._host =
                host;
            window.Rebind();
            window.Show();
            window.Repaint();
        }

        public static void OpenWithLibraryPackage(
            P12EventRuleLibraryPackage package,
            string sourceLabel)
        {
            var window =
                GetWindow<
                    P12EventNodeEditorWindow>(
                    "VCR Event Nodes");
            window._pendingLibraryPackage =
                package;
            window._pendingLibrarySource =
                sourceLabel;
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            ResolveFromSelection();
            Rebind();
        }

        private void OnSelectionChange()
        {
            var selected =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject
                        .GetComponentInParent<
                            EventRuntimeHost>()
                    : null;

            if (selected != null &&
                !ReferenceEquals(
                    selected,
                    _host))
            {
                _host =
                    selected;
                Rebind();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Event Rule Node Editor",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "P12 edits the existing EventRuntimeRule pipeline. The graph is an authoring/view layer only: Filter → Conditions (AND) → State Mutations → Actions. Runtime execution remains in EventRuntimeEngine.",
                MessageType.Info);

            var nextHost =
                (EventRuntimeHost)
                EditorGUILayout.ObjectField(
                    "Event Runtime",
                    _host,
                    typeof(EventRuntimeHost),
                    true);

            if (!ReferenceEquals(
                    nextHost,
                    _host))
            {
                _host =
                    nextHost;
                Rebind();
            }

            if (_host == null ||
                _serializedHost == null ||
                _rules == null)
            {
                EditorGUILayout.HelpBox(
                    "Select an EventRuntimeHost to author event rules.",
                    MessageType.Info);
                return;
            }

            _serializedHost.Update();

            if (!string.IsNullOrWhiteSpace(
                    _message))
            {
                EditorGUILayout.HelpBox(
                    _message,
                    _messageType);
            }

            if (_hasPendingChanges)
            {
                EditorGUILayout.HelpBox(
                    "Event rules changed since the last successful validation. Validate & Apply before treating the graph as runtime-ready.",
                    MessageType.Warning);
            }

            DrawPendingLibraryPackage();
            DrawLibraryExportMetadata();
            DrawRuleToolbar();

            if (_rules.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No event rules are authored. Add a rule to begin.",
                    MessageType.None);
                ApplyModifiedProperties();
                return;
            }

            _ruleIndex =
                Mathf.Clamp(
                    _ruleIndex,
                    0,
                    _rules.arraySize - 1);

            var rule =
                _rules.GetArrayElementAtIndex(
                    _ruleIndex);

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            DrawRuleSettings(
                rule);
            DrawGraph(
                rule);
            DrawSelectedNodeInspector(
                rule);

            EditorGUILayout.EndScrollView();
            ApplyModifiedProperties();
        }

        private void DrawPendingLibraryPackage()
        {
            if (_pendingLibraryPackage == null)
            {
                return;
            }

            var source =
                string.IsNullOrWhiteSpace(
                    _pendingLibrarySource)
                    ? "<library>"
                    : _pendingLibrarySource;
            var count =
                _pendingLibraryPackage.Rules?.Length ??
                0;

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Pending Event Rule Library",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Package",
                    _pendingLibraryPackage.PackageId ??
                    "<missing>");
                EditorGUILayout.LabelField(
                    "Source",
                    source);
                EditorGUILayout.LabelField(
                    "Rules",
                    count.ToString());

                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    if (GUILayout.Button(
                            "Import Pending Package"))
                    {
                        ImportPackage(
                            _pendingLibraryPackage,
                            source);
                    }

                    if (GUILayout.Button(
                            "Dismiss"))
                    {
                        _pendingLibraryPackage =
                            null;
                        _pendingLibrarySource =
                            null;
                    }
                }
            }
        }

        private void DrawLibraryExportMetadata()
        {
            _showLibraryExportMetadata =
                EditorGUILayout.Foldout(
                    _showLibraryExportMetadata,
                    "Rule Library Export Metadata",
                    toggleOnLabelClick:
                        true);

            if (!_showLibraryExportMetadata)
            {
                return;
            }

            using (new EditorGUILayout
                       .VerticalScope(
                           EditorStyles.helpBox))
            {
                _libraryExportDescription =
                    EditorGUILayout.TextField(
                        "Description",
                        _libraryExportDescription ??
                        string.Empty);
                _libraryExportTags =
                    EditorGUILayout.TextField(
                        "Tags (comma-separated)",
                        _libraryExportTags ??
                        string.Empty);
                _libraryExportRevision =
                    Mathf.Max(
                        1,
                        EditorGUILayout.IntField(
                            "Revision",
                            _libraryExportRevision));

                EditorGUILayout.HelpBox(
                    "Description, tags and revision are package-library metadata only. They do not change EventRuntime execution semantics.",
                    MessageType.None);
            }
        }

        private void DrawRuleToolbar()
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           _rules.arraySize <= 1))
                {
                    if (GUILayout.Button(
                            "Prev Rule"))
                    {
                        SelectRule(
                            -1);
                    }

                    if (GUILayout.Button(
                            "Next Rule"))
                    {
                        SelectRule(
                            1);
                    }
                }

                if (GUILayout.Button(
                        "Add Rule"))
                {
                    AddRule();
                }

                if (GUILayout.Button(
                        "Add Template"))
                {
                    ShowTemplateMenu();
                }

                using (new EditorGUI.DisabledScope(
                           _rules.arraySize == 0))
                {
                    if (GUILayout.Button(
                            "Duplicate"))
                    {
                        DuplicateRule();
                    }

                    if (GUILayout.Button(
                            "Delete"))
                    {
                        DeleteRule();
                    }

                    if (GUILayout.Button(
                            "Export Rule"))
                    {
                        ExportSelectedRule();
                    }
                }

                if (GUILayout.Button(
                        "Export All"))
                {
                    ExportAllRules();
                }

                if (GUILayout.Button(
                        "Import Library"))
                {
                    ImportRuleLibrary();
                }

                if (GUILayout.Button(
                        "Project Library"))
                {
                    P12EventRuleLibraryWindow
                        .Open();
                }

                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateAndApply();
                }
            }
        }

        private void DrawRuleSettings(
            SerializedProperty rule)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Rule {_ruleIndex + 1}/{_rules.arraySize}",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                rule.FindPropertyRelative(
                    "Id"));
            EditorGUILayout.PropertyField(
                rule.FindPropertyRelative(
                    "Enabled"));

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                EditorGUILayout.PropertyField(
                    rule.FindPropertyRelative(
                        "CooldownSeconds"),
                    new GUIContent(
                        "Cooldown (s)"));
                EditorGUILayout.PropertyField(
                    rule.FindPropertyRelative(
                        "StopAfterMatch"));
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                EditorGUILayout.PropertyField(
                    rule.FindPropertyRelative(
                        "RateLimitWindowSeconds"),
                    new GUIContent(
                        "Rate Window (s)"));
                EditorGUILayout.PropertyField(
                    rule.FindPropertyRelative(
                        "RateLimitMaxExecutions"),
                    new GUIContent(
                        "Rate Max"));
            }
        }

        private void DrawGraph(
            SerializedProperty rule)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Rule Graph",
                EditorStyles.boldLabel);

            var rect =
                GUILayoutUtility.GetRect(
                    600f,
                    270f,
                    GUILayout.ExpandWidth(
                        true));
            GUI.Box(
                rect,
                GUIContent.none,
                EditorStyles.helpBox);

            var inner =
                new Rect(
                    rect.x + 12f,
                    rect.y + 12f,
                    Mathf.Max(
                        100f,
                        rect.width - 24f),
                    rect.height - 24f);
            const float gap = 12f;
            var width =
                Mathf.Max(
                    120f,
                    (inner.width -
                     gap * 3f) /
                    4f);

            var filterRect =
                new Rect(
                    inner.x,
                    inner.y,
                    width,
                    inner.height);
            var conditionsRect =
                new Rect(
                    filterRect.xMax +
                    gap,
                    inner.y,
                    width,
                    inner.height);
            var mutationsRect =
                new Rect(
                    conditionsRect.xMax +
                    gap,
                    inner.y,
                    width,
                    inner.height);
            var actionsRect =
                new Rect(
                    mutationsRect.xMax +
                    gap,
                    inner.y,
                    width,
                    inner.height);

            DrawStageConnection(
                filterRect,
                conditionsRect);
            DrawStageConnection(
                conditionsRect,
                mutationsRect);
            DrawStageConnection(
                mutationsRect,
                actionsRect);

            DrawFilterNode(
                rule,
                filterRect);
            DrawArrayStage(
                rule.FindPropertyRelative(
                    "Conditions"),
                GraphStage.Conditions,
                "Conditions (AND)",
                conditionsRect,
                ConditionLabel);
            DrawArrayStage(
                rule.FindPropertyRelative(
                    "StateMutations"),
                GraphStage.Mutations,
                "State Mutations",
                mutationsRect,
                MutationLabel);
            DrawArrayStage(
                rule.FindPropertyRelative(
                    "Actions"),
                GraphStage.Actions,
                "Actions",
                actionsRect,
                ActionLabel);
        }

        private void DrawFilterNode(
            SerializedProperty rule,
            Rect rect)
        {
            var selected =
                _selectedStage ==
                    GraphStage.Filter;
            GUI.Box(
                rect,
                GUIContent.none,
                selected
                    ? EditorStyles.helpBox
                    : GUI.skin.box);

            var header =
                new Rect(
                    rect.x + 6f,
                    rect.y + 6f,
                    rect.width - 12f,
                    26f);

            if (GUI.Button(
                    header,
                    "Event / Filter"))
            {
                _selectedStage =
                    GraphStage.Filter;
                _selectedItemIndex =
                    -1;
            }

            var filter =
                rule.FindPropertyRelative(
                    "Filter");
            var type =
                filter?.FindPropertyRelative(
                        "Type")
                    .stringValue;
            var source =
                filter?.FindPropertyRelative(
                        "SourceId")
                    .stringValue;

            GUI.Label(
                new Rect(
                    rect.x + 10f,
                    header.yMax + 10f,
                    rect.width - 20f,
                    60f),
                $"Type: {(string.IsNullOrWhiteSpace(type) ? "*" : type)}\nSource: {(string.IsNullOrWhiteSpace(source) ? "*" : source)}\n→ match",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawArrayStage(
            SerializedProperty array,
            GraphStage stage,
            string title,
            Rect rect,
            Func<SerializedProperty, int, string>
                labelFactory)
        {
            var selected =
                _selectedStage ==
                    stage;
            GUI.Box(
                rect,
                GUIContent.none,
                selected
                    ? EditorStyles.helpBox
                    : GUI.skin.box);

            var header =
                new Rect(
                    rect.x + 6f,
                    rect.y + 6f,
                    rect.width - 12f,
                    26f);

            if (GUI.Button(
                    header,
                    $"{title} ({array.arraySize})"))
            {
                _selectedStage =
                    stage;
                _selectedItemIndex =
                    -1;
            }

            var y =
                header.yMax +
                7f;
            var visible =
                Math.Min(
                    array.arraySize,
                    7);

            for (var i = 0;
                 i < visible;
                 i++)
            {
                var item =
                    array.GetArrayElementAtIndex(
                        i);
                var itemRect =
                    new Rect(
                        rect.x + 8f,
                        y,
                        rect.width - 16f,
                        24f);
                var itemSelected =
                    selected &&
                    _selectedItemIndex ==
                        i;
                var style =
                    itemSelected
                        ? EditorStyles.miniButtonMid
                        : EditorStyles.miniButton;

                if (GUI.Button(
                        itemRect,
                        labelFactory(
                            item,
                            i),
                        style))
                {
                    _selectedStage =
                        stage;
                    _selectedItemIndex =
                        i;
                }

                y +=
                    28f;
            }

            if (array.arraySize >
                visible)
            {
                GUI.Label(
                    new Rect(
                        rect.x + 10f,
                        y,
                        rect.width - 20f,
                        20f),
                    $"+ {array.arraySize - visible} more",
                    EditorStyles.miniLabel);
            }

            if (array.arraySize == 0)
            {
                GUI.Label(
                    new Rect(
                        rect.x + 10f,
                        y,
                        rect.width - 20f,
                        40f),
                    "<empty>\nSelect stage to add.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawStageConnection(
            Rect from,
            Rect to)
        {
            Handles.BeginGUI();
            var start =
                new Vector3(
                    from.xMax,
                    from.center.y,
                    0f);
            var end =
                new Vector3(
                    to.x,
                    to.center.y,
                    0f);
            var tangent =
                Mathf.Max(
                    24f,
                    (end.x -
                     start.x) *
                    0.45f);

            Handles.DrawBezier(
                start,
                end,
                start +
                Vector3.right *
                tangent,
                end +
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
            Handles.EndGUI();
        }

        private void DrawSelectedNodeInspector(
            SerializedProperty rule)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Selected Node",
                EditorStyles.boldLabel);

            switch (_selectedStage)
            {
                case GraphStage.Filter:
                    EditorGUILayout.PropertyField(
                        rule.FindPropertyRelative(
                            "Filter"),
                        includeChildren:
                            true);
                    break;

                case GraphStage.Conditions:
                    DrawArrayInspector(
                        rule.FindPropertyRelative(
                            "Conditions"),
                        GraphStage.Conditions,
                        "Condition");
                    break;

                case GraphStage.Mutations:
                    DrawArrayInspector(
                        rule.FindPropertyRelative(
                            "StateMutations"),
                        GraphStage.Mutations,
                        "Mutation");
                    break;

                case GraphStage.Actions:
                    DrawArrayInspector(
                        rule.FindPropertyRelative(
                            "Actions"),
                        GraphStage.Actions,
                        "Action");
                    break;
            }
        }

        private void DrawArrayInspector(
            SerializedProperty array,
            GraphStage stage,
            string label)
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add " +
                        label))
                {
                    AddStageItem(
                        array,
                        stage);
                }

                using (new EditorGUI.DisabledScope(
                           _selectedItemIndex < 0 ||
                           _selectedItemIndex >=
                               array.arraySize))
                {
                    if (GUILayout.Button(
                            "Move Up"))
                    {
                        MoveSelectedItem(
                            array,
                            -1);
                    }

                    if (GUILayout.Button(
                            "Move Down"))
                    {
                        MoveSelectedItem(
                            array,
                            1);
                    }

                    if (GUILayout.Button(
                            "Delete"))
                    {
                        DeleteSelectedItem(
                            array);
                    }
                }
            }

            if (_selectedItemIndex < 0 ||
                _selectedItemIndex >=
                    array.arraySize)
            {
                EditorGUILayout.HelpBox(
                    $"Select a {label.ToLowerInvariant()} node, or add one.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.PropertyField(
                array.GetArrayElementAtIndex(
                    _selectedItemIndex),
                new GUIContent(
                    $"{label} {_selectedItemIndex + 1}"),
                includeChildren:
                    true);
        }

        private void SelectRule(
            int offset)
        {
            if (_rules.arraySize == 0)
            {
                return;
            }

            _ruleIndex =
                (_ruleIndex +
                 offset +
                 _rules.arraySize) %
                _rules.arraySize;
            _selectedStage =
                GraphStage.Filter;
            _selectedItemIndex =
                -1;
        }

        private void ShowTemplateMenu()
        {
            var menu =
                new GenericMenu();

            AddTemplateMenuItem(
                menu,
                "Manual / Restore Default Appearance",
                P12BuiltInEventRuleTemplate
                    .ManualRestoreDefault);
            AddTemplateMenuItem(
                menu,
                "Tracking / Subject Lost → Restore Default",
                P12BuiltInEventRuleTemplate
                    .SubjectLostRestoreDefault);
            AddTemplateMenuItem(
                menu,
                "Broadcast / Chat → Appearance Preset",
                P12BuiltInEventRuleTemplate
                    .ChatAppearancePreset);
            AddTemplateMenuItem(
                menu,
                "Broadcast / Donation → Effect",
                P12BuiltInEventRuleTemplate
                    .DonationEffect);

            menu.ShowAsContext();
        }

        private void AddTemplateMenuItem(
            GenericMenu menu,
            string label,
            P12BuiltInEventRuleTemplate template)
        {
            menu.AddItem(
                new GUIContent(
                    label),
                false,
                () =>
                    AddTemplate(
                        template));
        }

        private void AddTemplate(
            P12BuiltInEventRuleTemplate template)
        {
            if (!TryPrepareLibraryOperation(
                    out var existing))
            {
                return;
            }

            var authored =
                P12EventRuleLibraryUtility
                    .CreateTemplate(
                        template);
            var merged =
                P12EventRuleLibraryUtility
                    .MergeRules(
                        existing,
                        new[]
                        {
                            authored
                        });

            if (!P12EventRuleAuthoringUtility
                .TryValidateRules(
                    merged,
                    out var error))
            {
                _message =
                    "Built-in event template could not be added: " +
                    error;
                _messageType =
                    MessageType.Error;
                return;
            }

            Undo.RecordObject(
                _host,
                "Add Event Rule Template");
            _host.SetRules(
                merged);
            EditorUtility.SetDirty(
                _host);
            _ruleIndex =
                Math.Max(
                    0,
                    merged.Length - 1);
            Rebind();

            _message =
                $"Added event rule template '{merged[_ruleIndex].Id}'. Edit placeholder ids/text as needed, then Validate & Apply.";
            _messageType =
                MessageType.Info;
        }

        private void ExportSelectedRule()
        {
            if (!TryPrepareLibraryOperation(
                    out var rules) ||
                rules.Length == 0)
            {
                return;
            }

            var index =
                Mathf.Clamp(
                    _ruleIndex,
                    0,
                    rules.Length - 1);
            var rule =
                rules[index];

            ExportRulePackage(
                rule?.Id ??
                "event-rule",
                new[]
                {
                    rule
                });
        }

        private void ExportAllRules()
        {
            if (!TryPrepareLibraryOperation(
                    out var rules))
            {
                return;
            }

            ExportRulePackage(
                _host != null &&
                !string.IsNullOrWhiteSpace(
                    _host.name)
                    ? _host.name +
                      "-event-rules"
                    : "event-rules",
                rules);
        }

        private void ExportRulePackage(
            string packageId,
            EventRuntimeRule[] rules)
        {
            if (!P12EventRuleLibraryUtility
                .TryCreatePackage(
                    packageId,
                    _libraryExportDescription,
                    ParseExportTags(
                        _libraryExportTags),
                    _libraryExportRevision,
                    rules,
                    out var package,
                    out var error) ||
                !P12EventRuleLibraryUtility
                .TrySerialize(
                    package,
                    out var json,
                    out error))
            {
                _message =
                    "Event rule library export failed: " +
                    (error ?? "unknown error");
                _messageType =
                    MessageType.Error;
                return;
            }

            var safeName =
                SanitizeFileName(
                    package.PackageId);
            var path =
                EditorUtility.SaveFilePanel(
                    "Export Event Rule Library",
                    Application.dataPath,
                    safeName +
                    ".vcrevents",
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
                _message =
                    $"Exported {package.Rules.Length} event rule(s): {path}";
                _messageType =
                    MessageType.Info;
            }
            catch (Exception exception)
            {
                _message =
                    "Event rule library file write failed: " +
                    exception.Message;
                _messageType =
                    MessageType.Error;
            }
        }

        private void ImportRuleLibrary()
        {
            if (!TryPrepareLibraryOperation(
                    out var existing))
            {
                return;
            }

            var path =
                EditorUtility.OpenFilePanel(
                    "Import Event Rule Library",
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
                _message =
                    "Event rule library file read failed: " +
                    exception.Message;
                _messageType =
                    MessageType.Error;
                return;
            }

            if (!P12EventRuleLibraryUtility
                .TryParse(
                    json,
                    out var package,
                    out var error))
            {
                _message =
                    "Event rule library import rejected: " +
                    error;
                _messageType =
                    MessageType.Error;
                return;
            }

            ImportPackage(
                package,
                path);
        }

        private void ImportPackage(
            P12EventRuleLibraryPackage package,
            string sourceLabel)
        {
            if (!P12EventRuleLibraryUtility
                .TryValidatePackage(
                    package,
                    out var error))
            {
                _message =
                    "Event rule library import rejected: " +
                    (error ?? "unknown error");
                _messageType =
                    MessageType.Error;
                return;
            }

            if (!TryPrepareLibraryOperation(
                    out var existing))
            {
                return;
            }

            var merged =
                P12EventRuleLibraryUtility
                    .MergeRules(
                        existing,
                        package.Rules);

            if (!P12EventRuleAuthoringUtility
                .TryValidateRules(
                    merged,
                    out error))
            {
                _message =
                    "Merged event rule library is invalid: " +
                    error;
                _messageType =
                    MessageType.Error;
                return;
            }

            Undo.RecordObject(
                _host,
                "Import Event Rule Library");
            _host.SetRules(
                merged);
            EditorUtility.SetDirty(
                _host);
            _ruleIndex =
                existing.Length <
                merged.Length
                    ? existing.Length
                    : Math.Max(
                        0,
                        merged.Length - 1);
            Rebind();

            _pendingLibraryPackage =
                null;
            _pendingLibrarySource =
                null;
            _message =
                $"Imported event rule library '{package.PackageId}': {package.Rules.Length} rule(s), {merged.Length} total. ID collisions were suffixed deterministically." +
                (string.IsNullOrWhiteSpace(
                    sourceLabel)
                    ? string.Empty
                    : $" Source: {sourceLabel}");
            _messageType =
                MessageType.Info;
        }

        private bool TryPrepareLibraryOperation(
            out EventRuntimeRule[] rules)
        {
            rules =
                Array.Empty<
                    EventRuntimeRule>();

            if (_host == null ||
                _serializedHost == null)
            {
                _message =
                    "Event Runtime host is unavailable.";
                _messageType =
                    MessageType.Error;
                return false;
            }

            _serializedHost
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _host);

            rules =
                _host.CaptureRules();

            if (!P12EventRuleAuthoringUtility
                .TryValidateRules(
                    rules,
                    out var error))
            {
                _message =
                    "Validate current event graph before template/library operations: " +
                    error;
                _messageType =
                    MessageType.Error;
                return false;
            }

            _host.SetRules(
                rules);
            _lastValidJson =
                EditorJsonUtility.ToJson(
                    _host,
                    prettyPrint:
                        false);
            _hasPendingChanges =
                false;
            RebindSerializedOnly();
            return true;
        }

        private static string[] ParseExportTags(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return Array.Empty<string>();
            }

            return value.Split(
                new[]
                {
                    ','
                },
                StringSplitOptions
                    .RemoveEmptyEntries);
        }

        private static string SanitizeFileName(
            string value)
        {
            value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "event-rules"
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

        private void AddRule()
        {
            Undo.RecordObject(
                _host,
                "Add Event Rule");
            var index =
                _rules.arraySize;
            _rules.arraySize =
                index + 1;
            var rule =
                _rules.GetArrayElementAtIndex(
                    index);
            ResetRule(
                rule,
                P12EventRuleAuthoringUtility
                    .BuildUniqueRuleId(
                        "event-rule",
                        RuleIdExists));
            _ruleIndex =
                index;
            _selectedStage =
                GraphStage.Filter;
            _selectedItemIndex =
                -1;
        }

        private void DuplicateRule()
        {
            if (_rules.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(
                _host,
                "Duplicate Event Rule");
            var sourceIndex =
                Mathf.Clamp(
                    _ruleIndex,
                    0,
                    _rules.arraySize - 1);
            _rules.InsertArrayElementAtIndex(
                sourceIndex);
            var duplicateIndex =
                sourceIndex +
                1;
            var duplicate =
                _rules.GetArrayElementAtIndex(
                    duplicateIndex);
            var sourceId =
                duplicate.FindPropertyRelative(
                        "Id")
                    .stringValue;
            duplicate.FindPropertyRelative(
                    "Id")
                .stringValue =
                    P12EventRuleAuthoringUtility
                        .BuildUniqueRuleId(
                            string.IsNullOrWhiteSpace(
                                sourceId)
                                ? "event-rule-copy"
                                : sourceId +
                                  "-copy",
                            RuleIdExists);
            _ruleIndex =
                duplicateIndex;
            _selectedStage =
                GraphStage.Filter;
            _selectedItemIndex =
                -1;
        }

        private void DeleteRule()
        {
            if (_rules.arraySize == 0)
            {
                return;
            }

            Undo.RecordObject(
                _host,
                "Delete Event Rule");
            _rules.DeleteArrayElementAtIndex(
                _ruleIndex);
            _ruleIndex =
                Mathf.Clamp(
                    _ruleIndex,
                    0,
                    Math.Max(
                        0,
                        _rules.arraySize - 1));
            _selectedStage =
                GraphStage.Filter;
            _selectedItemIndex =
                -1;
        }

        private void AddStageItem(
            SerializedProperty array,
            GraphStage stage)
        {
            Undo.RecordObject(
                _host,
                "Add Event Graph Node");
            var index =
                array.arraySize;
            array.arraySize =
                index + 1;
            var item =
                array.GetArrayElementAtIndex(
                    index);
            ResetStageItem(
                item,
                stage);
            _selectedStage =
                stage;
            _selectedItemIndex =
                index;
        }

        private void MoveSelectedItem(
            SerializedProperty array,
            int offset)
        {
            var next =
                Mathf.Clamp(
                    _selectedItemIndex +
                    offset,
                    0,
                    array.arraySize - 1);

            if (next ==
                _selectedItemIndex)
            {
                return;
            }

            Undo.RecordObject(
                _host,
                "Reorder Event Graph Node");
            array.MoveArrayElement(
                _selectedItemIndex,
                next);
            _selectedItemIndex =
                next;
        }

        private void DeleteSelectedItem(
            SerializedProperty array)
        {
            if (_selectedItemIndex < 0 ||
                _selectedItemIndex >=
                    array.arraySize)
            {
                return;
            }

            Undo.RecordObject(
                _host,
                "Delete Event Graph Node");
            array.DeleteArrayElementAtIndex(
                _selectedItemIndex);
            _selectedItemIndex =
                Mathf.Clamp(
                    _selectedItemIndex,
                    0,
                    array.arraySize - 1);

            if (array.arraySize == 0)
            {
                _selectedItemIndex =
                    -1;
            }
        }

        private void ValidateAndApply()
        {
            _serializedHost
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _host);

            var rules =
                _host.CaptureRules();

            if (P12EventRuleAuthoringUtility
                .TryValidateRules(
                    rules,
                    out var error))
            {
                _host.SetRules(
                    rules);
                _lastValidJson =
                    EditorJsonUtility.ToJson(
                        _host,
                        prettyPrint:
                            false);
                _hasPendingChanges =
                    false;
                _message =
                    $"Event graph validated and applied: {rules.Length} rule(s).";
                _messageType =
                    MessageType.Info;
                RebindSerializedOnly();
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    _lastValidJson))
            {
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(
                        _lastValidJson,
                        _host);
                    EditorUtility.SetDirty(
                        _host);
                    RebindSerializedOnly();
                    _host.SetRules(
                        _host.CaptureRules());
                    _hasPendingChanges =
                        false;
                    _message =
                        "Event graph validation failed and edits were rolled back to the last valid snapshot: " +
                        error;
                    _messageType =
                        MessageType.Error;
                    return;
                }
                catch (Exception exception)
                {
                    _message =
                        "Event graph validation failed, and rollback also failed: " +
                        error +
                        " / " +
                        exception.Message;
                    _messageType =
                        MessageType.Error;
                    return;
                }
            }

            _message =
                "Event graph validation failed: " +
                error;
            _messageType =
                MessageType.Error;
        }

        private void ApplyModifiedProperties()
        {
            if (_serializedHost != null &&
                _serializedHost.ApplyModifiedProperties())
            {
                _hasPendingChanges =
                    true;
                EditorUtility.SetDirty(
                    _host);
            }
        }

        private bool RuleIdExists(
            string id)
        {
            for (var i = 0;
                 i < _rules.arraySize;
                 i++)
            {
                if (string.Equals(
                        _rules.GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "Id")
                            .stringValue,
                        id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResetRule(
            SerializedProperty rule,
            string id)
        {
            rule.FindPropertyRelative(
                    "Id")
                .stringValue =
                    id;
            rule.FindPropertyRelative(
                    "Enabled")
                .boolValue =
                    true;
            rule.FindPropertyRelative(
                    "CooldownSeconds")
                .doubleValue =
                    0.0;
            rule.FindPropertyRelative(
                    "RateLimitWindowSeconds")
                .doubleValue =
                    0.0;
            rule.FindPropertyRelative(
                    "RateLimitMaxExecutions")
                .intValue =
                    0;
            rule.FindPropertyRelative(
                    "StopAfterMatch")
                .boolValue =
                    false;

            var filter =
                rule.FindPropertyRelative(
                    "Filter");
            filter.FindPropertyRelative(
                    "Type")
                .stringValue =
                    string.Empty;
            filter.FindPropertyRelative(
                    "SourceId")
                .stringValue =
                    string.Empty;
            filter.FindPropertyRelative(
                    "ActorId")
                .stringValue =
                    string.Empty;
            filter.FindPropertyRelative(
                    "TextContains")
                .stringValue =
                    string.Empty;
            filter.FindPropertyRelative(
                    "RequireAmount")
                .boolValue =
                    false;
            filter.FindPropertyRelative(
                    "HasMinimumAmount")
                .boolValue =
                    false;
            filter.FindPropertyRelative(
                    "MinimumAmount")
                .doubleValue =
                    0.0;
            filter.FindPropertyRelative(
                    "HasMaximumAmount")
                .boolValue =
                    false;
            filter.FindPropertyRelative(
                    "MaximumAmount")
                .doubleValue =
                    0.0;

            rule.FindPropertyRelative(
                    "Conditions")
                .arraySize = 0;
            rule.FindPropertyRelative(
                    "StateMutations")
                .arraySize = 0;
            rule.FindPropertyRelative(
                    "Actions")
                .arraySize = 0;
        }

        private static void ResetStageItem(
            SerializedProperty item,
            GraphStage stage)
        {
            switch (stage)
            {
                case GraphStage.Conditions:
                    item.FindPropertyRelative(
                            "Kind")
                        .enumValueIndex =
                            (int)
                            EventStateConditionKind
                                .Exists;
                    item.FindPropertyRelative(
                            "Key")
                        .stringValue =
                            "state.key";
                    item.FindPropertyRelative(
                            "NumberValue")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "TextValue")
                        .stringValue =
                            string.Empty;
                    break;

                case GraphStage.Mutations:
                    item.FindPropertyRelative(
                            "Kind")
                        .enumValueIndex =
                            (int)
                            EventStateMutationKind
                                .SetNumber;
                    item.FindPropertyRelative(
                            "Key")
                        .stringValue =
                            "state.key";
                    item.FindPropertyRelative(
                            "NumericSource")
                        .enumValueIndex =
                            (int)
                            EventNumericValueSource
                                .Constant;
                    item.FindPropertyRelative(
                            "ConstantNumber")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "NumericScale")
                        .doubleValue =
                            1.0;
                    item.FindPropertyRelative(
                            "NumericOffset")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "TextSource")
                        .enumValueIndex =
                            (int)
                            EventTextValueSource
                                .Constant;
                    item.FindPropertyRelative(
                            "ConstantText")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "TextTransforms")
                        .intValue =
                            (int)
                            EventTextTransformFlags
                                .None;
                    item.FindPropertyRelative(
                            "TextPrefix")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "TextSuffix")
                        .stringValue =
                            string.Empty;
                    break;

                case GraphStage.Actions:
                    item.FindPropertyRelative(
                            "ActionType")
                        .stringValue =
                            "expression.set";
                    item.FindPropertyRelative(
                            "TargetId")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "Name")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "HasValue")
                        .boolValue =
                            false;
                    item.FindPropertyRelative(
                            "NumericSource")
                        .enumValueIndex =
                            (int)
                            EventNumericValueSource
                                .Constant;
                    item.FindPropertyRelative(
                            "ConstantNumber")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "ConstantNumberY")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "ConstantNumberZ")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "ConstantNumberW")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "NumericScale")
                        .doubleValue =
                            1.0;
                    item.FindPropertyRelative(
                            "NumericOffset")
                        .doubleValue =
                            0.0;
                    item.FindPropertyRelative(
                            "TextSource")
                        .enumValueIndex =
                            (int)
                            EventTextValueSource
                                .Constant;
                    item.FindPropertyRelative(
                            "ConstantText")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "TextTransforms")
                        .intValue =
                            (int)
                            EventTextTransformFlags
                                .None;
                    item.FindPropertyRelative(
                            "TextPrefix")
                        .stringValue =
                            string.Empty;
                    item.FindPropertyRelative(
                            "TextSuffix")
                        .stringValue =
                            string.Empty;
                    break;
            }
        }

        private static string ConditionLabel(
            SerializedProperty item,
            int index)
        {
            var kind =
                (EventStateConditionKind)
                item.FindPropertyRelative(
                        "Kind")
                    .enumValueIndex;
            var key =
                item.FindPropertyRelative(
                        "Key")
                    .stringValue;

            return
                $"{index + 1}. {kind}\n{key}";
        }

        private static string MutationLabel(
            SerializedProperty item,
            int index)
        {
            var kind =
                (EventStateMutationKind)
                item.FindPropertyRelative(
                        "Kind")
                    .enumValueIndex;
            var key =
                item.FindPropertyRelative(
                        "Key")
                    .stringValue;

            return
                $"{index + 1}. {kind}\n{key}";
        }

        private static string ActionLabel(
            SerializedProperty item,
            int index)
        {
            var type =
                item.FindPropertyRelative(
                        "ActionType")
                    .stringValue;

            return
                $"{index + 1}. {(string.IsNullOrWhiteSpace(type) ? "<action>" : type)}";
        }

        private void ResolveFromSelection()
        {
            if (_host != null)
            {
                return;
            }

            _host =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject
                        .GetComponentInParent<
                            EventRuntimeHost>()
                    : null;
        }

        private void Rebind()
        {
            _message = null;
            _hasPendingChanges =
                false;
            _selectedStage =
                GraphStage.Filter;
            _selectedItemIndex =
                -1;

            if (_host == null)
            {
                _serializedHost =
                    null;
                _rules =
                    null;
                _lastValidJson =
                    null;
                return;
            }

            RebindSerializedOnly();

            if (P12EventRuleAuthoringUtility
                .TryValidateRules(
                    _host.CaptureRules(),
                    out var error))
            {
                _lastValidJson =
                    EditorJsonUtility.ToJson(
                        _host,
                        prettyPrint:
                            false);
            }
            else
            {
                _lastValidJson =
                    null;
                _message =
                    "Current event rule configuration is invalid: " +
                    error;
                _messageType =
                    MessageType.Warning;
            }
        }

        private void RebindSerializedOnly()
        {
            _serializedHost =
                new SerializedObject(
                    _host);
            _rules =
                _serializedHost.FindProperty(
                    "rules");
            _ruleIndex =
                _rules != null &&
                _rules.arraySize > 0
                    ? Mathf.Clamp(
                        _ruleIndex,
                        0,
                        _rules.arraySize - 1)
                    : 0;
        }
    }
}
