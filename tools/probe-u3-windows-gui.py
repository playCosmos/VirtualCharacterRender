#!/usr/bin/env python3
"""Source-bound real Alpha/P11 Windows GUI observation, never hardware acceptance.

Use the established U1 HWND/Unity measurement primitives, not P1's
feature-search/dock workflow, to inspect independently built U3 Players.
Every success bit is tied to an actual Player PID and its own Player.log.
"""
import argparse
import datetime as dt
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import zipfile


def load_sibling(name, filename):
    spec = importlib.util.spec_from_file_location(
        name, Path(__file__).resolve().with_name(filename))
    if spec is None or spec.loader is None:
        raise RuntimeError("Missing reusable native GUI/smoke verifier: " + filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


native = load_sibling("vcr_u3_native_archive", "run-player-smoke.py")
gui = load_sibling("vcr_u3_gui_core", "probe-u1-windows-gui.py")


def require_and_extract(archive, source, unpacked):
    native.require_commit_sha(source, "U3 tested source SHA")
    if not archive.is_file():
        raise RuntimeError("U3 exact-source Windows Player ZIP missing")
    if native.sidecar(archive, ".source-sha").read_text(
            encoding="utf-8").strip() != source:
        raise RuntimeError("U3 Windows Player ZIP source does not match HEAD")
    digest_file = native.sidecar(archive, ".sha256")
    fields = digest_file.read_text(encoding="utf-8").strip().split()
    if len(fields) != 2 or fields[1] != "windows.zip":
        raise RuntimeError("U3 Player checksum sidecar malformed")
    actual = native.sha256(archive)
    if fields[0].lower() != actual:
        raise RuntimeError("U3 Player archive SHA-256 mismatch")
    native.validate_player_archive(archive)
    # Native macOS .app archives can have symlinks. U3 Windows archives
    # cannot, and Python ZipFile.extractall must not create links.
    with zipfile.ZipFile(archive) as package:
        for item in package.infolist():
            if (item.external_attr >> 16) & 0o170000 == stat.S_IFLNK:
                raise RuntimeError("U3 Windows Player ZIP cannot contain symlinks")
    if unpacked.is_symlink():
        raise RuntimeError("Unsafe U3 Player unpack path")
    if unpacked.exists():
        shutil.rmtree(unpacked)
    unpacked.mkdir(parents=True)
    with zipfile.ZipFile(archive) as package:
        package.extractall(unpacked)
    exe = unpacked / "VirtualCharacterRender.exe"
    if not exe.is_file() or not (unpacked / "VirtualCharacterRender_Data").is_dir():
        raise RuntimeError("U3 archive does not contain a complete Unity Player")
    return exe, actual


def read_config(path):
    if not path.is_file() or path.stat().st_size > 4 * 1024 * 1024:
        return False
    configuration = json.loads(path.read_text(encoding="utf-8"))
    scene = configuration.get("Scene") if isinstance(configuration, dict) else None
    overlay = scene.get("Overlay") if isinstance(scene, dict) else None
    return isinstance(overlay, dict) and overlay.get("Transparent") is False


def run(args):
    output = args.output.resolve()
    if output.is_symlink():
        raise RuntimeError("Refusing symlink output path")
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "u3-gui-probe.json"
    report = {
        "suite": "u3-windows-alpha-p11-gui-v1",
        "variant": args.variant,
        "sourceSha": args.source,
        "runnerPlatform": platform.platform(),
        "timestampUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "windowObserved": False,
        "runtimeStartupObserved": False,
        "workspaceUiTreeReady": False,
        "nativeFramePassed": False,
        "nativeInputTargetsPassed": False,
        "freeResizePassed": False,
        "compactLayoutPassed": False,
        "opaqueWindowConfigPassed": False,
        "unityFrameCaptured": False,
        "unityFrameDiverse": False,
        "runtimeExceptionFree": False,
        "managedQuitObserved": False,
        "exitCode": None,
        "normalCloseRequested": False,
        "passed": False,
        "error": "",
    }
    process = None
    start = time.monotonic()
    try:
        if sys.platform != "win32":
            raise RuntimeError("A native Windows desktop runner is required")
        executable, digest = require_and_extract(
            args.archive.resolve(), args.source, output / "unpacked-player")
        report["archiveSha256"] = digest
        report["playerExecutableSha256"] = native.sha256(executable)
        player_log = output / "Player.log"
        config = output / "gui-config.json"
        screenshot = output / "unity-frame.png"
        for path in (player_log, config, screenshot):
            path.unlink(missing_ok=True)
        process = subprocess.Popen([
            str(executable), "-screen-fullscreen", "0",
            "-screen-width", str(args.screen_width),
            "-screen-height", str(args.screen_height),
            "-logFile", str(player_log),
            "--vcr-config=" + str(config),
            "--vcr-ui-probe", "--vcr-ui-probe-image=" + str(screenshot),
        ], cwd=str(executable.parent),
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        report["playerPid"] = process.pid
        deadline = time.monotonic() + args.startup_timeout
        window = None
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError("Real U3 Player exited before Unity HWND appeared")
            observed = gui.visible_player_windows(process.pid)
            if observed:
                window = observed[0]
                break
            time.sleep(0.25)
        if window is None:
            raise RuntimeError("No actual Unity HWND; hosted GUI may be unavailable")
        report["windowObserved"] = True
        report["windowClass"] = window["class"]
        report["windowTitle"] = window["title"]
        gui.require_runtime_startup(process, player_log, deadline)
        report["runtimeStartupObserved"] = True

        layout_deadline = time.monotonic() + 5
        while time.monotonic() < layout_deadline:
            if (player_log.is_file() and
                    gui.WORKSPACE_LAYOUT_MARKER in player_log.read_text(
                        encoding="utf-8", errors="replace")):
                break
            time.sleep(0.2)
        initial_log = player_log.read_text(
            encoding="utf-8", errors="replace") if player_log.is_file() else ""
        report["workspaceUiTreeReady"] = gui.WORKSPACE_LAYOUT_MARKER in initial_log
        report["compactLayout"] = gui.read_compact_layout_evidence(initial_log)
        report["compactLayoutPassed"] = gui.compact_layout_usable(
            report["compactLayout"])

        frame = gui.inspect_native_window_frame(window["hwnd"])
        report["nativeFrame"] = frame
        report["nativeFramePassed"] = all((
            frame["hasNativeTitleBar"], frame["resizableFrame"],
            frame["hasSystemMenu"], frame["hasMinimizeBox"],
            frame["hasMaximizeBox"], not frame["borderlessPopup"]))
        try:
            targets = gui.probe_native_input_targets(window["hwnd"], frame)
            report["nativeInputTargets"] = targets
            report["nativeInputTargetsPassed"] = targets["passed"]
        except Exception as exc:
            report["nativeInputTargetsError"] = str(exc)
        try:
            resize = gui.probe_free_resize(window["hwnd"], frame)
            report["freeResize"] = resize
            report["freeResizePassed"] = all((
                resize["reachedIndependentSize"],
                resize["clientAspectChanged"], resize["restored"]))
        except Exception as exc:
            report["freeResizeError"] = str(exc)

        try:
            gui.capture_foreground_player_frame(
                window["hwnd"], output / "player-window.bmp")
            report["desktopFrameCaptured"] = True
        except Exception as exc:
            report["desktopFrameError"] = str(exc)

        image_deadline = time.monotonic() + 4
        while time.monotonic() < image_deadline:
            captured = gui.inspect_unity_screenshot(screenshot)
            if captured is not None:
                report["unityFrameCaptured"] = True
                report["unityFrameDiverse"] = captured["pixelDiversityPassed"]
                report["unityFrame"] = captured
                break
            time.sleep(0.2)

        gui.request_normal_close(window["hwnd"])
        report["normalCloseRequested"] = True
        report["exitCode"] = process.wait(timeout=args.quit_timeout)
        log = player_log.read_text(
            encoding="utf-8", errors="replace") if player_log.is_file() else ""
        report["managedQuitObserved"] = gui.QUIT_MARKER in log
        report["runtimeExceptions"] = gui.find_workspace_runtime_exceptions(log)
        report["runtimeExceptionFree"] = not report["runtimeExceptions"]
        report["workspaceUiTreeReady"] = gui.WORKSPACE_LAYOUT_MARKER in log
        report["compactLayout"] = gui.read_compact_layout_evidence(log)
        report["compactLayoutPassed"] = gui.compact_layout_usable(
            report["compactLayout"])
        report["opaqueWindowConfigPassed"] = read_config(config)
        report["passed"] = all((
            report["runtimeStartupObserved"],
            report["workspaceUiTreeReady"],
            report["nativeFramePassed"],
            report["nativeInputTargetsPassed"],
            report["freeResizePassed"],
            report["compactLayoutPassed"],
            report["opaqueWindowConfigPassed"],
            report["unityFrameCaptured"],
            report["unityFrameDiverse"],
            report["runtimeExceptionFree"],
            report["managedQuitObserved"],
            report["exitCode"] == 0,
        ))
        if not report["passed"]:
            raise RuntimeError("Real U3 GUI invariants failed: " + json.dumps({
                name: report[name] for name in (
                    "workspaceUiTreeReady", "nativeFramePassed",
                    "nativeInputTargetsPassed", "freeResizePassed",
                    "compactLayoutPassed", "opaqueWindowConfigPassed",
                    "unityFrameCaptured", "unityFrameDiverse",
                    "runtimeExceptionFree", "managedQuitObserved", "exitCode")}))
        print("U3 " + args.variant +
              " Windows GUI: native frame, compact Toolkit, screenshot and WM_CLOSE PASS")
    except Exception as exc:
        report["error"] = str(exc)
        print("U3 " + args.variant + " GUI FAILED/UNAVAILABLE: " +
              str(exc), file=sys.stderr, flush=True)
    finally:
        if process is not None and process.poll() is None:
            report["forcedCleanup"] = True
            process.kill()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                pass
        report["elapsedMs"] = round((time.monotonic() - start) * 1000)
        report_path.write_text(
            json.dumps(report, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8")
    return 0 if report["passed"] else 1


def self_test():
    with tempfile.TemporaryDirectory(prefix="u3-gui-test-") as directory:
        root = Path(directory)
        archive = root / "windows.zip"
        with zipfile.ZipFile(archive, "w") as package:
            package.writestr("../escape.exe", b"invalid")
        (root / "windows.source-sha").write_text("a" * 40 + "\n")
        (root / "windows.sha256").write_text(
            native.sha256(archive) + "  windows.zip\n")
        try:
            require_and_extract(archive, "a" * 40, root / "output")
        except RuntimeError as error:
            if "Unsafe Player archive path" not in str(error):
                raise
        else:
            raise RuntimeError("U3 GUI fixture accepted traversal archive")
        assert not (root / "output").exists()
    print("U3 GUI source-hash and archive traversal self-test: PASS")


def main():
    if len(sys.argv) == 2 and sys.argv[1] == "--self-test":
        self_test()
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--variant", choices=("Alpha", "P11"), required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--screen-width", type=int, default=960)
    parser.add_argument("--screen-height", type=int, default=520)
    parser.add_argument("--startup-timeout", type=int, default=60)
    parser.add_argument("--quit-timeout", type=int, default=45)
    args = parser.parse_args()
    if not (640 <= args.screen_width <= 2560 and
            400 <= args.screen_height <= 1440):
        parser.error("Invalid GUI viewport dimensions")
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
