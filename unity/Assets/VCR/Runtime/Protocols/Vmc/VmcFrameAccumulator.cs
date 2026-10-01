using System;
using System.Collections.Generic;
using VCR.Runtime.Protocols.Osc;

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
        private readonly string _sourceId;
        private readonly NormalizedBonePose[] _bones =
            new NormalizedBonePose[(int)HumanoidBoneId.Count];
        private readonly bool[] _hasBone =
            new bool[(int)HumanoidBoneId.Count];

        private readonly float[] _expressionStaging =
            new float[(int)StandardExpression.Count];
        private readonly Dictionary<string, float> _customExpressionStaging =
            new(StringComparer.Ordinal);

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

        public VmcFrameAccumulator(string sourceId)
        {
            _sourceId = string.IsNullOrWhiteSpace(sourceId)
                ? "vmc"
                : sourceId;
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

            var changed = false;
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
                        changed |= ProcessAvailable(message);
                        break;

                    case "/VMC/Ext/T":
                        ProcessTime(message);
                        break;

                    case "/VMC/Ext/Root/Pos":
                        changed |= ProcessRoot(message);
                        break;

                    case "/VMC/Ext/Bone/Pos":
                        changed |= ProcessBone(message);
                        break;

                    case "/VMC/Ext/Blend/Val":
                        ProcessBlendValue(message);
                        break;

                    case "/VMC/Ext/Blend/Apply":
                        expressionApply = true;
                        changed = true;
                        break;
                }
            }

            if (expressionApply)
            {
                _committedExpressions = BuildExpressions();
            }

            if (!changed)
            {
                return false;
            }

            var pose = _hasAnyBone
                ? BuildPose()
                : null;

            var subjectDetected =
                _trackingKnown
                    ? _trackingOk
                    : (_loadedKnown ? _loaded : _hasAnyBone);

            var regions = pose != null
                ? TrackingRegion.FullBody
                : TrackingRegion.None;

            var sourceTimestampUs =
                _senderTimestampUs > 0
                    ? _senderTimestampUs
                    : arrivalTimestampUs;

            frame = new TrackingFrame(
                ++_sequence,
                sourceTimestampUs,
                regions,
                subjectDetected ? 1f : 0f,
                subjectDetected,
                humanoidPose: pose,
                expressions: _committedExpressions,
                sourceId: _sourceId);

            return true;
        }

        private bool ProcessAvailable(OscMessage message)
        {
            var args = message.Arguments;
            if (args.Length < 1 ||
                !args[0].TryGetInt(out var loaded))
            {
                return false;
            }

            var previousLoadedKnown = _loadedKnown;
            var previousLoaded = _loaded;
            var previousTrackingKnown = _trackingKnown;
            var previousTracking = _trackingOk;

            _loadedKnown = true;
            _loaded = loaded != 0;

            if (args.Length >= 4 &&
                args[3].TryGetInt(out var tracking))
            {
                _trackingKnown = true;
                _trackingOk = tracking != 0;
            }

            return
                !previousLoadedKnown ||
                previousLoaded != _loaded ||
                previousTrackingKnown != _trackingKnown ||
                previousTracking != _trackingOk;
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
                _senderTimestampUs =
                    (long)(seconds * 1_000_000.0);
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

            _rootPosition = position;
            _rootRotation = rotation;
            return true;
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

            var index = (int)bone;
            _bones[index] = new NormalizedBonePose(
                position,
                rotation);
            _hasBone[index] = true;
            _hasAnyBone = true;
            return true;
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

            value = Clamp01(value);

            if (StandardExpressionNames.TryParse(
                name,
                out var expression))
            {
                _expressionStaging[(int)expression] = value;
            }
            else if (!string.IsNullOrEmpty(name))
            {
                _customExpressionStaging[name] = value;
            }
        }

        private NormalizedHumanoidPose BuildPose()
        {
            var bones =
                new NormalizedBonePose[_bones.Length];
            var hasBone =
                new bool[_hasBone.Length];

            Array.Copy(_bones, bones, _bones.Length);
            Array.Copy(_hasBone, hasBone, _hasBone.Length);

            return new NormalizedHumanoidPose(
                _rootPosition,
                _rootRotation,
                bones,
                hasBone);
        }

        private NormalizedExpressionState BuildExpressions()
        {
            var standard =
                new float[_expressionStaging.Length];
            Array.Copy(
                _expressionStaging,
                standard,
                _expressionStaging.Length);

            var custom =
                new NamedExpressionValue[
                    _customExpressionStaging.Count];

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
                custom);
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

            position = new TrackingVector3(px, py, pz);
            rotation = NormalizeQuaternion(qx, qy, qz, qw);
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
