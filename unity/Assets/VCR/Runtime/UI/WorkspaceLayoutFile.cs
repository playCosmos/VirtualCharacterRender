using System;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.UI
{
    /// <summary>Versioned, atomic workspace-only layout snapshot. Does not store model/device settings.</summary>
    internal static class WorkspaceLayoutFile
    {
        private const int Version = 1;
        private const string FileName = "workspace-layout.v1.json";

        [Serializable]
        private sealed class Layout
        {
            public int version = Version;
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
            public string[] docks;
            public int[] orders;
        }

        private static readonly string[] Ids = WorkspacePanelCatalog.GetDefaultPanelIds(true);
        private static string PathName => Path.Combine(Application.persistentDataPath, FileName);

        internal static bool Save()
        {
            try
            {
                var layout = new Layout
                {
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
                    docks = new string[Ids.Length],
                    orders = new int[Ids.Length]
                };
                for (var i = 0; i < Ids.Length; i++)
                {
                    layout.docks[i] = PlayerPrefs.GetString("VCR.UI.Workspace.Dock." + Ids[i] + ".v1", "");
                    layout.orders[i] = PlayerPrefs.GetInt("VCR.UI.Workspace.Order." + Ids[i] + ".v1", -1);
                }
                var path = PathName;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(layout, true));
                if (File.Exists(path))
                {
                    try { File.Replace(temporary, path, path + ".bak"); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, path + ".bak", true);
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

        internal static bool Load()
        {
            if (TryRead(PathName, out var layout) || TryRead(PathName + ".bak", out layout))
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
                for (var i = 0; i < Ids.Length; i++)
                {
                    var dock = layout.docks[i];
                    if (dock == "left" || dock == "right" || dock == "bottom")
                        PlayerPrefs.SetString("VCR.UI.Workspace.Dock." + Ids[i] + ".v1", dock);
                    if (layout.orders[i] >= 0)
                        PlayerPrefs.SetInt("VCR.UI.Workspace.Order." + Ids[i] + ".v1", layout.orders[i]);
                }
                PlayerPrefs.Save();
                return true;
            }
            return false;
        }

        private static void SetSize(string name, float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            PlayerPrefs.SetFloat("VCR.UI.Workspace." + name + ".v1", Mathf.Clamp(value, min, max));
        }

        private static void SetTab(string dock, string panelName)
        {
            if (string.IsNullOrEmpty(panelName)) return;
            foreach (var id in Ids)
                if (panelName == "vcr-panel-" + id)
                {
                    PlayerPrefs.SetString("VCR.UI.Workspace.ActiveTab." + dock + ".v1", panelName);
                    return;
                }
        }

        private static bool TryRead(string path, out Layout layout)
        {
            layout = null;
            try
            {
                if (!File.Exists(path)) return false;
                var candidate = JsonUtility.FromJson<Layout>(File.ReadAllText(path));
                if (candidate == null || candidate.version != Version ||
                    candidate.docks == null || candidate.orders == null ||
                    candidate.docks.Length != Ids.Length || candidate.orders.Length != Ids.Length)
                    return false;
                for (var i = 0; i < Ids.Length; i++)
                {
                    var dock = candidate.docks[i];
                    if (!string.IsNullOrEmpty(dock) && dock != "left" && dock != "right" && dock != "bottom")
                        return false;
                    if (candidate.orders[i] < -1 || candidate.orders[i] > Ids.Length * 4)
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
