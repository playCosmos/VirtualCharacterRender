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
using VCR.Editor.P7;
using VCR.Editor.P8;
using VCR.Editor.P9;
using VCR.Editor.P10;

namespace VCR.Editor.P11
{
    public static class P11BatchValidation
    {
        public static void RunSourceFreeAndExit()
        {
            try
            {
                var p0Passed = P0SourceFreeValidationSuite.RunAllChecks();
                var p1Passed = P1RendererCoreValidation.RunChecks();
                var p2Passed = P2MaterialRuntimeValidation.RunChecks();
                var p3Passed = P3BuiltInTrackingValidation.RunChecks();
                var p4Passed = P4TrackingRoutingValidation.RunChecks();
                var p5Passed = P5ExpressionMixerValidation.RunChecks();
                var p6Passed = P6EnvironmentRuntimeValidation.RunChecks();
                var p7Passed = P7ShaderPackageValidation.RunChecks();
                var p8Passed = P8ProtocolEventAdapterValidation.RunChecks();
                var p9Passed = P9EventRuntimeValidation.RunChecks();
                var p10Passed = P10BroadcastOutputValidation.RunChecks();
                var p11Passed = P11ApplicationUiValidation.RunChecks();

                EditorApplication.Exit(
                    p0Passed &&
                    p1Passed &&
                    p2Passed &&
                    p3Passed &&
                    p4Passed &&
                    p5Passed &&
                    p6Passed &&
                    p7Passed &&
                    p8Passed &&
                    p9Passed &&
                    p10Passed &&
                    p11Passed
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P11 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
