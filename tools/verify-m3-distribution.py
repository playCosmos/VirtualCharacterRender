#!/usr/bin/env python3
"""Read-only M3 source-bound alpha artifact preflight; NOT a signing/release PASS.

Use on independently downloaded release files before any production-signing
decision. No network access, archive extraction, uploads, secrets, or packaging.
This checks *claimed* source and bytes, not trusted publisher identity, clean
installation, licenses, notarization, Gatekeeper or Authenticode.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import posixpath
import re
import stat
import sys
import tempfile
import zipfile

SHA40 = re.compile(r"[0-9a-f]{40}\Z")
SHA64 = re.compile(r"[0-9a-f]{64}\Z")
VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?\Z")
MAX_MANIFEST_BYTES = 1024 * 1024
MAX_ZIP_ENTRIES = 100000
MAX_UNCOMPRESSED_BYTES = 16 * 1024**3


def require(test, why):
    if not test:
        raise ValueError(why)


def read_small(path):
    require(path.is_file() and not path.is_symlink() and
            0 < path.stat().st_size <= MAX_MANIFEST_BYTES,
            "missing/unsafe/oversized manifest: " + str(path))
    return path.read_text(encoding="utf-8")


def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate provenance JSON field: " + key)
        result[key] = value
    return result


def sha256_file(path):
    h = hashlib.sha256()
    with path.open("rb") as input_file:
        for block in iter(lambda: input_file.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def expected_names(version):
    return ("VirtualCharacterRender-" + version + "-Windows-x64.zip",
            "VirtualCharacterRender-" + version + "-macOS.zip")


def validate_member_name(name):
    require(name and not name.startswith("/") and
            "\\" not in name and "\x00" not in name and
            not any(ord(char) < 32 for char in name),
            "unsafe ZIP member path")
    trimmed = name
    while trimmed.startswith("./"):
        trimmed = trimmed[2:]
    require(trimmed and not trimmed.startswith("/") and
            ":" not in trimmed and not re.match(r"^[A-Za-z]:", trimmed),
            "absolute/drive/alternate-data-stream ZIP member path")
    parts = PurePosixPath(trimmed).parts
    require(".." not in parts and
            posixpath.normpath(trimmed).lstrip("/") != ".." and
            not posixpath.normpath(trimmed).startswith("../"),
            "ZIP path traversal")
    return posixpath.normpath(trimmed).rstrip("/")


def inspect_zip(path, platform):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size > 0,
            "missing/unsafe/empty release archive: " + str(path))
    found = {}
    folded = set()
    total_bytes = 0
    with zipfile.ZipFile(path, "r") as archive:
        infos = archive.infolist()
        require(0 < len(infos) <= MAX_ZIP_ENTRIES,
                "unreasonable ZIP member count")
        for entry in infos:
            name = validate_member_name(entry.filename)
            require(name not in found, "duplicated ZIP member: " + name)
            if platform == "windows":
                require(name.casefold() not in folded,
                        "Case-colliding Windows ZIP member: " + name)
                folded.add(name.casefold())
            found[name] = entry
            require(not (entry.flag_bits & 1), "encrypted ZIP entry")
            unix_type = stat.S_IFMT(entry.external_attr >> 16)
            require(unix_type in (0, stat.S_IFREG, stat.S_IFDIR, stat.S_IFLNK),
                    "Unsupported special-file ZIP entry")
            total_bytes += entry.file_size
            require(total_bytes <= MAX_UNCOMPRESSED_BYTES,
                    "ZIP uncompressed content too large")
            if unix_type == stat.S_IFLNK:
                require(entry.file_size <= 4096, "oversized ZIP symlink")
                link = archive.read(entry).decode("utf-8")
                require(link and not link.startswith("/") and "\\" not in link and
                        not re.match(r"^[A-Za-z]:", link),
                        "absolute ZIP symlink")
                target = posixpath.normpath(
                    posixpath.join(posixpath.dirname(name), link))
                require(target != ".." and not target.startswith("../"),
                        "ZIP symlink escapes archive")
        # ZIP CRC integrity is distinct from the compressed-file checksum:
        # reject even a correctly hashed producer ZIP with broken members.
        require(archive.testzip() is None, "Corrupt ZIP member CRC")
        if platform == "windows":
            exe = "VirtualCharacterRender.exe"
            require(exe in found and not found[exe].is_dir() and
                    stat.S_IFMT(found[exe].external_attr >> 16) != stat.S_IFLNK,
                    "Windows EXE missing or symlink")
            require(any(name.startswith("VirtualCharacterRender_Data/") and
                        not item.is_dir() for name, item in found.items()),
                    "Windows data directory missing")
            with archive.open(found[exe]) as stream:
                require(stream.read(2) == b"MZ", "Windows EXE lacks MZ header")
        else:
            plist = "VirtualCharacterRender.app/Contents/Info.plist"
            require(plist in found and not found[plist].is_dir(),
                    "macOS Info.plist missing")
            exec_prefix = "VirtualCharacterRender.app/Contents/MacOS/"
            executables = [(name, item) for name, item in found.items()
                           if name.startswith(exec_prefix) and
                           len(name) > len(exec_prefix) and
                           not item.is_dir() and
                           stat.S_IFMT(item.external_attr >> 16) != stat.S_IFLNK]
            require(executables, "macOS bundle executable missing")
            # Architecture, hardened-runtime options and signature are not
            # inferred from archive naming or Mach-O header alone.
            require(found[plist].file_size > 0, "empty macOS Info.plist")
    return {"members": len(found), "uncompressedBytes": total_bytes,
            "binaryStructure": "EXPECTED_PATHS_AND_HEADERS_ONLY"}


def check_release(root, version, source):
    require(VERSION.fullmatch(version) is not None,
            "Invalid release version")
    require(SHA40.fullmatch(source) is not None,
            "Expected exact lower-case 40-hex source SHA")
    root = Path(root)
    require(root.is_dir() and not root.is_symlink(),
            "Release artifact directory missing/unsafe")
    try:
        manifest = json.loads(
            read_small(root / "build-provenance.json"),
            object_pairs_hook=reject_duplicate_keys)
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError("Invalid build provenance JSON") from exc
    require(type(manifest) is dict, "Build provenance must be an object")
    require(manifest.get("tag") == "v" + version and
            manifest.get("source_sha") == source and
            manifest.get("unity_version") == "6000.3.25f1",
            "Release tag/source/Unity provenance mismatch")
    require(isinstance(manifest.get("workflow_run"), str) and
            manifest["workflow_run"].startswith(
                "https://github.com/playCosmos/VirtualCharacterRender/actions/runs/"),
            "Missing canonical build workflow URL")
    require(type(manifest.get("assets")) is list and
            len(manifest["assets"]) == 2, "Exactly two release assets required")

    names = expected_names(version)
    claims = {}
    for entry in manifest["assets"]:
        require(type(entry) is dict and
                entry.get("name") in names and
                isinstance(entry.get("sha256"), str) and
                SHA64.fullmatch(entry["sha256"]) is not None,
                "Malformed release provenance asset")
        require(entry["name"] not in claims, "Duplicate release asset claim")
        claims[entry["name"]] = entry["sha256"]
    require(set(claims) == set(names), "Release manifest asset set mismatch")

    lines = read_small(root / "SHA256SUMS.txt").splitlines()
    require(len(lines) == 2, "Exactly two checksum rows required")
    checksums = {}
    for line in lines:
        match = re.fullmatch(r"([a-f0-9]{64}) [ *]([^/\\]+)", line)
        require(match is not None, "Invalid checksum row")
        checksum, name = match.groups()
        require(name in names and name not in checksums,
                "Unexpected/duplicate checksum filename")
        checksums[name] = checksum
    require(checksums == claims, "SHA256SUMS and provenance disagree")

    report_assets = []
    for name, platform in zip(names, ("windows", "macos")):
        archive = root / name
        actual = sha256_file_checked(archive)
        require(actual == claims[name], "Independently rehashed archive differs: " + name)
        structure = inspect_zip(archive, platform)
        report_assets.append({"file": name, "sha256": actual,
                              "platformLabel": platform, **structure})
    return {
        "suite": "m3-offline-distribution-preflight-v1",
        "status": "SOURCE_BOUND_ARCHIVES_VERIFIED_ONLY",
        "sourceSha": source, "version": version, "assets": report_assets,
        "publisherIdentityVerified": False,
        "windowsAuthenticodeVerified": False,
        "macosDeveloperIdVerified": False,
        "macosNotarizationVerified": False,
        "licenseAuditApproved": False,
        "cleanMachineValidation": "DEFERRED",
        "distributionAcceptance": False,
        "notes": [
            "Archive digests and contents were independently recalculated against locally supplied provenance.",
            "The provenance document is an untrusted producer claim; no publisher cryptographic identity was authenticated.",
            "Windows signing, macOS hardened runtime/notarization/stapling, licenses, clean installation and rollback require native independent evidence.",
        ],
    }


def sha256_file_checked(path):
    require(path.is_file() and not path.is_symlink() and
            path.stat().st_size > 0,
            "Release archive missing, empty or unsafe: " + str(path))
    return sha256_file(path)


def self_test():
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        version = "0.1.0-alpha.9"
        source = "a" * 40
        names = expected_names(version)
        def create_archives():
            with zipfile.ZipFile(root / names[0], "w") as z:
                z.writestr("VirtualCharacterRender.exe", b"MZtest")
                z.writestr("VirtualCharacterRender_Data/boot.config", b"config")
            with zipfile.ZipFile(root / names[1], "w") as z:
                z.writestr("VirtualCharacterRender.app/Contents/Info.plist", b"plist")
                z.writestr("VirtualCharacterRender.app/Contents/MacOS/VirtualCharacterRender",
                           b"Mach-O test fixture")
        def write_manifests():
            claims = [{"name": n, "sha256": sha256_file(root / n)}
                      for n in names]
            metadata = {"tag": "v" + version, "source_sha": source,
                        "unity_version": "6000.3.25f1",
                        "workflow_run": "https://github.com/playCosmos/VirtualCharacterRender/actions/runs/100",
                        "assets": claims}
            (root / "build-provenance.json").write_text(
                json.dumps(metadata), encoding="utf-8")
            (root / "SHA256SUMS.txt").write_text(
                "".join(e["sha256"] + "  " + e["name"] + "\n" for e in claims),
                encoding="utf-8")
        create_archives()
        write_manifests()
        report = check_release(root, version, source)
        assert len(report["assets"]) == 2
        assert report["distributionAcceptance"] is False
        assert report["windowsAuthenticodeVerified"] is False
        assert report["macosNotarizationVerified"] is False

        def must_fail(reason):
            try:
                check_release(root, version, source)
            except (ValueError, zipfile.BadZipFile, UnicodeError):
                return
            raise AssertionError("Unsafe release passed: " + reason)

        (root / names[0]).write_bytes((root / names[0]).read_bytes() + b"corrupted")
        must_fail("tampered archive")
        create_archives()
        write_manifests()
        (root / "SHA256SUMS.txt").write_text("0" * 64 + "  " + names[0] + "\n",
                                             encoding="utf-8")
        must_fail("missing checksum row")
        write_manifests()
        with zipfile.ZipFile(root / names[0], "w") as z:
            z.writestr("../evil.exe", b"MZ")
            z.writestr("VirtualCharacterRender.exe", b"MZtest")
            z.writestr("VirtualCharacterRender_Data/boot.config", b"config")
        write_manifests()
        must_fail("archive traversal with matching checksum")
        create_archives()
        write_manifests()
        with zipfile.ZipFile(root / names[0], "w") as z:
            z.writestr("VirtualCharacterRender.exe", b"MZtest")
            z.writestr("./VirtualCharacterRender.exe", b"MZtest")
            z.writestr("VirtualCharacterRender_Data/boot.config", b"config")
        write_manifests()
        must_fail("duplicate normalized ZIP paths")
        create_archives()
        with zipfile.ZipFile(root / names[0], "w") as z:
            z.writestr("VirtualCharacterRender.exe", b"MZtest")
            z.writestr("VirtualCharacterRender_Data/boot.config", b"config")
            z.writestr("virtualcharacterrender.exe", b"MZduplicate-case")
        write_manifests()
        must_fail("case-insensitive Windows archive collision")
        create_archives()
        with zipfile.ZipFile(root / names[0], "w") as z:
            z.writestr("VirtualCharacterRender.exe", b"MZtest")
            z.writestr("VirtualCharacterRender_Data/evil:ads", b"stream")
        write_manifests()
        must_fail("Windows NTFS alternate data stream path")
        create_archives()
        write_manifests()
        metadata = json.loads((root / "build-provenance.json").read_text())
        metadata["source_sha"] = "b" * 40
        (root / "build-provenance.json").write_text(json.dumps(metadata), encoding="utf-8")
        must_fail("mismatched source SHA")
        create_archives()
        write_manifests()
        (root / names[1]).unlink()
        must_fail("missing macOS distribution")
    print("M3 archive provenance self-test PASS; signature, legal and clean-machine G/R deferred")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--version", help="Expected release version without v")
    parser.add_argument("--source", help="Expected 40-hex source commit")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.artifacts is None or args.version is None or args.source is None:
        parser.error("--artifacts, --version and --source are required")
    try:
        result = check_release(args.artifacts, args.version, args.source)
        output = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
        if args.report is not None:
            with args.report.open("x", encoding="utf-8") as report:
                report.write(output)
        print(output, end="")
        return 0
    except (OSError, ValueError, UnicodeError, json.JSONDecodeError,
            zipfile.BadZipFile, RuntimeError) as error:
        print("M3 archive preflight INVALID/INCOMPLETE: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
