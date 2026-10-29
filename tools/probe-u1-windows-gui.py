#!/usr/bin/env python3
"""Probe a real P1 Windows GUI Player window and normal WM_CLOSE teardown.

This is *not* interactive UI/OBS/overlay acceptance. It requires a visible
Windows desktop, validates a top-level Unity window, and does not invent
successful GUI evidence when Windows runners lack an interactive session.
No third-party Python modules, synthetic window fixtures, or forced-success.
"""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time


WM_CLOSE = 0x0010
EXPECTED_CLASS = "UnityWndClass"
QUIT_MARKER = "VCR exit: OnApplicationQuit finished"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def visible_player_windows(pid):
    """Read-only enumeration of windows owned by the exact Player process."""
    user32 = ctypes.windll.user32
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetClassNameW.argtypes = [
        wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.argtypes = [
        wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    result = []
    def visitor(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value != pid:
            return True
        name = ctypes.create_unicode_buffer(256)
        title = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, name, len(name))
        user32.GetWindowTextW(hwnd, title, len(title))
        if EXPECTED_CLASS in name.value:
            result.append({
                "hwnd": int(hwnd),
                "class": name.value,
                "title": title.value[:255],
            })
        return True

    visitor_callback = callback_type(visitor)
    if not user32.EnumWindows(visitor_callback, 0):
        raise OSError("EnumWindows failed while probing Player GUI")
    return result


def request_normal_close(hwnd):
    user32 = ctypes.windll.user32
    user32.IsWindow.argtypes = [wintypes.HWND]
    user32.IsWindow.restype = wintypes.BOOL
    user32.PostMessageW.argtypes = [
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.PostMessageW.restype = wintypes.BOOL
    if not user32.IsWindow(hwnd):
        raise RuntimeError("Unity window vanished before the close request")
    if not user32.PostMessageW(hwnd, WM_CLOSE, 0, 0):
        raise OSError("Could not post WM_CLOSE to Unity main window")


def run_probe(args):
    if sys.platform != "win32" or platform.machine().lower() not in (
            "amd64", "x86_64"):
        raise RuntimeError("Real Windows x64 GUI desktop is required")
    executable = args.player.resolve()
    if not executable.is_file() or executable.name != "VirtualCharacterRender.exe":
        raise RuntimeError("Expected an existing P1 VirtualCharacterRender.exe")
    if not (executable.parent / "VirtualCharacterRender_Data").is_dir():
        raise RuntimeError("P1 Player data folder is missing")
    if len(args.source) != 40 or any(x not in "0123456789abcdef" for x in args.source):
        raise RuntimeError("Expected exact lowercase source Git SHA")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    player_log = output / "Player.log"
    report_path = output / "gui-probe.json"
    config_path = output / "gui-probe-config.json"
    for stale in (player_log, report_path, config_path):
        stale.unlink(missing_ok=True)
    report = {
        "suite": "u1-windows-visible-window-probe-v1",
        "sourceSha": args.source,
        "playerExecutableSha256": sha256(executable),
        "runnerPlatform": platform.platform(),
        "windowObserved": False,
        "normalCloseRequested": False,
        "managedQuitObserved": False,
        "exitCode": None,
        "passed": False,
        "interactiveU1Accepted": False,
    }
    process = None
    start = time.monotonic()
    try:
        command = [
            str(executable),
            "-screen-fullscreen", "0",
            "-screen-width", "1280", "-screen-height", "720",
            "-logFile", str(player_log),
            "--vcr-config=" + str(config_path),
        ]
        # Deliberately omit -batchmode, -nographics and smoke-report switches.
        process = subprocess.Popen(
            command, cwd=str(executable.parent),
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        report["playerPid"] = process.pid
        deadline = start + args.startup_timeout
        observed = None
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(
                    "GUI Player exited before a visible Unity window appeared (exit " +
                    str(process.returncode) + ")")
            windows = visible_player_windows(process.pid)
            if windows:
                observed = windows[0]
                break
            time.sleep(0.25)
        if observed is None:
            raise RuntimeError(
                "No visible UnityWndClass for the Player PID; "
                "runner may lack an interactive desktop session")
        report["windowObserved"] = True
        report["windowClass"] = observed["class"]
        report["windowTitle"] = observed["title"]
        report["windowObservedAfterMs"] = round((time.monotonic() - start) * 1000)
        # Allow one update tick before sending an ordinary OS close request.
        time.sleep(1.0)
        close_requested_at = time.monotonic()
        request_normal_close(observed["hwnd"])
        report["normalCloseRequested"] = True
        try:
            report["exitCode"] = process.wait(timeout=args.quit_timeout)
        except subprocess.TimeoutExpired:
            raise RuntimeError("WM_CLOSE failed to exit Player within timeout")
        report["closeRequestToProcessExitMs"] = round(
            (time.monotonic() - close_requested_at) * 1000)
        report["managedQuitObserved"] = (
            player_log.is_file() and QUIT_MARKER in
            player_log.read_text(encoding="utf-8", errors="replace"))
        if report["exitCode"] != 0:
            raise RuntimeError("GUI Player exited with " + str(report["exitCode"]))
        if not report["managedQuitObserved"]:
            raise RuntimeError(
                "GUI Player did not emit managed normal quit completion marker")
        report["passed"] = True
        print("U1 Windows visible-window/normal-quit probe PASS "
              "(interactive UI not accepted)", flush=True)
    except Exception as error:
        report["error"] = str(error)
        print("U1 Windows GUI probe FAILED/UNAVAILABLE: " + str(error),
              file=sys.stderr, flush=True)
    finally:
        if process is not None and process.poll() is None:
            # Forced cleanup is explicitly a FAILURE, never accepted as quit.
            report["forcedCleanup"] = True
            process.kill()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                pass
        report["elapsedMs"] = round((time.monotonic() - start) * 1000)
        report_path.write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", required=True, type=Path)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--startup-timeout", type=int, default=50)
    parser.add_argument("--quit-timeout", type=int, default=45)
    args = parser.parse_args()
    if not 5 <= args.startup_timeout <= 120 or not 5 <= args.quit_timeout <= 120:
        parser.error("Timeouts must be in the 5..120 second range")
    try:
        return run_probe(args)
    except Exception as error:
        print("U1 Windows GUI probe unavailable: " + str(error),
              file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
