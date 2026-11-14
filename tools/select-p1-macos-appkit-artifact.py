#!/usr/bin/env python3
"""Source-bound AppKit plugin artifact selection for macOS build-only retries.

An isolated rerun of build-p1-macos has a new GITHUB_RUN_ATTEMPT but the
successful AppKit compile may have uploaded only the older attempt's artifact.
Reject corrupt newer attempts instead of silently using old plugin bytes.
"""
import argparse
import hashlib
import json
import shutil
import tempfile
from pathlib import Path

BINARY = "VcrSettingsWindow.dylib"
META = BINARY + ".meta"
MANIFEST = "appkit-provenance.json"
FILES = (BINARY, META, MANIFEST)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def check_source(source: str) -> None:
    if len(source) != 40 or any(c not in "0123456789abcdef" for c in source):
        raise ValueError("Expected exact lowercase 40-character source SHA")


def require_file(path: Path) -> None:
    if not path.is_file() or path.is_symlink() or path.stat().st_size == 0:
        raise ValueError("Missing, empty, or unsafe AppKit artifact: " + str(path))


def package(binary: Path, meta: Path, output: Path, source: str) -> None:
    check_source(source)
    require_file(binary)
    require_file(meta)
    if output.exists() and any(output.iterdir()):
        raise ValueError("Refusing to overwrite AppKit package")
    output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(binary, output / BINARY)
    shutil.copyfile(meta, output / META)
    evidence = {
        "source_sha": source,
        "platform": "macOS",
        "architecture": "arm64",
        "binary_sha256": sha256(output / BINARY),
        "meta_sha256": sha256(output / META),
    }
    (output / MANIFEST).write_text(
        json.dumps(evidence, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print("AppKit source-bound artifact packaged: " + evidence["binary_sha256"])


def select(downloads: Path, output: Path, run_id: str, attempt: int,
           source: str, expected_meta: Path) -> int:
    check_source(source)
    if not run_id.isdecimal() or attempt < 1:
        raise ValueError("Invalid GitHub run id/attempt")
    require_file(expected_meta)
    if not downloads.is_dir():
        raise ValueError("AppKit downloads directory is missing")
    prefix = "p1-macos-appkit-" + run_id + "-"
    candidates = []
    for path in downloads.iterdir():
        if not path.is_dir() or path.is_symlink() or not path.name.startswith(prefix):
            continue
        suffix = path.name[len(prefix):]
        if suffix.isdecimal() and 1 <= int(suffix) <= attempt:
            candidates.append((int(suffix), path))
    if not candidates:
        raise ValueError("No AppKit artifact for this run/attempt")
    producer_attempt, chosen = max(candidates, key=lambda pair: pair[0])
    for name in FILES:
        require_file(chosen / name)
    if set(p.name for p in chosen.iterdir()) != set(FILES):
        raise ValueError("Unexpected files in AppKit artifact")
    try:
        provenance = json.loads((chosen / MANIFEST).read_text(encoding="utf-8"))
    except (ValueError, UnicodeError) as exc:
        raise ValueError("Invalid AppKit provenance") from exc
    expected = {
        "source_sha": source,
        "platform": "macOS",
        "architecture": "arm64",
        "binary_sha256": sha256(chosen / BINARY),
        "meta_sha256": sha256(chosen / META),
    }
    if provenance != expected:
        raise ValueError("AppKit source, platform, or SHA-256 provenance mismatch")
    if (chosen / META).read_bytes() != expected_meta.read_bytes():
        raise ValueError("AppKit Unity importer differs from source repository")
    if output.exists() and any((output / name).exists() for name in (BINARY, META)):
        raise ValueError("Refusing to overwrite an existing Unity AppKit plugin")
    output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(chosen / BINARY, output / BINARY)
    shutil.copyfile(chosen / META, output / META)
    print("Selected verified AppKit from run " + run_id + " producer attempt " +
          str(producer_attempt) + " for build attempt " + str(attempt) +
          ": " + expected["binary_sha256"])
    return producer_attempt


def self_test() -> None:
    source = "a" * 40
    run_id = "12345"
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        binary = root / "compiled.dylib"
        meta = root / "source.meta"
        binary.write_bytes(b"arm64 AppKit fixture binary")
        meta.write_bytes(b"fileFormatVersion: 2\nguid: test\n")
        downloads = root / "downloads"
        downloads.mkdir()
        older = downloads / ("p1-macos-appkit-" + run_id + "-1")
        package(binary, meta, older, source)

        # Build-only retry: no attempt-2 artifact, reuse exact attempt 1.
        assert select(downloads, root / "unity1", run_id, 2, source, meta) == 1
        assert (root / "unity1" / BINARY).read_bytes() == binary.read_bytes()

        newer = downloads / ("p1-macos-appkit-" + run_id + "-2")
        package(binary, meta, newer, source)
        assert select(downloads, root / "unity2", run_id, 2, source, meta) == 2

        def rejected(label, operation):
            try:
                operation()
            except ValueError:
                return
            raise AssertionError("Invalid AppKit artifact accepted: " + label)

        (newer / BINARY).write_bytes(b"corrupt")
        rejected("newest corrupt, must not fall back", lambda: select(
            downloads, root / "reject1", run_id, 2, source, meta))
        (newer / BINARY).write_bytes(binary.read_bytes())
        rejected("wrong source", lambda: select(
            downloads, root / "reject2", run_id, 2, "b" * 40, meta))
        meta.write_bytes(b"different Unity importer")
        rejected("wrong meta", lambda: select(
            downloads, root / "reject3", run_id, 2, source, meta))
        meta.write_bytes((older / META).read_bytes())
        rejected("wrong run", lambda: select(
            downloads, root / "reject4", "6789", 2, source, meta))
        rejected("invalid attempt", lambda: select(
            downloads, root / "reject5", run_id, 0, source, meta))
        rejected("existing output", lambda: select(
            downloads, root / "unity1", run_id, 1, source, meta))
        (newer / MANIFEST).write_text("{}", encoding="utf-8")
        rejected("manifest corruption", lambda: select(
            downloads, root / "reject6", run_id, 2, source, meta))
        assert select(downloads, root / "unity3", run_id, 1, source, meta) == 1
    print("P1 macOS AppKit build-only retry selection: PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--package", action="store_true")
    mode.add_argument("--select", action="store_true")
    mode.add_argument("--self-test", action="store_true")
    parser.add_argument("--source")
    parser.add_argument("--binary", type=Path)
    parser.add_argument("--meta", type=Path)
    parser.add_argument("--expected-meta", type=Path)
    parser.add_argument("--downloads", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--run-id")
    parser.add_argument("--attempt", type=int)
    args = parser.parse_args()
    if args.self_test:
        self_test()
    elif args.package:
        if not all((args.source, args.binary, args.meta, args.output)):
            parser.error("Packaging requires source, binary, meta and output")
        package(args.binary, args.meta, args.output, args.source)
    else:
        if not all((args.source, args.expected_meta, args.downloads, args.output,
                    args.run_id, args.attempt)):
            parser.error("Selection requires source, expected-meta, downloads, output, run-id and attempt")
        select(args.downloads, args.output, args.run_id, args.attempt,
               args.source, args.expected_meta)


if __name__ == "__main__":
    main()
