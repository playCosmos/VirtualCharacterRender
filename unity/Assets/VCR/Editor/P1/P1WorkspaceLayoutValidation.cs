using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using VCR.Runtime.UI;
using VCR.Runtime.Tracking;

namespace VCR.Editor.P1
{
    // Source-free regression for the real workspace layout snapshot writer and
    // reader. Uses the internal directory seam rather than reimplementing the
    // persistence format in a test-only class.
    internal static class P1WorkspaceLayoutValidation
    {
        private const BindingFlags StaticPrivate =
            BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly string[] PanelIds =
            WorkspacePanelCatalog.GetDefaultPanelIds(true);

        [Serializable]
        private sealed class LayoutMirror
        {
            public int version;
            public bool advanced;
            public bool tabs;
            public bool leftVisible = true;
            public bool rightVisible = true;
            public bool bottomVisible = true;
            public float leftWidth = 220f;
            public float rightWidth = 260f;
            public float bottomHeight = 150f;
            public float scenesHeight = 180f;
            public float inspectorHeight = 260f;
            public string activeLeft;
            public string activeRight;
            public string activeBottom;
            public string[] panelIds;
            public string[] docks;
            public int[] orders;
        }

        private enum PrefKind { String, Int, Float }

        private sealed class PrefEntry
        {
            public string Key;
            public PrefKind Kind;
            public bool Exists;
            public string StringValue;
            public int IntValue;
            public float FloatValue;
        }

        public static bool RunChecks()
        {
            var directory = Path.Combine(Path.GetTempPath(),
                "vcr-workspace-layout-" + Guid.NewGuid().ToString("N"));
            var legacyDirectory = Path.Combine(Path.GetTempPath(),
                "vcr-workspace-layout-legacy-" + Guid.NewGuid().ToString("N"));
            PrefEntry[] originalPrefs = null;
            try
            {
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(legacyDirectory);
                originalPrefs = CapturePrefs();

                var type = typeof(WorkspaceToolkitShell).Assembly.GetType(
                    "VCR.Runtime.UI.WorkspaceLayoutFile", true);
                var save = RequireStatic(type, "Save", typeof(string));
                var saveAutomatic = RequireStatic(type, "SaveAutomatic", typeof(string));
                var load = RequireStatic(type, "Load", typeof(string));
                var loadForStartup = RequireStatic(type, "LoadForStartup", typeof(string));
                var clear = RequireStatic(type, "ClearSnapshots", typeof(string));
                InvokeVoid(clear, directory);

                // Exact round trip: manual checkpoint wins over mutated prefs.
                ApplyLayoutA();
                Check(InvokeBool(save, directory), "manual save must succeed");
                Check(File.Exists(ManualPath(directory)) &&
                      File.Exists(AutomaticPath(directory)),
                    "save must write both checkpoint and automatic snapshot");
                ApplySentinel();
                Check(InvokeBool(load, directory), "manual load must succeed");
                CheckLayoutA("manual round trip");

                // Runtime autosave is preferred at startup, while an explicit
                // Restore deliberately keeps the manual checkpoint.
                ApplyLayoutB();
                Check(InvokeBool(saveAutomatic, directory),
                    "automatic save must succeed");
                Check(InvokeBool(loadForStartup, directory),
                    "startup load must succeed");
                CheckLayoutB("startup autosave preference");
                Check(InvokeBool(load, directory),
                    "explicit manual restore must succeed");
                CheckLayoutA("manual checkpoint authority");

                // A damaged automatic file must fall back to its .bak, then to
                // the manual checkpoint; it must never be treated as valid.
                File.WriteAllText(AutomaticPath(directory), "{ damaged auto");
                ApplySentinel();
                Check(InvokeBool(loadForStartup, directory),
                    "startup load must recover from a damaged automatic file");
                // The previous automatic replacement rotated layout A into .bak.
                CheckLayoutA("automatic backup recovery");

                // Saving over a corrupt primary must not replace a good .bak.
                File.WriteAllText(AutomaticPath(directory), "{ damaged again");
                ApplyLayoutB();
                Check(InvokeBool(saveAutomatic, directory),
                    "automatic save must replace a corrupt primary");
                var backup = JsonUtility.FromJson<LayoutMirror>(
                    File.ReadAllText(AutomaticPath(directory) + ".bak"));
                Check(backup != null && Math.Abs(backup.leftWidth - 321f) < 0.01f,
                    "a corrupt primary must not rotate over the last good backup");

                // v1 snapshots map positionally against the append-only panel
                // catalog. Missing tail panels (Materials/Capabilities) must
                // reset instead of retaining stale dock assignments.
                BuildLegacySnapshot(directory, legacyDirectory, 444f, 7, 1, null);
                ApplySentinel();
                Check(InvokeBool(load, legacyDirectory),
                    "v1 positional snapshot must migrate");
                Check(Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.LeftWidth.v1", -1f) - 444f) < 0.01f,
                    "v1 snapshot size must be restored");
                Check(PlayerPrefs.GetString(
                          "VCR.UI.Workspace.Dock.scenes.v1", "") == "left" &&
                      PlayerPrefs.GetString(
                          "VCR.UI.Workspace.Dock.inspector.v1", "") == "right",
                    "v1 positional dock assignments must map to the current catalog");
                Check(!PlayerPrefs.HasKey("VCR.UI.Workspace.Dock.materials.v1") &&
                      !PlayerPrefs.HasKey("VCR.UI.Workspace.Dock.capabilities.v1"),
                    "panels absent from a v1 snapshot must reset to defaults");

                // Unsupported future versions, duplicate IDs and malformed JSON
                // are rejected without partially mutating the live layout.
                BuildLegacySnapshot(directory, legacyDirectory, 1f, 7, 99, null);
                ApplySentinel();
                Check(!InvokeBool(load, legacyDirectory),
                    "unsupported snapshot versions must be rejected");
                CheckSentinel("unsupported version");

                BuildLegacySnapshot(directory, legacyDirectory, 1f, 7, 2,
                    mirror => mirror.panelIds = new[] { "scenes", "scenes" });
                ApplySentinel();
                Check(!InvokeBool(load, legacyDirectory),
                    "duplicate panel IDs must be rejected");
                CheckSentinel("duplicate panel IDs");

                File.WriteAllText(Path.Combine(legacyDirectory, "workspace-layout.v1.json"),
                    "{ not json");
                ApplySentinel();
                Check(!InvokeBool(load, legacyDirectory),
                    "malformed JSON must be rejected");
                CheckSentinel("malformed JSON");

                // Unknown IDs come from a newer catalog; known entries must
                // still load and the unknown entry must be ignored.
                BuildLegacySnapshot(directory, legacyDirectory, 333f, PanelIds.Length, 2,
                    mirror =>
                    {
                        var ids = new List<string>(mirror.panelIds)
                            { "futurePanel" };
                        var docks = new List<string>(mirror.docks) { "left" };
                        var orders = new List<int>(mirror.orders) { 40 };
                        mirror.panelIds = ids.ToArray();
                        mirror.docks = docks.ToArray();
                        mirror.orders = orders.ToArray();
                    });
                ApplySentinel();
                Check(InvokeBool(load, legacyDirectory),
                    "unknown future panel IDs must not invalidate known entries");
                Check(Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.LeftWidth.v1", -1f) - 333f) < 0.01f,
                    "known entries must load alongside an unknown panel ID");
                Check(!PlayerPrefs.HasKey("VCR.UI.Workspace.Dock.futurePanel.v1"),
                    "unknown panel IDs must be ignored, not persisted");

                // Persisted dimensions are clamped into their supported ranges.
                BuildLegacySnapshot(directory, legacyDirectory, 9999f, PanelIds.Length, 2,
                    mirror =>
                    {
                        mirror.leftWidth = 9999f;
                        mirror.rightWidth = -5f;
                        mirror.bottomHeight = 50f;
                        mirror.scenesHeight = 9999f;
                        mirror.inspectorHeight = 0f;
                    });
                Check(InvokeBool(load, legacyDirectory),
                    "out-of-range sizes must not invalidate an otherwise valid snapshot");
                Check(Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.LeftWidth.v1", -1f) - 520f) < 0.01f &&
                      Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.RightWidth.v1", -1f) - 160f) < 0.01f &&
                      Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.BottomHeight.v1", -1f) - 100f) < 0.01f &&
                      Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.ScenesHeight.v1", -1f) - 400f) < 0.01f &&
                      Math.Abs(PlayerPrefs.GetFloat(
                          "VCR.UI.Workspace.InspectorHeight.v1", -1f) - 140f) < 0.01f,
                    "persisted dimensions must be clamped to the supported ranges");

                // Reset must remove the checkpoint, autosave and both backups.
                InvokeVoid(clear, directory);
                Check(!File.Exists(ManualPath(directory)) &&
                      !File.Exists(AutomaticPath(directory)) &&
                      !File.Exists(ManualPath(directory) + ".bak") &&
                      !File.Exists(AutomaticPath(directory) + ".bak"),
                    "reset must remove checkpoint and recovery snapshots");

                var shellSource = File.ReadAllText(Path.Combine(
                    Application.dataPath, "VCR/Runtime/UI/WorkspaceToolkitShell.cs"));
                Check(NativeSettingsActionsKeepDistinctMeanings(),
                    "native settings actions 204 and 207 must stay load and render-default");
                Check(SettingsConfirmationLifecycle(),
                    "Settings close, reopen, alternate actions and reset must disarm confirmation");
                Check(TrackingReconnectedProviderRestoresPreferences(),
                    "recreated tracking control with same ID must restore its saved enabled state");
                Check(TrackingCachedProviderExternalStateDriftRestoresSavedPreference(),
                    "cached tracking sources must reconcile external On/Off drift without stale drafts");
                Check(TrackingCachedProviderExternalSettingsDriftRestoresSavedPreference(),
                    "cached tracking sources must reconcile normalized config-only drift, saved-value updates and failed retries");
                Check(TrackingRepeatedRestoreFailuresBackoffPerProvider(),
                    "repeated tracking restore faults must back off without delaying a new provider or operator recovery");
                Check(TrackingSavedEnabledPreferenceChangeCancelsRestoreBackoff(),
                    "external saved On/Off changes must not inherit stale tracking retry delays");
                Check(TrackingSavedConfigurationChangesCancelRestoreBackoff(),
                    "external saved configuration updates/additions/deletions must bypass retry backoff without SDK polling");
                Check(TrackingSavedOffSafetyBypassesConfigurationBackoff(),
                    "saved-Off source must be stopped after failed Apply and while config retries are deferred");
                Check(TrackingBackgroundRestoreInvalidatesStaleEditors(),
                    "accepted background tracking restore must invalidate stale U1 settings drafts");
                Check(TrackingFailedApplyMutationsInvalidateOldDrafts(),
                    "failed provider configuration must not preserve a stale U1 editor after live mutation");
                Check(TrackingEditorSchemaParity(),
                    "native and Toolkit tracking editors must reject null/oversized schemas");
                Check(TrackingRecoveryDoesNotEnableDisabledSource(),
                    "tracking recovery must not enable disabled sources and must replay saved state after restart");
                Check(TrackingFailedRecoveryRestoresChangedDeviceState(),
                    "failed recovery that changed live source must invalidate drafts and replay saved settings");
                Check(TrackingToggleRequiresExplicitMembership(),
                    "tracking Toggle must not implicitly register an unconfigured source");
                Check(TrackingSavedOffRestoresBeforeConfiguration(),
                    "saved-off tracking source must stop capture before applying configuration");
                Check(TrackingPersistenceIdsPreserveRegisteredCase(),
                    "case-insensitive provider reconnect must use registered PlayerPrefs key spelling");
                Check(TrackingStaleDeviceActionsRejectReplacement(),
                    "stale tracking buttons must not mutate a replacement provider");
                Check(TrackingStaleMembershipActionsRejectStateChanges(),
                    "stale Add/Remove labels must not invert membership changes");
                Check(TrackingMembershipABAActionsRejectPriorRevisions(),
                    "stale Add/Remove must reject a later membership with the same displayed state");
                Check(TrackingFailedMembershipStopPreservesRegistration(),
                    "failed Add/Remove stop must not commit membership or retain stale tracking state");
                Check(TrackingMembershipStopRequiresDisabledReadback(),
                    "an acknowledged Stop must not unregister a still-enabled tracker");
                Check(TrackingStaleToggleActionsRejectStateChanges(),
                    "stale Enable/Disable labels must not invert tracking state changes");
                Check(TrackingFailedToggleRestoresSavedState(),
                    "failed toggles with live side effects must retry the saved On/Off preference");
                Check(TrackingAcknowledgedToggleRequiresMatchingReadback(),
                    "acknowledged tracking toggles must reach the requested enabled state before persistence");
                Check(TrackingStaleRecoveryActionsRejectPriorSessions(),
                    "stale Recover buttons must not restart a newer tracking session");
                Check(TrackingRecoveryButtonsRequireRunningSavedOnSource(),
                    "Recover controls must match enabled state and saved preference");
                Check(TrackingSettingsSaveRequiresCurrentMembership(),
                    "tracking settings Save must not re-register a removed device");
                Check(TrackingStaleConfigureButtonsRejectRemovedSources(),
                    "tracking settings must not open a removed source's editor");
                Check(TrackingEditVersionPreventsStaleSaves(),
                    "concurrent Native and Toolkit tracking drafts must not overwrite newer settings");
                Check(TrackingExternalProviderChangesRejectStaleSave(),
                    "tracking editors must reject Save after direct provider setting changes");
                Check(TrackingStateTransitionsInvalidateOpenEditors(),
                    "successful tracking Toggle and Recover must invalidate previous editor drafts");
                Check(LegacyRestoreSkipsDestroyedRefresh(),
                    "legacy character restore must not refresh a destroyed controller");
                Check(ContinuationStopsBeforeUi(shellSource, "LoadCharacterAsync") &&
                      ContinuationStopsBeforeUi(shellSource, "ReloadCharacterAsync"),
                    "character load continuation must stop before UI writes when the shell is destroyed");

                Debug.Log("VCR P1 workspace layout persistence validation: PASS.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace layout persistence validation: FAIL: " +
                               exception);
                return false;
            }
            finally
            {
                if (originalPrefs != null) RestorePrefs(originalPrefs);
                TryDeleteDirectory(directory);
                TryDeleteDirectory(legacyDirectory);
            }
        }

        private static string ManualPath(string directory) =>
            Path.Combine(directory, "workspace-layout.v1.json");

        private static string AutomaticPath(string directory) =>
            Path.Combine(directory, "workspace-layout.autosave.v1.json");

        private static void ApplyLayoutA()
        {
            PlayerPrefs.SetInt("VCR.UI.Workspace.Advanced.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.TabMode.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.LeftVisible.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.RightVisible.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.BottomVisible.v1", 0);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.LeftWidth.v1", 321f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.RightWidth.v1", 222f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.BottomHeight.v1", 133f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.ScenesHeight.v1", 144f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.InspectorHeight.v1", 155f);
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.left.v1", "vcr-panel-sources");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.right.v1", "vcr-panel-inspector");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.bottom.v1", "vcr-panel-events");
            ApplyDockSet(new[]
            {
                "left", "left", "", "right", "bottom", "bottom", "bottom",
                "right", "right"
            });
        }

        private static void ApplyLayoutB()
        {
            PlayerPrefs.SetInt("VCR.UI.Workspace.Advanced.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.TabMode.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.LeftVisible.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.RightVisible.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.BottomVisible.v1", 1);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.LeftWidth.v1", 401f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.RightWidth.v1", 300f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.BottomHeight.v1", 200f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.ScenesHeight.v1", 300f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.InspectorHeight.v1", 220f);
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.left.v1", "vcr-panel-scenes");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.right.v1", "vcr-panel-materials");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.bottom.v1", "vcr-panel-actions");
            ApplyDockSet(new[]
            {
                "left", "bottom", "", "right", "bottom", "left", "bottom",
                "right", "right"
            });
        }

        private static void ApplySentinel()
        {
            PlayerPrefs.SetInt("VCR.UI.Workspace.Advanced.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.TabMode.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.LeftVisible.v1", 1);
            PlayerPrefs.SetInt("VCR.UI.Workspace.RightVisible.v1", 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.BottomVisible.v1", 1);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.LeftWidth.v1", 100f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.RightWidth.v1", 500f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.BottomHeight.v1", 100f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.ScenesHeight.v1", 400f);
            PlayerPrefs.SetFloat("VCR.UI.Workspace.InspectorHeight.v1", 140f);
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.left.v1", "");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.right.v1", "");
            PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab.bottom.v1", "");
            ApplyDockSet(new[]
            {
                "right", "right", "", "left", "left", "left", "left",
                "left", "left"
            });
        }

        private static void ApplyDockSet(string[] docks)
        {
            for (var i = 0; i < PanelIds.Length; i++)
            {
                PlayerPrefs.SetString(
                    "VCR.UI.Workspace.Dock." + PanelIds[i] + ".v1", docks[i]);
                PlayerPrefs.SetInt(
                    "VCR.UI.Workspace.Order." + PanelIds[i] + ".v1", i);
            }
        }

        private static void CheckLayoutA(string stage)
        {
            Check(PlayerPrefs.GetInt("VCR.UI.Workspace.Advanced.v1", -1) == 1 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.TabMode.v1", -1) == 0 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.LeftVisible.v1", -1) == 0 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.RightVisible.v1", -1) == 1 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.BottomVisible.v1", -1) == 0,
                stage + ": booleans");
            Check(Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.LeftWidth.v1", -1f) - 321f) < 0.01f &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.RightWidth.v1", -1f) - 222f) < 0.01f &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.BottomHeight.v1", -1f) - 133f) < 0.01f &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.ScenesHeight.v1", -1f) - 144f) < 0.01f &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.InspectorHeight.v1", -1f) - 155f) < 0.01f,
                stage + ": sizes");
            Check(PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.left.v1", "") ==
                      "vcr-panel-sources" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.right.v1", "") ==
                      "vcr-panel-inspector" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.bottom.v1", "") ==
                      "vcr-panel-events",
                stage + ": active tabs");
            Check(PlayerPrefs.GetString("VCR.UI.Workspace.Dock.scenes.v1", "") == "left" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.sources.v1", "") == "left" &&
                  !PlayerPrefs.HasKey("VCR.UI.Workspace.Dock.preview.v1") &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.inspector.v1", "") == "right" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.tracking.v1", "") == "bottom" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.actions.v1", "") == "bottom" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.events.v1", "") == "bottom" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.materials.v1", "") == "right" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.capabilities.v1", "") == "right",
                stage + ": dock assignments");
        }

        private static void CheckLayoutB(string stage)
        {
            Check(PlayerPrefs.GetInt("VCR.UI.Workspace.Advanced.v1", -1) == 0 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.TabMode.v1", -1) == 1 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.LeftVisible.v1", -1) == 1 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.RightVisible.v1", -1) == 0 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.BottomVisible.v1", -1) == 1,
                stage + ": booleans");
            Check(Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.LeftWidth.v1", -1f) - 401f) < 0.01f &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.InspectorHeight.v1", -1f) - 220f) < 0.01f,
                stage + ": sizes");
            Check(PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.left.v1", "") ==
                      "vcr-panel-scenes" &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.sources.v1", "") == "bottom",
                stage + ": autosave layout values");
        }

        private static void CheckSentinel(string stage)
        {
            Check(PlayerPrefs.GetInt("VCR.UI.Workspace.Advanced.v1", -1) == 0 &&
                  PlayerPrefs.GetInt("VCR.UI.Workspace.TabMode.v1", -1) == 1 &&
                  Math.Abs(PlayerPrefs.GetFloat("VCR.UI.Workspace.LeftWidth.v1", -1f) - 100f) < 0.01f &&
                  PlayerPrefs.GetString("VCR.UI.Workspace.Dock.scenes.v1", "") == "right",
                stage + ": rejected snapshots must not partially mutate the live layout");
        }

        private static void BuildLegacySnapshot(
            string sourceDirectory, string targetDirectory, float leftWidth,
            int count, int version, Action<LayoutMirror> mutate)
        {
            var source = JsonUtility.FromJson<LayoutMirror>(
                File.ReadAllText(ManualPath(sourceDirectory)));
            var mirror = new LayoutMirror
            {
                version = version,
                advanced = source.advanced,
                tabs = source.tabs,
                leftVisible = source.leftVisible,
                rightVisible = source.rightVisible,
                bottomVisible = source.bottomVisible,
                leftWidth = leftWidth,
                rightWidth = source.rightWidth,
                bottomHeight = source.bottomHeight,
                scenesHeight = source.scenesHeight,
                inspectorHeight = source.inspectorHeight,
                activeLeft = source.activeLeft,
                activeRight = source.activeRight,
                activeBottom = source.activeBottom,
                panelIds = version == 1 ? null : Slice(source.panelIds, count),
                docks = Slice(source.docks, count),
                orders = Slice(source.orders, count)
            };
            mutate?.Invoke(mirror);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(Path.Combine(targetDirectory, "workspace-layout.v1.json"),
                JsonUtility.ToJson(mirror, true));
        }

        private static string[] Slice(string[] values, int count)
        {
            var result = new string[count];
            Array.Copy(values, result, count);
            return result;
        }

        private static int[] Slice(int[] values, int count)
        {
            var result = new int[count];
            Array.Copy(values, result, count);
            return result;
        }

        private static PrefEntry[] CapturePrefs()
        {
            var entries = new List<PrefEntry>();
            AddPref(entries, "VCR.UI.Workspace.Advanced.v1", PrefKind.Int);
            AddPref(entries, "VCR.UI.Workspace.TabMode.v1", PrefKind.Int);
            AddPref(entries, "VCR.UI.Workspace.LeftVisible.v1", PrefKind.Int);
            AddPref(entries, "VCR.UI.Workspace.RightVisible.v1", PrefKind.Int);
            AddPref(entries, "VCR.UI.Workspace.BottomVisible.v1", PrefKind.Int);
            AddPref(entries, "VCR.UI.Workspace.LeftWidth.v1", PrefKind.Float);
            AddPref(entries, "VCR.UI.Workspace.RightWidth.v1", PrefKind.Float);
            AddPref(entries, "VCR.UI.Workspace.BottomHeight.v1", PrefKind.Float);
            AddPref(entries, "VCR.UI.Workspace.ScenesHeight.v1", PrefKind.Float);
            AddPref(entries, "VCR.UI.Workspace.InspectorHeight.v1", PrefKind.Float);
            AddPref(entries, "VCR.UI.Workspace.ActiveTab.left.v1", PrefKind.String);
            AddPref(entries, "VCR.UI.Workspace.ActiveTab.right.v1", PrefKind.String);
            AddPref(entries, "VCR.UI.Workspace.ActiveTab.bottom.v1", PrefKind.String);
            foreach (var id in PanelIds)
            {
                AddPref(entries, "VCR.UI.Workspace.Dock." + id + ".v1", PrefKind.String);
                AddPref(entries, "VCR.UI.Workspace.Order." + id + ".v1", PrefKind.Int);
            }
            return entries.ToArray();
        }

        private static void AddPref(List<PrefEntry> entries, string key, PrefKind kind)
        {
            var entry = new PrefEntry
            {
                Key = key,
                Kind = kind,
                Exists = PlayerPrefs.HasKey(key)
            };
            if (entry.Exists)
            {
                if (kind == PrefKind.String) entry.StringValue = PlayerPrefs.GetString(key);
                else if (kind == PrefKind.Int) entry.IntValue = PlayerPrefs.GetInt(key);
                else entry.FloatValue = PlayerPrefs.GetFloat(key);
            }
            entries.Add(entry);
        }

        private static void RestorePrefs(PrefEntry[] entries)
        {
            foreach (var entry in entries)
            {
                if (!entry.Exists)
                {
                    PlayerPrefs.DeleteKey(entry.Key);
                    continue;
                }
                if (entry.Kind == PrefKind.String) PlayerPrefs.SetString(entry.Key, entry.StringValue);
                else if (entry.Kind == PrefKind.Int) PlayerPrefs.SetInt(entry.Key, entry.IntValue);
                else PlayerPrefs.SetFloat(entry.Key, entry.FloatValue);
            }
            PlayerPrefs.Save();
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Workspace layout validation cleanup failed: " +
                                 exception.Message);
            }
        }

        private static MethodInfo RequireStatic(Type type, string name,
            params Type[] parameters)
        {
            return type.GetMethod(name, StaticPrivate, null, parameters, null) ??
                   throw new MissingMethodException(type.FullName, name);
        }

        private static bool HasLiveRefreshAfter(string source, int marker, string call)
        {
            var callAt = source.IndexOf(call, marker, StringComparison.Ordinal);
            if (callAt < 0) return false;
            var refresh = source.IndexOf("RefreshAll();", callAt, StringComparison.Ordinal);
            if (refresh < 0) return false;
            var guard = source.LastIndexOf("this != null", refresh, StringComparison.Ordinal);
            return guard > callAt && refresh - guard < 180;
        }
        private static bool HasLiveRefresh(string source, int marker)
        {
            var refresh = source.IndexOf("RefreshAll();", marker, StringComparison.Ordinal);
            if (refresh < 0) return false;
            var guard = source.LastIndexOf("this != null", refresh, StringComparison.Ordinal);
            return guard > marker && refresh - guard < 180;
        }
        private static bool NativeSettingsActionsKeepDistinctMeanings()
        {
            var shell = File.ReadAllText(Path.Combine(Application.dataPath,
                "VCR/Runtime/UI/WorkspaceToolkitShell.cs"));
            var legacy = File.ReadAllText(Path.Combine(Application.dataPath,
                "VCR/Runtime/UI/ApplicationUiController.NativeSettings.cs"));
            var register = shell.IndexOf("Add(0, 204,", StringComparison.Ordinal);
            var restore = shell.IndexOf("Add(0, 207,", StringComparison.Ordinal);
            if (register < 0 || restore < 0 || restore < register) return false;
            var registerLabel = shell.Substring(register, Math.Min(180, shell.Length - register));
            var restoreLabel = shell.Substring(restore, Math.Min(180, shell.Length - restore));
            if (!registerLabel.Contains("Reload saved configuration")) return false;
            if (!restoreLabel.Contains("Restore render defaults")) return false;
            if (!shell.Contains("if (action == 204)")) return false;
            if (!shell.Contains("action != 207")) return false;
            // Native Settings has one visible Reload action (legacy 204).
            // Internal U1 action 206 remains supported by the fallback.
            if (shell.Contains("Add(0, 206,")) return false;
            return legacy.Contains("AddNativeAction(0, 204") &&
                   legacy.Contains("ShowProfileLoadUnavailable");
        }
        private static bool SettingsConfirmationLifecycle()
        {
            var shellSource = File.ReadAllText(Path.Combine(Application.dataPath,
                "VCR/Runtime/UI/WorkspaceToolkitShell.cs"));
            var configSource = File.ReadAllText(Path.Combine(Application.dataPath,
                "VCR/Runtime/UI/WorkspaceToolkitShell.Configuration.cs"));
            var openAt = shellSource.IndexOf("private void OpenWorkspaceNativeSettings()",
                StringComparison.Ordinal);
            var pumpAt = shellSource.IndexOf("private void PumpWorkspaceNativeSettings()",
                StringComparison.Ordinal);
            var fallbackAt = shellSource.IndexOf("private void ShowWorkspaceSettingsFallback()",
                StringComparison.Ordinal);
            if (openAt < 0 || pumpAt < openAt || fallbackAt < 0) return false;
            var open = shellSource.Substring(openAt, pumpAt - openAt);
            var pump = shellSource.Substring(pumpAt,
                Math.Min(8500, shellSource.Length - pumpAt));
            var fallback = shellSource.Substring(fallbackAt,
                Math.Min(9000, shellSource.Length - fallbackAt));
            // A new Settings session or a different action cannot inherit
            // destructive confirmation armed in a previous context.
            if (!open.Contains("ResetWorkspaceConfigurationActions();") ||
                !pump.Contains("ResetWorkspaceConfigurationActions();") ||
                !Regex.IsMatch(pump,
                    @"if \(!_workspaceNativeSettings\.IsVisible\)\s*return;") ||
                !pump.Contains("if (action != 205) _workspaceSaveConfirmUntil = 0f;") ||
                !pump.Contains("if (action != 204 && action != 206) _workspaceReloadConfirmUntil = 0f;") ||
                !fallback.Contains("ResetWorkspaceConfigurationActions();") ||
                !fallback.Contains("scale.RegisterCallback<FocusInEvent>(RevokeConfigurationConfirmationOnFocus);") ||
                !fallback.Contains("scale.RegisterValueChangedCallback") ||
                !fallback.Contains("button.text != \"Save config\"") ||
                !fallback.Contains("button.clicked += ResetWorkspaceConfigurationActions;") ||
                !configSource.Contains("_workspaceReloadConfirmUntil = 0f;\n                    // A recovered/failed configuration") ||
                !configSource.Contains("_workspaceSaveConfirmUntil = 0f;\n                    // Reload replaces live runtime settings"))
            {
                Debug.LogError("Settings confirmation lifecycle source contract failed.");
                return false;
            }

            var host = new GameObject("VCR Settings confirmation regression");
            host.SetActive(false);
            try
            {
                var shell = host.AddComponent<WorkspaceToolkitShell>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(WorkspaceToolkitShell);
                var save = type.GetField("_workspaceSaveConfirmUntil", flags);
                var reload = type.GetField("_workspaceReloadConfirmUntil", flags);
                var reset = type.GetMethod("ResetWorkspaceConfigurationActions", flags);
                if (shell == null || save == null || reload == null || reset == null)
                {
                    Debug.LogError("Settings confirmation runtime members were not found.");
                    return false;
                }
                save.SetValue(shell, 30f); reload.SetValue(shell, 45f); reset.Invoke(shell, null);
                if ((float)save.GetValue(shell) != 0f || (float)reload.GetValue(shell) != 0f)
                {
                    Debug.LogError("Settings confirmation reset did not clear both states.");
                    return false;
                }
                save.SetValue(shell, 30f);
                reset.Invoke(shell, null);
                if ((float)save.GetValue(shell) != 0f) return false;
                reload.SetValue(shell, 45f);
                reset.Invoke(shell, null);
                var buttonClearedReload = (float)reload.GetValue(shell) == 0f;
                return buttonClearedReload;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
        }
            }
        private static bool TrackingEditorSchemaParity()
        {
            var shellType = typeof(WorkspaceToolkitShell);
            var ready = shellType.GetMethod(
                "IsWorkspaceTrackingEditableSchema", StaticPrivate);
            var display = shellType.GetMethod(
                "GetWorkspaceTrackingDisplayedValue", StaticPrivate);
            if (ready == null || display == null) return false;
            bool CanEdit(IReadOnlyList<TrackingRuntimeSetting> schema) =>
                (bool)ready.Invoke(null, new object[] { schema });
            if (CanEdit(null) ||
                !CanEdit(Array.Empty<TrackingRuntimeSetting>()) ||
                !CanEdit(new TrackingRuntimeSetting[256]) ||
                CanEdit(new TrackingRuntimeSetting[257]))
                return false;
            // The live provider readback must take precedence over any
            // previously persisted value when opening either editor.
            var liveValue = new TrackingRuntimeSetting(
                "test-key", "Test value", "current-device-value");
            if ((string)display.Invoke(null, new object[] { liveValue }) !=
                "current-device-value")
                return false;

            var source = File.ReadAllText(Path.Combine(Application.dataPath,
                "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
            var nativeAt = source.IndexOf(
                "private void ShowWorkspaceNativeTrackingDetails(", StringComparison.Ordinal);
            var nativeEnd = source.IndexOf(
                "private void SaveWorkspaceNativeTrackingDetails()", StringComparison.Ordinal);
            var toolkitAt = source.IndexOf(
                "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
            if (nativeAt < 0 || nativeEnd <= nativeAt || toolkitAt < 0)
                return false;
            var native = source.Substring(nativeAt, nativeEnd - nativeAt);
            var toolkit = source.Substring(toolkitAt);
            return native.Contains("!IsWorkspaceTrackingEditableSchema(settings)") &&
                   toolkit.Contains("!IsWorkspaceTrackingEditableSchema(settings)") &&
                   native.Contains("RebuildWorkspaceNativeTrackingSources();") &&
                   native.Contains("new HashSet<string>(StringComparer.OrdinalIgnoreCase)") &&
                   native.Contains("GetWorkspaceTrackingDisplayedValue(setting)") &&
                   toolkit.Contains("GetWorkspaceTrackingDisplayedValue(setting)") &&
                   toolkit.Contains("field.textEdition.placeholder = setting.Placeholder;") &&
                   toolkit.Contains("field.textEdition.hidePlaceholderOnFocus = true;") &&
                   toolkit.Contains("new Label(setting.HelpText)") &&
                   toolkit.Contains("new Dictionary<string, TextField>(StringComparer.OrdinalIgnoreCase)") &&
                   !native.Contains("PlayerPrefs.GetString(") &&
                   !toolkit.Contains("PlayerPrefs.GetString(");
        }

        private static bool TrackingStateTransitionsInvalidateOpenEditors()
        {
            var id = "vcr-parity-state-transition-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var recover = type.GetMethod("TryRequestWorkspaceTrackingRecovery", flags);
                var revision = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (toggle == null || recover == null || revision == null ||
                    canSave == null || configuredField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var toggleStart = source.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var recoverStart = source.IndexOf(
                    "private bool TryRequestWorkspaceTrackingRecovery(", StringComparison.Ordinal);
                var nativeStart = source.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                if (toggleStart < 0 || recoverStart <= toggleStart ||
                    nativeStart <= recoverStart)
                    return false;
                var toggleCode = source.Substring(toggleStart, recoverStart - toggleStart);
                var recoverCode = source.Substring(recoverStart, nativeStart - recoverStart);
                // Both UI surfaces already share these helpers. A revision
                // changes only after each provider accepts a state transition.
                if (!toggleCode.Contains("InvalidateWorkspaceTrackingEditors(id)") ||
                    !recoverCode.Contains(
                        "InvalidateWorkspaceTrackingEditors(control.ControlId)") ||
                    toggleCode.IndexOf("InvalidateWorkspaceTrackingEditors(",
                        StringComparison.Ordinal) <
                    toggleCode.IndexOf("if (!control.TrySetControlEnabled(",
                        StringComparison.Ordinal) ||
                    recoverCode.IndexOf("InvalidateWorkspaceTrackingEditors(",
                        StringComparison.Ordinal) <
                    recoverCode.IndexOf("if (!control.TryRecover(", StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 state transition editor shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 state transition provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);

                int Version() => (int)revision.Invoke(shell, new object[] { id });
                bool DraftValid(int openedVersion) =>
                    (bool)canSave.Invoke(shell,
                        new object[] { id.ToUpperInvariant(), openedVersion });
                bool Toggle(bool enabled)
                {
                    var args = new object[] { provider, enabled, null };
                    return (bool)toggle.Invoke(shell, args);
                }
                bool Recover()
                {
                    var args = new object[] { provider, null };
                    return (bool)recover.Invoke(shell, args);
                }

                var initialVersion = Version();
                if (!DraftValid(initialVersion)) return false;
                provider.FailEnableOnce = true;
                if (Toggle(true) || Version() != initialVersion ||
                    !DraftValid(initialVersion))
                    return false;
                if (!Toggle(true) || !provider.ControlEnabled ||
                    Version() == initialVersion || DraftValid(initialVersion) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                var beforeRecovery = Version();
                if (!DraftValid(beforeRecovery)) return false;
                provider.FailRecoveryOnce = true;
                if (Recover() || Version() != beforeRecovery ||
                    !DraftValid(beforeRecovery))
                    return false;

                // The successful Recover probe intentionally leaves the
                // same component, enabled state and setting readback intact.
                // Its old editor draft still must be invalidated.
                if (!Recover() || !provider.ControlEnabled ||
                    Version() == beforeRecovery || DraftValid(beforeRecovery) ||
                    provider.ConfigurationCalls != 0)
                    return false;

                var beforeDisable = Version();
                if (!Toggle(false) || provider.ControlEnabled ||
                    Version() == beforeDisable || DraftValid(beforeDisable) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                configured.Remove(id);
                var afterRemove = Version();
                return !Toggle(true) && !Recover() &&
                       Version() == afterRemove &&
                       !configured.Contains(id);
            }
            finally
            {
                if (providerRoot != null)
                    UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null)
                    UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingExternalProviderChangesRejectStaleSave()
        {
            GameObject root = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Static | BindingFlags.NonPublic;
                var capture = type.GetMethod("CaptureWorkspaceTrackingSettings", flags);
                var unchanged = type.GetMethod("AreWorkspaceTrackingSettingsUnchanged", flags);
                if (capture == null || unchanged == null) return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeStart = code.IndexOf(
                    "private void SaveWorkspaceNativeTrackingDetails()", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var toolkitStart = code.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                if (nativeStart < 0 || nativeEnd <= nativeStart || toolkitStart < 0)
                    return false;
                var native = code.Substring(nativeStart, nativeEnd - nativeStart);
                var toolkit = code.Substring(toolkitStart);
                var nativeCheck = native.IndexOf("AreWorkspaceTrackingSettingsUnchanged(",
                    StringComparison.Ordinal);
                var nativeApply = native.IndexOf("TryApplyWorkspaceTrackingSettings(",
                    StringComparison.Ordinal);
                var toolkitOpen = toolkit.IndexOf(
                    "var openedValues = CaptureWorkspaceTrackingSettings(",
                    StringComparison.Ordinal);
                var toolkitCheck = toolkit.IndexOf("AreWorkspaceTrackingSettingsUnchanged(",
                    StringComparison.Ordinal);
                var toolkitApply = toolkit.IndexOf("TryApplyWorkspaceTrackingSettings(",
                    StringComparison.Ordinal);
                if (!code.Contains("_workspaceNativeTrackingOpenValues = CaptureWorkspaceTrackingSettings(") ||
                    nativeCheck < 0 || nativeApply <= nativeCheck ||
                    toolkitOpen < 0 || toolkitCheck <= toolkitOpen ||
                    toolkitApply <= toolkitCheck)
                    return false;

                root = new GameObject("P1 external tracking setting change");
                var provider = root.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = "vcr-external-configuration";
                var opened = (Dictionary<string, string>)capture.Invoke(
                    null, new object[] { provider.GetConfigurationSettings() });
                if (opened == null || opened.Count != 1 ||
                    opened["test-value"] != "default-value")
                    return false;

                bool IsUnchanged() => (bool)unchanged.Invoke(null, new object[] {
                    provider, opened
                });
                if (!IsUnchanged()) return false;

                // No U1 UI action has occurred, but direct provider readback
                // changes while the editor remains open.
                provider.ExternalReadbackOverride = "changed-externally";
                if (IsUnchanged()) return false;
                provider.ExternalReadbackOverride = "default-value";
                if (!IsUnchanged()) return false;

                // Schema drift, null readiness, and oversized schema must
                // reject stale edits without mutating the provider.
                provider.ReportAdditionalSetting = true;
                if (IsUnchanged()) return false;
                provider.ReportAdditionalSetting = false;
                provider.ReturnNullSchemaOnce = true;
                if (IsUnchanged() || !IsUnchanged()) return false;
                provider.ReportOversizedSchema = true;
                if (IsUnchanged()) return false;
                provider.ReportOversizedSchema = false;

                // Case-insensitive setting keys do not relax value equality.
                var uppercase = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase) {
                        ["TEST-VALUE"] = "default-value"
                    };
                if (!(bool)unchanged.Invoke(null, new object[] { provider, uppercase }))
                    return false;
                uppercase["TEST-VALUE"] = "Default-Value";
                return !(bool)unchanged.Invoke(null, new object[] { provider, uppercase });
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool TrackingEditVersionPreventsStaleSaves()
        {
            var id = "vcr-parity-concurrent-" + Guid.NewGuid().ToString("N");
            GameObject shellRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var read = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var invalidate = type.GetMethod("InvalidateWorkspaceTrackingEditors", flags);
                var current = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (read == null || invalidate == null || current == null ||
                    configuredField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeStart = code.IndexOf(
                    "private void ShowWorkspaceNativeTrackingDetails(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var nativeActionStart = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeActionEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var toolkitStart = code.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                var membershipStart = code.IndexOf(
                    "private void UpdateWorkspaceTrackingMembership(", StringComparison.Ordinal);
                var membershipEnd = code.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                if (nativeStart < 0 || nativeEnd <= nativeStart ||
                    nativeActionStart < 0 || nativeActionEnd <= nativeActionStart ||
                    toolkitStart < 0 || membershipStart < 0 ||
                    membershipEnd <= membershipStart)
                    return false;
                var native = code.Substring(nativeStart, nativeEnd - nativeStart);
                var nativeActions = code.Substring(
                    nativeActionStart, nativeActionEnd - nativeActionStart);
                var toolkit = code.Substring(toolkitStart);
                var membership = code.Substring(
                    membershipStart, membershipEnd - membershipStart);
                if (!native.Contains("_workspaceNativeTrackingEditVersion =") ||
                    !native.Contains("IsWorkspaceTrackingEditVersionCurrent(") ||
                    !toolkit.Contains("var openedVersion = GetWorkspaceTrackingEditVersion(") ||
                    !toolkit.Contains("IsWorkspaceTrackingEditVersionCurrent(") ||
                    !nativeActions.Contains("InvalidateWorkspaceTrackingEditors(id)") ||
                    !membership.Contains("InvalidateWorkspaceTrackingEditors(sourceId)") ||
                    !native.Contains("InvalidateWorkspaceTrackingEditors(control.ControlId)") ||
                    !toolkit.Contains("InvalidateWorkspaceTrackingEditors(sourceId)") ||
                    native.IndexOf("IsWorkspaceTrackingEditVersionCurrent(",
                        StringComparison.Ordinal) >
                    native.IndexOf("TryApplyWorkspaceTrackingSettings(", StringComparison.Ordinal) ||
                    toolkit.IndexOf("IsWorkspaceTrackingEditVersionCurrent(",
                        StringComparison.Ordinal) >
                    toolkit.IndexOf("TryApplyWorkspaceTrackingSettings(", StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 concurrent tracking settings shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;
                configured.Add(id);

                int Version(string logicalId) =>
                    (int)read.Invoke(shell, new object[] { logicalId });
                bool CanSave(string logicalId, int openedVersion) =>
                    (bool)current.Invoke(shell, new object[] { logicalId, openedVersion });
                void SaveOrChangeMembership(string logicalId) =>
                    invalidate.Invoke(shell, new object[] { logicalId });

                var nativeDraft = Version(id);
                var toolkitDraft = Version(id.ToUpperInvariant());
                if (nativeDraft != toolkitDraft ||
                    !CanSave(id, nativeDraft) ||
                    !CanSave(id.ToUpperInvariant(), toolkitDraft))
                    return false;

                // After Native Save, an earlier Toolkit draft is rejected,
                // including when the provider spelling changes by case.
                SaveOrChangeMembership(id);
                if (CanSave(id, nativeDraft) ||
                    CanSave(id.ToUpperInvariant(), toolkitDraft))
                    return false;
                var currentDraft = Version(id);
                if (currentDraft == nativeDraft ||
                    !CanSave(id, currentDraft))
                    return false;

                // Remove followed by Add must not revive the original draft,
                // even though membership is once again present.
                configured.Remove(id);
                SaveOrChangeMembership(id);
                if (CanSave(id, currentDraft)) return false;
                configured.Add(id.ToUpperInvariant());
                SaveOrChangeMembership(id.ToUpperInvariant());
                return !CanSave(id, currentDraft) &&
                       CanSave(id, Version(id)) &&
                       Version(id) == Version(id.ToUpperInvariant());
            }
            finally
            {
                if (shellRoot != null)
                    UnityEngine.Object.DestroyImmediate(shellRoot);
            }
        }

        private static bool TrackingStaleConfigureButtonsRejectRemovedSources()
        {
            var id = "vcr-parity-stale-configure-" + Guid.NewGuid().ToString("N");
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var edit = type.GetMethod("EditWorkspaceTrackingSettings", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var invalidate = type.GetMethod("InvalidateWorkspaceTrackingEditors", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var editorField = type.GetField("_workspaceTrackingEditor", flags);
                var feedbackField = type.GetField("_workspaceTrackingFeedback", flags);
                if (edit == null || edit.GetParameters().Length != 3 ||
                    version == null || invalidate == null || configuredField == null ||
                    editorField == null || feedbackField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeAt = code.IndexOf(
                    "private void ShowWorkspaceNativeTrackingDetails(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private bool IsWorkspaceTrackingMembershipStateCurrent(", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                var actionAt = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var actionEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelAt = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                if (nativeAt < 0 || nativeEnd <= nativeAt || toolkitAt < 0 ||
                    actionAt < 0 || actionEnd <= actionAt ||
                    panelAt < 0 || panelEnd <= panelAt)
                    return false;
                var native = code.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = code.Substring(toolkitAt);
                var action = code.Substring(actionAt, actionEnd - actionAt);
                var panel = code.Substring(panelAt, panelEnd - panelAt);
                var nativeConfigureAt = action.IndexOf(
                    "if (action < 5200)", StringComparison.Ordinal);
                var nativeToggleAt = action.IndexOf(
                    "else if (action < 5400)", StringComparison.Ordinal);
                if (nativeConfigureAt < 0 || nativeToggleAt <= nativeConfigureAt)
                    return false;
                var configureBranch = action.Substring(
                    nativeConfigureAt, nativeToggleAt - nativeConfigureAt);
                var nativeGuard = native.IndexOf(
                    "_workspaceConfiguredTracking.Contains(controlId)", StringComparison.Ordinal);
                var nativeRead = native.IndexOf(
                    "configurable.GetConfigurationSettings()", StringComparison.Ordinal);
                var toolkitGuard = toolkit.IndexOf(
                    "_workspaceConfiguredTracking.Contains(sourceId)", StringComparison.Ordinal);
                var toolkitRead = toolkit.IndexOf(
                    "configurable.GetConfigurationSettings()", StringComparison.Ordinal);
                var toolkitVersionGuard = toolkit.IndexOf(
                    "IsWorkspaceTrackingEditVersionCurrent(sourceId, displayedVersion)",
                    StringComparison.Ordinal);
                if (nativeGuard < 0 || nativeRead <= nativeGuard ||
                    toolkitGuard < 0 || toolkitRead <= toolkitGuard ||
                    toolkitVersionGuard < 0 || toolkitRead <= toolkitVersionGuard ||
                    !native.Contains("RebuildWorkspaceNativeTrackingSources()") ||
                    !toolkit.Contains("_workspaceTrackingEditor.Clear()") ||
                    !configureBranch.Contains("_workspaceNativeTrackingVersions.TryGetValue(slot") ||
                    !configureBranch.Contains("IsWorkspaceTrackingEditVersionCurrent(id, displayedVersion)") ||
                    configureBranch.IndexOf("IsWorkspaceTrackingEditVersionCurrent(",
                        StringComparison.Ordinal) >
                    configureBranch.IndexOf("ShowWorkspaceNativeTrackingDetails(id)",
                        StringComparison.Ordinal) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(panel,
                        @"EditWorkspaceTrackingSettings\(\s*sourceId, source, displayedVersion\)"))
                    return false;

                shellRoot = new GameObject("P1 stale configure shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 stale configure provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id.ToLowerInvariant();
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;
                var editor = new UnityEngine.UIElements.VisualElement();
                var feedback = new UnityEngine.UIElements.Label();
                editorField.SetValue(shell, editor);
                feedbackField.SetValue(shell, feedback);

                // A stale Configure button must not inspect live fields
                // on an unconfigured device or offer Save.
                var initialVersion = (int)version.Invoke(shell, new object[] { id });
                edit.Invoke(shell, new object[] { id, provider, initialVersion });
                if (provider.SchemaCalls != 0 || editor.childCount != 0 ||
                    !feedback.text.Contains("Add"))
                    return false;

                configured.Add(id.ToUpperInvariant());
                edit.Invoke(shell, new object[] { id, provider, initialVersion });
                if (provider.SchemaCalls != 1 || editor.childCount == 0 ||
                    provider.ConfigurationCalls != 0)
                    return false;

                // Another UI removes membership without replacing the
                // component. A stale Configure click must clear old fields.
                configured.Remove(id);
                invalidate.Invoke(shell, new object[] { id });
                edit.Invoke(shell, new object[] { id, provider, initialVersion });
                if (provider.SchemaCalls != 1 ||
                    provider.ConfigurationCalls != 0 ||
                    editor.childCount != 0 || configured.Contains(id) ||
                    !feedback.text.Contains("removed"))
                    return false;

                // Remove->Add returns to the same provider and membership
                // state. The original Configure button belongs to the old
                // revision and must not open an editor for the new session.
                configured.Add(id.ToUpperInvariant());
                invalidate.Invoke(shell, new object[] { id.ToUpperInvariant() });
                var latestVersion = (int)version.Invoke(shell, new object[] { id });
                if (latestVersion == initialVersion) return false;
                edit.Invoke(shell, new object[] { id, provider, initialVersion });
                if (provider.SchemaCalls != 1 ||
                    provider.ConfigurationCalls != 0 ||
                    editor.childCount != 0 ||
                    !configured.Contains(id) ||
                    !feedback.text.Contains("Select Configure again"))
                    return false;

                // A fresh Configure on the newly registered session works.
                edit.Invoke(shell, new object[] { id, provider, latestVersion });
                return provider.SchemaCalls == 2 &&
                       provider.ConfigurationCalls == 0 &&
                       editor.childCount > 0 &&
                       configured.Contains(id);
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
            }
        }

        private static bool TrackingSettingsSaveRequiresCurrentMembership()
        {
            var id = "vcr-parity-save-membership-" + Guid.NewGuid().ToString("N");
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var guard = type.GetMethod(
                    "IsWorkspaceTrackingConfiguredEditorCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (guard == null || configuredField == null) return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeStart = source.IndexOf(
                    "private void SaveWorkspaceNativeTrackingDetails()", StringComparison.Ordinal);
                var nativeEnd = source.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var toolkitStart = source.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                if (nativeStart < 0 || nativeEnd <= nativeStart || toolkitStart < 0)
                    return false;

                var nativeSave = source.Substring(nativeStart, nativeEnd - nativeStart);
                var toolkitEditor = source.Substring(toolkitStart);
                // Both Save paths check the current membership before Apply.
                // A successful Save must never implicitly add membership.
                if (!nativeSave.Contains("IsWorkspaceTrackingConfiguredEditorCurrent(") ||
                    !toolkitEditor.Contains("IsWorkspaceTrackingConfiguredEditorCurrent(") ||
                    nativeSave.IndexOf("IsWorkspaceTrackingConfiguredEditorCurrent(",
                        StringComparison.Ordinal) >
                    nativeSave.IndexOf("TryApplyWorkspaceTrackingSettings(", StringComparison.Ordinal) ||
                    toolkitEditor.IndexOf("IsWorkspaceTrackingConfiguredEditorCurrent(",
                        StringComparison.Ordinal) >
                    toolkitEditor.IndexOf("TryApplyWorkspaceTrackingSettings(", StringComparison.Ordinal) ||
                    nativeSave.Contains("_workspaceConfiguredTracking.Add(") ||
                    toolkitEditor.Contains("_workspaceConfiguredTracking.Add("))
                    return false;

                shellRoot = new GameObject("P1 tracking Save membership shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 tracking Save membership source");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;

                bool CanSave(ITrackingRuntimeControl displayed,
                    ITrackingRuntimeControl live) =>
                    (bool)guard.Invoke(shell, new object[] { id, displayed, live });

                // Opening a stale editor while unconfigured cannot authorize
                // Save; adding a source is an explicit independent action.
                if (CanSave(provider, provider)) return false;
                configured.Add(id);
                if (!CanSave(provider, provider)) return false;
                configured.Remove(id);
                if (CanSave(provider, provider) ||
                    configured.Contains(id) ||
                    provider.ConfigurationCalls != 0)
                    return false;

                // The same logical ID with a new physical provider must not
                // revive an old editor, even if membership was re-added.
                configured.Add(id.ToUpperInvariant());
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 tracking Save replacement");
                var replacement = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                replacement.Id = id;
                return !CanSave(provider, replacement) &&
                       CanSave(replacement, replacement) &&
                       replacement.ConfigurationCalls == 0;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
            }
        }

        private static bool TrackingStaleDeviceActionsRejectReplacement()
        {
            var id = "vcr-parity-stale-action-" + Guid.NewGuid().ToString("N");
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var panelGuard = type.GetMethod("IsWorkspaceTrackingPanelActionCurrent", flags);
                var membership = type.GetMethod("UpdateWorkspaceTrackingMembership", flags);
                var toggle = type.GetMethod("ToggleWorkspaceTrackingEnabled", flags);
                var recover = type.GetMethod("RecoverWorkspaceTrackingSource", flags);
                var nativeSources = type.GetField("_workspaceNativeTrackingProviders", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (panelGuard == null || membership == null || toggle == null ||
                    recover == null || nativeSources == null || configuredField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeStart = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelStart = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                if (nativeStart < 0 || nativeEnd <= nativeStart ||
                    panelStart < 0 || panelEnd <= panelStart ||
                    !code.Substring(nativeStart, nativeEnd - nativeStart).Contains(
                        "IsWorkspaceTrackingEditorSourceCurrent(displayed, control)") ||
                    !code.Substring(panelStart, panelEnd - panelStart).Contains(
                        "behaviour.GetInstanceID()") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        code.Substring(panelStart, panelEnd - panelStart),
                        @"UpdateWorkspaceTrackingMembership\(\s*sourceId, source, configured, displayedVersion\)") ||
                    !code.Substring(panelStart, panelEnd - panelStart).Contains(
                        "ToggleWorkspaceTrackingEnabled(\n                                " +
                        "sourceId, source, displayedEnabled, displayedVersion)") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        code.Substring(panelStart, panelEnd - panelStart),
                        @"RecoverWorkspaceTrackingSource\(\s*sourceId, source, displayedEnabled, displayedVersion\)") ||
                    !code.Substring(panelStart, panelEnd - panelStart).Contains(
                        "EditWorkspaceTrackingSettings(\n                                    " +
                        "sourceId, source, displayedVersion)"))
                    return false;

                shellRoot = new GameObject("P1 stale tracking button shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var native = nativeSources.GetValue(shell) as
                    Dictionary<int, ITrackingRuntimeControl>;
                if (configured == null || native == null) return false;
                configured.Add(id);

                sourceRoot = new GameObject("P1 stale tracking button original");
                var original = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                original.Id = id;
                var originalInstanceId = original.GetInstanceID();
                native[0] = original;
                if (!(bool)panelGuard.Invoke(shell, new object[] { id, original }))
                    return false;

                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 stale tracking button replacement");
                var replacement = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                replacement.Id = id;

                if (replacement.GetInstanceID() == originalInstanceId ||
                    (bool)panelGuard.Invoke(shell, new object[] { id, original }) ||
                    !(bool)panelGuard.Invoke(shell, new object[] { id, replacement }) ||
                    !ReferenceEquals(native[0], original))
                    return false;

                // A stale Add/Remove or Toggle must not operate on the new
                // provider or change logical membership.
                membership.Invoke(shell, new object[] { id, original, true, 0 });
                toggle.Invoke(shell, new object[] { id, original, false, 0 });
                recover.Invoke(shell, new object[] { id, original, true, 0 });
                return configured.Contains(id) &&
                       replacement.ToggleCalls == 0 &&
                       replacement.RecoveryCalls == 0;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
            }
        }

        private static bool TrackingMembershipStopRequiresDisabledReadback()
        {
            var id = "vcr-parity-stop-postcondition-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var stop = type.GetMethod("TryStopWorkspaceTrackingForMembership", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                if (stop == null || version == null ||
                    configuredField == null || restoredField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var helperAt = code.IndexOf(
                    "private bool TryStopWorkspaceTrackingForMembership(", StringComparison.Ordinal);
                var helperEnd = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                if (helperAt < 0 || helperEnd <= helperAt)
                    return false;
                var helper = code.Substring(helperAt, helperEnd - helperAt);
                var postconditionAt = helper.IndexOf(
                    "if (stopped && control.ControlEnabled)", StringComparison.Ordinal);
                var returnAt = helper.IndexOf("if (stopped)\n                return true;",
                    StringComparison.Ordinal);
                if (postconditionAt < 0 || returnAt <= postconditionAt ||
                    !helper.Contains("stopped = false;") ||
                    !helper.Contains("remains enabled"))
                    return false;

                shellRoot = new GameObject("P1 membership Stop postcondition shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 membership Stop postcondition source");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null ||
                    !provider.TrySetControlEnabled(true, out _))
                    return false;
                configured.Add(id);
                restored[id] = provider;
                PlayerPrefs.SetInt(enabledKey, 1);
                var beforeVersion = (int)version.Invoke(shell, new object[] { id });

                bool Stop(out string failure)
                {
                    var args = new object[] { provider, null };
                    var accepted = (bool)stop.Invoke(shell, args);
                    failure = args[1] as string;
                    return accepted;
                }

                // Some third-party implementations acknowledge Stop before
                // they really change ControlEnabled. Removal must not proceed.
                provider.AcknowledgeDisableWithoutStoppingOnce = true;
                if (Stop(out var error) ||
                    string.IsNullOrEmpty(error) ||
                    !error.Contains("remains enabled") ||
                    !provider.ControlEnabled ||
                    !configured.Contains(id) ||
                    !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1 ||
                    (int)version.Invoke(shell, new object[] { id }) != beforeVersion)
                    return false;

                // Unregistered Add must likewise refuse a false-positive Stop
                // and must not acquire or persist ownership of the live source.
                configured.Remove(id);
                restored.Remove(id);
                provider.AcknowledgeDisableWithoutStoppingOnce = true;
                if (Stop(out error) ||
                    string.IsNullOrEmpty(error) ||
                    provider.ControlEnabled == false ||
                    configured.Contains(id) ||
                    restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // A confirmed Stop is still accepted; no permanent block.
                return Stop(out _) && !provider.ControlEnabled;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingFailedMembershipStopPreservesRegistration()
        {
            var id = "vcr-parity-membership-stop-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var stop = type.GetMethod("TryStopWorkspaceTrackingForMembership", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (stop == null || restore == null || version == null ||
                    canSave == null || configuredField == null ||
                    restoredField == null || scanField == null)
                    return false;

                var sourceCode = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var helperAt = sourceCode.IndexOf(
                    "private bool TryStopWorkspaceTrackingForMembership(", StringComparison.Ordinal);
                var nativeAt = sourceCode.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = sourceCode.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var toolkitAt = sourceCode.IndexOf(
                    "private void UpdateWorkspaceTrackingMembership(", StringComparison.Ordinal);
                var toolkitEnd = sourceCode.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                if (helperAt < 0 || nativeAt <= helperAt ||
                    nativeEnd <= nativeAt || toolkitAt < 0 ||
                    toolkitEnd <= toolkitAt)
                    return false;
                var helper = sourceCode.Substring(helperAt, nativeAt - helperAt);
                var native = sourceCode.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = sourceCode.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!helper.Contains("control.TrySetControlEnabled(false, out error)") ||
                    !helper.Contains("if (stateChanged)") ||
                    !helper.Contains("InvalidateWorkspaceTrackingEditors(id)") ||
                    !helper.Contains("_workspaceRestoredTracking.Remove(id)") ||
                    !helper.Contains("_workspaceConfiguredTracking.Contains(id)") ||
                    !helper.Contains("_nextTrackingRestoreScan = 0f") ||
                    !native.Contains("TryStopWorkspaceTrackingForMembership(control,") ||
                    !native.Contains("IsWorkspaceTrackingEditVersionCurrent(id, displayedVersion)") ||
                    !toolkit.Contains("TryStopWorkspaceTrackingForMembership(source,") ||
                    !toolkit.Contains("IsWorkspaceTrackingEditVersionCurrent(sourceId, displayedVersion)"))
                    return false;

                shellRoot = new GameObject("P1 partial membership stop shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 partial membership stop provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;
                configured.Add(id);
                if (!provider.TrySetControlEnabled(true, out _)) return false;
                restored[id] = provider;
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "membership-saved-setting");

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int opened) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), opened });
                bool Stop(out string feedback)
                {
                    var args = new object[] { provider, null };
                    var success = (bool)stop.Invoke(shell, args);
                    feedback = args[1] as string;
                    return success;
                }
                void Scan()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                // A clean rejected Remove leaves the running, registered
                // device, previous revision and saved preference untouched.
                var original = Version();
                provider.FailDisableOnce = true;
                if (Stop(out _) || !provider.ControlEnabled ||
                    !configured.Contains(id) || !restored.ContainsKey(id) ||
                    Version() != original || !DraftValid(original) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // If failed Remove already turned capture Off, do not commit
                // the removal. Restore must replay saved On and settings.
                provider.FailDisableAfterMutationOnce = true;
                if (Stop(out var error) || provider.ControlEnabled ||
                    !configured.Contains(id) || restored.ContainsKey(id) ||
                    Version() == original || DraftValid(original) ||
                    !error.Contains("Live tracking state changed") ||
                    (float)scanField.GetValue(shell) != 0f ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;
                Scan();
                if (!provider.ControlEnabled ||
                    provider.AppliedValue != "membership-saved-setting" ||
                    provider.ConfigurationCalls != 1 ||
                    !restored.ContainsKey(id))
                    return false;
                var stable = Version();
                Scan();
                if (Version() != stable || provider.ConfigurationCalls != 1)
                    return false;

                // Failed Add must not register an external source nor
                // automatically re-enable it: U1 does not own it yet.
                configured.Remove(id);
                restored.Remove(id);
                var beforeUnregistered = Version();
                var oldScan = (float)scanField.GetValue(shell);
                provider.FailDisableAfterMutationOnce = true;
                if (Stop(out error) || provider.ControlEnabled ||
                    configured.Contains(id) || restored.ContainsKey(id) ||
                    Version() == beforeUnregistered ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1 ||
                    (float)scanField.GetValue(shell) != oldScan)
                    return false;
                Scan();
                return !provider.ControlEnabled &&
                       !configured.Contains(id) &&
                       !restored.ContainsKey(id) &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 1;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingStaleMembershipActionsRejectStateChanges()
        {
            var id = "vcr-parity-stale-membership-" + Guid.NewGuid().ToString("N");
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var membership = type.GetMethod("UpdateWorkspaceTrackingMembership", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var nativeMembershipField = type.GetField(
                    "_workspaceNativeTrackingConfigured", flags);
                if (membership == null || configuredField == null ||
                    nativeMembershipField == null ||
                    membership.GetParameters().Length != 4)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var rebuildAt = code.IndexOf(
                    "private void RebuildWorkspaceNativeTrackingSources(", StringComparison.Ordinal);
                var rebuildEnd = code.IndexOf(
                    "private static bool IsWorkspaceTrackingEditorSourceCurrent(", StringComparison.Ordinal);
                var nativeAt = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelAt = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void UpdateWorkspaceTrackingMembership(", StringComparison.Ordinal);
                var toolkitEnd = code.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                if (rebuildAt < 0 || rebuildEnd <= rebuildAt ||
                    nativeAt < 0 || nativeEnd <= nativeAt ||
                    panelAt < 0 || panelEnd <= panelAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt)
                    return false;
                var rebuildCode = code.Substring(rebuildAt, rebuildEnd - rebuildAt);
                var nativeCode = code.Substring(nativeAt, nativeEnd - nativeAt);
                var panelCode = code.Substring(panelAt, panelEnd - panelAt);
                var toolkitCode = code.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!rebuildCode.Contains("_workspaceNativeTrackingConfigured[i] = configured") ||
                    !nativeCode.Contains("_workspaceNativeTrackingConfigured.TryGetValue(slot") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(panelCode,
                        @"UpdateWorkspaceTrackingMembership\(\s*sourceId, source, configured, displayedVersion\)") ||
                    !toolkitCode.Contains(
                        "IsWorkspaceTrackingMembershipIntentCurrent(") ||
                    nativeCode.IndexOf("IsWorkspaceTrackingMembershipIntentCurrent(",
                        StringComparison.Ordinal) > nativeCode.IndexOf(
                        "if (remove && control.ControlEnabled", StringComparison.Ordinal) ||
                    toolkitCode.IndexOf("IsWorkspaceTrackingMembershipIntentCurrent(",
                        StringComparison.Ordinal) > toolkitCode.IndexOf(
                        "TryStopWorkspaceTrackingForMembership(source,", StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 stale membership shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 stale membership source");
                var source = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                source.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var nativeMembership = nativeMembershipField.GetValue(shell) as
                    Dictionary<int, bool>;
                if (configured == null || nativeMembership == null) return false;

                // Button still says Add, but another surface has already added it.
                configured.Add(id);
                nativeMembership[0] = false;
                membership.Invoke(shell, new object[] { id, source, false, 0 });
                if (!configured.Contains(id) || source.ToggleCalls != 0 ||
                    nativeMembership[0])
                    return false;

                // Button still says Remove, but another surface removed it.
                configured.Remove(id);
                nativeMembership[0] = true;
                membership.Invoke(shell, new object[] { id.ToUpperInvariant(), source, true, 0 });
                return !configured.Contains(id) && source.ToggleCalls == 0 &&
                       nativeMembership[0];
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
            }
        }

        private static bool TrackingMembershipABAActionsRejectPriorRevisions()
        {
            var id = "vcr-parity-membership-aba-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            const string configuredKey = "VCR.TrackingInputs.Configured.v1";
            var hadConfiguredKey = PlayerPrefs.HasKey(configuredKey);
            var previousConfigured = PlayerPrefs.GetString(configuredKey, "");
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var membership = type.GetMethod("UpdateWorkspaceTrackingMembership", flags);
                var intent = type.GetMethod("IsWorkspaceTrackingMembershipIntentCurrent", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var nativeVersionsField = type.GetField("_workspaceNativeTrackingVersions", flags);
                if (membership == null || membership.GetParameters().Length != 4 ||
                    intent == null || version == null || configuredField == null ||
                    nativeVersionsField == null)
                    return false;
                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeAt = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelAt = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                var actionAt = code.IndexOf(
                    "private void UpdateWorkspaceTrackingMembership(", StringComparison.Ordinal);
                var actionEnd = code.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                if (nativeAt < 0 || nativeEnd <= nativeAt ||
                    panelAt < 0 || panelEnd <= panelAt ||
                    actionAt < 0 || actionEnd <= actionAt)
                    return false;
                var nativeCode = code.Substring(nativeAt, nativeEnd - nativeAt);
                var panelCode = code.Substring(panelAt, panelEnd - panelAt);
                var actionCode = code.Substring(actionAt, actionEnd - actionAt);
                if (!nativeCode.Contains("_workspaceNativeTrackingVersions.TryGetValue(slot") ||
                    !nativeCode.Contains("IsWorkspaceTrackingMembershipIntentCurrent(") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(panelCode,
                        @"UpdateWorkspaceTrackingMembership\(\s*sourceId, source, configured, displayedVersion\)") ||
                    !panelCode.Contains(
                        "GetWorkspaceTrackingEditVersion(source.ControlId) +") ||
                    !actionCode.Contains("IsWorkspaceTrackingMembershipIntentCurrent(") ||
                    nativeCode.IndexOf("IsWorkspaceTrackingMembershipIntentCurrent(",
                        StringComparison.Ordinal) >
                    nativeCode.IndexOf("TryStopWorkspaceTrackingForMembership(control,",
                        StringComparison.Ordinal) ||
                    actionCode.IndexOf("IsWorkspaceTrackingMembershipIntentCurrent(",
                        StringComparison.Ordinal) >
                    actionCode.IndexOf("TryStopWorkspaceTrackingForMembership(source,",
                        StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 membership ABA shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 membership ABA provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var nativeVersions = nativeVersionsField.GetValue(shell) as
                    Dictionary<int, int>;
                if (configured == null || nativeVersions == null) return false;

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool Current(bool displayedConfigured, int displayedVersion) =>
                    (bool)intent.Invoke(shell, new object[] {
                        id.ToUpperInvariant(), displayedConfigured, displayedVersion
                    });
                void Click(bool displayedConfigured, int displayedVersion) =>
                    membership.Invoke(shell, new object[] {
                        id, provider, displayedConfigured, displayedVersion
                    });

                var initial = Version();
                nativeVersions[0] = initial;
                if (!Current(false, initial)) return false;
                Click(false, initial); // Another surface adds the provider.
                var addedVersion = Version();
                if (!configured.Contains(id) || addedVersion == initial ||
                    !Current(true, addedVersion))
                    return false;
                Click(true, addedVersion); // Then removes it again.
                var removedVersion = Version();
                if (configured.Contains(id) || removedVersion == initial ||
                    Current(false, initial))
                    return false;
                Click(false, initial); // Stale Add from the initial session.
                if (configured.Contains(id) || Version() != removedVersion ||
                    provider.ToggleCalls != 0 ||
                    nativeVersions[0] != initial)
                    return false;

                Click(false, removedVersion); // Fresh Add.
                var secondAddedVersion = Version();
                if (!configured.Contains(id) ||
                    secondAddedVersion == removedVersion ||
                    !Current(true, secondAddedVersion))
                    return false;
                Click(true, secondAddedVersion); // Another surface removes.
                var secondRemovedVersion = Version();
                Click(false, secondRemovedVersion); // Another surface adds.
                var thirdAddedVersion = Version();
                if (!configured.Contains(id) ||
                    thirdAddedVersion == secondAddedVersion ||
                    Current(true, secondAddedVersion))
                    return false;

                Click(true, secondAddedVersion); // Stale Remove, same membership.
                if (!configured.Contains(id) ||
                    Version() != thirdAddedVersion ||
                    provider.ToggleCalls != 0)
                    return false;

                Click(true, thirdAddedVersion); // Fresh Remove still works.
                return !configured.Contains(id) &&
                       Version() != thirdAddedVersion &&
                       provider.ToggleCalls == 0;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                if (hadConfiguredKey)
                    PlayerPrefs.SetString(configuredKey, previousConfigured);
                else
                    PlayerPrefs.DeleteKey(configuredKey);
                PlayerPrefs.Save();
            }
        }

        private static bool TrackingAcknowledgedToggleRequiresMatchingReadback()
        {
            var id = "vcr-parity-toggle-ack-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (toggle == null || restore == null || version == null ||
                    canSave == null || configuredField == null ||
                    restoredField == null || scanField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var coreAt = source.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var coreEnd = source.IndexOf(
                    "private bool TryRequestWorkspaceTrackingRecovery(", StringComparison.Ordinal);
                var backgroundAt = source.IndexOf(
                    "private void RestoreWorkspaceTrackingPreferences()", StringComparison.Ordinal);
                var backgroundEnd = source.IndexOf(
                    "private string GetWorkspaceTrackingSummary()", StringComparison.Ordinal);
                if (coreAt < 0 || coreEnd <= coreAt ||
                    backgroundAt < 0 || backgroundEnd <= backgroundAt)
                    return false;
                var core = source.Substring(coreAt, coreEnd - coreAt);
                var background = source.Substring(backgroundAt, backgroundEnd - backgroundAt);
                if (!core.Contains("control.ControlEnabled != enabled") ||
                    !core.Contains("unconfirmedToggle = acknowledged") ||
                    !core.Contains("if (stateChanged || unconfirmedToggle)") ||
                    !background.Contains("!stopAcknowledged || source.ControlEnabled") ||
                    !background.Contains("!toggleAcknowledged || source.ControlEnabled != enabled") ||
                    !background.Contains("_workspaceRestoredTracking.Remove(id)"))
                    return false;

                shellRoot = new GameObject("P1 acknowledged-toggle shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 acknowledged-toggle provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null)
                    return false;
                configured.Add(id);
                restored[id] = provider;
                PlayerPrefs.SetInt(enabledKey, 0);

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int initial) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), initial });
                bool Toggle(bool enabled, out string feedback)
                {
                    var args = new object[] { provider, enabled, null };
                    var success = (bool)toggle.Invoke(shell, args);
                    feedback = args[2] as string;
                    return success;
                }
                void Scan()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                // Acknowledged Enable without enabling cannot persist On.
                var initial = Version();
                provider.AcknowledgeEnableWithoutChangingOnce = true;
                if (Toggle(true, out var feedback) ||
                    !feedback.Contains("did not change") ||
                    provider.ControlEnabled ||
                    restored.ContainsKey(id) ||
                    Version() == initial || DraftValid(initial) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;
                Scan(); // Original saved Off is authoritative.
                if (provider.ControlEnabled || !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // Successful explicit Enable still persists and caches.
                if (!Toggle(true, out _) || !provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1 ||
                    !restored.ContainsKey(id))
                    return false;
                var beforeDisable = Version();
                provider.AcknowledgeDisableWithoutStoppingOnce = true;
                if (Toggle(false, out feedback) ||
                    !feedback.Contains("did not change") ||
                    !provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() == beforeDisable || DraftValid(beforeDisable) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;
                Scan(); // The existing saved On is unchanged.
                if (!provider.ControlEnabled || !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // Automatic restoration must not mark a true-without-enable
                // response complete. The next scan should retry and succeed.
                if (!provider.TrySetControlEnabled(false, out _))
                    return false;
                restored.Remove(id);
                provider.AcknowledgeEnableWithoutChangingOnce = true;
                var beforeAutomaticEnable = Version();
                Scan();
                if (provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() != beforeAutomaticEnable ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;
                Scan();
                if (!provider.ControlEnabled || !restored.ContainsKey(id) ||
                    Version() == beforeAutomaticEnable ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // Automatic saved-Off stop has the symmetric postcondition.
                PlayerPrefs.SetInt(enabledKey, 0);
                restored.Remove(id);
                provider.AcknowledgeDisableWithoutStoppingOnce = true;
                var beforeAutomaticStop = Version();
                Scan();
                if (!provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() != beforeAutomaticStop ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;
                Scan();
                return !provider.ControlEnabled &&
                       restored.ContainsKey(id) &&
                       Version() != beforeAutomaticStop &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 0;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingFailedToggleRestoresSavedState()
        {
            var id = "vcr-parity-toggle-partial-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (toggle == null || restore == null || version == null ||
                    canSave == null || configuredField == null ||
                    restoredField == null || scanField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var coreAt = code.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var coreEnd = code.IndexOf(
                    "private bool TryRequestWorkspaceTrackingRecovery(", StringComparison.Ordinal);
                var nativeAt = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var toolkitEnd = code.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(", StringComparison.Ordinal);
                var restoreAt = code.IndexOf(
                    "private void RestoreWorkspaceTrackingPreferences()", StringComparison.Ordinal);
                var restoreEnd = code.IndexOf(
                    "private string GetWorkspaceTrackingSummary()", StringComparison.Ordinal);
                if (coreAt < 0 || coreEnd <= coreAt ||
                    nativeAt < 0 || nativeEnd <= nativeAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt ||
                    restoreAt < 0 || restoreEnd <= restoreAt)
                    return false;
                var core = code.Substring(coreAt, coreEnd - coreAt);
                var native = code.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = code.Substring(toolkitAt, toolkitEnd - toolkitAt);
                var background = code.Substring(restoreAt, restoreEnd - restoreAt);
                if (!background.Contains("source.ControlEnabled != beforeRestoreEnabled") ||
                    !background.Contains("source.ControlHealthState != beforeRestoreHealth") ||
                    !background.Contains("if (acceptedMutation)") ||
                    !core.Contains("finally") ||
                    !core.Contains("control.ControlEnabled != beforeEnabled") ||
                    !core.Contains("control.ControlHealthState != beforeHealth") ||
                    !core.Contains("if (stateChanged || unconfirmedToggle)") ||
                    !core.Contains("InvalidateWorkspaceTrackingEditors(id)") ||
                    !core.Contains("_workspaceRestoredTracking.Remove(id)") ||
                    !core.Contains("_nextTrackingRestoreScan = 0f") ||
                    !native.Contains(
                        "!IsWorkspaceTrackingEditVersionCurrent(id, displayedVersion)") ||
                    !toolkit.Contains(
                        "!IsWorkspaceTrackingEditVersionCurrent(sourceId, displayedVersion)"))
                    return false;

                shellRoot = new GameObject("P1 failed toggle restore shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 failed toggle source");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;
                configured.Add(id);
                restored[id] = provider;
                PlayerPrefs.SetInt(enabledKey, 0);

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int opened) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), opened });
                bool Toggle(bool next)
                {
                    var args = new object[] { provider, next, null };
                    return (bool)toggle.Invoke(shell, args);
                }
                void Restore()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                // A clean rejected Enable must not discard the editor or
                // reapply preferences that the provider never changed.
                var original = Version();
                provider.FailEnableOnce = true;
                if (Toggle(true) || provider.ControlEnabled ||
                    provider.ToggleCalls != 1 ||
                    !restored.ContainsKey(id) || Version() != original ||
                    !DraftValid(original) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // A rejected Enable may have turned capture On. Persisted
                // Off must be preserved and reconciled on the next scan.
                provider.FailEnableAfterMutationOnce = true;
                if (Toggle(true) || !provider.ControlEnabled ||
                    provider.ToggleCalls != 2 ||
                    restored.ContainsKey(id) || Version() == original ||
                    DraftValid(original) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0 ||
                    (float)scanField.GetValue(shell) != 0f)
                    return false;
                Restore();
                if (provider.ControlEnabled ||
                    provider.ToggleCalls != 3 ||
                    !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // Symmetric failure: a Disable may have turned capture Off.
                // Saved On must be reasserted without recording the failed
                // request as a new persistent user preference.
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;
                PlayerPrefs.SetInt(enabledKey, 1);
                restored[id] = provider;
                var beforeDisable = Version();
                provider.FailDisableAfterMutationOnce = true;
                if (Toggle(false) || provider.ControlEnabled ||
                    restored.ContainsKey(id) || Version() == beforeDisable ||
                    DraftValid(beforeDisable) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;
                Restore();
                if (!provider.ControlEnabled || !restored.ContainsKey(id) ||
                    provider.ToggleCalls != 6 ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // No mutation on a failed Disable leaves the existing
                // editor revision and restored-instance cache intact.
                var stable = Version();
                provider.FailDisableOnce = true;
                if (Toggle(false) || !provider.ControlEnabled ||
                    Version() != stable || !DraftValid(stable) ||
                    !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // The same failure-after-write can occur during automatic
                // restoration, not only from explicit Native/Toolkit actions.
                // A failed stop must still invalidate the pre-restore draft.
                PlayerPrefs.SetInt(enabledKey, 0);
                restored.Remove(id);
                provider.FailDisableAfterMutationOnce = true;
                var beforeAutomaticStop = Version();
                Restore();
                if (provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() == beforeAutomaticStop ||
                    DraftValid(beforeAutomaticStop) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;
                var afterAutomaticStop = Version();
                Restore();
                if (provider.ControlEnabled || !restored.ContainsKey(id) ||
                    Version() != afterAutomaticStop)
                    return false;

                // A failed start can likewise leave capture On. Preserve
                // saved On and the invalidation, then settle without replay.
                PlayerPrefs.SetInt(enabledKey, 1);
                restored.Remove(id);
                provider.FailEnableAfterMutationOnce = true;
                var beforeAutomaticStart = Version();
                Restore();
                if (!provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() == beforeAutomaticStart ||
                    DraftValid(beforeAutomaticStart) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;
                var afterAutomaticStart = Version();
                Restore();
                return provider.ControlEnabled &&
                       restored.ContainsKey(id) &&
                       Version() == afterAutomaticStart &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 1;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingStaleToggleActionsRejectStateChanges()
        {
            var id = "vcr-parity-stale-toggle-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = type.GetMethod("ToggleWorkspaceTrackingEnabled", flags);
                var setEnabled = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var intent = type.GetMethod("IsWorkspaceTrackingToggleIntentCurrent", flags);
                var getVersion = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var nativeEnabledField = type.GetField("_workspaceNativeTrackingEnabled", flags);
                var nativeVersionField = type.GetField("_workspaceNativeTrackingVersions", flags);
                if (toggle == null || toggle.GetParameters().Length != 4 ||
                    setEnabled == null || intent == null || getVersion == null ||
                    configuredField == null || nativeEnabledField == null ||
                    nativeVersionField == null)
                    return false;
                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var rebuildAt = code.IndexOf(
                    "private void RebuildWorkspaceNativeTrackingSources(", StringComparison.Ordinal);
                var rebuildEnd = code.IndexOf(
                    "private static bool IsWorkspaceTrackingEditorSourceCurrent(", StringComparison.Ordinal);
                var nativeAt = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelAt = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var toolkitEnd = code.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(", StringComparison.Ordinal);
                if (rebuildAt < 0 || rebuildEnd <= rebuildAt ||
                    nativeAt < 0 || nativeEnd <= nativeAt ||
                    panelAt < 0 || panelEnd <= panelAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt)
                    return false;
                var rebuildCode = code.Substring(rebuildAt, rebuildEnd - rebuildAt);
                var nativeCode = code.Substring(nativeAt, nativeEnd - nativeAt);
                var panelCode = code.Substring(panelAt, panelEnd - panelAt);
                var toolkitCode = code.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!rebuildCode.Contains("_workspaceNativeTrackingEnabled[i] = source.ControlEnabled") ||
                    !rebuildCode.Contains("_workspaceNativeTrackingVersions[i] =") ||
                    !nativeCode.Contains("_workspaceNativeTrackingEnabled.TryGetValue(slot") ||
                    !nativeCode.Contains("_workspaceNativeTrackingVersions.TryGetValue(slot") ||
                    !panelCode.Contains("var displayedEnabled = source.ControlEnabled") ||
                    !panelCode.Contains("var displayedVersion = GetWorkspaceTrackingEditVersion(sourceId)") ||
                    !panelCode.Contains("sourceId, source, displayedEnabled, displayedVersion)") ||
                    !toolkitCode.Contains("IsWorkspaceTrackingToggleIntentCurrent(") ||
                    !nativeCode.Contains("IsWorkspaceTrackingToggleIntentCurrent(") ||
                    nativeCode.IndexOf("IsWorkspaceTrackingToggleIntentCurrent(",
                        StringComparison.Ordinal) >
                    nativeCode.IndexOf("TrySetWorkspaceTrackingEnabled(",
                        StringComparison.Ordinal) ||
                    toolkitCode.IndexOf("IsWorkspaceTrackingToggleIntentCurrent(",
                        StringComparison.Ordinal) >
                    toolkitCode.IndexOf("TrySetWorkspaceTrackingEnabled(",
                        StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 stale tracking toggle shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 stale tracking toggle provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var nativeEnabled = nativeEnabledField.GetValue(shell) as
                    Dictionary<int, bool>;
                var nativeVersions = nativeVersionField.GetValue(shell) as
                    Dictionary<int, int>;
                if (configured == null || nativeEnabled == null || nativeVersions == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);

                int Version() => (int)getVersion.Invoke(shell, new object[] { id });
                bool Current(bool displayedEnabled, int displayedVersion) =>
                    (bool)intent.Invoke(shell, new object[] {
                        id.ToUpperInvariant(), provider, displayedEnabled, displayedVersion
                    });
                void Click(bool displayedEnabled, int displayedVersion) =>
                    toggle.Invoke(shell, new object[] {
                        id, provider, displayedEnabled, displayedVersion
                    });
                bool Set(bool enabled) =>
                    (bool)setEnabled.Invoke(shell, new object[] {
                        provider, enabled, null
                    });

                var firstVersion = Version();
                nativeEnabled[0] = false;
                nativeVersions[0] = firstVersion;
                if (!Current(nativeEnabled[0], nativeVersions[0])) return false;

                // The stale Enable label must not disable an externally enabled provider.
                if (!provider.TrySetControlEnabled(true, out _) ||
                    Current(false, firstVersion))
                    return false;
                Click(false, firstVersion);
                if (!provider.ControlEnabled || provider.ToggleCalls != 1 ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // A state change through the other U1 surface invalidates the
                // earlier Disable, even if both callbacks retain the same provider.
                var disableVersion = Version();
                if (!Current(true, disableVersion) || !Set(false) ||
                    Current(true, disableVersion))
                    return false;
                Click(true, disableVersion);
                if (provider.ControlEnabled || provider.ToggleCalls != 2 ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // An Enable-Disable round trip restores the original boolean
                // but not the original edit revision; reject ABA stale clicks.
                var beforeRoundTrip = Version();
                if (!Set(true) || !Set(false) || Current(false, beforeRoundTrip))
                    return false;
                Click(false, beforeRoundTrip);
                if (provider.ControlEnabled || provider.ToggleCalls != 4)
                    return false;

                // A fresh Enable action still succeeds and persists.
                var freshVersion = Version();
                Click(false, freshVersion);
                return provider.ControlEnabled &&
                       provider.ToggleCalls == 5 &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 1 &&
                       configured.Contains(id);
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingStaleRecoveryActionsRejectPriorSessions()
        {
            var id = "vcr-parity-stale-recover-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var recover = type.GetMethod("RecoverWorkspaceTrackingSource", flags);
                var request = type.GetMethod("TryRequestWorkspaceTrackingRecovery", flags);
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var intent = type.GetMethod("IsWorkspaceTrackingRecoveryIntentCurrent", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (recover == null || recover.GetParameters().Length != 4 ||
                    request == null || toggle == null || intent == null ||
                    version == null || configuredField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeAt = source.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = source.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var panelAt = source.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = source.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                var toolkitAt = source.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(", StringComparison.Ordinal);
                var toolkitEnd = source.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                if (nativeAt < 0 || nativeEnd <= nativeAt ||
                    panelAt < 0 || panelEnd <= panelAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt)
                    return false;
                var nativeCode = source.Substring(nativeAt, nativeEnd - nativeAt);
                var panelCode = source.Substring(panelAt, panelEnd - panelAt);
                var toolkitCode = source.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!panelCode.Contains(
                        "sourceId, source, displayedEnabled, displayedVersion)") ||
                    !nativeCode.Contains(
                        "IsWorkspaceTrackingRecoveryIntentCurrent(") ||
                    !nativeCode.Contains(
                        "!IsWorkspaceTrackingEditVersionCurrent(id, displayedVersion)") ||
                    !toolkitCode.Contains(
                        "IsWorkspaceTrackingRecoveryIntentCurrent(") ||
                    nativeCode.IndexOf("IsWorkspaceTrackingRecoveryIntentCurrent(",
                        StringComparison.Ordinal) >
                    nativeCode.IndexOf("TryRequestWorkspaceTrackingRecovery(",
                        StringComparison.Ordinal) ||
                    toolkitCode.IndexOf("IsWorkspaceTrackingRecoveryIntentCurrent(",
                        StringComparison.Ordinal) >
                    toolkitCode.IndexOf("TryRequestWorkspaceTrackingRecovery(",
                        StringComparison.Ordinal))
                    return false;

                shellRoot = new GameObject("P1 stale recovery shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 stale recovery provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;
                configured.Add(id);

                int CurrentVersion() => (int)version.Invoke(shell, new object[] { id });
                bool CanRecover(bool displayedEnabled, int displayedVersion) =>
                    (bool)intent.Invoke(shell, new object[]
                    {
                        id.ToUpperInvariant(), provider, displayedEnabled, displayedVersion
                    });
                void Recover(bool displayedEnabled, int displayedVersion) =>
                    recover.Invoke(shell, new object[]
                    {
                        id, provider, displayedEnabled, displayedVersion
                    });
                bool Toggle(bool enabled) =>
                    (bool)toggle.Invoke(shell, new object[] { provider, enabled, null });

                if (!Toggle(true) || !provider.ControlEnabled) return false;
                var firstVersion = CurrentVersion();
                if (!CanRecover(true, firstVersion)) return false;

                Recover(true, firstVersion);
                if (provider.RecoveryCalls != 1 ||
                    CurrentVersion() == firstVersion ||
                    CanRecover(true, firstVersion))
                    return false;
                Recover(true, firstVersion);
                if (provider.RecoveryCalls != 1) return false;

                // A failed recovery leaves its current button usable for a retry.
                var failedVersion = CurrentVersion();
                provider.FailRecoveryOnce = true;
                Recover(true, failedVersion);
                if (provider.RecoveryCalls != 2 ||
                    CurrentVersion() != failedVersion ||
                    !CanRecover(true, failedVersion))
                    return false;
                Recover(true, failedVersion);
                if (provider.RecoveryCalls != 3 ||
                    CurrentVersion() == failedVersion)
                    return false;

                // A stale Recover must not affect an explicitly disabled source.
                var beforeDisable = CurrentVersion();
                if (!Toggle(false) || CanRecover(true, beforeDisable))
                    return false;
                Recover(true, beforeDisable);
                if (provider.RecoveryCalls != 3) return false;

                // A fresh enabled session can recover once.
                if (!Toggle(true)) return false;
                var freshVersion = CurrentVersion();
                Recover(true, freshVersion);
                return provider.RecoveryCalls == 4 &&
                       CurrentVersion() != freshVersion &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 1 &&
                       configured.Contains(id);
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingRecoveryButtonsRequireRunningSavedOnSource()
        {
            var id = "vcr-parity-recovery-visibility-" + Guid.NewGuid().ToString("N");
            var canonicalId = id.ToUpperInvariant();
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + canonicalId;
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var available = type.GetMethod(
                    "IsWorkspaceTrackingRecoveryActionAvailable", flags);
                var intent = type.GetMethod("IsWorkspaceTrackingRecoveryIntentCurrent", flags);
                var recover = type.GetMethod("TryRequestWorkspaceTrackingRecovery", flags);
                var getVersion = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                if (available == null || intent == null || recover == null ||
                    getVersion == null || configuredField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var listAt = code.IndexOf(
                    "private void RebuildWorkspaceNativeTrackingSources()", StringComparison.Ordinal);
                var listEnd = code.IndexOf(
                    "private static bool IsWorkspaceTrackingEditorSourceCurrent(", StringComparison.Ordinal);
                var panelAt = code.IndexOf(
                    "private void RefreshWorkspaceTrackingPanel()", StringComparison.Ordinal);
                var panelEnd = code.IndexOf(
                    "private void SetWorkspaceTrackingFeedback(", StringComparison.Ordinal);
                if (listAt < 0 || listEnd <= listAt ||
                    panelAt < 0 || panelEnd <= panelAt)
                    return false;
                var listCode = code.Substring(listAt, listEnd - listAt);
                var panelCode = code.Substring(panelAt, panelEnd - panelAt);
                // Recover must not be a clickable dead action for Off sources.
                // A saved-Off preference also overrides a transient live-On
                // state until the restoration scan reconciles the provider.
                if (!listCode.Contains(
                        "if (IsWorkspaceTrackingRecoveryActionAvailable(source.ControlId, source))") ||
                    !panelCode.Contains(
                        "if (IsWorkspaceTrackingRecoveryActionAvailable(sourceId, source))") ||
                    !panelCode.Contains(
                        "PlayerPrefs.GetInt(TrackingEnabledKey +") ||
                    !panelCode.Contains(
                        "GetWorkspaceTrackingPersistenceId(source.ControlId), 0)"))
                    return false;

                shellRoot = new GameObject("P1 recovery visibility shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 recovery visibility provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                if (configured == null) return false;
                PlayerPrefs.SetInt(enabledKey, 1);
                int Version() => (int)getVersion.Invoke(shell, new object[] { canonicalId });
                bool Offered() =>
                    (bool)available.Invoke(shell, new object[] { id, provider });
                bool Current() =>
                    (bool)intent.Invoke(shell, new object[] {
                        canonicalId, provider, true, Version()
                    });

                // No configured membership: never offer Recover, even if
                // external code enabled the provider and preferences say On.
                if (Offered() || !provider.TrySetControlEnabled(true, out _) ||
                    Offered())
                    return false;
                configured.Add(canonicalId);
                if (!Offered() || !Current()) return false;

                // Saved Off must override transient live On, including when
                // the live provider spells its ID differently from PlayerPrefs.
                PlayerPrefs.SetInt(enabledKey, 0);
                if (Offered() || Current()) return false;
                var args = new object[] { provider, null };
                if ((bool)recover.Invoke(shell, args) ||
                    provider.RecoveryCalls != 0 ||
                    !((string)args[1]).Contains("saved as disabled"))
                    return false;

                PlayerPrefs.SetInt(enabledKey, 1);
                if (!Offered() || !Current()) return false;
                if (!provider.TrySetControlEnabled(false, out _) ||
                    Offered() || Current())
                    return false;

                return provider.RecoveryCalls == 0 && configured.Contains(id);
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingPersistenceIdsPreserveRegisteredCase()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var registeredId = "VCR-PARITY-CASE-" + suffix;
            var reconnectedId = registeredId.ToLowerInvariant();
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + registeredId;
            var settingKey = "VCR.TrackingInputs.Setting.v1." +
                registeredId + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var persistenceId = type.GetMethod(
                    "GetWorkspaceTrackingPersistenceId", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                if (configuredField == null || restoredField == null ||
                    persistenceId == null || restore == null ||
                    toggle == null || scan == null)
                    return false;

                shellRoot = new GameObject("P1 tracking persisted ID casing shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 tracking changed-case provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = reconnectedId;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null)
                    return false;

                configured.Add(registeredId);
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "registered-case-setting");
                if ((string)persistenceId.Invoke(shell,
                        new object[] { reconnectedId }) != registeredId)
                    return false;

                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!provider.ControlEnabled ||
                    provider.ToggleCalls != 1 ||
                    provider.ConfigurationCalls != 1 ||
                    provider.AppliedValue != "registered-case-setting" ||
                    !restored.ContainsKey(registeredId))
                    return false;

                var args = new object[] { provider, false, null };
                if (!(bool)toggle.Invoke(shell, args) ||
                    provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;
                args[1] = true;
                args[2] = null;
                if (!(bool)toggle.Invoke(shell, args) ||
                    !provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // The canonical registered spelling persists after a same-ID
                // reattachment, even when the provider advertises lowercase.
                UnityEngine.Object.DestroyImmediate(providerRoot);
                providerRoot = new GameObject("P1 tracking changed-case replacement");
                var replacement = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                replacement.Id = reconnectedId;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!replacement.ControlEnabled ||
                    replacement.AppliedValue != "registered-case-setting" ||
                    replacement.ToggleCalls != 1)
                    return false;

                // Both Native Settings and Toolkit must use the registered
                // spelling for their setting writes and remove operations.
                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeAt = source.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeEnd = source.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var toolkitAt = source.IndexOf(
                    "private void UpdateWorkspaceTrackingMembership(", StringComparison.Ordinal);
                var toolkitEnd = source.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                if (nativeAt < 0 || nativeEnd <= nativeAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt)
                    return false;
                return source.Substring(nativeAt, nativeEnd - nativeAt)
                           .Contains("TrackingEnabledKey + persistenceId") &&
                       source.Substring(toolkitAt, toolkitEnd - toolkitAt)
                           .Contains("TrackingEnabledKey + persistenceId");
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingSavedOffRestoresBeforeConfiguration()
        {
            var id = "vcr-parity-off-restore-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                shellRoot = new GameObject("P1 saved-off tracking restoration shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 saved-off tracking provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configured = type.GetField("_workspaceConfiguredTracking", flags)
                    ?.GetValue(shell) as HashSet<string>;
                var restored = type.GetField("_workspaceRestoredTracking", flags)
                    ?.GetValue(shell) as Dictionary<string, ITrackingRuntimeControl>;
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                if (configured == null || restored == null || scan == null || restore == null)
                    return false;

                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);
                PlayerPrefs.SetString(settingKey, "saved-off-value");
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;

                // A transient failure to stop capture must prevent settings
                // applying on the still-active camera. Retry on the next scan.
                provider.FailDisableOnce = true;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!provider.ControlEnabled || provider.ToggleCalls != 2 ||
                    provider.ConfigurationCalls != 0 || restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (provider.ControlEnabled || provider.ToggleCalls != 3 ||
                    provider.ConfigurationCalls != 1 ||
                    provider.AppliedWhileEnabled ||
                    provider.AppliedValue != "saved-off-value" ||
                    !restored.TryGetValue(id, out var marked) ||
                    !ReferenceEquals(marked, provider))
                    return false;

                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                return provider.ToggleCalls == 3 &&
                       provider.ConfigurationCalls == 1 &&
                       PlayerPrefs.GetString(settingKey) == "saved-off-value";
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingToggleRequiresExplicitMembership()
        {
            var id = "vcr-parity-toggle-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var toggle = type.GetMethod("TrySetWorkspaceTrackingEnabled", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                if (toggle == null || configuredField == null || restoredField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var listAt = source.IndexOf(
                    "private void RebuildWorkspaceNativeTrackingSources()",
                    StringComparison.Ordinal);
                var listEnd = source.IndexOf(
                    "private static bool IsWorkspaceTrackingEditorSourceCurrent(",
                    StringComparison.Ordinal);
                var nativeAt = source.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(",
                    StringComparison.Ordinal);
                var nativeEnd = source.IndexOf(
                    "private ScrollView _workspaceTrackingList;",
                    StringComparison.Ordinal);
                var toolkitAt = source.IndexOf(
                    "private void ToggleWorkspaceTrackingEnabled(",
                    StringComparison.Ordinal);
                var toolkitEnd = source.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(",
                    StringComparison.Ordinal);
                if (listAt < 0 || listEnd <= listAt || nativeAt < 0 ||
                    nativeEnd <= nativeAt || toolkitAt < 0 ||
                    toolkitEnd <= toolkitAt)
                    return false;
                var list = source.Substring(listAt, listEnd - listAt);
                var native = source.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = source.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!list.Contains("if (configured)") ||
                    !list.Contains("5200 + i") ||
                    !list.Contains("5400 + i") ||
                    !native.Contains("TrySetWorkspaceTrackingEnabled(") ||
                    !toolkit.Contains("TrySetWorkspaceTrackingEnabled("))
                    return false;

                shellRoot = new GameObject("P1 tracking toggle membership shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 tracking toggle membership provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null)
                    return false;

                var args = new object[] { provider, true, null };
                if ((bool)toggle.Invoke(shell, args) ||
                    provider.ToggleCalls != 0 ||
                    provider.ControlEnabled ||
                    configured.Contains(id) ||
                    restored.ContainsKey(id) ||
                    PlayerPrefs.HasKey(enabledKey))
                    return false;

                configured.Add(id);
                provider.FailEnableOnce = true;
                args[2] = null;
                if ((bool)toggle.Invoke(shell, args) ||
                    provider.ToggleCalls != 1 ||
                    provider.ControlEnabled ||
                    restored.ContainsKey(id) ||
                    PlayerPrefs.HasKey(enabledKey))
                    return false;

                args[2] = null;
                if (!(bool)toggle.Invoke(shell, args) ||
                    provider.ToggleCalls != 2 ||
                    !provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1 ||
                    !restored.TryGetValue(id, out var recorded) ||
                    !ReferenceEquals(recorded, provider))
                    return false;

                args[1] = false;
                args[2] = null;
                if (!(bool)toggle.Invoke(shell, args) ||
                    provider.ToggleCalls != 3 ||
                    provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                configured.Remove(id);
                args[1] = true;
                args[2] = null;
                return !(bool)toggle.Invoke(shell, args) &&
                       provider.ToggleCalls == 3 &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 0 &&
                       !configured.Contains(id);
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingFailedRecoveryRestoresChangedDeviceState()
        {
            var id = "vcr-parity-recovery-partial-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var recover = type.GetMethod("TryRequestWorkspaceTrackingRecovery", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var revision = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (recover == null || restore == null || revision == null ||
                    canSave == null || configuredField == null ||
                    restoredField == null || scanField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var coreAt = code.IndexOf(
                    "private bool TryRequestWorkspaceTrackingRecovery(", StringComparison.Ordinal);
                var coreEnd = code.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var nativeAt = coreEnd;
                var nativeEnd = code.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(", StringComparison.Ordinal);
                var toolkitEnd = code.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                if (coreAt < 0 || coreEnd <= coreAt ||
                    nativeEnd <= nativeAt ||
                    toolkitAt < 0 || toolkitEnd <= toolkitAt)
                    return false;
                var core = code.Substring(coreAt, coreEnd - coreAt);
                var native = code.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = code.Substring(toolkitAt, toolkitEnd - toolkitAt);
                if (!core.Contains("configurable.GetConfigurationSettings()") ||
                    !core.Contains("AreWorkspaceTrackingSettingsUnchanged(") ||
                    !core.Contains("control.ControlEnabled != beforeEnabled") ||
                    !core.Contains("control.ControlHealthState != beforeHealth") ||
                    !core.Contains("if (stateChanged)") ||
                    !core.Contains("_workspaceRestoredTracking.Remove(control.ControlId)") ||
                    !core.Contains("InvalidateWorkspaceTrackingEditors(control.ControlId)") ||
                    !core.Contains("_nextTrackingRestoreScan = 0f") ||
                    !native.Contains("if (recovered ||") ||
                    !native.Contains(
                        "!IsWorkspaceTrackingEditVersionCurrent(id, displayedVersion)") ||
                    !toolkit.Contains("if (recovered ||") ||
                    !toolkit.Contains(
                        "!IsWorkspaceTrackingEditVersionCurrent(sourceId, displayedVersion)"))
                    return false;

                shellRoot = new GameObject("P1 partially failed recovery shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 partially failed recovery provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;
                configured.Add(id);
                if (!provider.TrySetControlEnabled(true, out _)) return false;
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "saved-recovery-value");
                restored[id] = provider;

                int Version() => (int)revision.Invoke(shell, new object[] { id });
                bool DraftValid(int opened) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), opened });
                bool Recover()
                {
                    var args = new object[] { provider, null };
                    return (bool)recover.Invoke(shell, args);
                }
                void Restore()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                // Clean provider refusal: no changed live state, so retain
                // the editor revision, cache and saved preference for retry.
                var baseline = Version();
                provider.FailRecoveryOnce = true;
                if (Recover() || !provider.ControlEnabled ||
                    Version() != baseline || !DraftValid(baseline) ||
                    !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1)
                    return false;

                // The restart may have stopped capture before reporting a
                // failure. A stale editor is invalid and saved On must replay.
                provider.FailRecoveryAfterMutationOnce = true;
                if (Recover() || provider.ControlEnabled ||
                    Version() == baseline || DraftValid(baseline) ||
                    restored.ContainsKey(id) ||
                    (float)scanField.GetValue(shell) != 0f ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 1 ||
                    PlayerPrefs.GetString(settingKey) != "saved-recovery-value")
                    return false;
                var afterFailedRestart = Version();
                Restore();
                if (!provider.ControlEnabled ||
                    provider.AppliedValue != "saved-recovery-value" ||
                    provider.ConfigurationCalls != 1 ||
                    !restored.ContainsKey(id) ||
                    Version() == afterFailedRestart)
                    return false;

                // A recovery failure may preserve enabled/health while
                // mutating settings. Detect readback-only changes too.
                provider.ExternalReadbackOverride = "before-second-recovery";
                restored[id] = provider;
                var beforeSettingsOnlyFailure = Version();
                provider.FailRecoveryAfterSettingsMutationOnce = true;
                if (Recover() || !provider.ControlEnabled ||
                    provider.ExternalReadbackOverride != "changed-after-failed-recovery" ||
                    Version() == beforeSettingsOnlyFailure ||
                    DraftValid(beforeSettingsOnlyFailure) ||
                    restored.ContainsKey(id) ||
                    PlayerPrefs.GetString(settingKey) != "saved-recovery-value")
                    return false;

                Restore();
                if (!provider.ControlEnabled || !restored.ContainsKey(id) ||
                    provider.ConfigurationCalls != 2 ||
                    provider.AppliedValue != "saved-recovery-value")
                    return false;
                var stable = Version();
                Restore();
                return Version() == stable && provider.ConfigurationCalls == 2 &&
                       provider.RecoveryCalls == 3 &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 1;
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingRecoveryDoesNotEnableDisabledSource()
        {
            var id = "vcr-parity-recovery-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var request = type.GetMethod("TryRequestWorkspaceTrackingRecovery", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var nextScanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (request == null || restore == null || configuredField == null ||
                    restoredField == null || nextScanField == null)
                    return false;

                var trackingSource = File.ReadAllText(Path.Combine(
                    Application.dataPath, "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeAt = trackingSource.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var fallbackAt = trackingSource.IndexOf(
                    "private void RecoverWorkspaceTrackingSource(", StringComparison.Ordinal);
                if (nativeAt < 0 || fallbackAt < nativeAt ||
                    !trackingSource.Substring(nativeAt, fallbackAt - nativeAt)
                        .Contains("TryRequestWorkspaceTrackingRecovery(control, out var feedback)") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        trackingSource.Substring(fallbackAt),
                        @"TryRequestWorkspaceTrackingRecovery\(\s*displayedSource\s*,\s*out\s+var\s+feedback\s*\)"))
                    return false;

                shellRoot = new GameObject("P1 tracking recovery shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 tracking recovery provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;

                var arguments = new object[] { provider, null };
                // Recovery must not add an unconfigured source implicitly.
                if ((bool)request.Invoke(shell, arguments) ||
                    !((string)arguments[1]).Contains("Add") ||
                    provider.RecoveryCalls != 0)
                    return false;

                configured.Add(id);
                arguments[1] = null;
                // MediaPipe/Ultraleap's recovery may enable an off source;
                // require an explicit Enable action before recovery.
                if ((bool)request.Invoke(shell, arguments) ||
                    !((string)arguments[1]).Contains("Enable") ||
                    provider.RecoveryCalls != 0)
                    return false;

                if (!provider.TrySetControlEnabled(true, out _)) return false;
                // A provider can be On before startup's saved-Off preference
                // is restored. Recovery must not restart such a source.
                PlayerPrefs.SetInt(enabledKey, 0);
                arguments[1] = null;
                if ((bool)request.Invoke(shell, arguments) ||
                    provider.RecoveryCalls != 0 ||
                    !provider.ControlEnabled ||
                    !((string)arguments[1]).Contains("saved as disabled") ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                // The configured set stores the original spelling. The live
                // provider ID can differ in case without bypassing the guard.
                provider.Id = id.ToUpperInvariant();
                arguments[1] = null;
                if ((bool)request.Invoke(shell, arguments) ||
                    provider.RecoveryCalls != 0 ||
                    !provider.ControlEnabled ||
                    PlayerPrefs.GetInt(enabledKey, -1) != 0)
                    return false;

                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "saved-after-restart");
                restored[id] = provider;
                provider.ResetOnRecovery = true;
                arguments[1] = null;
                if (!(bool)request.Invoke(shell, arguments) ||
                    provider.RecoveryCalls != 1 ||
                    provider.ControlEnabled || restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;

                // An accepted restart may clear device state in-place.
                // The same provider instance must be restored on the next scan.
                nextScanField.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!provider.ControlEnabled ||
                    provider.AppliedValue != "saved-after-restart" ||
                    provider.ConfigurationCalls != 1 ||
                    !restored.ContainsKey(id))
                    return false;

                provider.FailRecoveryOnce = true;
                arguments[1] = null;
                if ((bool)request.Invoke(shell, arguments) ||
                    provider.RecoveryCalls != 2 ||
                    !restored.ContainsKey(id) ||
                    !provider.ControlEnabled ||
                    PlayerPrefs.GetString(settingKey) != "saved-after-restart")
                    return false;
                return true;
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingFailedApplyMutationsInvalidateOldDrafts()
        {
            var id = "vcr-parity-partial-apply-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var apply = type.GetMethod("TryApplyWorkspaceTrackingSettings", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var reconcile = type.GetMethod(
                    "ReconcileWorkspaceTrackingFailedApplyMutation", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                if (apply == null || version == null || reconcile == null ||
                    canSave == null || restore == null || scan == null ||
                    configuredField == null || restoredField == null ||
                    apply.GetParameters().Length != 6)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var sharedAt = code.IndexOf(
                    "private bool TryApplyWorkspaceTrackingSettings(", StringComparison.Ordinal);
                var nativeAt = code.IndexOf(
                    "private void SaveWorkspaceNativeTrackingDetails()", StringComparison.Ordinal);
                var nativeEnd = code.IndexOf(
                    "private bool TrySetWorkspaceTrackingEnabled(", StringComparison.Ordinal);
                var toolkitAt = code.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                var restoreAt = code.IndexOf(
                    "private void RestoreWorkspaceTrackingPreferences()", StringComparison.Ordinal);
                var restoreEnd = code.IndexOf(
                    "private string GetWorkspaceTrackingSummary()", StringComparison.Ordinal);
                if (sharedAt < 0 || nativeAt <= sharedAt ||
                    nativeEnd <= nativeAt || toolkitAt < 0 ||
                    restoreAt < 0 || restoreEnd <= restoreAt)
                    return false;
                var shared = code.Substring(sharedAt, nativeAt - sharedAt);
                var native = code.Substring(nativeAt, nativeEnd - nativeAt);
                var toolkit = code.Substring(toolkitAt);
                var background = code.Substring(restoreAt, restoreEnd - restoreAt);
                if (!shared.Contains("configurable.TryApplyConfiguration(values, out error)") ||
                    !shared.Contains("AreWorkspaceTrackingSettingsUnchanged(configurable, beforeValues)") ||
                    !shared.Contains("control.ControlHealthState != previousHealth") ||
                    !native.Contains("TryApplyWorkspaceTrackingSettings(control, configurable, values,") ||
                    !native.Contains("if (changedOnFailure)") ||
                    !native.Contains("ReconcileWorkspaceTrackingFailedApplyMutation(control.ControlId)") ||
                    !toolkit.Contains("TryApplyWorkspaceTrackingSettings(") ||
                    !toolkit.Contains("if (changedOnFailure)") ||
                    !toolkit.Contains("ReconcileWorkspaceTrackingFailedApplyMutation(sourceId)") ||
                    !background.Contains("acceptedMutation |= changedOnFailure") ||
                    !shared.Contains("_workspaceRestoredTracking.Remove(sourceId)") ||
                    !shared.Contains("_nextTrackingRestoreScan = 0f"))
                    return false;

                shellRoot = new GameObject("P1 failed apply revision shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                sourceRoot = new GameObject("P1 failed apply provider");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;
                configured.Add(id);
                restored[id] = provider;
                PlayerPrefs.SetInt(enabledKey, 0);
                PlayerPrefs.SetString(settingKey, "default-value");
                scan.SetValue(shell, 123f);
                var before = new Dictionary<string, string> {
                    { "test-value", "default-value" }
                };
                var values = new Dictionary<string, string> {
                    { "test-value", "new-value" }
                };
                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int current) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), current });
                bool Apply(out bool changedOnFailure)
                {
                    var args = new object[] { provider, provider, values, before, null, false };
                    var success = (bool)apply.Invoke(shell, args);
                    changedOnFailure = (bool)args[5];
                    return success;
                }

                var initial = Version();
                provider.FailConfigurationOnce = true;
                if (Apply(out var unchangedOnFailure) || unchangedOnFailure ||
                    Version() != initial || !DraftValid(initial) ||
                    !restored.ContainsKey(id) ||
                    (float)scan.GetValue(shell) != 123f ||
                    provider.ConfigurationCalls != 1)
                    return false;

                provider.FailConfigurationAfterMutationOnce = true;
                if (Apply(out var mutatedOnFailure) || !mutatedOnFailure ||
                    provider.ExternalReadbackOverride != "new-value" ||
                    provider.ConfigurationCalls != 2)
                    return false;

                // Both Native and Toolkit apply paths now schedule a saved
                // preference replay after a failed-but-mutating Apply.
                reconcile.Invoke(shell, new object[] { id });
                if (Version() == initial || DraftValid(initial) ||
                    restored.ContainsKey(id) ||
                    (float)scan.GetValue(shell) != 0f ||
                    PlayerPrefs.GetString(settingKey) != "default-value")
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (provider.ConfigurationCalls != 3 ||
                    provider.AppliedValue != "default-value" ||
                    !restored.ContainsKey(id) ||
                    provider.ControlEnabled ||
                    PlayerPrefs.GetString(settingKey) != "default-value")
                    return false;

                // Startup restore also detects a provider that wrote live
                // settings and then failed restarting. Do not cache the
                // partially restored component as fully initialized.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 failed startup restore");
                provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.FailConfigurationAfterMutationOnce = true;
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "saved-value");
                var beforeRestore = Version();
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (provider.ConfigurationCalls != 1 ||
                    provider.ExternalReadbackOverride != "saved-value" ||
                    restored.ContainsKey(id) || Version() == beforeRestore ||
                    DraftValid(beforeRestore) ||
                    PlayerPrefs.GetString(settingKey) != "saved-value")
                    return false;

                var afterFailedRestore = Version();
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (provider.ConfigurationCalls != 2 ||
                    provider.AppliedValue != "saved-value" ||
                    !provider.ControlEnabled ||
                    !restored.ContainsKey(id) ||
                    Version() == afterFailedRestore)
                    return false;

                var afterSuccess = Version();
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                return provider.ConfigurationCalls == 2 &&
                       Version() == afterSuccess;
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingBackgroundRestoreInvalidatesStaleEditors()
        {
            var id = "vcr-parity-background-restore-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                if (restore == null || version == null || canSave == null ||
                    scan == null || configuredField == null || restoredField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var start = code.IndexOf(
                    "private void RestoreWorkspaceTrackingPreferences()", StringComparison.Ordinal);
                var end = code.IndexOf(
                    "private string GetWorkspaceTrackingSummary()", StringComparison.Ordinal);
                if (start < 0 || end <= start) return false;
                var restoreCode = code.Substring(start, end - start);
                // Contract is about which accepted operations invalidate
                // drafts, not the literal count of assignments. A rejected
                // provider call may also change state or make readback fail.
                if (!restoreCode.Contains("var acceptedMutation = false;") ||
                    !restoreCode.Contains("if (acceptedMutation)") ||
                    !restoreCode.Contains("InvalidateWorkspaceTrackingEditors(id)") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(restoreCode,
                        @"TrySetControlEnabled\(false, out var stopError\)[\s\S]*?acceptedMutation = true;") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(restoreCode,
                        @"TryApplyWorkspaceTrackingSettings\([\s\S]*?acceptedMutation = true;") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(restoreCode,
                        @"TrySetControlEnabled\(enabled, out var enableError\)[\s\S]*?acceptedMutation = true;") ||
                    !restoreCode.Contains("acceptedMutation |= changedOnFailure") ||
                    !restoreCode.Contains("source.ControlEnabled != beforeRestoreEnabled") ||
                    !restoreCode.Contains("source.ControlHealthState != beforeRestoreHealth") ||
                    !restoreCode.Contains("if (acceptedMutation)\n                        InvalidateWorkspaceTrackingEditors(id)"))
                    return false;

                shellRoot = new GameObject("P1 background restore revision shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null) return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "persisted-restore");

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int opened) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), opened });
                void Scan()
                {
                    scan.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                sourceRoot = new GameObject("P1 restore config rejection");
                var provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.FailConfigurationOnce = true;
                var originalVersion = Version();
                if (!DraftValid(originalVersion)) return false;
                Scan();
                // A failed provider Apply before any accepted mutation is not
                // allowed to discard a user's open settings draft.
                if (provider.ConfigurationCalls != 1 ||
                    provider.ToggleCalls != 0 ||
                    restored.ContainsKey(id) ||
                    Version() != originalVersion ||
                    !DraftValid(originalVersion))
                    return false;

                Scan();
                if (!provider.ControlEnabled ||
                    provider.ConfigurationCalls != 2 ||
                    provider.ToggleCalls != 1 ||
                    Version() == originalVersion ||
                    DraftValid(originalVersion) ||
                    !restored.ContainsKey(id))
                    return false;
                var stableVersion = Version();
                Scan();
                if (Version() != stableVersion ||
                    provider.ConfigurationCalls != 2 ||
                    provider.ToggleCalls != 1)
                    return false;

                // Configuration was accepted but enabling failed. Even the
                // partial restore must invalidate earlier drafts, then retry.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 partial background restore");
                provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.FailEnableOnce = true;
                var beforePartial = Version();
                Scan();
                if (provider.ControlEnabled ||
                    provider.ConfigurationCalls != 1 ||
                    provider.ToggleCalls != 1 ||
                    restored.ContainsKey(id) ||
                    Version() == beforePartial ||
                    DraftValid(beforePartial))
                    return false;

                var beforeRetry = Version();
                Scan();
                if (!provider.ControlEnabled ||
                    provider.ConfigurationCalls != 2 ||
                    provider.ToggleCalls != 2 ||
                    Version() == beforeRetry ||
                    DraftValid(beforeRetry))
                    return false;

                // Saved-Off restoration must stop an externally running
                // capture before requesting settings, even if the schema
                // becomes temporarily unavailable after the successful stop.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 saved-off partial restore");
                provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;
                provider.ReturnNullSchemaOnce = true;
                PlayerPrefs.SetInt(enabledKey, 0);
                var beforeStop = Version();
                Scan();
                if (provider.ControlEnabled ||
                    provider.ConfigurationCalls != 0 ||
                    provider.ToggleCalls != 2 ||
                    restored.ContainsKey(id) ||
                    Version() == beforeStop ||
                    DraftValid(beforeStop))
                    return false;
                var beforeSettingsRetry = Version();
                Scan();
                if (provider.ControlEnabled ||
                    provider.ConfigurationCalls != 1 ||
                    provider.ToggleCalls != 2 ||
                    !restored.ContainsKey(id) ||
                    Version() == beforeSettingsRetry)
                    return false;

                // With no persisted field to apply, a rejected Enable
                // changes neither provider state nor editor revision.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 no accepted restore mutation");
                provider = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.FailEnableOnce = true;
                PlayerPrefs.DeleteKey(settingKey);
                PlayerPrefs.SetInt(enabledKey, 1);
                var beforeFailedEnable = Version();
                Scan();
                if (provider.ConfigurationCalls != 0 ||
                    provider.ToggleCalls != 1 ||
                    provider.ControlEnabled ||
                    restored.ContainsKey(id) ||
                    Version() != beforeFailedEnable ||
                    !DraftValid(beforeFailedEnable))
                    return false;
                Scan();
                return provider.ControlEnabled &&
                       provider.ToggleCalls == 2 &&
                       provider.ConfigurationCalls == 0 &&
                       Version() != beforeFailedEnable &&
                       !DraftValid(beforeFailedEnable) &&
                       restored.ContainsKey(id);
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingCachedProviderExternalStateDriftRestoresSavedPreference()
        {
            var id = "vcr-parity-cached-drift-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (restore == null || version == null || canSave == null ||
                    configuredField == null || restoredField == null ||
                    scanField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var restoreAt = source.IndexOf(
                    "private void RestoreWorkspaceTrackingPreferences()", StringComparison.Ordinal);
                var restoreEnd = source.IndexOf(
                    "private string GetWorkspaceTrackingSummary()", StringComparison.Ordinal);
                if (restoreAt < 0 || restoreEnd <= restoreAt)
                    return false;
                var restoreCode = source.Substring(restoreAt, restoreEnd - restoreAt);
                var cachedGuard = restoreCode.IndexOf(
                    "_workspaceRestoredTracking.TryGetValue(id, out var previous)",
                    StringComparison.Ordinal);
                var stateGuard = restoreCode.IndexOf(
                    "if (stateMatches)", StringComparison.Ordinal);
                var cacheDrop = restoreCode.IndexOf(
                    "_workspaceRestoredTracking.Remove(id)", stateGuard,
                    StringComparison.Ordinal);
                var invalidate = restoreCode.IndexOf(
                    "InvalidateWorkspaceTrackingEditors(id)", stateGuard,
                    StringComparison.Ordinal);
                if (cachedGuard < 0 || stateGuard <= cachedGuard ||
                    cacheDrop <= stateGuard || invalidate <= cacheDrop ||
                    !restoreCode.Contains("source.ControlEnabled ==") ||
                    !restoreCode.Contains("GetWorkspaceTrackingPersistenceId(id)"))
                    return false;

                shellRoot = new GameObject("P1 cached tracking drift shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 cached tracking drift provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                if (configured == null || restored == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 1);

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int old) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), old });
                void Scan()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                Scan();
                if (!provider.ControlEnabled || provider.ToggleCalls != 1 ||
                    !restored.ContainsKey(id))
                    return false;
                var settled = Version();
                Scan();
                if (Version() != settled || provider.ToggleCalls != 1)
                    return false;

                // External source code stopped the already-cached tracker.
                // Reconcile the same instance and reject its former UI draft.
                if (!provider.TrySetControlEnabled(false, out _))
                    return false;
                var oldDraft = Version();
                Scan();
                if (!provider.ControlEnabled || provider.ToggleCalls != 3 ||
                    !restored.ContainsKey(id) || Version() == oldDraft ||
                    DraftValid(oldDraft) || PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;
                var stable = Version();
                Scan();
                if (Version() != stable || provider.ToggleCalls != 3)
                    return false;

                // Failed recovery of external drift must leave the cache
                // uncached so a later scan retries the saved preference.
                if (!provider.TrySetControlEnabled(false, out _))
                    return false;
                provider.FailEnableOnce = true;
                oldDraft = Version();
                Scan();
                if (provider.ControlEnabled || restored.ContainsKey(id) ||
                    Version() == oldDraft || DraftValid(oldDraft) ||
                    PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;
                Scan();
                if (!provider.ControlEnabled || !restored.ContainsKey(id) ||
                    PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;

                // Changing the saved preference while an instance remains
                // cached must also settle it instead of skipping the device.
                PlayerPrefs.SetInt(enabledKey, 0);
                oldDraft = Version();
                Scan();
                if (provider.ControlEnabled || restored.ContainsKey(id) == false ||
                    Version() == oldDraft || DraftValid(oldDraft))
                    return false;
                var stoppedVersion = Version();
                Scan();
                if (Version() != stoppedVersion || provider.ControlEnabled)
                    return false;

                // An externally reenabled source must be turned back Off.
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;
                oldDraft = Version();
                Scan();
                return !provider.ControlEnabled &&
                       restored.ContainsKey(id) &&
                       Version() != oldDraft &&
                       !DraftValid(oldDraft) &&
                       PlayerPrefs.GetInt(enabledKey, -1) == 0;
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
            }
        }

        private static bool TrackingSavedConfigurationChangesCancelRestoreBackoff()
        {
            var id = "vcr-parity-retry-config-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            var additionalKey = "VCR.TrackingInputs.Setting.v1." + id + ".external-field";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var retryField = type.GetField("_workspaceTrackingRestoreRetries", flags);
                if (restore == null || scan == null || configuredField == null ||
                    restoredField == null || retryField == null)
                    return false;
                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                if (!code.Contains("HaveWorkspaceTrackingSavedFieldsChanged(sourceId, retry.SavedFields)") ||
                    !code.Contains("observedRetrySettings = settings") ||
                    !code.Contains("settings = CaptureWorkspaceTrackingSettings(observedSettings)"))
                    return false;

                shellRoot = new GameObject("P1 changed saved configuration backoff shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 changed saved configuration backoff provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.ReportAdditionalSetting = true;
                provider.ConfigurationFailuresRemaining = 3;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                var retries = retryField.GetValue(shell) as System.Collections.IDictionary;
                if (configured == null || restored == null || retries == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);
                PlayerPrefs.SetString(settingKey, "original");

                void Scan()
                {
                    scan.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }
                bool BackedOff()
                {
                    if (!retries.Contains(id) || restored.ContainsKey(id))
                        return false;
                    var retry = retries[id];
                    var field = retry.GetType().GetField("RetryAfter",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    return field != null &&
                        (float)field.GetValue(retry) > Time.unscaledTime + 2f;
                }

                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 3 || !BackedOff())
                    return false;
                var schemaCalls = provider.SchemaCalls;
                Scan();
                if (provider.ConfigurationCalls != 3 || provider.SchemaCalls != schemaCalls)
                    return false;

                // Changed saved field replays immediately (at the next global
                // scan), without waiting for its old per-source deadline.
                PlayerPrefs.SetString(settingKey, "updated");
                Scan();
                if (provider.ConfigurationCalls != 4 ||
                    provider.AppliedValue != "updated" || provider.ControlEnabled ||
                    !restored.ContainsKey(id) || retries.Contains(id))
                    return false;

                // An added key for a field present in the previously observed
                // provider schema also invalidates the old retry.
                provider.ExternalReadbackOverride = "external-drift-add";
                provider.ConfigurationFailuresRemaining = 3;
                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 7 || !BackedOff())
                    return false;
                schemaCalls = provider.SchemaCalls;
                Scan();
                if (provider.ConfigurationCalls != 7 || provider.SchemaCalls != schemaCalls)
                    return false;
                PlayerPrefs.SetString(additionalKey, "new-extra-value");
                Scan();
                if (provider.ConfigurationCalls != 8 ||
                    provider.AppliedValue != "updated" ||
                    !restored.ContainsKey(id) || retries.Contains(id) ||
                    PlayerPrefs.GetString(additionalKey) != "new-extra-value")
                    return false;

                // Deleting the same persisted key also cancels backoff.
                provider.ExternalReadbackOverride = "external-drift-delete";
                provider.ConfigurationFailuresRemaining = 3;
                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 11 || !BackedOff())
                    return false;
                PlayerPrefs.DeleteKey(additionalKey);
                Scan();
                if (provider.ConfigurationCalls != 12 ||
                    provider.AppliedValue != "updated" || provider.ControlEnabled ||
                    !restored.ContainsKey(id) || retries.Contains(id))
                    return false;
                schemaCalls = provider.SchemaCalls;
                Scan();
                // A restored provider is no longer backed off: its ordinary
                // two-second cached scan must read its schema once to catch
                // external configuration drift. Zero SDK calls is required
                // only while a failed provider is still in retry backoff.
                return provider.ConfigurationCalls == 12 &&
                       provider.SchemaCalls == schemaCalls + 1 &&
                       PlayerPrefs.GetString(settingKey) == "updated";
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
                PlayerPrefs.DeleteKey(additionalKey);
            }
        }

        private static bool TrackingSavedEnabledPreferenceChangeCancelsRestoreBackoff()
        {
            var id = "vcr-parity-retry-intent-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var retryField = type.GetField("_workspaceTrackingRestoreRetries", flags);
                if (restore == null || scan == null || configuredField == null ||
                    restoredField == null || retryField == null)
                    return false;

                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                if (!source.Contains("retry.SavedEnabled != savedEnabled") ||
                    !source.Contains("retry.SavedEnabled = PlayerPrefs.GetInt("))
                    return false;

                shellRoot = new GameObject("P1 tracking changed saved intent shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 tracking changed saved intent provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.ConfigurationFailuresRemaining = 3;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                var retries = retryField.GetValue(shell) as System.Collections.IDictionary;
                if (configured == null || restored == null || retries == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);
                PlayerPrefs.SetString(settingKey, "saved");
                void Scan()
                {
                    scan.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 3 || !retries.Contains(id) ||
                    restored.ContainsKey(id) || provider.ControlEnabled)
                    return false;
                var oldDelay = retries[id].GetType().GetField("RetryAfter",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (oldDelay == null ||
                    (float)oldDelay.GetValue(retries[id]) <= Time.unscaledTime + 2f)
                    return false;
                // A further scan with identical persisted intent cannot retry.
                Scan();
                if (provider.ConfigurationCalls != 3)
                    return false;

                // An external settings surface changes the durable preference.
                // A new On intent bypasses the *old* failure delay immediately.
                PlayerPrefs.SetInt(enabledKey, 1);
                Scan();
                if (provider.ConfigurationCalls != 4 ||
                    provider.AppliedValue != "saved" ||
                    !provider.ControlEnabled ||
                    !restored.ContainsKey(id) || retries.Contains(id))
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 4)
                    return false;

                // A newly failing On source must also release its delay when
                // an external settings surface changes the saved intent to Off.
                provider.ExternalReadbackOverride = "drift";
                provider.ConfigurationFailuresRemaining = 3;
                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 7 || !retries.Contains(id) ||
                    !provider.ControlEnabled)
                    return false;
                PlayerPrefs.SetInt(enabledKey, 0);
                Scan();
                return !provider.ControlEnabled &&
                       provider.ConfigurationCalls == 8 &&
                       provider.AppliedValue == "saved" &&
                       !retries.Contains(id) && restored.ContainsKey(id);
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingSavedOffSafetyBypassesConfigurationBackoff()
        {
            var id = "vcr-parity-saved-off-backoff-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var retriesField = type.GetField("_workspaceTrackingRestoreRetries", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (restore == null || configuredField == null || restoredField == null ||
                    retriesField == null || scanField == null)
                    return false;
                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                if (!code.Contains("TryEnforceWorkspaceTrackingSavedOff(source, id)") ||
                    !code.Contains("if (!savedOffStopFailedThisAttempt &&"))
                    return false;

                shellRoot = new GameObject("P1 saved-Off backoff shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 saved-Off backoff provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.FailConfigurationAfterEnablingOnce = true;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                var retries = retriesField.GetValue(shell) as System.Collections.IDictionary;
                if (configured == null || restored == null || retries == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 0);
                PlayerPrefs.SetString(settingKey, "saved-off-value");

                void Scan()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                // The provider wrongly starts capture before reporting a failed
                // Apply. Restore's finally must stop it in the SAME attempt.
                Scan();
                if (provider.ConfigurationCalls != 1 || provider.ControlEnabled ||
                    provider.ToggleCalls != 1 || restored.ContainsKey(id) ||
                    !retries.Contains(id) || PlayerPrefs.GetInt(enabledKey, 1) != 0)
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 2 ||
                    provider.AppliedValue != "saved-off-value" ||
                    provider.ControlEnabled || !restored.ContainsKey(id) ||
                    retries.Contains(id))
                    return false;

                // Build up a real configuration backoff while saved Off.
                provider.ExternalReadbackOverride = "out-of-band-config";
                provider.ConfigurationFailuresRemaining = 3;
                Scan();
                Scan();
                Scan();
                if (provider.ConfigurationCalls != 5 || !retries.Contains(id) ||
                    restored.ContainsKey(id) || provider.ControlEnabled)
                    return false;
                var retry = retries[id];
                var retryAfter = retry.GetType().GetField("RetryAfter",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (retryAfter == null ||
                    (float)retryAfter.GetValue(retry) <= Time.unscaledTime + 2f)
                    return false;

                // An external SDK now turns a saved-Off camera On during the
                // four-second configuration delay. The next two-second global
                // scan must issue Stop without retrying configuration.
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;
                var beforeSafetyToggle = provider.ToggleCalls;
                Scan();
                if (provider.ControlEnabled ||
                    provider.ToggleCalls != beforeSafetyToggle + 1 ||
                    provider.ConfigurationCalls != 5 ||
                    !retries.Contains(id))
                    return false;

                // A failed stop must be attempted again on the following scan
                // even though configuration remains backed off.
                if (!provider.TrySetControlEnabled(true, out _))
                    return false;
                provider.FailDisableOnce = true;
                Scan();
                if (!provider.ControlEnabled || provider.ConfigurationCalls != 5)
                    return false;
                Scan();
                if (provider.ControlEnabled || provider.ConfigurationCalls != 5)
                    return false;

                // Once the delayed provider recovers, restore saved config and
                // discard both the backoff and old cached source state.
                retryAfter.SetValue(retries[id], 0f);
                Scan();
                if (provider.ControlEnabled || provider.ConfigurationCalls != 6 ||
                    !restored.ContainsKey(id) || retries.Contains(id) ||
                    provider.AppliedValue != "saved-off-value")
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 6 ||
                    provider.ControlEnabled || retries.Contains(id))
                    return false;

                // A *successful* Apply may restart capture, then its
                // post-Apply Stop can fail. Do not send a second Stop inside
                // finally; leave the source uncached for the next scan.
                provider.ExternalReadbackOverride = "new-config-only-drift";
                provider.EnableOnSuccessfulConfigurationOnce = true;
                provider.FailDisableOnce = true;
                var beforeStop = provider.ToggleCalls;
                Scan();
                if (provider.ConfigurationCalls != 7 || !provider.ControlEnabled ||
                    provider.ToggleCalls != beforeStop + 1 ||
                    restored.ContainsKey(id) || !retries.Contains(id))
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 8 || provider.ControlEnabled ||
                    provider.ToggleCalls != beforeStop + 2 ||
                    !restored.ContainsKey(id) || retries.Contains(id))
                    return false;
                Scan();
                return provider.ConfigurationCalls == 8 && !provider.ControlEnabled;
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingRepeatedRestoreFailuresBackoffPerProvider()
        {
            var id = "vcr-parity-retry-backoff-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var retriesField = type.GetField("_workspaceTrackingRestoreRetries", flags);
                var scanField = type.GetField("_nextTrackingRestoreScan", flags);
                if (restore == null || configuredField == null || restoredField == null ||
                    retriesField == null || scanField == null)
                    return false;

                var code = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                if (!code.Contains("IsWorkspaceTrackingRestoreRetryDue(id, source)") ||
                    !code.Contains("RecordWorkspaceTrackingRestoreFailure(") ||
                    !code.Contains("id, source, observedRetrySettings)") ||
                    !code.Contains("_workspaceTrackingRestoreRetries.Remove(id)") ||
                    !code.Contains("!ReferenceEquals(retry.Provider, source)"))
                    return false;

                shellRoot = new GameObject("P1 tracking retry backoff shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 repeatedly failing tracking provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.ConfigurationFailuresRemaining = 4;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                var retries = retriesField.GetValue(shell) as System.Collections.IDictionary;
                if (configured == null || restored == null || retries == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "saved-after-retry");

                void Scan()
                {
                    scanField.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }
                object Retry() => retries.Contains(id) ? retries[id] : null;
                var retryType = retriesField.FieldType.GetGenericArguments()[1];
                var failures = retryType.GetField("Failures",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var retryAfter = retryType.GetField("RetryAfter",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (failures == null || retryAfter == null)
                    return false;
                bool ExpectFailure(int count, float expectedDelay)
                {
                    var retry = Retry();
                    return retry != null && !restored.ContainsKey(id) &&
                           provider.ConfigurationCalls == count &&
                           (int)failures.GetValue(retry) == count &&
                           (float)retryAfter.GetValue(retry) - Time.unscaledTime >= expectedDelay - 0.75f &&
                           (float)retryAfter.GetValue(retry) - Time.unscaledTime <= expectedDelay + 0.75f;
                }
                void BypassElapsedTime()
                {
                    var retry = Retry();
                    if (retry != null)
                        retryAfter.SetValue(retry, 0f);
                }

                Scan();
                if (!ExpectFailure(1, 0f))
                    return false;
                // Respect the real global two-second scan gate: this immediate
                // second call must not retry unless a test advances the gate.
                restore.Invoke(shell, null);
                if (provider.ConfigurationCalls != 1)
                    return false;
                Scan();
                if (!ExpectFailure(2, 0f))
                    return false;
                Scan();
                if (!ExpectFailure(3, 4f))
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 3)
                    return false;
                BypassElapsedTime();
                Scan();
                if (!ExpectFailure(4, 8f))
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 4)
                    return false;
                BypassElapsedTime();
                Scan();
                if (provider.ConfigurationCalls != 5 ||
                    provider.AppliedValue != "saved-after-retry" ||
                    !provider.ControlEnabled || !restored.ContainsKey(id) ||
                    retries.Contains(id))
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 5)
                    return false;

                // Failed replay from a cached provider must not delay the next
                // instance with the same logical ID after a real disconnect.
                provider.ExternalReadbackOverride = "changed-externally";
                provider.FailConfigurationOnce = true;
                Scan();
                if (provider.ConfigurationCalls != 6 || !retries.Contains(id) ||
                    restored.ContainsKey(id))
                    return false;
                UnityEngine.Object.DestroyImmediate(providerRoot);
                providerRoot = new GameObject("P1 replacement during retry backoff");
                var replacement = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                replacement.Id = id;
                Scan();
                if (replacement.ConfigurationCalls != 1 ||
                    replacement.AppliedValue != "saved-after-retry" ||
                    !replacement.ControlEnabled || retries.Contains(id) ||
                    !restored.ContainsKey(id))
                    return false;
                Scan();
                return replacement.ConfigurationCalls == 1 &&
                       PlayerPrefs.GetString(settingKey) == "saved-after-retry";
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingCachedProviderExternalSettingsDriftRestoresSavedPreference()
        {
            var id = "vcr-parity-cached-settings-drift-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject providerRoot = null;
            try
            {
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var version = type.GetMethod("GetWorkspaceTrackingEditVersion", flags);
                var canSave = type.GetMethod("IsWorkspaceTrackingEditVersionCurrent", flags);
                var configuredField = type.GetField("_workspaceConfiguredTracking", flags);
                var restoredField = type.GetField("_workspaceRestoredTracking", flags);
                var baselineField = type.GetField("_workspaceTrackingRestoredReadbacks", flags);
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                if (restore == null || version == null || canSave == null ||
                    configuredField == null || restoredField == null ||
                    baselineField == null || scan == null)
                    return false;

                // Run actual shell restoration, not just a source-text assertion.
                var source = File.ReadAllText(Path.Combine(Application.dataPath,
                    "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                if (!source.Contains("AreWorkspaceTrackingCachedSettingsCurrent(source, id)") ||
                    !source.Contains("TryRememberWorkspaceTrackingReadback(source, id)") ||
                    !source.Contains("WorkspaceTrackingValuesMatch(baseline.Saved, current.Saved)") ||
                    !source.Contains("WorkspaceTrackingValuesMatch(baseline.Live, current.Live)"))
                    return false;

                shellRoot = new GameObject("P1 cached tracking settings drift shell");
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                providerRoot = new GameObject("P1 cached tracking settings drift provider");
                var provider = providerRoot.AddComponent<P1TrackingReconnectProbe>();
                provider.Id = id;
                provider.NormalizeAppliedReadback = true;
                var configured = configuredField.GetValue(shell) as HashSet<string>;
                var restored = restoredField.GetValue(shell) as
                    Dictionary<string, ITrackingRuntimeControl>;
                var snapshots = baselineField.GetValue(shell) as System.Collections.IDictionary;
                if (configured == null || restored == null || snapshots == null)
                    return false;
                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "  saved-value  ");

                int Version() => (int)version.Invoke(shell, new object[] { id });
                bool DraftValid(int previous) => (bool)canSave.Invoke(
                    shell, new object[] { id.ToUpperInvariant(), previous });
                void Scan()
                {
                    scan.SetValue(shell, 0f);
                    restore.Invoke(shell, null);
                }

                Scan();
                if (!provider.ControlEnabled || provider.ConfigurationCalls != 1 ||
                    provider.AppliedValue != "  saved-value  " ||
                    provider.ExternalReadbackOverride != "saved-value" ||
                    !restored.ContainsKey(id) || !snapshots.Contains(id))
                    return false;
                var settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 1 || Version() != settled ||
                    provider.ToggleCalls != 1)
                    return false;

                // External configuration-only drift: live On/Off did not change.
                provider.ExternalReadbackOverride = "external-change";
                var oldDraft = Version();
                Scan();
                if (provider.ConfigurationCalls != 2 ||
                    provider.ExternalReadbackOverride != "saved-value" ||
                    Version() == oldDraft || DraftValid(oldDraft) ||
                    !restored.ContainsKey(id) ||
                    PlayerPrefs.GetString(settingKey) != "  saved-value  ")
                    return false;
                settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 2 || Version() != settled)
                    return false;

                // Persisted value drift with unchanged live readback also replays.
                PlayerPrefs.SetString(settingKey, "  updated-value  ");
                oldDraft = Version();
                Scan();
                if (provider.ConfigurationCalls != 3 ||
                    provider.ExternalReadbackOverride != "updated-value" ||
                    Version() == oldDraft || DraftValid(oldDraft))
                    return false;
                settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 3 || Version() != settled)
                    return false;

                // A failed first replay must not leave a restored cache entry.
                provider.ExternalReadbackOverride = "drift-before-retry";
                provider.FailConfigurationOnce = true;
                oldDraft = Version();
                Scan();
                if (provider.ConfigurationCalls != 4 ||
                    restored.ContainsKey(id) || snapshots.Contains(id) ||
                    Version() == oldDraft || DraftValid(oldDraft) ||
                    PlayerPrefs.GetString(settingKey) != "  updated-value  ")
                    return false;
                Scan();
                if (provider.ConfigurationCalls != 5 ||
                    provider.ExternalReadbackOverride != "updated-value" ||
                    !restored.ContainsKey(id) || !snapshots.Contains(id))
                    return false;

                // Saved Off means config-only drift must not start capture.
                PlayerPrefs.SetInt(enabledKey, 0);
                Scan();
                if (provider.ControlEnabled || provider.ConfigurationCalls != 6 ||
                    !restored.ContainsKey(id))
                    return false;
                var stoppedToggles = provider.ToggleCalls;
                provider.ExternalReadbackOverride = "off-but-config-drift";
                Scan();
                if (provider.ControlEnabled || provider.ToggleCalls != stoppedToggles ||
                    provider.ConfigurationCalls != 7 ||
                    provider.ExternalReadbackOverride != "updated-value")
                    return false;
                settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 7 || Version() != settled ||
                    provider.ControlEnabled || !restored.ContainsKey(id) ||
                    !snapshots.Contains(id))
                    return false;

                // Native/Toolkit Add or Toggle can mark a provider cached before
                // the first successful restore snapshot. Existing saved fields
                // must still be replayed on the next scan, not skipped forever.
                snapshots.Remove(id);
                provider.ExternalReadbackOverride = "readded-with-old-prefs";
                oldDraft = Version();
                Scan();
                if (provider.ConfigurationCalls != 8 ||
                    provider.ExternalReadbackOverride != "updated-value" ||
                    Version() == oldDraft || DraftValid(oldDraft) ||
                    provider.ControlEnabled || !snapshots.Contains(id))
                    return false;
                settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 8 || Version() != settled)
                    return false;

                // No saved configuration: a missing snapshot should establish
                // a clean baseline without restarting or applying device fields.
                PlayerPrefs.DeleteKey(settingKey);
                snapshots.Remove(id);
                provider.ExternalReadbackOverride = "unpersisted-live-value";
                settled = Version();
                Scan();
                if (provider.ConfigurationCalls != 8 || Version() != settled ||
                    provider.ExternalReadbackOverride != "unpersisted-live-value" ||
                    !restored.ContainsKey(id) || !snapshots.Contains(id))
                    return false;
                Scan();
                return provider.ConfigurationCalls == 8 && Version() == settled &&
                       !provider.ControlEnabled;
            }
            finally
            {
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool TrackingReconnectedProviderRestoresPreferences()
        {
            var id = "vcr-parity-reconnect-" + Guid.NewGuid().ToString("N");
            var enabledKey = "VCR.TrackingInputs.Enabled.v1." + id;
            var settingKey = "VCR.TrackingInputs.Setting.v1." + id + ".test-value";
            GameObject shellRoot = null;
            GameObject sourceRoot = null;
            try
            {
                shellRoot = new GameObject("P1 tracking reconnect shell");
                // Keep U1 UI setup off; exercise its real tracking restore path.
                shellRoot.SetActive(false);
                var shell = shellRoot.AddComponent<WorkspaceToolkitShell>();
                var type = typeof(WorkspaceToolkitShell);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configured = type.GetField("_workspaceConfiguredTracking", flags)
                    ?.GetValue(shell) as HashSet<string>;
                var restored = type.GetField("_workspaceRestoredTracking", flags)
                    ?.GetValue(shell) as Dictionary<string, ITrackingRuntimeControl>;
                var scan = type.GetField("_nextTrackingRestoreScan", flags);
                var restore = type.GetMethod("RestoreWorkspaceTrackingPreferences", flags);
                var editorIdentity = type.GetMethod(
                    "IsWorkspaceTrackingEditorSourceCurrent", StaticPrivate);
                if (configured == null || restored == null || scan == null ||
                    restore == null || editorIdentity == null)
                    return false;
                // Both the native window and UI Toolkit editor must enforce
                // the identity check before submitting any pending values.
                var trackingSource = File.ReadAllText(Path.Combine(
                    Application.dataPath, "VCR/Runtime/UI/WorkspaceToolkitShell.TrackingSettings.cs"));
                var nativeSaveAt = trackingSource.IndexOf(
                    "private void SaveWorkspaceNativeTrackingDetails()", StringComparison.Ordinal);
                var nativeSaveEnd = trackingSource.IndexOf(
                    "private bool HandleWorkspaceNativeTrackingAction(", StringComparison.Ordinal);
                var toolkitEditAt = trackingSource.IndexOf(
                    "private void EditWorkspaceTrackingSettings(", StringComparison.Ordinal);
                if (nativeSaveAt < 0 || nativeSaveEnd <= nativeSaveAt || toolkitEditAt < 0 ||
                    !trackingSource.Substring(nativeSaveAt, nativeSaveEnd - nativeSaveAt)
                        .Contains("IsWorkspaceTrackingConfiguredEditorCurrent(") ||
                    !trackingSource.Substring(toolkitEditAt)
                        .Contains("IsWorkspaceTrackingConfiguredEditorCurrent("))
                    return false;
                // Native Add must stop capture through the shared guard
                // before it commits membership or caches the provider. Its
                // rejected path must return before either mutation.
                var nativeAddAt = trackingSource.IndexOf(
                    "!TryStopWorkspaceTrackingForMembership(control, out var addError)",
                    StringComparison.Ordinal);
                var nativeAddReturn = nativeAddAt < 0 ? -1 :
                    trackingSource.IndexOf("return true;", nativeAddAt,
                        StringComparison.Ordinal);
                var nativeAddCommit = nativeAddAt < 0 ? -1 :
                    trackingSource.IndexOf("_workspaceConfiguredTracking.Add(id);",
                        nativeAddAt, StringComparison.Ordinal);
                var nativeAddCache = nativeAddAt < 0 ? -1 :
                    trackingSource.IndexOf("_workspaceRestoredTracking[id] = control;",
                        nativeAddAt, StringComparison.Ordinal);
                var nativeAddEnd = trackingSource.IndexOf(
                    "private ScrollView _workspaceTrackingList;", StringComparison.Ordinal);
                if (nativeAddAt < 0 || nativeAddReturn <= nativeAddAt ||
                    nativeAddCommit <= nativeAddReturn ||
                    nativeAddCache <= nativeAddCommit ||
                    nativeAddEnd <= nativeAddCache)
                    return false;

                configured.Add(id);
                PlayerPrefs.SetInt(enabledKey, 1);
                PlayerPrefs.SetString(settingKey, "persisted-value");
                sourceRoot = new GameObject("P1 tracking reconnect first provider");
                var first = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                first.Id = id;
                if (!(bool)editorIdentity.Invoke(null, new object[] { first, first }) ||
                    (bool)editorIdentity.Invoke(null, new object[] { first, null }))
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!first.ControlEnabled || first.ToggleCalls != 1 ||
                    first.ConfigurationCalls != 1 ||
                    first.AppliedValue != "persisted-value")
                    return false;

                // The same component should not repeatedly reapply settings.
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (first.ToggleCalls != 1 || first.ConfigurationCalls != 1)
                    return false;

                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = null;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                // The logical preference survives disconnect but the destroyed
                // component reference must not be retained by the live shell.
                if (restored.ContainsKey(id) || !configured.Contains(id) ||
                    PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;

                sourceRoot = new GameObject("P1 tracking reconnect replacement provider");
                var replacement = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                replacement.Id = id;
                // A stale editor from the old source may not apply to the
                // replacement even if both advertise the same logical ID.
                if ((bool)editorIdentity.Invoke(null,
                        new object[] { first, replacement }) ||
                    !(bool)editorIdentity.Invoke(null,
                        new object[] { replacement, replacement }))
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!replacement.ControlEnabled || replacement.ToggleCalls != 1 ||
                    replacement.ConfigurationCalls != 1 ||
                    replacement.AppliedValue != "persisted-value")
                    return false;

                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (replacement.ToggleCalls != 1 ||
                    replacement.ConfigurationCalls != 1)
                    return false;

                // A temporary provider configuration failure must not mark
                // the provider restored or rewrite the saved preference.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 tracking config retry provider");
                var configRetry = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                configRetry.Id = id;
                configRetry.FailConfigurationOnce = true;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (restored.ContainsKey(id) ||
                    configRetry.ConfigurationCalls != 1 ||
                    configRetry.ToggleCalls != 0 ||
                    PlayerPrefs.GetString(settingKey) != "persisted-value")
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!configRetry.ControlEnabled || configRetry.ToggleCalls != 1 ||
                    configRetry.ConfigurationCalls != 2 ||
                    configRetry.AppliedValue != "persisted-value")
                    return false;

                // A temporary enable failure also leaves the restore pending.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 tracking enable retry provider");
                var enableRetry = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                enableRetry.Id = id;
                enableRetry.FailEnableOnce = true;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (restored.ContainsKey(id) || enableRetry.ControlEnabled ||
                    enableRetry.ToggleCalls != 1 ||
                    enableRetry.ConfigurationCalls != 1 ||
                    PlayerPrefs.GetInt(enabledKey, 0) != 1)
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (!enableRetry.ControlEnabled ||
                    enableRetry.ToggleCalls != 2 ||
                    enableRetry.ConfigurationCalls != 2 ||
                    enableRetry.AppliedValue != "persisted-value")
                    return false;

                // A configurable source may not have discovered its schema
                // yet. Null must postpone both config and enabled restoration.
                UnityEngine.Object.DestroyImmediate(sourceRoot);
                sourceRoot = new GameObject("P1 tracking delayed schema provider");
                var delayed = sourceRoot.AddComponent<P1TrackingReconnectProbe>();
                delayed.Id = id;
                delayed.ReturnNullSchemaOnce = true;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                if (restored.ContainsKey(id) ||
                    delayed.SchemaCalls != 1 ||
                    delayed.ConfigurationCalls != 0 ||
                    delayed.ToggleCalls != 0 ||
                    PlayerPrefs.GetString(settingKey) != "persisted-value")
                    return false;
                scan.SetValue(shell, 0f);
                restore.Invoke(shell, null);
                return restored.ContainsKey(id) &&
                       delayed.SchemaCalls == 3 &&
                       delayed.ControlEnabled &&
                       delayed.ToggleCalls == 1 &&
                       delayed.ConfigurationCalls == 1 &&
                       delayed.AppliedValue == "persisted-value";
            }
            finally
            {
                if (sourceRoot != null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if (shellRoot != null) UnityEngine.Object.DestroyImmediate(shellRoot);
                PlayerPrefs.DeleteKey(enabledKey);
                PlayerPrefs.DeleteKey(settingKey);
            }
        }

        private static bool LegacyRestoreSkipsDestroyedRefresh()
        {
            var source = File.ReadAllText(Path.Combine(
                Application.dataPath, "VCR/Runtime/UI/ApplicationUiController.cs"));
            var at = source.IndexOf("Previous character restored.", StringComparison.Ordinal);
            if (at < 0) return false;
            var load = source.IndexOf("Loading character...", at, StringComparison.Ordinal);
            var reload = source.IndexOf("Reloading character...", at, StringComparison.Ordinal);
            if (load < 0 || reload < 0) return false;
            if (!HasLiveRefreshAfter(source, load, "LoadCharacterAsync") ||
                !HasLiveRefreshAfter(source, reload, "ReloadCharacterAsync")) return false;
            var refresh = source.IndexOf("RefreshAll();", at, StringComparison.Ordinal);
            if (refresh < 0) return false;
            var guard = source.LastIndexOf("this != null", refresh, StringComparison.Ordinal);
            return guard > at && refresh - guard < 400;
        }
        private static bool ContinuationStopsBeforeUi(string source, string awaitCall)
        {
            var awaitAt = source.IndexOf("await sceneRuntime." + awaitCall, StringComparison.Ordinal);
            if (awaitAt < 0) return false;
            var methodEnd = source.IndexOf("\n        private ", awaitAt, StringComparison.Ordinal);
            if (methodEnd < 0) methodEnd = source.Length;
            var body = source.Substring(awaitAt, methodEnd - awaitAt);
            // Success, cancellation, and failure each resume on a possibly
            // destroyed shell. Every path must stop before a UI write.
            return body.Contains("if (loaded == null)")
                ? CountGuards(body) >= 3
                : body.IndexOf("this == null", StringComparison.Ordinal) >= 0 &&
                  body.IndexOf("this == null", StringComparison.Ordinal) < body.IndexOf("_actionResult", StringComparison.Ordinal) &&
                  CountGuards(body) >= 3;
        }

        private static int CountGuards(string body)
        {
            var count = 0;
            var at = 0;
            while ((at = body.IndexOf("if (this == null) return;", at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += 10;
            }
            return count;
        }
        private static bool InvokeBool(MethodInfo method, params object[] arguments)
        {
            try
            {
                return (bool)method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    method.Name + " threw", exception.InnerException ?? exception);
            }
        }

        private static void InvokeVoid(MethodInfo method, params object[] arguments)
        {
            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    method.Name + " threw", exception.InnerException ?? exception);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
    // Source-free runtime fixture: an OS/device reconnection can destroy a
    // provider and create a new component with exactly the same logical ID.
    internal sealed class P1TrackingReconnectProbe : MonoBehaviour,
        ITrackingRuntimeControl, ITrackingRuntimeConfigurable
    {
        public string Id { get; set; }
        public string ControlId => Id;
        public string DisplayName => "P1 reconnect probe";
        public bool ControlEnabled { get; private set; }
        public TrackingSourceHealthState ControlHealthState =>
            TrackingSourceHealthState.Healthy;
        public string ControlError => "";
        public int ToggleCalls { get; private set; }
        public int ConfigurationCalls { get; private set; }
        public string AppliedValue { get; private set; }
        public bool FailConfigurationOnce { get; set; }
        public int ConfigurationFailuresRemaining { get; set; }
        public bool FailConfigurationAfterMutationOnce { get; set; }
        public bool FailConfigurationAfterEnablingOnce { get; set; }
        public bool EnableOnSuccessfulConfigurationOnce { get; set; }
        public bool FailEnableOnce { get; set; }
        public bool FailDisableOnce { get; set; }
        public bool AcknowledgeEnableWithoutChangingOnce { get; set; }
        public bool AcknowledgeDisableWithoutStoppingOnce { get; set; }
        public bool FailEnableAfterMutationOnce { get; set; }
        public bool FailDisableAfterMutationOnce { get; set; }
        public bool AppliedWhileEnabled { get; private set; }
        public bool ReturnNullSchemaOnce { get; set; }
        public bool ReportAdditionalSetting { get; set; }
        public bool ReportOversizedSchema { get; set; }
        public string ExternalReadbackOverride { get; set; }
        public bool NormalizeAppliedReadback { get; set; }
        public bool ResetOnRecovery { get; set; }
        public bool FailRecoveryOnce { get; set; }
        public bool FailRecoveryAfterMutationOnce { get; set; }
        public bool FailRecoveryAfterSettingsMutationOnce { get; set; }
        public int RecoveryCalls { get; private set; }
        public int SchemaCalls { get; private set; }
        public string ConfigurationTitle => "P1 reconnect configuration";
        public bool OpenConfigurationOnAdd => false;

        public IReadOnlyList<TrackingRuntimeSetting> GetConfigurationSettings()
        {
            SchemaCalls++;
            if (ReturnNullSchemaOnce)
            {
                ReturnNullSchemaOnce = false;
                return null;
            }
            if (ReportOversizedSchema)
                return new TrackingRuntimeSetting[257];
            var testValue = new TrackingRuntimeSetting(
                "test-value", "Test value", ExternalReadbackOverride ?? "default-value");
            return ReportAdditionalSetting
                ? new[] { testValue, new TrackingRuntimeSetting(
                    "external-field", "External field", "new") }
                : new[] { testValue };
        }

        public bool TryApplyConfiguration(
            IReadOnlyDictionary<string, string> values, out string error)
        {
            if (!values.TryGetValue("test-value", out var value))
            {
                error = "Missing persisted test value";
                return false;
            }
            ConfigurationCalls++;
            if (ConfigurationFailuresRemaining > 0)
            {
                ConfigurationFailuresRemaining--;
                error = "Repeated simulated device initialization fault";
                return false;
            }
            if (FailConfigurationOnce)
            {
                FailConfigurationOnce = false;
                error = "Provider not initialized yet";
                return false;
            }
            if (FailConfigurationAfterEnablingOnce)
            {
                FailConfigurationAfterEnablingOnce = false;
                ControlEnabled = true;
                error = "Simulated Apply failure after starting capture";
                return false;
            }
            if (FailConfigurationAfterMutationOnce)
            {
                FailConfigurationAfterMutationOnce = false;
                ExternalReadbackOverride = value;
                error = "Simulated restart fault after applying live settings";
                return false;
            }
            AppliedWhileEnabled |= ControlEnabled;
            AppliedValue = value;
            if (EnableOnSuccessfulConfigurationOnce)
            {
                EnableOnSuccessfulConfigurationOnce = false;
                ControlEnabled = true;
            }
            if (NormalizeAppliedReadback)
                ExternalReadbackOverride = value.Trim();
            error = null;
            return true;
        }

        public bool TrySetControlEnabled(bool enabled, out string error)
        {
            ToggleCalls++;
            if (FailEnableOnce && enabled)
            {
                FailEnableOnce = false;
                error = "Device not available yet";
                return false;
            }
            if (FailDisableOnce && !enabled)
            {
                FailDisableOnce = false;
                error = "Capture is still stopping";
                return false;
            }
            if (enabled && AcknowledgeEnableWithoutChangingOnce)
            {
                AcknowledgeEnableWithoutChangingOnce = false;
                error = null;
                return true;
            }
            if (!enabled && AcknowledgeDisableWithoutStoppingOnce)
            {
                AcknowledgeDisableWithoutStoppingOnce = false;
                error = null;
                return true;
            }
            if ((enabled && FailEnableAfterMutationOnce) ||
                (!enabled && FailDisableAfterMutationOnce))
            {
                FailEnableAfterMutationOnce = false;
                FailDisableAfterMutationOnce = false;
                ControlEnabled = enabled;
                error = "Simulated failed toggle after changing the live source";
                return false;
            }
            error = null;
            ControlEnabled = enabled;
            return true;
        }

        public bool TryRecover(out string error)
        {
            RecoveryCalls++;
            if (FailRecoveryOnce)
            {
                FailRecoveryOnce = false;
                error = "Simulated recovery failure";
                return false;
            }
            if (FailRecoveryAfterMutationOnce)
            {
                FailRecoveryAfterMutationOnce = false;
                ControlEnabled = false;
                ExternalReadbackOverride = "reset-after-failed-recovery";
                error = "Simulated restart fault after mutating device state";
                return false;
            }
            if (FailRecoveryAfterSettingsMutationOnce)
            {
                FailRecoveryAfterSettingsMutationOnce = false;
                ExternalReadbackOverride = "changed-after-failed-recovery";
                error = "Simulated restart fault after changing device settings";
                return false;
            }
            if (ResetOnRecovery)
            {
                ControlEnabled = false;
                AppliedValue = null;
                ResetOnRecovery = false;
            }
            error = null;
            return true;
        }
    }
}
