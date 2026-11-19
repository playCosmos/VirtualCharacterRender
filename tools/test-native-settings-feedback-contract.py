#!/usr/bin/env python3
"""Verify UI Toolkit native Settings feedback and status controls.

The active WorkspaceToolkitShell owns both action feedback row 90 and
configuration status row 94. Validate Win32, AppKit, packaged Unity native
interop and visible GUI probe contracts without requiring retired uGUI files.
"""
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent.parent


def require(pattern: str, source: str, message: str) -> None:
    if not re.search(pattern, source, flags=re.DOTALL):
        raise ValueError(message)


def verify(workspace: str, config: str, bridge: str,
           appkit: str, fixture: str, windows_probe: str) -> None:
    for ident in (90, 94):
        require(
            rf"_workspaceNativeSettings\.Add\(\s*0\s*,\s*{ident}\s*,\s*"
            r"DesktopNativeSettingsWindow\.ControlKind\.Label",
            workspace, f"U1 workspace General label {ident} is missing")
        require(
            rf"\b(?:id|ident)\s*==\s*{ident}\b",
            bridge, f"Win32 multiline row {ident} no longer exists")
        require(
            rf"category\s*==\s*0\s*&&\s*\(\s*ident\s*==\s*90"
            r"\s*\|\|\s*ident\s*==\s*94\s*\)",
            appkit, "AppKit General status layout no longer covers both rows")
        require(
            rf"\badd\(\s*0\s*,\s*{ident}\s*,\s*0\s*,",
            fixture, f"Native AppKit fixture does not create actual row {ident}")

    require(
        r"statusDetails\s*=\s*category\s*==\s*0\s*&&\s*"
        r"\(\s*id\s*==\s*90\s*\|\|\s*id\s*==\s*94\s*\)\s*&&\s*"
        r"kind\s*==\s*ControlKind\.Label",
        bridge, "Windows multiline configuration and feedback are not both enabled")
    require(
        r"var\s+top\s*=\s*nextRowTop\[category\]\s*;",
        bridge, "Win32 native Settings rows no longer use a running Y cursor")
    require(
        r"nextRowTop\[category\]\s*\+=\s*statusDetails\s*\?\s*112\s*:\s*48\s*;",
        bridge, "Win32 tall General feedback rows have reverted to a 48px pitch")
    require(
        r"rows\[category\]\s*=\s*0\s*;\s*nextRowTop\[category\]\s*=\s*82\s*;",
        bridge, "Win32 native Settings rebuild retains old row spacing")
    require(
        r"feedback_bounds\.bottom\s*\+\s*8\s*>\s*bounds\.top",
        windows_probe, "Real Windows GUI probe no longer rejects status row overlaps")
    require(
        r"if\s*\(\s*categoryViews\[@0\]\s*&&\s*"
        r"\(\s*ident\s*==\s*90\s*\|\|\s*ident\s*==\s*94\s*\)\s*&&\s*"
        r"fields\[@\(ident\)\]\s*==\s*field\s*\)",
        appkit, "AppKit live updates do not recalculate both status row heights")
    require(
        r"_workspaceNativeSettings\.SetText\(\s*94\s*,\s*GetWorkspaceConfigurationStatus\(\)\)",
        config, "U1 configuration path/recovery state is not bound to 94")
    require(
        r'return\s+"Configuration:\s*"', config,
        "U1 no longer displays the configuration file path")
    require(
        r'" \| Automatic save: enabled"', config,
        "U1 no longer reports automatic-save state")
    smoke = bridge.split("internal static void VerifyMacManagedInteropSmoke()", 1)
    if len(smoke) != 2:
        raise ValueError("Real Unity macOS managed P/Invoke smoke method missing")
    smoke = smoke[1].split("#else", 1)[0]
    for ident in (90, 94):
        require(
            rf"settings\.Add\(\s*0\s*,\s*{ident}\s*,\s*ControlKind\.Label",
            smoke, f"Real Unity macOS P/Invoke smoke omits U1 status {ident}")
        require(
            rf"settings\.GetText\(\s*{ident}\s*\)",
            smoke, f"Real Unity macOS P/Invoke smoke does not read status {ident}")
    require(
        r"new\s+string\(\s*'가'\s*,\s*3600\s*\)",
        smoke, "Real Unity macOS P/Invoke smoke lacks long Korean recovery path")
    require(
        r"new\s+DesktopNativeSettingsWindow\(\)",
        smoke, "Real Unity macOS P/Invoke smoke does not recreate native Settings")
    require(
        r"status\s*=\s*user32\.GetDlgItem\(\s*settings_hwnd\s*,\s*94\s*\)",
        windows_probe, "Windows U1 GUI probe must inspect configuration status 94")
    require(
        r"\bsetText\(\s*90\s*,\s*warning\.UTF8String\s*\)",
        fixture, "AppKit fixture does not resize action feedback 90")
    require(
        r"\bsetText\(\s*94\s*,\s*warning\.UTF8String\s*\)",
        fixture, "AppKit fixture does not resize U1 configuration 94")
    require(
        r"\bsetText\(\s*94\s*,\s*unbrokenPath\.UTF8String\s*\)",
        fixture, "AppKit fixture omits an unbroken U1 configuration path")


def self_test(sources) -> None:
    workspace, config, bridge, appkit, fixture, probe = sources
    cases = (
        ("U1 94 missing", (workspace.replace(
            "_workspaceNativeSettings.Add(0, 94", "_workspaceNativeSettings.Add(0, 95"),
            config, bridge, appkit, fixture, probe)),
        ("macOS Unity interop omits U1 94", (workspace, config,
            bridge.replace("settings.Add(0, 94, ControlKind.Label",
                           "settings.Add(0, 95, ControlKind.Label"),
            appkit, fixture, probe)),
        ("Win32 row pitch regressed", (workspace, config, bridge.replace(
            "nextRowTop[category] += statusDetails ? 112 : 48;",
            "nextRowTop[category] += 48;"),
            appkit, fixture, probe)),
        ("Windows GUI overlap ignored", (workspace, config, bridge,
            appkit, fixture, probe.replace(
                "if feedback_bounds.bottom + 8 > bounds.top:",
                "if False:"))),
        ("Win32 94 disabled", (workspace, config, bridge.replace(
            "(id == 90 || id == 94)", "(id == 90 || id == 95)"),
            appkit, fixture, probe)),
        ("AppKit 94 disabled", (workspace, config, bridge,
            appkit.replace("(ident == 90 || ident == 94)",
                           "(ident == 90 || ident == 95)"),
            fixture, probe)),
        ("AppKit 94 relayout disabled", (workspace, config, bridge,
            appkit.replace("fields[@(ident)] == field",
                           "fields[@90] == field"), fixture, probe)),
        ("U1 writes status to wrong row", (workspace, config.replace(
            "SetText(94, GetWorkspaceConfigurationStatus())",
            "SetText(90, GetWorkspaceConfigurationStatus())"),
            bridge, appkit, fixture, probe)),
        ("U1 Windows probe inspects feedback 90", (workspace,
            config, bridge, appkit, fixture, probe.replace(
                "status = user32.GetDlgItem(settings_hwnd, 94)",
                "status = user32.GetDlgItem(settings_hwnd, 90)"))),
        ("AppKit fixture still synthetic", (workspace, config,
            bridge, appkit, fixture.replace(
                "setText(94, warning.UTF8String)",
                "setText(95, warning.UTF8String)"), probe)),
    )
    for label, sources in cases:
        try:
            verify(*sources)
        except ValueError:
            continue
        raise AssertionError("Invalid native Settings contract accepted: " + label)


def main() -> None:
    prefix = ROOT / "unity/Assets/VCR/Runtime/UI"
    sources = (
        (prefix / "WorkspaceToolkitShell.cs").read_text(encoding="utf-8"),
        (prefix / "WorkspaceToolkitShell.Configuration.cs").read_text(encoding="utf-8"),
        (prefix / "DesktopNativeSettingsWindow.cs").read_text(encoding="utf-8"),
        (ROOT / "native/macos/VcrSettingsWindow.mm").read_text(encoding="utf-8"),
        (ROOT / "tools/test-native-settings-macos.mm").read_text(encoding="utf-8"),
        (ROOT / "tools/probe-u1-windows-gui.py").read_text(encoding="utf-8"),
    )
    verify(*sources)
    self_test(sources)
    print("Native Settings feedback 90 + U1 configuration status 94: PASS "
          "(runtime owners, Win32/AppKit and real GUI probes)")


if __name__ == "__main__":
    main()
