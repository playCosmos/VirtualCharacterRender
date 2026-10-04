using UnityEditor;
using UnityEngine;
using VCR.Runtime.Presentation2D;

namespace VCR.Editor.P13
{
    internal sealed class P13ParameterMappingWindow :
        EditorWindow
    {
        private Character2DParameterMappingProfile _profile;
        private string _validationMessage;
        private MessageType _validationType =
            MessageType.Info;

        [MenuItem("VCR/P13/Open 2D Parameter Mapping")]
        private static void Open()
        {
            var window =
                GetWindow<P13ParameterMappingWindow>(
                    "VCR 2D Parameter Mapping");
            window.minSize =
                new Vector2(
                    520f,
                    360f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "P13 — 2D Parameter Mapping",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Author backend-specific target parameter IDs against the shared normalized face, expression, and head-pose semantics. " +
                "This editor does not install or select a Live2D/Inochi2D backend.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var selected =
                (Character2DParameterMappingProfile)
                EditorGUILayout.ObjectField(
                    "Mapping Profile",
                    _profile,
                    typeof(
                        Character2DParameterMappingProfile),
                    false);
            if (EditorGUI.EndChangeCheck())
            {
                _profile =
                    selected;
                ClearValidation();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Create Profile Asset"))
                {
                    CreateProfile();
                }

                using (new EditorGUI.DisabledScope(
                           _profile == null))
                {
                    if (GUILayout.Button(
                            "Ping Asset"))
                    {
                        EditorGUIUtility.PingObject(
                            _profile);
                    }
                }
            }

            if (_profile == null)
            {
                EditorGUILayout.HelpBox(
                    "Create or select a Character2DParameterMappingProfile asset to continue.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();

            var serialized =
                new SerializedObject(
                    _profile);
            serialized.Update();

            var backendId =
                serialized.FindProperty(
                    "backendId");
            var bindings =
                serialized.FindProperty(
                    "bindings");

            if (backendId == null ||
                bindings == null)
            {
                EditorGUILayout.HelpBox(
                    "The selected mapping profile is missing the expected serialized fields.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(
                backendId,
                new GUIContent(
                    "Backend ID",
                    "Must exactly match the installed ICharacter2DBackend.BackendId."));
            EditorGUILayout.PropertyField(
                bindings,
                new GUIContent(
                    "Bindings"),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                serialized
                    .ApplyModifiedProperties();
                EditorUtility.SetDirty(
                    _profile);
                ClearValidation();
            }
            else
            {
                serialized
                    .ApplyModifiedProperties();
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Validate"))
                {
                    ValidateProfile();
                }

                if (GUILayout.Button(
                        "Validate & Save"))
                {
                    if (ValidateProfile())
                    {
                        EditorUtility.SetDirty(
                            _profile);
                        AssetDatabase.SaveAssets();
                        _validationMessage =
                            "Profile is valid and saved.";
                        _validationType =
                            MessageType.Info;
                    }
                }
            }

            if (!string.IsNullOrEmpty(
                    _validationMessage))
            {
                EditorGUILayout.HelpBox(
                    _validationMessage,
                    _validationType);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "A profile is only a backend-neutral authoring asset. Runtime rendering still requires a separately installed backend adapter and real model/runtime validation.",
                MessageType.None);
        }

        private bool ValidateProfile()
        {
            if (Character2DParameterMapper
                .TryValidate(
                    _profile,
                    out var error))
            {
                _validationMessage =
                    "Profile validation passed.";
                _validationType =
                    MessageType.Info;
                return true;
            }

            _validationMessage =
                error ??
                "Profile validation failed.";
            _validationType =
                MessageType.Error;
            return false;
        }

        private void CreateProfile()
        {
            var path =
                EditorUtility.SaveFilePanelInProject(
                    "Create 2D Parameter Mapping Profile",
                    "VCR 2D Parameter Mapping",
                    "asset",
                    "Choose where to save the mapping profile.");

            if (string.IsNullOrEmpty(
                    path))
            {
                return;
            }

            var profile =
                CreateInstance<
                    Character2DParameterMappingProfile>();

            try
            {
                AssetDatabase.CreateAsset(
                    profile,
                    path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _profile =
                    profile;
                Selection.activeObject =
                    profile;
                EditorGUIUtility.PingObject(
                    profile);
                ClearValidation();
            }
            catch
            {
                DestroyImmediate(
                    profile);
                throw;
            }
        }

        private void ClearValidation()
        {
            _validationMessage =
                null;
            _validationType =
                MessageType.Info;
        }
    }
}
