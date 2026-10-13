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

QUIT = b"VCR exit: quit requested (t=0 ms)"
MANAGED_END = b"VCR exit: OnApplicationQuit finished at "


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
    output.mkdir(parents=True, exist_ok=True)
    log = output / "Player.log"
    result = output / "exit-diagnosis.json"
    if log.exists():
        log.unlink()
    command = ([str(executable), args[0], "-logFile", str(log), *args[1:]]
               if script_first else
               [str(executable), "-logFile", str(log), *args])
    started = time.monotonic()
    events = {}
    processed = 0
    tail = b""
    timeouts = False
    with (output / "process-stdout.log").open("wb") as out:
        process = subprocess.Popen(command, stdout=out, stderr=subprocess.STDOUT,
                                   cwd=executable.parent)
        print("Player PID:", process.pid, "— close the application normally.")
        last_visible = None
        while process.poll() is None:
            now = time.monotonic()
            if log.exists():
                try:
                    with log.open("rb") as stream:
                        stream.seek(processed)
                        incoming = stream.read()
                        processed = stream.tell()
                    if incoming:
                        tail = (tail + incoming)[-4096:]
                        if "quitRequestedAtMs" not in events and QUIT in tail:
                            events["quitRequestedAtMs"] = round((now - started) * 1000)
                        if "managedQuitFinishedAtMs" not in events and MANAGED_END in tail:
                            events["managedQuitFinishedAtMs"] = round((now - started) * 1000)
                except OSError:
                    pass
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

    finished = time.monotonic()
    events["processLifetimeMs"] = round((finished - started) * 1000)
    events["processExited"] = process.poll() is not None
    events["exitCode"] = process.poll()
    events["timedOut"] = timeouts
    if "quitRequestedAtMs" in events:
        if events["processExited"]:
            events["quitToProcessExitMs"] = (events["processLifetimeMs"] -
                                               events["quitRequestedAtMs"])
        if "managedQuitFinishedAtMs" in events:
            events["quitToManagedFinishMs"] = (events["managedQuitFinishedAtMs"] -
                                                 events["quitRequestedAtMs"])
        if "windowsGoneAtMs" in events:
            events["quitToWindowsGoneMs"] = (events["windowsGoneAtMs"] -
                                              events["quitRequestedAtMs"])
    result.write_text(json.dumps(events, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(events, ensure_ascii=False, indent=2))
    print("Evidence:", output)
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
        assert data["quitToManagedFinishMs"] >= 100
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
