#!/usr/bin/env python3
"""Verify that native recovery feedback uses the ID actually emitted by Unity.

This source-level guard complements the real AppKit test: its former synthetic
ID 94 test passed even though ApplicationUiController writes to ID 90.
"""
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parent.parent


def verify(managed: str, bridge: str, appkit: str, fixture: str) -> None:
    helper = re.search(
        r"private\s+void\s+AddNativeFeedback\s*\(\s*int\s+category\s*\)"
        r"\s*\{(?P<body>.*?)\n\s*\}", managed, re.DOTALL
    )
    if not helper:
        raise ValueError("ApplicationUiController.AddNativeFeedback missing")
    emitted = re.search(
        r"_nativeSettings\.Add\(\s*category\s*,\s*(\d+)\s*\+\s*category",
        helper.group("body"),
    )
    if not emitted:
        raise ValueError("Cannot determine native category feedback ID")
    general_id = int(emitted.group(1))
    if general_id != 90:
        raise ValueError("General recovery messages target ID 90; feedback ID changed")
    if not re.search(r"_nativeSettings\.SetText\(\s*90\s*,", managed):
        raise ValueError("General recovery status no longer writes native ID 90")

    if not re.search(
        rf"statusDetails\s*=\s*category\s*==\s*0\s*&&\s*id\s*==\s*{general_id}"
        rf"\s*&&\s*kind\s*==\s*ControlKind\.Label", bridge
    ):
        raise ValueError("Win32 multiline recovery text targets wrong control")

    if not re.search(
        rf"if\s*\(\s*category\s*==\s*0\s*&&\s*ident\s*==\s*{general_id}\s*\)",
        appkit,
    ):
        raise ValueError("AppKit multiline configuration text targets wrong control")
    if not re.search(
        rf"if\s*\(\s*ident\s*==\s*{general_id}\s*&&\s*categoryViews\[@0\]"
        rf"\s*&&\s*fields\[@{general_id}\]\s*==\s*field\s*\)",
        appkit,
    ):
        raise ValueError("AppKit live feedback updates cannot resize the real row")

    # An AppKit mock using invented ID 94 is not an integration test of the
    # actual General status. Assert both creation and live SetText use ID 90.
    if not re.search(rf"\badd\(\s*0\s*,\s*{general_id}\s*,\s*0\s*,", fixture):
        raise ValueError("Native AppKit test does not create real feedback control")
    if not re.search(
        rf"\bsetText\(\s*{general_id}\s*,\s*warning\.UTF8String\s*\)",
        fixture,
    ):
        raise ValueError("Native AppKit test does not update real feedback control")


def self_test(managed: str, bridge: str, appkit: str, fixture: str) -> None:
    changes = (
        ("Windows reverted to old 94", (managed, bridge.replace(
            "category == 0 && id == 90", "category == 0 && id == 94"
        ), appkit, fixture)),
        ("macOS layout reverted to old 94", (managed, bridge, appkit.replace(
            "category == 0 && ident == 90", "category == 0 && ident == 94"
        ), fixture)),
        ("macOS live updates reverted to old 94", (managed, bridge, appkit.replace(
            "ident == 90 && categoryViews[@0]", "ident == 94 && categoryViews[@0]"
        ), fixture)),
        ("fake AppKit fixture restored", (managed, bridge, appkit, fixture.replace(
            "setText(90, warning.UTF8String)", "setText(94, warning.UTF8String)"
        ))),
    )
    for name, sources in changes:
        try:
            verify(*sources)
        except ValueError:
            continue
        raise AssertionError("Contract verifier accepted: " + name)


def main() -> None:
    managed = (ROOT / "unity/Assets/VCR/Runtime/UI/"
               "ApplicationUiController.NativeSettings.cs").read_text(encoding="utf-8")
    bridge = (ROOT / "unity/Assets/VCR/Runtime/UI/"
              "DesktopNativeSettingsWindow.cs").read_text(encoding="utf-8")
    appkit = (ROOT / "native/macos/VcrSettingsWindow.mm").read_text(encoding="utf-8")
    fixture = (ROOT / "tools/test-native-settings-macos.mm").read_text(encoding="utf-8")
    verify(managed, bridge, appkit, fixture)
    self_test(managed, bridge, appkit, fixture)
    print("Native Settings actual General feedback ID 90 contract: PASS "
          "(Win32, AppKit, runtime and test fixture)")


if __name__ == "__main__":
    main()
