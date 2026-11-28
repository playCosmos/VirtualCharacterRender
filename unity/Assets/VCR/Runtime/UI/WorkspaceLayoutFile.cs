using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.UI
{
    /// <summary>Versioned, atomic workspace-only layout snapshot. Does not store model/device settings.</summary>
    internal static class WorkspaceLayoutFile
    {
        private const int Version = 2;
        // Version 1 stored docks/orders by position in the panel catalog.
        // Keep reading it so an upgrade does not discard the operator's layout.
        private const int LegacyVersion = 1;
        // A manual checkpoint must not be overwritten by periodic recovery.
        private const string FileName = "workspace-layout.v1.json";
        private const string AutomaticFileName = "workspace-layout.autosave.v1.json";

        [Serializable]
        private sealed class Layout
        {
            // Zero (not the current version) so a truncated payload is rejected
            // instead of being silently interpreted as the newest schema.
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

        private static readonly string[] Ids = WorkspacePanelCatalog.GetDefaultPanelIds(true);

        private static string PathFor(string directory, string fileName) =>
            Path.Combine(string.IsNullOrEmpty(directory)
                ? UnityEngine.Application.persistentDataPath
                : directory, fileName);

        // Explicit Save updates both the checkpoint and startup recovery.
        // Only SaveAutomatic may run after subsequent unsaved layout changes.
        // directory is an internal test/import seam; production callers omit it.
        internal static bool Save(string directory = null)
        {
            if (!SaveToPath(PathFor(directory, FileName))) return false;
            return SaveToPath(PathFor(directory, AutomaticFileName));
        }

        internal static bool SaveAutomatic(string directory = null) =>
            SaveToPath(PathFor(directory, AutomaticFileName));

        private static bool SaveToPath(string path)
        {
            try
            {
                var layout = new Layout
                {
                    version = Version,
                    advanced = PlayerPrefs.GetInt("VCR.UI.Workspace.Advanced.v1", 0) != 0,
                    tabs = PlayerPrefs.GetInt("VCR.UI.Workspace.TabMode.v1", 0) != 0,
                    leftVisible = PlayerPrefs.GetInt("VCR.UI.Workspace.LeftVisible.v1", 1) != 0,
                    rightVisible = PlayerPrefs.GetInt("VCR.UI.Workspace.RightVisible.v1", 1) != 0,
                    bottomVisible = PlayerPrefs.GetInt("VCR.UI.Workspace.BottomVisible.v1", 1) != 0,
                    leftWidth = PlayerPrefs.GetFloat("VCR.UI.Workspace.LeftWidth.v1", 220f),
                    rightWidth = PlayerPrefs.GetFloat("VCR.UI.Workspace.RightWidth.v1", 260f),
                    bottomHeight = PlayerPrefs.GetFloat("VCR.UI.Workspace.BottomHeight.v1", 150f),
                    scenesHeight = PlayerPrefs.GetFloat("VCR.UI.Workspace.ScenesHeight.v1", 180f),
                    inspectorHeight = PlayerPrefs.GetFloat("VCR.UI.Workspace.InspectorHeight.v1", 260f),
                    activeLeft = PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.left.v1", ""),
                    activeRight = PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.right.v1", ""),
                    activeBottom = PlayerPrefs.GetString("VCR.UI.Workspace.ActiveTab.bottom.v1", ""),
                    panelIds = (string[])Ids.Clone(),
                    docks = new string[Ids.Length],
                    orders = new int[Ids.Length]
                };
                for (var i = 0; i < Ids.Length; i++)
                {
                    layout.docks[i] = PlayerPrefs.GetString("VCR.UI.Workspace.Dock." + Ids[i] + ".v1", "");
                    layout.orders[i] = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + Ids[i] + ".v1", -1);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(layout, true));
                if (File.Exists(path))
                {
                    // Never replace a valid backup with a corrupt primary snapshot.
                    var validPrimary = TryRead(path, out _);
                    if (validPrimary)
                    {
                        try { File.Replace(temporary, path, path + ".bak"); }
                        catch (PlatformNotSupportedException)
                        {
                            File.Copy(path, path + ".bak", true);
                            File.Delete(path);
                            File.Move(temporary, path);
                        }
                    }
                    else
                    {
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                }
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Workspace layout save failed: " + ex.Message);
                return false;
            }
        }

        // Startup prefers automatic recovery; existing manual v1 snapshot
        // remains the fallback for upgrades or a damaged automatic file.
        internal static bool LoadForStartup(string directory = null) =>
            Load(true, directory);

        // Explicit Restore ignores auto-recovery and keeps the operator's
        // deliberately saved checkpoint authoritative.
        internal static bool Load(string directory = null) =>
            Load(false, directory);

        private static bool Load(bool preferAutomatic, string directory)
        {
            Layout layout;
            if ((preferAutomatic &&
                    (TryRead(PathFor(directory, AutomaticFileName), out layout) ||
                     TryRead(PathFor(directory, AutomaticFileName) + ".bak", out layout))) ||
                TryRead(PathFor(directory, FileName), out layout) ||
                TryRead(PathFor(directory, FileName) + ".bak", out layout))
            {
                Apply(layout);
                return true;
            }
            return false;
        }

        private static void Apply(Layout layout)
        {
            PlayerPrefs.SetInt("VCR.UI.Workspace.Advanced.v1", layout.advanced ? 1 : 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.TabMode.v1", layout.tabs ? 1 : 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.LeftVisible.v1", layout.leftVisible ? 1 : 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.RightVisible.v1", layout.rightVisible ? 1 : 0);
            PlayerPrefs.SetInt("VCR.UI.Workspace.BottomVisible.v1", layout.bottomVisible ? 1 : 0);
            SetSize("LeftWidth", layout.leftWidth, 140f, 520f);
            SetSize("RightWidth", layout.rightWidth, 160f, 520f);
            SetSize("BottomHeight", layout.bottomHeight, 100f, 400f);
            SetSize("ScenesHeight", layout.scenesHeight, 100f, 400f);
            SetSize("InspectorHeight", layout.inspectorHeight, 140f, 520f);
            SetTab("left", layout.activeLeft);
            SetTab("right", layout.activeRight);
            SetTab("bottom", layout.activeBottom);

            var docks = new Dictionary<string, string>(StringComparer.Ordinal);
            var orders = new Dictionary<string, int>(StringComparer.Ordinal);
            if (layout.version == LegacyVersion)
            {
                // v1 arrays are positional. A shorter array is an older
                // append-only panel catalog; missing tail panels use defaults.
                var count = Math.Min(layout.docks?.Length ?? 0, Ids.Length);
                for (var i = 0; i < count; i++)
                {
                    docks[Ids[i]] = layout.docks[i];
                    orders[Ids[i]] = layout.orders[i];
                }
            }
            else
            {
                for (var i = 0; i < layout.panelIds.Length; i++)
                {
                    var id = layout.panelIds[i];
                    // Unknown IDs belong to a newer catalog. Ignore them so a
                    // downgrade does not discard the rest of the snapshot.
                    if (!WorkspacePanelCatalog.IsKnownPanel(id)) continue;
                    docks[id] = layout.docks[i];
                    orders[id] = layout.orders[i];
                }
            }

            foreach (var id in Ids)
            {
                var dockKey = "VCR.UI.Workspace.Dock." + id + ".v1";
                if (docks.TryGetValue(id, out var dock) &&
                    (dock == "left" || dock == "right" || dock == "bottom"))
                    PlayerPrefs.SetString(dockKey, dock);
                else PlayerPrefs.DeleteKey(dockKey);
                var orderKey = "VCR.UI.Workspace.Order." + id + ".v1";
                if (orders.TryGetValue(id, out var order) && order >= 0)
                    PlayerPrefs.SetInt(orderKey, order);
                else PlayerPrefs.DeleteKey(orderKey);
            }
            PlayerPrefs.Save();
        }

        internal static void ClearSnapshots(string directory = null)
        {
            // Reset is a user-requested destructive operation. Remove both snapshots
            // so a later restore cannot resurrect the previous arrangement.
            foreach (var basePath in new[]
                     {
                         PathFor(directory, FileName),
                         PathFor(directory, AutomaticFileName)
                     })
                foreach (var suffix in new[] { "", ".bak", ".tmp" })
                {
                    var path = basePath + suffix;
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch (IOException ex) { Debug.LogWarning("Workspace layout reset cleanup failed: " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { Debug.LogWarning("Workspace layout reset cleanup failed: " + ex.Message); }
                }
        }

        private static void SetSize(string name, float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            PlayerPrefs.SetFloat("VCR.UI.Workspace." + name + ".v1", Mathf.Clamp(value, min, max));
        }

        private static void SetTab(string dock, string panelName)
        {
            if (string.IsNullOrEmpty(panelName))
            {
                PlayerPrefs.DeleteKey("VCR.UI.Workspace.ActiveTab." + dock + ".v1");
                return;
            }
            foreach (var id in Ids)
                if (panelName == "vcr-panel-" + id)
                {
                    PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab." + dock + ".v1", panelName);
                    return;
                }
            PlayerPrefs.DeleteKey("VCR.UI.Workspace.ActiveTab." + dock + ".v1");
        }

        private static bool IsValidDock(string dock) =>
            string.IsNullOrEmpty(dock) ||
            dock == "left" || dock == "right" || dock == "bottom";

        private static bool IsValidOrder(int order) =>
            order >= -1 && order <= Ids.Length * 4;

        private static bool TryRead(string path, out Layout layout)
        {
            layout = null;
            try
            {
                if (!File.Exists(path)) return false;
                var candidate = JsonUtility.FromJson<Layout>(File.ReadAllText(path));
                if (candidate == null) return false;
                if (candidate.docks == null || candidate.orders == null ||
                    candidate.docks.Length != candidate.orders.Length)
                    return false;

                if (candidate.version == LegacyVersion)
                {
                    // A longer array cannot belong to this catalog; rejecting
                    // is safer than dropping the first N and misaligning them.
                    if (candidate.docks.Length > Ids.Length) return false;
                    for (var i = 0; i < candidate.docks.Length; i++)
                        if (!IsValidDock(candidate.docks[i]) ||
                            !IsValidOrder(candidate.orders[i]))
                            return false;
                    layout = candidate;
                    return true;
                }

                if (candidate.version != Version || candidate.panelIds == null ||
                    candidate.panelIds.Length != candidate.docks.Length)
                    return false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < candidate.panelIds.Length; i++)
                {
                    var id = candidate.panelIds[i];
                    if (string.IsNullOrEmpty(id) || !seen.Add(id))
                        return false;
                    // Unknown IDs belong to a newer catalog. Their values are
                    // ignored, so they must not invalidate known entries.
                    if (!WorkspacePanelCatalog.IsKnownPanel(id)) continue;
                    if (!IsValidDock(candidate.docks[i]) ||
                        !IsValidOrder(candidate.orders[i]))
                        return false;
                }
                layout = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Workspace layout read failed: " + ex.Message);
                return false;
            }
        }
    }
}
