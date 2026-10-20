using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P11;

namespace VCR.Editor.P12
{
    public static class P12BatchValidation
    {
        public static bool RunChecks()
        {
            var inheritedPassed =
                P11BatchValidation.RunChecks();
            var p12Passed =
                P12SourceValidation.RunChecks();

            return
                inheritedPassed &&
                p12Passed;
        }

        public static void RunSourceFreeAndExit()
        {
            try
            {
                EditorApplication.Exit(
                    RunChecks()
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P12 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
