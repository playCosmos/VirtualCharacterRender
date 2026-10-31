using System;
using System.Collections.Generic;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Stable workspace panel identities. Labels and translated titles must not
    /// be used as persistence keys. This catalog does not instantiate any UI.
    /// </summary>
    public static class WorkspacePanelCatalog
    {
        public const string Scenes = "scenes";
        public const string Sources = "sources";
        public const string Preview = "preview";
        public const string Inspector = "inspector";
        public const string Tracking = "tracking";
        public const string Actions = "actions";
        public const string Events = "events";
        public const string Materials = "materials";
        public const string Capabilities = "capabilities";

        private static readonly string[] LivePanels =
        {
            Scenes, Sources, Preview, Inspector, Tracking, Actions, Events
        };

        private static readonly string[] AdvancedPanels =
        {
            Scenes, Sources, Preview, Inspector, Tracking, Actions, Events,
            Materials, Capabilities
        };

        private static readonly HashSet<string> KnownPanels =
            new HashSet<string>(AdvancedPanels, StringComparer.Ordinal);

        public static string[] GetDefaultPanelIds(bool advanced)
        {
            return (string[])(advanced ? AdvancedPanels : LivePanels).Clone();
        }

        public static bool IsKnownPanel(string panelId)
        {
            return panelId != null && KnownPanels.Contains(panelId);
        }

        /// <summary>
        /// Validates persisted panel IDs before the future dock host restores
        /// them. Rejects duplicates, unknown IDs and missing preview.
        /// </summary>
        public static bool TryValidatePanelIds(
            IReadOnlyList<string> panelIds, out string error)
        {
            if (panelIds == null || panelIds.Count == 0)
            {
                error = "Workspace panel list is empty.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < panelIds.Count; i++)
            {
                var id = panelIds[i];
                if (!IsKnownPanel(id))
                {
                    error = "Unknown workspace panel ID.";
                    return false;
                }

                if (!seen.Add(id))
                {
                    error = "Duplicate workspace panel ID.";
                    return false;
                }
            }

            if (!seen.Contains(Preview))
            {
                error = "Workspace preview panel is missing.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
