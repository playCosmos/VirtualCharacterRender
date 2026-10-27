using System;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P0;

namespace VCR.Editor.P1
{
    public static class P1BatchValidation
    {
        // Generates the missing P1 scene in a clean batch workspace, then validates
        // the installed UI Toolkit shell. Never overwrites an existing scene.
        public static void GenerateWorkspaceAndExit()
        {
            try
            {
                if (System.IO.File.Exists(P1ApplicationSceneBuilder.ScenePath))
                {
                    Debug.LogError("VCR P1: refusing to regenerate an existing scene.");
                    EditorApplication.Exit(1);
                    return;
                }

                P1ApplicationSceneBuilder.CreateApplicationRuntimeScene();
                var passed =
                    System.IO.File.Exists(P1ApplicationSceneBuilder.ScenePath) &&
                    P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene();
                Debug.Log(passed
                    ? "VCR P1 workspace generation: PASS."
                    : "VCR P1 workspace generation: FAIL.");
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace generation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // Invoke with -batchmode -executeMethod VCR.Editor.P1.P1BatchValidation.RunWorkspaceAndExit.
        // This checks the actual P1 scene and upgrades legacy scenes before a player build.
        // RunSourceFreeAndExit remains usable without a generated scene asset.
        public static void RunWorkspaceAndExit()
        {
            try
            {
                var passed = P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene();
                Debug.Log(passed
                    ? "VCR P1 workspace scene validation: PASS."
                    : "VCR P1 workspace scene validation: FAIL.");
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace scene validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

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

                EditorApplication.Exit(
                    p0Passed && p1Passed
                        ? 0
                        : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P1 batch validation: unexpected exception.");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
