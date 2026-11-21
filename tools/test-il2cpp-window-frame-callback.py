#!/usr/bin/env python3
"""Prevent the Win32 EnumWindows closure that crashes a Windows IL2CPP Player.

This is a source-level guard; only the separate Windows IL2CPP Player smoke
can verify that the generated native binary actually works.
"""
from pathlib import Path
import re


SOURCE = (Path(__file__).resolve().parents[1] /
          "unity/Assets/VCR/Runtime/Output/Unity/UniWinCOverlayOutput.cs")


def check(source: str) -> None:
    required = (
        "[AOT.MonoPInvokeCallback(typeof(EnumWindowsCallback))]",
        "private static bool RestoreDesktopFrameForWindow(",
        "private static readonly EnumWindowsCallback RestoreDesktopFrameCallback =",
        "EnumWindows(RestoreDesktopFrameCallback, GCHandle.ToIntPtr(context))",
        "GCHandle.FromIntPtr(context).Target",
        "context.Free();",
    )
    for marker in required:
        if marker not in source:
            raise AssertionError("IL2CPP window callback contract missing: " + marker)
    # An instance-capturing lambda used to work in Mono Player, while
    # generating a NotSupportedException in the first IL2CPP config relaunch.
    if re.search(r"EnumWindows\s*\(\s*\(", source):
        raise AssertionError("EnumWindows must not receive a capturing lambda")


def main() -> None:
    source = SOURCE.read_text(encoding="utf-8")
    check(source)
    for broken in (
        source.replace("[AOT.MonoPInvokeCallback(typeof(EnumWindowsCallback))]", "", 1),
        source.replace("private static bool RestoreDesktopFrameForWindow(",
                       "private bool RestoreDesktopFrameForWindow(", 1),
        source.replace("EnumWindows(RestoreDesktopFrameCallback, GCHandle.ToIntPtr(context))",
                       "EnumWindows((hwnd, param) => true, IntPtr.Zero)", 1),
    ):
        try:
            check(broken)
        except AssertionError:
            pass
        else:
            raise AssertionError("A broken IL2CPP callback escaped the regression guard")
    print("Win32 IL2CPP static reverse P/Invoke callback: PASS")


if __name__ == "__main__":
    main()
