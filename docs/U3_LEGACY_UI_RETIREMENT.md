# U3 — isolate Toolkit-only P1 and inventory legacy uGUI retirement

**Stacked baseline:** U2 Draft PR #45 (`951220a8eda23468f420a7fd35adf431982af2af`), itself based on U1 Draft PR #44. Physical hardware acceptance was **assumed by the operator for development sequencing**; no real-device evidence has been fabricated. Both previous PRs remain Draft/unmerged. This first U3 change does **not** claim the global legacy application controller has been removed.

## Runtime separation implemented

- P1 scene upgrade/build now validates the **actual loaded scene before any migration mutations**. It rejects an enabled **or inactive** `VCR.Runtime.UI.ApplicationUiController` and any missing `MonoBehaviour` scripts, including uninstantiated scene objects. The build path already calls `EnsureWorkspaceInExistingScene()`, so the guard applies to standalone Windows and macOS P1 Player generation as well as Editor upgrades. The historical Alpha scene is not modified by this check.
- The Toolkit appearance-preset restore and event-rule initial restore no longer call `FindFirstObjectByType<ApplicationUiController>()` or branch on the legacy UI's presence. This removes two genuine runtime cross-UI dependencies. P1 must have only one UI owner, so the shell is responsible for restoring each saved file **once per runtime/provider identity** and must preserve unsaved runtime changes on shell re-enable/dock rebuild.
- This does not automatically delete, disable or replace an authored legacy scene: a mixed scene is rejected with a specific diagnostic, leaving it untouched for manual migration. It must be inspected and saved explicitly before a Toolkit-only build.

## Machine-readable inventory and CI protection

`tools/audit-u3-legacy-ui.py --self-test --strict-p1` scans source files, `ApplicationUiController.cs.meta`'s GUID and tracked serialized scene/prefab/asset references. It verifies that P1 keeps its legacy-UI and missing-script rejection, that the P1 builder does not instantiate the old controller, and that **no** `WorkspaceToolkitShell*.cs` file references the legacy UI class. The JSON inventory in `Builds/Validation/u3-legacy-ui-inventory.json` is uploaded as a Repository validation CI artifact.

The script intentionally distinguishes **no legacy UI in generated P1** from **no legacy UI anywhere in the repository**. Generated P1/Alpha scenes are normally not tracked in Git: the *Editor scene check* must verify actual compiled/generated GameObjects; static file scanning alone cannot prove they are clean. A reported global retirement blocker does **not** fail CI until the historical Alpha line and its tests are deliberately migrated. A new direct runtime shell dependency or missing P1 guard **does** fail CI.

## Global deletion blockers — separate coherent retirement change

1. `unity/Assets/VCR/Editor/Alpha/AlphaApplicationSceneBuilder.cs` still attaches `ApplicationUiController` to the historical Alpha player. Existing P0–P13 and release baselines depend on that scene path; deleting the controller without migrating or formally retiring those validations produces compile/build failures.
2. `unity/Assets/VCR/Runtime/UI/ApplicationUiController.cs`, `ApplicationUiController.NativeSettings.cs` and `ApplicationUiController.MockupDashboard.cs`, along with their `.meta` files, remain checked in. They must be removed with their final references **only after** a complete Alpha/editor/serialization migration, not by removing Unity's built-in UI packages indiscriminately.
3. Repository and Editor regression contracts still refer to old UI code, including `tools/validate-repository-structure.py`'s native Settings check and Alpha scene builder constraint; the P1 workspace validator also reads old native Settings source. Migrate these to the independent Toolkit/native Settings tests before deleting the old implementation. The audit records remaining source locations, not just a hardcoded list.
4. Any authored legacy Canvas/EventSystem bindings and missing MonoBehaviour GUID references in migrated scenes must be removed explicitly, preserving unrelated Unity UI module users in other tools and editors. Retiring the application uGUI is **not** equivalent to removing all `UnityEngine.UI` assemblies or packages.

**Next retirement batch:** move the remaining historical Alpha UI behavior/tests to Toolkit-backed or UI-independent fixtures; update the Alpha scene builder and repository assertions together; delete controller partials and their `.meta` files in the same change; verify GUID/reference absence and build all original P0–P13 plus generated P1 Windows/macOS Players; exercise native Settings and quit paths. Then and only then change the global retirement status to complete. Exact-source Unity CI, hosted native smoke and source audits are distinct from physical hardware/operator acceptance.

No release, tag, merge, or automatic upgrade of historical Alpha scenes is authorized by this U3 separation change.

## U3.2 — historical scene migration / retired uGUI (validation pending)

Alpha and P11 scene generators now instantiate a single `UIDocument`
+ `WorkspaceToolkitShell` with project-owned persistent `PanelSettings`.
Generated Alpha/P11 scenes switch to opaque framed desktop output as required
for a clickable interactive Toolkit workspace. Three retired application
controller partial sources and their matching Unity `.meta` files are
deleted in the same commit, rather than leaving missing C# references.

P11 application model/policy checks remain. P1 Workspace behavioral
regressions no longer open deleted controller source files. Repository
validation replaces 110 micro-allocation assertions that only made sense
for the removed Canvas renderer with explicit Toolkit/native Settings
contracts; P0–P13 validation and exact-source hosted Player checks must
still execute. The U3 auditor permanently preserves the retired MonoScript
GUID, checks authored serialized files, and fails CI using `--strict-global`.

**Limitations:** existing user-authored or untracked Alpha/P11 scenes are
not auto-migrated. They require an explicit scene rebuild/manual migration
and missing-script verification. Source edits and hosted CI are not
hardware/operator acceptance. PRs #44/#45/#46 remain Draft; no merge,
release or tag is authorized.

## U3.3 — exact-source generated scene acceptance gate

The U3 branch's existing Unity workspace-generation pass now runs
`VCR.Editor.Alpha.U3ToolkitSceneValidation.GenerateAndValidateAllScenesAndExit`
instead of creating only P1. It refuses to overwrite any pre-existing P1,
P11, or Alpha scene/PanelSettings, generates each from the actual Editor
builders, then inspects the saved scene graph for a single active Toolkit
shell, a persistent matching `UIDocument`/`PanelSettings`, missing
MonoBehaviour scripts, bootstrap/output ownership, and opaque framed
desktop defaults. P1 is reloaded after the P11/Alpha generation to catch
scene-order side effects before the normal P1 Windows Player build.

Repository-level retirement audit now scans tracked/untracked Unity text
serialization under all `unity/Assets` for the retained retired MonoScript
GUID, detects any orphaned retired partial `.meta`, checks the U3 Editor
generation gate is present, and fails global retirement acceptance when
a required generator or CI contract disappears.

Passing generated-Editor-scene validation is **not** proof of actual Alpha/P11
Player execution, macOS native UI acceptance, authored-scene migration, or
hardware approval. Generated fixture scenes are used only in CI; the
operator's existing scenes are not changed by this gate.

## U3.4 — read-only migration acceptance of existing Alpha/P11 scenes

Unity Editor menu **VCR → U3 → Validate Existing Alpha and P11 Scenes
(Read Only)** and batch method
`VCR.Editor.Alpha.U3ToolkitSceneValidation.ValidateExistingAlphaAndP11ScenesAndExit`
inspect the *existing* authored assets, without invoking a generator or
saving the scene. They require both scene assets and their persistent
PanelSettings, reject unsaved/dirty loaded scenes, missing scripts including
inactive objects, the retired `ApplicationUiController` MonoScript GUID,
duplicate Toolkit shell/`UIDocument` ownership, incorrect or inactive
PanelSettings, missing bootstrap/output, and unwanted transparent or
click-through window defaults. SHA-256 of the two scenes and two PanelSettings
is compared before and after to detect accidental writes. A failed
inspection leaves the scenes intact. It **does not** silently detach or
delete missing scripts, recreate scenes, or convert Canvas components.

For manual migration: back up the authored scenes and external dependencies;
remove or replace the retired UI and its bindings explicitly; attach one
active `WorkspaceToolkitShell` and `UIDocument` using the expected
persistent panel asset; ensure an opaque framed output; save intentionally
in Unity; then run the read-only acceptance. Canvas/EventSystem use by
other subsystems is not globally deleted. Existing P1 has its separate
upgrade/validation gate.

This acceptance only checks Unity Editor serialization and ownership, not
per-pixel/UI interaction equivalence, hardware operation or Alpha/P11
Player startup. A successful U3.3 generated-scene test is not a substitute
for the operator validating their actual authored scene assets.

## U3.5 — actual Alpha/P11 Windows Player native smoke

The isolated U3-only workflow `.github/workflows/u3-alpha-p11-windows-native.yml`
builds **two separate** development Windows Mono Players, directly from
the real generated Alpha and P11 Toolkit scenes. It refuses any existing
scene/panel input in the clean batch workspace; the native Player build
requires the same read-only acceptance used for authored scenes.
`P11ApplicationSceneBuilder` now keeps the ordinary framed window,
starts MediaPipe webcam and diagnostics logging disabled (consistent with
interactive P1/Alpha defaults), and attaches an **opt-in-only**
`PlayerStartupSmoke` component. A normal P11 launch does not invoke the
test or automatically quit. Both Players use the pinned source commit and
separate ZIP archives with SHA-256 + source identity sidecars. The native
Windows job runs the existing complete startup/relaunch/config-recovery/
normal-shutdown suite **for both** Players, not the P1 binary as a proxy.

This workflow is separate from the existing P0–P13/P1 CI job to preserve
its bounded build time and avoid claiming U3 Alpha/P11 native validation
when only scene generation succeeded. It is diagnostic development Mono
evidence, **not** Windows IL2CPP release, macOS Alpha/P11, GUI pixel
acceptance, or physical operator/hardware acceptance. PRs remain Draft;
no merge, tag, or release is authorized.

## U3.6 — real Alpha/P11 Windows GUI smoke (hosted, not operator approval)

The U3 Windows workflow now has two independent native Windows GUI
observations (Alpha and P11), gated behind the real standalone smoke. It
reuses the P1 Win32 observation/Unity screenshot helpers while **not**
assuming the P1-only feature-search/docking interactions exist on all
historical scenes. Each separate source-hashed Player is verified against
its archive digest and extracted after unsafe ZIP path/link checks.
The visible, non-batchmode Player must show a real Unity HWND with an OS
title bar and freely resizable frame, native mouse hit targets, measured
compact UI Toolkit controls, an opaque desktop configuration, a diverse
Unity-generated frame, no UI exceptions, and actual managed WM_CLOSE
shutdown. Separate JSON reports, native logs and images are uploaded.

A runner with no GUI records `windowObserved=false`; this **does not**
count as a GUI observation, even if the Actions job is green. A runner that
**does** expose a Unity HWND must satisfy every measured invariant or fail
the job. This remains hosted automation, not user-authored scene parity,
native Settings interactive acceptance, tracking hardware, or Windows
production IL2CPP certification. PR #46 stays Draft.

## U3.7 — independent native macOS Alpha/P11 Player integration

The U3-only source-bound workflow
`.github/workflows/u3-alpha-p11-macos-native.yml` first compiles
the arm64 AppKit Settings bridge on a real macOS runner. The existing
strict source/sha verifier then restores that binary and its exact Unity
importer to a clean pinned Unity 6000.3.25f1 project. The generalized
`U3AlphaP11PlayerBuild.BuildMacMonoAndExit` refuses to overwrite
existing authored scenes, generates and read-only validates both
independent Toolkit scenes, and creates two **non-development** macOS
Mono applications, each with its own native executable and AppKit dylib.
The original IL2CPP project baseline is restored and SHA-verified.

Alpha and P11 macOS bundles are packaged into separate symlink-aware
ZIP archives, each containing exact source and SHA-256 sidecars.
Only a complete same-run source-verified pair may be consumed by
the real `macos-15` test. Both Players execute the established
startup/relaunch/recovery/normal-shutdown suite, and every one of
the eight reports per variant must confirm managed-to-native AppKit
P/Invoke, nondevelopment build and normal runtime termination.

This is a CI integration test, not signed/notarized production
distribution, physical device/OBS/VRM tests, GUI pointer acceptance,
or authored-scene migration acceptance. All PRs remain Draft.
