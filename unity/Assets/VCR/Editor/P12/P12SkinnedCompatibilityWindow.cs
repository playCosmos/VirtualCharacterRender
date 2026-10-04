using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P12
{
    public sealed class P12SkinnedCompatibilityWindow :
        EditorWindow
    {
        private SkinnedMeshRenderer _sourceRenderer;
        private Animator _targetAnimator;
        private P12SkinnedCompatibilityReport _report;
        private string _error;
        private Vector2 _scroll;

        [MenuItem("VCR/P12/Open Skinned Compatibility")]
        public static void Open()
        {
            GetWindow<
                    P12SkinnedCompatibilityWindow>(
                    "VCR Skinned Compatibility")
                .Show();
        }

        public static void OpenWithTarget(
            Animator targetAnimator)
        {
            var window =
                GetWindow<
                    P12SkinnedCompatibilityWindow>(
                    "VCR Skinned Compatibility");
            window._targetAnimator =
                targetAnimator;
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            if (_sourceRenderer == null &&
                Selection.activeGameObject != null)
            {
                _sourceRenderer =
                    Selection.activeGameObject
                        .GetComponentInChildren<
                            SkinnedMeshRenderer>(
                            true);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Skinned Asset Compatibility",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool performs structural skeleton checks only. A structurally compatible result does not prove bind-pose fit, deformation quality, clipping, or tracking stability. No automatic rebind is performed.",
                MessageType.Warning);

            _sourceRenderer =
                (SkinnedMeshRenderer)
                EditorGUILayout.ObjectField(
                    "Source Skinned Renderer",
                    _sourceRenderer,
                    typeof(
                        SkinnedMeshRenderer),
                    true);
            _targetAnimator =
                (Animator)
                EditorGUILayout.ObjectField(
                    "Target Humanoid Animator",
                    _targetAnimator,
                    typeof(Animator),
                    true);

            using (new EditorGUILayout
                       .HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           _sourceRenderer == null ||
                           _targetAnimator == null))
                {
                    if (GUILayout.Button(
                            "Analyze Structure"))
                    {
                        Analyze();
                    }
                }

                if (GUILayout.Button(
                        "Use Selection"))
                {
                    UseSelection();
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    _error))
            {
                EditorGUILayout.HelpBox(
                    _error,
                    MessageType.Error);
            }

            DrawReport();
        }

        private void UseSelection()
        {
            var selected =
                Selection.activeGameObject;

            if (selected == null)
            {
                return;
            }

            var renderer =
                selected.GetComponentInChildren<
                    SkinnedMeshRenderer>(
                    true);

            if (renderer != null)
            {
                _sourceRenderer =
                    renderer;
            }

            var animator =
                selected.GetComponentInParent<
                    Animator>();

            if (animator == null)
            {
                animator =
                    selected.GetComponentInChildren<
                        Animator>(
                        true);
            }

            if (animator != null)
            {
                _targetAnimator =
                    animator;
            }
        }

        private void Analyze()
        {
            _error = null;
            _report = null;

            if (!P12SkinnedCompatibilityAnalyzer
                .TryAnalyze(
                    _sourceRenderer,
                    _targetAnimator,
                    out _report,
                    out _error))
            {
                return;
            }
        }

        private void DrawReport()
        {
            if (_report == null)
            {
                return;
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                _report.StructurallyCompatible
                    ? $"Structurally compatible: {_report.MappedBoneCount}/{_report.SourceBoneCount} bones mapped. Real bind-pose/avatar preview is still required."
                    : $"Structurally incompatible: {_report.MappedBoneCount}/{_report.SourceBoneCount} bones mapped. Resolve errors before considering a skinned package.",
                _report.StructurallyCompatible
                    ? MessageType.Info
                    : MessageType.Error);

            EditorGUILayout.LabelField(
                $"Humanoid mappings: {_report.HumanoidMappedCount} | Exact-name mappings: {_report.ExactNameMappedCount}");

            _scroll =
                EditorGUILayout.BeginScrollView(
                    _scroll);

            if (_report.Errors.Length > 0)
            {
                EditorGUILayout.LabelField(
                    "Errors",
                    EditorStyles.boldLabel);

                foreach (var message in
                         _report.Errors)
                {
                    EditorGUILayout.HelpBox(
                        message,
                        MessageType.Error);
                }
            }

            if (_report.Warnings.Length > 0)
            {
                EditorGUILayout.LabelField(
                    "Warnings",
                    EditorStyles.boldLabel);

                foreach (var message in
                         _report.Warnings)
                {
                    EditorGUILayout.HelpBox(
                        message,
                        MessageType.Warning);
                }
            }

            EditorGUILayout.LabelField(
                "Bone Mapping",
                EditorStyles.boldLabel);

            foreach (var mapping in
                     _report.Mappings)
            {
                if (mapping == null)
                {
                    continue;
                }

                var strategy =
                    mapping.Strategy ==
                    P12SkinnedBoneMappingStrategy
                        .HumanoidBone
                        ? "Humanoid:" +
                          mapping.HumanoidBone
                        : "ExactName";

                EditorGUILayout.LabelField(
                    $"{mapping.SourceBone?.name ?? "<null>"} → {mapping.TargetBone?.name ?? "<missing>"} ({strategy})",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
