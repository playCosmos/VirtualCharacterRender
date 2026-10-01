using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace VCR.Editor.P0
{
    public static class P0PackageBaselineCheck
    {
        private const string UniWinCManifestPin =
            "https://github.com/kirurobo/UniWindowController.git?path=/UniWinC/Assets/Kirurobo/UniWindowController#v0.9.8";
        private static readonly IReadOnlyDictionary<string, string> Expected =
            new Dictionary<string, string>
            {
                ["com.unity.render-pipelines.universal"] = "17.3",
                ["com.vrmc.gltf"] = "0.131.2",
                ["com.vrmc.univrm"] = "0.131.2",
                ["com.vrmc.vrm"] = "0.131.2",
                ["com.github.homuler.mediapipe"] = "0.16.3",
                ["com.kirurobo.uniwinc"] = "0.9.8"
            };

        private static void ValidateManifestPins(
            List<string> failures)
        {
            var manifestPath = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "Packages",
                    "manifest.json"));

            if (!File.Exists(manifestPath))
            {
                failures.Add("Packages/manifest.json was not found.");
                return;
            }

            var manifest = File.ReadAllText(manifestPath);
            if (!manifest.Contains(
                UniWinCManifestPin,
                StringComparison.Ordinal))
            {
                failures.Add(
                    "UniWindowController must be pinned to the v0.9.8 UPM subfolder URL.");
            }
        }

        [MenuItem("VCR/P0/Validate Package Baseline")]
        public static void Validate()
        {
            var packages = PackageInfo.GetAllRegisteredPackages()
                .ToDictionary(package => package.name, StringComparer.Ordinal);

            var failures = new List<string>();

            foreach (var pair in Expected)
            {
                if (!packages.TryGetValue(pair.Key, out var package))
                {
                    failures.Add($"Missing package: {pair.Key}");
                    continue;
                }

                if (!package.version.StartsWith(pair.Value, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"Version mismatch: {pair.Key} expected {pair.Value}, got {package.version}");
                }
            }

            ValidateManifestPins(failures);

            if (failures.Count == 0)
            {
                Debug.Log("VCR P0 package baseline: PASS");
                return;
            }

            Debug.LogError(
                "VCR P0 package baseline: FAIL\n" +
                string.Join("\n", failures));
        }
    }
}
