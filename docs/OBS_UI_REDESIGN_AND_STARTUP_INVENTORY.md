# OBS-style UI/UX rebuild and startup inventory

Status: **DESIGN BASELINE / IMPLEMENTATION STAGED** (2026-10-26)
Source reviewed: `main@a7d3ab24085567259f89194ef7789b943a3f06e9`.
Scope: Windows x64 and Apple Silicon macOS. No release or milestone promotion is implied.

## Verified current behavior

- `ApplicationUiController.TryRestoreLastCharacter()` uses `PlayerPrefs["VCR.Character.LastSuccessfulPath.v1"]`. A successful **UI** VRM load saves the path immediately; explicit unload deletes the key. Restoration is attempted asynchronously after the scene runtime is available. It is **not** an end-of-session model-list snapshot; a CLI `--vcr-vrm` startup path does not flow through the same preference update.
- `ApplicationRuntimeBootstrap` persists `SceneRuntimeConfiguration` (render/camera/light/overlay/environment) but **not** a model path or model-library inventory. `character='<none>'` in its early startup line means `CurrentCharacterPath == null` at that instant, not necessarily no visible model after later asynchronous restore.
- `Vrm10CharacterLoader` supports VRM 0.x/1.0 via UniVRM; **no production FBX import/restore path is present**. The legacy scene's `Character` GameObject is a parent/root and does not prove a loaded VRM model.
- `CapabilityRegistry` represents registered/activated service factories; `DeclarativeCapabilityCatalog` represents validated metadata-only package providers. **Arbitrary executable third-party plugins remain unsupported**. `RuntimeShaderRegistry` tracks explicitly registered shaders; built-in shaders are not necessarily enumerated there.
- Existing separate Win32/AppKit Settings implementation and UI panels are provisional. Real GUI/OBS/physical device acceptance remains open in #18/#30/#31/#32.

## UX concept (OBS-like, not code or imagery copied from OBS)

The new application workspace is a resizable desktop main window:

- **Top:** menu bar, project/profile, active scene and render/device state, visibility of workspace panels.
- **Center:** preview/program area (character and environment), viewport tools, transparent preview mode, output/capture state. Preview is **not** the capture transport itself.
- **Bottom docks:** Scenes/Profiles, Sources (VRM models, future FBX adapters, trackers, overlays), Audio/Motion status, Scene Controls, Log/Events.
- **Side docks:** properties/inspector and optional materials/shaders, extensions, animation/expression controls. Reorder/dock/undock, restore defaults, persist panel layouts per profile.
- **Settings:** an independent, standard operating-system window with a sidebar and detail pages. Native title-bar drag, keyboard/focus/resize/DPI/Retina and Alt+Tab/Command+Tab are acceptance requirements. Never implement hover-to-auto-hide or a user `Always on top` setting. Avoid a new heavyweight UI framework.
- **Log dock:** filter by category/severity, copy/export with a privacy warning, chronological startup events, runtime module transitions, shader/package/model load outcomes and recoverable failures. Logging must work even with docks hidden or main window minimized; the UI consumes logging but does not own it.
- **Palette:** consistent OKLCH-based neutral surfaces and semantic states; accessible keyboard focus, scalable labels, and high-DPI typography. UI overhaul is a separate implementation track; **do not conflate plan or logging foundation with finished UX**.

Functional boundaries: do not rewrite VRM import, tracking, shader compilation, config persistence or output backends only for UI work. Bind panels to existing runtime/controller APIs where safe; consolidate redundant UI actions as needed. Preserve offline operation and original model files.

## Model state and future model-library contract

The single-active-character runtime remains the source of truth. Distinguish:

1. `model.available`: validated entry in a user-selected library/catalog; not necessarily in memory.
2. `model.remembered`: last successful session load reference, safe to offer as a candidate.
3. `model.loading / model.loaded / model.failed / model.unloaded`: actual runtime transitions.
4. `model.restored`: successful **automatic** load of the remembered model after all startup prerequisites are ready.

**Restore policy:**
- Preserve the existing VRM restoration during UI replacement. Move ownership out of `ApplicationUiController` into a bounded model-session service or application bootstrap without changing the existing preference's interpretation during migration.
- Remember a successful manual load and explicit unload immediately; never persist an attempted or failed path as successful. Closing with no active model must not create a phantom restore. The remembered format is VRM for existing settings.
- When a remembered file is missing, unsupported or corrupt, do not block startup or silently replace it; report one actionable warning, keep old failure evidence, and offer browse/remove/skip. Respect explicit `--vcr-vrm` precedence.
- Do not enumerate arbitrary file system locations or upload personal model filenames. Model library scans are opt-in and bounded. Log basename/type/status by default; paths only in private diagnostic context.
- **FBX:** add a format adapter and verified load/unload/validation first. Only after FBX actually loads through a supported runtime adapter should it be eligible for last-model restoration. Do not treat a Unity Editor FBX import or a `.fbx` string as working runtime support.
- Reserve a versioned session/model reference record for format, source identity, active/remembered state, load outcome, migration/rollback. Preserve current config corruption/backup behavior; never overwrite unreadable data on automatic shutdown.

## Startup/loading logs — immediate foundation

Implement a low-impact, deterministic inventory in the existing Unity Player log. A future OBS-style log dock presents the same events; no second source of truth.

- On successful core startup emit application/scene module state, Unity/OS/graphics pipeline identity, registration/activation counts and IDs for capabilities, declarative package provider IDs, and registered **runtime** shader IDs.
- Report `executable_plugin=unsupported`, not a fabricated loaded plugin list. Distinguish `registered` from `loaded` from `enabled`; never claim a shader is runtime-bound merely because it is registered.
- At startup log active model state and remembered-model basename/availability separately, with the **phase** explicitly marked `pre-ui-restore`. Subsequent actual VRM load/unload and restore outcomes produce their own logs. Do not use `character='<none>'` alone as no-character proof.
- Successful platform-specific shader bundle/package loads emit the actual shader IDs/package identity once; failures retain the existing error path. Do not log every frame or enumerate every Unity assembly/material/shader or recursively scan all disks.
- Prefix entries `VCR inventory:` with a clear `stage=` / `category=` / `status=`, keep default local-only Player.log behavior and exclude credentials/tokens, raw file contents and full model paths from new inventory lines. Bound output by already-validated package/catalog limits.
- Log instrumentation must not change load order, force-init optional services, perform IO during shutdown, increase steady-state per-frame cost, or delay startup if logging fails.

## Incremental delivery / acceptance

| Stage | Deliverable | Gate |
| --- | --- | --- |
| L0 | Verify VRM preference/restoration, document FBX gap, agree OBS-style concept | Source proof and no false claim of absent restore |
| L1 | Startup module/provider/shader/model inventory + real load events | Native Player startup evidence and source-free tests; log truthfulness |
| U0 | Workspace wireframes, panel contracts/commands, layout save/restore and accessibility spec | User-approved functional scope and Windows/macOS design review |
| U1 | New OBS-like shell with existing runtime bindings and dedicated OS Settings | Real title bar, drag/dock/resize/DPI, render while Settings is closed |
| U2 | Migrate VRM last-model persistence to UI-independent session controller; model library, conditional FBX after adapter exists | Successful-load only, explicit unload, missing/corrupt file, CLI precedence, clean profile and rollback |
| U3 | Log dock, optional source transitions, output/OBS and full desktop workflows | Real hardware/OBS UI evidence and no startup/logging regressions |

Follow one coherent commit/PR per package; do not change production tags/releases or report M1–M3 accepted from headless CI. Source test is not real desktop interaction evidence.
