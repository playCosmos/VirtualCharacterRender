#!/usr/bin/env python3
"""Reject a mismatch between the TWO real General Settings text controls.

The legacy ApplicationUiController creates action feedback 90. The U1
WorkspaceToolkitShell creates BOTH action feedback 90 and configuration status
94. The Windows GUI probe must inspect 94, while both labels should wrap.
An earlier source guard incorrectly required 94 to be absent, hiding this bug.
"""
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent.parent


def require(pattern: str, source: str, message: str) -> None:
    if not re.search(pattern, source, flags=re.DOTALL):
        raise ValueError(message)


def verify(legacy: str, workspace: str, config: str, bridge: str,
           appkit: str, fixture: str, windows_probe: str) -> None:
    require(
        r"private\s+void\s+AddNativeFeedback\s*\(\s*int\s+category\s*\)"
        r"\s*\{.*?_nativeSettings\.Add\(\s*category\s*,\s*90\s*\+\s*category",
        legacy, "Legacy General action feedback 90 is missing")
    require(r"_nativeSettings\.SetText\(\s*90\s*,",
            legacy, "Legacy recovery action no longer writes feedback 90")

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
    legacy, workspace, config, bridge, appkit, fixture, probe = sources
    cases = (
        ("U1 94 missing", (legacy, workspace.replace(
            "_workspaceNativeSettings.Add(0, 94", "_workspaceNativeSettings.Add(0, 95"),
            config, bridge, appkit, fixture, probe)),
        ("Win32 94 disabled", (legacy, workspace, config, bridge.replace(
            "(id == 90 || id == 94)", "(id == 90 || id == 95)"),
            appkit, fixture, probe)),
        ("AppKit 94 disabled", (legacy, workspace, config, bridge,
            appkit.replace("(ident == 90 || ident == 94)",
                           "(ident == 90 || ident == 95)"),
            fixture, probe)),
        ("AppKit 94 relayout disabled", (legacy, workspace, config, bridge,
            appkit.replace("fields[@(ident)] == field",
                           "fields[@90] == field"), fixture, probe)),
        ("U1 writes status to wrong row", (legacy, workspace, config.replace(
            "SetText(94, GetWorkspaceConfigurationStatus())",
            "SetText(90, GetWorkspaceConfigurationStatus())"),
            bridge, appkit, fixture, probe)),
        ("U1 Windows probe inspects feedback 90", (legacy, workspace,
            config, bridge, appkit, fixture, probe.replace(
                "status = user32.GetDlgItem(settings_hwnd, 94)",
                "status = user32.GetDlgItem(settings_hwnd, 90)"))),
        ("AppKit fixture still synthetic", (legacy, workspace, config,
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
        (prefix / "ApplicationUiController.NativeSettings.cs").read_text(encoding="utf-8"),
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
