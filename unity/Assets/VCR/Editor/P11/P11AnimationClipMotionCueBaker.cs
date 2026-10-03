using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    public sealed class P11AnimationClipMotionCueBaker :
        EditorWindow
    {
        private AnimationClip _clip;
        private GameObject _referenceRoot;
        private BakedMotionCueSource _targetSource;
        private string _cueId = "quick-change";
        private float _sampleRate = 30f;
        private bool _loop;
        private bool _holdLastPose;

        [MenuItem("VCR/P11/Open AnimationClip Cue Baker")]
        public static void Open()
        {
            GetWindow<
                    P11AnimationClipMotionCueBaker>(
                    "VCR Motion Cue Baker")
                .Show();
        }

        public static void OpenWithClip(
            AnimationClip clip)
        {
            var window =
                GetWindow<
                    P11AnimationClipMotionCueBaker>(
                    "VCR Motion Cue Baker");
            window._clip =
                clip;
            window._cueId =
                clip != null &&
                !string.IsNullOrWhiteSpace(
                    clip.name)
                    ? clip.name
                    : "quick-change";
            window.Show();
            window.Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "AnimationClip → Baked Motion Cue",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bake once against a reference humanoid. Playback uses normalized additive pose data and does not sample AnimationClip/Animator every frame. Animation events named VCRMarker (marker name in stringParameter) or VCRMarker_<name> are preserved as reusable timeline markers.",
                MessageType.Info);

            _clip =
                (AnimationClip)EditorGUILayout
                    .ObjectField(
                        "Animation Clip",
                        _clip,
                        typeof(AnimationClip),
                        false);
            _referenceRoot =
                (GameObject)EditorGUILayout
                    .ObjectField(
                        "Reference Character Root",
                        _referenceRoot,
                        typeof(GameObject),
                        true);
            _targetSource =
                (BakedMotionCueSource)
                    EditorGUILayout.ObjectField(
                        "Register To Source",
                        _targetSource,
                        typeof(BakedMotionCueSource),
                        true);
            _cueId =
                EditorGUILayout.TextField(
                    "Cue ID",
                    _cueId);
            _sampleRate =
                EditorGUILayout.FloatField(
                    "Sample Rate",
                    _sampleRate);
            _loop =
                EditorGUILayout.Toggle(
                    "Loop",
                    _loop);
            _holdLastPose =
                EditorGUILayout.Toggle(
                    "Hold Last Pose",
                    _holdLastPose);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(
                       _clip == null ||
                       _referenceRoot == null ||
                       string.IsNullOrWhiteSpace(
                           _cueId)))
            {
                if (GUILayout.Button(
                        "Bake Motion Cue Asset"))
                {
                    BakeFromWindow();
                }
            }
        }

        private void BakeFromWindow()
        {
            if (!TryBake(
                    _clip,
                    _referenceRoot,
                    _cueId,
                    _sampleRate,
                    _loop,
                    _holdLastPose,
                    out var cue,
                    out var error))
            {
                EditorUtility.DisplayDialog(
                    "VCR Motion Cue Baker",
                    error ??
                    "AnimationClip bake failed.",
                    "OK");
                return;
            }

            var fileName =
                SanitizeFileName(
                    cue.CueId) +
                ".asset";
            var path =
                EditorUtility
                    .SaveFilePanelInProject(
                        "Save Baked Motion Cue",
                        fileName,
                        "asset",
                        "Choose where to save the baked motion cue asset.");

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return;
            }

            var existing =
                AssetDatabase.LoadAssetAtPath<
                    BakedMotionCueAsset>(
                    path);

            if (existing != null)
            {
                Undo.RecordObject(
                    existing,
                    "Update Baked Motion Cue");
                existing.SetCue(
                    cue);
                EditorUtility.SetDirty(
                    existing);
            }
            else
            {
                if (AssetDatabase
                    .LoadMainAssetAtPath(
                        path) != null)
                {
                    EditorUtility.DisplayDialog(
                        "VCR Motion Cue Baker",
                        "The selected path already contains a different asset type.",
                        "OK");
                    return;
                }

                var asset =
                    CreateInstance<
                        BakedMotionCueAsset>();
                asset.SetCue(
                    cue);
                AssetDatabase.CreateAsset(
                    asset,
                    path);
                existing = asset;
            }

            if (_targetSource != null)
            {
                Undo.RecordObject(
                    _targetSource,
                    "Register Baked Motion Cue");

                if (!_targetSource.TryRegisterAsset(
                        existing,
                        out var registerError))
                {
                    EditorUtility.DisplayDialog(
                        "VCR Motion Cue Baker",
                        "The cue asset was saved, but registration failed: " +
                        registerError,
                        "OK");
                }
                else
                {
                    EditorUtility.SetDirty(
                        _targetSource);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject =
                existing;
            EditorGUIUtility.PingObject(
                existing);

            Debug.Log(
                $"VCR P11 baked AnimationClip '{_clip.name}' as motion cue '{cue.CueId}' ({cue.FrameCount} frames at {_sampleRate:0.##} Hz)." +
                (_targetSource != null
                    ? " Registered to '" +
                      _targetSource.name +
                      "'."
                    : string.Empty),
                existing);
        }

        public static bool TryBake(
            AnimationClip clip,
            GameObject referenceRoot,
            string cueId,
            float sampleRate,
            bool loop,
            bool holdLastPose,
            out BakedMotionCueDefinition cue,
            out string error)
        {
            cue = null;
            error = null;

            if (clip == null)
            {
                error =
                    "AnimationClip is required.";
                return false;
            }

            if (referenceRoot == null)
            {
                error =
                    "Reference character root is required.";
                return false;
            }

            cueId =
                cueId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    cueId))
            {
                error =
                    "Cue ID is required.";
                return false;
            }

            if (float.IsNaN(
                    sampleRate) ||
                float.IsInfinity(
                    sampleRate) ||
                sampleRate <= 0f)
            {
                error =
                    "Sample rate must be finite and greater than zero.";
                return false;
            }

            if (clip.length <= 0f)
            {
                error =
                    "AnimationClip must have a positive duration.";
                return false;
            }

            if (EditorUtility.IsPersistent(
                    referenceRoot))
            {
                error =
                    "Reference character root must be a scene instance so Unity AnimationMode can sample and restore it safely.";
                return false;
            }

            if (AnimationMode.InAnimationMode())
            {
                error =
                    "Stop the current Unity Animation preview before baking a motion cue.";
                return false;
            }

            if (!P11MotionMarkerUtility
                .TryExtractFromAnimationClip(
                    clip,
                    out var motionMarkers,
                    out error))
            {
                return false;
            }

            var animator =
                referenceRoot.GetComponentInChildren<
                    Animator>(
                    true);

            var bones =
                ResolveBones(
                    referenceRoot.transform,
                    animator);

            if (bones.Count == 0)
            {
                error =
                    "No humanoid bones could be resolved from the reference character.";
                return false;
            }

            var frameCount =
                Mathf.Max(
                    2,
                    Mathf.CeilToInt(
                        clip.length *
                        sampleRate) +
                    1);

            var rootPositions =
                new Vector3[
                    frameCount];
            var rootRotations =
                new Quaternion[
                    frameCount];

            var tracks =
                new List<
                    BakedBoneMotionCueTrack>(
                    bones.Count);
            var trackByBone =
                new Dictionary<
                    HumanoidBoneId,
                    BakedBoneMotionCueTrack>();

            foreach (var pair in bones)
            {
                var track =
                    new BakedBoneMotionCueTrack
                    {
                        Bone =
                            pair.Key,
                        LocalPositionOffsets =
                            new Vector3[
                                frameCount],
                        LocalRotationOffsets =
                            new Quaternion[
                                frameCount]
                    };

                tracks.Add(
                    track);
                trackByBone.Add(
                    pair.Key,
                    track);
            }

            var rootBasePosition =
                referenceRoot.transform
                    .localPosition;
            var rootBaseRotation =
                referenceRoot.transform
                    .localRotation;
            var boneBasePositions =
                new Dictionary<
                    HumanoidBoneId,
                    Vector3>();
            var boneBaseRotations =
                new Dictionary<
                    HumanoidBoneId,
                    Quaternion>();

            foreach (var pair in bones)
            {
                boneBasePositions[
                    pair.Key] =
                        pair.Value.localPosition;
                boneBaseRotations[
                    pair.Key] =
                        pair.Value.localRotation;
            }

            try
            {
                AnimationMode
                    .StartAnimationMode();

                for (var frameIndex = 0;
                     frameIndex < frameCount;
                     frameIndex++)
                {
                    var normalized =
                        frameCount <= 1
                            ? 0f
                            : frameIndex /
                              (float)(
                                  frameCount - 1);
                    var time =
                        clip.length *
                        normalized;

                    AnimationMode
                        .BeginSampling();
                    AnimationMode
                        .SampleAnimationClip(
                            referenceRoot,
                            clip,
                            time);
                    AnimationMode
                        .EndSampling();

                    rootPositions[
                        frameIndex] =
                            referenceRoot.transform
                                .localPosition -
                            rootBasePosition;
                    rootRotations[
                        frameIndex] =
                            Quaternion.Inverse(
                                rootBaseRotation) *
                            referenceRoot.transform
                                .localRotation;

                    foreach (var pair in bones)
                    {
                        var track =
                            trackByBone[
                                pair.Key];
                        var transform =
                            pair.Value;

                        track.LocalPositionOffsets[
                            frameIndex] =
                                transform.localPosition -
                                boneBasePositions[
                                    pair.Key];
                        track.LocalRotationOffsets[
                            frameIndex] =
                                Quaternion.Inverse(
                                    boneBaseRotations[
                                        pair.Key]) *
                                transform.localRotation;
                    }
                }
            }
            catch (Exception exception)
            {
                error =
                    "AnimationClip sampling failed: " +
                    exception.Message;
                return false;
            }
            finally
            {
                if (AnimationMode
                    .InAnimationMode())
                {
                    AnimationMode
                        .StopAnimationMode();
                }
            }

            cue =
                new BakedMotionCueDefinition
                {
                    CueId =
                        cueId,
                    DurationSeconds =
                        clip.length,
                    Loop =
                        loop,
                    HoldLastPose =
                        holdLastPose,
                    Markers =
                        motionMarkers,
                    PoseSpace =
                        HumanoidPoseSpace
                            .NormalizedLocal,
                    FrameCount =
                        frameCount,
                    RootPositionOffsets =
                        rootPositions,
                    RootRotationOffsets =
                        rootRotations,
                    Bones =
                        tracks.ToArray()
                };

            return true;
        }

        private static Dictionary<
            HumanoidBoneId,
            Transform> ResolveBones(
            Transform root,
            Animator animator)
        {
            var result =
                new Dictionary<
                    HumanoidBoneId,
                    Transform>();

            for (var i = 0;
                 i < (int)HumanoidBoneId.Count;
                 i++)
            {
                var bone =
                    (HumanoidBoneId)i;
                Transform transform = null;

                if (animator != null &&
                    animator.avatar != null &&
                    animator.avatar.isHuman &&
                    Enum.TryParse(
                        bone.ToString(),
                        false,
                        out HumanBodyBones unityBone) &&
                    unityBone !=
                        HumanBodyBones.LastBone)
                {
                    transform =
                        animator.GetBoneTransform(
                            unityBone);
                }

                transform ??=
                    FindByName(
                        root,
                        bone.ToString());

                if (transform != null)
                {
                    result.Add(
                        bone,
                        transform);
                }
            }

            return result;
        }

        private static Transform FindByName(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(
                    root.name,
                    name,
                    StringComparison.Ordinal))
            {
                return root;
            }

            for (var i = 0;
                 i < root.childCount;
                 i++)
            {
                var found =
                    FindByName(
                        root.GetChild(i),
                        name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string SanitizeFileName(
            string value)
        {
            foreach (var invalid in
                     System.IO.Path
                         .GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return string.IsNullOrWhiteSpace(
                value)
                ? "BakedMotionCue"
                : value;
        }
    }
}
