# U1 UI parity audit

Current remote HEAD: b4c53fb57b7dba19f7207754ee1232752c1b8111. PR #44 remains Draft and unmerged.

## CI on b4c53fb5

Unity P0-P13 source-free validation succeeded: https://github.com/playCosmos/VirtualCharacterRender/actions/runs/43447066129
macOS P1 Player build and native startup smoke succeeded: https://github.com/playCosmos/VirtualCharacterRender/actions/runs/43447066150
Repository validation failed twice because the macOS arm64 launcher job was cancelled before a hosted runner acquired it. The annotation says the runner was not acquired. The other jobs in those runs passed: https://github.com/playCosmos/VirtualCharacterRender/actions/runs/43447076442 and https://github.com/playCosmos/VirtualCharacterRender/actions/runs/43447066213
This is an environment cancellation, not a test assertion failure. It is neither a product defect nor a pass.
ec676fad CI does not validate this SHA.

## Action 204

Native control IDs are not persisted. Win32 and AppKit return the ID registered by C# while the window exists.
204 remains reload saved configuration on legacy and U1. U1 render-default restore is 207. U1 205 is save and 206 is reload. U1 204 uses the same confirmed reload as 206.
The local source-free batch passed the label check before commit. Log: .tmp/u1-p1-action204.log.

## Feature classification

Code-complete means U1 calls the same runtime service and reports failure. Verified means a test on this SHA exercises it.

| Feature | Legacy | U1 | Persistence | Status | Risk |
|---|---|---|---|---|---|
| Reload saved configuration | Native 204 | Native 204 and 206 | Configuration file and backup | Code-complete; source-free CI passed | P0 |
| Save configuration | Native 205 | Native 205 | Same file | Code-complete; P0 suite in passing P0-P13 | P0 |
| Restore render defaults | None | Native 207 | Not stored | Code-complete; local label test | P1 |
| Render scale, FPS, VSync, background | 200-203 | 200-203 | Render settings | Code path only | P1 |
| VRM browse, load, reload, unload | ApplicationUiController | Shell and native 300-303 | Last path | Code-complete; destroyed-UI guards in source-free suite | P0 |
| Layout save and restore | U1 only | WorkspaceLayoutFile v2 | JSON and backup | Verified by P1WorkspaceLayoutValidation in P0-P13 | P0 |
| Tracking start, stop, switch, recover | Tracking controls | TrackingSettings | Preferences | Recover failure is visible; device reconnect blocked | P0 |
| Materials and shaders | Material section | Materials partial | Active model | Code path; current-model guard exists | P1 |
| Appearance and expression | Appearance section | Appearance partials | Current profile | Code path; model change clears selection | P1 |
| Motion and motion cue | Motion section | Motion and MotionCues | Mixer | Code path only | P1 |
| Scene and environment | Environment section | Scenes and Environment | Scene runtime | Code path only | P1 |
| Event rules | Events section | EventRules | Rule file | Code path only | P1 |
| Output transparency and recover | Overlay actions | Native 401-403 and fallback | Overlay settings | Code path; OBS blocked | P1 |
| Diagnostics and log | Diagnostics section | RuntimeLog | Export | Code path only | P1 |
| Shortcuts and focus | Legacy inputs | Feature search | None | Not retested on this SHA | P2 |
| Long Korean text, DPI, reopen | Native and uGUI | Existing layout | None | Not retested on this SHA | P2 |

## Physical acceptance

BLOCKED and requires a person or device:

- Windows and macOS manual GUI
- Korean IME compose, commit, delete, and long text
- Real VRM load and swap
- Real tracking connect and reconnect
- OBS transparent capture
- 720p60, 1080p60, and a 4-hour run through shutdown

Legacy uGUI stays. PR #44 is not ready to merge because repository validation did not pass and physical rows are open.
