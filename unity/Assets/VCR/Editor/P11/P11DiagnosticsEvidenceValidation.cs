using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Diagnostics;

namespace VCR.Editor.P11
{
    internal static class P11DiagnosticsEvidenceValidation
    {
        public static void RunChecks(
            List<string> failures)
        {
            var snapshot =
                new RuntimeDiagnosticsSnapshot(
                    7,
                    12.5,
                    8.1f,
                    12.2f,
                    15.4f,
                    60.0,
                    30.0,
                    0.0,
                    60.0,
                    10.0,
                    20.0,
                    double.NaN,
                    8.0,
                    null,
                    new[]
                    {
                        new RuntimeMetric(
                            "appearance.transition.progress",
                            0.5,
                            "ratio"),
                        new RuntimeMetric(
                            "motion.baked.playing",
                            1.0,
                            "bool")
                    });

            var document =
                RuntimeDiagnosticsEvidenceDocument
                    .FromSnapshot(
                        snapshot);
            var json =
                JsonUtility.ToJson(
                    document,
                    prettyPrint:
                        true);
            RuntimeDiagnosticsEvidenceDocument
                roundTrip = null;

            try
            {
                roundTrip =
                    JsonUtility.FromJson<
                        RuntimeDiagnosticsEvidenceDocument>(
                        json);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "diagnostics evidence JSON round trip threw: " +
                    exception.Message);
            }

            Expect(
                roundTrip != null &&
                roundTrip.Version ==
                    RuntimeDiagnosticsEvidenceDocument
                        .CurrentVersion &&
                roundTrip.Sequence ==
                    7 &&
                Math.Abs(
                    roundTrip.FrameP95Ms -
                    12.2f) <
                    0.001f &&
                roundTrip.Metrics != null &&
                roundTrip.Metrics.Length ==
                    2 &&
                roundTrip.Metrics[0].Name ==
                    "appearance.transition.progress" &&
                Math.Abs(
                    roundTrip.Metrics[0].Value -
                    0.5) <
                    0.0001,
                "diagnostics snapshot evidence JSON must preserve version, frame stats, and subsystem metrics",
                failures);

            Expect(
                document.Utc != null &&
                document.OperatingSystem != null &&
                document.Cpu != null &&
                document.Gpu != null &&
                document.UnityVersion != null,
                "diagnostics snapshot evidence must include system/runtime identity fields",
                failures);

            Expect(
                !document.HasPresence &&
                document.PresenceState == null &&
                !document.AnySourceAvailable &&
                !document.SubjectEvidence,
                "diagnostics snapshot evidence must represent absent presence data explicitly",
                failures);
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }
}
