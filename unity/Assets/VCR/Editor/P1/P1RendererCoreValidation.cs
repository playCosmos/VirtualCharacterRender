using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Character;
using VCR.Runtime.Rendering;
using VCR.Runtime.Scene;

namespace VCR.Editor.P1
{
    public static class P1RendererCoreValidation
    {
        [MenuItem("VCR/P1/Validate Renderer Core")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures = new List<string>();
            GameObject root = null;

            var originalRunInBackground =
                Application.runInBackground;
            var originalVSync =
                QualitySettings.vSyncCount;
            var originalTargetFrameRate =
                Application.targetFrameRate;

            try
            {
                root = new GameObject(
                    "VCR P1 Renderer Core Validation");

                var characterRoot =
                    new GameObject("Character");
                characterRoot.transform.SetParent(
                    root.transform,
                    false);
                characterRoot.AddComponent<
                    Vrm10CharacterLoader>();

                var render =
                    root.AddComponent<
                        DesktopRenderBootstrap>();

                render.SetCustomResolution(100, 100);
                Expect(
                    render.RequestedWidth == 320 &&
                    render.RequestedHeight == 240,
                    "custom resolution must clamp to 320x240 minimum",
                    failures);

                render.SetRenderScale(0.1f);
                Expect(
                    Math.Abs(
                        render.RenderScale - 0.5f) <
                    0.0001f,
                    "render scale must clamp to 0.5 minimum",
                    failures);

                render.SetRenderScale(3.0f);
                Expect(
                    Math.Abs(
                        render.RenderScale - 2.0f) <
                    0.0001f,
                    "render scale must clamp to 2.0 maximum",
                    failures);

                render.SetFramePacing(
                    1,
                    vSync: false);
                Expect(
                    render.TargetFrameRate == 30,
                    "target frame rate must clamp to 30 FPS minimum",
                    failures);

                var scene =
                    root.AddComponent<
                        SingleCharacterSceneRuntime>();

                Expect(
                    scene.Initialize(),
                    "scene runtime must initialize from local renderer/character dependencies",
                    failures);

                Expect(
                    scene.State ==
                    SceneRuntimeState.Ready,
                    "empty initialized scene must be Ready",
                    failures);

                scene.UnloadCharacter();

                Expect(
                    scene.State ==
                    SceneRuntimeState.Ready,
                    "unloading an empty scene must remain Ready",
                    failures);

                Expect(
                    !DeclaresPerFrameUpdate(
                        typeof(
                            SingleCharacterSceneRuntime)),
                    "scene runtime must not add a per-frame Update loop",
                    failures);

                Expect(
                    !DeclaresPerFrameUpdate(
                        typeof(
                            DesktopRenderBootstrap)),
                    "render bootstrap must not add a per-frame Update loop",
                    failures);

                scene.Shutdown();

                Expect(
                    scene.State ==
                    SceneRuntimeState.Stopped,
                    "scene shutdown must end in Stopped state",
                    failures);

                Expect(
                    Application.runInBackground ==
                    originalRunInBackground,
                    "shutdown must restore Application.runInBackground",
                    failures);

                Expect(
                    QualitySettings.vSyncCount ==
                    originalVSync,
                    "shutdown must restore vSyncCount",
                    failures);

                Expect(
                    Application.targetFrameRate ==
                    originalTargetFrameRate,
                    "shutdown must restore targetFrameRate",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                Application.runInBackground =
                    originalRunInBackground;
                QualitySettings.vSyncCount =
                    originalVSync;
                Application.targetFrameRate =
                    originalTargetFrameRate;

                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P1 renderer core validation: PASS " +
                    "(graphics clamps, global restore, scene lifecycle, no per-frame coordinator loop)");
                return true;
            }

            Debug.LogError(
                "VCR P1 renderer core validation: FAIL\n" +
                string.Join("\n", failures));
            return false;
        }

        private static bool DeclaresPerFrameUpdate(
            Type type)
        {
            return type.GetMethod(
                       "Update",
                       BindingFlags.Instance |
                       BindingFlags.Public |
                       BindingFlags.NonPublic |
                       BindingFlags.DeclaredOnly) !=
                   null;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }
}
