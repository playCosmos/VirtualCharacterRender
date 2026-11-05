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
import zlib


WM_CLOSE = 0x0010
WM_NCHITTEST = 0x0084
HTCAPTION = 2
HTLEFT = 10
HTRIGHT = 11
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


def find_workspace_runtime_exceptions(log_text):
    """The actual P1 log must not hide UI exceptions behind green HWND checks."""
    return [line.strip()[:512] for line in log_text.splitlines()
            if line.strip().startswith("NullReferenceException:") or
            ("VCR U1 " in line and " refresh stage " in line and
             " failed " in line)]


def compact_layout_usable(measurements):
    """Attached controls are insufficient without a usable scroll viewport."""
    return bool(measurements and
                measurements["dockHeight"] >= 120 and
                15 <= measurements["tabsHeight"] <= 75 and
                measurements["panelHeight"] >= 70 and
                measurements["scrollHeight"] >= 36 and
                measurements["scrollHeight"] <= measurements["panelHeight"])


def parse_workspace_click_target_evidence(log_text, start_offset=0):
    """Only the current compact transition's measured Unity target qualifies."""
    marker = "VCR U1 GUI probe: workspace_click_target_ready=true mode=compact "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(t.split("=", 1) for t in line.split() if "=" in t)
        try:
            x, y = float(values["x"]), float(values["y"])
            width, height = float(values["root_w"]), float(values["root_h"])
            initial = values["initial_mode"]
        except (KeyError, ValueError):
            return None
        if not all(map(math.isfinite, (x, y, width, height))) or not (
                256 <= width <= 2560 and 400 <= height <= 1440 and
                8 <= x <= width - 8 and 8 <= y <= height - 8 and
                initial in ("live", "advanced")):
            return None
        return {"x": x, "y": y, "rootWidth": width,
                "rootHeight": height, "initialMode": initial}
    return None


def parse_feature_search_workspace_target(log_text, start_offset=0):
    """Only a fresh, measured Advanced toolbar target may return to Live."""
    marker = "VCR U1 GUI probe: feature_search_workspace_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (values.get("current_mode") != "advanced" or
                not all(math.isfinite(v) for v in (x, y, w, h)) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w,
                "rootHeight": h, "initialMode": "advanced"}
    return None


def parse_layout_snapshot_target(log_text, stage, start_offset=0):
    """Read only a fresh, hit-tested toolbar Save/Hide/Restore target."""
    marker = "VCR U1 GUI probe: layout_snapshot_target_ready=true stage=" + stage + " "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (not all(math.isfinite(v) for v in (x, y, w, h)) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w,
                "rootHeight": h, "initialMode": "live"}
    return None


def wait_for_layout_snapshot_target(player_log, stage, offset):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        target = parse_layout_snapshot_target(log, stage, offset)
        if target is not None:
            return target
        time.sleep(0.12)
    raise RuntimeError("Layout snapshot " + stage + " control not hit-testable")


def parse_feature_search_inspector_drag_target(log_text, start_offset=0):
    """Use freshly measured source tab and empty-right-rail drop coordinates."""
    marker = "VCR U1 GUI probe: feature_search_inspector_drag_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            sx, sy = float(values["sx"]), float(values["sy"])
            dx, dy = float(values["dx"]), float(values["dy"])
            width, height = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (values.get("from") != "bottom" or values.get("to") != "right" or
                not all(math.isfinite(v) for v in (
                    sx, sy, dx, dy, width, height)) or
                not 256 <= width <= 2560 or not 400 <= height <= 1440 or
                not 8 <= sx <= width - 8 or not 8 <= sy <= height - 8 or
                not 8 <= dx <= width - 8 or not 8 <= dy <= height - 8 or
                abs(dx - sx) < 100):
            return None
        return {"source": [sx, sy], "destination": [dx, dy],
                "rootWidth": width, "rootHeight": height}
    return None


def parse_feature_search_tracking_tab_target(log_text, start_offset=0):
    """Accept only a new on-screen Tracking tab on the moved Bottom dock."""
    marker = "VCR U1 GUI probe: feature_search_tracking_tab_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (values.get("mode") != "live" or
                not all(math.isfinite(v) for v in (x, y, w, h)) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w,
                "rootHeight": h, "initialMode": "live"}
    return None


def parse_feature_search_inspector_move_target(log_text, start_offset=0):
    """Read a fresh visible Inspector Move button after manual Live restoration."""
    marker = "VCR U1 GUI probe: feature_search_inspector_move_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (values.get("mode") != "live" or
                not all(math.isfinite(v) for v in (x, y, w, h)) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w,
                "rootHeight": h, "initialMode": "live"}
    return None


def parse_feature_search_hide_left_target(log_text, start_offset=0):
    """Accept only fresh, on-screen coordinates of the Advanced Hide left button."""
    marker = "VCR U1 GUI probe: feature_search_hide_left_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (values.get("mode") != "advanced" or
                not all(math.isfinite(v) for v in (x, y, w, h)) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w,
                "rootHeight": h, "initialMode": "advanced"}
    return None


def parse_settings_click_target_evidence(log_text, start_offset=0):
    """Require a real, post-workspace-switch Settings button location."""
    marker = "VCR U1 GUI probe: settings_click_target_ready=true "
    for line in reversed(log_text[start_offset:].splitlines()):
        if not line.startswith(marker):
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            x, y = float(values["x"]), float(values["y"])
            w, h = float(values["root_w"]), float(values["root_h"])
        except (KeyError, ValueError):
            return None
        if (not all(map(math.isfinite, (x, y, w, h))) or
                not 256 <= w <= 2560 or not 400 <= h <= 1440 or
                not 8 <= x <= w - 8 or not 8 <= y <= h - 8):
            return None
        return {"x": x, "y": y, "rootWidth": w, "rootHeight": h}
    return None


def parse_compact_header_evidence(log_text, mode, start_offset=0):
    """Accept only the live, fully visible first three toolbar actions."""
    marker = ("VCR U1 GUI probe: compact_header_ready=true " +
              "mode=" + mode +
              " primary_actions=3 auto_tabs_hidden=true pointer_targets=3")
    return any(line.strip() == marker
               for line in log_text[start_offset:].splitlines())


def parse_single_panel_move_evidence(log_text, mode, start_offset=0):
    """Only a live compact/expanded Inspector header Move qualifies."""
    marker = ("VCR U1 GUI probe: single_panel_move_ready=true " +
              "mode=" + mode + " panel=inspector pointer_target=true")
    return any(line.strip() == marker
               for line in log_text[start_offset:].splitlines())


def parse_inspector_axes_evidence(log_text, start_offset=0):
    """Require the actual expanded Inspector's three visible editable axes."""
    marker = "VCR U1 GUI probe: inspector_axes_ready=true"
    for line in log_text[start_offset:].splitlines():
        if marker not in line:
            continue
        values = dict(token.split("=", 1) for token in line.split()
                      if "=" in token)
        try:
            count = int(values["count"])
            width = float(values["axis_w"])
            content_width = float(values["content_w"])
            viewport_width = float(values["viewport_w"])
            contrast = float(values["text_contrast"])
        except (KeyError, ValueError):
            return None
        if (count != 3 or not math.isfinite(width) or width < 40 or
                values.get("horizontal_hidden") != "true" or
                not math.isfinite(content_width) or
                not math.isfinite(viewport_width) or
                viewport_width < 150 or content_width < 0 or
                content_width > viewport_width + 1.5 or
                not math.isfinite(contrast) or contrast < 0.40):
            return None
        return {"count": count, "axisWidth": width,
                "horizontalHidden": True,
                "contentWidth": content_width, "viewportWidth": viewport_width,
                "textContrast": contrast}
    return None


def parse_workspace_resize_transition(log_text, mode, start_offset=0):
    """Require measured Unity layout after a real compact/expanded transition."""
    marker = "VCR U1 GUI probe: resize_layout_ready=true mode=" + mode
    for line in log_text[start_offset:].splitlines():
        if marker not in line:
            continue
        values = {}
        for token in line.split():
            if "=" not in token:
                continue
            key, value = token.split("=", 1)
            if key == "narrow_tabs":
                if value not in ("true", "false"):
                    return None
                values[key] = value == "true"
                continue
            if key not in ("width", "height", "bottom_h"):
                continue
            try:
                parsed = float(value)
            except ValueError:
                return None
            if not math.isfinite(parsed):
                return None
            values[key] = parsed
        if not all(k in values for k in ("width", "height", "bottom_h")) or \
                values["width"] < 256 or values["bottom_h"] < 95:
            return None
        if mode == "expanded" and values["height"] < 640:
            return None
        # A responsive tab strip consumes additional vertical space. Never
        # accept an expanded narrow layout with the old 150px dock.
        if mode == "expanded" and values.get("narrow_tabs") is True and \
                values["bottom_h"] < 180:
            return None
        if mode == "compact" and not 180 <= values["height"] < 640:
            return None
        return values
    return None


def await_workspace_resize_transition(player_log, mode, start_offset):
    deadline = time.monotonic() + 6
    while time.monotonic() < deadline:
        try:
            log_text = player_log.read_text(encoding="utf-8", errors="replace")
            found = parse_workspace_resize_transition(
                log_text, mode, start_offset)
            if found is not None:
                return found
        except OSError:
            pass
        time.sleep(0.15)
    raise RuntimeError(
        "Unity workspace failed to rebuild after native Windows " +
        mode + " transition")


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def inspect_unity_screenshot(path):
    """Inspect real PNG pixels without external image packages.

    A valid PNG header and dimensions alone can pass even if the Player
    captured a blank frame. Diversity is only a render-quality smoke
    signal, never proof of correct widgets or interactive acceptance.
    """
    if not path.is_file():
        return None
    data = path.read_bytes()
    if len(data) < 69 or data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    if data[12:16] != b"IHDR" or data[-8:-4] != b"IEND":
        return None
    width, height, depth, color, compression, filtering, interlace = struct.unpack(
        ">IIBBBBB", data[16:29])
    if not 64 <= width <= 8192 or not 64 <= height <= 8192:
        return None
    if (depth, color, compression, filtering, interlace) not in (
            (8, 2, 0, 0, 0), (8, 6, 0, 0, 0)):
        return None
    bpp = 3 if color == 2 else 4
    stride = width * bpp
    if height * (stride + 1) > 64 * 1024 * 1024:
        return None
    offset = 8
    compressed = []
    has_end = False
    while offset + 12 <= len(data):
        size = struct.unpack_from(">I", data, offset)[0]
        if size > len(data) - offset - 12:
            return None
        name = data[offset + 4:offset + 8]
        chunk = data[offset + 8:offset + 8 + size]
        if name == b"IDAT":
            compressed.append(chunk)
        if name == b"IEND":
            has_end = True
            break
        offset += size + 12
    if not has_end or not compressed:
        return None
    try:
        pixels = zlib.decompress(b"".join(compressed))
    except zlib.error:
        return None
    if len(pixels) != height * (stride + 1):
        return None

    sample_x = sorted({min(width - 1, (2 * i + 1) * width // 80)
                       for i in range(40)})
    sample_y = {min(height - 1, (2 * j + 1) * height // 60)
                for j in range(30)}
    previous = bytearray(stride)
    sampled = Counter()
    offset = 0
    for y in range(height):
        filter_type = pixels[offset]
        current = bytearray(pixels[offset + 1:offset + 1 + stride])
        offset += stride + 1
        if filter_type not in (0, 1, 2, 3, 4):
            return None
        if filter_type:
            for i in range(stride):
                left = current[i - bpp] if i >= bpp else 0
                above = previous[i]
                upper_left = previous[i - bpp] if i >= bpp else 0
                if filter_type == 1:
                    predictor = left
                elif filter_type == 2:
                    predictor = above
                elif filter_type == 3:
                    predictor = (left + above) // 2
                else:
                    p = left + above - upper_left
                    da, db, dc = abs(p-left), abs(p-above), abs(p-upper_left)
                    predictor = (left if da <= db and da <= dc else
                                 above if db <= dc else upper_left)
                current[i] = (current[i] + predictor) & 255
        if y in sample_y:
            for x in sample_x:
                sampled[bytes(current[x * bpp:x * bpp + 3])] += 1
        previous = current
    total = sum(sampled.values())
    dominant = max(sampled.values()) / total if total else 1.0
    diverse = len(sampled) >= 10 and dominant < 0.95
    return {
        "file": path.name,
        "sha256": sha256(path),
        "width": width,
        "height": height,
        "bytes": len(data),
        "sampledDistinctPixels": len(sampled),
        "sampledDominantFraction": round(dominant, 4),
        "pixelDiversityPassed": diverse,
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


def validated_visible_unity_frames(report):
    """Require live diverse pixels: startup OR BOTH measured resize frames.

    Hosted Unity can delay its initial opt-in Screenshot capture until after
    frame/UI initialization. Two fresh maximize/restore screenshots from the
    same actual Player process are stronger evidence than a missing startup
    shot. Never accept Win32 style, blank bitmap, or geometry alone.
    """
    startup = report.get("unityFrame") if report.get("unityFrameCaptured") else None
    if startup and startup.get("pixelDiversityPassed") is True:
        return "startup"
    controls = report.get("nativeWindowControls") or {}
    expanded = controls.get("expandedUnityFrame") or {}
    restored = controls.get("compactRestoredUnityFrame") or {}
    if (report.get("nativeWindowControlsPassed") is True and
            controls.get("passed") is True and
            expanded.get("pixelDiversityPassed") is True and
            restored.get("pixelDiversityPassed") is True and
            expanded.get("sha256") and restored.get("sha256") and
            expanded.get("file") == "unity-expanded.png" and
            restored.get("file") == "unity-restored.png"):
        return "maximized+restored"
    return None


def classify_native_input_targets(samples):
    """A native caption and both horizontal resize edges must be hit-testable."""
    left = any(item["result"] == HTLEFT for item in samples
               if item["target"] == "left")
    right = any(item["result"] == HTRIGHT for item in samples
                if item["target"] == "right")
    caption = any(item["result"] == HTCAPTION for item in samples
                  if item["target"] == "caption")
    return {
        "leftResizeHit": left,
        "rightResizeHit": right,
        "captionDragHit": caption,
        "passed": left and right and caption,
    }


def probe_native_input_targets(hwnd, frame):
    """Query live Win32 WM_NCHITTEST; no synthetic mouse click or drag.

    SetWindowPos can resize a non-interactively resizable window. These
    independent edge/title hit tests establish OS-recognized mouse targets,
    but do not replace an operator physically dragging the window.
    """
    user32 = ctypes.windll.user32
    user32.SendMessageTimeoutW.argtypes = [
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM,
        wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t)]
    user32.SendMessageTimeoutW.restype = wintypes.LPARAM
    left, top, right, bottom = frame["windowRect"]
    midpoint_y = (top + bottom) // 2
    midpoint_x = (left + right) // 2
    samples = []
    positions = [
        ("caption", midpoint_x, top + 20),
        *[("left", left + offset, midpoint_y) for offset in (2, 5, 8)],
        *[("right", right - offset, midpoint_y) for offset in (2, 5, 8)],
    ]
    # Unity may be busy submitting its first GPU screenshot when the CI
    # probes the caption. A single 800ms SendMessageTimeout does not prove
    # mouse hit testing is broken. Retry *live* OS responses with a finite
    # bound; never infer a passing result from window style/SetWindowPos.
    timeouts = []
    attempts = 0
    for attempt in range(3):
        attempts = attempt + 1
        evidence = classify_native_input_targets(samples)
        for target, x, y in positions:
            # Once a target has a genuine HT* response, avoid unnecessary
            # messages to Unity's render thread during startup.
            if (attempt > 0 and (
                    (target == "caption" and evidence["captionDragHit"]) or
                    (target == "left" and evidence["leftResizeHit"]) or
                    (target == "right" and evidence["rightResizeHit"]))):
                continue
            # WM_NCHITTEST LPARAM carries signed screen coordinates in
            # 16-bit halves, including negative multi-monitor coordinates.
            coords = ((y & 0xffff) << 16) | (x & 0xffff)
            outcome = ctypes.c_size_t(0)
            ok = user32.SendMessageTimeoutW(
                hwnd, WM_NCHITTEST, 0, coords, 0x0002, 1200,
                ctypes.byref(outcome))
            if not ok:
                timeouts.append({"target": target, "x": x, "y": y,
                                 "attempt": attempts})
                continue
            samples.append({"target": target, "x": x, "y": y,
                            "attempt": attempts,
                            "result": ctypes.c_ssize_t(outcome.value).value})
        if classify_native_input_targets(samples)["passed"]:
            break
        if attempt < 2:
            time.sleep(0.2)
    result = classify_native_input_targets(samples)
    result["samples"] = samples
    result["timeouts"] = timeouts
    result["attempts"] = attempts
    return result


def wait_for_workspace_screenshot(path, expected_layout, mode):
    """Require an actual PNG rendered by Unity at the measured layout size."""
    deadline = time.monotonic() + 5
    last = None
    while time.monotonic() < deadline:
        last = inspect_unity_screenshot(path)
        if last is not None and last["pixelDiversityPassed"] and all(
                abs(last[key] - expected_layout[key]) <= 8
                for key in ("width", "height")):
            return last
        time.sleep(0.15)
    raise RuntimeError(
        "Unity did not capture the " + mode +
        " layout at its measured dimensions: " + str(last))


def probe_native_window_controls(hwnd, before, player_log, output):
    """Exercise actual OS maximize/minimize/restore controls on the P1 HWND.

    This checks native window-state transitions, not user mouse interaction.
    Always restore the original normal window before the GUI close probe.
    """
    user32 = ctypes.windll.user32
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.ShowWindow.restype = wintypes.BOOL
    user32.IsZoomed.argtypes = [wintypes.HWND]
    user32.IsZoomed.restype = wintypes.BOOL
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    sw_maximize, sw_minimize, sw_restore = 3, 6, 9
    result = {
        "initialOuterSize": before["outerSize"],
        "maximized": False,
        "restoredFromMaximize": False,
        "minimized": False,
        "restoredFromMinimize": False,
        "restoredOriginalSize": False,
        "expandedLayout": None,
        "inspectorAxes": None,
        "expandedSinglePanelMove": False,
        "expandedCompactHeaderReady": False,
        "compactRestoredLayout": None,
        "compactSinglePanelMove": False,
        "compactHeaderReady": False,
        "compactWorkspaceClickTarget": None,
        "expandedUnityFrame": None,
        "compactRestoredUnityFrame": None,
        "passed": False,
    }

    def wait_until(predicate, label):
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            if predicate():
                return
            time.sleep(0.1)
        raise RuntimeError("Native window " + label + " timed out")

    def is_normal():
        return not user32.IsZoomed(hwnd) and not user32.IsIconic(hwnd)

    try:
        # Read the log before each transition to avoid accepting markers
        # from earlier GUI startup or a previous window-size change.
        log_offset = len(player_log.read_text(
            encoding="utf-8", errors="replace"))
        user32.ShowWindow(hwnd, sw_maximize)
        wait_until(lambda: bool(user32.IsZoomed(hwnd)), "maximize")
        result["maximized"] = True
        maximized_frame = inspect_native_window_frame(hwnd)
        result["maximizedOuterSize"] = maximized_frame["outerSize"]
        result["maximizedClientSize"] = maximized_frame["clientSize"]
        result["expandedLayout"] = await_workspace_resize_transition(
            player_log, "expanded", log_offset)
        # Read only the marker emitted before this transition's ready marker.
        # Otherwise a prior probe or unrelated hidden Inspector can silently
        # pass the geometry gate without testing any editable X/Y/Z control.
        expanded_log = player_log.read_text(encoding="utf-8", errors="replace")
        expanded_log = expanded_log[log_offset:]
        ready_at = expanded_log.find(
            "VCR U1 GUI probe: resize_layout_ready=true mode=expanded")
        result["inspectorAxes"] = parse_inspector_axes_evidence(
            expanded_log[:ready_at] if ready_at >= 0 else "")
        if result["inspectorAxes"] is None:
            raise RuntimeError(
                "Expanded Inspector X/Y/Z axes were not visibly measured")
        result["expandedSinglePanelMove"] = parse_single_panel_move_evidence(
            expanded_log[:ready_at] if ready_at >= 0 else "", "expanded")
        if not result["expandedSinglePanelMove"]:
            raise RuntimeError(
                "Expanded single Inspector header Move control not verified")
        # At the narrow expanded breakpoint, verify the compact toolbar
        # *in this transition*, not a marker from startup/another resize.
        result["expandedCompactHeaderReady"] = (
            maximized_frame["clientSize"][0] >= 1100 or
            parse_compact_header_evidence(
                expanded_log[:ready_at] if ready_at >= 0 else "",
                "expanded"))
        if not result["expandedCompactHeaderReady"]:
            raise RuntimeError(
                "Expanded narrow toolbar clipped primary actions or showed disabled tabs")
        # An expanded window that is tall but narrower than the responsive
        # breakpoint must actually have passed Unity's live auto-tab check.
        if maximized_frame["clientSize"][0] < 1100 and \
                result["expandedLayout"].get("narrow_tabs") is not True:
            raise RuntimeError(
                "Expanded narrow Unity workspace did not expose verified dock tabs")
        result["expandedUnityFrame"] = wait_for_workspace_screenshot(
            output / "unity-expanded.png", result["expandedLayout"], "expanded")

        log_offset = len(player_log.read_text(
            encoding="utf-8", errors="replace"))
        user32.ShowWindow(hwnd, sw_restore)
        wait_until(is_normal, "restore after maximize")
        result["restoredFromMaximize"] = True
        result["compactRestoredLayout"] = await_workspace_resize_transition(
            player_log, "compact", log_offset)
        compact_log = player_log.read_text(encoding="utf-8", errors="replace")
        compact_log = compact_log[log_offset:]
        ready_at = compact_log.find(
            "VCR U1 GUI probe: resize_layout_ready=true mode=compact")
        result["compactSinglePanelMove"] = parse_single_panel_move_evidence(
            compact_log[:ready_at] if ready_at >= 0 else "", "compact")
        if not result["compactSinglePanelMove"]:
            raise RuntimeError(
                "Restored compact single Inspector header Move not verified")
        result["compactHeaderReady"] = parse_compact_header_evidence(
            compact_log[:ready_at] if ready_at >= 0 else "", "compact")
        if not result["compactHeaderReady"]:
            raise RuntimeError(
                "Compact toolbar clipped primary actions or showed disabled tabs")
        result["compactWorkspaceClickTarget"] = parse_workspace_click_target_evidence(
            compact_log[:ready_at] if ready_at >= 0 else "")
        if result["compactWorkspaceClickTarget"] is None:
            raise RuntimeError(
                "Restored UI Toolkit workspace button has no measured click target")
        result["compactRestoredUnityFrame"] = wait_for_workspace_screenshot(
            output / "unity-restored.png", result["compactRestoredLayout"], "restored")

        user32.ShowWindow(hwnd, sw_minimize)
        wait_until(lambda: bool(user32.IsIconic(hwnd)), "minimize")
        result["minimized"] = True

        user32.ShowWindow(hwnd, sw_restore)
        wait_until(is_normal, "restore after minimize")
        result["restoredFromMinimize"] = True
        deadline = time.monotonic() + 3
        after = inspect_native_window_frame(hwnd)
        while time.monotonic() < deadline:
            if all(abs(after["outerSize"][i] - before["outerSize"][i]) <= 12
                   for i in range(2)):
                break
            time.sleep(0.1)
            after = inspect_native_window_frame(hwnd)
        result["finalOuterSize"] = after["outerSize"]
        result["restoredOriginalSize"] = all(
            abs(after["outerSize"][i] - before["outerSize"][i]) <= 12
            for i in range(2))
        result["passed"] = all((result["maximized"],
                                result["restoredFromMaximize"],
                                result["minimized"],
                                result["restoredFromMinimize"],
                                result["restoredOriginalSize"],
                                result["expandedLayout"] is not None,
                                result["inspectorAxes"] is not None,
                                result["expandedSinglePanelMove"],
                                result["expandedCompactHeaderReady"],
                                result["compactSinglePanelMove"],
                                result["compactHeaderReady"],
                                result["compactWorkspaceClickTarget"] is not None,
                                result["compactRestoredLayout"] is not None,
                                result["expandedUnityFrame"] is not None,
                                result["compactRestoredUnityFrame"] is not None))
        return result
    finally:
        # Do not leave the hosted Player minimized/maximized after failure.
        user32.ShowWindow(hwnd, sw_restore)


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


def probe_real_workspace_mouse_drag(hwnd, target, player_log, output,
                                    expected_marker):
    """Drag via Windows desktop mouse messages, not UI Toolkit test events.

    This verifies the tab's PointerDown/Move/Up path through a real Unity HWND,
    with hit-tested endpoints. It is automated and not human acceptance.
    """
    user32 = ctypes.windll.user32
    user32.ClientToScreen.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
    user32.ClientToScreen.restype = wintypes.BOOL
    user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
    user32.GetCursorPos.restype = wintypes.BOOL
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.SetCursorPos.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.mouse_event.argtypes = [
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
        wintypes.DWORD, ctypes.c_size_t]
    user32.mouse_event.restype = None
    user32.WindowFromPoint.argtypes = [wintypes.POINT]
    user32.WindowFromPoint.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    frame = inspect_native_window_frame(hwnd)
    width, height = frame["clientSize"]
    sx = width / target["rootWidth"]
    sy = height / target["rootHeight"]
    if not (0.5 <= sx <= 3 and 0.5 <= sy <= 3):
        raise RuntimeError("Drag coordinates cannot map Unity to Windows")
    pts = []
    for coords in (target["source"], target["destination"]):
        point = wintypes.POINT(round(coords[0] * sx), round(coords[1] * sy))
        if not (8 <= point.x < width - 8 and 8 <= point.y < height - 8):
            raise RuntimeError("Drag endpoint lies outside Player client area")
        if not user32.ClientToScreen(hwnd, ctypes.byref(point)):
            raise OSError("ClientToScreen failed for drag endpoint")
        pts.append(point)
    start, end = pts
    original = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(original)):
        raise OSError("GetCursorPos failed for drag")
    log_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    left_down = False
    try:
        user32.SetForegroundWindow(hwnd)
        if not user32.SetCursorPos(start.x, start.y):
            raise OSError("Cannot point to draggable Inspector tab")
        time.sleep(0.15)
        for point in (start, end):
            hit = user32.WindowFromPoint(point)
            if not hit or user32.GetAncestor(hit, 2) != hwnd:
                raise RuntimeError("Drag source or empty Dock target is covered")
        user32.mouse_event(0x0002, 0, 0, 0, 0)  # left button down
        left_down = True
        time.sleep(0.18)
        for step in range(1, 17):
            f = step / 16
            px = round(start.x + (end.x - start.x) * f)
            py = round(start.y + (end.y - start.y) * f)
            if not user32.SetCursorPos(px, py):
                raise OSError("Cannot drag Inspector tab to right Dock rail")
            time.sleep(0.06)
        time.sleep(0.18)
        user32.mouse_event(0x0004, 0, 0, 0, 0)  # left button up
        left_down = False
        deadline = time.monotonic() + 6
        while time.monotonic() < deadline:
            log = player_log.read_text(encoding="utf-8", errors="replace")
            if any(line.strip() == expected_marker
                   for line in log[log_offset:].splitlines()):
                frame = wait_for_workspace_screenshot(
                    output / "unity-feature-inspector-drag-restored.png",
                    {"width": target["rootWidth"],
                     "height": target["rootHeight"]},
                    "Inspector dragged into empty right Dock")
                return {"passed": True, "sourceScreenPoint": [
                            start.x, start.y],
                        "destinationScreenPoint": [end.x, end.y],
                        "unityFrame": frame, "physicalHumanAcceptance": False}
            time.sleep(0.12)
        evidence = [line.strip() for line in
                    player_log.read_text(encoding="utf-8", errors="replace")[
                        log_offset:].splitlines()
                    if "inspector_grip_" in line or
                    "feature_search_inspector_drag_restored=" in line]
        raise RuntimeError(
            "Real mouse drag did not restore Inspector to right Dock; " +
            "inputEvidence=" + repr(evidence[-12:]))
    finally:
        if left_down:
            user32.mouse_event(0x0004, 0, 0, 0, 0)
        user32.SetCursorPos(original.x, original.y)


def probe_real_workspace_mouse_click(hwnd, target, player_log, output,
                                     screenshot_name="unity-workspace-clicked.png",
                                     expected_marker=None):
    """Send a genuine desktop left-click to the measured Unity Button center.

    Unlike Panel.Pick, this crosses Win32 input routing and Unity's UI Toolkit
    event callback. It remains a narrowly scoped hosted-desktop smoke test,
    NOT human physical interaction or full operator acceptance.
    """
    user32 = ctypes.windll.user32
    user32.ClientToScreen.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
    user32.ClientToScreen.restype = wintypes.BOOL
    user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
    user32.GetCursorPos.restype = wintypes.BOOL
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.SetCursorPos.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.mouse_event.argtypes = [
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
        wintypes.DWORD, ctypes.c_size_t]
    user32.mouse_event.restype = None
    user32.WindowFromPoint.argtypes = [wintypes.POINT]
    user32.WindowFromPoint.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    frame = inspect_native_window_frame(hwnd)
    width, height = frame["clientSize"]
    scale_x = width / target["rootWidth"]
    scale_y = height / target["rootHeight"]
    if not (0.5 <= scale_x <= 3 and 0.5 <= scale_y <= 3):
        raise RuntimeError("Window/client size cannot map Unity click coordinates")
    point = wintypes.POINT(round(target["x"] * scale_x),
                           round(target["y"] * scale_y))
    if not (8 <= point.x < width - 8 and 8 <= point.y < height - 8):
        raise RuntimeError("Workspace click point falls outside Player client")
    if not user32.ClientToScreen(hwnd, ctypes.byref(point)):
        raise OSError("ClientToScreen failed for Player mouse target")
    previous = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(previous)):
        raise OSError("GetCursorPos failed before real desktop mouse click")
    expected_mode = ("advanced" if target["initialMode"] == "live" else "live")
    marker = (expected_marker or
              ("VCR U1 GUI probe: workspace_toggle_clicked=true " +
               "mode=" + expected_mode))
    offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    clicked = False
    try:
        # The hosted Windows GUI runner exposes a real visible Unity HWND.
        # A synthetic pointer event is sent through the OS (not Unity's
        # Click() helper), so an occluded/disabled control cannot pass.
        user32.SetForegroundWindow(hwnd)
        if not user32.SetCursorPos(point.x, point.y):
            raise OSError("SetCursorPos rejected live Unity mouse target")
        time.sleep(0.15)
        hit_hwnd = user32.WindowFromPoint(point)
        if not hit_hwnd or user32.GetAncestor(hit_hwnd, 2) != hwnd:
            raise RuntimeError(
                "Workspace mouse target is covered by a non-Unity window")
        user32.mouse_event(0x0002, 0, 0, 0, 0)  # LEFTDOWN
        time.sleep(0.06)
        user32.mouse_event(0x0004, 0, 0, 0, 0)  # LEFTUP
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            log = player_log.read_text(encoding="utf-8", errors="replace")
            if any(line.strip() == marker for line in log[offset:].splitlines()):
                clicked = True
                break
            time.sleep(0.15)
    finally:
        user32.SetCursorPos(previous.x, previous.y)
    if not clicked:
        raise RuntimeError(
            "Real desktop click did not invoke UI Toolkit workspace toggle: " +
            expected_mode)
    screenshot = wait_for_workspace_screenshot(
        output / screenshot_name,
        {"width": target["rootWidth"], "height": target["rootHeight"]},
        "workspace click")
    return {"passed": True, "target": target, "clientSize": [width, height],
            "screenPoint": [point.x, point.y],
            "observedNewMode": (expected_mode if expected_marker is None
                                else target["initialMode"]),
            "unityFrame": screenshot, "physicalHumanAcceptance": False}


def window_fits_work_area(rect, work):
    """Window must be entirely within the actual monitor's taskbar-free area."""
    return (len(rect) == 4 and len(work) == 4 and
            work[0] <= rect[0] < rect[2] <= work[2] and
            work[1] <= rect[1] < rect[3] <= work[3])


def visible_native_settings_windows(pid):
    """Find only the independently created Settings HWND from this Player."""
    user32 = ctypes.windll.user32
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    found = []
    def visitor(hwnd, _):
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value != pid or not user32.IsWindowVisible(hwnd):
            return True
        name = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, name, len(name))
        if name.value != "VCR.NativeSettings.Window.1":
            return True
        title = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd, title, len(title))
        found.append({"hwnd": int(hwnd), "title": title.value,
                      "class": name.value})
        return True
    if not user32.EnumWindows(callback_type(visitor), 0):
        raise OSError("Cannot enumerate native Settings windows")
    return found


def probe_native_settings_open(player_hwnd, pid, player_log, start_offset):
    """Exercise Settings through Win32 mouse input; require visible, on-screen HWND.

    This verifies the real native window independently of a Unity callback.
    It intentionally does not edit settings, depend on a screen bitmap, or
    claim physical operator interaction.
    """
    deadline = time.monotonic() + 4
    target = None
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        target = parse_settings_click_target_evidence(log, start_offset)
        if target is not None:
            break
        time.sleep(0.1)
    if target is None:
        raise RuntimeError("No fresh Unity Settings button coordinates after switch")
    user32 = ctypes.windll.user32
    user32.ClientToScreen.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
    user32.ClientToScreen.restype = wintypes.BOOL
    user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
    user32.GetCursorPos.restype = wintypes.BOOL
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.SetCursorPos.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.mouse_event.argtypes = [
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
        wintypes.DWORD, ctypes.c_size_t]
    user32.WindowFromPoint.argtypes = [wintypes.POINT]
    user32.WindowFromPoint.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    frame = inspect_native_window_frame(player_hwnd)
    w, h = frame["clientSize"]
    scale_x = w / target["rootWidth"]
    scale_y = h / target["rootHeight"]
    if not 0.5 <= scale_x <= 3 or not 0.5 <= scale_y <= 3:
        raise RuntimeError("Settings click target scale differs from Unity viewport")
    pt = wintypes.POINT(round(target["x"] * scale_x),
                        round(target["y"] * scale_y))
    if not (8 <= pt.x < w - 8 and 8 <= pt.y < h - 8):
        raise RuntimeError("Settings click point outside Unity viewport")
    if not user32.ClientToScreen(player_hwnd, ctypes.byref(pt)):
        raise OSError("ClientToScreen failed for Settings button")
    previous = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(previous)):
        raise OSError("GetCursorPos failed before Settings click")
    offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    marker = "VCR U1 GUI probe: settings_action_clicked=true native_visible=true"
    negative = "VCR U1 GUI probe: settings_action_clicked=true native_visible=false"
    window = None
    attempts = []
    try:
        # A hosted desktop can consume the first OS click to reactivate the
        # Player after screen capture. Exercise the same real mouse path at
        # most three times and require the actual Unity callback AND the real
        # native Settings HWND. Never synthesize a callback or a window.
        for attempt in range(1, 4):
            user32.SetForegroundWindow(player_hwnd)
            if not user32.SetCursorPos(pt.x, pt.y):
                raise OSError("Cannot position cursor over Settings button")
            time.sleep(0.25)
            hit = user32.WindowFromPoint(pt)
            if not hit or user32.GetAncestor(hit, 2) != player_hwnd:
                raise RuntimeError("Settings button covered by another OS window")
            user32.mouse_event(0x0002, 0, 0, 0, 0)
            time.sleep(0.09)
            user32.mouse_event(0x0004, 0, 0, 0, 0)
            attempts.append(attempt)
            deadline = time.monotonic() + 2.5
            while time.monotonic() < deadline:
                log = player_log.read_text(encoding="utf-8", errors="replace")
                events = [line.strip() for line in log[offset:].splitlines()]
                if negative in events:
                    raise RuntimeError(
                        "Settings callback ran but native window was not visible; " +
                        "inspect Player.log for native Settings fallback")
                candidates = visible_native_settings_windows(pid)
                if marker in events and len(candidates) == 1:
                    window = candidates[0]
                    break
                time.sleep(0.12)
            if window is not None:
                break
    finally:
        user32.SetCursorPos(previous.x, previous.y)
    if window is None:
        raise RuntimeError(
            "OS Settings click did not invoke callback and show native HWND " +
            "after " + str(len(attempts)) + " real click attempts")
    hwnd = window["hwnd"]
    rect = inspect_native_window_frame(hwnd)["windowRect"]
    class Rect(ctypes.Structure):
        _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                    ("right", ctypes.c_long), ("bottom", ctypes.c_long)]
    class MonitorInfo(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD),
                    ("monitor", Rect), ("work", Rect),
                    ("flags", wintypes.DWORD)]
    user32.MonitorFromWindow.argtypes = [wintypes.HWND, wintypes.DWORD]
    user32.MonitorFromWindow.restype = wintypes.HANDLE
    user32.GetMonitorInfoW.argtypes = [
        wintypes.HANDLE, ctypes.POINTER(MonitorInfo)]
    user32.GetMonitorInfoW.restype = wintypes.BOOL
    monitor = user32.MonitorFromWindow(hwnd, 2)
    info = MonitorInfo()
    info.cbSize = ctypes.sizeof(info)
    if not monitor or not user32.GetMonitorInfoW(monitor, ctypes.byref(info)):
        raise OSError("Cannot verify Settings monitor work area")
    work = [info.work.left, info.work.top, info.work.right, info.work.bottom]
    inside = window_fits_work_area(rect, work)
    if not inside:
        raise RuntimeError(
            "Native Settings window clipped by monitor work area: " +
            str({"window": rect, "work": work}))
    return {"passed": True, "window": window, "rect": rect,
            "monitorWorkArea": work, "withinWorkArea": True,
            "target": target, "screenPoint": [pt.x, pt.y],
            "clickAttempts": len(attempts),
            "physicalHumanAcceptance": False}


def native_settings_page_visibility(selected, controls):
    """Only the selected native page's representative child may be visible."""
    return (selected in (0, 1, 2, 3) and
            all(bool(controls.get(category)) == (category == selected)
                for category in range(4)))


def probe_native_settings_navigation(settings_hwnd, player_log):
    """Click all four real Win32 sidebar tabs and verify native page contents.

    Event evidence comes from Unity handling actual WM_COMMAND messages after
    OS mouse input. Child visibility comes independently from user32, so
    a logged action cannot mask an unchanged or obscured Settings page.
    """
    user32 = ctypes.windll.user32
    user32.GetDlgItem.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetDlgItem.restype = wintypes.HWND
    user32.GetWindowRect.argtypes = [
        wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetWindowRect.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
    user32.GetCursorPos.restype = wintypes.BOOL
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.SetCursorPos.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.WindowFromPoint.argtypes = [wintypes.POINT]
    user32.WindowFromPoint.restype = wintypes.HWND
    user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    user32.GetAncestor.restype = wintypes.HWND
    user32.mouse_event.argtypes = [
        wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
        wintypes.DWORD, ctypes.c_size_t]

    # Render-scale EDIT, model-path EDIT, Refresh tracking, output toggle.
    page_ids = (10, 12, 450, 401)
    def page_visibility():
        controls = {}
        for category, child_id in enumerate(page_ids):
            handle = user32.GetDlgItem(settings_hwnd, child_id)
            if not handle:
                raise RuntimeError(
                    "Native Settings has no page control " + str(child_id))
            controls[category] = bool(user32.IsWindowVisible(handle))
        return controls

    initial = page_visibility()
    if not native_settings_page_visibility(0, initial):
        raise RuntimeError("Native Settings did not open on Render page: " +
                           str(initial))
    cursor = wintypes.POINT()
    if not user32.GetCursorPos(ctypes.byref(cursor)):
        raise OSError("Cannot preserve cursor before Settings tab navigation")
    result = []
    try:
        for category in (1, 2, 3, 0):
            button = user32.GetDlgItem(settings_hwnd, 100 + category)
            if not button or not user32.IsWindowVisible(button):
                raise RuntimeError(
                    "Native Settings sidebar tab missing: " + str(category))
            bounds = wintypes.RECT()
            if not user32.GetWindowRect(button, ctypes.byref(bounds)):
                raise OSError("Cannot measure native Settings sidebar button")
            point = wintypes.POINT((bounds.left + bounds.right) // 2,
                                   (bounds.top + bounds.bottom) // 2)
            if bounds.right - bounds.left < 40 or bounds.bottom - bounds.top < 24:
                raise RuntimeError("Native Settings sidebar tab is clipped")
            before = len(player_log.read_text(
                encoding="utf-8", errors="replace"))
            marker = ("VCR U1 GUI probe: native_settings_category_selected=true " +
                      "category=" + str(category) + " native_visible=true")
            user32.SetForegroundWindow(settings_hwnd)
            if not user32.SetCursorPos(point.x, point.y):
                raise OSError("Cannot place mouse over native Settings tab")
            time.sleep(0.12)
            hit = user32.WindowFromPoint(point)
            if not hit or hit != button:
                raise RuntimeError(
                    "Native Settings sidebar covered or cannot receive click: " +
                    str(category))
            user32.mouse_event(0x0002, 0, 0, 0, 0)
            time.sleep(0.07)
            user32.mouse_event(0x0004, 0, 0, 0, 0)
            deadline = time.monotonic() + 4
            evidence = None
            while time.monotonic() < deadline:
                log = player_log.read_text(encoding="utf-8", errors="replace")
                if any(line.strip() == marker
                       for line in log[before:].splitlines()):
                    controls = page_visibility()
                    if native_settings_page_visibility(category, controls):
                        evidence = {"page": category, "callbackObserved": True,
                                    "controlsVisible": controls,
                                    "screenPoint": [point.x, point.y]}
                        break
                time.sleep(0.1)
            if evidence is None:
                raise RuntimeError(
                    "Actual native Settings tab click did not switch page " +
                    str(category) + " and show its own controls")
            result.append(evidence)
    finally:
        user32.SetCursorPos(cursor.x, cursor.y)
    return {"passed": True, "visitedPages": [x["page"] for x in result],
            "steps": result, "physicalHumanAcceptance": False}


def probe_native_settings_close_reopen(player_hwnd, pid, player_log,
                                      target_log_offset, opened):
    """Close the real native window, then re-open it using the Unity button.

    WM_CLOSE must hide, not destroy or duplicate, the independent Settings
    window. Do not apply/edit runtime configuration during this smoke.
    """
    user32 = ctypes.windll.user32
    user32.IsWindow.argtypes = [wintypes.HWND]
    user32.IsWindow.restype = wintypes.BOOL
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.PostMessageW.argtypes = [
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.PostMessageW.restype = wintypes.BOOL
    hwnd = opened["window"]["hwnd"]
    if not user32.IsWindow(hwnd) or not user32.IsWindowVisible(hwnd):
        raise RuntimeError("Native Settings missing before WM_CLOSE lifecycle test")
    if not user32.PostMessageW(hwnd, WM_CLOSE, 0, 0):
        raise OSError("Unable to post native Settings WM_CLOSE")
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline and user32.IsWindowVisible(hwnd):
        time.sleep(0.10)
    if user32.IsWindowVisible(hwnd) or not user32.IsWindow(hwnd):
        raise RuntimeError(
            "Native Settings WM_CLOSE did not hide and preserve its HWND")
    if visible_native_settings_windows(pid):
        raise RuntimeError("Native Settings window still visible after WM_CLOSE")
    reopened = probe_native_settings_open(
        player_hwnd, pid, player_log, target_log_offset)
    if reopened["window"]["hwnd"] != hwnd:
        raise RuntimeError(
            "Settings reopen created a duplicate native HWND instead of reusing it")
    return {"passed": True, "hiddenByNativeClose": True,
            "reopenedByRealMouse": True, "reusedWindow": True,
            "reopenClickAttempts": reopened["clickAttempts"],
            "reopenedRect": reopened["rect"],
            "physicalHumanAcceptance": False}


def probe_feature_search_keyboard(player_hwnd, settings_hwnd, player_log, output):
    """Press real Ctrl+K, type fov, and navigate by Enter on the hosted desktop.

    The registry, palette, filtered result and target all must provide fresh
    Unity markers. The post-search Unity screenshot must also contain diverse
    pixels. This is automated OS keyboard input, NOT human/IME acceptance.
    """
    user32 = ctypes.windll.user32
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.PostMessageW.argtypes = [
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.PostMessageW.restype = wintypes.BOOL
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.restype = wintypes.BOOL
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.keybd_event.argtypes = [
        wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]
    user32.keybd_event.restype = None
    if not user32.PostMessageW(settings_hwnd, WM_CLOSE, 0, 0):
        raise OSError("Cannot hide native Settings before palette keyboard test")
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline and user32.IsWindowVisible(settings_hwnd):
        time.sleep(0.1)
    if user32.IsWindowVisible(settings_hwnd):
        raise RuntimeError("Native Settings covered the Unity search shortcut")
    user32.SetForegroundWindow(player_hwnd)
    deadline = time.monotonic() + 3
    while time.monotonic() < deadline and user32.GetForegroundWindow() != player_hwnd:
        time.sleep(0.1)
    if user32.GetForegroundWindow() != player_hwnd:
        raise RuntimeError("Player never received foreground keyboard focus")

    def key(vk):
        user32.keybd_event(vk, 0, 0, 0)
        time.sleep(0.06)
        user32.keybd_event(vk, 0, 0x0002, 0)
        time.sleep(0.10)

    opened = "VCR U1 GUI probe: feature_search_opened=true"
    indexed_prefix = "VCR U1 GUI probe: feature_search_registry_ready=true indexed="
    offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    user32.keybd_event(0x11, 0, 0, 0)  # Ctrl DOWN
    key(0x4b)  # K
    user32.keybd_event(0x11, 0, 0x0002, 0)  # Ctrl UP
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        lines = [line.strip() for line in log[offset:].splitlines()]
        index_ready = any(
            line.startswith(indexed_prefix) and
            line[len(indexed_prefix):].isdigit() and
            int(line[len(indexed_prefix):]) >= 20 for line in lines)
        if opened in lines and index_ready:
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Real Ctrl+K did not open and validate feature palette")
    # Exercise the real ESC handler and return to the same palette by the
    # native Ctrl+K shortcut; do not treat a prior 'opened' log as a reopen.
    escape_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    key(0x1b)  # Esc
    escaped = "VCR U1 GUI probe: feature_search_esc_closed=true"
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == escaped
               for line in log[escape_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Real Esc failed to close the feature palette")

    reopen_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    user32.keybd_event(0x11, 0, 0, 0)  # Ctrl DOWN
    key(0x4b)  # K
    user32.keybd_event(0x11, 0, 0x0002, 0)  # Ctrl UP
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == opened
               for line in log[reopen_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Ctrl+K did not reopen feature search after real Esc")
    offset = reopen_offset
    # UI Toolkit's focus scheduling occurs after the shortcut event dispatch.
    time.sleep(0.30)
    query_marker = ("VCR U1 GUI probe: feature_search_query_ready=true "
                    "query=fov first=camera.fov")
    for vk in (0x46, 0x4f, 0x56):  # F, O, V
        key(vk)
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == query_marker
               for line in log[offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "OS keyboard 'fov' did not filter palette to camera.fov")
    key(0x0d)  # Enter: navigation, never Apply or model unload.
    navigated = ("VCR U1 GUI probe: feature_search_navigated=true "
                 "id=camera.fov target=vcr-search-camera-fov mode=advanced")
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == navigated
               for line in log[offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Real Enter did not navigate to current Camera FOV control")
    frame = inspect_native_window_frame(player_hwnd)
    screenshot = wait_for_workspace_screenshot(
        output / "unity-feature-searched.png",
        {"width": frame["clientSize"][0], "height": frame["clientSize"][1]},
        "feature search")
    # The Inspector is initially the active right-hand tab in the hosted
    # compact layout. Search must select an actually hidden Advanced tab,
    # not merely find the text of a detached Materials panel.
    offset2 = len(player_log.read_text(encoding="utf-8", errors="replace"))
    user32.keybd_event(0x11, 0, 0, 0)  # Ctrl DOWN
    key(0x4b)  # K
    user32.keybd_event(0x11, 0, 0x0002, 0)  # Ctrl UP
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == opened
               for line in log[offset2:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Real Ctrl+K did not reopen feature search")
    time.sleep(0.3)
    for vk in (0x4d, 0x41, 0x54, 0x45, 0x52, 0x49, 0x41, 0x4c, 0x53):
        key(vk)  # MATERIALS
    materials_query = (
        "VCR U1 GUI probe: feature_search_query_ready=true "
        "query=materials first=materials")
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == materials_query
               for line in log[offset2:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("OS keyboard MATERIALS did not find Advanced panel")
    key(0x0d)
    materials_navigation = (
        "VCR U1 GUI probe: feature_search_navigated=true "
        "id=materials target=vcr-panel-materials mode=advanced")
    visible = (
        "VCR U1 GUI probe: feature_search_panel_visible=true "
        "id=materials target_visible=true dock_visible=true mode=advanced")
    viewport_marker = (
        "VCR U1 GUI probe: feature_search_materials_layout_ready=true "
        "viewport_clipped=true panel_above_bottom=true scroll_height=")
    viewport_height = None
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        lines = [line.strip() for line in log[offset2:].splitlines()]
        metrics = [
            line for line in lines if line.startswith(viewport_marker)]
        if metrics:
            try:
                viewport_height = float(
                    metrics[-1][len(viewport_marker):])
            except ValueError:
                viewport_height = None
        if (materials_navigation in lines and visible in lines and
                viewport_height is not None and
                math.isfinite(viewport_height) and viewport_height >= 35):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Advanced Materials search did not activate a clipped right-dock "
            "viewport wholly above bottom dock: " +
            str({"navigation": materials_navigation in lines,
                 "tab": visible in lines, "viewportHeight": viewport_height,
                 "viewportEvidence": metrics[-1] if metrics else None}))
    materials_screenshot = wait_for_workspace_screenshot(
        output / "unity-feature-materials.png",
        {"width": frame["clientSize"][0], "height": frame["clientSize"][1]},
        "Materials feature navigation")

    # Start from an OS-confirmed Live mode, not an inferred default.
    # The first Materials result is already visible; Unity publishes the
    # measured live toolbar button center at the end of the real UI layout.
    back_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        back_target = parse_feature_search_workspace_target(log, offset2)
        if back_target is not None:
            break
        time.sleep(0.12)
    if back_target is None:
        raise RuntimeError("No visible/OS-hit-testable Live toolbar target")
    back_click_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    back_click = probe_real_workspace_mouse_click(
        player_hwnd, back_target, player_log, output,
        screenshot_name="unity-feature-live.png")
    if back_click["observedNewMode"] != "live":
        raise RuntimeError("Cannot establish real Live starting state")
    # Build() replaces the clicked Button while its click event is still
    # dispatching. An immediate focus query can legitimately be false;
    # the shell retries focus after dispatch. Only the settled focus counts.
    focus_marker = "VCR U1 GUI probe: workspace_focus_post_dispatch=true"
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == focus_marker
               for line in log[back_click_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Workspace mode rebuild failed to restore keyboard focus "
            "after click dispatch")

    # Search Advanced-only Materials from Live. The UI must reveal it but
    # leave the operator's persisted WorkspaceModeKey set to Live.
    offset3 = len(player_log.read_text(encoding="utf-8", errors="replace"))
    user32.SetForegroundWindow(player_hwnd)
    user32.keybd_event(0x11, 0, 0, 0)
    key(0x4b)
    user32.keybd_event(0x11, 0, 0x0002, 0)
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == opened
               for line in log[offset3:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Ctrl+K did not open palette from Live")
    time.sleep(0.30)
    for vk in (0x4d, 0x41, 0x54, 0x45, 0x52, 0x49, 0x41, 0x4c, 0x53):
        key(vk)
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == materials_query
               for line in log[offset3:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("MATERIALS query from Live did not select Materials")
    key(0x0d)
    preserved = (
        "VCR U1 GUI probe: feature_search_mode_preserved=true "
        "id=materials from=live to=advanced persisted=live")
    layout_preserved = (
        "VCR U1 GUI probe: feature_search_layout_preserved=true id=materials")
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        lines = [line.strip() for line in log[offset3:].splitlines()]
        metrics = [
            line for line in lines if line.startswith(viewport_marker)]
        if (preserved in lines and layout_preserved in lines and
                materials_navigation in lines and visible in lines and metrics):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Materials from Live failed to reveal Advanced without changing "
            "the persisted Live mode, dock layout or right-dock viewport")
    from_live_screenshot = wait_for_workspace_screenshot(
        output / "unity-feature-materials-from-live.png",
        {"width": frame["clientSize"][0], "height": frame["clientSize"][1]},
        "Materials auto Advanced navigation")
    # Hide the actual left dock using its measured, pointer-receiving
    # toolbar button. Search must reveal the Scenes panel WITHOUT altering
    # the user's saved hidden-dock or active-tab preference.
    hide_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        hide_target = parse_feature_search_hide_left_target(log, offset3)
        if hide_target is not None:
            break
        time.sleep(0.12)
    if hide_target is None:
        raise RuntimeError("No visible OS-hit-testable Hide left button after Materials search")
    hide_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    hidden_click = probe_real_workspace_mouse_click(
        player_hwnd, hide_target, player_log, output,
        screenshot_name="unity-feature-left-hidden.png",
        expected_marker=(
            "VCR U1 GUI probe: left_dock_hidden=true persisted_hidden=true"))
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == focus_marker
               for line in log[hide_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Hide left rebuild lost keyboard focus after OS click")

    query_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    user32.SetForegroundWindow(player_hwnd)
    user32.keybd_event(0x11, 0, 0, 0)
    key(0x4b)
    user32.keybd_event(0x11, 0, 0x0002, 0)
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == opened
               for line in log[query_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Ctrl+K did not reopen search with left dock hidden")
    time.sleep(0.30)
    for vk in (0x53, 0x43, 0x45, 0x4e, 0x45, 0x20,
               0x4d, 0x41, 0x4e, 0x41, 0x47, 0x45, 0x4d, 0x45, 0x4e, 0x54):
        key(vk)  # SCENE MANAGEMENT
    scene_query = (
        "VCR U1 GUI probe: feature_search_query_ready=true "
        "query=scene management first=scene.list")
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == scene_query
               for line in log[query_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError("Hidden dock search did not rank Scene management first")
    key(0x0d)
    expected_scene = (
        "VCR U1 GUI probe: feature_search_navigated=true "
        "id=scene.list target=vcr-panel-scenes mode=advanced")
    expected_revealed = (
        "VCR U1 GUI probe: feature_search_hidden_left_revealed=true "
        "visual_visible=true persisted_hidden=true")
    scene_preserved = (
        "VCR U1 GUI probe: feature_search_layout_preserved=true id=scene.list")
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        lines = [line.strip() for line in log[query_offset:].splitlines()]
        if (expected_scene in lines and expected_revealed in lines and
                scene_preserved in lines):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Hidden left dock search did not reveal Scenes while preserving "
            "the operator's saved hidden-dock and active-tab layout")
    scene_screenshot = wait_for_workspace_screenshot(
        output / "unity-feature-scenes-revealed.png",
        {"width": frame["clientSize"][0], "height": frame["clientSize"][1]},
        "Scenes revealed from hidden left dock")
    # Real manual Live click must supersede temporary search selection and
    # restore the saved hidden-left preference as visible UI state.
    manual_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        manual_target = parse_feature_search_workspace_target(log, query_offset)
        if manual_target is not None:
            break
        time.sleep(0.12)
    if manual_target is None:
        raise RuntimeError("No visible Live toggle after hidden Scenes search")
    manual_offset = len(player_log.read_text(encoding="utf-8", errors="replace"))
    manual_live = probe_real_workspace_mouse_click(
        player_hwnd, manual_target, player_log, output,
        screenshot_name="unity-feature-hidden-return-live.png")
    hidden_reset_marker = (
        "VCR U1 GUI probe: feature_search_hidden_reset=true "
        "visual_hidden=true persisted_hidden=true temporary_tabs=0 "
        "mode=live persisted_mode=live")
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() == hidden_reset_marker
               for line in log[manual_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Manual Live click failed to restore saved hidden Dock and "
            "discard the search-only tab selection")

    # The compact Inspector Move control is an actual clickable button.
    # Exercise it after search and require its persisted destination, active
    # tab and the previously hidden left dock to remain consistent.
    move_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        move_target = parse_feature_search_inspector_move_target(log, manual_offset)
        if move_target is not None:
            break
        time.sleep(0.12)
    if move_target is None:
        raise RuntimeError("Inspector Move button is not clickable after search reset")
    expected_move = (
        "VCR U1 GUI probe: feature_search_inspector_moved=true "
        "destination=bottom panel_visible=true persisted_dock=true "
        "selected_tab=true temporary_tabs=0 hidden_left_preserved=true "
        "empty_right_rail=true preview_expanded=true")
    moved_log_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    moved = probe_real_workspace_mouse_click(
        player_hwnd, move_target, player_log, output,
        screenshot_name="unity-feature-inspector-moved.png",
        expected_marker=expected_move)
    tracking_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        tracking_target = parse_feature_search_tracking_tab_target(
            log, moved_log_offset)
        if tracking_target is not None:
            break
        time.sleep(0.12)
    if tracking_target is None:
        raise RuntimeError("Tracking tab is not clickable after Inspector moved")
    expected_tab = (
        "VCR U1 GUI probe: feature_search_tracking_tab_clicked=true "
        "visible=true inspector_hidden=true persisted=true "
        "temporary_tabs=0 hidden_left_preserved=true")
    tracking_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    tracking_click = probe_real_workspace_mouse_click(
        player_hwnd, tracking_target, player_log, output,
        screenshot_name="unity-feature-tracking-tab.png",
        expected_marker=expected_tab)
    drag_target = None
    deadline = time.monotonic() + 4
    while time.monotonic() < deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        drag_target = parse_feature_search_inspector_drag_target(
            log, tracking_offset)
        if drag_target is not None:
            break
        time.sleep(0.12)
    if drag_target is None:
        raise RuntimeError("Inspector tab or right Dock rail cannot accept drag")
    expected_drag = (
        "VCR U1 GUI probe: feature_search_inspector_drag_restored=true "
        "inspector_visible=true right_width_restored=true persisted_dock=true "
        "selected_tab=true tracking_selected=true temporary_tabs=0 "
        "hidden_left_preserved=true")
    snapshot_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    drag_restored = probe_real_workspace_mouse_drag(
        player_hwnd, drag_target, player_log, output, expected_drag)
    save_target = wait_for_layout_snapshot_target(
        player_log, "save", snapshot_offset)
    save_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    saved = probe_real_workspace_mouse_click(
        player_hwnd, save_target, player_log, output,
        screenshot_name="unity-feature-snapshot-saved.png",
        expected_marker="VCR U1 GUI probe: layout_snapshot_saved=true")
    hide_target = wait_for_layout_snapshot_target(player_log, "hide", save_offset)
    hide_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    hidden_right = probe_real_workspace_mouse_click(
        player_hwnd, hide_target, player_log, output,
        screenshot_name="unity-feature-snapshot-right-hidden.png",
        expected_marker=("VCR U1 GUI probe: layout_snapshot_right_hidden=true "
                         "persisted_hidden=true"))
    # Prove the hidden state was written by the periodic autosave first.
    # Without separate checkpoint/recovery files, Restore will now revert
    # to the hidden state and this roundtrip must fail.
    auto_deadline = time.monotonic() + 8
    while time.monotonic() < auto_deadline:
        log = player_log.read_text(encoding="utf-8", errors="replace")
        if any(line.strip() ==
               "VCR U1 GUI probe: layout_snapshot_autosaved_hidden=true"
               for line in log[hide_offset:].splitlines()):
            break
        time.sleep(0.12)
    else:
        raise RuntimeError(
            "Layout hidden state was never persisted by automatic recovery")
    restore_target = wait_for_layout_snapshot_target(
        player_log, "restore", hide_offset)
    restore_expected = (
        "VCR U1 GUI probe: layout_snapshot_restored=true "
        "inspector_visible=true full_right=true dock_persisted=true "
        "right_tab_persisted=true tracking_tab_persisted=true "
        "left_hidden=true temporary_tabs=0")
    restore_offset = len(player_log.read_text(
        encoding="utf-8", errors="replace"))
    restored = probe_real_workspace_mouse_click(
        player_hwnd, restore_target, player_log, output,
        screenshot_name="unity-feature-snapshot-restored.png",
        expected_marker=restore_expected)
    final_target = wait_for_layout_snapshot_target(
        player_log, "hide-final", restore_offset)
    # The runtime intentionally uses the same screenshot filename for both
    # Hide-right clicks. Archive the first capture and require a genuinely new
    # PNG for the last click; never mistake an old frame for new evidence.
    initial_hide_png = output / "unity-feature-snapshot-right-hidden.png"
    archived_hide_png = output / "unity-feature-snapshot-right-hidden-initial.png"
    archived_hide_png.unlink(missing_ok=True)
    initial_hide_png.rename(archived_hide_png)
    final_hidden = probe_real_workspace_mouse_click(
        player_hwnd, final_target, player_log, output,
        screenshot_name="unity-feature-snapshot-right-hidden.png",
        expected_marker=("VCR U1 GUI probe: layout_snapshot_right_hidden=true "
                         "persisted_hidden=true"))
    return {"passed": True, "shortcut": "Ctrl+K",
            "escapeReopenPassed": True, "query": "fov",
            "resultId": "camera.fov", "targetName": "vcr-search-camera-fov",
            "postSearchFrame": screenshot,
            "advancedPanelSearch": {
                "query": "materials", "resultId": "materials",
                "targetName": "vcr-panel-materials",
                "visibleRightDock": True, "scrollClippedAboveBottom": True,
                "viewportHeight": viewport_height,
                "postSearchFrame": materials_screenshot},
            "liveToAdvancedSearch": {
                "liveClickObserved": True, "modePersistedLive": True,
                "fullLayoutSignaturePreserved": True,
                "advancedPanelRevealed": True,
                "liveFrame": back_click["unityFrame"],
                "postSearchFrame": from_live_screenshot},
            "hiddenDockSearch": {
                "realHideLeftClick": True,
                "query": "scene management",
                "resultId": "scene.list",
                "persistedHidden": True,
                "fullLayoutSignaturePreserved": True,
                "leftDockRevealedInSession": True,
                "manualLiveRestoresSavedHidden": True,
                "manualLiveFrame": manual_live["unityFrame"],
                "hiddenFrame": hidden_click["unityFrame"],
                "revealedFrame": scene_screenshot},
            "manualPanelMove": {
                "realMoveClick": True,
                "destinationDockPersisted": True,
                "destinationTabSelected": True,
                "searchOverrideCleared": True,
                "hiddenLeftPreserved": True,
                "emptyRightDockRail": True,
                "previewExpanded": True,
                "trackingTabClicked": True,
                "trackingTabPersisted": True,
                "trackingTabVisible": True,
                "dragBackToEmptyRight": drag_restored["passed"],
                "rightDockWidthRestored": True,
                "rightDockTabSelected": True,
                "leftHiddenAfterDrag": True,
                "snapshotSaveClicked": saved["passed"],
                "snapshotHideRightClicked": hidden_right["passed"],
                "snapshotAutoSaveObserved": True,
                "snapshotRestoreClicked": restored["passed"],
                "snapshotFinalRightHidden": final_hidden["passed"],
                "snapshotRightDockRestored": True,
                "snapshotBottomTabPreserved": True,
                "snapshotLeftHiddenPreserved": True,
                "snapshotRestoredFrame": restored["unityFrame"],
                "dragBackFrame": drag_restored["unityFrame"],
                "trackingTabFrame": tracking_click["unityFrame"],
                "movedFrame": moved["unityFrame"]},
            "physicalHumanAcceptance": False}


def probe_layout_restart_after_autosave(executable, args, output):
    """Verify automatic recovery across a real Player exit/relaunch.

    The first Player ends with right hidden but a manually saved right-visible
    checkpoint. The second must load the hidden automatic file and then use
    the real Restore button to recover the visible manual checkpoint.
    """
    folder = output / "restart"
    folder.mkdir(parents=True, exist_ok=True)
    player_log = folder / "Player.log"
    image_path = folder / "unity-frame.png"
    for file in folder.iterdir():
        if file.is_file():
            file.unlink()
    command = [
        str(executable),
        "-screen-fullscreen", "0",
        "-screen-width", str(args.screen_width),
        "-screen-height", str(args.screen_height),
        "-logFile", str(player_log),
        "--vcr-config=" + str(output / "gui-probe-config.json"),
        "--vcr-ui-probe", "--vcr-ui-probe-restart",
        "--vcr-ui-probe-image=" + str(image_path),
    ]
    process = subprocess.Popen(
        command, cwd=str(executable.parent),
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    closed = False
    try:
        deadline = time.monotonic() + args.startup_timeout
        window = None
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError("Restart Player exited before its GUI appeared")
            candidates = visible_player_windows(process.pid)
            if candidates:
                window = candidates[0]
                break
            time.sleep(0.15)
        if window is None:
            raise RuntimeError("Restart Player did not expose a UnityWndClass")
        require_runtime_startup(process, player_log, deadline)
        marker = (
            "VCR U1 GUI probe: layout_restart_autosave_loaded=true "
            "snapshot_loaded=true right_hidden=true left_hidden=true "
            "inspector_right=true tracking_bottom=true")
        while time.monotonic() < deadline:
            log = player_log.read_text(encoding="utf-8", errors="replace")
            if any(line.strip() == marker for line in log.splitlines()):
                break
            time.sleep(0.12)
        else:
            raise RuntimeError(
                "Restart did not load the hidden-right automatic recovery state")
        frame = inspect_native_window_frame(window["hwnd"])
        bounds = dict(zip(("width", "height"), frame["clientSize"]))
        # Use the genuine source-matched startup screenshot, produced after
        # workspace_tree_ready on THIS second process. The runtime marker
        # above independently certifies the restored persisted dock state.
        auto_frame = wait_for_workspace_screenshot(
            image_path, bounds, "restarted automatic recovery")
        restore_target = wait_for_layout_snapshot_target(player_log, "restore", 0)
        expected_restore = (
            "VCR U1 GUI probe: layout_snapshot_restored=true "
            "inspector_visible=true full_right=true dock_persisted=true "
            "right_tab_persisted=true tracking_tab_persisted=true "
            "left_hidden=true temporary_tabs=0")
        restored = probe_real_workspace_mouse_click(
            window["hwnd"], restore_target, player_log, folder,
            screenshot_name="unity-feature-snapshot-restored.png",
            expected_marker=expected_restore)
        request_normal_close(window["hwnd"])
        try:
            returncode = process.wait(timeout=args.quit_timeout)
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("Restart Player WM_CLOSE did not exit") from error
        closed = True
        final_log = player_log.read_text(encoding="utf-8", errors="replace")
        if returncode != 0 or QUIT_MARKER not in final_log or (
                find_workspace_runtime_exceptions(final_log)):
            raise RuntimeError("Restart Player failed clean quit or emitted UI errors")
        return {
            "passed": True, "autoSnapshotLoaded": True,
            "hiddenRightRecovered": True, "hiddenLeftRecovered": True,
            "manualCheckpointRestored": restored["passed"],
            "autoFrame": auto_frame, "restoredFrame": restored["unityFrame"],
            "managedQuitObserved": True, "exitCode": returncode,
            "physicalHumanAcceptance": False,
        }
    finally:
        if not closed and process.poll() is None:
            process.kill()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                pass


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
    # Use real decodable PNG fixtures (no Pillow dependency) to prove a blank
    # screenshot fails while a diverse RGB screenshot passes.
    with tempfile.TemporaryDirectory() as directory:
        def png_chunk(name, payload):
            return (struct.pack(">I", len(payload)) + name + payload +
                    struct.pack(">I", zlib.crc32(name + payload) & 0xffffffff))

        def write_png(path, colorful):
            rows = bytearray()
            for y in range(96):
                rows.append(0)  # PNG None filter
                for x in range(96):
                    rows.extend((x * 2, y * 2, (x + y) % 256)
                                if colorful else (25, 25, 25))
            header = struct.pack(">IIBBBBB", 96, 96, 8, 2, 0, 0, 0)
            path.write_bytes(
                b"\x89PNG\r\n\x1a\n" +
                png_chunk(b"IHDR", header) +
                png_chunk(b"IDAT", zlib.compress(rows)) +
                png_chunk(b"IEND", b""))

        flat = Path(directory) / "uniform.png"
        varied = Path(directory) / "diverse.png"
        write_png(flat, False)
        write_png(varied, True)
        flat_result = inspect_unity_screenshot(flat)
        varied_result = inspect_unity_screenshot(varied)
        if flat_result is None or flat_result["pixelDiversityPassed"]:
            raise RuntimeError("Uniform PNG falsely accepted as rendered UI")
        if varied_result is None or not varied_result["pixelDiversityPassed"]:
            raise RuntimeError("Diverse PNG falsely rejected")
        flat.write_bytes(flat.read_bytes()[:-20])
        if inspect_unity_screenshot(flat) is not None:
            raise RuntimeError("Truncated PNG incorrectly accepted")

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

    transition = ("VCR U1 GUI probe: resize_layout_ready=true" +
                  " mode=expanded width=962 height=730 bottom_h=210 narrow_tabs=true")
    if parse_workspace_resize_transition(transition, "expanded") is None:
        raise RuntimeError("Valid expanded transition marker rejected")
    if parse_workspace_resize_transition(transition, "expanded")[
            "narrow_tabs"] is not True:
        raise RuntimeError("Narrow automatic tab evidence missing")
    if parse_workspace_resize_transition(
            transition.replace("narrow_tabs=true", "narrow_tabs=maybe"),
            "expanded") is not None:
        raise RuntimeError("Invalid narrow-tab evidence accepted")
    if parse_workspace_resize_transition(
            transition.replace("bottom_h=210", "bottom_h=179"),
            "expanded") is not None:
        raise RuntimeError("Narrow layout with unusably short dock accepted")
    if parse_workspace_resize_transition(transition, "compact") is not None:
        raise RuntimeError("Expanded marker accepted as compact")
    click_marker = ("VCR U1 GUI probe: workspace_click_target_ready=true " +
                    "mode=compact x=290 y=30 root_w=962 root_h=520 " +
                    "initial_mode=live")
    expected_target = {"x": 290.0, "y": 30.0,
                       "rootWidth": 962.0, "rootHeight": 520.0,
                       "initialMode": "live"}
    if parse_workspace_click_target_evidence(click_marker) != expected_target:
        raise RuntimeError("Valid Unity workspace click target rejected")
    if parse_workspace_click_target_evidence(
            click_marker.replace("mode=compact", "mode=expanded")) is not None:
        raise RuntimeError("Wrong-mode Unity click target accepted")
    if parse_workspace_click_target_evidence(
            click_marker.replace("x=290", "x=NaN")) is not None:
        raise RuntimeError("Nonfinite Unity click point accepted")
    if parse_workspace_click_target_evidence(
            click_marker.replace("x=290", "x=1000")) is not None:
        raise RuntimeError("Offscreen Unity click point accepted")
    if parse_workspace_click_target_evidence(
            click_marker.replace("initial_mode=live",
                                 "initial_mode=unknown")) is not None:
        raise RuntimeError("Invalid initial workspace mode accepted")
    if parse_workspace_click_target_evidence(
            click_marker, len(click_marker)) is not None:
        raise RuntimeError("Stale workspace click target accepted")
    back_marker = (
        "VCR U1 GUI probe: feature_search_workspace_target_ready=true " +
        "x=149.5 y=29.5 root_w=962 root_h=520 current_mode=advanced")
    back_target = {"x": 149.5, "y": 29.5, "rootWidth": 962.0,
                   "rootHeight": 520.0, "initialMode": "advanced"}
    if parse_feature_search_workspace_target(back_marker) != back_target:
        raise RuntimeError("Valid return-to-Live OS target rejected")
    if parse_feature_search_workspace_target(
            back_marker.replace("current_mode=advanced", "current_mode=live")) is not None:
        raise RuntimeError("Non-Advanced return-to-Live target accepted")
    if parse_feature_search_workspace_target(
            back_marker.replace("x=149.5", "x=NaN")) is not None:
        raise RuntimeError("Nonfinite return-to-Live target accepted")
    if parse_feature_search_workspace_target(
            back_marker, len(back_marker)) is not None:
        raise RuntimeError("Stale return-to-Live target accepted")
    hidden_left_marker = (
        "VCR U1 GUI probe: feature_search_hide_left_target_ready=true " +
        "x=502 y=29.5 root_w=962 root_h=520 mode=advanced")
    hidden_left_target = {"x": 502.0, "y": 29.5, "rootWidth": 962.0,
                          "rootHeight": 520.0, "initialMode": "advanced"}
    if parse_feature_search_hide_left_target(hidden_left_marker) != hidden_left_target:
        raise RuntimeError("Valid Hide left OS target rejected")
    if parse_feature_search_hide_left_target(
            hidden_left_marker.replace("mode=advanced", "mode=live")) is not None:
        raise RuntimeError("Wrong-mode Hide left target accepted")
    if parse_feature_search_hide_left_target(
            hidden_left_marker.replace("x=502", "x=NaN")) is not None:
        raise RuntimeError("Nonfinite Hide left target accepted")
    if parse_feature_search_hide_left_target(
            hidden_left_marker, len(hidden_left_marker)) is not None:
        raise RuntimeError("Stale Hide left target accepted")
    native_settings_marker = ("VCR U1 GUI probe: settings_click_target_ready=true "
                              "x=210 y=29.5 root_w=962 root_h=520")
    if parse_settings_click_target_evidence(native_settings_marker) != {
            "x": 210.0, "y": 29.5, "rootWidth": 962.0,
            "rootHeight": 520.0}:
        raise RuntimeError("Valid Settings native click target rejected")
    if parse_settings_click_target_evidence(
            native_settings_marker.replace("x=210", "x=NaN")) is not None:
        raise RuntimeError("Invalid Settings click target accepted")
    if parse_settings_click_target_evidence(
            native_settings_marker.replace("x=210", "x=1000")) is not None:
        raise RuntimeError("Offscreen Settings click target accepted")
    if parse_settings_click_target_evidence(
            native_settings_marker, len(native_settings_marker)) is not None:
        raise RuntimeError("Stale Settings click target accepted")
    toolbar_marker = ("VCR U1 GUI probe: compact_header_ready=true "
                      "mode=expanded primary_actions=3 "
                      "auto_tabs_hidden=true pointer_targets=3")
    if not parse_compact_header_evidence(toolbar_marker, "expanded"):
        raise RuntimeError("Valid compact toolbar evidence rejected")
    if parse_compact_header_evidence(toolbar_marker, "compact"):
        raise RuntimeError("Wrong-mode compact toolbar marker accepted")
    if parse_compact_header_evidence(
            toolbar_marker.replace("primary_actions=3",
                                   "primary_actions=2"), "expanded"):
        raise RuntimeError("Partially hidden toolbar actions accepted")
    if parse_compact_header_evidence(
            toolbar_marker.replace("auto_tabs_hidden=true",
                                   "auto_tabs_hidden=false"), "expanded"):
        raise RuntimeError("Disabled tab toggle in toolbar accepted")
    if parse_compact_header_evidence(
            toolbar_marker.replace("pointer_targets=3",
                                   "pointer_targets=2"), "expanded"):
        raise RuntimeError("Clipped or occluded toolbar pointer targets accepted")
    if parse_compact_header_evidence(
            toolbar_marker.replace(" pointer_targets=3", ""),
            "expanded"):
        raise RuntimeError("Toolbar geometry without pointer evidence accepted")
    if parse_compact_header_evidence(
            toolbar_marker, "expanded", len(toolbar_marker)):
        raise RuntimeError("Stale compact toolbar marker accepted")
    move_marker = ("VCR U1 GUI probe: single_panel_move_ready=true "
                   "mode=expanded panel=inspector pointer_target=true")
    if not parse_single_panel_move_evidence(move_marker, "expanded"):
        raise RuntimeError("Expanded Inspector header Move evidence rejected")
    if parse_single_panel_move_evidence(move_marker, "compact"):
        raise RuntimeError("Wrong-mode header Move evidence accepted")
    if parse_single_panel_move_evidence(
            move_marker.replace("panel=inspector", "panel=preview"),
            "expanded"):
        raise RuntimeError("Wrong-panel Move evidence accepted")
    if parse_single_panel_move_evidence(
            "previous " + move_marker, "expanded"):
        raise RuntimeError("Malformed header Move evidence accepted")
    if parse_single_panel_move_evidence(
            move_marker.replace("pointer_target=true",
                                "pointer_target=false"), "expanded"):
        raise RuntimeError("Unpickable Inspector Move control accepted")
    if parse_single_panel_move_evidence(
            move_marker.replace(" pointer_target=true", ""),
            "expanded"):
        raise RuntimeError("Move geometry without pointer evidence accepted")
    if parse_single_panel_move_evidence(
            move_marker, "expanded", start_offset=len(move_marker)):
        raise RuntimeError("Stale header Move marker accepted")
    valid_axes = ("VCR U1 GUI probe: inspector_axes_ready=true "
                  "count=3 axis_w=65 horizontal_hidden=true "
                  "content_w=199 viewport_w=200 text_contrast=0.72")
    if parse_inspector_axes_evidence(valid_axes) != {
            "count": 3, "axisWidth": 65.0,
            "horizontalHidden": True,
            "contentWidth": 199.0, "viewportWidth": 200.0,
            "textContrast": 0.72}:
        raise RuntimeError("Visible Inspector axis evidence rejected")
    if parse_inspector_axes_evidence(
            valid_axes.replace("count=3", "count=2")) is not None:
        raise RuntimeError("Incomplete Inspector axis count accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("axis_w=65", "axis_w=29")) is not None:
        raise RuntimeError("Clipped Inspector numeric inputs accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("axis_w=65", "axis_w=NaN")) is not None:
        raise RuntimeError("Nonfinite Inspector axis width accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("horizontal_hidden=true",
                               "horizontal_hidden=false")) is not None:
        raise RuntimeError("Visible Inspector horizontal scrollbar accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("content_w=199", "content_w=230")) is not None:
        raise RuntimeError("Oversized Inspector content accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("viewport_w=200", "viewport_w=NaN")) is not None:
        raise RuntimeError("Nonfinite Inspector viewport width accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("text_contrast=0.72",
                               "text_contrast=0.12")) is not None:
        raise RuntimeError("Unreadable Inspector number text accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace("text_contrast=0.72",
                               "text_contrast=NaN")) is not None:
        raise RuntimeError("Nonfinite Inspector contrast accepted")
    if parse_inspector_axes_evidence(
            valid_axes.replace(" text_contrast=0.72", "")) is not None:
        raise RuntimeError("Missing Inspector contrast accepted")
    if parse_inspector_axes_evidence(
            "stale-prefix " + valid_axes,
            start_offset=len("stale-prefix " + valid_axes)) is not None:
        raise RuntimeError("Stale Inspector marker accepted")
    compact = transition.replace("expanded", "compact").replace(
        "height=730", "height=520")
    if parse_workspace_resize_transition(compact, "compact") is None:
        raise RuntimeError("Valid compact restore marker rejected")
    if parse_workspace_resize_transition(
            transition.replace("height=730", "height=NaN"),
            "expanded") is not None:
        raise RuntimeError("Invalid transition geometry accepted")
    if parse_workspace_resize_transition(
            "stale-prefix " + compact, "compact",
            start_offset=len("stale-prefix " + compact)) is not None:
        raise RuntimeError("Pre-transition marker accepted as new transition")

    if find_workspace_runtime_exceptions("VCR U1 GUI probe: workspace_tree_ready=true"):
        raise RuntimeError("Normal workspace marker misclassified as exception")
    if not find_workspace_runtime_exceptions(
            "NullReferenceException: Object reference not set"):
        raise RuntimeError("NullReferenceException silently accepted")
    if not find_workspace_runtime_exceptions(
            "VCR U1 WorkspacePanels refresh stage 5 failed (additional occurrences suppressed)"):
        raise RuntimeError("Suppressed UI refresh failure silently accepted")

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
    # A missing startup PNG is permissible only if both real window
    # transition screenshots were independently captured and diverse.
    render = {"nativeWindowControlsPassed": True,
              "nativeWindowControls": {
                  "passed": True,
                  "expandedUnityFrame": {
                      "file": "unity-expanded.png", "sha256": "a",
                      "pixelDiversityPassed": True},
                  "compactRestoredUnityFrame": {
                      "file": "unity-restored.png", "sha256": "b",
                      "pixelDiversityPassed": True}}}
    if validated_visible_unity_frames(render) != "maximized+restored":
        raise RuntimeError("Valid post-transition Unity frames rejected")
    render["nativeWindowControls"]["compactRestoredUnityFrame"][
        "pixelDiversityPassed"] = False
    if validated_visible_unity_frames(render) is not None:
        raise RuntimeError("Blank restored Unity frame accepted")
    render["nativeWindowControls"]["compactRestoredUnityFrame"][
        "pixelDiversityPassed"] = True
    render["nativeWindowControlsPassed"] = False
    if validated_visible_unity_frames(render) is not None:
        raise RuntimeError("Unvalidated window transitions accepted")
    render["nativeWindowControlsPassed"] = True
    render["nativeWindowControls"]["expandedUnityFrame"]["file"] = "stale.png"
    if validated_visible_unity_frames(render) is not None:
        raise RuntimeError("Unmatched Unity transition screenshot accepted")
    if validated_visible_unity_frames({
            "unityFrameCaptured": True,
            "unityFrame": {"pixelDiversityPassed": True}}) != "startup":
        raise RuntimeError("Valid startup screenshot rejected")
    if validated_visible_unity_frames({
            "unityFrameCaptured": True,
            "unityFrame": {"pixelDiversityPassed": False}}) is not None:
        raise RuntimeError("Blank startup screenshot accepted")
    if not window_fits_work_area(
            [42, 4, 982, 724], [0, 0, 1024, 728]):
        raise RuntimeError("Visible native Settings window rejected")
    if window_fits_work_area(
            [160, 120, 1100, 840], [0, 0, 1024, 728]):
        raise RuntimeError("Clipped native Settings window accepted")
    if window_fits_work_area(
            [42, 4, 982, 730], [0, 0, 1024, 728]):
        raise RuntimeError("Taskbar-obscured native Settings accepted")
    pages = {0: False, 1: True, 2: False, 3: False}
    if not native_settings_page_visibility(1, pages):
        raise RuntimeError("Valid native Settings page visibility rejected")
    if native_settings_page_visibility(2, pages):
        raise RuntimeError("Wrong native Settings page accepted")
    pages[2] = True
    if native_settings_page_visibility(1, pages):
        raise RuntimeError("Two visible native Settings pages accepted")
    pages[2] = False
    pages[1] = False
    if native_settings_page_visibility(1, pages):
        raise RuntimeError("Invisible native Settings contents accepted")
    hit_samples = [
        {"target": "caption", "result": HTCAPTION},
        {"target": "left", "result": HTLEFT},
        {"target": "right", "result": HTRIGHT},
    ]
    if not classify_native_input_targets(hit_samples)["passed"]:
        raise RuntimeError("Valid OS frame mouse targets rejected")
    if classify_native_input_targets(hit_samples[:2])["passed"]:
        raise RuntimeError("Missing right-edge mouse resize accepted")
    if classify_native_input_targets([
            {"target": "caption", "result": 1},
            {"target": "left", "result": 1},
            {"target": "right", "result": 1}])["passed"]:
        raise RuntimeError("Client-area-only window accepted as native frame")
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
    for stale in (player_log, report_path, config_path, unity_image_path,
                  output / "unity-expanded.png", output / "unity-restored.png",
                  output / "unity-workspace-clicked.png",
                  output / "unity-feature-searched.png",
                  output / "unity-feature-materials.png",
                  output / "unity-feature-live.png",
                  output / "unity-feature-materials-from-live.png",
                  output / "unity-feature-left-hidden.png",
                  output / "unity-feature-scenes-revealed.png",
                  output / "unity-feature-hidden-return-live.png"):
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
        "nativeInputTargetsPassed": False,
        "nativeWindowControlsPassed": False,
        "workspaceMouseClickPassed": False,
        "nativeSettingsOpenPassed": False,
        "nativeSettingsNavigationPassed": False,
        "nativeSettingsLifecyclePassed": False,
        "featureSearchKeyboardPassed": False,
        "freeResizePassed": False,
        "windowFrameCaptured": False,
        "windowFrameIsInteractiveAcceptance": False,
        "workspaceUiTreeReady": False,
        "runtimeExceptionFree": False,
        "runtimeExceptions": [],
        "compactLayout": None,
        "compactLayoutPassed": False,
        "layoutRestartPassed": False,
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
                      f"distinct={unity_frame['sampledDistinctPixels']}, " +
                      f"dominant={unity_frame['sampledDominantFraction']:.3f}, " +
                      f"diverse={unity_frame['pixelDiversityPassed']}, " +
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
            native_targets = probe_native_input_targets(observed["hwnd"], frame)
            report["nativeInputTargets"] = native_targets
            report["nativeInputTargetsPassed"] = native_targets["passed"]
            print("U1 native input targets: " +
                  f"caption={native_targets['captionDragHit']}, " +
                  f"left={native_targets['leftResizeHit']}, " +
                  f"right={native_targets['rightResizeHit']}, " +
                  f"passed={native_targets['passed']}", flush=True)
        except Exception as error:
            report["nativeInputTargetsError"] = str(error)
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
        # The presence of min/max WS_* flags does not prove Windows can
        # actually perform these state transitions on the Unity HWND.
        try:
            controls = probe_native_window_controls(
                observed["hwnd"], frame, player_log, output)
            report["nativeWindowControls"] = controls
            report["nativeWindowControlsPassed"] = controls["passed"]
            print("U1 native window maximize/minimize/restore: " +
                  f"maximized={controls['maximized']}, " +
                  f"minimized={controls['minimized']}, " +
                  f"restored={controls['restoredOriginalSize']}, " +
                  f"expandedLayout={controls['expandedLayout'] is not None}, " +
                  f"inspectorAxes={controls['inspectorAxes']}, " +
                  f"singleMove={controls['expandedSinglePanelMove'] and controls['compactSinglePanelMove']}, " +
                  f"compactRestored={controls['compactRestoredLayout'] is not None}, " +
                  f"expandedFrame={controls['expandedUnityFrame'] is not None}, " +
                  f"restoredFrame={controls['compactRestoredUnityFrame'] is not None}, " +
                  f"passed={controls['passed']}", flush=True)
        except Exception as error:
            report["nativeWindowControlsError"] = str(error)
        if report["nativeWindowControlsPassed"]:
            try:
                settings_log_offset = len(player_log.read_text(
                    encoding="utf-8", errors="replace"))
                target = report["nativeWindowControls"][
                    "compactWorkspaceClickTarget"]
                click = probe_real_workspace_mouse_click(
                    observed["hwnd"], target, player_log, output)
                report["workspaceMouseClick"] = click
                report["workspaceMouseClickPassed"] = click["passed"]
                print("U1 desktop mouse click invoked workspace toggle: " +
                      f"newMode={click['observedNewMode']} " +
                      f"screen={click['screenPoint']}, " +
                      f"rendered={click['unityFrame']['pixelDiversityPassed']}",
                      flush=True)
            except Exception as error:
                report["workspaceMouseClickError"] = str(error)
        if report["workspaceMouseClickPassed"] and not report["nativeInputTargetsPassed"]:
            # All startup WM_NCHITTEST samples can time out while Unity is
            # taking its first GPU screenshots. Retry the *real* HWND after
            # a successful OS mouse click proves the Player is processing
            # input. Do not accept style bits or the click as a substitute
            # for actual HTCAPTION/HTLEFT/HTRIGHT return values.
            try:
                report["nativeInputTargetsStartup"] = report.get(
                    "nativeInputTargets")
                stable_frame = inspect_native_window_frame(observed["hwnd"])
                stable_targets = probe_native_input_targets(
                    observed["hwnd"], stable_frame)
                report["nativeInputTargets"] = stable_targets
                report["nativeInputTargetsPassed"] = stable_targets["passed"]
                print("U1 native input targets after real workspace click: " +
                      f"caption={stable_targets['captionDragHit']} " +
                      f"left={stable_targets['leftResizeHit']} " +
                      f"right={stable_targets['rightResizeHit']} " +
                      f"attempts={stable_targets['attempts']} " +
                      f"passed={stable_targets['passed']}", flush=True)
            except Exception as error:
                report["nativeInputTargetsRetryError"] = str(error)
        if report["workspaceMouseClickPassed"]:
            try:
                native_settings = probe_native_settings_open(
                    observed["hwnd"], process.pid, player_log,
                    settings_log_offset)
                report["nativeSettingsOpen"] = native_settings
                report["nativeSettingsOpenPassed"] = native_settings["passed"]
                print("U1 native Settings opened inside monitor work area: " +
                      f"bounds={native_settings['rect']} " +
                      f"work={native_settings['monitorWorkArea']}",
                      flush=True)
                navigation = probe_native_settings_navigation(
                    native_settings["window"]["hwnd"], player_log)
                report["nativeSettingsNavigation"] = navigation
                report["nativeSettingsNavigationPassed"] = navigation["passed"]
                print("U1 native Settings OS-clicked page navigation: " +
                      f"pages={navigation['visitedPages']}, " +
                      f"passed={navigation['passed']}", flush=True)
                # Actual Win32 desktop pixels for operator review; never
                # confuse dark-theme sample diversity with click acceptance.
                try:
                    user32 = ctypes.windll.user32
                    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
                    user32.SetForegroundWindow.restype = wintypes.BOOL
                    user32.SetForegroundWindow(native_settings["window"]["hwnd"])
                    time.sleep(0.15)
                    report["nativeSettingsFrame"] = capture_foreground_player_frame(
                        native_settings["window"]["hwnd"],
                        output / "native-settings-window.bmp")
                    print("U1 captured native Settings desktop BMP: " +
                          str(report["nativeSettingsFrame"]["sampledDistinctPixels"]) +
                          " sampled colors", flush=True)
                except Exception as capture_error:
                    report["nativeSettingsFrameError"] = str(capture_error)
                lifecycle = probe_native_settings_close_reopen(
                    observed["hwnd"], process.pid, player_log,
                    settings_log_offset, native_settings)
                report["nativeSettingsLifecycle"] = lifecycle
                report["nativeSettingsLifecyclePassed"] = lifecycle["passed"]
                print("U1 native Settings WM_CLOSE/hide/reopen: " +
                      f"sameHandle={lifecycle['reusedWindow']} " +
                      f"reopenClicks={lifecycle['reopenClickAttempts']}", flush=True)
                try:
                    searched = probe_feature_search_keyboard(
                        observed["hwnd"], native_settings["window"]["hwnd"],
                        player_log, output)
                    report["featureSearchKeyboard"] = searched
                    report["featureSearchKeyboardPassed"] = searched["passed"]
                    print("U1 OS-keyboard palette search: " +
                          f"query={searched['query']} " +
                          f"result={searched['resultId']} " +
                          f"passed={searched['passed']}", flush=True)
                except Exception as search_error:
                    report["featureSearchKeyboardError"] = str(search_error)
            except Exception as error:
                report["nativeSettingsOpenError"] = str(error)
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
        report["runtimeExceptions"] = find_workspace_runtime_exceptions(final_log)
        report["runtimeExceptionFree"] = not report["runtimeExceptions"]
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
        if not report["runtimeExceptionFree"]:
            raise RuntimeError(
                "P1 GUI runtime emitted an exception: " +
                report["runtimeExceptions"][0])
        report["verifiedUnityFrameSource"] = validated_visible_unity_frames(report)
        if report["verifiedUnityFrameSource"] is None:
            raise RuntimeError(
                "P1 Unity GUI pixels missing: require diverse startup image " +
                "or BOTH source-matched maximize/restore screenshots")
        if not (report["nativeFramePassed"] and
                report["nativeInputTargetsPassed"] and
                report["nativeWindowControlsPassed"] and
                report["workspaceMouseClickPassed"] and
                report["nativeSettingsOpenPassed"] and
                report["nativeSettingsNavigationPassed"] and
                report["nativeSettingsLifecyclePassed"] and
                report["featureSearchKeyboardPassed"] and
                report["freeResizePassed"]):
            raise RuntimeError(
                "P1 native Windows caption, mouse resize edge, window "
                "state transitions or free aspect resize validation failed")
        # The first Player intentionally exited with hidden Right despite
        # a saved visible-Right manual checkpoint. Assert both persistence
        # modes across a clean second process, not just within one session.
        restart = probe_layout_restart_after_autosave(executable, args, output)
        report["layoutRestart"] = restart
        report["layoutRestartPassed"] = restart["passed"]
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
