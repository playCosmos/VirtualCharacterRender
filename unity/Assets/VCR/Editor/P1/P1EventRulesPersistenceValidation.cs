using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using VCR.Runtime.UI;

namespace VCR.Editor.P1
{
    // Exercises the U1 Save/Reload handlers against the real EventRuntimeHost
    // and versioned disk store without requiring external event providers.
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
                var storeType = FindRuntimeType(
                    "VCR.Runtime.EventRuntime.Unity.EventRuntimeConfigurationStore");
                var hostType = FindRuntimeType(
                    "VCR.Runtime.EventRuntime.Unity.EventRuntimeHost");
                var ruleType = FindRuntimeType(
                    "VCR.Runtime.EventRuntime.EventRuntimeRule");
                if (storeType == null || hostType == null || ruleType == null)
                    return false;
                Directory.CreateDirectory(directory);
                var store = Activator.CreateInstance(storeType,
                    Path.Combine(directory, "event-rules.json"));
                var storePath = storeType.GetProperty("Path");
                var tryLoad = storeType.GetMethod("TryLoad");
                if (storePath == null || tryLoad == null)
                    return false;
                runtimeRoot = new GameObject("VCR P1 Event Runtime Persistence");
                runtimeRoot.SetActive(false);
                var eventHost = runtimeRoot.AddComponent(hostType);
                var persistedRule = CreateRule(ruleType, "p1.persisted.rule",
                    "Persisted rule", enabled: false);
                SetRules(hostType, eventHost, ruleType, new[] { persistedRule });
                if (!SetMaxCommands(hostType, eventHost, 19))
                    return false;

                shellRoot = new GameObject("VCR P1 Event Settings Shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var storeField = type.GetField("_workspaceEventStore", flags);
                var hostField = type.GetField("_workspaceEventHost", flags);
                var restoreCheckedField =
                    type.GetField("_workspaceEventRestoreChecked", flags);
                var save = type.GetMethod("SaveWorkspaceEventRules", flags);
                var reload = type.GetMethod("ReloadWorkspaceEventRules", flags);
                if (storeField == null || hostField == null ||
                    restoreCheckedField == null || save == null || reload == null)
                    return false;
                storeField.SetValue(shell, store);
                hostField.SetValue(shell, eventHost);
                restoreCheckedField.SetValue(shell, true);

                var eventSource = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.EventRules.cs"));
                if (!eventSource.Contains("new Button(SaveWorkspaceEventRules)") ||
                    !eventSource.Contains("new Button(ReloadWorkspaceEventRules)"))
                    return false;
                save.Invoke(shell, null);
                if (!File.Exists(storePath.GetValue(store) as string))
                    return false;
                var saved = LoadStoredRules(tryLoad, store);
                if (saved == null ||
                    !MatchesRule(ruleType, saved.Value.Rule,
                        "p1.persisted.rule", "Persisted rule", false) ||
                    saved.Value.MaxCommands != 19)
                    return false;

                var unsavedRule = CreateRule(ruleType, "p1.unsaved.rule",
                    "Unsaved rule", enabled: true);
                SetRules(hostType, eventHost, ruleType, new[] { unsavedRule });
                if (!SetMaxCommands(hostType, eventHost, 7))
                    return false;

                // First click arms the deliberate unsaved-edit safeguard.
                reload.Invoke(shell, null);
                if (!MatchesRule(hostType, eventHost, ruleType,
                        "p1.unsaved.rule", "Unsaved rule", true) ||
                    (int)hostType.GetProperty("MaxCommandsPerEvent")
                        .GetValue(eventHost) != 7)
                    return false;
                // The second immediate click follows the actual U1 handler
                // and restores the saved rule set into the live runtime.
                reload.Invoke(shell, null);
                if (!MatchesRule(hostType, eventHost, ruleType,
                        "p1.persisted.rule", "Persisted rule", false) ||
                    (int)hostType.GetProperty("MaxCommandsPerEvent")
                        .GetValue(eventHost) != 19)
                    return false;

                // The reload helper must read and validate the complete file
                // before mutating the live host. A damaged file must leave
                // unsaved runtime rules and limits intact.
                SetRules(hostType, eventHost, ruleType, new[] { unsavedRule });
                if (!SetMaxCommands(hostType, eventHost, 7))
                    return false;
                var path = storePath.GetValue(store) as string;
                File.WriteAllText(path, "{ damaged event rules");
                reload.Invoke(shell, null);
                reload.Invoke(shell, null);
                if (!MatchesRule(hostType, eventHost, ruleType,
                        "p1.unsaved.rule", "Unsaved rule", true))
                    return false;
                return (int)hostType.GetProperty("MaxCommandsPerEvent")
                           .GetValue(eventHost) == 7;
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

        private static Type FindRuntimeType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null) return type;
            }
            return null;
        }

        private static object CreateRule(
            Type ruleType, string id, string label, bool enabled)
        {
            var rule = Activator.CreateInstance(ruleType);
            ruleType.GetField("Id")?.SetValue(rule, id);
            ruleType.GetField("GraphLabel")?.SetValue(rule, label);
            ruleType.GetField("GraphGroup")?.SetValue(rule, "P1");
            ruleType.GetField("Enabled")?.SetValue(rule, enabled);
            ruleType.GetField("CooldownSeconds")?.SetValue(rule, 3.5d);
            ruleType.GetField("StopAfterMatch")?.SetValue(rule, true);
            return rule;
        }

        private static void SetRules(
            Type hostType, Component host, Type ruleType, object[] rules)
        {
            var values = Array.CreateInstance(ruleType, rules.Length);
            for (var i = 0; i < rules.Length; i++)
                values.SetValue(rules[i], i);
            var setRules = hostType.GetMethod("SetRules");
            if (setRules == null)
                throw new MissingMethodException(hostType.FullName, "SetRules");
            setRules.Invoke(host, new object[] { values });
        }

        private static bool SetMaxCommands(
            Type hostType, Component host, int value)
        {
            var method = hostType.GetMethod("TrySetMaxCommandsPerEvent");
            if (method == null) return false;
            var args = new object[] { value, null };
            return (bool)method.Invoke(host, args) &&
                   string.IsNullOrWhiteSpace(args[1] as string);
        }

        private static bool MatchesRule(
            Type hostType, Component host, Type ruleType,
            string id, string label, bool enabled)
        {
            var count = hostType.GetProperty("RuleCount")?.GetValue(host);
            var getRule = hostType.GetMethod("GetRuleAt");
            if (!(count is int countValue) || countValue != 1 || getRule == null)
                return false;
            var rule = getRule.Invoke(host, new object[] { 0 });
            return rule != null &&
                   (string)ruleType.GetField("Id")?.GetValue(rule) == id &&
                   (string)ruleType.GetField("GraphLabel")?.GetValue(rule) == label &&
                   (bool)ruleType.GetField("Enabled")?.GetValue(rule) == enabled &&
                   Math.Abs((double)ruleType.GetField("CooldownSeconds")
                       ?.GetValue(rule) - 3.5d) < 0.0001d &&
                   (bool)ruleType.GetField("StopAfterMatch")?.GetValue(rule);
        }

        private static (object Rule, int MaxCommands)?
            LoadStoredRules(MethodInfo tryLoad, object store)
        {
            var args = new object[] { null, 0, null };
            if (!(bool)tryLoad.Invoke(store, args) ||
                !string.IsNullOrWhiteSpace(args[2] as string))
                return null;
            var rules = args[0] as Array;
            if (rules == null || rules.Length != 1)
                return null;
            return (rules.GetValue(0), (int)args[1]);
        }
    }
}
