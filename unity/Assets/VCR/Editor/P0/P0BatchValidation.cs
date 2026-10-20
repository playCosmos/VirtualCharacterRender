using System;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P0
{
    public static class P0BatchValidation
    {
        /// <summary>
        /// Batch-mode entrypoint for:
        ///
        /// Unity -batchmode -nographics -projectPath <repo>/unity \
        ///   -executeMethod VCR.Editor.P0.P0BatchValidation.RunSourceFreeAndExit
        ///
        /// The method exits Unity with code 0 on PASS and 1 on FAIL.
        /// </summary>
        public static void RunSourceFreeAndExit()
        {
            try
            {
                var passed = P0SourceFreeValidationSuite.RunAllChecks();
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P0 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
