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


def make_command(platform_name, unpacked, log_file, telemetry_file, vrm):
    args = [
        "-screen-fullscreen", "0",
        "-logFile", str(log_file),
        "--vcr-interactive-evidence=" + str(telemetry_file),
    ]
    if vrm:
        args.append("--vcr-vrm=" + str(vrm))
    if platform_name == "windows":
        binary = unpacked / "VirtualCharacterRender.exe"
        if not binary.is_file():
            raise ValueError("Extracted Windows Player executable missing")
        return [str(binary), *args]
    bundle = unpacked / "VirtualCharacterRender.app"
    if not (bundle / "Contents" / "Info.plist").is_file():
        raise ValueError("Extracted macOS Player bundle missing")
    return ["open", "-W", "-n", "-a", str(bundle), "--args", *args]


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
    if log_file.exists() or telemetry_file.exists():
        raise ValueError("Refusing to overwrite existing session evidence for " + label)

    vrm = args.vrm.resolve() if args.vrm else None
    if vrm and (not vrm.is_file() or vrm.suffix.lower() != ".vrm"):
        raise ValueError("--vrm must be an existing .vrm file")
    print("Archive verified SHA-256:", digest, flush=True)
    print("Source commit:", args.source, flush=True)
    print("Target label:", args.target, "(configure resolution and FPS in the GUI)", flush=True)
    print("Player log:", log_file, flush=True)
    print("Native telemetry:", telemetry_file, flush=True)
    print("Use OBS manually; no screenshot or transparency result is inferred.", flush=True)
    print("Close the Player normally after recording and stable sampling.", flush=True)
    with tempfile.TemporaryDirectory(prefix="vcr-p10-player-") as tmp:
        unpacked = Path(tmp)
        if args.platform == "macos":
            subprocess.run(["unzip", "-q", str(archive), "-d", str(unpacked)], check=True)
        else:
            with zipfile.ZipFile(archive) as package:
                package.extractall(unpacked)
        command = make_command(args.platform, unpacked, log_file, telemetry_file, vrm)
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
                                   root / "case-runtime.json", None)
            assembled = " ".join(command)
            if ("--vcr-interactive-evidence=" not in assembled
                    or "-screen-fullscreen 0" not in assembled
                    or "-batchmode" in assembled or "-nographics" in assembled):
                raise AssertionError("invalid interactive Player command: " + assembled)
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
