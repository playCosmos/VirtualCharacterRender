using System;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Mixing
{
    /// <summary>
    /// Explicit/manual expression-only layer for UI and event-driven control.
    ///
    /// The source has no Update loop and never contributes subject-presence
    /// evidence. A new frame is published only when an expression value
    /// actually changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManualExpressionLayerSource :
        MonoBehaviour,
        ITrackingFrameProvider
    {
        [SerializeField]
        private string sourceId =
            "manual-expression-layer";

        private readonly float[] _values =
            new float[
                (int)StandardExpression.Count];

        private TrackingFrame _latest;
        private long _sequence;

        public string SourceId => sourceId;
        public long Sequence => _sequence;

        public bool SetExpression(
            StandardExpression expression,
            float value)
        {
            var index =
                (int)expression;

            if (index < 0 ||
                index >=
                    (int)StandardExpression.Count ||
                float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                return false;
            }

            var normalized =
                Mathf.Clamp01(value);

            if (_latest != null &&
                Mathf.Abs(
                    _values[index] -
                    normalized) <=
                0.000001f)
            {
                return true;
            }

            _values[index] =
                normalized;

            Publish();
            return true;
        }

        public bool ClearExpression(
            StandardExpression expression)
        {
            return SetExpression(
                expression,
                0f);
        }

        public void ClearAll()
        {
            var changed = false;

            for (var i = 0;
                 i < _values.Length;
                 i++)
            {
                if (Mathf.Abs(
                        _values[i]) <=
                    0.000001f)
                {
                    continue;
                }

                _values[i] = 0f;
                changed = true;
            }

            if (changed ||
                _latest == null)
            {
                Publish();
            }
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
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame =
                isActiveAndEnabled
                    ? _latest
                    : null;

            return frame != null;
        }

        private void Publish()
        {
            var nowUs =
                MonotonicClock
                    .NowMicroseconds();

            var snapshot =
                new float[_values.Length];

            Array.Copy(
                _values,
                snapshot,
                _values.Length);

            _latest =
                new TrackingFrame(
                    ++_sequence,
                    sourceTimestampUs:
                        nowUs,
                    validRegions:
                        TrackingRegion.Expressions,
                    confidence:
                        1f,
                    subjectDetected:
                        false,
                    expressions:
                        new NormalizedExpressionState(
                            snapshot),
                    sourceId:
                        string.IsNullOrWhiteSpace(
                            sourceId)
                            ? "manual-expression-layer"
                            : sourceId,
                    runtimeTimestampUs:
                        nowUs);
        }
    }
}
