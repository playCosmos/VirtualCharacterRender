using System;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Diagnostics
{
    [Serializable]
    public sealed class RuntimeDiagnosticsMetricEvidence
    {
        public string Name;
        public double Value;
        public string Unit;
    }

    [Serializable]
    public sealed class RuntimeDiagnosticsEvidenceDocument
    {
        public const int CurrentVersion = 1;

        public int Version =
            CurrentVersion;
        public string Utc;
        public string OperatingSystem;
        public string Cpu;
        public string Gpu;
        public string UnityVersion;
        public long Sequence;
        public double RealtimeSeconds;
        public float FrameAverageMs;
        public float FrameP95Ms;
        public float FrameP99Ms;
        public double FaceHz;
        public double BodyHandsHz;
        public double FullBodyHz;
        public double ExpressionHz;
        public double FaceAgeMs;
        public double BodyHandsAgeMs;
        public double FullBodyAgeMs;
        public double ExpressionAgeMs;
        public bool HasPresence;
        public string PresenceState;
        public bool AnySourceAvailable;
        public bool SubjectEvidence;
        public RuntimeDiagnosticsMetricEvidence[] Metrics =
            Array.Empty<RuntimeDiagnosticsMetricEvidence>();

        public static RuntimeDiagnosticsEvidenceDocument
            FromSnapshot(
                RuntimeDiagnosticsSnapshot snapshot)
        {
            var sourceMetrics =
                snapshot.Metrics ??
                Array.Empty<RuntimeMetric>();
            var metrics =
                new RuntimeDiagnosticsMetricEvidence[
                    sourceMetrics.Length];

            for (var i = 0;
                 i < sourceMetrics.Length;
                 i++)
            {
                metrics[i] =
                    new RuntimeDiagnosticsMetricEvidence
                    {
                        Name =
                            sourceMetrics[i].Name,
                        Value =
                            sourceMetrics[i].Value,
                        Unit =
                            sourceMetrics[i].Unit
                    };
            }

            var presence =
                snapshot.Presence;

            return new RuntimeDiagnosticsEvidenceDocument
            {
                Utc =
                    DateTime.UtcNow.ToString(
                        "O"),
                OperatingSystem =
                    UnityEngine.SystemInfo
                        .operatingSystem,
                Cpu =
                    UnityEngine.SystemInfo
                        .processorType,
                Gpu =
                    UnityEngine.SystemInfo
                        .graphicsDeviceName,
                UnityVersion =
                    UnityEngine.Application
                        .unityVersion,
                Sequence =
                    snapshot.Sequence,
                RealtimeSeconds =
                    snapshot.RealtimeSeconds,
                FrameAverageMs =
                    snapshot.FrameAverageMs,
                FrameP95Ms =
                    snapshot.FrameP95Ms,
                FrameP99Ms =
                    snapshot.FrameP99Ms,
                FaceHz =
                    snapshot.FaceHz,
                BodyHandsHz =
                    snapshot.BodyHandsHz,
                FullBodyHz =
                    snapshot.FullBodyHz,
                ExpressionHz =
                    snapshot.ExpressionHz,
                FaceAgeMs =
                    snapshot.FaceAgeMs,
                BodyHandsAgeMs =
                    snapshot.BodyHandsAgeMs,
                FullBodyAgeMs =
                    snapshot.FullBodyAgeMs,
                ExpressionAgeMs =
                    snapshot.ExpressionAgeMs,
                HasPresence =
                    presence.HasValue,
                PresenceState =
                    presence.HasValue
                        ? presence.Value
                            .SubjectState
                            .ToString()
                        : null,
                AnySourceAvailable =
                    presence.HasValue &&
                    presence.Value
                        .AnySourceAvailable,
                SubjectEvidence =
                    presence.HasValue &&
                    presence.Value
                        .SubjectEvidence,
                Metrics =
                    metrics
            };
        }
    }
}
