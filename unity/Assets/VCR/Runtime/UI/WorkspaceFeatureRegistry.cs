using System;
using System.Collections.Generic;
using System.Text;

namespace VCR.Runtime.UI
{
    /// <summary>Stable search metadata. No VisualElement references survive a UI rebuild.</summary>
    public sealed class WorkspaceFeature
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Category;
        public readonly string Description;
        public readonly string PanelId;
        public readonly string TargetName;
        public readonly bool RequiresAdvanced;
        public readonly int NativeSettingsCategory;
        public readonly string[] Keywords;

        public WorkspaceFeature(string id, string title, string category,
            string description, string panelId, string targetName,
            bool requiresAdvanced, int nativeSettingsCategory, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Feature ID and title are required.");
            if (nativeSettingsCategory < -1 || nativeSettingsCategory > 3)
                throw new ArgumentOutOfRangeException(nameof(nativeSettingsCategory));
            if (nativeSettingsCategory < 0 && panelId != null &&
                !WorkspacePanelCatalog.IsKnownPanel(panelId))
                throw new ArgumentException("Feature refers to an unknown workspace panel.");
            Id = id;
            Title = title;
            Category = category ?? "Other";
            Description = description ?? "";
            PanelId = panelId;
            TargetName = targetName;
            RequiresAdvanced = requiresAdvanced;
            NativeSettingsCategory = nativeSettingsCategory;
            Keywords = keywords == null ? Array.Empty<string>() : (string[])keywords.Clone();
        }
    }

    /// <summary>
    /// Small in-memory feature index; search never executes the feature itself.
    /// Routes are looked up against the current UI Toolkit tree at navigation time.
    /// </summary>
    public sealed class WorkspaceFeatureRegistry
    {
        private readonly Dictionary<string, WorkspaceFeature> byId =
            new Dictionary<string, WorkspaceFeature>(StringComparer.Ordinal);
        private readonly List<WorkspaceFeature> features = new List<WorkspaceFeature>();
        public IReadOnlyList<WorkspaceFeature> All => features;

        public void Register(WorkspaceFeature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (byId.ContainsKey(feature.Id))
                throw new ArgumentException("Duplicate feature ID: " + feature.Id);
            byId.Add(feature.Id, feature);
            features.Add(feature);
        }

        public bool TryGet(string id, out WorkspaceFeature feature)
        {
            return byId.TryGetValue(id ?? "", out feature);
        }

        public List<WorkspaceFeature> Search(string query, int limit = 32)
        {
            var results = new List<WorkspaceFeature>();
            if (limit <= 0) return results;
            var normalized = Normalize(query).Trim();
            if (normalized.Length == 0)
            {
                results.AddRange(features);
                results.Sort((a, b) =>
                {
                    var category = string.Compare(a.Category, b.Category,
                        StringComparison.OrdinalIgnoreCase);
                    return category != 0 ? category :
                        string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                var terms = normalized.Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var ranks = new List<KeyValuePair<int, WorkspaceFeature>>();
                foreach (var feature in features)
                {
                    var rank = Rank(feature, terms, normalized);
                    if (rank > 0)
                        ranks.Add(new KeyValuePair<int, WorkspaceFeature>(rank, feature));
                }
                ranks.Sort((a, b) =>
                {
                    var rank = b.Key.CompareTo(a.Key);
                    return rank != 0 ? rank : string.Compare(
                        a.Value.Title, b.Value.Title, StringComparison.OrdinalIgnoreCase);
                });
                foreach (var entry in ranks) results.Add(entry.Value);
            }
            if (results.Count > limit) results.RemoveRange(limit, results.Count - limit);
            return results;
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        }

        private static int Rank(WorkspaceFeature feature, string[] terms, string full)
        {
            var title = Normalize(feature.Title);
            var category = Normalize(feature.Category);
            var description = Normalize(feature.Description);
            var id = Normalize(feature.Id);
            var aliases = new string[feature.Keywords.Length];
            for (var i = 0; i < aliases.Length; ++i)
                aliases[i] = Normalize(feature.Keywords[i]);
            var score = title == full || id == full ? 1000 :
                title.StartsWith(full, StringComparison.Ordinal) ? 500 :
                title.Contains(full) ? 300 : 0;
            foreach (var alias in aliases)
                if (alias == full) score = Math.Max(score, 700);
                else if (alias.StartsWith(full, StringComparison.Ordinal))
                    score = Math.Max(score, 400);
            // All tokens must occur; never rank unrelated results due to one
            // shared word like "camera" in a multi-term query.
            foreach (var term in terms)
            {
                var matched = title.Contains(term) || category.Contains(term) ||
                    id.Contains(term) || description.Contains(term);
                var exactAlias = false;
                foreach (var alias in aliases)
                {
                    matched |= alias.Contains(term);
                    if (alias == term) exactAlias = true;
                }
                if (!matched) return 0;
                score += exactAlias ? 90 : title.Contains(term) ? 60 :
                    id.Contains(term) ? 35 : 15;
            }
            return score;
        }

        public static WorkspaceFeatureRegistry CreateDefault()
        {
            var registry = new WorkspaceFeatureRegistry();
            // Stable IDs, not localized UI labels, are the contract.
            registry.Register(new WorkspaceFeature("model.load", "Load VRM", "Character",
                "Load a local VRM model", WorkspacePanelCatalog.Sources,
                "vcr-search-model-path", false, -1,
                "vrm", "model", "character", "모델", "캐릭터", "불러오기", "경로"));
            registry.Register(new WorkspaceFeature("model.reload", "Reload character", "Character",
                "Reload the active VRM model", WorkspacePanelCatalog.Actions,
                "vcr-search-reload-model", false, -1,
                "reload", "model", "vrm", "다시 불러오기", "재로드"));
            registry.Register(new WorkspaceFeature("model.unload", "Unload character", "Character",
                "Find the existing unload control; does not execute it", WorkspacePanelCatalog.Actions,
                "vcr-search-unload-model", false, -1,
                "unload", "remove", "vrm", "모델 해제", "언로드"));
            registry.Register(new WorkspaceFeature("scene.list", "Scene management", "Scene / Camera",
                "Inspect and manage the current scene", WorkspacePanelCatalog.Scenes,
                null, false, -1, "scene", "장면", "씬", "상태"));
            registry.Register(new WorkspaceFeature("camera.position", "Camera position", "Scene / Camera",
                "Edit camera position and rotation", WorkspacePanelCatalog.Inspector,
                "vcr-search-camera-position", false, -1,
                "camera", "position", "rotation", "카메라", "위치", "회전"));
            registry.Register(new WorkspaceFeature("camera.fov", "Camera FOV", "Scene / Camera",
                "Edit the camera field of view", WorkspacePanelCatalog.Inspector,
                "vcr-search-camera-fov", false, -1,
                "camera", "fov", "field of view", "카메라", "시야각", "화각"));
            registry.Register(new WorkspaceFeature("camera.clip", "Camera clipping", "Scene / Camera",
                "Set near and far camera clipping planes", WorkspacePanelCatalog.Inspector,
                "vcr-search-camera-near", false, -1,
                "near", "far", "clip", "카메라", "클리핑"));
            registry.Register(new WorkspaceFeature("light.intensity", "Primary light", "Scene / Camera",
                "Edit the primary light and shadow options", WorkspacePanelCatalog.Inspector,
                "vcr-search-light-intensity", false, -1,
                "light", "intensity", "shadow", "조명", "밝기", "그림자"));
            registry.Register(new WorkspaceFeature("render.fps", "Target FPS", "Render / Output",
                "Edit the target frame rate", WorkspacePanelCatalog.Inspector,
                "vcr-search-render-fps", false, -1,
                "fps", "frame rate", "프레임", "프레임레이트"));
            registry.Register(new WorkspaceFeature("tracking.devices", "Tracking devices", "Tracking",
                "Inspect tracking sources and connection state", WorkspacePanelCatalog.Tracking,
                null, false, -1,
                "tracking", "device", "sensor", "트래킹", "추적", "장치"));
            registry.Register(new WorkspaceFeature("tracking.preview", "Tracking preview", "Tracking",
                "Find tracking camera privacy preview controls", WorkspacePanelCatalog.Tracking,
                null, false, -1,
                "camera", "tracking", "preview", "프리뷰", "트래킹 카메라"));
            registry.Register(new WorkspaceFeature("broadcast.output", "Broadcast output", "Render / Output",
                "Inspect broadcast and capture readiness", WorkspacePanelCatalog.Actions,
                null, false, -1,
                "broadcast", "obs", "stream", "송출", "방송", "출력"));
            registry.Register(new WorkspaceFeature("environment", "Environment states", "Workspace",
                "Find environment state and transition controls", WorkspacePanelCatalog.Actions,
                null, false, -1,
                "environment", "transition", "환경", "전환"));
            registry.Register(new WorkspaceFeature("appearance", "Appearance", "Appearance / Motion",
                "Find character appearance and preset controls", WorkspacePanelCatalog.Actions,
                null, false, -1,
                "appearance", "preset", "의상", "외형", "프리셋"));
            registry.Register(new WorkspaceFeature("motion", "Motion cues", "Appearance / Motion",
                "Find current pose, expression and motion controls", WorkspacePanelCatalog.Actions,
                null, false, -1,
                "motion", "pose", "expression", "모션", "포즈", "표정"));
            registry.Register(new WorkspaceFeature("events", "Event rules and logs", "Diagnostics",
                "Find event rules, logs and diagnostics", WorkspacePanelCatalog.Events,
                null, false, -1,
                "events", "logs", "diagnostics", "이벤트", "로그", "진단"));
            registry.Register(new WorkspaceFeature("materials", "Materials / shaders", "Appearance / Motion",
                "Inspect material and shader overrides", WorkspacePanelCatalog.Materials,
                null, true, -1,
                "materials", "shader", "재질", "셰이더", "마테리얼"));
            registry.Register(new WorkspaceFeature("modules", "Modules", "Diagnostics",
                "Inspect enabled capabilities and module state", WorkspacePanelCatalog.Capabilities,
                null, true, -1,
                "module", "plugin", "capability", "모듈", "플러그인"));
            registry.Register(new WorkspaceFeature("output.opaque", "Recover opaque window", "Render / Output",
                "Find the existing opaque output recovery control", null,
                "vcr-header-opaque", false, -1,
                "opaque", "transparency", "click through", "투명", "복구"));
            registry.Register(new WorkspaceFeature("settings.render", "Render settings", "Settings",
                "Open the native render settings category", null, null, false, 0,
                "settings", "render scale", "fps", "설정", "렌더", "환경설정"));
            registry.Register(new WorkspaceFeature("settings.character", "Character settings", "Settings",
                "Open the native character/model settings category", null, null, false, 1,
                "settings", "vrm", "path", "설정", "모델", "캐릭터"));
            registry.Register(new WorkspaceFeature("settings.tracking", "Tracking settings", "Settings",
                "Open the native tracking settings category", null, null, false, 2,
                "settings", "device", "tracking", "설정", "트래킹", "추적"));
            registry.Register(new WorkspaceFeature("settings.output", "Output settings", "Settings",
                "Open the native overlay/output settings category", null, null, false, 3,
                "settings", "overlay", "transparent", "설정", "출력", "투명"));
            return registry;
        }
    }
}
