# U1 UI parity audit

Remote HEAD at audit update: ec676fad227c5303f256d0f72dd22bb426e53072. PR #44 remains Draft.
CI on that SHA: Repository validation success (push and pull_request), Unity P0-P13 source-free validation success. macOS P1 Player build was still in progress and is not claimed here.

## Action 204

Native control IDs are not persisted. Win32 and AppKit return the ID registered by C# for the lifetime of the window. Reusing 204 for a different command would make the legacy button and the U1 button disagree.

Decision: keep 204 as reload saved configuration on both UIs. U1 render-default restore moves to new action 207. U1 205 remains save and 206 remains reload. Action 204 in U1 calls the same confirmed reload path as 206, so the overwrite confirmation stays. Legacy 204 still calls ReloadSavedConfiguration.

P1WorkspaceLayoutValidation checks the labels and both registrations. Local Unity 6000.3.25f1 RunSourceFreeAndExit passed after the change. Log: .tmp/u1-p1-action204.log.

## Already verified on ec676fad

Layout schema v2 stores panel IDs. Version 1 stays positional and rejects arrays longer than the current catalog. Character load, reload, and last-model restore do not update a destroyed controller. Local source-free batches passed before that commit. GitHub Actions on ec676fad passed repository validation and Unity P0-P13.

## Status rules

A row is code-complete only when the U1 control calls the same runtime service. It is verified only when a test or CI run on the current SHA exercises that behavior. Physical rows stay blocked.

| Feature | Legacy | U1 | Persistence | Status | Evidence |
|---|---|---|---|---|---|
| Reload saved configuration | Native action 204 | Native actions 204 and 206 | Runtime configuration file and backup | Code-complete, local test | Action label test in source-free batch |
| Save configuration | Native action 205 | Native action 205 | Same file | Code-complete, P0 suite | P0 persistence suite inside P0-P13 CI |
| Restore render defaults | No legacy native action | Native action 207 | Not stored | Code-complete, local test | Label and dispatch test |
| Render scale, FPS, VSync, background | 200-203 | 200-203 | Render settings in configuration | Code-complete, not separately retested on this change | Existing shell dispatch |
| VRM browse, load, reload, unload | ApplicationUiController | WorkspaceToolkitShell and native 300-303 | Last successful path | Code-complete, lifecycle test | Destroyed-controller guards in source-free batch |
| Layout save and restore | U1 only | WorkspaceLayoutFile v2 | workspace-layout JSON and backup | Verified | P1WorkspaceLayoutValidation and CI |
| Tracking recover | TryRecover with visible failure | Same service | Tracking preferences | Code-complete, no device test | Failure text is not swallowed |
| Output transparency and recover | Overlay actions | Native 401-403 and fallback | Overlay settings | Code-complete, OBS blocked | Same scene runtime |
| Materials, appearance, motion, events, environment | ApplicationUiController sections | WorkspaceToolkitShell partials | Existing service files | Code path only | No new end-to-end test on this SHA |

## Still blocked

Windows and macOS physical GUI, Korean IME, a real VRM swap, a real tracking device, OBS capture, 720p60, 1080p60, and a 4-hour run. Legacy uGUI stays. PR #44 stays Draft and unmerged.
