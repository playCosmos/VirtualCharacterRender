#!/usr/bin/env python3
"""Launch a provenance-checked native P10 Player with GUI telemetry (never OBS PASS)."""
import argparse
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import platform
import posixpath
import subprocess
import sys
import tempfile
import zipfile


def load_verifier():
    script = Path(__file__).with_name("verify-p10-interactive-evidence.py")
    spec = importlib.util.spec_from_file_location("p10_evidence_verifier", script)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_entries(archive):
    """Reject zip-slip and symlink paths escaping the unpack root."""
    with zipfile.ZipFile(archive) as package:
        for info in package.infolist():
            name = info.filename
            while name.startswith("./"):
                name = name[2:]
            if not name:
                continue
            normalized = PurePosixPath(name)
            if (name.startswith("/") or "\\" in name or normalized.is_absolute()
                    or ".." in normalized.parts or ":" in normalized.parts[0]):
                raise ValueError("Unsafe archive member: " + info.filename)
            unix_type = (info.external_attr >> 16) & 0o170000
            if unix_type == 0o120000:
                link = package.read(info).decode("utf-8")
                if (not link or link.startswith("/") or "\\" in link):
                    raise ValueError("Unsafe ZIP symlink: " + name)
                destination = posixpath.normpath(posixpath.join(
                    posixpath.dirname(name), link))
                if destination == ".." or destination.startswith("../"):
                    raise ValueError("ZIP symlink escapes extraction: " + name)


def require_desktop(platform_name):
    if platform_name == "windows":
        if sys.platform != "win32" or platform.machine().lower() not in ("amd64", "x86_64"):
            raise ValueError("Windows x64 desktop required; cannot run on another host")
    elif sys.platform != "darwin" or platform.machine().lower() != "arm64":
        raise ValueError("Native Apple Silicon macOS desktop required")
    if platform_name == "macos" and not os.environ.get("HOME"):
        raise ValueError("A logged-in macOS desktop session is required")


def make_command(platform_name, unpacked, log_file, telemetry_file, vrm,
                 m2_performance_file=None):
    args = [
        "-screen-fullscreen", "0",
        "-logFile", str(log_file),
        "--vcr-interactive-evidence=" + str(telemetry_file),
    ]
    if vrm:
        args.append("--vcr-vrm=" + str(vrm))
    if m2_performance_file is not None:
        args.append("--vcr-m2-perf-csv=" + str(m2_performance_file))
    if platform_name == "windows":
        binary = unpacked / "VirtualCharacterRender.exe"
        if not binary.is_file():
            raise ValueError("Extracted Windows Player executable missing")
        return [str(binary), *args]
    bundle = unpacked / "VirtualCharacterRender.app"
    if not (bundle / "Contents" / "Info.plist").is_file():
        raise ValueError("Extracted macOS Player bundle missing")
    return ["open", "-W", "-n", "-a", str(bundle), "--args", *args]


def m2_file_set(performance_file):
    """The opt-in recorder always owns all three mutually bound local files."""
    return (
        performance_file,
        Path(str(performance_file) + ".session.json"),
        Path(str(performance_file) + ".events.csv"),
    )


def check_m2_outputs(performance_file):
    # Presence is a launcher sanity check, NOT acceptance of contents.
    # The separate strict M2 verifier checks clocks, shutdown markers and
    # complete VRM callbacks; OBS evidence remains independent.
    maximum_bytes = (24 * 1024 * 1024, 32 * 1024, 24 * 1024 * 1024)
    for path, limit in zip(m2_file_set(performance_file), maximum_bytes):
        if (path.is_symlink() or not path.is_file() or
                not 0 < path.stat().st_size <= limit):
            raise ValueError("M2 evidence output missing/unsafe/empty/excessive: " +
                             str(path))


def refuse_prior_evidence(label, log_file, telemetry_file,
                          m2_performance_file=None):
    paths = [log_file, telemetry_file]
    if m2_performance_file is not None:
        paths.extend(m2_file_set(m2_performance_file))
    # A dangling symlink must not bypass the preflight. Never overwrite
    # partial evidence left after a crash, including M2 lifecycle events.
    if any(path.exists() or path.is_symlink() for path in paths):
        raise ValueError("Refusing to overwrite existing session evidence for " +
                         label)


def run(args):
    verifier = load_verifier()
    if not verifier.SHA40.fullmatch(args.source):
        raise ValueError("Expected lowercase 40-character Git source SHA")
    require_desktop(args.platform)

    artifacts = args.artifacts.resolve()
    archive = artifacts / (args.platform + ".zip")
    digest = verifier.check_archive(artifacts, args.platform, args.source)
    validate_entries(archive)

    evidence = args.evidence.resolve()
    if not evidence.is_dir() or not (evidence / (args.platform + "-" + args.target + ".json")).is_file():
        raise ValueError("Create P10 templates with verify-p10-interactive-evidence.py --init first")
    label = args.platform + "-" + args.target
    attachments = evidence / "attachments"
    attachments.mkdir(exist_ok=True)
    log_file = attachments / (label + ".log")
    telemetry_file = attachments / (label + "-runtime.json")
    performance_file = attachments / (label + "-m2-performance.csv")
    refuse_prior_evidence(
        label, log_file, telemetry_file,
        performance_file if args.m2_performance else None)

    vrm = args.vrm.resolve() if args.vrm else None
    if vrm and (not vrm.is_file() or vrm.suffix.lower() != ".vrm"):
        raise ValueError("--vrm must be an existing .vrm file")
    print("Archive verified SHA-256:", digest, flush=True)
    print("Source commit:", args.source, flush=True)
    print("Target label:", args.target, "(configure resolution and FPS in the GUI)", flush=True)
    print("Player log:", log_file, flush=True)
    print("Native telemetry:", telemetry_file, flush=True)
    if args.m2_performance:
        print("Optional M2 performance CSV:", performance_file, flush=True)
        print("M2 evidence is measurement-only, NOT proof of OBS frames.", flush=True)
    print("Use OBS manually; no screenshot or transparency result is inferred.", flush=True)
    print("Close the Player normally after recording and stable sampling.", flush=True)
    with tempfile.TemporaryDirectory(prefix="vcr-p10-player-") as tmp:
        unpacked = Path(tmp)
        if args.platform == "macos":
            subprocess.run(["unzip", "-q", str(archive), "-d", str(unpacked)], check=True)
        else:
            with zipfile.ZipFile(archive) as package:
                package.extractall(unpacked)
        command = make_command(
            args.platform, unpacked, log_file, telemetry_file, vrm,
            performance_file if args.m2_performance else None)
        completed = subprocess.run(command, cwd=unpacked, check=False)
    if completed.returncode != 0:
        raise ValueError("GUI launcher/player exited nonzero: " + str(completed.returncode))
    if not log_file.is_file() or not log_file.stat().st_size:
        raise ValueError("Player log missing/empty; no GUI session evidence")
    if not telemetry_file.is_file() or not telemetry_file.stat().st_size:
        raise ValueError("Player telemetry missing/empty; no GUI session evidence")
    telemetry = json.loads(telemetry_file.read_text(encoding="utf-8"))
    expected = "WindowsPlayer" if args.platform == "windows" else "OSXPlayer"
    if (telemetry.get("suite") != "p10-interactive-runtime-telemetry-v1"
            or telemetry.get("platform") != expected
            or telemetry.get("unityVersion") != "6000.3.25f1"
            or telemetry.get("quitCallbackObserved") is not True
            or not isinstance(telemetry.get("samples"), list)
            or len(telemetry["samples"]) < 2):
        raise ValueError("Telemetry identity, normal quit or sample count is incomplete")
    if args.m2_performance:
        check_m2_outputs(performance_file)
        print("M2 CSV, session metadata and lifecycle events written; verify " +
              "contents separately with tools/verify-m2-performance.py. " +
              "No automatic model/device/OBS PASS.")
    print("Native session files recorded. GUI/OBS observations STILL REQUIRE MANUAL REVIEW.")
    print("Operator JSON must reference the log and runtime telemetry files; no PASS auto-written.")
    return 0


def self_test():
    verifier = load_verifier()
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        zip_path = root / "windows.zip"
        with zipfile.ZipFile(zip_path, "w") as package:
            package.writestr("VirtualCharacterRender.exe", b"self-test")
            package.writestr("VirtualCharacterRender_Data/boot.config", b"test")
        digest = verifier.sha256_file(zip_path)
        (root / "windows.sha256").write_text(
            digest + "  player-artifacts/windows.zip\n", encoding="utf-8")
        source = "a" * 40
        (root / "windows.source-sha").write_text(source + "\n", encoding="utf-8")
        if verifier.check_archive(root, "windows", source) != digest:
            raise AssertionError("valid artifact was rejected")
        validate_entries(zip_path)
        with zipfile.ZipFile(root / "unsafe.zip", "w") as package:
            package.writestr("../escape.txt", b"bad")
        try:
            validate_entries(root / "unsafe.zip")
        except ValueError:
            pass
        else:
            raise AssertionError("archive traversal was accepted")
        for name, kind in (("windows", "VirtualCharacterRender.exe"),
                           ("macos", "VirtualCharacterRender.app/Contents/Info.plist")):
            unpacked = root / name
            path = unpacked / kind
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("test", encoding="utf-8")
            command = make_command(name, unpacked, root / "case.log",
                                   root / "case-runtime.json", None,
                                   root / "case-m2.csv")
            assembled = " ".join(command)
            if ("--vcr-interactive-evidence=" not in assembled
                    or "-screen-fullscreen 0" not in assembled
                    or "--vcr-m2-perf-csv=" not in assembled
                    or "-batchmode" in assembled or "-nographics" in assembled):
                raise AssertionError("invalid interactive Player command: " + assembled)
        perf = root / "case-m2.csv"
        expected = m2_file_set(perf)
        player_log, native_telemetry = root / "session.log", root / "session-runtime.json"
        for prior_file in (player_log, native_telemetry, *expected):
            prior_file.write_text("previous evidence", encoding="utf-8")
            try:
                refuse_prior_evidence("windows-720p60", player_log,
                                      native_telemetry, perf)
            except ValueError:
                pass
            else:
                raise AssertionError("Refusing old M2 evidence failed: " + str(prior_file))
            prior_file.unlink()
        refuse_prior_evidence("windows-720p60", player_log, native_telemetry, perf)
        for path in expected:
            path.write_text("self-test\n", encoding="utf-8")
        check_m2_outputs(perf)
        for path in expected:
            path.unlink()
            try:
                check_m2_outputs(perf)
            except ValueError:
                pass
            else:
                raise AssertionError("Missing M2 sidecar incorrectly accepted: " + str(path))
            path.write_text("self-test\\n", encoding="utf-8")
        expected[-1].write_bytes(b"")
        try:
            check_m2_outputs(perf)
        except ValueError:
            pass
        else:
            raise AssertionError("Empty M2 events CSV incorrectly accepted")
    print("P10 desktop Player runner self-test PASS (no GUI/OBS claim)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--platform", choices=("windows", "macos"))
    parser.add_argument("--target", choices=("720p60", "1080p60"))
    parser.add_argument("--source")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--vrm", type=Path, help="Optional private model, never uploaded")
    parser.add_argument(
        "--m2-performance", action="store_true",
        help="Record optional real-desktop frame/memory CSV with no automatic OBS pass")
    options = parser.parse_args()
    try:
        if options.self_test:
            self_test()
            return 0
        missing = [key for key in ("platform", "target", "source", "artifacts", "evidence")
                   if getattr(options, key) is None]
        if missing:
            parser.error("missing required arguments: " + ", ".join(missing))
        return run(options)
    except (OSError, ValueError, subprocess.SubprocessError, zipfile.BadZipFile,
            UnicodeError, json.JSONDecodeError) as error:
        print("P10 GUI runner FAILED/INCOMPLETE: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
