# U1 UI parity audit — evidence and remaining acceptance

Repository: playCosmos/VirtualCharacterRender
PR: #44, Draft / unmerged
Baseline before current follow-up: 034a2bdfd3d9ac140cbfcaa786821467b535dc1f (2026-11-14 KST).
The current follow-up modifies Settings confirmation lifecycle and its source-free regression. Recheck the branch HEAD and exact CI run before claiming completion.

## Evidence hierarchy

- Implemented: UI event registration and a runtime-service call exist in source. This is NOT a pass.
- Automated: a test on the same source SHA exercises an actual runtime operation or a documented static contract; distinguish the two in the table.
- Physical: operator-controlled Windows/macOS GUI, Korean IME, real VRM/tracker/OBS and soak results. Never inherit this from hosted CI.
- Blocked: the evidence requires a physical setup or an explicit product decision.

## Source-linked feature parity inventory

| User operation | Legacy entry or action | U1 entry or action | State/storage and evidence | Current result | Risk |
|---|---|---|---|---|---|
| Reload saved runtime config | ApplicationUiController.NativeSettings, 204 → ShowProfileLoadUnavailable → ReloadSavedConfiguration | WorkspaceToolkitShell, 204 → ApplyWorkspaceConfigurationAction(206); fallback Reload config invokes 206 | ApplicationRuntimeBootstrap owns config file, backup and suppression | Implemented; direct reload path confirmed; U1 adds 15-second two-click safeguard not present in legacy | P0 |
| Save current runtime config | Native 205 → SaveConfiguration | Native 205 / fallback Save config | ApplicationRuntimeBootstrap.SaveConfiguration; protected recovery needs second click | Implemented; not a physical durability approval | P0 |
| Render defaults | No legacy native action | Native 207 | Sets scale 1, FPS 60 and VSync off; leaves background execution preference unchanged | U1-only action, intentionally distinct from 204 | P1 |
| Render scale / FPS | Legacy native 200/201 | U1 native 200/201 and fallback Apply | SingleCharacterSceneRuntime; scale 0.5–2, FPS 30–120 | Code paths identified; compare actual readback and validation on both platforms | P1 |
| VSync / background | Legacy 202/203 | U1 native 202/203 and fallback toggles | Runtime render settings and Unity background state | Code paths identified; real background behavior not physically tested | P1 |
| VRM path / load / reload / unload | Legacy native 300–303, ApplicationUiController | U1 native 300–303, WorkspaceToolkitShell character actions | SingleCharacterSceneRuntime and LastSuccessfulPath PlayerPrefs; destroyed-shell continuation guards | Source-free guard regression present; real VRM file/swap blocked | P0 |
| Tracking list / edit / enable / reconnect | Legacy RebuildNativeTrackingSources, ShowNativeTrackingDetails, SaveNativeTrackingDetails | U1 TrackingSettings: RebuildWorkspaceNativeTrackingSources, HandleWorkspaceNativeTrackingAction, RecoverWorkspaceTrackingSource | ITrackingRuntimeConfigurable and per-source PlayerPrefs | Code path identified; no physical disconnect/reconnect acceptance | P0 |
| Material / shader overrides | Legacy material controls | U1 Materials: ApplyWorkspaceMaterialShader/Float, Load/Reload/UnloadWorkspaceShaderPackage | Current model/scene runtime; package files | Code path only; real model/material effect pending | P1 |
| Appearance / expression / saved presets | Legacy appearance controls | U1 Appearance, AppearancePresets, Motion ApplyWorkspaceManualExpression | Scene/model runtime, per-model preset persistence | Code path only; model change and preset rollback unverified in real Player | P1 |
| Motion / Motion Cue | Legacy motion controls | U1 Motion ApplyWorkspacePoseWeight; MotionCues ExecuteWorkspaceCue | Scene motion services and mixer | Code path only; device/model integration unverified | P1 |
| Scene / environment | Legacy scene/environment controls | U1 Scenes ChangeWorkspaceSceneSuspension; Environment ApplyWorkspaceEnvironmentState | Scene runtime | Code path only; live scene result unverified | P1 |
| Event rules | Legacy event controls | U1 EventRules SaveWorkspaceEventRules, ReloadWorkspaceEventRules | JSON event rule file and rule runtime | Code path only; persistence and recovery tests required | P1 |
| Overlay / output / recovery | Legacy 401–403 | U1 native 401–403, fallback and Broadcast panel | Active overlay adapter and scene runtime | Code path only; OBS capture blocked | P1 |
| Diagnostics / log | Legacy diagnostics controls | U1 RuntimeLog / diagnostics paging | Bounded log queue, metrics and export | Existing automated log stress check; verify manual export contents | P1 |
| Workspace dock / tabs / layout | No equivalent OBS dock in legacy | WorkspaceToolkitShell / WorkspaceLayoutFile v2 | Panel-ID JSON, atomic manual+auto backups, v1 positional reader | Actual layout round-trip and corruption tests in P1WorkspaceLayoutValidation | P0 |
| Search, shortcuts, focus | Legacy key/UI actions | U1 FeatureSearch including IME-composition guards | Transient selection and workspace prefs | Code path only; physical IME/focus blocked | P2 |
| Independent Settings window | Legacy DesktopNativeSettingsWindow | U1 DesktopNativeSettingsWindow / non-native fallback | Win32/AppKit bridge, UI Toolkit fallback | Hosted native bridge and managed AppKit P/Invoke automated; physical GUI blocked | P1 |

The inventory maps main feature families and concrete entry points, not every dynamic device-specific setting field. Treat any unexamined device control, error branch, or persisted key as OPEN, not Complete.

## Confirmed corrections and residual risks

1. Layout regression fixed at ec676fad: v1 positions could assign docks to different panels after catalog change. v2 stores panel IDs; unknown newer IDs are ignored, invalid/truncated snapshots rejected. P1WorkspaceLayoutValidation exercises round-trip, backup, legacy read, unknown IDs and corruption.
2. VRM async UI lifecycle fixed at ec676fad: loading/reloading continuation must not update destroyed shell; legacy restored-model refresh is also guarded. Static guard contracts are checked, but physical VRM load/swap is open.
3. Native command parity fixed at b4c53fb: 204 now reloads saved configuration in both legacy and U1; 207 exclusively restores render defaults. 205 saves and 206 remains the internal U1/fallback reload action. Control IDs are window-local and not persisted. U1 reload intentionally demands a second click; this safety difference requires an operator acceptance decision.
4. Subsequent Settings confirmation fix: native window close/reopen and fallback panel toggle must revoke pending Save/Reload confirmation. An unrelated native action or fallback Apply/Status and switching Save ↔ Reload must also revoke it. The duplicate visible native Reload button 206 was removed; handler 206 remains for U1 fallback. P1WorkspaceLayoutValidation includes source-call-site checks plus a runtime reset-field invocation. Do not mark the feature Verified until source-matched Unity CI passes.
5. Remaining Settings risk: a change of focus elsewhere within an open fallback panel might not generate an action that cancels the armed 15-second confirmation. Physical operator behavior and a dedicated UI interaction regression remain open.
6. The exact fields, ranges, disabled states, error messages and recovery actions of dynamic Tracking Settings still need per-control comparison between legacy and U1. Device-side acceptance blocked.

## Source-matched CI ledger

| SHA / workflow | Evidence | Outcome |
|---|---|---|
| b4c53fb / Unity P0–P13 #43447066129 | Editor source-free validation | PASS |
| b4c53fb / macOS P1 #43447066150 | AppKit arm64 and managed Unity Player native smoke | PASS |
| b4c53fb / repository #43447076442 and #43447066213 | macOS launcher job cancelled before runner assignment | NOT PASS; cancellation is not an assertion failure |
| 034a2bd / repository #43519155074 and #43519145789 | Push and PR repository validation | PASS |
| Current Settings follow-up HEAD | Run current Repository validation, P0–P13, Windows Player and macOS as applicable | PENDING until exact run results are checked |

Other earlier CI successes remain valid for their own SHA only. Never claim a previous source revision proves the current fix.

## Same-ID tracking runtime replacement regression

Source review found that `RestoreWorkspaceTrackingPreferences` previously stored only a restored `ControlId` in a `HashSet<string>`. Destroying the first provider and reconnecting another MonoBehaviour with the same ID left the restored flag set; persisted per-device settings and enabled state could be skipped on the replacement.

- `f0f89b3`: record the exact `ITrackingRuntimeControl` instance alongside its ID, replace the entry on reconnection, and retain the existing retry-on-failed-apply behavior. All manual add, toggle and save actions also mark the current provider instance.
- `01e6bd0`, strengthened by `645f0c6`: P1 source-free regression creates an actual Unity tracking provider implementing both runtime control and runtime configuration, restores a persisted configuration field and enabled state, verifies same-instance no-op on the next scan, destroys that provider, creates a replacement with the same ID, and verifies both persisted settings and enabled state are applied again exactly once. The test deletes its uniquely named temporary PlayerPrefs keys.
- **Verification status:** implementation and Unity fixture committed; source-matched P0–P13 and Windows/macOS acceptance are **PENDING** until the new HEAD CI completes. This is not yet physical camera/tracker reconnection approval.
- Physical disconnect/reconnect with the same component (rather than a replaced component) still depends on each provider's own recovery policy and remains an acceptance item.

## Tracking editor draft vs provider replacement

A separate real-world reconnect risk was found after the restored-instance fix: an operator can open a device's native Settings or UI Toolkit editor, then the device component can be destroyed and replaced by another provider with the same logical ID **while unsaved editor fields remain visible**. Before this fix, both Save handlers rediscovered the current provider by ID and could apply the old provider's draft to the replacement.

- `8af058a`: native Settings now remembers the provider instance that created the editor fields, clears that reference when the device list is rebuilt, and refuses Save if the instance changed. It rebuilds the list and requests that the operator reopen Settings. The UI Toolkit Save closure similarly compares its captured provider with the live current provider, discards the stale editor and reports the issue.
- `2f81270`: the P1 Unity regression verifies that a provider matches itself, rejects a null target and a same-ID replacement, and requires both native and UI Toolkit submission paths to call the identity guard. This uses actual Unity provider fixtures, but **not** native window click simulation.
- This is a protection against applying **stale unsaved drafts to a different runtime instance**. It does not verify that a provider's own recovery implementation preserves live settings on a disconnect that keeps the same component.
- Exact-source CI on these changes and physical device/camera detach–reconnect remain required. Keep status **PENDING** until CI completes.

## Tracking restore cache lifecycle and transient failures

The prior restored-instance dictionary correctly replays persisted values on a same-ID replacement, but it strongly held disconnected Unity providers until the shell closed. With many unique hot-plug devices this could retain destroyed component references unnecessarily.

- `9ecdce8`: the 2-second restore scan now removes entries for missing, ambiguous or unconfigured source IDs from the restored-instance cache. It **does not remove** the user's configured IDs, saved setting fields or enabled preferences, so reattachment still restores the saved values.
- `18723f9`: the real Unity provider fixture asserts a disconnected source is released from the cache while its preference remains, before reattaching a same-ID source.
- `72e9ed2`: extend the fixture with a temporary `TryApplyConfiguration` failure and a separate temporary `TrySetControlEnabled` failure. A failed attempt must leave the source unmarked, not overwrite persisted values, and a later scan must reapply settings and enabled state successfully. Expected warnings from intentionally simulated temporary failure are test input, not physical-device proof.
- **Current exact-HEAD CI:** Pending until new branch validation runs finish. The previous `bee92b5` Unity/Windows suite passed, but its result cannot validate this new change.

This fixture covers an editor-controlled provider object with simulated failures. Real camera/tracker disconnect, restart side effects, driver hangs, delayed callbacks and a live UI session remain blocked on physical acceptance.

## Priority next work

- P0: exact-HEAD Unity Editor CI for confirmation lifecycle; investigate failures rather than weakening checks.
- P0: real first/second-click behavior in native and fallback Settings, including close/reopen within 15 seconds, mixing Save/Reload, error/recovery and pending Inspector drafts.
- P0: physical VRM swap and Tracking stop/reconnect, with output state correctness.
- P1: complete field-by-field native Win32/AppKit/fallback comparison, including tracking dynamic fields and disabled/readback states.
- P1: actual material, appearance, motion, scene, event-rule and output persistence/effect tests.
- P2: keyboard/IME/DPI/focus, OBS transparent capture at 720p60 and 1080p60, four-hour soak and proper quit.

## Physical acceptance status

BLOCKED — Requires Physical Acceptance: Windows/macOS hands-on GUI; Korean IME compose/commit/delete/long text; actual VRM load/swap; camera/tracker connect, disconnect and reconnect; OBS transparent capture; 720p60/1080p60 measurements; four-hour soak and native quit.

Legacy uGUI stays until functionality, failure paths, persistence and physical acceptance are demonstrated. PR #44 stays Draft; do not merge, tag or release.
