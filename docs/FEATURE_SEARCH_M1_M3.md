# U1 Feature Search — M1–M3 integration contract

Status: **implemented on draft PR #44; Unity/Windows CI and operator sign-off remain separate gates.** This specification applies only to the UI Toolkit U1 workspace; it does not change the legacy uGUI production controller or the discontinued AI plugin idea.

## User-facing behavior

- A fixed-width **Find** button in the U1 header remains visible at compact 960×520 while the rest of the actions toolbar scrolls. `Ctrl+K` (Windows/Linux) or `Cmd+K` (macOS) opens or closes the same palette.
- The palette is an **overlay**, not a persistent dock or second implementation of settings. It has a query field, a scrollable result list, a category-based complete index when the query is empty, and a status/result count. Escape or a click on the backdrop closes it; Up/Down and Enter select/navigate; clicking a result navigates.
- Search accepts Korean/English aliases, Unicode-normalized tokens and partial matches. Exact feature names and aliases rank ahead of generic text. Multi-word queries require all tokens. Features have immutable IDs independent of translated labels, dock placement, or the current UI element instance. Search is **navigation-only**: it never executes destructive actions such as Unload VRM, toggles overlay click-through, or commits camera settings.
- The current index covers character load/reload/unload, scene, camera controls, lighting, render FPS, tracking, camera preview, broadcast, environment, appearance, motion, event rules/logs, advanced materials/modules, opaque recovery, and native settings (render/character/tracking/output). Search is scoped to the features actually present in U1, not speculative future UI.
- When selecting a feature inside a dock: (1) optionally enable Advanced **for this session only**, (2) show its actual left/right/bottom dock without writing visibility preferences, (3) temporarily select that panel's tab without writing `VCR.UI.Workspace.ActiveTab.*`, (4) resolve a fresh control by stable `name`, scroll it into view, focus it and briefly highlight its left border. The search result never stores a `VisualElement`. Existing draft preservation during `Build()` applies.
- Selecting Settings invokes the existing independent `DesktopNativeSettingsWindow` (or its existing unsupported-platform fallback) and navigates to the selected native category. No parallel controls are introduced in the palette.
- Manual tab selection clears the relevant temporary search override and uses the existing persisted tab behavior. The search overlay disappears during workspace rebuild/resize, rather than leaving stale or invisible focus references.

## Implementation map

| Responsibility | Existing/new location |
| --- | --- |
| Stable feature IDs, descriptors, alias matching, duplicate checking | `unity/Assets/VCR/Runtime/UI/WorkspaceFeatureRegistry.cs` |
| Palette, keyboard handling, results, navigation, temporary tab selection | `unity/Assets/VCR/Runtime/UI/WorkspaceToolkitShell.FeatureSearch.cs` |
| Header entry, key registration, named fields and dock/tab integration | `unity/Assets/VCR/Runtime/UI/WorkspaceToolkitShell.cs` |
| Existing native categories 0–3 and render/model/tracking/output actions | `WorkspaceToolkitShell.cs`, `DesktopNativeSettingsWindow.cs` |
| Stable panel IDs and layout migration | `WorkspacePanelCatalog.cs`, `WorkspaceLayoutFile.cs` |

All `.cs` files ship with explicit Unity `.meta` GUIDs. Search metadata is not stored in PlayerPrefs or external JSON. No new third-party packages, server, indexing daemon or disk-writing search cache is required.

## Verification

1. **Registry contract:** every feature ID is unique, panel routes refer to catalog IDs, native routes are 0–3, and Korean/English queries including `fov`, `시야각`, `vrm`, `트래킹` return the expected feature. Unknown words return no unrelated results. Search itself does not call runtime actions.
2. **Keyboard & overlay:** click Find; Ctrl/Cmd+K; type with English/Korean IME; Up/Down; Enter; Escape; click outside; verify no interaction leaks to underlying camera/model controls while the palette is open.
3. **Narrow GUI:** at 960×520 and expanded/narrow layouts, Find and the existing workspace/Settings/Opaque buttons remain available, palette results scroll without overlapping the status footer, and the selected result is keyboard reachable.
4. **Navigation:** Inspector Camera FOV and FPS focus/scroll; hidden bottom/left/right docks temporarily reappear without writing saved visibility; the selected dock tab is shown without changing persisted tab key; selecting Materials from Live temporarily enables Advanced without changing `WorkspaceModeKey`; native Settings category navigation still uses the real top-level Settings window. The Windows GUI probe separately types `materials` via OS keyboard, requires the exact first result, a real Enter navigation and a measured Materials scroll viewport wholly inside its right panel and above the bottom Dock; it preserves `unity-feature-materials.png` independently of the Camera FOV screenshot. It also performs a second **actual OS mouse click** on a measured toolbar button to return to Live, then uses real Ctrl+K/MATERIALS/Enter from Live and requires the resulting Advanced panel plus an unchanged persisted Live-mode preference. It retains separate `unity-feature-live.png` and `unity-feature-materials-from-live.png` images. Physical operator interaction, IME composition, and saved Dock preference integrity remain separate acceptance gates.
5. **Regression:** retain existing Windows native caption, free resize, maximize/restore, real UI Toolkit click, Settings 4-page click, Settings hide/reopen/WM_CLOSE, managed quit and runtime exception gates. Preserve source-matched screenshots and logs; CI does not imply manual model/tracking or real-user acceptance.
6. **Manual acceptance remaining:** VRM-loaded and tracking-hardware UI; window DPI and multiple monitors; macOS Command+K/AppKit Settings; Korean IME composition (Enter must not unexpectedly navigate while selecting a composition candidate); original operator-reported exceptions.

## Follow-up milestones (not in M1–M3)

- **M4:** user favorites, recent items and persisted preference controls.
- **M5:** optional safe inline commands, state-dependent availability and explicit confirmation for dangerous actions.
- **M6:** module/plugin-driven registration and deregistration with permissions and lifecycle validation.

Do not merge U1 or remove existing application uGUI until all relevant parity, physical operator and release acceptance gates are complete.
