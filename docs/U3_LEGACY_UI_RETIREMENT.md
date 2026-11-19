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
