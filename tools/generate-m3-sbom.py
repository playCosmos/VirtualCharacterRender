#!/usr/bin/env python3
"""Generate a deterministic *partial* CycloneDX 1.6 inventory from pinned source.

This covers Unity package-lock dependencies and bootstrap inputs. It does NOT
invent license identifiers or attest packaged native binary contents. A human
license and final-binary audit is mandatory before M3 distribution approval.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parent.parent
AUDIT = Path(__file__).with_name("audit-third-party-dependencies.py")


def load_audit():
    spec = importlib.util.spec_from_file_location("vcr_third_party_inventory", AUDIT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def property_pair(name, value):
    return {"name": name, "value": str(value)}


def generate(root=ROOT):
    audit = load_audit()
    inventory = audit.generate(root)
    lock = json.loads((root / "unity/Packages/packages-lock.json").read_text("utf-8"))
    resolved = lock["dependencies"]
    components = []
    dependencies = []
    for item in inventory["resolvedUnityPackages"]:
        name = item["name"]
        record = resolved[name]
        version = record["version"]
        component = {
            "type": "library",
            "bom-ref": "unity:" + name,
            "name": name,
            "version": version,
            "properties": [
                property_pair("vcr:source", item["resolvedSource"]),
                property_pair("vcr:license-review", "PENDING_MANUAL_APPROVAL"),
                property_pair("vcr:distribution-included", "NOT_VERIFIED"),
                property_pair("vcr:package-depth", record.get("depth", "unknown")),
            ],
        }
        if item.get("resolvedCommit"):
            component["properties"].append(
                property_pair("vcr:resolved-git-commit",
                              item["resolvedCommit"]))
        components.append(component)
        dependencies.append({
            "ref": "unity:" + name,
            "dependsOn": sorted(
                "unity:" + target for target in
                record.get("dependencies", {}) if target in resolved),
        })
    pins = inventory["bootstrapInputs"]
    for name in sorted(pins):
        entry = pins[name]
        ver = entry.get("version", entry.get("sourceCommit", "pinned-blobs"))
        props = [
            property_pair("vcr:source", "bootstrap"),
            property_pair("vcr:license-review", "PENDING_MANUAL_APPROVAL"),
            property_pair("vcr:distribution-included", "NOT_VERIFIED"),
        ]
        if entry.get("sha256"):
            props.append(property_pair("vcr:bootstrap-sha256", entry["sha256"]))
        if entry.get("sourceCommit"):
            props.append(property_pair("vcr:source-commit", entry["sourceCommit"]))
        if entry.get("files"):
            props.append(property_pair("vcr:pinned-file-count", len(entry["files"])))
        components.append({
            "type": "library",
            "bom-ref": "bootstrap:" + name,
            "name": name,
            "version": ver,
            "properties": props,
        })
    return {
        "bomFormat": "CycloneDX",
        "specVersion": "1.6",
        "version": 1,
        "metadata": {
            "component": {"type": "application", "name": "VirtualCharacterRender",
                          "version": "unreleased-source"},
            "properties": [
                property_pair("vcr:coverage", "PARTIAL_SOURCE_PIN_INVENTORY_ONLY"),
                property_pair("vcr:license-audit", "NOT_APPROVED"),
                property_pair("vcr:distribution-acceptance", "DEFERRED"),
                property_pair("vcr:uncovered",
                              "final native binaries, OS SDK/Unity terms, shipped models and runtime assets"),
            ],
        },
        "components": sorted(components, key=lambda entry: entry["bom-ref"]),
        "dependencies": sorted(dependencies, key=lambda entry: entry["ref"]),
    }


def self_test():
    document = generate()
    assert document["bomFormat"] == "CycloneDX"
    assert document["specVersion"] == "1.6"
    refs = [x["bom-ref"] for x in document["components"]]
    assert len(refs) == len(set(refs))
    assert len(refs) >= 35
    assert all("licenses" not in component for component in document["components"])
    assert all(any(p["name"] == "vcr:license-review" and
                   p["value"] == "PENDING_MANUAL_APPROVAL"
                   for p in component["properties"])
               for component in document["components"])
    assert all(set(dep["dependsOn"]).issubset(set(refs))
               for dep in document["dependencies"])
    assert document["metadata"]["properties"][1]["value"] == "NOT_APPROVED"
    print("M3 partial CycloneDX source-inventory self-test PASS; legal audit PENDING")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        output = json.dumps(generate(), indent=2, ensure_ascii=False) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("x", encoding="utf-8") as file:
                file.write(output)
        else:
            print(output, end="")
        return 0
    except (ValueError, OSError, KeyError, AssertionError, TypeError) as exc:
        print("M3 partial SBOM generation FAILED: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
