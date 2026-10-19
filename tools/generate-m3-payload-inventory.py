#!/usr/bin/env python3
"""Generate an exact-bytes ZIP payload inventory for M3 license/binary audit.

Input ZIPs must pass the existing read-only M3 provenance preflight first.
This inventory hashes each *uncompressed* shipped entry (or symlink text);
it does not identify vendors, claim licenses, inspect notarization, or
approve redistribution. The human reviewer must map files to owners/notices.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import zipfile

SCRIPT = Path(__file__).with_name("verify-m3-distribution.py")
NATIVE_ENDINGS = (".exe", ".dll", ".dylib", ".so", ".bundle", ".framework",
                  ".a", ".lib", ".app", ".jar", ".node", ".pyd")
ASSET_ENDINGS = (".vrm", ".onnx", ".tflite", ".task", ".pb", ".bin",
                 ".otf", ".ttf", ".ttc", ".woff", ".woff2",
                 ".png", ".jpg", ".jpeg", ".webp", ".wav", ".mp3", ".ogg")
MAX_MEMBERS = 100000


def verifier():
    spec = importlib.util.spec_from_file_location("vcr_m3_distribution", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest_stream(reader):
    result = hashlib.sha256()
    for chunk in iter(lambda: reader.read(1024 * 1024), b""):
        result.update(chunk)
    return result.hexdigest()


def classify(path, symlink):
    lower = path.lower()
    if symlink:
        return "SYMLINK_REVIEW"
    if lower.endswith(NATIVE_ENDINGS) or ".framework/" in lower or "/macos/" in lower:
        return "EXECUTABLE_OR_NATIVE_LIBRARY_REVIEW"
    if lower.endswith(ASSET_ENDINGS):
        return "MODEL_FONT_OR_MEDIA_RIGHTS_REVIEW"
    return "OTHER_DISTRIBUTED_FILE_REVIEW"


def collect(archives, version, source):
    check = verifier()
    preflight = check.check_release(archives, version, source)
    data = []
    total = 0
    for asset in preflight["assets"]:
        archive_name = asset["file"]
        entries = []
        with zipfile.ZipFile(archives / archive_name) as package:
            for item in package.infolist():
                if item.is_dir():
                    continue
                # inspect_zip checked path traversal, duplicates, limits,
                # ZIP CRC, symlink boundary and version/source association.
                import stat
                symlink = stat.S_IFMT(item.external_attr >> 16) == stat.S_IFLNK
                with package.open(item) as contents:
                    digest = digest_stream(contents)
                entries.append({
                    "path": item.filename.removeprefix("./"),
                    "sha256": digest,
                    "sizeBytes": item.file_size,
                    "kind": "symlink" if symlink else "file",
                    "reviewCategory": classify(item.filename, symlink),
                    "componentOwner": None,
                    "redistributionLicense": None,
                    "licenseReview": "PENDING_MANUAL_REVIEW",
                })
                total += 1
                check.require(total <= MAX_MEMBERS * 2,
                              "Combined release payload count too large")
        data.append({
            "archiveName": archive_name,
            "archiveSha256": asset["sha256"],
            "platformLabel": asset["platformLabel"],
            "shippedFilesOrLinks": len(entries),
            "entries": sorted(entries, key=lambda entry: entry["path"]),
        })
    return {
        "suite": "m3-final-payload-inventory-v1",
        "scope": "EXACT_FILES_INSIDE_VERIFIED_SOURCE_BOUND_ZIPS",
        "version": version,
        "sourceSha": source,
        "sourcePackageToShippedFileMapping": "PENDING_MANUAL_REVIEW",
        "nativeBinaryLicensesAndSignaturesVerified": False,
        "commercialRedistributionApproved": False,
        "distributionAcceptance": False,
        "shippedFilesOrLinks": total,
        "assets": data,
    }


def self_test():
    from contextlib import redirect_stdout
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        check = verifier()
        version = "0.1.0-alpha.9"
        source = "a" * 40
        names = check.expected_names(version)

        def setup():
            with zipfile.ZipFile(root / names[0], "w") as z:
                z.writestr("VirtualCharacterRender.exe", b"MZapp")
                z.writestr("UnityPlayer.dll", b"MZlibrary")
                z.writestr("VirtualCharacterRender_Data/model.task", b"model")
            with zipfile.ZipFile(root / names[1], "w") as z:
                z.writestr("VirtualCharacterRender.app/Contents/Info.plist", b"plist")
                z.writestr("VirtualCharacterRender.app/Contents/MacOS/VirtualCharacterRender", b"binary")
            assets = [{"name": n, "sha256": check.sha256_file(root / n)}
                      for n in names]
            (root / "SHA256SUMS.txt").write_text(
                "".join(a["sha256"] + "  " + a["name"] + "\n" for a in assets),
                encoding="utf-8")
            (root / "build-provenance.json").write_text(json.dumps({
                "tag": "v" + version,
                "source_sha": source,
                "unity_version": "6000.3.25f1",
                "workflow_run": "https://github.com/playCosmos/VirtualCharacterRender/actions/runs/1",
                "assets": assets,
            }), encoding="utf-8")

        setup()
        report = collect(root, version, source)
        assert report["shippedFilesOrLinks"] == 5
        assert report["distributionAcceptance"] is False
        assert report["commercialRedistributionApproved"] is False
        entry = next(e for e in report["assets"][0]["entries"]
                     if e["path"] == "UnityPlayer.dll")
        assert entry["sha256"] == hashlib.sha256(b"MZlibrary").hexdigest()
        assert entry["reviewCategory"] == "EXECUTABLE_OR_NATIVE_LIBRARY_REVIEW"
        assert all(e["licenseReview"] == "PENDING_MANUAL_REVIEW"
                   for a in report["assets"] for e in a["entries"])
        with zipfile.ZipFile(root / names[0], "a") as z:
            z.writestr("hidden-extra.dll", b"unexpected")
        try:
            collect(root, version, source)
        except ValueError:
            pass
        else:
            raise AssertionError("Modified archive without new manifest checksum was accepted")
    print("M3 exact-archive payload inventory self-test PASS; legal/native R approval deferred")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--version")
    parser.add_argument("--source")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        if not args.artifacts or not args.version or not args.source:
            parser.error("--artifacts, --version and --source are required")
        output = json.dumps(collect(args.artifacts, args.version, args.source),
                            indent=2, ensure_ascii=False) + "\n"
        if args.output:
            with args.output.open("x", encoding="utf-8") as file:
                file.write(output)
        else:
            print(output, end="")
        return 0
    except (ValueError, OSError, RuntimeError, UnicodeError,
            zipfile.BadZipFile) as exc:
        print("M3 payload inventory FAILED/INCOMPLETE: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
