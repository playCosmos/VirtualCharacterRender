using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using VCR.Runtime.UI;

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
}
