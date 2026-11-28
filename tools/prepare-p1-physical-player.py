#!/usr/bin/env python3
"""Prepare the verified CI-built Windows P1 IL2CPP Player for physical testing.

Input: extracted GitHub Actions artifact directory containing windows.zip,
windows.sha256, windows.source-sha, and windows.payload-sha256.json.
Output: a NEW directory containing only the verified extracted Player files.

This does NOT download an artifact, authenticate its publisher, run the app,
or claim that an operator has accepted the UI. Verify the expected Git commit
independently against the selected successful GitHub Actions run.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import zipfile


def load_verifier():
    path = Path(__file__).with_name("verify-p1-il2cpp-archive.py")
    spec = importlib.util.spec_from_file_location("p1_archive_verifier", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prepare(artifact_dir, expected_source, output):
    verifier = load_verifier()
    artifacts = Path(artifact_dir).expanduser().resolve()
    destination = Path(output).expanduser().resolve()
    if destination.exists():
        raise ValueError("Refusing to overwrite existing Player directory: " + str(destination))
    if destination == artifacts or artifacts in destination.parents:
        raise ValueError("Player output must be outside the downloaded artifact directory")
    # Check archive ZIP digest, manifest, exact member hashes and source claim
    # before creating an output directory.
    receipt = verifier.verify(artifacts, expected_source)
    manifest = json.loads(
        (artifacts / "windows.payload-sha256.json").read_text(encoding="utf-8"))
    expected = {item["path"]: item["sha256"].lower() for item in manifest["files"]}
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".vcr-p1-verify-", dir=destination.parent) as temp:
        stage = Path(temp) / "Player"
        stage.mkdir()
        with zipfile.ZipFile(artifacts / "windows.zip") as archive:
            # Verifier rejects unsafe, colliding, duplicate or extra paths.
            # Re-hash *during extraction* to guard against a changed source
            # ZIP between verification and copying.
            for info in archive.infolist():
                dest = stage.joinpath(*info.filename.split("/"))
                dest.parent.mkdir(parents=True, exist_ok=True)
                digest = hashlib.sha256()
                with archive.open(info) as source, dest.open("xb") as target:
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        target.write(chunk)
                        digest.update(chunk)
                if digest.hexdigest() != expected[info.filename]:
                    raise ValueError("P1 ZIP changed during extraction: " + info.filename)
        if destination.exists():
            raise ValueError("Refusing to replace Player directory created during verification")
        stage.rename(destination)
    return receipt


def self_test():
    v = load_verifier()
    with tempfile.TemporaryDirectory() as root_text:
        root = Path(root_text)
        artifacts = root / "GitHub artifact"
        artifacts.mkdir()
        source = "a" * 40
        members = {
            "VirtualCharacterRender.exe": b"synthetic exe",
            "UnityPlayer.dll": b"synthetic unity dll",
            "GameAssembly.dll": b"synthetic assembly",
            "VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat": b"synthetic metadata",
        }
        with zipfile.ZipFile(artifacts / "windows.zip", "w", zipfile.ZIP_DEFLATED) as archive:
            for name, data in members.items():
                archive.writestr(name, data)
        manifest = {
            "format": "vcr-p1-payload-sha256-v1",
            "sourceSha": source,
            "files": [{"path": name, "size": len(data),
                       "sha256": hashlib.sha256(data).hexdigest()}
                      for name, data in members.items()],
        }
        (artifacts / "windows.payload-sha256.json").write_text(
            json.dumps(manifest), encoding="utf-8")
        (artifacts / "windows.source-sha").write_text(source + "\n", encoding="utf-8")
        zip_digest = v.hash_file(artifacts / "windows.zip")
        (artifacts / "windows.sha256").write_text(
            zip_digest + "  windows.zip\n", encoding="utf-8")

        output = root / "Player directory"
        try:
            prepare(artifacts, "b" * 40, output)
        except ValueError:
            pass
        else:
            raise AssertionError("Wrong source SHA allowed")
        if output.exists():
            raise AssertionError("Rejected artifact created output")
        result = prepare(artifacts, source, output)
        if result["fileCount"] != len(members):
            raise AssertionError("File inventory count mismatch")
        for name, data in members.items():
            if (output / name).read_bytes() != data:
                raise AssertionError("Extracted Player file differs: " + name)
        if (output / "windows.payload-sha256.json").exists():
            raise AssertionError("Manifest must not contaminate extracted Player directory")
        try:
            prepare(artifacts, source, output)
        except ValueError:
            pass
        else:
            raise AssertionError("Existing Player output overwritten")
        shutil.rmtree(output)
        (artifacts / "windows.sha256").write_text(
            "0" * 64 + "  windows.zip\n", encoding="utf-8")
        try:
            prepare(artifacts, source, output)
        except ValueError:
            pass
        else:
            raise AssertionError("Changed archive digest accepted")
        if output.exists():
            raise AssertionError("Failed verification created output")
    print("P1 physical Player extraction self-test: PASS (synthetic artifacts only)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-dir", type=Path)
    parser.add_argument("--source")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.artifact_dir or not args.source or not args.output:
        parser.error("--artifact-dir, --source and --output are required")
    result = prepare(args.artifact_dir, args.source, args.output)
    print("Verified P1 IL2CPP Player extracted:", args.output)
    print("Source:", result["sourceSha"])
    print("Files:", result["fileCount"], "ZIP SHA-256:", result["archiveSha256"])
    print("Use run-p1-player.ps1 with -Mode IL2CPP, -PlayerDirectory,")
    print("-ManifestFile pointing to the UNEXTRACTED windows.payload-sha256.json,")
    print("-ExpectedSourceSha and a fresh -EvidenceDirectory.")
    print("No user acceptance, publisher authenticity or real UI test is asserted.")


if __name__ == "__main__":
    main()
