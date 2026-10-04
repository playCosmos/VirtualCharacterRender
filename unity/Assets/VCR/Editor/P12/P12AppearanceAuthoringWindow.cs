using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Editor.P12
{
    public sealed class P12AppearanceAuthoringWindow :
        EditorWindow
    {
        private BasicCharacterAppearanceRuntime _runtime;
        private SerializedObject _serializedRuntime;
        private SerializedProperty _defaultPresetId;
        private SerializedProperty _applyDefaultOnAwake;
        private SerializedProperty _autoDiscoverHierarchy;
        private SerializedProperty _appearanceRootName;
        private SerializedProperty _outfitsRootName;
        private SerializedProperty _accessoriesRootName;
        private SerializedProperty _outfits;
        private SerializedProperty _accessories;
        private SerializedProperty _presets;

        private Vector2 _scroll;
        private bool _showOutfits = true;
        private bool _showAccessories = true;
        private bool _showPresets = true;
        private bool _hasPendingChanges;
        private string _lastValidJson;
        private string _newPresetId =
            "new-preset";
        private string _accessoryPackageDestination =
            P12AccessoryPackageImporter
                .DefaultDestinationRoot;
        private string _message;
        private MessageType _messageType =
            MessageType.Info;

        [MenuItem("VCR/P12/Open Appearance Authoring")]
        public static void Open()
        {
            GetWindow<
                    P12AppearanceAuthoringWindow>(
                    "VCR Appearance Authoring")
                .Show();
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
                            BasicCharacterAppearanceRuntime>()
                    : null;

            if (selected != null &&
                !ReferenceEquals(
                    selected,
                    _runtime))
            {
                _runtime =
                    selected;
                Rebind();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Advanced Appearance Authoring",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "P12 authoring edits the scene-backed appearance bindings used by BasicCharacterAppearanceRuntime. Outfit/accessory roots are Unity scene references; exported transition JSON remains logical-ID-only.",
                MessageType.Info);

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
                _runtime =
                    nextRuntime;
                Rebind();
            }

            if (_runtime == null ||
                _serializedRuntime == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a BasicCharacterAppearanceRuntime to author wardrobe and accessories.",
                    MessageType.Info);
                return;
            }

            _serializedRuntime.Update();

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
                    "Serialized appearance bindings changed since the last successful validation. Use Validate & Apply before treating this configuration as runtime-ready.",
                    MessageType.Warning);
            }

            DrawTopActions();

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            DrawRuntimeSettings();
            DrawOutfits();
            DrawAccessories();
            DrawPresets();

            EditorGUILayout.EndScrollView();

            if (_serializedRuntime
                .ApplyModifiedProperties())
            {
                _hasPendingChanges =
                    true;
                EditorUtility.SetDirty(
                    _runtime);
            }
        }

        private void DrawTopActions()
        {
            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Discover Convention → Explicit Bindings"))
                {
                    DiscoverConventionBindings();
                }

                if (GUILayout.Button(
                        "Validate & Apply"))
                {
                    ValidateAndApply();
                }

                if (GUILayout.Button(
                        "Import Accessory Package"))
                {
                    ImportAccessoryPackage();
                }

                if (GUILayout.Button(
                        "Open Transition Timeline"))
                {
                    VCR.Editor.P11
                        .P11AppearanceTransitionTimelineEditor
                        .Open();
                }
            }

            _accessoryPackageDestination =
                EditorGUILayout.TextField(
                    "Accessory Import Destination",
                    _accessoryPackageDestination);
        }

        private void DrawRuntimeSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Runtime Binding Settings",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _autoDiscoverHierarchy,
                new GUIContent(
                    "Auto Discover When Empty"));
            EditorGUILayout.PropertyField(
                _appearanceRootName);
            EditorGUILayout.PropertyField(
                _outfitsRootName);
            EditorGUILayout.PropertyField(
                _accessoriesRootName);
            EditorGUILayout.PropertyField(
                _defaultPresetId,
                new GUIContent(
                    "Default Preset ID"));
            EditorGUILayout.PropertyField(
                _applyDefaultOnAwake);
        }

        private void DrawOutfits()
        {
            EditorGUILayout.Space();
            _showOutfits =
                EditorGUILayout.Foldout(
                    _showOutfits,
                    $"Outfits ({_outfits.arraySize})",
                    true);

            if (!_showOutfits)
            {
                return;
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Outfit"))
                {
                    AddEmptyOutfit();
                }

                using (new EditorGUI.DisabledScope(
                           Selection.activeGameObject ==
                           null))
                {
                    if (GUILayout.Button(
                            "Add Selected Root"))
                    {
                        AddSelectedOutfitRoot();
                    }
                }
            }

            for (var i = 0;
                 i < _outfits.arraySize;
                 i++)
            {
                var outfit =
                    _outfits
                        .GetArrayElementAtIndex(
                            i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            outfit.FindPropertyRelative(
                                "OutfitId"),
                            new GUIContent(
                                $"Outfit {i + 1} ID"));

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    70f)))
                        {
                            _outfits
                                .DeleteArrayElementAtIndex(
                                    i);
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        outfit.FindPropertyRelative(
                            "Roots"),
                        includeChildren:
                            true);
                }
            }
        }

        private void DrawAccessories()
        {
            EditorGUILayout.Space();
            _showAccessories =
                EditorGUILayout.Foldout(
                    _showAccessories,
                    $"Accessories ({_accessories.arraySize})",
                    true);

            if (!_showAccessories)
            {
                return;
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Accessory"))
                {
                    AddEmptyAccessory();
                }

                using (new EditorGUI.DisabledScope(
                           Selection.activeGameObject ==
                           null))
                {
                    if (GUILayout.Button(
                            "Add Selected Accessory"))
                    {
                        AddSelectedAccessory();
                    }
                }
            }

            for (var i = 0;
                 i < _accessories.arraySize;
                 i++)
            {
                var accessory =
                    _accessories
                        .GetArrayElementAtIndex(
                            i);

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            accessory.FindPropertyRelative(
                                "SlotId"),
                            new GUIContent(
                                $"Accessory {i + 1} Slot"));
                        EditorGUILayout.PropertyField(
                            accessory.FindPropertyRelative(
                                "AccessoryId"),
                            new GUIContent(
                                "ID"));

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    70f)))
                        {
                            _accessories
                                .DeleteArrayElementAtIndex(
                                    i);
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        accessory.FindPropertyRelative(
                            "Root"));
                    DrawAccessoryAnchor(
                        accessory);
                }
            }
        }

        private void DrawAccessoryAnchor(
            SerializedProperty accessory)
        {
            var anchorMode =
                accessory.FindPropertyRelative(
                    "AnchorMode");

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(
                anchorMode,
                new GUIContent(
                    "Anchor Mode"));

            var mode =
                (AppearanceAccessoryAnchorMode)
                anchorMode.enumValueIndex;

            switch (mode)
            {
                case AppearanceAccessoryAnchorMode.Transform:
                    EditorGUILayout.PropertyField(
                        accessory.FindPropertyRelative(
                            "AnchorTransform"));

                    using (new EditorGUI.DisabledScope(
                               Selection.activeTransform ==
                               null))
                    {
                        if (GUILayout.Button(
                                "Use Selected Transform As Anchor"))
                        {
                            accessory.FindPropertyRelative(
                                    "AnchorTransform")
                                .objectReferenceValue =
                                    Selection.activeTransform;
                        }
                    }

                    break;

                case AppearanceAccessoryAnchorMode.HumanoidBone:
                    EditorGUILayout.PropertyField(
                        accessory.FindPropertyRelative(
                            "AnchorAnimator"));
                    EditorGUILayout.PropertyField(
                        accessory.FindPropertyRelative(
                            "AnchorBone"));

                    using (new EditorGUI.DisabledScope(
                               _runtime == null))
                    {
                        if (GUILayout.Button(
                                "Use Runtime Humanoid Animator"))
                        {
                            var animator =
                                _runtime
                                    .GetComponentInChildren<Animator>(
                                        true);

                            if (animator == null)
                            {
                                animator =
                                    _runtime
                                        .GetComponentInParent<Animator>();
                            }

                            accessory.FindPropertyRelative(
                                    "AnchorAnimator")
                                .objectReferenceValue =
                                    animator;
                        }
                    }

                    break;
            }

            if (mode !=
                AppearanceAccessoryAnchorMode.None)
            {
                using (new EditorGUILayout
                           .HorizontalScope())
                {
                    if (GUILayout.Button(
                            "Capture Current Offset"))
                    {
                        CaptureAccessoryAnchorOffset(
                            accessory);
                    }

                    if (GUILayout.Button(
                            "Preview Attachment"))
                    {
                        PreviewAccessoryAttachment(
                            accessory);
                    }

                    if (GUILayout.Button(
                            "Reset Offset"))
                    {
                        accessory.FindPropertyRelative(
                                "LocalPosition")
                            .vector3Value =
                                Vector3.zero;
                        accessory.FindPropertyRelative(
                                "LocalEulerAngles")
                            .vector3Value =
                                Vector3.zero;
                    }
                }

                EditorGUILayout.PropertyField(
                    accessory.FindPropertyRelative(
                        "LocalPosition"));
                EditorGUILayout.PropertyField(
                    accessory.FindPropertyRelative(
                        "LocalEulerAngles"));

                var overrideScale =
                    accessory.FindPropertyRelative(
                        "OverrideLocalScale");
                EditorGUILayout.PropertyField(
                    overrideScale);

                if (overrideScale.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        accessory.FindPropertyRelative(
                            "LocalScale"));
                }

                EditorGUILayout.PropertyField(
                    accessory.FindPropertyRelative(
                        "RestoreOriginalTransformWhenInactive"));
            }
        }

        private void CaptureAccessoryAnchorOffset(
            SerializedProperty accessory)
        {
            if (!TryResolveAccessoryAnchor(
                    accessory,
                    out var root,
                    out var anchor,
                    out var resolveError))
            {
                _message =
                    "Accessory anchor resolve failed: " +
                    resolveError;
                _messageType =
                    MessageType.Error;
                return;
            }

            if (!P12AppearanceAuthoringUtility
                .TryCaptureAnchorOffset(
                    root != null
                        ? root.transform
                        : null,
                    anchor,
                    out var localPosition,
                    out var localEulerAngles,
                    out var captureError))
            {
                _message =
                    "Accessory offset capture failed: " +
                    captureError;
                _messageType =
                    MessageType.Error;
                return;
            }

            accessory.FindPropertyRelative(
                    "LocalPosition")
                .vector3Value =
                    localPosition;
            accessory.FindPropertyRelative(
                    "LocalEulerAngles")
                .vector3Value =
                    localEulerAngles;
            _hasPendingChanges =
                true;
            _message =
                "Captured accessory local position/rotation relative to the selected anchor.";
            _messageType =
                MessageType.Info;
        }

        private void PreviewAccessoryAttachment(
            SerializedProperty accessory)
        {
            if (!TryResolveAccessoryAnchor(
                    accessory,
                    out var root,
                    out var anchor,
                    out var resolveError))
            {
                _message =
                    "Accessory anchor resolve failed: " +
                    resolveError;
                _messageType =
                    MessageType.Error;
                return;
            }

            if (root == null ||
                anchor == null)
            {
                _message =
                    "Accessory preview requires a root and a resolved anchor.";
                _messageType =
                    MessageType.Error;
                return;
            }

            var localPosition =
                accessory.FindPropertyRelative(
                        "LocalPosition")
                    .vector3Value;
            var localEulerAngles =
                accessory.FindPropertyRelative(
                        "LocalEulerAngles")
                    .vector3Value;
            var overrideLocalScale =
                accessory.FindPropertyRelative(
                        "OverrideLocalScale")
                    .boolValue;
            var localScale =
                accessory.FindPropertyRelative(
                        "LocalScale")
                    .vector3Value;

            if (!P12AppearanceAuthoringUtility
                .TryValidateAccessoryAnchorPose(
                    localPosition,
                    localEulerAngles,
                    overrideLocalScale,
                    localScale,
                    out var poseError))
            {
                _message =
                    "Accessory preview rejected: " +
                    poseError;
                _messageType =
                    MessageType.Error;
                return;
            }

            Undo.SetTransformParent(
                root.transform,
                anchor,
                "Preview Accessory Attachment");
            Undo.RecordObject(
                root.transform,
                "Preview Accessory Attachment");
            root.transform.localPosition =
                localPosition;
            root.transform.localRotation =
                Quaternion.Euler(
                    localEulerAngles);

            if (overrideLocalScale)
            {
                root.transform.localScale =
                    localScale;
            }

            EditorUtility.SetDirty(
                root.transform);
            SceneView.RepaintAll();

            _message =
                $"Previewed '{root.name}' on anchor '{anchor.name}'. Use Undo to restore the previous hierarchy/pose, then Validate & Apply when the authored values are final.";
            _messageType =
                MessageType.Info;
        }

        private static bool TryResolveAccessoryAnchor(
            SerializedProperty accessory,
            out GameObject root,
            out Transform anchor,
            out string error)
        {
            root =
                accessory.FindPropertyRelative(
                        "Root")
                    .objectReferenceValue as
                    GameObject;
            var mode =
                (AppearanceAccessoryAnchorMode)
                accessory.FindPropertyRelative(
                        "AnchorMode")
                    .enumValueIndex;
            var explicitAnchor =
                accessory.FindPropertyRelative(
                        "AnchorTransform")
                    .objectReferenceValue as
                    Transform;
            var animator =
                accessory.FindPropertyRelative(
                        "AnchorAnimator")
                    .objectReferenceValue as
                    Animator;
            var bone =
                (HumanBodyBones)
                accessory.FindPropertyRelative(
                        "AnchorBone")
                    .enumValueIndex;

            return P12AppearanceAuthoringUtility
                .TryResolveAccessoryAnchor(
                    mode,
                    explicitAnchor,
                    animator,
                    bone,
                    out anchor,
                    out error);
        }

        private void DrawPresets()
        {
            EditorGUILayout.Space();
            _showPresets =
                EditorGUILayout.Foldout(
                    _showPresets,
                    $"Authored Presets ({_presets.arraySize})",
                    true);

            if (!_showPresets)
            {
                return;
            }

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                if (GUILayout.Button(
                        "Add Empty Preset"))
                {
                    AddEmptyPreset();
                }

                _newPresetId =
                    EditorGUILayout.TextField(
                        _newPresetId,
                        GUILayout.MinWidth(
                            150f));

                if (GUILayout.Button(
                        "Capture Current Appearance"))
                {
                    CaptureCurrentAppearanceAsPreset();
                }
            }

            for (var i = 0;
                 i < _presets.arraySize;
                 i++)
            {
                var preset =
                    _presets
                        .GetArrayElementAtIndex(
                            i);
                var presetId =
                    preset.FindPropertyRelative(
                            "PresetId")
                        .stringValue;

                using (new EditorGUILayout
                           .VerticalScope(
                               EditorStyles.helpBox))
                {
                    using (new EditorGUILayout
                               .HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(
                            preset.FindPropertyRelative(
                                "PresetId"),
                            new GUIContent(
                                $"Preset {i + 1} ID"));

                        if (GUILayout.Button(
                                "Set Default",
                                GUILayout.Width(
                                    92f)))
                        {
                            _defaultPresetId
                                .stringValue =
                                    presetId;
                        }

                        if (GUILayout.Button(
                                "Delete",
                                GUILayout.Width(
                                    70f)))
                        {
                            _presets
                                .DeleteArrayElementAtIndex(
                                    i);
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.PropertyField(
                        preset.FindPropertyRelative(
                            "OutfitId"));
                    EditorGUILayout.PropertyField(
                        preset.FindPropertyRelative(
                            "PreferredTransitionId"));
                    EditorGUILayout.PropertyField(
                        preset.FindPropertyRelative(
                            "Accessories"),
                        includeChildren:
                            true);
                }
            }
        }

        private void ImportAccessoryPackage()
        {
            var manifestPath =
                EditorUtility.OpenFilePanel(
                    "Import VCR Accessory Package Manifest",
                    string.Empty,
                    "json");

            if (string.IsNullOrWhiteSpace(
                    manifestPath))
            {
                return;
            }

            if (!P12AccessoryPackageImporter
                .TryLoadManifest(
                    manifestPath,
                    out var manifest,
                    out var anchorMode,
                    out var bone,
                    out var manifestError))
            {
                _message =
                    "Accessory package validation failed: " +
                    manifestError;
                _messageType =
                    MessageType.Error;
                return;
            }

            if (AccessoryIdExists(
                    manifest.SlotId,
                    manifest.AccessoryId))
            {
                _message =
                    $"Accessory package conflicts with existing binding '{manifest.SlotId}/{manifest.AccessoryId}'.";
                _messageType =
                    MessageType.Error;
                return;
            }

            if (!P12AccessoryPackageImporter
                .TryImport(
                    manifestPath,
                    _accessoryPackageDestination,
                    out var importResult,
                    out var importError))
            {
                _message =
                    "Accessory package import failed: " +
                    importError;
                _messageType =
                    MessageType.Error;
                return;
            }

            Animator packageAnimator =
                null;

            if (anchorMode ==
                AppearanceAccessoryAnchorMode
                    .HumanoidBone)
            {
                packageAnimator =
                    _runtime
                        .GetComponentInChildren<Animator>(
                            true);

                if (packageAnimator == null)
                {
                    packageAnimator =
                        _runtime
                            .GetComponentInParent<Animator>();
                }

                if (packageAnimator == null ||
                    packageAnimator.avatar == null ||
                    !packageAnimator.isHuman ||
                    packageAnimator.GetBoneTransform(
                        bone) == null)
                {
                    P12AccessoryPackageImporter
                        .DeleteImportedPackage(
                            importResult);
                    _message =
                        $"Accessory package requests humanoid bone '{bone}', but the selected appearance runtime does not expose that bone through a humanoid Animator. Imported assets were rolled back.";
                    _messageType =
                        MessageType.Error;
                    return;
                }
            }

            GameObject instance = null;

            try
            {
                instance =
                    PrefabUtility.InstantiatePrefab(
                        importResult.ModelAsset) as
                        GameObject;

                if (instance == null)
                {
                    instance =
                        UnityEngine.Object.Instantiate(
                            importResult.ModelAsset);
                }

                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Imported accessory model could not be instantiated.");
                }

                Undo.RegisterCreatedObjectUndo(
                    instance,
                    "Import Accessory Package");
                instance.name =
                    manifest.AccessoryId;
                instance.SetActive(
                    false);

                var parent =
                    FindOrCreateImportedAccessorySlot(
                        manifest.SlotId);
                instance.transform.SetParent(
                    parent,
                    false);

                Undo.RecordObject(
                    _runtime,
                    "Register Imported Accessory");

                var index =
                    _accessories.arraySize;
                _accessories.arraySize =
                    index + 1;
                var accessory =
                    _accessories
                        .GetArrayElementAtIndex(
                            index);

                accessory.FindPropertyRelative(
                        "SlotId")
                    .stringValue =
                        manifest.SlotId;
                accessory.FindPropertyRelative(
                        "AccessoryId")
                    .stringValue =
                        manifest.AccessoryId;
                accessory.FindPropertyRelative(
                        "Root")
                    .objectReferenceValue =
                        instance;
                accessory.FindPropertyRelative(
                        "AnchorMode")
                    .enumValueIndex =
                        (int)anchorMode;
                accessory.FindPropertyRelative(
                        "AnchorTransform")
                    .objectReferenceValue =
                        null;

                accessory.FindPropertyRelative(
                        "AnchorAnimator")
                    .objectReferenceValue =
                        anchorMode ==
                            AppearanceAccessoryAnchorMode
                                .HumanoidBone
                            ? packageAnimator
                            : null;
                accessory.FindPropertyRelative(
                        "AnchorBone")
                    .enumValueIndex =
                        (int)bone;
                accessory.FindPropertyRelative(
                        "LocalPosition")
                    .vector3Value =
                        manifest.LocalPosition;
                accessory.FindPropertyRelative(
                        "LocalEulerAngles")
                    .vector3Value =
                        manifest.LocalEulerAngles;
                accessory.FindPropertyRelative(
                        "OverrideLocalScale")
                    .boolValue =
                        manifest.OverrideLocalScale;
                accessory.FindPropertyRelative(
                        "LocalScale")
                    .vector3Value =
                        manifest.LocalScale;
                accessory.FindPropertyRelative(
                        "RestoreOriginalTransformWhenInactive")
                    .boolValue =
                        manifest
                            .RestoreOriginalTransformWhenInactive;

                _serializedRuntime
                    .ApplyModifiedProperties();
                EditorUtility.SetDirty(
                    _runtime);
                _hasPendingChanges =
                    true;

                _message =
                    $"Imported accessory package '{manifest.PackageId}' {manifest.PackageVersion} as '{manifest.SlotId}/{manifest.AccessoryId}'. Validate & Apply to register the scene binding.";

                _messageType =
                    MessageType.Info;

                Selection.activeGameObject =
                    instance;
            }
            catch (Exception exception)
            {
                if (instance != null)
                {
                    Undo.DestroyObjectImmediate(
                        instance);
                }

                P12AccessoryPackageImporter
                    .DeleteImportedPackage(
                        importResult);

                _serializedRuntime.Update();
                _message =
                    "Accessory package scene registration failed and imported assets were rolled back: " +
                    exception.Message;
                _messageType =
                    MessageType.Error;
            }
        }

        private Transform FindOrCreateImportedAccessorySlot(
            string slotId)
        {
            var container =
                _runtime.transform.Find(
                    "VCRImportedAccessories");

            if (container == null)
            {
                var containerObject =
                    new GameObject(
                        "VCRImportedAccessories");
                Undo.RegisterCreatedObjectUndo(
                    containerObject,
                    "Create Imported Accessory Container");
                containerObject.transform.SetParent(
                    _runtime.transform,
                    false);
                container =
                    containerObject.transform;
            }

            var slot =
                container.Find(
                    slotId);

            if (slot != null)
            {
                return slot;
            }

            var slotObject =
                new GameObject(
                    slotId);
            Undo.RegisterCreatedObjectUndo(
                slotObject,
                "Create Imported Accessory Slot");
            slotObject.transform.SetParent(
                container,
                false);
            return slotObject.transform;
        }

        private void DiscoverConventionBindings()
        {
            if (_runtime == null)
            {
                return;
            }

            if ((_outfits.arraySize > 0 ||
                 _accessories.arraySize > 0) &&
                !EditorUtility.DisplayDialog(
                    "Replace Explicit Appearance Bindings?",
                    "Convention discovery will replace the current explicit outfit and accessory binding arrays. Presets and transitions are preserved.",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            if (!P12AppearanceAuthoringUtility
                .TryDiscoverConvention(
                    _runtime.transform,
                    _appearanceRootName.stringValue,
                    _outfitsRootName.stringValue,
                    _accessoriesRootName.stringValue,
                    out var outfits,
                    out var accessories,
                    out var error))
            {
                _message =
                    "Appearance discovery failed: " +
                    error;
                _messageType =
                    MessageType.Error;
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Discover Appearance Bindings");
            WriteOutfits(
                outfits);
            WriteAccessories(
                accessories);
            _serializedRuntime
                .ApplyModifiedProperties();
            _hasPendingChanges =
                true;
            EditorUtility.SetDirty(
                _runtime);

            _message =
                $"Discovered {outfits.Length} outfit(s) and {accessories.Length} accessory binding(s). Validate & Apply to commit runtime configuration.";
            _messageType =
                MessageType.Info;
        }

        private void AddEmptyOutfit()
        {
            Undo.RecordObject(
                _runtime,
                "Add Appearance Outfit");
            var index =
                _outfits.arraySize;
            _outfits.arraySize =
                index + 1;
            var outfit =
                _outfits
                    .GetArrayElementAtIndex(
                        index);
            outfit.FindPropertyRelative(
                    "OutfitId")
                .stringValue =
                    BuildUniqueOutfitId(
                        "outfit");
            outfit.FindPropertyRelative(
                    "Roots")
                .arraySize = 0;
        }

        private void AddSelectedOutfitRoot()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            Undo.RecordObject(
                _runtime,
                "Add Selected Appearance Outfit");
            var index =
                _outfits.arraySize;
            _outfits.arraySize =
                index + 1;
            var outfit =
                _outfits
                    .GetArrayElementAtIndex(
                        index);
            outfit.FindPropertyRelative(
                    "OutfitId")
                .stringValue =
                    BuildUniqueOutfitId(
                        selected.name);
            var roots =
                outfit.FindPropertyRelative(
                    "Roots");
            roots.arraySize = 1;
            roots.GetArrayElementAtIndex(
                    0)
                .objectReferenceValue =
                    selected;
        }

        private void AddEmptyAccessory()
        {
            Undo.RecordObject(
                _runtime,
                "Add Appearance Accessory");
            var index =
                _accessories.arraySize;
            _accessories.arraySize =
                index + 1;
            var accessory =
                _accessories
                    .GetArrayElementAtIndex(
                        index);
            accessory.FindPropertyRelative(
                    "SlotId")
                .stringValue =
                    "accessory";
            accessory.FindPropertyRelative(
                    "AccessoryId")
                .stringValue =
                    BuildUniqueAccessoryId(
                        "accessory",
                        "item");
            accessory.FindPropertyRelative(
                    "Root")
                .objectReferenceValue =
                    null;
            ResetAccessoryAnchor(
                accessory);
        }

        private void AddSelectedAccessory()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            var slotId =
                InferAccessorySlotId(
                    selected);
            var accessoryId =
                BuildUniqueAccessoryId(
                    slotId,
                    selected.name);

            Undo.RecordObject(
                _runtime,
                "Add Selected Appearance Accessory");
            var index =
                _accessories.arraySize;
            _accessories.arraySize =
                index + 1;
            var accessory =
                _accessories
                    .GetArrayElementAtIndex(
                        index);
            accessory.FindPropertyRelative(
                    "SlotId")
                .stringValue =
                    slotId;
            accessory.FindPropertyRelative(
                    "AccessoryId")
                .stringValue =
                    accessoryId;
            accessory.FindPropertyRelative(
                    "Root")
                .objectReferenceValue =
                    selected;
            ResetAccessoryAnchor(
                accessory);
        }

        private static void ResetAccessoryAnchor(
            SerializedProperty accessory)
        {
            accessory.FindPropertyRelative(
                    "AnchorMode")
                .enumValueIndex =
                    (int)
                    AppearanceAccessoryAnchorMode.None;
            accessory.FindPropertyRelative(
                    "AnchorTransform")
                .objectReferenceValue =
                    null;
            accessory.FindPropertyRelative(
                    "AnchorAnimator")
                .objectReferenceValue =
                    null;
            accessory.FindPropertyRelative(
                    "AnchorBone")
                .enumValueIndex =
                    (int)HumanBodyBones.Head;
            accessory.FindPropertyRelative(
                    "LocalPosition")
                .vector3Value =
                    Vector3.zero;
            accessory.FindPropertyRelative(
                    "LocalEulerAngles")
                .vector3Value =
                    Vector3.zero;
            accessory.FindPropertyRelative(
                    "OverrideLocalScale")
                .boolValue =
                    false;
            accessory.FindPropertyRelative(
                    "LocalScale")
                .vector3Value =
                    Vector3.one;
            accessory.FindPropertyRelative(
                    "RestoreOriginalTransformWhenInactive")
                .boolValue =
                    true;
        }

        private void AddEmptyPreset()
        {
            Undo.RecordObject(
                _runtime,
                "Add Appearance Preset");
            var index =
                _presets.arraySize;
            _presets.arraySize =
                index + 1;
            var preset =
                _presets
                    .GetArrayElementAtIndex(
                        index);
            preset.FindPropertyRelative(
                    "PresetId")
                .stringValue =
                    BuildUniquePresetId(
                        "preset");
            preset.FindPropertyRelative(
                    "OutfitId")
                .stringValue =
                    FirstOutfitId();
            preset.FindPropertyRelative(
                    "PreferredTransitionId")
                .stringValue =
                    "Immediate";
            preset.FindPropertyRelative(
                    "Accessories")
                .arraySize = 0;
        }

        private void CaptureCurrentAppearanceAsPreset()
        {
            var current =
                _runtime.Current;

            if (string.IsNullOrWhiteSpace(
                    current.OutfitId) &&
                (current.Accessories == null ||
                 current.Accessories.Length == 0))
            {
                _message =
                    "No active outfit/accessory state is available to capture.";
                _messageType =
                    MessageType.Warning;
                return;
            }

            var id =
                BuildUniquePresetId(
                    string.IsNullOrWhiteSpace(
                        _newPresetId)
                        ? "captured-preset"
                        : _newPresetId);
            var binding =
                P12AppearanceAuthoringUtility
                    .CreatePresetFromCurrent(
                        id,
                        current,
                        "Immediate");

            Undo.RecordObject(
                _runtime,
                "Capture Appearance Preset");
            var index =
                _presets.arraySize;
            _presets.arraySize =
                index + 1;
            WritePreset(
                _presets
                    .GetArrayElementAtIndex(
                        index),
                binding);
            _newPresetId =
                id;

            _message =
                $"Captured current appearance as authored preset '{id}'. Validate & Apply to register it.";
            _messageType =
                MessageType.Info;
        }

        private void ValidateAndApply()
        {
            _serializedRuntime
                .ApplyModifiedProperties();
            EditorUtility.SetDirty(
                _runtime);

            if (_runtime.RebuildConfiguration(
                    out var error))
            {
                _lastValidJson =
                    EditorJsonUtility.ToJson(
                        _runtime,
                        prettyPrint:
                            false);
                _hasPendingChanges =
                    false;
                _message =
                    $"Appearance configuration validated: {_runtime.PresetIds.Count} preset(s), {_runtime.TransitionIds.Count} transition(s).";
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
                    EditorJsonUtility
                        .FromJsonOverwrite(
                            _lastValidJson,
                            _runtime);
                    EditorUtility.SetDirty(
                        _runtime);
                    RebindSerializedOnly();
                    _runtime.RebuildConfiguration(
                        out _);
                    _hasPendingChanges =
                        false;
                    _message =
                        "Appearance validation failed and authored bindings were rolled back to the last valid snapshot: " +
                        error;
                    _messageType =
                        MessageType.Error;
                    return;
                }
                catch (Exception exception)
                {
                    _message =
                        "Appearance validation failed, and snapshot rollback also failed: " +
                        error +
                        " / " +
                        exception.Message;
                    _messageType =
                        MessageType.Error;
                    return;
                }
            }

            _message =
                "Appearance validation failed: " +
                error;
            _messageType =
                MessageType.Error;
        }

        private void WriteOutfits(
            AppearanceOutfitBinding[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceOutfitBinding>();
            _outfits.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var property =
                    _outfits
                        .GetArrayElementAtIndex(
                            i);
                var binding =
                    source[i];
                property.FindPropertyRelative(
                        "OutfitId")
                    .stringValue =
                        binding?.OutfitId ??
                        string.Empty;

                var roots =
                    property.FindPropertyRelative(
                        "Roots");
                var sourceRoots =
                    binding?.Roots ??
                    Array.Empty<GameObject>();
                roots.arraySize =
                    sourceRoots.Length;

                for (var rootIndex = 0;
                     rootIndex <
                     sourceRoots.Length;
                     rootIndex++)
                {
                    roots.GetArrayElementAtIndex(
                            rootIndex)
                        .objectReferenceValue =
                            sourceRoots[
                                rootIndex];
                }
            }
        }

        private void WriteAccessories(
            AppearanceAccessoryBinding[] source)
        {
            source ??=
                Array.Empty<
                    AppearanceAccessoryBinding>();
            _accessories.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var property =
                    _accessories
                        .GetArrayElementAtIndex(
                            i);
                var binding =
                    source[i];

                property.FindPropertyRelative(
                        "SlotId")
                    .stringValue =
                        binding?.SlotId ??
                        string.Empty;
                property.FindPropertyRelative(
                        "AccessoryId")
                    .stringValue =
                        binding?.AccessoryId ??
                        string.Empty;
                property.FindPropertyRelative(
                        "Root")
                    .objectReferenceValue =
                        binding?.Root;
                property.FindPropertyRelative(
                        "AnchorMode")
                    .enumValueIndex =
                        (int)(
                            binding?.AnchorMode ??
                            AppearanceAccessoryAnchorMode.None);
                property.FindPropertyRelative(
                        "AnchorTransform")
                    .objectReferenceValue =
                        binding?.AnchorTransform;
                property.FindPropertyRelative(
                        "AnchorAnimator")
                    .objectReferenceValue =
                        binding?.AnchorAnimator;
                property.FindPropertyRelative(
                        "AnchorBone")
                    .enumValueIndex =
                        (int)(
                            binding?.AnchorBone ??
                            HumanBodyBones.Head);
                property.FindPropertyRelative(
                        "LocalPosition")
                    .vector3Value =
                        binding?.LocalPosition ??
                        Vector3.zero;
                property.FindPropertyRelative(
                        "LocalEulerAngles")
                    .vector3Value =
                        binding?.LocalEulerAngles ??
                        Vector3.zero;
                property.FindPropertyRelative(
                        "OverrideLocalScale")
                    .boolValue =
                        binding?.OverrideLocalScale ??
                        false;
                property.FindPropertyRelative(
                        "LocalScale")
                    .vector3Value =
                        binding?.LocalScale ??
                        Vector3.one;
                property.FindPropertyRelative(
                        "RestoreOriginalTransformWhenInactive")
                    .boolValue =
                        binding?.RestoreOriginalTransformWhenInactive ??
                        true;
            }
        }

        private static void WritePreset(
            SerializedProperty property,
            AppearancePresetBinding binding)
        {
            property.FindPropertyRelative(
                    "PresetId")
                .stringValue =
                    binding?.PresetId ??
                    string.Empty;
            property.FindPropertyRelative(
                    "OutfitId")
                .stringValue =
                    binding?.OutfitId ??
                    string.Empty;
            property.FindPropertyRelative(
                    "PreferredTransitionId")
                .stringValue =
                    binding?.PreferredTransitionId ??
                    "Immediate";

            var accessories =
                property.FindPropertyRelative(
                    "Accessories");
            var source =
                binding?.Accessories ??
                Array.Empty<
                    AppearanceAccessorySelectionBinding>();
            accessories.arraySize =
                source.Length;

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var item =
                    accessories
                        .GetArrayElementAtIndex(
                            i);
                item.FindPropertyRelative(
                        "SlotId")
                    .stringValue =
                        source[i]?.SlotId ??
                        string.Empty;
                item.FindPropertyRelative(
                        "AccessoryId")
                    .stringValue =
                        source[i]?.AccessoryId ??
                        string.Empty;
            }
        }

        private string BuildUniqueOutfitId(
            string preferred) =>
                P12AppearanceAuthoringUtility
                    .BuildUniqueId(
                        preferred,
                        OutfitIdExists);

        private string BuildUniqueAccessoryId(
            string slotId,
            string preferred) =>
                P12AppearanceAuthoringUtility
                    .BuildUniqueId(
                        preferred,
                        candidate =>
                            AccessoryIdExists(
                                slotId,
                                candidate));

        private string BuildUniquePresetId(
            string preferred) =>
                P12AppearanceAuthoringUtility
                    .BuildUniqueId(
                        preferred,
                        PresetIdExists);

        private bool OutfitIdExists(
            string id)
        {
            for (var i = 0;
                 i < _outfits.arraySize;
                 i++)
            {
                if (string.Equals(
                        _outfits
                            .GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "OutfitId")
                            .stringValue,
                        id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AccessoryIdExists(
            string slotId,
            string accessoryId)
        {
            for (var i = 0;
                 i < _accessories.arraySize;
                 i++)
            {
                var property =
                    _accessories
                        .GetArrayElementAtIndex(
                            i);

                if (string.Equals(
                        property.FindPropertyRelative(
                                "SlotId")
                            .stringValue,
                        slotId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        property.FindPropertyRelative(
                                "AccessoryId")
                            .stringValue,
                        accessoryId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PresetIdExists(
            string id)
        {
            for (var i = 0;
                 i < _presets.arraySize;
                 i++)
            {
                if (string.Equals(
                        _presets
                            .GetArrayElementAtIndex(
                                i)
                            .FindPropertyRelative(
                                "PresetId")
                            .stringValue,
                        id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string FirstOutfitId()
        {
            return _outfits.arraySize > 0
                ? _outfits
                    .GetArrayElementAtIndex(
                        0)
                    .FindPropertyRelative(
                        "OutfitId")
                    .stringValue
                : string.Empty;
        }

        private string InferAccessorySlotId(
            GameObject selected)
        {
            var transform =
                selected?.transform;
            var parent =
                transform?.parent;

            if (parent != null &&
                parent.parent != null &&
                string.Equals(
                    parent.parent.name,
                    _accessoriesRootName
                        .stringValue,
                    StringComparison.Ordinal))
            {
                return parent.name;
            }

            return "accessory";
        }

        private void ResolveFromSelection()
        {
            if (_runtime != null)
            {
                return;
            }

            _runtime =
                Selection.activeGameObject != null
                    ? Selection.activeGameObject
                        .GetComponentInParent<
                            BasicCharacterAppearanceRuntime>()
                    : null;
        }

        private void Rebind()
        {
            _message = null;
            _hasPendingChanges =
                false;

            if (_runtime == null)
            {
                _serializedRuntime =
                    null;
                _lastValidJson =
                    null;
                return;
            }

            RebindSerializedOnly();

            if (_runtime.RebuildConfiguration(
                    out var error))
            {
                _lastValidJson =
                    EditorJsonUtility.ToJson(
                        _runtime,
                        prettyPrint:
                            false);
            }
            else
            {
                _lastValidJson =
                    null;
                _message =
                    "Current appearance configuration is already invalid: " +
                    error;
                _messageType =
                    MessageType.Warning;
            }
        }

        private void RebindSerializedOnly()
        {
            _serializedRuntime =
                new SerializedObject(
                    _runtime);
            _defaultPresetId =
                _serializedRuntime
                    .FindProperty(
                        "defaultPresetId");
            _applyDefaultOnAwake =
                _serializedRuntime
                    .FindProperty(
                        "applyDefaultOnAwake");
            _autoDiscoverHierarchy =
                _serializedRuntime
                    .FindProperty(
                        "autoDiscoverHierarchy");
            _appearanceRootName =
                _serializedRuntime
                    .FindProperty(
                        "appearanceRootName");
            _outfitsRootName =
                _serializedRuntime
                    .FindProperty(
                        "outfitsRootName");
            _accessoriesRootName =
                _serializedRuntime
                    .FindProperty(
                        "accessoriesRootName");
            _outfits =
                _serializedRuntime
                    .FindProperty(
                        "outfits");
            _accessories =
                _serializedRuntime
                    .FindProperty(
                        "accessories");
            _presets =
                _serializedRuntime
                    .FindProperty(
                        "presets");
        }
    }
}
