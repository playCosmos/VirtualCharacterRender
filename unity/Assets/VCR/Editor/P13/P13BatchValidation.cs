using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P12;

namespace VCR.Editor.P13
{
    public static class P13BatchValidation
    {
        public static bool RunChecks()
        {
            var inheritedPassed =
                P12BatchValidation.RunChecks();
            var p13Passed =
                P13SourceValidation.RunChecks();

            return
                inheritedPassed &&
                p13Passed;
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
                    "VCR P13 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
