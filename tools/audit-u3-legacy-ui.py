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
    legacy_guid = match.group(1) if match else None
    serialized = []
    if legacy_guid:
        for ext in ("*.unity", "*.prefab", "*.asset"):
            for path in sorted((root / SOURCE).rglob(ext)):
                if legacy_guid in path.read_text(encoding="utf-8", errors="replace"):
                    serialized.append(str(path.relative_to(root)).replace("\\", "/"))

    alpha = read(root, ALPHA)
    alpha_entrypoint = re.search(
        r"AddComponent\s*<\s*ApplicationUiController\s*>", alpha) is not None

    # Explicit blockers are not errors in a staged migration. The P0-P13
    # historical Alpha path and editor validation must be migrated separately.
    blockers = []
    if alpha_entrypoint:
        blockers.append({"kind": "alpha-scene-entrypoint", "path": ALPHA})
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
        "schema": 1,
        "p1_guard_active": gate_contract and not legacy_constructor,
        "p1_toolkit_legacy_references": shell_legacy,
        "alpha_instantiates_legacy_ui": alpha_entrypoint,
        "legacy_controller_files": owners,
        "legacy_code_references": references,
        "tracked_serialized_legacy_guid_references": serialized,
        "global_retirement_blockers": blockers,
        "global_retirement_complete": len(blockers) == 0 and not shell_legacy,
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
            META: "fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\n",
            "unity/Assets/VCR/Runtime/UI/WorkspaceToolkitShell.cs": "class WorkspaceToolkitShell {}\n",
            "unity/Assets/VCR/Runtime/UI/ApplicationUiController.cs": "class ApplicationUiController {}\n",
            "unity/Assets/VCR/P0/Legacy.prefab":
                "m_Script: {fileID: 11500000, guid: 1234567890abcdef1234567890abcdef, type: 3}\n",
        }
        for name, content in samples.items():
            file = root / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(content, encoding="utf-8")
        report = scan(root)
        assert report["p1_guard_active"] and not report["p1_contract_errors"]
        assert report["alpha_instantiates_legacy_ui"]
        assert len(report["tracked_serialized_legacy_guid_references"]) == 1
        assert not report["global_retirement_complete"]
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


if __name__ == "__main__":
    main()
