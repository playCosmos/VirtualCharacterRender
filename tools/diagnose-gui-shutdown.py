#!/usr/bin/env python3
"""Measure real desktop quit latency without hiding or killing the Player.

Launch the provided Unity Player, close it normally from its own UI or OS frame,
then inspect both the elapsed process lifetime and managed cleanup markers.
No third-party Python packages are needed.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid
import zipfile
import platform
import re

QUIT = b"VCR exit: quit requested (t=0 ms)"
MANAGED_END = b"VCR exit: OnApplicationQuit finished at "
LATE_MANAGED_END = b"VCR exit: late managed quit callback at "


def windows_visible_windows(pid):
    """Read-only enumeration: no ShowWindow, SetWindowPos, or hide operations."""
    if sys.platform != "win32":
        return None
    import ctypes
    from ctypes import wintypes
    user32 = ctypes.windll.user32
    visible = []
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def visitor(hwnd, unused):
        if not user32.IsWindowVisible(hwnd):
            return True
        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value != pid:
            return True
        cls = ctypes.create_unicode_buffer(256)
        title = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls, len(cls))
        user32.GetWindowTextW(hwnd, title, len(title))
        if ("UnityWndClass" in cls.value or
                cls.value.startswith("VCR.NativeSettings.") or
                "VirtualCharacterRender" in title.value):
            visible.append(cls.value + ": " + title.value)
        return True

    user32.EnumWindows(callback_type(visitor), 0)
    return visible


def observe(executable, args, output, limit_seconds=180, script_first=False):
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    log = output / "Player.log"
    result = output / "exit-diagnosis.json"
    if log.exists():
        log.unlink()
    command = ([str(executable), args[0], "-logFile", str(log), *args[1:]]
               if script_first else
               [str(executable), "-logFile", str(log), *args])
    started = time.monotonic()
    events = {
        "platform": platform.platform(),
        "diagnosticVersion": 2,
        "pollIntervalMs": 50,
    }
    processed = 0
    tail = b""
    timeouts = False

    def read_player_log(after_exit=False):
        nonlocal processed, tail
        if not log.exists():
            return
        try:
            with log.open("rb") as stream:
                stream.seek(processed)
                incoming = stream.read()
                processed = stream.tell()
        except OSError:
            return
        if not incoming:
            return
        combined = tail + incoming
        now_ms = round((time.monotonic() - started) * 1000)
        if "quitRequestedAtMs" not in events and QUIT in combined:
            events["quitRequestedAtMs"] = now_ms
            events["quitMarkerObservedAfterExit"] = after_exit
        if "managedQuitFinishedAtMs" not in events:
            match = re.search(rb"VCR exit: OnApplicationQuit finished at (\d+) ms", combined)
            if match:
                events["managedQuitFinishedAtMs"] = now_ms
                events["managedQuitFinishedElapsedMs"] = int(match.group(1))
        if "lateManagedQuitAtMs" not in events:
            match = re.search(rb"VCR exit: late managed quit callback at (\d+) ms", combined)
            if match:
                events["lateManagedQuitAtMs"] = now_ms
                events["lateManagedQuitElapsedMs"] = int(match.group(1))
        tail = combined[-(max(len(QUIT), len(MANAGED_END),
                              len(LATE_MANAGED_END)) - 1):]
    with (output / "process-stdout.log").open("wb") as out:
        process = subprocess.Popen(command, stdout=out, stderr=subprocess.STDOUT,
                                   cwd=executable.parent)
        print("Player PID:", process.pid, "— close the application normally.")
        last_visible = None
        while process.poll() is None:
            now = time.monotonic()
            read_player_log()
            try:
                windows = windows_visible_windows(process.pid)
                if windows is not None:
                    if windows:
                        last_visible = now
                    elif (last_visible is not None and
                          "windowsGoneAtMs" not in events):
                        events["windowsGoneAtMs"] = round((now - started) * 1000)
            except (OSError, AttributeError):
                pass
            if now - started >= limit_seconds:
                timeouts = True
                break  # Do not kill a still-running Player just to collect evidence.
            time.sleep(0.05)

    # A Player can write its complete quit log and terminate before the first
    # poll. Always drain after exit; report the marker as late-observed rather
    # than pretending a precise quit timestamp was sampled while alive.
    exited = process.poll() is not None
    read_player_log(after_exit=exited)
    # The last visible-window observation may precede PID exit. Observe
    # disappearance one final time, without claiming an exact earlier time.
    if last_visible is not None and "windowsGoneAtMs" not in events:
        try:
            windows = windows_visible_windows(process.pid)
            if windows is not None and not windows:
                events["windowsGoneAtMs"] = round(
                    (time.monotonic() - started) * 1000)
                events["windowsGoneObservedAfterExit"] = exited
        except (OSError, AttributeError):
            pass
    finished = time.monotonic()
    events["processLifetimeMs"] = round((finished - started) * 1000)
    events["processExited"] = process.poll() is not None
    events["exitCode"] = process.poll()
    events["timedOut"] = timeouts
    if "quitRequestedAtMs" in events:
        if events["processExited"]:
            if not events.get("quitMarkerObservedAfterExit"):
                events["quitToProcessExitMs"] = (
                    events["processLifetimeMs"] - events["quitRequestedAtMs"])
            else:
                events["quitToProcessExitMs"] = None
                events["quitToProcessExitLowerBoundMs"] = 0
        if "managedQuitFinishedElapsedMs" in events:
            # Unity's internal elapsed marker is more precise than two
            # log-read observations, especially when both arrive post-exit.
            events["quitToManagedFinishMs"] = events["managedQuitFinishedElapsedMs"]
        elif "managedQuitFinishedAtMs" in events and not events.get("quitMarkerObservedAfterExit"):
            events["quitToManagedFinishMs"] = (events["managedQuitFinishedAtMs"] -
                                                 events["quitRequestedAtMs"])
        if "lateManagedQuitElapsedMs" in events:
            events["quitToLateManagedMs"] = events["lateManagedQuitElapsedMs"]
        elif "lateManagedQuitAtMs" in events and not events.get("quitMarkerObservedAfterExit"):
            events["quitToLateManagedMs"] = (events["lateManagedQuitAtMs"] -
                                              events["quitRequestedAtMs"])
        if "windowsGoneAtMs" in events:
            events["quitToWindowsGoneMs"] = (events["windowsGoneAtMs"] -
                                              events["quitRequestedAtMs"])
    # Standalone Windows users can upload exactly one local evidence ZIP.
    # Never upload or transmit diagnostics automatically; paths, usernames,
    # model filenames, and native logs may contain private information.
    analysis = output / "shutdown-phases.txt"
    if log.exists():
        try:
            from importlib.util import module_from_spec, spec_from_file_location
            helper = Path(__file__).with_name("analyze-shutdown-log.py")
            if helper.exists():
                spec = spec_from_file_location("vcr_shutdown_analyzer", helper)
                module = module_from_spec(spec)
                spec.loader.exec_module(module)
                analysis.write_text(
                    module.format_report(
                        log.read_text(encoding="utf-8", errors="replace")),
                    encoding="utf-8")
        except Exception as exception:
            analysis.write_text("Shutdown analysis unavailable: " +
                                str(exception) + "\n", encoding="utf-8")
    result.write_text(json.dumps(events, indent=2) + "\n", encoding="utf-8")
    bundle = output / "exit-evidence.zip"
    with zipfile.ZipFile(bundle, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for item in (result, log, analysis, output / "process-stdout.log"):
            if item.is_file():
                package.write(item, item.name)
    print(json.dumps(events, ensure_ascii=False, indent=2))
    print("Diagnostic ZIP (review before sharing):", bundle)
    return 0 if events["processExited"] and "quitRequestedAtMs" in events else 1


def self_test():
    # No GUI necessary: check append-only evidence collection and actual exit.
    from tempfile import TemporaryDirectory
    with TemporaryDirectory() as dirname:
        root = Path(dirname)
        code = ("import pathlib,sys,time;"
                "p=pathlib.Path(sys.argv[sys.argv.index('-logFile')+1]);"
                "p.write_bytes(b'VCR exit: quit requested (t=0 ms)\\n');"
                "time.sleep(0.2);"
                "p.write_bytes(p.read_bytes()+b'VCR exit: OnApplicationQuit finished at 8 ms\\n');"
                "time.sleep(0.2)")
        # The interpreter's own -logFile option is not accepted; create a tiny
        # stdlib-only executable shim through a Python script instead.
        stub = root / "test-player.py"
        stub.write_text(code, encoding="utf-8")
        output = root / "out"
        assert observe(Path(sys.executable), [str(stub)], output, 5, script_first=True) == 0
        data = json.loads((output / "exit-diagnosis.json").read_text())
        assert data["quitToProcessExitMs"] >= 100
        # Do not mistake polling latency for Unity managed cleanup duration.
        assert data["quitToManagedFinishMs"] == 8
        assert data["managedQuitFinishedElapsedMs"] == 8
        assert (output / "exit-evidence.zip").is_file()
        # A fast Player may terminate before our first poll. Final log
        # draining must still detect its quit request and pass.
        stub.write_text(code.replace("time.sleep(0.2);", "time.sleep(0);")
                             .replace("time.sleep(0.2)", "time.sleep(0)"),
                        encoding="utf-8")
        rapid = root / "rapid"
        assert observe(Path(sys.executable), [str(stub)], rapid, 5,
                       script_first=True) == 0
        rapid_data = json.loads((rapid / "exit-diagnosis.json").read_text())
        assert rapid_data["processExited"] is True
        assert rapid_data["managedQuitFinishedElapsedMs"] == 8
        assert rapid_data["quitToManagedFinishMs"] == 8
        if rapid_data.get("quitMarkerObservedAfterExit"):
            assert rapid_data["quitToProcessExitMs"] is None
        # Simulate a window surviving every live probe, then disappearing
        # at process exit. The final read-only probe must record it.
        from unittest.mock import patch
        import os
        def visible_until_exit(pid):
            try:
                os.kill(pid, 0)
                return ["UnityWndClass"]
            except ProcessLookupError:
                return []
        stub.write_text(code, encoding="utf-8")
        window_test = root / "window-after-exit"
        with patch.dict(observe.__globals__, {
            "windows_visible_windows": visible_until_exit
        }):
            assert observe(Path(sys.executable), [str(stub)], window_test, 5,
                           script_first=True) == 0
        window_data = json.loads(
            (window_test / "exit-diagnosis.json").read_text())
        assert window_data.get("windowsGoneAtMs") is not None
        assert window_data.get("windowsGoneObservedAfterExit") is True
    print("GUI shutdown diagnosis harness self-test PASS")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("executable", type=Path, nargs="?")
    p.add_argument("--output", type=Path, default=Path("vcr-exit-evidence"))
    p.add_argument("--timeout", type=int, default=180)
    p.add_argument("--self-test", action="store_true")
    p.add_argument("player_args", nargs="*")
    args = p.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.executable is None or not args.executable.is_file():
        p.error("provide the actual Player executable file")
    if args.timeout < 1:
        p.error("timeout must be positive")
    session = args.output / ("session-" + uuid.uuid4().hex[:8])
    return observe(args.executable.resolve(), args.player_args, session, args.timeout)


if __name__ == "__main__":
    sys.exit(main())
