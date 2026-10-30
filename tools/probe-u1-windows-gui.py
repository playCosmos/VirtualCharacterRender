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
import struct
import subprocess
import sys
import tempfile
import time


WM_CLOSE = 0x0010
EXPECTED_CLASS = "UnityWndClass"
QUIT_MARKER = "VCR exit: OnApplicationQuit finished"
RUNTIME_STARTUP_MARKER = (
    "VCR inventory: stage=startup category=module id=application status=started")


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


def capture_foreground_player_frame(hwnd, destination):
    """Capture only foreground Player window pixels via the Windows desktop.

    This produces reviewable visual evidence, NOT proof that specific buttons,
    model pixels, native Settings or interactive behavior work. GPU surfaces
    may be blank on hosted/remote desktops, which is reported separately.
    """
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    user32.GetDC.argtypes = [wintypes.HWND]
    user32.GetDC.restype = wintypes.HDC
    user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
    gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
    gdi32.CreateCompatibleDC.restype = wintypes.HDC
    gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
    gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
    gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
    gdi32.SelectObject.restype = wintypes.HGDIOBJ
    gdi32.BitBlt.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int,
                             ctypes.c_int, ctypes.c_int, wintypes.HDC,
                             ctypes.c_int, ctypes.c_int, wintypes.DWORD]
    gdi32.BitBlt.restype = wintypes.BOOL
    gdi32.GetDIBits.argtypes = [
        wintypes.HDC, wintypes.HBITMAP, wintypes.UINT, wintypes.UINT,
        ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT]
    gdi32.GetDIBits.restype = ctypes.c_int
    gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
    gdi32.DeleteDC.argtypes = [wintypes.HDC]

    if user32.GetForegroundWindow() != hwnd:
        raise RuntimeError("Player window is not foreground; desktop capture skipped")
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise OSError("GetWindowRect failed")
    width = rect.right - rect.left
    height = rect.bottom - rect.top
    if not 64 <= width <= 4096 or not 64 <= height <= 4096:
        raise RuntimeError("Player window has unsupported screenshot dimensions")
    x_virtual = user32.GetSystemMetrics(76)
    y_virtual = user32.GetSystemMetrics(77)
    w_virtual = user32.GetSystemMetrics(78)
    h_virtual = user32.GetSystemMetrics(79)
    if (rect.left < x_virtual or rect.top < y_virtual or
        rect.right > x_virtual + w_virtual or
        rect.bottom > y_virtual + h_virtual):
        raise RuntimeError("Player window extends outside captured desktop")

    desktop_dc = user32.GetDC(None)
    if not desktop_dc:
        raise OSError("GetDC failed")
    compatible_dc = None
    bitmap = None
    previous = None
    try:
        compatible_dc = gdi32.CreateCompatibleDC(desktop_dc)
        if not compatible_dc:
            raise OSError("CreateCompatibleDC failed")
        bitmap = gdi32.CreateCompatibleBitmap(desktop_dc, width, height)
        if not bitmap:
            raise OSError("CreateCompatibleBitmap failed")
        previous = gdi32.SelectObject(compatible_dc, bitmap)
        if not previous:
            raise OSError("SelectObject failed")
        # SRCCOPY | CAPTUREBLT: include layered windows; only the
        # previously verified foreground Player rectangle is captured.
        if not gdi32.BitBlt(
                compatible_dc, 0, 0, width, height,
                desktop_dc, rect.left, rect.top, 0x40CC0020):
            raise OSError("BitBlt failed")
        # Top-down 32-bit BI_RGB avoids flipping the Unity frame.
        row_bytes = width * 4
        header = struct.pack(
            "<IiiHHIIiiII", 40, width, -height, 1, 32, 0,
            row_bytes * height, 0, 0, 0, 0)
        pixels = (ctypes.c_ubyte * (row_bytes * height))()
        info = ctypes.create_string_buffer(header + bytes(16))
        rows = gdi32.GetDIBits(
            compatible_dc, bitmap, 0, height,
            ctypes.cast(pixels, ctypes.c_void_p), ctypes.byref(info), 0)
        if rows != height:
            raise OSError("GetDIBits did not return full Player frame")
        raw = bytes(pixels)
        # BMP file header is 14 bytes, followed by BITMAPINFOHEADER and pixels.
        bmp = struct.pack("<2sIHHI", b"BM", 54 + len(raw), 0, 0, 54)
        destination.write_bytes(bmp + header + raw)
        sampled = {raw[i:i+4] for i in range(0, len(raw), max(4, len(raw) // 1024 // 4 * 4))}
        return {
            "file": destination.name,
            "sha256": sha256(destination),
            "width": width,
            "height": height,
            "sampledDistinctPixels": len(sampled),
            "blankOrUniformPossible": len(sampled) <= 2,
        }
    finally:
        if previous is not None and compatible_dc:
            gdi32.SelectObject(compatible_dc, previous)
        if bitmap:
            gdi32.DeleteObject(bitmap)
        if compatible_dc:
            gdi32.DeleteDC(compatible_dc)
        user32.ReleaseDC(None, desktop_dc)


def runtime_startup_logged(player_log):
    """Do not mistake early window creation for completed managed startup."""
    try:
        return RUNTIME_STARTUP_MARKER in player_log.read_text(
            encoding="utf-8", errors="replace")
    except OSError:
        # Unity may still hold or have not created the Player log.
        return False


def require_runtime_startup(process, player_log, deadline):
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(
                "GUI Player exited before managed runtime startup completed")
        if runtime_startup_logged(player_log):
            return
        time.sleep(0.25)
    raise RuntimeError(
        "Visible GUI window did not reach managed runtime startup; "
        "normal-close observation was not attempted")


def run_self_test():
    class Process:
        def __init__(self, exit_code=None):
            self.exit_code = exit_code

        def poll(self):
            return self.exit_code

    with tempfile.TemporaryDirectory() as directory:
        log = Path(directory) / "Player.log"
        if runtime_startup_logged(log):
            raise RuntimeError("Missing Player log accepted as startup")
        log.write_text("UnityWndClass visible; no runtime inventory yet\n",
                       encoding="utf-8")
        if runtime_startup_logged(log):
            raise RuntimeError("Visible window alone accepted as startup")
        try:
            require_runtime_startup(Process(), log, time.monotonic() + 0.02)
        except RuntimeError as error:
            if "did not reach managed runtime startup" not in str(error):
                raise
        else:
            raise RuntimeError("Missing runtime startup did not fail closed")
        log.write_text(RUNTIME_STARTUP_MARKER + "\n", encoding="utf-8")
        require_runtime_startup(Process(), log, time.monotonic() + 1)
        try:
            require_runtime_startup(Process(0), log, time.monotonic() + 1)
        except RuntimeError as error:
            if "exited before managed runtime startup" not in str(error):
                raise
        else:
            raise RuntimeError("Exited Player accepted as running")
    print("U1 GUI probe startup-readiness self-test: PASS")
    return 0


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
        "suite": "u1-windows-visible-window-probe-v2",
        "sourceSha": args.source,
        "playerExecutableSha256": sha256(executable),
        "runnerPlatform": platform.platform(),
        "windowObserved": False,
        "runtimeStartupObserved": False,
        "windowFrameCaptured": False,
        "windowFrameIsInteractiveAcceptance": False,
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
        # Unity can expose its top-level window before the scene/bootstrap
        # reaches Start. Closing at that point runs OnDisable but may never
        # invoke the managed OnApplicationQuit marker this probe requires.
        require_runtime_startup(process, player_log, deadline)
        report["runtimeStartupObserved"] = True
        report["runtimeStartupObservedAfterMs"] = round(
            (time.monotonic() - start) * 1000)
        # Allow one update tick after managed startup. Capture the real
        # foreground Player rectangle for manual pixel inspection. Desktop
        # capture is optional: a hosted runner may not expose readable GPU
        # pixels, and this never elevates the result to UI acceptance.
        time.sleep(1.0)
        try:
            frame = capture_foreground_player_frame(
                observed["hwnd"], output / "player-window.bmp")
            report["windowFrameCaptured"] = True
            report["windowFrame"] = frame
        except Exception as capture_error:
            report["windowFrameError"] = str(capture_error)
            print("U1 Player frame evidence unavailable: " +
                  str(capture_error), file=sys.stderr, flush=True)
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
    if len(sys.argv) == 2 and sys.argv[1] == "--self-test":
        return run_self_test()
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
