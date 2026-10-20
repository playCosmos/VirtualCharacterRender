using System;
using System.Collections.Generic;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Diagnostics
{
    public readonly struct RuntimeDiagnosticsSnapshot
    {
        public RuntimeDiagnosticsSnapshot(
            long sequence,
            double realtimeSeconds,
            float frameAverageMs,
            float frameP95Ms,
            float frameP99Ms,
            double faceHz,
            double bodyHandsHz,
            double fullBodyHz,
            double expressionHz,
            double faceAgeMs,
            double bodyHandsAgeMs,
            double fullBodyAgeMs,
            double expressionAgeMs,
            TrackingPresenceSnapshot? presence,
            RuntimeMetric[] metrics)
        {
            Sequence = sequence;
            RealtimeSeconds = realtimeSeconds;
            FrameAverageMs = frameAverageMs;
            FrameP95Ms = frameP95Ms;
            FrameP99Ms = frameP99Ms;
            FaceHz = faceHz;
            BodyHandsHz = bodyHandsHz;
            FullBodyHz = fullBodyHz;
            ExpressionHz = expressionHz;
            FaceAgeMs = faceAgeMs;
            BodyHandsAgeMs = bodyHandsAgeMs;
            FullBodyAgeMs = fullBodyAgeMs;
            ExpressionAgeMs = expressionAgeMs;
            Presence = presence;
            Metrics =
                Array.AsReadOnly(
                    metrics ??
                    Array.Empty<RuntimeMetric>());
        }

        public long Sequence { get; }
        public double RealtimeSeconds { get; }

        public float FrameAverageMs { get; }
        public float FrameP95Ms { get; }
        public float FrameP99Ms { get; }

        public double FaceHz { get; }
        public double BodyHandsHz { get; }
        public double FullBodyHz { get; }
        public double ExpressionHz { get; }

        public double FaceAgeMs { get; }
        public double BodyHandsAgeMs { get; }
        public double FullBodyAgeMs { get; }
        public double ExpressionAgeMs { get; }

        public TrackingPresenceSnapshot? Presence { get; }
        public IReadOnlyList<RuntimeMetric> Metrics { get; }
    }
}
