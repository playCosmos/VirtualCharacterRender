#!/usr/bin/env python3
"""Source-bound third-party inventory; NOT a license approval or complete SBOM.

Fails closed for malformed/unpinned Unity packages and bootstrap references.
Never downloads dependencies or executes package code.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
SHA1 = re.compile(r"^[a-f0-9]{40}$")
SHA256 = re.compile(r"^[a-f0-9]{64}$")
KNOWN_SOURCES = {"git", "builtin", "registry", "local", "local-tarball"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inspect_lock(manifest, lock):
    direct = manifest.get("dependencies")
    resolved = lock.get("dependencies")
    require(isinstance(direct, dict) and direct, "Unity manifest dependencies missing")
    require(isinstance(resolved, dict) and resolved, "Unity lock dependencies missing")
    entries = []
    for name, requested in sorted(direct.items()):
        require(isinstance(name, str) and name, "Invalid Unity package name")
        require(isinstance(requested, str) and requested, f"{name}: empty package request")
        actual = resolved.get(name)
        require(isinstance(actual, dict), f"{name}: missing locked dependency")
        locked = actual.get("version")
        source = actual.get("source")
        require(source in KNOWN_SOURCES, f"{name}: unknown lock source {source!r}")
        require(locked == requested,
                f"{name}: manifest/lock version mismatch ({requested!r} vs {locked!r})")
        if source == "git":
            require("#" in locked and locked.split("#", 1)[1],
                    f"{name}: unpinned Git reference")
            require(SHA1.fullmatch(str(actual.get("hash", ""))),
                    f"{name}: Git package missing resolved 40-hex commit")
        elif source in ("local", "local-tarball"):
            require(locked.startswith("file:LocalPackages/"),
                    f"{name}: unexpected local package location")
            subpath = Path(locked.removeprefix("file:"))
            require(not subpath.is_absolute() and ".." not in subpath.parts,
                    f"{name}: unsafe local package path")
        elif source in ("registry", "builtin"):
            require(re.fullmatch(r"\d+\.\d+\.\d+", locked) is not None,
                    f"{name}: version must be explicit semver")
        entries.append({
            "name": name,
            "requested": requested,
            "resolvedSource": source,
            "resolvedCommit": actual.get("hash") if source == "git" else None,
            "licenseReview": "pending-manual-review",
        })
    return entries


def bootstrap_pins(root):
    tools = root / "tools"
    def read(name):
        return (tools / name).read_text(encoding="utf-8")
    mediapipe = read("bootstrap-mediapipe.sh")
    m = re.search(r'^VERSION="([0-9]+\.[0-9]+\.[0-9]+)"$', mediapipe, re.M)
    d = re.search(r'^EXPECTED_SHA256="([a-f0-9]{64})"$', mediapipe, re.M)
    require(m is not None and d is not None and SHA256.fullmatch(d.group(1)),
            "MediaPipe download must have a version and verified SHA-256")
    pins = {"mediapipe": {"version": m.group(1), "sha256": d.group(1)}}
    for key, script in (("ultraleap", "bootstrap-ultraleap.sh"),
                        ("uniwinc", "bootstrap-uniwinc.sh")):
        source = read(script)
        match = re.search(r'^PIN="([a-f0-9]{40})"$', source, re.M)
        require(match is not None, f"{key}: exact upstream commit missing")
        pins[key] = {"sourceCommit": match.group(1)}
    source = read("bootstrap-ui-fonts.sh")
    fonts = re.findall(
        r'^install_font\s+"([^"]+\.otf)"\s+"([a-f0-9]{40})"\s*$',
        source, re.M)
    require(len(fonts) == 3 and len({name for name, _ in fonts}) == 3,
            "Pretendard fonts must have three distinct pinned Git blobs")
    pins["pretendard"] = {
        "files": [{"name": name, "gitBlob": blob} for name, blob in fonts]}
    return pins


def generate(root):
    manifest = json.loads((root / "unity/Packages/manifest.json").read_text(encoding="utf-8"))
    lock = json.loads((root / "unity/Packages/packages-lock.json").read_text(encoding="utf-8"))
    entries = inspect_lock(manifest, lock)
    pins = bootstrap_pins(root)
    requested = {x["name"]: x["requested"] for x in entries}
    require(requested.get("com.github.homuler.mediapipe") ==
            f'file:LocalPackages/com.github.homuler.mediapipe-{pins["mediapipe"]["version"]}.tgz',
            "MediaPipe manifest package must match verified bootstrap version")
    require(requested.get("com.ultraleap.tracking") ==
            "file:LocalPackages/com.ultraleap.tracking",
            "Ultraleap manifest must use audited local bootstrap")
    require(requested.get("com.kirurobo.uniwinc") ==
            "file:LocalPackages/com.kirurobo.uniwinc",
            "UniWinC manifest must use audited local bootstrap")
    return {
        "schema": "vcr-dependency-inventory-v1",
        "licenseAuditApproved": False,
        "distributionApproved": False,
        "disclaimer": "Pin verification only. Not SPDX SBOM, license clearance or distribution approval.",
        "directUnityPackages": entries,
        "lockedPackageCount": len(lock["dependencies"]),
        "bootstrapInputs": pins,
        "outstandingCommercialChecks": [
            "Verify redistribution licenses and required notices per dependency",
            "Inspect transitive/native binaries and model redistribution rights",
            "Verify final distributed archive matches audited components",
            "Verify Apple/Windows signing, notarization and OS permissions",
        ],
    }


def self_test():
    inventory = generate(ROOT)
    assert inventory["licenseAuditApproved"] is False
    assert len(inventory["directUnityPackages"]) >= 5
    manifest = json.loads((ROOT / "unity/Packages/manifest.json").read_text(encoding="utf-8"))
    lock = json.loads((ROOT / "unity/Packages/packages-lock.json").read_text(encoding="utf-8"))
    bad = json.loads(json.dumps(manifest))
    bad["dependencies"]["com.vrmc.univrm"] += "#unsafe"
    try:
        inspect_lock(bad, lock)
    except ValueError as exc:
        assert "mismatch" in str(exc)
    else:
        raise AssertionError("Unpinned Unity manifest drift must fail")
    bad_lock = json.loads(json.dumps(lock))
    bad_lock["dependencies"]["com.vrmc.univrm"].pop("hash")
    try:
        inspect_lock(manifest, bad_lock)
    except ValueError as exc:
        assert "resolved 40-hex commit" in str(exc)
    else:
        raise AssertionError("Unresolved Git dependency must fail")
    print("Commercial dependency pin/inventory self-test PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        data = generate(ROOT)
        encoded = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(encoded, encoding="utf-8")
        else:
            print(encoded, end="")
        print("Unity dependency pin inventory PASS; license/distribution audit PENDING",
              file=sys.stderr)
        return 0
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as exc:
        print("Dependency inventory FAIL: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
