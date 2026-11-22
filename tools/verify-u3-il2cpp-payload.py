#!/usr/bin/env python3
"""Verify U3 Alpha/P11 Windows IL2CPP archives match every built byte."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

NEEDED = (
    "VirtualCharacterRender.exe",
    "UnityPlayer.dll",
    "GameAssembly.dll",
    "VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat",
)


def sha256_stream(stream):
    value = hashlib.sha256()
    for part in iter(lambda: stream.read(1024 * 1024), b""):
        value.update(part)
    return value.hexdigest()


def verify(root, artifacts, source):
    if len(source) != 40 or any(c not in "0123456789abcdef" for c in source):
        raise ValueError("Expected exact 40-hex source SHA")
    for variant in ("Alpha", "P11"):
        directory = root / variant / "Windows-IL2CPP"
        bundle = artifacts / variant
        archive = bundle / "windows.zip"
        manifest = json.loads(
            (bundle / "windows.payload-sha256.json").read_text(encoding="utf-8"))
        if (manifest.get("format") != "vcr-u3-il2cpp-payload-v1" or
                manifest.get("variant") != variant or
                manifest.get("sourceSha") != source):
            raise ValueError(variant + ": manifest metadata mismatch")
        entries = manifest.get("files")
        if not isinstance(entries, list) or not entries:
            raise ValueError(variant + ": empty payload manifest")
        if (bundle / "windows.source-sha").read_text(encoding="utf-8").strip() != source:
            raise ValueError(variant + ": source SHA mismatch")
        recorded = (bundle / "windows.sha256").read_text(
            encoding="utf-8").split()
        if len(recorded) != 2 or recorded[1] != "windows.zip":
            raise ValueError(variant + ": invalid archive digest sidecar")
        with archive.open("rb") as stream:
            archive_sha256 = sha256_stream(stream)
        if recorded[0].lower() != archive_sha256:
            raise ValueError(variant + ": archive_sha256 mismatch")
        actual = sorted(p for p in directory.rglob("*") if p.is_file())
        names = sorted(p.relative_to(directory).as_posix() for p in actual)
        if len(actual) != len(entries) or not all(name in names for name in NEEDED):
            raise ValueError(variant + ": missing IL2CPP component or payload file")
        if (directory / "MonoBleedingEdge").exists():
            raise ValueError(variant + ": unexpected Mono runtime")
        manifest_names = [entry.get("path") for entry in entries]
        # Windows Path orders names case-insensitively while ZIP and POSIX
        # inventory sorting use case-sensitive strings; compare sets of
        # exact spellings, not platform-dependent enumeration order.
        if (len(manifest_names) != len(set(manifest_names)) or
                sorted(manifest_names) != names):
            raise ValueError(variant + ": manifest path inventory mismatch")
        with ZipFile(archive) as zipfile:
            zipped = zipfile.namelist()
            if sorted(zipped) != names or len(zipped) != len(set(zipped)):
                raise ValueError(variant + ": archive differs from build directory")
            for entry in entries:
                path = entry["path"]
                file = directory / path
                if entry.get("bytes") != file.stat().st_size:
                    raise ValueError(variant + ": file size mismatch " + path)
                with file.open("rb") as stream:
                    if sha256_stream(stream) != entry["sha256"]:
                        raise ValueError(variant + ": build-file hash mismatch " + path)
                with zipfile.open(entry["path"]) as packed:
                    if sha256_stream(packed) != entry["sha256"]:
                        raise ValueError(variant + ": zip-file hash mismatch " + path)
        print(variant + ": exact-source Windows IL2CPP payload verified: " +
              str(len(entries)) + " files, ZIP SHA-256=" + archive_sha256)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--source", required=True)
    args = parser.parse_args()
    verify(args.root, args.artifacts, args.source)


if __name__ == "__main__":
    main()
