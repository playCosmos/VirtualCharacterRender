using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Editor.P12;

namespace VCR.Editor.P13
{
    public static class P13BatchValidation
    {
        [Serializable]
        private sealed class SourceFreeEvidence
        {
            public string suite;
            public string unityVersion;
            public bool passed;
            public string timestampUtc;
        }

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
            var passed = false;
            try
            {
                passed = RunChecks();
                WriteSourceFreeEvidence(passed);
                Debug.Log(
                    passed
                        ? "VCR P0-P13 Unity source-free validation: PASS."
                        : "VCR P0-P13 Unity source-free validation: FAIL.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P13 batch validation: unexpected exception.");
                Debug.LogException(exception);
                try
                {
                    WriteSourceFreeEvidence(false);
                }
                catch (Exception evidenceException)
                {
                    Debug.LogException(evidenceException);
                }

                passed = false;
            }

            EditorApplication.Exit(passed ? 0 : 1);
        }

        private static void WriteSourceFreeEvidence(bool passed)
        {
            var projectRoot = Directory.GetParent(
                Application.dataPath);
            var repositoryRoot = projectRoot == null
                ? null
                : Directory.GetParent(projectRoot.FullName);
            if (repositoryRoot == null)
            {
                throw new InvalidOperationException(
                    "Could not locate repository root for validation evidence.");
            }

            var folder = Path.Combine(
                repositoryRoot.FullName, "Builds", "Validation");
            Directory.CreateDirectory(folder);

            var evidence = new SourceFreeEvidence
            {
                suite = "P0-P13",
                unityVersion = Application.unityVersion,
                passed = passed,
                timestampUtc = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(
                Path.Combine(folder, "p0-p13-source-free.json"),
                JsonUtility.ToJson(evidence, true));
        }
    }
}
