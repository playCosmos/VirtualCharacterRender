using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;
using VCR.Runtime.UI;

namespace VCR.Editor.P1
{
    // Exercises the U1 reload helper against the real EventRuntimeHost and
    // versioned disk store without requiring external event providers.
    internal static class P1EventRulesPersistenceValidation
    {
        public static bool RunChecks()
        {
            var directory = Path.Combine(Path.GetTempPath(),
                "vcr-p1-event-rules-" + Guid.NewGuid().ToString("N"));
            GameObject runtimeRoot = null;
            GameObject shellRoot = null;
            try
            {
                Directory.CreateDirectory(directory);
                var store = new EventRuntimeConfigurationStore(
                    Path.Combine(directory, "event-rules.json"));
                runtimeRoot = new GameObject("VCR P1 Event Runtime Persistence");
                runtimeRoot.SetActive(false);
                var eventHost = runtimeRoot.AddComponent<EventRuntimeHost>();
                var persistedRule = new EventRuntimeRule
                {
                    Id = "p1.persisted.rule",
                    GraphLabel = "Persisted rule",
                    GraphGroup = "P1",
                    Enabled = false,
                    CooldownSeconds = 3.5d,
                    StopAfterMatch = true
                };
                eventHost.SetRules(persistedRule);
                if (!eventHost.TrySetMaxCommandsPerEvent(19, out var limitError) ||
                    !string.IsNullOrWhiteSpace(limitError) ||
                    !store.TrySave(eventHost.CaptureRules(),
                        eventHost.MaxCommandsPerEvent, out var saveError) ||
                    !string.IsNullOrWhiteSpace(saveError) ||
                    !File.Exists(store.Path))
                    return false;

                shellRoot = new GameObject("VCR P1 Event Settings Shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var storeField = type.GetField("_workspaceEventStore", flags);
                var reload = type.GetMethod("TryReloadWorkspaceEventRules", flags);
                if (storeField == null || reload == null)
                    return false;
                storeField.SetValue(shell, store);

                var unsavedRule = new EventRuntimeRule
                {
                    Id = "p1.unsaved.rule",
                    GraphLabel = "Unsaved rule",
                    Enabled = true
                };
                eventHost.SetRules(unsavedRule);
                if (!eventHost.TrySetMaxCommandsPerEvent(7, out _))
                    return false;

                var reloadArguments = new object[] { eventHost, null };
                if (!(bool)reload.Invoke(shell, reloadArguments))
                    return false;
                var loadedRule = eventHost.GetRuleAt(0);
                if (eventHost.RuleCount != 1 ||
                    loadedRule == null ||
                    loadedRule.Id != persistedRule.Id ||
                    loadedRule.GraphLabel != persistedRule.GraphLabel ||
                    loadedRule.Enabled != persistedRule.Enabled ||
                    Math.Abs(loadedRule.CooldownSeconds -
                        persistedRule.CooldownSeconds) > 0.0001d ||
                    !loadedRule.StopAfterMatch ||
                    eventHost.MaxCommandsPerEvent != 19)
                    return false;

                // The reload helper must read and validate the complete file
                // before mutating the live host. A damaged file must leave
                // unsaved runtime rules and limits intact.
                eventHost.SetRules(unsavedRule);
                if (!eventHost.TrySetMaxCommandsPerEvent(7, out _))
                    return false;
                File.WriteAllText(store.Path, "{ damaged event rules");
                reloadArguments = new object[] { eventHost, null };
                if ((bool)reload.Invoke(shell, reloadArguments))
                    return false;
                var retainedRule = eventHost.GetRuleAt(0);
                return retainedRule != null &&
                       retainedRule.Id == unsavedRule.Id &&
                       eventHost.MaxCommandsPerEvent == 7;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "P1 event rule persistence validation failed: " + exception);
                return false;
            }
            finally
            {
                if (shellRoot != null)
                    UnityEngine.Object.DestroyImmediate(shellRoot);
                if (runtimeRoot != null)
                    UnityEngine.Object.DestroyImmediate(runtimeRoot);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }
}
