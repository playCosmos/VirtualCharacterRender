using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Editor.P1;
using VCR.Editor.P2;

namespace VCR.Editor.P3
{
    public static class P3BatchValidation
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

                EditorApplication.Exit(
                    p0Passed &&
                    p1Passed &&
                    p2Passed &&
                    p3Passed
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P3 batch validation: unexpected exception.");
                Debug.LogException(
                    exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
