using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Editor.P1;
using VCR.Editor.P2;
using VCR.Editor.P3;
using VCR.Editor.P4;
using VCR.Editor.P5;
using VCR.Editor.P6;

namespace VCR.Editor.P7
{
    public static class P7BatchValidation
    {
        public static void RunSourceFreeAndExit()
        {
            try
            {
                var p0Passed =
                    P0SourceFreeValidationSuite
                        .RunAllChecks();
                var p1Passed =
                    P1RendererCoreValidation
                        .RunChecks();
                var p2Passed =
                    P2MaterialRuntimeValidation
                        .RunChecks();
                var p3Passed =
                    P3BuiltInTrackingValidation
                        .RunChecks();
                var p4Passed =
                    P4TrackingRoutingValidation
                        .RunChecks();
                var p5Passed =
                    P5ExpressionMixerValidation
                        .RunChecks();
                var p6Passed =
                    P6EnvironmentRuntimeValidation
                        .RunChecks();
                var p7Passed =
                    P7ShaderPackageValidation
                        .RunChecks();

                EditorApplication.Exit(
                    p0Passed &&
                    p1Passed &&
                    p2Passed &&
                    p3Passed &&
                    p4Passed &&
                    p5Passed &&
                    p6Passed &&
                    p7Passed
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P7 batch validation: unexpected exception.");
                Debug.LogException(
                    exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
