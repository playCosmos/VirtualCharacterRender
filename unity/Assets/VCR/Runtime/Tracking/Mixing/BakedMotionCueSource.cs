using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Mixing
{
    /// <summary>
    /// Plays pre-baked AnimationClip-derived pose cues through the same
    /// additive/procedural mixer path as lightweight procedural cues.
    /// AnimationClip and Animator are not sampled during playback.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(5000)]
    public sealed class BakedMotionCueSource :
        MonoBehaviour,
        ITrackingFrameProvider,
        IMotionCueRuntime,
        IRuntimeMetricsSource
    {
        [SerializeField] private string runtimeId =
            "motion.clips";
        [SerializeField] private MotionExpressionMixer mixer;
        [SerializeField] private bool autoFindMixer = true;
        [SerializeField] private BakedMotionCueAsset[] cueAssets =
            Array.Empty<BakedMotionCueAsset>();

        private readonly Dictionary<string, BakedMotionCueDefinition>
            _cues =
                new(StringComparer.Ordinal);
        private readonly List<string> _cueIds =
            new();

        private BakedMotionCueDefinition[] _configuredCues =
            Array.Empty<BakedMotionCueDefinition>();
        private BakedMotionCueDefinition _activeCue;
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

        public IReadOnlyList<BakedMotionCueAsset> CueAssets =>
            cueAssets;

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

        public void ConfigureAssets(
            params BakedMotionCueAsset[] assets)
        {
            cueAssets =
                assets ??
                Array.Empty<BakedMotionCueAsset>();
            _configuredCues =
                Array.Empty<BakedMotionCueDefinition>();

            RebuildCues(
                out _);
        }

        public void ConfigureCues(
            params BakedMotionCueDefinition[] definitions)
        {
            cueAssets =
                Array.Empty<BakedMotionCueAsset>();
            _configuredCues =
                definitions ??
                Array.Empty<BakedMotionCueDefinition>();

            RebuildCues(
                out _);
        }

        public bool TryRegisterAsset(
            BakedMotionCueAsset asset,
            out string error)
        {
            error = null;

            if (asset == null ||
                !ValidateCue(
                    asset.Cue,
                    out error))
            {
                error ??=
                    "Baked motion cue asset is required.";
                return false;
            }

            foreach (var existing in
                     cueAssets ??
                     Array.Empty<
                         BakedMotionCueAsset>())
            {
                if (existing == null)
                {
                    continue;
                }

                if (ReferenceEquals(
                        existing,
                        asset))
                {
                    return RebuildCues(
                        out error);
                }

                if (string.Equals(
                        existing.Cue?.CueId,
                        asset.Cue.CueId,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Baked motion cue id '{asset.Cue.CueId}' is already registered.";
                    return false;
                }
            }

            foreach (var configured in
                     _configuredCues ??
                     Array.Empty<
                         BakedMotionCueDefinition>())
            {
                if (configured != null &&
                    string.Equals(
                        configured.CueId,
                        asset.Cue.CueId,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Baked motion cue id '{asset.Cue.CueId}' conflicts with a runtime-configured cue.";
                    return false;
                }
            }

            var current =
                cueAssets ??
                Array.Empty<
                    BakedMotionCueAsset>();
            var next =
                new BakedMotionCueAsset[
                    current.Length + 1];

            Array.Copy(
                current,
                next,
                current.Length);
            next[next.Length - 1] =
                asset;
            cueAssets =
                next;

            return RebuildCues(
                out error);
        }

        public bool RebuildCues(
            out string error)
        {
            error = null;
            _cues.Clear();
            _cueIds.Clear();

            foreach (var asset in
                     cueAssets ??
                     Array.Empty<BakedMotionCueAsset>())
            {
                if (asset == null)
                {
                    continue;
                }

                if (!TryAddCue(
                        asset.Cue,
                        out error))
                {
                    return false;
                }
            }

            foreach (var cue in
                     _configuredCues ??
                     Array.Empty<BakedMotionCueDefinition>())
            {
                if (!TryAddCue(
                        cue,
                        out error))
                {
                    return false;
                }
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
                    $"Unknown baked motion cue '{cueId ?? "<null>"}'.";
                return Fail(
                    error);
            }

            if (!EnsureRegistered(
                    out error))
            {
                return Fail(
                    error);
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
                    $"Active baked motion cue is '{_activeCue.CueId}', not '{cueId}'.";
                return Fail(
                    error);
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
                    $"Unknown baked motion cue '{cueId ?? "<null>"}'.";
                return Fail(
                    error);
            }

            if (float.IsNaN(
                    normalizedTime) ||
                float.IsInfinity(
                    normalizedTime))
            {
                error =
                    "Baked motion cue sample time must be finite.";
                return Fail(
                    error);
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

        private bool TryAddCue(
            BakedMotionCueDefinition cue,
            out string error)
        {
            error = null;

            if (!ValidateCue(
                    cue,
                    out error))
            {
                return Fail(
                    error);
            }

            if (!_cues.TryAdd(
                    cue.CueId,
                    cue))
            {
                error =
                    $"Duplicate baked motion cue id '{cue.CueId}'.";
                return Fail(
                    error);
            }

            _cueIds.Add(
                cue.CueId);
            return true;
        }

        private static bool ValidateCue(
            BakedMotionCueDefinition cue,
            out string error)
        {
            error = null;

            if (cue == null ||
                string.IsNullOrWhiteSpace(
                    cue.CueId))
            {
                error =
                    "Every baked motion cue requires a non-empty id.";
                return false;
            }

            if (float.IsNaN(
                    cue.DurationSeconds) ||
                float.IsInfinity(
                    cue.DurationSeconds) ||
                cue.DurationSeconds <= 0f)
            {
                error =
                    $"Baked motion cue '{cue.CueId}' requires a finite positive duration.";
                return false;
            }

            if (cue.FrameCount < 2)
            {
                error =
                    $"Baked motion cue '{cue.CueId}' requires at least two frames.";
                return false;
            }

            if (cue.RootPositionOffsets == null ||
                cue.RootPositionOffsets.Length !=
                    cue.FrameCount ||
                cue.RootRotationOffsets == null ||
                cue.RootRotationOffsets.Length !=
                    cue.FrameCount)
            {
                error =
                    $"Baked motion cue '{cue.CueId}' root tracks must match FrameCount.";
                return false;
            }

            var bones =
                new HashSet<HumanoidBoneId>();

            foreach (var track in
                     cue.Bones ??
                     Array.Empty<
                         BakedBoneMotionCueTrack>())
            {
                if (track == null ||
                    !bones.Add(
                        track.Bone))
                {
                    error =
                        $"Baked motion cue '{cue.CueId}' contains a null or duplicate bone track.";
                    return false;
                }

                if (track.LocalPositionOffsets == null ||
                    track.LocalPositionOffsets.Length !=
                        cue.FrameCount ||
                    track.LocalRotationOffsets == null ||
                    track.LocalRotationOffsets.Length !=
                        cue.FrameCount)
                {
                    error =
                        $"Baked motion cue '{cue.CueId}' bone '{track.Bone}' tracks must match FrameCount.";
                    return false;
                }
            }

            return true;
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
                    "No MotionExpressionMixer is available for the baked motion cue source.";
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
            BakedMotionCueDefinition cue,
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
            BakedMotionCueDefinition cue,
            float normalizedTime)
        {
            ResolveFramePair(
                cue.FrameCount,
                normalizedTime,
                out var first,
                out var second,
                out var blend);

            var rootPosition =
                Vector3.Lerp(
                    cue.RootPositionOffsets[first],
                    cue.RootPositionOffsets[second],
                    blend);
            var rootRotation =
                Quaternion.Slerp(
                    cue.RootRotationOffsets[first],
                    cue.RootRotationOffsets[second],
                    blend);

            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var hasBone =
                new bool[
                    (int)HumanoidBoneId.Count];

            foreach (var track in
                     cue.Bones ??
                     Array.Empty<
                         BakedBoneMotionCueTrack>())
            {
                if (track == null)
                {
                    continue;
                }

                var index =
                    (int)track.Bone;

                if (index < 0 ||
                    index >= bones.Length)
                {
                    continue;
                }

                var position =
                    Vector3.Lerp(
                        track.LocalPositionOffsets[
                            first],
                        track.LocalPositionOffsets[
                            second],
                        blend);
                var rotation =
                    Quaternion.Slerp(
                        track.LocalRotationOffsets[
                            first],
                        track.LocalRotationOffsets[
                            second],
                        blend);

                bones[index] =
                    new NormalizedBonePose(
                        ToTrackingVector3(
                            position),
                        ToTrackingQuaternion(
                            rotation));
                hasBone[index] =
                    true;
            }

            return new HumanoidPoseState(
                cue.PoseSpace,
                ToTrackingVector3(
                    rootPosition),
                ToTrackingQuaternion(
                    rootRotation),
                bones,
                hasBone);
        }

        private static void ResolveFramePair(
            int frameCount,
            float normalizedTime,
            out int first,
            out int second,
            out float blend)
        {
            var position =
                Mathf.Clamp01(
                    normalizedTime) *
                (frameCount - 1);
            first =
                Mathf.Clamp(
                    Mathf.FloorToInt(
                        position),
                    0,
                    frameCount - 1);
            second =
                Mathf.Min(
                    first + 1,
                    frameCount - 1);
            blend =
                second == first
                    ? 0f
                    : position - first;
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
                    "motion.baked.playing",
                    _playing
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "motion.baked.plays",
                    _playCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.baked.releases",
                    _releaseCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.baked.samples",
                    _sampleCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "motion.baked.failures",
                    _failureCount,
                    "count"));
        }
    }
}
