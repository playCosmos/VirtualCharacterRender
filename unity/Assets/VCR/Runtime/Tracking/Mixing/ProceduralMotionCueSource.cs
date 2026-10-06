using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Mixing
{
    [Serializable]
    public sealed class ProceduralBoneMotionCue
    {
        public HumanoidBoneId Bone;
        public Vector3 LocalPositionOffset =
            Vector3.zero;
        public Vector3 LocalEulerDegrees =
            Vector3.zero;
        public AnimationCurve ProgressCurve =
            AnimationCurve.Linear(
                0f,
                0f,
                1f,
                1f);
    }

    [Serializable]
    public sealed class ProceduralMotionCueDefinition
    {
        public string CueId;
        [Min(0.01f)] public float DurationSeconds = 1f;
        public bool Loop = false;
        public bool HoldLastPose = false;
        public HumanoidPoseSpace PoseSpace =
            HumanoidPoseSpace.NormalizedLocal;
        public Vector3 RootPositionOffset =
            Vector3.zero;
        public Vector3 RootEulerDegrees =
            Vector3.zero;
        public AnimationCurve ProgressCurve =
            AnimationCurve.Linear(
                0f,
                0f,
                1f,
                1f);
        public ProceduralBoneMotionCue[] Bones =
            Array.Empty<ProceduralBoneMotionCue>();
    }

    /// <summary>
    /// Lightweight procedural humanoid-pose source for transition/action cues.
    /// It registers as an additive/procedural mixer layer and disables its own
    /// Update callback while idle.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(5000)]
    public sealed class ProceduralMotionCueSource :
        MonoBehaviour,
        ITrackingFrameProvider,
        IMotionCueRuntime,
        IRuntimeMetricsSource
    {
        [SerializeField] private string runtimeId =
            "motion.quickchange";
        [SerializeField] private MotionExpressionMixer mixer;
        [SerializeField] private bool autoFindMixer = true;
        [SerializeField] private ProceduralMotionCueDefinition[] cues =
        {
            new ProceduralMotionCueDefinition
            {
                CueId = "spin",
                DurationSeconds = 0.9f,
                RootEulerDegrees =
                    new Vector3(
                        0f,
                        360f,
                        0f),
                ProgressCurve =
                    AnimationCurve.EaseInOut(
                        0f,
                        0f,
                        1f,
                        1f)
            }
        };

        private readonly Dictionary<string, ProceduralMotionCueDefinition>
            _cues =
                new(StringComparer.Ordinal);
        private readonly List<string> _cueIds =
            new();

        private ProceduralMotionCueDefinition _activeCue;
        private TrackingFrame _latestPoseFrame;
        private double _startedAt;
        private long _sequence;
        private bool _registered;
        private bool _playing;
        private string _lastError;

        private long _playCount;
        private long _releaseCount;
        private long _sampleCount;
        private long _failureCount;

        public MotionCueStatus Status =>
            new(
                runtimeId,
                _playing,
                _activeCue?.CueId,
                _lastError);

        public IReadOnlyList<string> CueIds =>
            _cueIds;

        private void Awake()
        {
            RebuildCues(
                out _);
            EnsureRegistered(
                out _);

            if (!_playing)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!_playing ||
                _activeCue == null)
            {
                enabled = false;
                return;
            }

            var duration =
                Math.Max(
                    0.0001,
                    _activeCue.DurationSeconds);

            var elapsed =
                Time.unscaledTimeAsDouble -
                _startedAt;

            float progress;

            if (_activeCue.Loop)
            {
                progress =
                    (float)(
                        (elapsed % duration) /
                        duration);
            }
            else
            {
                progress =
                    Mathf.Clamp01(
                        (float)(
                            elapsed /
                            duration));
            }

            Publish(
                _activeCue,
                progress);

            if (!_activeCue.Loop &&
                elapsed >= duration)
            {
                if (!_activeCue.HoldLastPose)
                {
                    _latestPoseFrame = null;
                    _activeCue = null;
                }

                _playing = false;
                enabled = false;
            }
        }

        public void ConfigureCues(
            params ProceduralMotionCueDefinition[] definitions)
        {
            cues =
                definitions ??
                Array.Empty<
                    ProceduralMotionCueDefinition>();

            RebuildCues(
                out _);
        }

        public bool RebuildCues(
            out string error)
        {
            error = null;
            _cues.Clear();
            _cueIds.Clear();

            foreach (var cue in
                     cues ??
                     Array.Empty<
                         ProceduralMotionCueDefinition>())
            {
                if (cue == null ||
                    string.IsNullOrWhiteSpace(
                        cue.CueId))
                {
                    error =
                        "Every procedural motion cue requires a non-empty id.";
                    return Fail(error);
                }

                if (float.IsNaN(
                        cue.DurationSeconds) ||
                    float.IsInfinity(
                        cue.DurationSeconds) ||
                    cue.DurationSeconds <= 0f)
                {
                    error =
                        $"Motion cue '{cue.CueId}' requires a finite positive duration.";
                    return Fail(error);
                }

                if (!_cues.TryAdd(
                        cue.CueId,
                        cue))
                {
                    error =
                        $"Duplicate motion cue id '{cue.CueId}'.";
                    return Fail(error);
                }

                _cueIds.Add(
                    cue.CueId);
            }

            _lastError = null;
            return true;
        }

        public bool TryPlayCue(
            string cueId,
            out string error)
        {
            error = null;

            if (!_cues.TryGetValue(
                    cueId ?? string.Empty,
                    out var cue))
            {
                error =
                    $"Unknown motion cue '{cueId ?? "<null>"}'.";
                return Fail(error);
            }

            if (!EnsureRegistered(
                    out error))
            {
                return Fail(error);
            }

            _activeCue = cue;
            _startedAt =
                Time.unscaledTimeAsDouble;
            _playing = true;
            _lastError = null;
            _playCount++;

            enabled = true;

            Publish(
                cue,
                0f);

            return true;
        }

        public bool TryReleaseCue(
            string cueId,
            out string error)
        {
            error = null;

            if (!string.IsNullOrWhiteSpace(
                    cueId) &&
                _activeCue != null &&
                !string.Equals(
                    cueId,
                    _activeCue.CueId,
                    StringComparison.Ordinal))
            {
                error =
                    $"Active motion cue is '{_activeCue.CueId}', not '{cueId}'.";
                return Fail(error);
            }

            _playing = false;
            _activeCue = null;
            _latestPoseFrame = null;
            _lastError = null;
            _releaseCount++;
            enabled = false;
            return true;
        }

        public bool TrySampleCue(
            string cueId,
            float normalizedTime,
            out HumanoidPoseState pose,
            out string error)
        {
            pose = null;
            error = null;

            if (!_cues.TryGetValue(
                    cueId ?? string.Empty,
                    out var cue))
            {
                error =
                    $"Unknown motion cue '{cueId ?? "<null>"}'.";
                return Fail(error);
            }

            if (float.IsNaN(
                    normalizedTime) ||
                float.IsInfinity(
                    normalizedTime))
            {
                error =
                    "Motion cue sample time must be finite.";
                return Fail(error);
            }

            pose =
                BuildPose(
                    cue,
                    Mathf.Clamp01(
                        normalizedTime));
            _sampleCount++;
            return true;
        }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame =
                _latestPoseFrame;

            return frame != null;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        private bool EnsureRegistered(
            out string error)
        {
            error = null;

            if (_registered &&
                mixer != null)
            {
                return true;
            }

            if (mixer == null &&
                autoFindMixer)
            {
                mixer =
                    FindFirstObjectByType<
                        MotionExpressionMixer>(
                            FindObjectsInactive.Exclude);
            }

            if (mixer == null)
            {
                error =
                    "No MotionExpressionMixer is available for the motion cue source.";
                return false;
            }

            var mask =
                new HumanoidPoseLayerMask();
            mask.SetDefaultBoneWeight(
                1f);
            mask.SetRootWeights(
                1f,
                1f);

            var settings =
                new HumanoidPoseLayerSettings();
            settings.Configure(
                layerEnabled:
                    true,
                layerRole:
                    MotionLayerRole.Procedural,
                mode:
                    HumanoidPoseBlendMode.Additive,
                layerWeight:
                    1f,
                layerMask:
                    mask);

            if (!mixer.TryAddAdditionalPoseLayer(
                    this,
                    settings,
                    out error))
            {
                return false;
            }

            _registered = true;
            return true;
        }

        private void Publish(
            ProceduralMotionCueDefinition cue,
            float normalizedTime)
        {
            var pose =
                BuildPose(
                    cue,
                    normalizedTime);

            var nowUs =
                MonotonicClock
                    .NowMicroseconds();

            _latestPoseFrame =
                new TrackingFrame(
                    ++_sequence,
                    sourceTimestampUs:
                        nowUs,
                    validRegions:
                        TrackingRegion.FullBody,
                    confidence:
                        1f,
                    subjectDetected:
                        false,
                    humanoidPose:
                        pose,
                    sourceId:
                        runtimeId +
                        ":" +
                        cue.CueId,
                    runtimeTimestampUs:
                        nowUs);
        }

        private static HumanoidPoseState BuildPose(
            ProceduralMotionCueDefinition cue,
            float normalizedTime)
        {
            var progress =
                Evaluate(
                    cue.ProgressCurve,
                    normalizedTime);

            var rootPosition =
                cue.RootPositionOffset *
                progress;

            var rootEuler =
                cue.RootEulerDegrees *
                progress;

            var rootRotation =
                ToTrackingQuaternion(
                    Quaternion.Euler(
                        rootEuler));

            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            ulong boneMask = 0;

            foreach (var boneCue in
                     cue.Bones ??
                     Array.Empty<
                         ProceduralBoneMotionCue>())
            {
                if (boneCue == null)
                {
                    continue;
                }

                var index =
                    (int)boneCue.Bone;

                if (index < 0 ||
                    index >= bones.Length)
                {
                    continue;
                }

                var boneProgress =
                    Evaluate(
                        boneCue.ProgressCurve,
                        normalizedTime);

                var position =
                    boneCue.LocalPositionOffset *
                    boneProgress;

                var euler =
                    boneCue.LocalEulerDegrees *
                    boneProgress;

                bones[index] =
                    new NormalizedBonePose(
                        ToTrackingVector3(
                            position),
                        ToTrackingQuaternion(
                            Quaternion.Euler(
                                euler)));
                boneMask |=
                    HumanoidPoseState.BoneBit(
                        (HumanoidBoneId)index);
            }

            return new HumanoidPoseState(
                cue.PoseSpace,
                ToTrackingVector3(
                    rootPosition),
                rootRotation,
                bones,
                boneMask);
        }

        private static float Evaluate(
            AnimationCurve curve,
            float normalizedTime)
        {
            return
                curve != null
                    ? curve.Evaluate(
                        normalizedTime)
                    : normalizedTime;
        }

        private static TrackingVector3
            ToTrackingVector3(
                Vector3 value)
        {
            return new TrackingVector3(
                value.x,
                value.y,
                value.z);
        }

        private static TrackingQuaternion
            ToTrackingQuaternion(
                Quaternion value)
        {
            return new TrackingQuaternion(
                value.x,
                value.y,
                value.z,
                value.w);
        }

        private bool Fail(
            string error)
        {
            _failureCount++;
            _lastError = error;
            return false;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "motion.cue.playing",
                    _playing
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "motion.cue.plays",
                    _playCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.cue.releases",
                    _releaseCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.cue.samples",
                    _sampleCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.cue.failures",
                    _failureCount,
                    "count"));
        }
    }
}
