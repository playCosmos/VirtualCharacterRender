using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P0
{
    public static class P0SourceFreeValidationSuite
    {
        [MenuItem("VCR/P0/Run All Source-Free Checks", priority = 0)]
        public static void RunAll()
        {
            var failures = new List<string>();
            var currentCheck = string.Empty;

            void Capture(
                string condition,
                string stackTrace,
                LogType type)
            {
                if (type != LogType.Error &&
                    type != LogType.Exception &&
                    type != LogType.Assert)
                {
                    return;
                }

                if (!condition.StartsWith(
                    "VCR P0",
                    StringComparison.Ordinal))
                {
                    return;
                }

                failures.Add(
                    string.IsNullOrEmpty(currentCheck)
                        ? condition
                        : $"{currentCheck}: {condition}");
            }

            Application.logMessageReceived += Capture;

            try
            {
                Run(
                    "Package baseline",
                    P0PackageBaselineCheck.Validate,
                    ref currentCheck);

                Run(
                    "Presence resolver",
                    P0PresenceValidation.Validate,
                    ref currentCheck);

                Run(
                    "iFacialMocap parser",
                    P0ArKitSetupMenu.ValidateParser,
                    ref currentCheck);

                Run(
                    "OSC/VMC codec",
                    P0VmcSetupMenu.ValidateCodec,
                    ref currentCheck);

                Run(
                    "Diagnostics math",
                    P0DiagnosticsValidation.Validate,
                    ref currentCheck);

                Run(
                    "Material override",
                    P0MaterialOverrideMenu.Validate,
                    ref currentCheck);

                Run(
                    "Normalized events",
                    P0EventRuntimeMenu.Validate,
                    ref currentCheck);

                Run(
                    "Environment runtime",
                    P0EnvironmentMenu.Validate,
                    ref currentCheck);

                Run(
                    "Lazy capabilities",
                    P0CapabilityMenu.Validate,
                    ref currentCheck);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{currentCheck}: unexpected exception: {exception}");
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                currentCheck = string.Empty;
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P0 source-free validation suite: PASS " +
                    "(package baseline, presence, ARKit parser, OSC/VMC codec, diagnostics math, material override, normalized events, environment, lazy capabilities)");
                return;
            }

            Debug.LogError(
                "VCR P0 source-free validation suite: FAIL\n" +
                string.Join("\n", failures));
        }

        private static void Run(
            string name,
            Action action,
            ref string currentCheck)
        {
            currentCheck = name;
            action();
        }
    }
}
