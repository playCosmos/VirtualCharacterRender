#!/usr/bin/env python3
"""Probe a real P1 Windows GUI Player window and normal WM_CLOSE teardown.

This is *not* interactive UI/OBS/overlay acceptance. It requires a visible
Windows desktop, validates a top-level Unity window, and does not invent
successful GUI evidence when Windows runners lack an interactive session.
No third-party Python modules, synthetic window fixtures, or forced-success.
"""
import argparse
from collections import Counter
import ctypes
from ctypes import wintypes
import hashlib
import json
import math
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
WORKSPACE_LAYOUT_MARKER = "VCR U1 GUI probe: workspace_tree_ready=true"
COMPACT_LAYOUT_FIELDS = {
    "compact_bottom_dock_h": "dockHeight",
    "compact_bottom_tabs_h": "tabsHeight",
    "compact_bottom_panel_h": "panelHeight",
    "compact_bottom_scroll_h": "scrollHeight",
}


def read_compact_layout_evidence(player_log_text):
    """Parse actual measured UIDocument bounds from the exact Player log."""
    lines = [line for line in player_log_text.splitlines()
             if WORKSPACE_LAYOUT_MARKER in line]
    if not lines:
        return None
    values = {}
    for token in lines[-1].split():
        if "=" not in token:
            continue
        key, value = token.split("=", 1)
        if key not in COMPACT_LAYOUT_FIELDS:
            continue
        try:
            number = float(value)
        except ValueError:
            return None
        if not math.isfinite(number):
            return None
        values[COMPACT_LAYOUT_FIELDS[key]] = number
    return values if len(values) == len(COMPACT_LAYOUT_FIELDS) else None


def compact_layout_usable(measurements):
    """Attached controls are insufficient without a usable scroll viewport."""
    return bool(measurements and
                measurements["dockHeight"] >= 120 and
                15 <= measurements["tabsHeight"] <= 75 and
                measurements["panelHeight"] >= 70 and
                measurements["scrollHeight"] >= 36 and
                measurements["scrollHeight"] <= measurements["panelHeight"])


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def inspect_unity_screenshot(path):
    """Read an actual completed Unity PNG, not a fabricated UI screenshot."""
    if not path.is_file():
        return None
    data = path.read_bytes()
    if len(data) < 69 or data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    if data[12:16] != b"IHDR" or data[-8:-4] != b"IEND":
        return None
    width, height = struct.unpack(">II", data[16:24])
    if not 64 <= width <= 8192 or not 64 <= height <= 8192:
        return None
    return {
        "file": path.name,
        "sha256": sha256(path),
        "width": width,
        "height": height,
        "bytes": len(data),
        "uiPixelsConfirmed": False,
    }


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


def classify_window_style(style):
    """Describe native Win32 non-client controls, not rendered Unity pixels."""
    return {
        "styleHex": f"0x{style & 0xffffffff:08X}",
        "hasNativeTitleBar": style & 0x00C00000 == 0x00C00000,
        "resizableFrame": bool(style & 0x00040000),
        "hasSystemMenu": bool(style & 0x00080000),
        "hasMinimizeBox": bool(style & 0x00020000),
        "hasMaximizeBox": bool(style & 0x00010000),
        "borderlessPopup": bool(style & 0x80000000),
    }


def inspect_native_window_frame(hwnd):
    """Read the actual Unity top-level HWND style and client rectangle."""
    user32 = ctypes.windll.user32
    user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetWindowLongW.restype = ctypes.c_long
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetClientRect.restype = wintypes.BOOL
    outer = wintypes.RECT()
    client = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(outer)) or not (
            user32.GetClientRect(hwnd, ctypes.byref(client))):
        raise OSError("Unable to inspect real Player window rectangles")
    result = classify_window_style(user32.GetWindowLongW(hwnd, -16))
    result["windowRect"] = [outer.left, outer.top, outer.right, outer.bottom]
    result["outerSize"] = [outer.right - outer.left, outer.bottom - outer.top]
    result["clientSize"] = [client.right - client.left, client.bottom - client.top]
    return result


def probe_free_resize(hwnd, before):
    """Try an independent width/height OS resize, then restore original size."""
    user32 = ctypes.windll.user32
    user32.SetWindowPos.argtypes = [
        wintypes.HWND, wintypes.HWND,
        ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
    user32.SetWindowPos.restype = wintypes.BOOL
    flags = 0x0002 | 0x0004 | 0x0010  # NOMOVE | NOZORDER | NOACTIVATE
    width, height = before["outerSize"]
    target = [max(640, width - 120), min(680, height + 90)]
    result = {"originalOuterSize": [width, height],
              "requestedOuterSize": target, "restored": False}
    try:
        if not user32.SetWindowPos(hwnd, None, 0, 0,
                                   target[0], target[1], flags):
            raise OSError("SetWindowPos refused non-proportional Player resize")
        deadline = time.monotonic() + 2
        after = inspect_native_window_frame(hwnd)
        while time.monotonic() < deadline:
            if all(abs(after["outerSize"][i] - target[i]) <= 8
                   for i in range(2)):
                break
            time.sleep(0.15)
            after = inspect_native_window_frame(hwnd)
        result["observedOuterSize"] = after["outerSize"]
        result["observedClientSize"] = after["clientSize"]
        original_ratio = before["clientSize"][0] / max(1, before["clientSize"][1])
        new_ratio = after["clientSize"][0] / max(1, after["clientSize"][1])
        result["clientAspectChanged"] = abs(original_ratio - new_ratio) > 0.10
        result["reachedIndependentSize"] = all(
            abs(after["outerSize"][i] - target[i]) <= 8 for i in range(2))
        return result
    finally:
        result["restored"] = bool(user32.SetWindowPos(
            hwnd, None, 0, 0, width, height, flags))


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


def intersect_window_desktop(window, desktop):
    """Return the on-screen rectangle or None, without moving the Player.

    Rectangles are (left, top, right, bottom) in virtual-desktop coordinates.
    The result records an honest partial frame when the Player is bigger than
    the hosted runner's desktop; no offscreen pixels are fabricated.
    """
    x1 = max(window[0], desktop[0])
    y1 = max(window[1], desktop[1])
    x2 = min(window[2], desktop[2])
    y2 = min(window[3], desktop[3])
    if x2 - x1 < 64 or y2 - y1 < 64:
        return None
    return x1, y1, x2, y2


def analyze_frame_interior(raw, width, height):
    """Sample the upper central Player region, excluding taskbar/borders.

    This only identifies likely uniform/blank evidence; nonuniform content
    still cannot confirm UI pixels or an interactive UI.
    """
    pixels = Counter()
    for y in range(height // 10, height * 3 // 4, max(1, height // 64)):
        for x in range(width // 10, width * 9 // 10,
                       max(1, width // 64)):
            offset = (y * width + x) * 4
            pixels[bytes(raw[offset:offset+4])] += 1
    total = sum(pixels.values())
    dominant = max(pixels.values()) / total if total else 1.0
    return {
        "interiorSampledDistinctPixels": len(pixels),
        "interiorDominantPixelFraction": round(dominant, 6),
        "blankOrUniformPossible": len(pixels) <= 3 or dominant >= 0.98,
        "uiPixelsConfirmed": False,
    }


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
    window_bounds = (rect.left, rect.top, rect.right, rect.bottom)
    desktop_bounds = (x_virtual, y_virtual,
                      x_virtual + w_virtual, y_virtual + h_virtual)
    capture_bounds = intersect_window_desktop(window_bounds, desktop_bounds)
    if capture_bounds is None:
        raise RuntimeError(
            "Player window has no usable visible desktop intersection: " +
            f"window={window_bounds}, desktop={desktop_bounds}")
    cropped = capture_bounds != window_bounds
    capture_left, capture_top, capture_right, capture_bottom = capture_bounds
    width = capture_right - capture_left
    height = capture_bottom - capture_top

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
                desktop_dc, capture_left, capture_top, 0x40CC0020):
            raise OSError("BitBlt failed")
        # GetDIBits requires the bitmap NOT to be selected into any DC.
        # Deselect after BitBlt or Windows can return zero scanlines.
        if not gdi32.SelectObject(compatible_dc, previous):
            raise OSError("Failed to deselect screenshot bitmap")
        previous = None
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
        sampled = {raw[i:i+4] for i in range(
            0, len(raw), max(4, len(raw) // 1024 // 4 * 4))}
        interior_quality = analyze_frame_interior(raw, width, height)
        return {
            "file": destination.name,
            "sha256": sha256(destination),
            "width": width,
            "height": height,
            "windowBounds": list(window_bounds),
            "desktopBounds": list(desktop_bounds),
            "captureBounds": list(capture_bounds),
            "partialWindow": cropped,
            "sampledDistinctPixels": len(sampled),
            **interior_quality,
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

    sample = (WORKSPACE_LAYOUT_MARKER +
              " width=962 height=520 compact_bottom_dock_h=176.8"
              " compact_bottom_tabs_h=39.2 compact_bottom_panel_h=130"
              " compact_bottom_scroll_h=51.5")
    parsed = read_compact_layout_evidence(sample)
    if not compact_layout_usable(parsed):
        raise RuntimeError("Valid compact layout measurements rejected")
    if compact_layout_usable(read_compact_layout_evidence(
            sample.replace("compact_bottom_scroll_h=51.5",
                           "compact_bottom_scroll_h=8"))):
        raise RuntimeError("Unusable compact scroll viewport accepted")
    if read_compact_layout_evidence(sample.replace(
            "compact_bottom_scroll_h=51.5",
            "compact_bottom_scroll_h=NaN")) is not None:
        raise RuntimeError("Nonfinite compact geometry accepted")
    if read_compact_layout_evidence(sample.replace(
            "compact_bottom_tabs_h=39.2", "")) is not None:
        raise RuntimeError("Incomplete compact geometry accepted")
    if read_compact_layout_evidence("VCR U1 GUI probe: something_else=true") is not None:
        raise RuntimeError("Unattached workspace accepted as geometry")

    # Hosted desktops often cannot contain a requested 1280x720 window.
    # Accept only the real overlap; never report full-frame coverage.
    test_cases = [
        ((0, 0, 800, 600), (0, 0, 1024, 768), (0, 0, 800, 600)),
        ((0, 0, 1296, 760), (0, 0, 1024, 768), (0, 0, 1024, 760)),
        ((-100, -50, 950, 700), (0, 0, 1024, 768), (0, 0, 950, 700)),
        ((-1920, 0, -640, 720), (-1920, 0, 0, 1080),
         (-1920, 0, -640, 720)),
        ((100, 100, 120, 120), (0, 0, 1024, 768), None),
        ((1200, 0, 1500, 500), (0, 0, 1024, 768), None),
    ]
    for window, desktop, expected in test_cases:
        result = intersect_window_desktop(window, desktop)
        if result != expected:
            raise RuntimeError(
                f"Window/desktop clip mismatch: {window} {desktop} {result}")
    # A busy Windows taskbar must not rescue a completely blank Player
    # viewport from the "possibly uniform" finding.
    width, height = 128, 128
    uniform_frame = bytes((31, 31, 32, 0)) * (width * height)
    uniform = analyze_frame_interior(uniform_frame, width, height)
    if not uniform["blankOrUniformPossible"] or uniform["interiorSampledDistinctPixels"] != 1:
        raise RuntimeError("Uniform GUI viewport misclassified as useful pixels")
    varied_frame = bytearray(uniform_frame)
    for y in range(height // 10, height * 3 // 4):
        for x in range(width // 10, width * 9 // 10):
            offset = (y * width + x) * 4
            varied_frame[offset:offset+4] = bytes((x % 255, y % 255, 100, 0))
    varied = analyze_frame_interior(varied_frame, width, height)
    if varied["blankOrUniformPossible"] or varied["uiPixelsConfirmed"]:
        raise RuntimeError("Nonuniform sample incorrectly accepted as UI")
    with tempfile.TemporaryDirectory() as d:
        png = Path(d) / "unity.png"
        if inspect_unity_screenshot(png) is not None:
            raise RuntimeError("Missing Unity screenshot accepted")
        png.write_bytes(b"not a valid PNG")
        if inspect_unity_screenshot(png) is not None:
            raise RuntimeError("Invalid screenshot accepted")
    native = classify_window_style(0x00CF0000)
    if not (native["hasNativeTitleBar"] and native["resizableFrame"] and
            native["hasSystemMenu"] and native["hasMinimizeBox"] and
            native["hasMaximizeBox"] and not native["borderlessPopup"]):
        raise RuntimeError("Standard overlapped Windows title bar rejected")
    popup = classify_window_style(0x90000000)
    if popup["hasNativeTitleBar"] or popup["resizableFrame"] or not popup["borderlessPopup"]:
        raise RuntimeError("Borderless popup incorrectly classified as normal window")
    print("U1 GUI probe startup, clipping, window styles and pixel-quality self-test: PASS")
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
    unity_image_path = output / "unity-frame.png"
    for stale in (player_log, report_path, config_path, unity_image_path):
        stale.unlink(missing_ok=True)
    report = {
        "suite": "u1-windows-visible-window-probe-v7",
        "sourceSha": args.source,
        "playerExecutableSha256": sha256(executable),
        "runnerPlatform": platform.platform(),
        "requestedPlayerSize": [args.screen_width, args.screen_height],
        "windowObserved": False,
        "runtimeStartupObserved": False,
        "nativeFramePassed": False,
        "freeResizePassed": False,
        "windowFrameCaptured": False,
        "windowFrameIsInteractiveAcceptance": False,
        "workspaceUiTreeReady": False,
        "compactLayout": None,
        "compactLayoutPassed": False,
        "unityFrameCaptured": False,
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
            "-screen-width", str(args.screen_width),
            "-screen-height", str(args.screen_height),
            "-logFile", str(player_log),
            "--vcr-config=" + str(config_path),
            "--vcr-ui-probe",
            "--vcr-ui-probe-image=" + str(unity_image_path),
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
        # Wait briefly for an opt-in marker from a real, laid-out
        # UIDocument and attached workspace controls. Startup alone does not
        # establish UI rendering and a missing marker is preserved honestly.
        ui_tree_deadline = min(deadline, time.monotonic() + 3.0)
        while time.monotonic() < ui_tree_deadline:
            try:
                if ("VCR U1 GUI probe: workspace_tree_ready=true" in
                    player_log.read_text(encoding="utf-8", errors="replace")):
                    report["workspaceUiTreeReady"] = True
                    break
            except OSError:
                pass
            time.sleep(0.2)
        # Capture the real foreground Player rectangle for manual review.
        # The screenshot can include the desktop/taskbar or miss GPU pixels;
        # its interior-quality metrics cannot certify visible UI.
        try:
            frame = capture_foreground_player_frame(
                observed["hwnd"], output / "player-window.bmp")
            report["windowFrameCaptured"] = True
            report["windowFrame"] = frame
            print("U1 Player frame evidence captured: " +
                  f"{frame['width']}x{frame['height']}, " +
                  f"partial={frame['partialWindow']}, " +
                  f"sampledDistinctPixels={frame['sampledDistinctPixels']}, " +
                  f"interiorDominant={frame['interiorDominantPixelFraction']:.3f}, " +
                  f"possibleUniform={frame['blankOrUniformPossible']}, " +
                  f"uiTreeReady={report['workspaceUiTreeReady']}, " +
                  f"sha256={frame['sha256']}", flush=True)
        except Exception as capture_error:
            report["windowFrameError"] = str(capture_error)
            print("U1 Player frame evidence unavailable: " +
                  str(capture_error), file=sys.stderr, flush=True)
        # Unity ScreenCapture is independent of Windows desktop BitBlt.
        # Keep a completed PNG only when the actual Player produces it;
        # it is still visual evidence, not proof of interactive controls.
        unity_deadline = time.monotonic() + 2.5
        while time.monotonic() < unity_deadline:
            unity_frame = inspect_unity_screenshot(unity_image_path)
            if unity_frame is not None:
                report["unityFrameCaptured"] = True
                report["unityFrame"] = unity_frame
                print("U1 Unity frame evidence captured: " +
                      f"{unity_frame['width']}x{unity_frame['height']}, " +
                      f"sha256={unity_frame['sha256']}", flush=True)
                break
            time.sleep(0.2)
        if not report["unityFrameCaptured"]:
            report["unityFrameError"] = (
                "Unity did not produce a complete opt-in screenshot")
            if unity_image_path.exists():
                unity_image_path.unlink()
        # Win32 style and non-proportional resize evidence is independent
        # from the CI desktop's unreliable GPU pixel readback.
        frame = inspect_native_window_frame(observed["hwnd"])
        report["nativeFrame"] = frame
        report["nativeFramePassed"] = all((
            frame["hasNativeTitleBar"], frame["resizableFrame"],
            frame["hasSystemMenu"], frame["hasMinimizeBox"],
            frame["hasMaximizeBox"], not frame["borderlessPopup"]))
        print("U1 native Windows frame: " +
              f"style={frame['styleHex']}, nativeTitle={frame['hasNativeTitleBar']}, " +
              f"resizable={frame['resizableFrame']}", flush=True)
        try:
            resize = probe_free_resize(observed["hwnd"], frame)
            report["windowResize"] = resize
            report["freeResizePassed"] = (
                resize["reachedIndependentSize"] and
                resize["clientAspectChanged"] and resize["restored"])
            print("U1 native independent aspect resize: " +
                  f"target={resize['requestedOuterSize']}, " +
                  f"actual={resize['observedOuterSize']}, " +
                  f"passed={report['freeResizePassed']}", flush=True)
        except Exception as error:
            report["windowResizeError"] = str(error)
        close_requested_at = time.monotonic()
        request_normal_close(observed["hwnd"])
        report["normalCloseRequested"] = True
        try:
            report["exitCode"] = process.wait(timeout=args.quit_timeout)
        except subprocess.TimeoutExpired:
            raise RuntimeError("WM_CLOSE failed to exit Player within timeout")
        report["closeRequestToProcessExitMs"] = round(
            (time.monotonic() - close_requested_at) * 1000)
        final_log = player_log.read_text(
            encoding="utf-8", errors="replace") if player_log.is_file() else ""
        report["managedQuitObserved"] = QUIT_MARKER in final_log
        # The U1 tree can finish layout after the early polling deadline.
        # Re-read after normal exit rather than freezing a false negative;
        # this only establishes tree attachment, not interactive acceptance.
        report["workspaceUiTreeReady"] = WORKSPACE_LAYOUT_MARKER in final_log
        report["compactLayout"] = read_compact_layout_evidence(final_log)
        report["compactLayoutPassed"] = compact_layout_usable(
            report["compactLayout"])
        if args.screen_height < 640 and not report["compactLayoutPassed"]:
            raise RuntimeError(
                "Compact UI tree attached without measured, usable bottom dock")
        if report["compactLayout"] is not None:
            print("U1 measured compact layout: " +
                  json.dumps(report["compactLayout"], sort_keys=True) +
                  f", passed={report['compactLayoutPassed']}", flush=True)
        if report["exitCode"] != 0:
            raise RuntimeError("GUI Player exited with " + str(report["exitCode"]))
        if not report["managedQuitObserved"]:
            raise RuntimeError(
                "GUI Player did not emit managed normal quit completion marker")
        if not report["nativeFramePassed"] or not report["freeResizePassed"]:
            raise RuntimeError(
                "P1 native Windows title bar or free aspect resize validation failed")
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
    parser.add_argument("--screen-width", type=int, default=1280)
    parser.add_argument("--screen-height", type=int, default=720)
    parser.add_argument("--startup-timeout", type=int, default=50)
    parser.add_argument("--quit-timeout", type=int, default=45)
    args = parser.parse_args()
    if not (640 <= args.screen_width <= 2560 and
            400 <= args.screen_height <= 1440):
        parser.error("GUI dimensions must be 640..2560 x 400..1440")
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
