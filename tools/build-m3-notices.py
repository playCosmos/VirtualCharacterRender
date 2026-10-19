#!/usr/bin/env python3
"""Fail-closed THIRD_PARTY_NOTICES builder for independently reviewed M3 ZIPs.

No guessed licenses, no default approvals, and no network calls. Bind EACH
shipped file/link to its archive digest and original payload SHA256. Pending
reviews cannot produce a notice or distribution PASS. This tool does not
grant legal rights; independent rights/signing/installation checks remain.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
import sys
import tempfile
import zipfile

PAYLOAD_TOOL = Path(__file__).with_name("generate-m3-payload-inventory.py")
VERSION = "vcr-m3-reviewed-licenses-v1"
DECISIONS = ("FIRST_PARTY", "THIRD_PARTY_NOTICE")
MAX_REVIEW_BYTES = 32 * 1024 * 1024
MAX_NOTICE_BYTES = 1024 * 1024


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def payload_tool():
    spec = importlib.util.spec_from_file_location("m3_payload", PAYLOAD_TOOL)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def source_entries(inventory):
    return [
        {"archive": asset["archiveName"], "archiveSha256": asset["archiveSha256"],
         "path": entry["path"], "sha256": entry["sha256"]}
        for asset in inventory["assets"] for entry in asset["entries"]
    ]


def blank_template(inventory):
    return {
        "schema": VERSION,
        "version": inventory["version"],
        "sourceSha": inventory["sourceSha"],
        "entries": [
            {**item, "decision": "PENDING",
             "component": None, "licenseId": None, "noticePath": None,
             "noticeSha256": None, "reviewer": None, "reviewedUtc": None}
            for item in source_entries(inventory)
        ],
        "warning": "NOT APPROVED; fill every review explicitly from verified licensing evidence.",
    }


def read_json(path):
    require(path.is_file() and not path.is_symlink() and
            0 < path.stat().st_size <= MAX_REVIEW_BYTES,
            "Review file absent/unsafe/too large")
    def no_duplicate_keys(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "Duplicated review field: " + key)
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"),
                      object_pairs_hook=no_duplicate_keys)


def validated_notice(root, relative, sha):
    require(isinstance(relative, str) and relative and
            not relative.startswith(("/", "\\")) and
            "\\" not in relative and ":" not in relative,
            "Notice must be a safe relative UTF-8 path")
    parts = PurePosixPath(relative).parts
    require(parts and all(p not in ("..", ".") for p in parts),
            "Notice traversal rejected")
    root = root.resolve(strict=True)
    path = root.joinpath(*parts)
    require(not root.is_symlink() and
            path.is_file() and not path.is_symlink() and
            path.resolve(strict=True).is_relative_to(root) and
            not any(part.is_symlink() for part in
                    [root.joinpath(*parts[:n]) for n in range(1, len(parts))]),
            "Notice path missing, symlinked or outside review root")
    require(0 < path.stat().st_size <= MAX_NOTICE_BYTES,
            "Notice is empty or too large")
    data = path.read_bytes()
    require(isinstance(sha, str) and
            re.fullmatch(r"[a-f0-9]{64}", sha) is not None and
            hashlib.sha256(data).hexdigest() == sha,
            "Notice text SHA256 mismatch")
    text = data.decode("utf-8")
    require(text.strip() and "\x00" not in text, "Invalid UTF-8 license notice")
    return text


def build(inventory, review, notices_root):
    require(type(review) is dict and review.get("schema") == VERSION and
            review.get("version") == inventory["version"] and
            review.get("sourceSha") == inventory["sourceSha"],
            "License review bound to wrong release/source")
    expected = {
        (e["archive"], e["path"]): e for e in source_entries(inventory)
    }
    require(type(review.get("entries")) is list and
            len(review["entries"]) == len(expected),
            "Every shipped file/link must have exactly one review")
    found = set()
    groups = {}
    for item in review["entries"]:
        require(type(item) is dict, "Invalid review entry")
        key = (item.get("archive"), item.get("path"))
        require(key in expected and key not in found,
                "Extra, duplicated or missing reviewed file")
        found.add(key)
        original = expected[key]
        require(item.get("sha256") == original["sha256"] and
                item.get("archiveSha256") == original["archiveSha256"],
                "Review belongs to a changed payload")
        decision = item.get("decision")
        require(decision in DECISIONS,
                "PENDING/unapproved component blocks third-party notices")
        component = item.get("component")
        reviewer = item.get("reviewer")
        date = item.get("reviewedUtc")
        require(isinstance(component, str) and 0 < len(component.strip()) <= 128 and
                isinstance(reviewer, str) and 0 < len(reviewer.strip()) <= 128 and
                isinstance(date, str) and
                re.fullmatch(r"\d{4}-\d{2}-\d{2}", date) is not None,
                "Each approval requires component/reviewer/ISO date")
        if decision == "FIRST_PARTY":
            require(not item.get("licenseId") and not item.get("noticePath") and
                    not item.get("noticeSha256"),
                    "First-party entry cannot silently supply third-party notice")
            continue
        license_id = item.get("licenseId")
        notice_path = item.get("noticePath")
        require(isinstance(license_id, str) and
                re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+-]{0,79}", license_id) is not None,
                "Third-party license ID missing/invalid")
        text = validated_notice(notices_root, notice_path, item.get("noticeSha256"))
        group_key = (component.strip(), license_id, item["noticeSha256"])
        group = groups.setdefault(group_key, {
            "notice": text, "paths": [], "sourcePath": notice_path
        })
        require(group["notice"] == text, "Conflicting notice text")
        group["paths"].append(item["archive"] + ": " + item["path"])
    require(found == set(expected), "Unreviewed shipped files remain")
    lines = [
        "VirtualCharacterRender — THIRD_PARTY_NOTICES",
        "",
        "Generated from explicit human review entries bound to exact release ZIP",
        "and payload SHA256. NOT an independent legal, SBOM, signing or release approval.",
        f"Version: {inventory['version']}",
        f"Source commit: {inventory['sourceSha']}",
        "",
    ]
    for (name, license_id, _), group in sorted(groups.items()):
        lines.extend([
            "=" * 72, name, "Declared license: " + license_id,
            "Reviewed license text: " + group["sourcePath"],
            "Shipped files:",
            *["  - " + path for path in sorted(group["paths"])],
            "", group["notice"].rstrip(), "",
        ])
    if not groups:
        lines.append("No third-party components declared by the human review.")
    return "\n".join(lines).rstrip() + "\n", {
        "schema": "vcr-m3-license-notice-build-report-v1",
        "sourceSha": inventory["sourceSha"],
        "version": inventory["version"],
        "reviewedFiles": len(found),
        "thirdPartyNoticeGroups": len(groups),
        "reviewInputsAccepted": True,
        "licenseAuditApproved": False,
        "distributionAcceptance": False,
        "caveat": "Human review assertions validated for integrity; legal clearance remains independent",
    }


def self_test():
    with tempfile.TemporaryDirectory() as dirname:
        root = Path(dirname)
        version = "0.1.0-alpha.9"
        source = "a" * 40
        import importlib.util
        distribution_spec = importlib.util.spec_from_file_location(
            "m3_dist", Path(__file__).with_name("verify-m3-distribution.py"))
        m = importlib.util.module_from_spec(distribution_spec)
        distribution_spec.loader.exec_module(m)
        names = m.expected_names(version)
        with zipfile.ZipFile(root / names[0], "w") as file:
            file.writestr("VirtualCharacterRender.exe", b"MZtest")
            file.writestr("VirtualCharacterRender_Data/font.otf", b"pretendard")
        with zipfile.ZipFile(root / names[1], "w") as file:
            file.writestr("VirtualCharacterRender.app/Contents/Info.plist", b"plist")
            file.writestr("VirtualCharacterRender.app/Contents/MacOS/VirtualCharacterRender", b"native")
        claims = [{"name": name, "sha256": m.sha256_file(root / name)}
                  for name in names]
        (root / "build-provenance.json").write_text(json.dumps({
            "tag": "v" + version, "source_sha": source,
            "unity_version": "6000.3.25f1",
            "workflow_run": "https://github.com/playCosmos/VirtualCharacterRender/actions/runs/1",
            "assets": claims,
        }), encoding="utf-8")
        (root / "SHA256SUMS.txt").write_text(
            "".join(x["sha256"] + "  " + x["name"] + "\n" for x in claims),
            encoding="utf-8")
        inventory = payload_tool().collect(root, version, source)
        review = blank_template(inventory)
        require(len(review["entries"]) == 4, "Unexpected fixture coverage")
        def fails(review_case):
            try:
                build(inventory, review_case, root)
            except (ValueError, TypeError, KeyError):
                return
            raise AssertionError("Unreviewed/mismatched licenses falsely passed")
        fails(review)
        for entry in review["entries"]:
            entry.update(decision="FIRST_PARTY", component="VirtualCharacterRender",
                         reviewer="test-reviewer", reviewedUtc="2026-10-19")
        font = next(x for x in review["entries"] if x["path"].endswith(".otf"))
        text = "Synthetic test notice. Example rights statement.\n"
        (root / "font-license.txt").write_text(text, encoding="utf-8")
        font.update(decision="THIRD_PARTY_NOTICE", component="Test Font",
                    licenseId="OFL-1.1", noticePath="font-license.txt",
                    noticeSha256=hashlib.sha256(text.encode()).hexdigest())
        notice, report = build(inventory, review, root)
        assert "Test Font" in notice and "OFL-1.1" in notice
        assert "font.otf" in notice and report["licenseAuditApproved"] is False
        corrupted = json.loads(json.dumps(review))
        corrupted["entries"][0]["sha256"] = "0" * 64
        fails(corrupted)
        omitted = json.loads(json.dumps(review))
        omitted["entries"].pop()
        fails(omitted)
        pending = json.loads(json.dumps(review))
        pending["entries"][0]["decision"] = "PENDING"
        fails(pending)
        font["noticeSha256"] = "0" * 64
        fails(review)
    print("M3 reviewed THIRD_PARTY_NOTICES self-test PASS; real legal review deferred")


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--self-test", action="store_true")
    cli.add_argument("--artifacts", type=Path)
    cli.add_argument("--version")
    cli.add_argument("--source")
    cli.add_argument("--template", type=Path,
                     help="Write a PENDING per-file review template; no notices")
    cli.add_argument("--review", type=Path,
                     help="Human-completed exact-file review JSON")
    cli.add_argument("--notices-root", type=Path,
                     help="Local folder with reviewed UTF-8 license text")
    cli.add_argument("--output", type=Path,
                     help="Create THIRD_PARTY_NOTICES.txt (no overwrite)")
    args = cli.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        require(args.artifacts is not None and args.version and args.source,
                "--artifacts, --version and --source are required")
        inventory = payload_tool().collect(args.artifacts, args.version, args.source)
        if args.template is not None:
            require(args.review is None and args.output is None,
                    "--template cannot be used as approved notice output")
            with args.template.open("x", encoding="utf-8") as output:
                json.dump(blank_template(inventory), output, indent=2,
                          ensure_ascii=False)
                output.write("\n")
            print("M3 PENDING license review template created; not approved")
            return 0
        require(args.review is not None and args.notices_root is not None and
                args.output is not None,
                "--review, --notices-root and --output are required")
        reviewed = read_json(args.review)
        notices, report = build(inventory, reviewed, args.notices_root)
        with args.output.open("x", encoding="utf-8") as file:
            file.write(notices)
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 0
    except (OSError, ValueError, KeyError, TypeError, UnicodeError,
            zipfile.BadZipFile, RuntimeError) as error:
        print("M3 notices INCOMPLETE/BLOCKED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
