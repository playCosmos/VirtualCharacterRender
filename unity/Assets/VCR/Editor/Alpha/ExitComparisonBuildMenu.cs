using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Editor.P0;
using VCR.Runtime.Application;

namespace VCR.Editor.Alpha
{
    /// <summary>
    /// Four source-identical Windows Player controls to localize native quit
    /// latency. No release output, no tags, no production scene changes.
    ///
    /// A: same Unity project, minimal scene, comparison probe only.
    /// B: P0 managed runtime, no UniWinC output/controller.
    /// C: P0 runtime with native UniWinC overlay.
    /// D: integrated alpha application with native UI/tracking integrations.
    ///
    /// A is a SCENE-only baseline. It does not promise native packages are
    /// absent from the built Player; compare actual loaded modules separately.
    /// </summary>
    public static class ExitComparisonBuildMenu
    {
        private const string SceneFolder =
            "Assets/VCR/Diagnostics/ExitComparison";
        private const string OutputFolder =
            "Builds/ExitComparison";

        [MenuItem("VCR/Diagnostics/Windows Exit Comparison/A - Unity Scene")]
        public static void BuildWindowsA() { BuildOrThrow("A"); }

        [MenuItem("VCR/Diagnostics/Windows Exit Comparison/B - VCR No Overlay")]
        public static void BuildWindowsB() { BuildOrThrow("B"); }

        [MenuItem("VCR/Diagnostics/Windows Exit Comparison/C - UniWinC")]
        public static void BuildWindowsC() { BuildOrThrow("C"); }

        [MenuItem("VCR/Diagnostics/Windows Exit Comparison/D - Full Alpha")]
        public static void BuildWindowsD() { BuildOrThrow("D"); }

        private static void BuildOrThrow(string variant)
        {
            if (!Build(variant))
                throw new InvalidOperationException(
                    "Windows exit comparison " + variant + " build failed");
        }

        public static bool Build(string variant)
        {
            if (variant != "A" && variant != "B" &&
                variant != "C" && variant != "D")
                throw new ArgumentException("Unknown comparison variant: " +
                    variant, nameof(variant));

            // Same graphics/project baseline, target and build flags for all.
            P0TransparentOutputMenu.ConfigureProjectBaseline();
            Directory.CreateDirectory(SceneFolder);
            AssetDatabase.Refresh();

            string scenePath = SceneFolder + "/ExitComparison-" + variant + ".unity";
            if (variant == "A")
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("VCR Exit Comparison A - Unity scene");
                var camera = new GameObject("Main Camera");
                camera.tag = "MainCamera";
                camera.transform.SetParent(root.transform, false);
                camera.AddComponent<Camera>();
            }
            else if (variant == "D")
            {
                if (!AlphaApplicationSceneBuilder.CreateAlphaRuntimeScene())
                    return false;
            }
            else
            {
                if (!P0TrackingSceneBuilder.CreateRuntimeScene(
                        scenePath, "VCR Exit Comparison " + variant,
                        "environment.p0.basic", "Compare-" + variant))
                    return false;
                var root = GameObject.Find("VCR Exit Comparison " + variant);
                if (root == null)
                    throw new InvalidOperationException(
                        "Comparison P0 scene root not found");

                var tracking = root.transform.Find("Tracking");
                if (tracking == null)
                    throw new InvalidOperationException("Tracking root missing");
                var mediaPipe = tracking.GetComponent<
                    VCR.Runtime.Tracking.MediaPipe.MediaPipeWebcamTrackingRunner>();
                if (mediaPipe != null)
                    mediaPipe.enabled = false;

                if (variant == "B")
                {
                    // Remove BOTH behaviours: RequireComponent adds the
                    // UniWindowController alongside UniWinCOverlayOutput.
                    // The rest of the VCR scene/bootstrap remains intact.
                    var overlay = root.GetComponent("UniWinCOverlayOutput");
                    if (overlay == null)
                        throw new InvalidOperationException(
                            "Expected UniWinCOverlayOutput in the P0 scene");
                    UnityEngine.Object.DestroyImmediate(overlay);
                    var windowController = root.GetComponent("UniWindowController");
                    if (windowController == null)
                        throw new InvalidOperationException(
                            "Expected UniWindowController in the P0 scene");
                    UnityEngine.Object.DestroyImmediate(windowController);
                }
            }

            var probeHost = new GameObject(
                "VCR Exit Comparison Probe " + variant);
            var probe = probeHost.AddComponent<ExitComparisonProbe>();
            probe.Configure(variant);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Cannot save diagnostic scene: " + scenePath);
            AssetDatabase.SaveAssets();

            var projectDirectory = Directory.GetParent(
                UnityEngine.Application.dataPath);
            if (projectDirectory == null || projectDirectory.Parent == null)
                throw new InvalidOperationException(
                    "Cannot resolve repository root");
            var path = Path.Combine(projectDirectory.Parent.FullName,
                OutputFolder, variant, "VirtualCharacterRender.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // All variants are non-development, matching real user runs.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            var success = report.summary.result == BuildResult.Succeeded;
            UnityEngine.Debug.Log(
                "VCR exit comparison: variant=" + variant +
                ", result=" + report.summary.result +
                ", errors=" + report.summary.totalErrors +
                ", output=" + path);
            return success;
        }
    }
}
