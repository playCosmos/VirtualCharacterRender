#!/usr/bin/env python3
"""Choose the newest source-verified P1 macOS Player artifact on a workflow retry.

GitHub Actions re-runs may execute only a cancelled smoke job. A Player built
during run_attempt=1 is then the correct input for run_attempt=2; requesting
only the attempt-2 artifact would fail even though the original build passed.
Artifacts are not mixed across attempts. A present but invalid newest artifact
is rejected rather than silently falling back to older bytes.
"""
import argparse
import hashlib
import os
import shutil
import tempfile
from pathlib import Path


EXPECTED_FILES = ("macos.zip", "macos.sha256", "macos.source-sha", "build-evidence.txt")


def select_player(downloads: Path, output: Path, run_id: str,
                  attempt: int, source: str) -> int:
    if not run_id.isdecimal() or attempt < 1:
        raise ValueError("Invalid GitHub run id or run attempt")
    if len(source) != 40 or any(c not in "0123456789abcdef" for c in source):
        raise ValueError("Expected full, lowercase 40-character source SHA")
    prefix = f"u1-p1-macos-player-{run_id}-"
    candidates = []
    if not downloads.is_dir():
        raise ValueError("Downloaded Player artifact directory is missing")
    for directory in downloads.iterdir():
        if not directory.is_dir() or not directory.name.startswith(prefix):
            continue
        suffix = directory.name[len(prefix):]
        if suffix.isdecimal() and 1 <= int(suffix) <= attempt:
            candidates.append((int(suffix), directory))
    if not candidates:
        raise ValueError("No Player artifact exists for this workflow run/attempt")
    selected_attempt, chosen = max(candidates, key=lambda candidate: candidate[0])

    for filename in EXPECTED_FILES:
        path = chosen / filename
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Missing or unsafe Player artifact file: {filename}")
    declared_source = (chosen / "macos.source-sha").read_text(encoding="utf-8").strip()
    if declared_source != source:
        raise ValueError("Player artifact source SHA differs from workflow source")
    checksums = (chosen / "macos.sha256").read_text(encoding="utf-8").splitlines()
    expected_hash = checksums[0].split("  ", 1) if len(checksums) == 1 else []
    if (len(expected_hash) != 2 or expected_hash[1] != "macos.zip" or
            len(expected_hash[0]) != 64 or
            any(c not in "0123456789abcdef" for c in expected_hash[0])):
        raise ValueError("Invalid Player archive checksum sidecar")
    digest = hashlib.sha256()
    with (chosen / "macos.zip").open("rb") as binary:
        for block in iter(lambda: binary.read(1024 * 1024), b""):
            digest.update(block)
    if digest.hexdigest() != expected_hash[0]:
        raise ValueError("Player archive SHA-256 mismatch")
    evidence = (chosen / "build-evidence.txt").read_text(encoding="utf-8")
    if (f"source_sha={source}\n" not in evidence or
            f"archive_sha256={expected_hash[0]}\n" not in evidence or
            "scene=P1/U1\n" not in evidence or
            "platform=macOS\n" not in evidence or
            "mode=nondevelopment\n" not in evidence or
            "backend=Mono\n" not in evidence):
        raise ValueError("Player build evidence does not match required P1 source")

    output.mkdir(parents=True, exist_ok=True)
    if any((output / filename).exists() for filename in EXPECTED_FILES):
        raise ValueError("Refusing to overwrite a previously selected Player artifact")
    for filename in EXPECTED_FILES:
        shutil.copyfile(chosen / filename, output / filename)
    print(f"Selected verified P1 Player from run {run_id} attempt "
          f"{selected_attempt} for smoke attempt {attempt}: {digest.hexdigest()}")
    return selected_attempt


def self_test() -> None:
    run_id = "1234567"
    source = "a" * 40
    content = b"real source-bound player archive"
    digest = hashlib.sha256(content).hexdigest()
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        downloads = root / "downloaded"
        downloads.mkdir()

        def artifact(attempt: int, *, sha=source, data=content, hash_override=None):
            folder = downloads / f"u1-p1-macos-player-{run_id}-{attempt}"
            folder.mkdir()
            (folder / "macos.zip").write_bytes(data)
            (folder / "macos.source-sha").write_text(sha + "\n", encoding="utf-8")
            (folder / "macos.sha256").write_text(
                (hash_override or hashlib.sha256(data).hexdigest()) +
                "  macos.zip\n", encoding="utf-8")
            (folder / "build-evidence.txt").write_text(
                "scene=P1/U1\nplatform=macOS\nmode=nondevelopment\n"
                "backend=Mono\nunity=6000.3.25f1\n"
                f"source_sha={sha}\narchive_sha256={hash_override or hashlib.sha256(data).hexdigest()}\n",
                encoding="utf-8")
            return folder

        artifact(1)
        # Crucial retry: no attempt-2 upload exists, so attempt 1 is correct.
        assert select_player(downloads, root / "out1", run_id, 2, source) == 1
        assert (root / "out1/macos.zip").read_bytes() == content
        artifact(2)
        assert select_player(downloads, root / "out2", run_id, 2, source) == 2

        def rejected(label, fn):
            try:
                fn()
            except ValueError:
                return
            raise AssertionError("Invalid artifact accepted: " + label)

        attempt2 = downloads / f"u1-p1-macos-player-{run_id}-2"
        (attempt2 / "macos.zip").write_bytes(b"corrupt")
        rejected("archive mismatch", lambda: select_player(
            downloads, root / "out3", run_id, 2, source))
        # Do not silently fall back to attempt-1 when attempt-2 is corrupt.
        (attempt2 / "macos.zip").write_bytes(content)
        (attempt2 / "macos.source-sha").write_text("b" * 40 + "\n", encoding="utf-8")
        rejected("wrong source SHA", lambda: select_player(
            downloads, root / "out4", run_id, 2, source))
        rejected("no matching attempt", lambda: select_player(
            downloads, root / "out5", run_id, 0, source))
        rejected("wrong run id", lambda: select_player(
            downloads, root / "out6", "999", 2, source))
        # Correct earlier attempt remains usable when the newer is outside
        # the current attempt's range.
        assert select_player(downloads, root / "out7", run_id, 1, source) == 1
    print("P1 macOS retry artifact selection: PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--downloads", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--run-id")
    parser.add_argument("--attempt", type=int)
    parser.add_argument("--source")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if (args.downloads is None or args.output is None or args.run_id is None or
            args.attempt is None or args.source is None):
        parser.error("Selection requires downloads, output, run-id, attempt, source")
    select_player(args.downloads, args.output, args.run_id, args.attempt, args.source)


if __name__ == "__main__":
    main()
