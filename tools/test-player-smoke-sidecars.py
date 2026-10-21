#!/usr/bin/env python3
"""Regression test for release/native sidecar naming in run-player-smoke.py."""
import importlib.util
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "run_player_smoke", ROOT / "tools/run-player-smoke.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def write(path, text):
    path.write_text(text, encoding="utf-8")


def expect(path, expected):
    actual = MODULE.sidecar(path, expected.suffix)
    if actual != expected:
        raise AssertionError(f"Expected {expected}, got {actual}")


def main():
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)

        release = root / "release"
        release.mkdir()
        archive = release / "VirtualCharacterRender-0.1.0-alpha.4-Windows-x64.zip"
        write(archive, "zip")
        digest = release / (archive.name + ".sha256")
        source = release / (archive.name + ".source-sha")
        write(digest, "a" * 64 + "  " + archive.name + "\n")
        write(source, "b" * 40 + "\n")
        expect(archive, digest)
        expect(archive, source)

        native = root / "native"
        native.mkdir()
        native_archive = native / "windows.zip"
        write(native_archive, "zip")
        native_digest = native / "windows.sha256"
        native_source = native / "windows.source-sha"
        write(native_digest, "c" * 64 + "  windows.zip\n")
        write(native_source, "d" * 40 + "\n")
        expect(native_archive, native_digest)
        expect(native_archive, native_source)

        missing = root / "missing"
        missing.mkdir()
        missing_archive = missing / "missing.zip"
        write(missing_archive, "zip")
        try:
            MODULE.sidecar(missing_archive, ".sha256")
        except RuntimeError:
            pass
        else:
            raise AssertionError("A missing sidecar must fail")

    print("Player smoke sidecar resolution: PASS")


if __name__ == "__main__":
    main()
