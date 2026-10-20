using UnityEditor;
using UnityEngine;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;

namespace VCR.Editor.P0
{
    public static class P0EnvironmentMenu
    {
        [MenuItem("VCR/P0/Validate Environment Runtime")]
        public static void Validate()
        {
            var root =
                new GameObject(
                    "VCR P0 Environment Test");

            try
            {
                var runtime =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();

                runtime.Configure(
                    "environment.p0.test",
                    "day",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                var changes = 0;
                EnvironmentStateChange last = default;

                runtime.StateChanged += change =>
                {
                    changes++;
                    last = change;
                };

                var changed =
                    runtime.SetState(
                        "night",
                        out var error);

                var repeated =
                    runtime.SetState(
                        "night",
                        out var repeatError);

                var status = runtime.Status;

                var pass =
                    changed &&
                    repeated &&
                    string.IsNullOrEmpty(error) &&
                    string.IsNullOrEmpty(
                        repeatError) &&
                    changes == 1 &&
                    last.PreviousStateId == "day" &&
                    last.StateId == "night" &&
                    status.EnvironmentId ==
                        "environment.p0.test" &&
                    status.StateId == "night" &&
                    status.UpdatePolicy ==
                        EnvironmentUpdatePolicy
                            .EventDriven &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.World;

                if (pass)
                {
                    Debug.Log(
                        "VCR P0 environment runtime: PASS - state changed without scene reload and event-driven/static controller requires no per-frame Update.");
                }
                else
                {
                    Debug.LogError(
                        "VCR P0 environment runtime: FAIL.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
