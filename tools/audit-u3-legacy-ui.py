#!/usr/bin/env python3
"""U3 pre-retirement audit: verify P1 Toolkit-only ownership without
mislabeling historical Alpha/editor uGUI sources as already removed.

Run: python3 tools/audit-u3-legacy-ui.py --strict-p1
Only the named P1 contracts are enforced; remaining migration blockers are
reported as evidence rather than silently deleting historical code.
"""
import argparse
import json
import re
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LEGACY = "ApplicationUiController"
LEGACY_FULL = "VCR.Runtime.UI.ApplicationUiController"
BUILDER = "unity/Assets/VCR/Editor/P1/P1ApplicationSceneBuilder.cs"
ALPHA = "unity/Assets/VCR/Editor/Alpha/AlphaApplicationSceneBuilder.cs"
META = "unity/Assets/VCR/Runtime/UI/ApplicationUiController.cs.meta"
RETIRED_GUID = "0a9969a7685c4bf9a390aef29d61a66f"
P11 = "unity/Assets/VCR/Editor/P11/P11ApplicationSceneBuilder.cs"
U3_VALIDATOR = "unity/Assets/VCR/Editor/Alpha/U3ToolkitSceneValidation.cs"
UNITY_CI = ".github/workflows/unity-source-free.yml"
U3_PLAYER = "unity/Assets/VCR/Editor/Alpha/U3AlphaP11PlayerBuild.cs"
U3_PLAYER_CI = ".github/workflows/u3-alpha-p11-windows-native.yml"
U3_GUI = "tools/probe-u3-windows-gui.py"
U3_MAC_CI = ".github/workflows/u3-alpha-p11-macos-native.yml"
U3_IL2CPP_CI = ".github/workflows/u3-alpha-p11-windows-il2cpp.yml"
U3_IL2CPP_CHECK = "tools/verify-u3-il2cpp-payload.py"
UNITY_SERIALIZED_SUFFIXES = (
    ".unity", ".prefab", ".asset", ".controller", ".overridecontroller",
    ".anim", ".mat", ".playable", ".mask", ".rendertexture",
    ".lighting", ".meta",
)
SOURCE = Path("unity/Assets/VCR")


def read(root, path):
    full = root / path
    return full.read_text(encoding="utf-8") if full.is_file() else ""


def scan(root):
    builder = read(root, BUILDER)
    ui_root = root / SOURCE / "Runtime/UI"
    shells = sorted(ui_root.glob("WorkspaceToolkitShell*.cs")) if ui_root.is_dir() else []
    shell_legacy = [
        str(path.relative_to(root)).replace("\\", "/")
        for path in shells if LEGACY in path.read_text(encoding="utf-8")
    ]
    gate_contract = all(token in builder for token in (
        "ValidateToolkitOnlyScene(scene)",
        "GetMonoBehavioursWithMissingScriptCount(candidate)",
        "GetComponentsInChildren<Transform>(true)",
        LEGACY_FULL,
    ))
    legacy_constructor = re.search(
        r"AddComponent\s*<\s*ApplicationUiController\s*>", builder) is not None

    scripts = sorted((root / SOURCE).rglob("*.cs")) if (root / SOURCE).is_dir() else []
    references = []
    owners = []
    for path in scripts:
        rel = str(path.relative_to(root)).replace("\\", "/")
        content = path.read_text(encoding="utf-8")
        if path.name.startswith(LEGACY) and path.parent.name == "UI":
            owners.append(rel)
        if LEGACY not in content:
            continue
        lines = [i for i, line in enumerate(content.splitlines(), 1)
                 if LEGACY in line]
        references.append({"path": rel, "lines": lines[:30]})

    meta = read(root, META)
    match = re.search(r"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$", meta)
    # Keep the old MonoScript GUID after .meta deletion: a stale scene must
    # not evade the inventory simply because its source no longer exists.
    legacy_guid = RETIRED_GUID
    bad_meta_guid = match is not None and match.group(1).lower() != RETIRED_GUID
    serialized = []
    if legacy_guid:
        assets = root / "unity/Assets"
        if assets.is_dir():
            for path in sorted(assets.rglob("*")):
                if (not path.is_file() or
                        path.suffix.lower() not in UNITY_SERIALIZED_SUFFIXES):
                    continue
                if legacy_guid in path.read_text(encoding="utf-8", errors="replace").lower():
                    serialized.append(str(path.relative_to(root)).replace("\\", "/"))

    alpha = read(root, ALPHA)
    p11 = read(root, P11)
    alpha_entrypoint = re.search(
        r"AddComponent\s*<\s*ApplicationUiController\s*>", alpha) is not None
    p11_entrypoint = re.search(
        r"AddComponent\s*<\s*ApplicationUiController\s*>", p11) is not None

    # Explicit blockers are not errors in a staged migration. The P0-P13
    # historical Alpha path and editor validation must be migrated separately.
    blockers = []
    if alpha_entrypoint:
        blockers.append({"kind": "alpha-scene-entrypoint", "path": ALPHA})
    if p11_entrypoint:
        blockers.append({"kind": "p11-scene-entrypoint", "path": P11})
    for path in sorted(ui_root.glob("ApplicationUiController*")):
        if path.is_file():
            blockers.append({
                "kind": "legacy-source-or-meta-retained",
                "path": str(path.relative_to(root)).replace("\\", "/")
            })
    if bad_meta_guid:
        blockers.append({"kind": "unexpected-legacy-meta-guid", "path": META})
    for scene_path, scene_text in ((ALPHA, alpha), (P11, p11)):
        if "workspace.AddComponent<WorkspaceToolkitShell>();" not in scene_text:
            blockers.append({"kind": "missing-toolkit-scene", "path": scene_path})
    generation = read(root, U3_VALIDATOR)
    ci = read(root, UNITY_CI)
    for path, content, required in (
        (U3_VALIDATOR, generation, (
            "P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene()",
            "P11ApplicationSceneBuilder.CreateApplicationUiRuntimeScene()",
            "AlphaApplicationSceneBuilder.CreateAlphaRuntimeScene()",
            "GetMonoBehavioursWithMissingScriptCount(go)",
            "document.panelSettings",
            "scene.isDirty",
            "ValidateExistingAlphaAndP11ScenesFromMenu",
            "ValidateExistingAlphaAndP11ScenesAndExit",
            "retired application UI MonoScript GUID remains",
            "documentCount != 1",
            "HashFile(paths[i])",
            "if (!ValidateExistingAlphaAndP11Scenes())"
        )),
        (UNITY_CI, ci, (
            "U3ToolkitSceneValidation.GenerateAndValidateAllScenesAndExit",
            "Require generated U3 Alpha and P11 Toolkit scenes",
        )),
        (U3_PLAYER, read(root, U3_PLAYER), (
            "BuildWindowsMonoAndExit",
            "ValidateExistingAlphaAndP11Scenes()",
            "BuildPipeline.BuildPlayer(",
            "P11ApplicationSceneBuilder.ScenePath",
            "AlphaApplicationSceneBuilder.ScenePath",
            "BuildMacMonoAndExit",
            "BuildWindowsIl2CppAndExit",
        )),
        (U3_PLAYER_CI, read(root, U3_PLAYER_CI), (
            "U3AlphaP11PlayerBuild.BuildWindowsMonoAndExit",
            "run-player-smoke.py",
            "u3-player-artifacts/Alpha/windows.zip",
            "u3-player-artifacts/P11/windows.zip",
            "tools/probe-u3-windows-gui.py",
            "nativeFramePassed",
            "workspaceUiTreeReady",
            "managedQuitObserved",
        )),
        (U3_IL2CPP_CI, read(root, U3_IL2CPP_CI), (
            "U3AlphaP11PlayerBuild.BuildWindowsIl2CppAndExit",
            "windows-6000.3.25f1-windows-il2cpp-3",
            "GameAssembly.dll",
            "global-metadata.dat",
            "--platform windows-nodebug",
            "tools/probe-u3-windows-gui.py",
            "for variant in Alpha P11; do",
            'u3-il2cpp-artifacts/$variant/windows.zip',
        )),
        (U3_IL2CPP_CHECK, read(root, U3_IL2CPP_CHECK), (
            "GameAssembly.dll", "windows.payload-sha256.json",
            "archive_sha256", "zipfile.open(entry",
        )),
        (U3_MAC_CI, read(root, U3_MAC_CI), (
            "U3AlphaP11PlayerBuild.BuildMacMonoAndExit",
            "select-p1-macos-appkit-artifact.py",
            "u3-macos-player-artifacts/Alpha/macos.zip",
            "u3-macos-player-artifacts/P11/macos.zip",
            "VCR_SMOKE_NATIVE_SETTINGS: '1'",
            "--platform macos-nodebug",
            "nativeSettingsInteropPassed",
        )),
        (U3_GUI, read(root, U3_GUI), (
            "validate_player_archive(archive)",
            "gui.visible_player_windows(process.pid)",
            "gui.WORKSPACE_LAYOUT_MARKER",
            "gui.probe_free_resize(",
            "gui.QUIT_MARKER",
            "u3-gui-probe.json",
        )),
    ):
        for token in required:
            if token not in content:
                blockers.append({
                    "kind": "missing-generated-scene-verification",
                    "path": path, "required": token
                })
    for path in owners:
        blockers.append({"kind": "legacy-controller-source", "path": path})
    for path in serialized:
        blockers.append({"kind": "tracked-serialized-legacy-guid", "path": path})
    # Includes editor source contracts; they must be rewritten or retired
    # before deleting the legacy type, not suppressed by an empty P1 scene.
    for reference in references:
        if reference["path"] not in owners and reference["path"] not in (ALPHA, BUILDER):
            blockers.append({
                "kind": "remaining-code-reference",
                "path": reference["path"]
            })

    errors = []
    if not gate_contract:
        errors.append("P1 scene upgrade is missing its Toolkit-only and missing-script guard")
    if legacy_constructor:
        errors.append("P1 scene builder must not instantiate the legacy controller")
    if shell_legacy:
        errors.append("Toolkit workspace has direct legacy-controller references: " +
                      ", ".join(shell_legacy))

    return {
        "schema": 2,
        "p1_guard_active": gate_contract and not legacy_constructor,
        "p1_toolkit_legacy_references": shell_legacy,
        "alpha_instantiates_legacy_ui": alpha_entrypoint,
        "p11_instantiates_legacy_ui": p11_entrypoint,
        "retired_monoscript_guid": RETIRED_GUID,
        "legacy_controller_files": owners,
        "legacy_code_references": references,
        "tracked_serialized_legacy_guid_references": serialized,
        "global_retirement_blockers": blockers,
        "global_retirement_complete": len(blockers) == 0 and not errors,
        "p1_contract_errors": errors,
        "note": "Generated Unity scenes are not repository files; the P1 Editor scene guard checks those at build time.",
    }


def self_test():
    with tempfile.TemporaryDirectory(prefix="vcr-u3-audit-") as temp:
        root = Path(temp)
        samples = {
            BUILDER: (
                "ValidateToolkitOnlyScene(scene);\n"
                "GetMonoBehavioursWithMissingScriptCount(candidate);\n"
                "GetComponentsInChildren<Transform>(true);\n"
                "const string key = \"" + LEGACY_FULL + "\";\n"),
            ALPHA: "root.AddComponent<\n    ApplicationUiController>();\n",
            META: "fileFormatVersion: 2\nguid: " + RETIRED_GUID + "\n",
            "unity/Assets/VCR/Runtime/UI/WorkspaceToolkitShell.cs": "class WorkspaceToolkitShell {}\n",
            "unity/Assets/VCR/Runtime/UI/ApplicationUiController.cs": "class ApplicationUiController {}\n",
            P11: "workspace.AddComponent<WorkspaceToolkitShell>();\n",
            U3_VALIDATOR: (
                "P1ApplicationSceneBuilder.EnsureWorkspaceInExistingScene()\n"
                "P11ApplicationSceneBuilder.CreateApplicationUiRuntimeScene()\n"
                "AlphaApplicationSceneBuilder.CreateAlphaRuntimeScene()\n"
                "GetMonoBehavioursWithMissingScriptCount(go)\n"
                "document.panelSettings\nscene.isDirty\n"
                "ValidateExistingAlphaAndP11ScenesFromMenu\n"
                "ValidateExistingAlphaAndP11ScenesAndExit\n"
                "retired application UI MonoScript GUID remains\n"
                "documentCount != 1\nHashFile(paths[i])\n"
                "if (!ValidateExistingAlphaAndP11Scenes())\n"),
            UNITY_CI: (
                "U3ToolkitSceneValidation.GenerateAndValidateAllScenesAndExit\n"
                "Require generated U3 Alpha and P11 Toolkit scenes\n"),
            U3_PLAYER: (
                "BuildWindowsMonoAndExit\nValidateExistingAlphaAndP11Scenes()\n"
                "BuildPipeline.BuildPlayer(\n"
                "P11ApplicationSceneBuilder.ScenePath\n"
                "AlphaApplicationSceneBuilder.ScenePath\n"
                "BuildMacMonoAndExit\n"
                "BuildWindowsIl2CppAndExit\n"),
            U3_PLAYER_CI: (
                "U3AlphaP11PlayerBuild.BuildWindowsMonoAndExit\n"
                "run-player-smoke.py\n"
                "u3-player-artifacts/Alpha/windows.zip\n"
                "u3-player-artifacts/P11/windows.zip\n"
                "tools/probe-u3-windows-gui.py\n"
                "nativeFramePassed\nworkspaceUiTreeReady\nmanagedQuitObserved\n"),
            U3_IL2CPP_CI: (
                "U3AlphaP11PlayerBuild.BuildWindowsIl2CppAndExit\n"
                "windows-6000.3.25f1-windows-il2cpp-3\n"
                "GameAssembly.dll\nglobal-metadata.dat\n"
                "--platform windows-nodebug\n"
                "tools/probe-u3-windows-gui.py\n"
                "for variant in Alpha P11; do\n"
                "u3-il2cpp-artifacts/$variant/windows.zip\n"),
            U3_IL2CPP_CHECK: (
                "GameAssembly.dll\nwindows.payload-sha256.json\n"
                "archive_sha256\nzipfile.open(entry\n"),
            U3_MAC_CI: (
                "U3AlphaP11PlayerBuild.BuildMacMonoAndExit\n"
                "select-p1-macos-appkit-artifact.py\n"
                "u3-macos-player-artifacts/Alpha/macos.zip\n"
                "u3-macos-player-artifacts/P11/macos.zip\n"
                "VCR_SMOKE_NATIVE_SETTINGS: '1'\n"
                "--platform macos-nodebug\n"
                "nativeSettingsInteropPassed\n"),
            U3_GUI: (
                "validate_player_archive(archive)\n"
                "gui.visible_player_windows(process.pid)\n"
                "gui.WORKSPACE_LAYOUT_MARKER\n"
                "gui.probe_free_resize(\n"
                "gui.QUIT_MARKER\nu3-gui-probe.json\n"),
            "unity/Assets/VCR/P0/Legacy.prefab":
                "m_Script: {fileID: 11500000, guid: " + RETIRED_GUID + ", type: 3}\n",
        }
        for name, content in samples.items():
            file = root / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(content, encoding="utf-8")
        report = scan(root)
        assert report["p1_guard_active"] and not report["p1_contract_errors"]
        assert report["alpha_instantiates_legacy_ui"]
        # The obsolete script definition .meta and the authored prefab both
        # contain the retired GUID in this adversarial fixture.
        assert len(report["tracked_serialized_legacy_guid_references"]) == 2
        assert any(path.endswith("Legacy.prefab") for path in
                   report["tracked_serialized_legacy_guid_references"])
        (root / META).unlink()
        assert any(path.endswith("Legacy.prefab") for path in
                   scan(root)["tracked_serialized_legacy_guid_references"])
        assert not report["global_retirement_complete"]
        assert not report["p11_instantiates_legacy_ui"]
        shell = root / "unity/Assets/VCR/Runtime/UI/WorkspaceToolkitShell.cs"
        shell.write_text("FindFirstObjectByType<ApplicationUiController>();\n",
                         encoding="utf-8")
        assert scan(root)["p1_contract_errors"]
        shell.write_text("class WorkspaceToolkitShell {}\n", encoding="utf-8")
        builder = root / BUILDER
        builder.write_text("root.AddComponent<ApplicationUiController>();\n",
                           encoding="utf-8")
        assert len(scan(root)["p1_contract_errors"]) >= 2


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--strict-p1", action="store_true",
                        help="fail on regressions in the P1 Toolkit-only contract")
    parser.add_argument("--strict-global", action="store_true",
                        help="fail if historical application uGUI still exists")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--output", type=Path, help="write JSON inventory")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    report = scan(ROOT)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n",
                               encoding="utf-8")
    print("U3 P1 Toolkit-only gate: " +
          ("PASS" if not report["p1_contract_errors"] else "FAIL"))
    print("Legacy global retirement blockers: " +
          str(len(report["global_retirement_blockers"])))
    print("Alpha still creates legacy UI: " +
          str(report["alpha_instantiates_legacy_ui"]).lower())
    for error in report["p1_contract_errors"]:
        print("ERROR: " + error)
    if args.strict_p1 and report["p1_contract_errors"]:
        raise SystemExit(1)
    if args.strict_global and not report["global_retirement_complete"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
