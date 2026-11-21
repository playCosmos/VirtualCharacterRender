#!/usr/bin/env python3
"""Verify a P1 IL2CPP ZIP, source sidecar and complete file manifest.

This tests the bytes *inside the packaged ZIP*, not only the source build
directory. The caller must independently trust the expected Git commit SHA
and the origin of the downloaded artifact. No extraction or network access.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import stat
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile

SHA40 = re.compile(r"[0-9a-fA-F]{40}\Z")
SHA64 = re.compile(r"[0-9a-fA-F]{64}\Z")
WINDOWS_RESERVED = re.compile(r"(?i)(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?\Z")
MAX_FILES = 100_000
MAX_BYTES = 16 * 1024**3
MAX_MANIFEST = 32 * 1024**2
REQUIRED = {
    "VirtualCharacterRender.exe",
    "UnityPlayer.dll",
    "GameAssembly.dll",
    "VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def hash_stream(handle):
    digest = hashlib.sha256()
    for chunk in iter(lambda: handle.read(1024 * 1024), b""):
        digest.update(chunk)
    return digest.hexdigest()


def hash_file(path):
    with path.open("rb") as handle:
        return hash_stream(handle)


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate manifest key: " + key)
        result[key] = value
    return result


def validate_path(value):
    require(isinstance(value, str) and value and not value.startswith("/")
            and "\\" not in value and ":" not in value
            and not any(ord(c) < 32 for c in value),
            "unsafe payload path")
    components = value.split("/")
    require(all(c and c not in (".", "..") and c == c.rstrip(". ")
                and not WINDOWS_RESERVED.fullmatch(c) for c in components),
            "unsafe Windows payload component: " + value)
    return value


def verify(artifact_dir, expected_source):
    require(isinstance(expected_source, str) and SHA40.fullmatch(expected_source),
            "source SHA must contain 40 hex digits")
    root = Path(artifact_dir)
    archive_path = root / "windows.zip"
    manifest_path = root / "windows.payload-sha256.json"
    sha_path = root / "windows.sha256"
    source_path = root / "windows.source-sha"
    for path in (archive_path, manifest_path, sha_path, source_path):
        require(path.is_file() and not path.is_symlink() and path.stat().st_size > 0,
                "missing or unsafe P1 artifact: " + path.name)
    require(manifest_path.stat().st_size <= MAX_MANIFEST,
            "P1 payload manifest exceeds size limit")

    source = source_path.read_text(encoding="utf-8").strip()
    require(SHA40.fullmatch(source) and source.lower() == expected_source.lower(),
            "P1 source sidecar mismatches expected commit")
    declared_sha = sha_path.read_text(encoding="utf-8").strip()
    match = re.fullmatch(r"([0-9a-fA-F]{64})  windows\.zip", declared_sha)
    require(match is not None, "invalid P1 ZIP SHA-256 sidecar format")
    actual_zip_sha = hash_file(archive_path)
    require(actual_zip_sha == match.group(1).lower(),
            "P1 ZIP SHA-256 sidecar mismatch")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"),
                          object_pairs_hook=unique_keys)
    require(isinstance(manifest, dict) and
            manifest.get("format") == "vcr-p1-payload-sha256-v1" and
            manifest.get("sourceSha") == source and
            isinstance(manifest.get("files"), list) and
            0 < len(manifest["files"]) <= MAX_FILES,
            "invalid P1 payload manifest/source")
    expected = {}
    seen_folded = set()
    total_size = 0
    for item in manifest["files"]:
        require(isinstance(item, dict) and set(item) == {"path", "size", "sha256"},
                "invalid P1 payload manifest file record")
        path = validate_path(item["path"])
        require(path.casefold() not in seen_folded,
                "duplicate or case-colliding P1 payload path: " + path)
        seen_folded.add(path.casefold())
        size = item["size"]
        digest = item["sha256"]
        require(type(size) is int and size >= 0 and
                isinstance(digest, str) and SHA64.fullmatch(digest),
                "invalid P1 payload size/hash: " + path)
        total_size += size
        require(total_size <= MAX_BYTES, "P1 payload uncompressed size too large")
        expected[path] = (size, digest.lower())
    require(REQUIRED <= set(expected), "P1 IL2CPP required binaries absent from manifest")
    require("MonoBleedingEdge" not in {p.split("/")[0] for p in expected},
            "MonoBleedingEdge present in IL2CPP manifest")

    with ZipFile(archive_path) as archive:
        infos = archive.infolist()
        require(len(infos) == len(expected), "P1 ZIP/manifest file count differs")
        actual_names = set()
        for info in infos:
            name = validate_path(info.filename)
            require(not info.is_dir(), "directory ZIP member not in P1 contract")
            require(not stat.S_ISLNK(info.external_attr >> 16),
                    "P1 archive contains a symbolic link")
            require(name not in actual_names and name in expected,
                    "unexpected or duplicate P1 ZIP member: " + name)
            actual_names.add(name)
            size, digest = expected[name]
            require(info.file_size == size,
                    "P1 ZIP member size mismatch: " + name)
            with archive.open(info) as handle:
                require(hash_stream(handle) == digest,
                        "P1 ZIP member SHA-256 mismatch: " + name)
        require(actual_names == set(expected), "P1 ZIP is missing manifest files")

    return {"sourceSha": source, "archiveSha256": actual_zip_sha,
            "manifestSha256": hash_file(manifest_path),
            "fileCount": len(expected), "uncompressedBytes": total_size}


def self_test():
    source = "a" * 40

    def make(root, zipped=None, manifest_files=None):
        data = zipped if zipped is not None else {
            "VirtualCharacterRender.exe": b"mock exe",
            "UnityPlayer.dll": b"mock unity",
            "GameAssembly.dll": b"mock assembly",
            "VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat": b"mock metadata",
        }
        file_records = manifest_files if manifest_files is not None else [
            {"path": path, "size": len(content),
             "sha256": hashlib.sha256(content).hexdigest()}
            for path, content in sorted(data.items())]
        with ZipFile(root / "windows.zip", "w", ZIP_DEFLATED) as archive:
            for path, content in data.items():
                archive.writestr(path, content)
        (root / "windows.sha256").write_text(
            hash_file(root / "windows.zip") + "  windows.zip\n", encoding="utf-8")
        (root / "windows.source-sha").write_text(source + "\n", encoding="utf-8")
        (root / "windows.payload-sha256.json").write_text(
            json.dumps({"format": "vcr-p1-payload-sha256-v1",
                        "sourceSha": source, "files": file_records}) + "\n",
            encoding="utf-8")
        return data, file_records

    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        data, files = make(root)
        require(verify(root, source)["fileCount"] == 4,
                "positive P1 archive verifier self-test failed")

        def rejected(label, action):
            make(root)
            action()
            try:
                verify(root, source)
            except (ValueError, OSError, json.JSONDecodeError):
                return
            raise AssertionError("verifier allowed " + label)

        rejected("wrong expected source", lambda: (root / "windows.source-sha").write_text(
            "b" * 40 + "\n", encoding="utf-8"))
        rejected("bad ZIP digest", lambda: (root / "windows.sha256").write_text(
            "0" * 64 + "  windows.zip\n", encoding="utf-8"))

        def tamper_member():
            broken = dict(data)
            broken["GameAssembly.dll"] = b"altered dll"
            make(root, zipped=broken, manifest_files=files)
        rejected("tampered DLL with refreshed ZIP digest", tamper_member)

        def extra_member():
            broken = dict(data)
            broken["unexpected.txt"] = b"extra"
            make(root, zipped=broken, manifest_files=files)
        rejected("untracked extra archive member", extra_member)

        def traversal_member():
            broken = dict(data)
            broken["../outside.txt"] = b"unsafe"
            make(root, zipped=broken)
        rejected("archive path traversal", traversal_member)

        def duplicate_manifest():
            p = root / "windows.payload-sha256.json"
            content = p.read_text(encoding="utf-8")
            p.write_text(content.replace('"format":', '"format": "bad", "format":', 1),
                         encoding="utf-8")
        rejected("duplicate JSON manifest key", duplicate_manifest)
    print("P1 IL2CPP archive verifier self-test: PASS (synthetic ZIPs, not a real Player)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-dir", type=Path)
    parser.add_argument("--source")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.artifact_dir or not args.source:
        parser.error("--artifact-dir and --source are required")
    result = verify(args.artifact_dir, args.source)
    print("P1 IL2CPP complete ZIP/manifest verification PASS:", json.dumps(result))


if __name__ == "__main__":
    main()
