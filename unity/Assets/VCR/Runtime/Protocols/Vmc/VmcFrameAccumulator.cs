using System;
using System.Collections.Generic;
using System.Text;
using VCR.Runtime.Core;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Protocols.Vmc
{
    /// <summary>
    /// Stateful VMC message accumulator.
    ///
    /// Bone transforms are VMC/Unity local transforms and already match VCR's
    /// +X right, +Y up, +Z forward normalized convention.
    /// </summary>
    public sealed class VmcFrameAccumulator
    {
        private readonly struct CustomExpressionWireName
        {
            public CustomExpressionWireName(
                byte[] utf8,
                string name)
            {
                Utf8 = utf8;
                Name = name;
            }

            public byte[] Utf8 { get; }
            public string Name { get; }
        }

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
        public const int MaxCustomExpressions =
            256;
        public const int MaxCustomExpressionNameCharacters =
            256;
        private readonly string _sourceId;
        private readonly HumanoidPoseSpace _poseSpace;
        private readonly NormalizedBonePose[] _bones =
            new NormalizedBonePose[(int)HumanoidBoneId.Count];
        private ulong _boneMask;

        private readonly float[] _expressionStaging =
            new float[(int)StandardExpression.Count];
        private readonly Dictionary<string, float> _customExpressionStaging =
            new(StringComparer.Ordinal);
        private readonly Dictionary<uint, CustomExpressionWireName>
            _customExpressionWireNames =
                new();

        private TrackingVector3 _rootPosition = TrackingVector3.Zero;
        private TrackingQuaternion _rootRotation = TrackingQuaternion.Identity;

        private NormalizedExpressionState _committedExpressions;

        private bool _loadedKnown;
        private bool _loaded;
        private bool _trackingKnown;
        private bool _trackingOk;
        private bool _hasAnyBone;
        private long _sequence;
        private long _senderTimestampUs;
        private long _droppedCustomExpressionCount;

        public int CustomExpressionCount =>
            _customExpressionStaging.Count;

        public long DroppedCustomExpressionCount =>
            _droppedCustomExpressionCount;

        public VmcFrameAccumulator(
            string sourceId,
            HumanoidPoseSpace poseSpace = HumanoidPoseSpace.OriginalLocal)
        {
            _sourceId = string.IsNullOrWhiteSpace(sourceId)
                ? "vmc"
                : sourceId;
            _poseSpace = poseSpace;
        }

        public void ResetState()
        {
            Array.Clear(
                _bones,
                0,
                _bones.Length);
            _boneMask = 0;
            Array.Clear(
                _expressionStaging,
                0,
                _expressionStaging.Length);
            _customExpressionStaging.Clear();
            _customExpressionWireNames.Clear();

            _rootPosition =
                TrackingVector3.Zero;
            _rootRotation =
                TrackingQuaternion.Identity;
            _committedExpressions = null;
            _loadedKnown = false;
            _loaded = false;
            _trackingKnown = false;
            _trackingOk = false;
            _hasAnyBone = false;
            _senderTimestampUs = 0;
        }

        public bool Process(
            IReadOnlyList<OscMessage> messages,
            long arrivalTimestampUs,
            out TrackingFrame frame)
        {
            frame = null;
            if (messages == null || messages.Count == 0)
            {
                return false;
            }

            var stateChanged = false;
            var poseChanged = false;
            var expressionApply = false;

            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                switch (message.Address)
                {
                    case "/VMC/Ext/OK":
                        stateChanged |= ProcessAvailable(message);
                        break;

                    case "/VMC/Ext/T":
                        ProcessTime(message);
                        break;

                    case "/VMC/Ext/Root/Pos":
                        poseChanged |= ProcessRoot(message);
                        break;

                    case "/VMC/Ext/Bone/Pos":
                        poseChanged |= ProcessBone(message);
                        break;

                    case "/VMC/Ext/Blend/Val":
                        ProcessBlendValue(message);
                        break;

                    case "/VMC/Ext/Blend/Apply":
                        expressionApply = true;
                        break;
                }
            }

            frame =
                CompletePacket(
                    stateChanged,
                    poseChanged,
                    expressionApply,
                    arrivalTimestampUs);

            return frame != null;
        }

        private bool ProcessAvailable(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length < 1 ||
                !args[0].TryGetInt(out var loaded))
            {
                return false;
            }

            var tracking = 0;
            var hasTracking =
                args.Length >= 4 &&
                args[3].TryGetInt(
                    out tracking);

            return ApplyAvailable(
                loaded,
                hasTracking,
                tracking);
        }

        private void ProcessTime(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length >= 1 &&
                args[0].TryGetFloat(out var seconds) &&
                seconds >= 0f &&
                !float.IsNaN(seconds) &&
                !float.IsInfinity(seconds))
            {
                ApplyTime(
                    seconds);
            }
        }

        private bool ProcessRoot(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length < 8 ||
                !args[0].TryGetString(out _))
            {
                return false;
            }

            if (!TryTransform(
                args,
                startIndex: 1,
                out var position,
                out var rotation))
            {
                return false;
            }

            return ApplyRoot(
                position,
                rotation);
        }

        private bool ProcessBone(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length < 8 ||
                !args[0].TryGetString(out var boneName) ||
                !HumanoidBoneNames.TryParse(
                    boneName,
                    out var bone) ||
                !TryTransform(
                    args,
                    startIndex: 1,
                    out var position,
                    out var rotation))
            {
                return false;
            }

            return ApplyBone(
                bone,
                position,
                rotation);
        }

        private void ProcessBlendValue(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length < 2 ||
                !args[0].TryGetString(out var name) ||
                !args[1].TryGetFloat(out var value))
            {
                return;
            }

            if (StandardExpressionNames.TryParse(
                    name,
                    out var expression))
            {
                ApplyStandardBlend(
                    expression,
                    value);
                return;
            }

            ApplyCustomBlend(
                name,
                value);
        }

        internal bool ApplyAvailable(
            int loaded,
            bool hasTracking,
            int tracking)
        {
            var previousLoadedKnown =
                _loadedKnown;
            var previousLoaded =
                _loaded;
            var previousTrackingKnown =
                _trackingKnown;
            var previousTracking =
                _trackingOk;

            _loadedKnown = true;
            _loaded =
                loaded != 0;

            if (hasTracking)
            {
                _trackingKnown = true;
                _trackingOk =
                    tracking != 0;
            }

            return
                !previousLoadedKnown ||
                previousLoaded != _loaded ||
                previousTrackingKnown !=
                    _trackingKnown ||
                previousTracking !=
                    _trackingOk;
        }

        internal void ApplyTime(
            float seconds)
        {
            if (seconds < 0f ||
                float.IsNaN(seconds) ||
                float.IsInfinity(seconds))
            {
                return;
            }

            _senderTimestampUs =
                (long)(
                    seconds *
                    1_000_000.0);
        }

        internal bool ApplyRoot(
            float px,
            float py,
            float pz,
            float qx,
            float qy,
            float qz,
            float qw)
        {
            if (!TryCreateTransform(
                    px,
                    py,
                    pz,
                    qx,
                    qy,
                    qz,
                    qw,
                    out var position,
                    out var rotation))
            {
                return false;
            }

            return ApplyRoot(
                position,
                rotation);
        }

        internal bool ApplyRoot(
            TrackingVector3 position,
            TrackingQuaternion rotation)
        {
            _rootPosition =
                position;
            _rootRotation =
                rotation;
            return true;
        }

        internal bool ApplyBone(
            HumanoidBoneId bone,
            float px,
            float py,
            float pz,
            float qx,
            float qy,
            float qz,
            float qw)
        {
            if (!TryCreateTransform(
                    px,
                    py,
                    pz,
                    qx,
                    qy,
                    qz,
                    qw,
                    out var position,
                    out var rotation))
            {
                return false;
            }

            return ApplyBone(
                bone,
                position,
                rotation);
        }

        internal bool ApplyBone(
            HumanoidBoneId bone,
            TrackingVector3 position,
            TrackingQuaternion rotation)
        {
            var index =
                (int)bone;

            if (index < 0 ||
                index >= _bones.Length)
            {
                return false;
            }

            _bones[index] =
                new NormalizedBonePose(
                    position,
                    rotation);
            _boneMask |=
                HumanoidPoseState.BoneBit(
                    bone);
            _hasAnyBone =
                true;
            return true;
        }

        internal void ApplyStandardBlend(
            StandardExpression expression,
            float value)
        {
            var index =
                (int)expression;

            if (index < 0 ||
                index >=
                    _expressionStaging.Length)
            {
                return;
            }

            _expressionStaging[index] =
                Clamp01(
                    value);
        }

        internal void ApplyCustomBlend(
            string name,
            float value)
        {
            value =
                Clamp01(
                    value);

            if (string.IsNullOrWhiteSpace(
                    name) ||
                name.Length >
                    MaxCustomExpressionNameCharacters)
            {
                _droppedCustomExpressionCount++;
                return;
            }

            if (_customExpressionStaging
                .ContainsKey(
                    name))
            {
                _customExpressionStaging[
                    name] =
                    value;
                return;
            }

            if (_customExpressionStaging.Count >=
                MaxCustomExpressions)
            {
                _droppedCustomExpressionCount++;
                return;
            }

            _customExpressionStaging.Add(
                name,
                value);
        }

        internal void ApplyCustomBlendUtf8(
            byte[] data,
            int start,
            int length,
            float value)
        {
            if (data == null ||
                start < 0 ||
                length <= 0 ||
                start + length >
                    data.Length)
            {
                _droppedCustomExpressionCount++;
                return;
            }

            var characterCount =
                StrictUtf8.GetCharCount(
                    data,
                    start,
                    length);

            if (characterCount <= 0 ||
                characterCount >
                    MaxCustomExpressionNameCharacters)
            {
                _droppedCustomExpressionCount++;
                return;
            }

            var bytes =
                data.AsSpan(
                    start,
                    length);
            var hash =
                ComputeUtf8Hash(
                    bytes);

            if (_customExpressionWireNames
                .TryGetValue(
                    hash,
                    out var cached) &&
                bytes.SequenceEqual(
                    cached.Utf8))
            {
                ApplyCustomBlend(
                    cached.Name,
                    value);
                return;
            }

            var name =
                StrictUtf8.GetString(
                    data,
                    start,
                    length);

            ApplyCustomBlend(
                name,
                value);

            if (!_customExpressionStaging
                    .ContainsKey(
                        name) ||
                _customExpressionWireNames
                    .ContainsKey(
                        hash))
            {
                // The name was rejected, or this hash already belongs to a
                // different valid UTF-8 spelling. Hash collisions fall back to
                // decoding rather than risking aliasing two custom channels.
                return;
            }

            var ownedBytes =
                new byte[length];
            Buffer.BlockCopy(
                data,
                start,
                ownedBytes,
                0,
                length);

            _customExpressionWireNames.Add(
                hash,
                new CustomExpressionWireName(
                    ownedBytes,
                    name));
        }

        private static uint ComputeUtf8Hash(
            ReadOnlySpan<byte> value)
        {
            unchecked
            {
                var hash =
                    2166136261u;

                for (var i = 0;
                     i < value.Length;
                     i++)
                {
                    hash ^=
                        value[i];
                    hash *=
                        16777619u;
                }

                return hash;
            }
        }

        internal TrackingFrame CompletePacket(
            bool stateChanged,
            bool poseChanged,
            bool expressionApply,
            long arrivalTimestampUs)
        {
            if (expressionApply)
            {
                _committedExpressions =
                    BuildExpressions();
            }

            if (!stateChanged &&
                !poseChanged &&
                !expressionApply)
            {
                return null;
            }

            // Do not refresh an old pose merely because a heartbeat or
            // expression packet arrived. Pose freshness is a separate domain.
            var pose =
                poseChanged &&
                _hasAnyBone
                    ? BuildPose()
                    : null;

            var expressions =
                expressionApply
                    ? _committedExpressions
                    : null;

            var subjectDetected =
                _trackingKnown
                    ? _trackingOk
                    : (_loadedKnown
                        ? _loaded
                        : _hasAnyBone);

            var regions =
                pose != null
                    ? TrackingRegion.FullBody
                    : TrackingRegion.None;

            var sourceTimestampUs =
                _senderTimestampUs > 0
                    ? _senderTimestampUs
                    : arrivalTimestampUs;

            return new TrackingFrame(
                ++_sequence,
                sourceTimestampUs,
                regions,
                subjectDetected
                    ? 1f
                    : 0f,
                subjectDetected,
                humanoidPose: pose,
                expressions: expressions,
                sourceId: _sourceId,
                runtimeTimestampUs:
                    MonotonicClock
                        .NowMicroseconds());
        }

        private HumanoidPoseState BuildPose()
        {
            var bones =
                new NormalizedBonePose[_bones.Length];

            Array.Copy(
                _bones,
                bones,
                _bones.Length);

            return new HumanoidPoseState(
                _poseSpace,
                _rootPosition,
                _rootRotation,
                bones,
                _boneMask,
                SnapshotArrayOwnership.Transfer);
        }

        private NormalizedExpressionState BuildExpressions()
        {
            var standard =
                new float[_expressionStaging.Length];
            Array.Copy(
                _expressionStaging,
                standard,
                _expressionStaging.Length);

            var customCount =
                _customExpressionStaging.Count;
            var custom =
                customCount == 0
                    ? Array.Empty<
                        NamedExpressionValue>()
                    : new NamedExpressionValue[
                        customCount];

            var index = 0;
            foreach (var pair in _customExpressionStaging)
            {
                custom[index++] =
                    new NamedExpressionValue(
                        pair.Key,
                        pair.Value);
            }

            return new NormalizedExpressionState(
                standard,
                custom,
                SnapshotArrayOwnership.Transfer);
        }

        private static bool TryTransform(
            OscArgument[] args,
            int startIndex,
            out TrackingVector3 position,
            out TrackingQuaternion rotation)
        {
            position = TrackingVector3.Zero;
            rotation = TrackingQuaternion.Identity;

            if (args.Length < startIndex + 7 ||
                !args[startIndex + 0].TryGetFloat(out var px) ||
                !args[startIndex + 1].TryGetFloat(out var py) ||
                !args[startIndex + 2].TryGetFloat(out var pz) ||
                !args[startIndex + 3].TryGetFloat(out var qx) ||
                !args[startIndex + 4].TryGetFloat(out var qy) ||
                !args[startIndex + 5].TryGetFloat(out var qz) ||
                !args[startIndex + 6].TryGetFloat(out var qw))
            {
                return false;
            }

            return TryCreateTransform(
                px,
                py,
                pz,
                qx,
                qy,
                qz,
                qw,
                out position,
                out rotation);
        }

        private static bool TryCreateTransform(
            float px,
            float py,
            float pz,
            float qx,
            float qy,
            float qz,
            float qw,
            out TrackingVector3 position,
            out TrackingQuaternion rotation)
        {
            position =
                TrackingVector3.Zero;
            rotation =
                TrackingQuaternion.Identity;

            if (!IsFinite(px) ||
                !IsFinite(py) ||
                !IsFinite(pz) ||
                !IsFinite(qx) ||
                !IsFinite(qy) ||
                !IsFinite(qz) ||
                !IsFinite(qw))
            {
                return false;
            }

            position =
                new TrackingVector3(
                    px,
                    py,
                    pz);
            rotation =
                NormalizeQuaternion(
                    qx,
                    qy,
                    qz,
                    qw);
            return true;
        }

        private static TrackingQuaternion NormalizeQuaternion(
            float x,
            float y,
            float z,
            float w)
        {
            var lengthSquared =
                x * x + y * y + z * z + w * w;

            if (lengthSquared < 1e-12f)
            {
                return TrackingQuaternion.Identity;
            }

            var inverse =
                1f / (float)Math.Sqrt(lengthSquared);

            return new TrackingQuaternion(
                x * inverse,
                y * inverse,
                z * inverse,
                w * inverse);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
