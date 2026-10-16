#!/usr/bin/env python3
"""Diagnostic-only build preparation: disable eager Ultraleap hint connection.

Applied only in the E native shutdown comparison workflow, after bootstrap.
Never patch source for normal app builds. Fails closed on upstream differences.
"""
from pathlib import Path

p = Path("unity/Packages/LocalPackages/com.ultraleap.tracking/Core/Runtime/Scripts/Utils/HandTrackingHintManager.cs")
if not p.is_file():
    matches = list(Path("unity/Packages/LocalPackages").glob(
        "*/Core/Runtime/Scripts/Utils/HandTrackingHintManager.cs"))
    if len(matches) != 1:
        raise SystemExit(f"Expected one pinned hint manager, found {len(matches)}: {matches}")
    p = matches[0]

source = p.read_text(encoding="utf-8-sig")
if "VCR_ULTRALEAP_LAZY_HINT_CONNECTION" in source:
    # The full application bootstrap now includes the isolated E fix.
    # E and A intentionally become equivalent at this source revision.
    required = (
        "if (currentHints.Count != 0)\\n                    SetupDeviceCallbacks();",
        "if (hints.Length != 0)\\n                    SetupDeviceCallbacks();",
        "if (hints.Length != 0 || _leapController != null)",
    )
    if not all(fragment in source for fragment in required):
        raise SystemExit("Incomplete production lazy-connection guard")
    print("E is equivalent to A: lazy hand-hint guard already in bootstrap")
    raise SystemExit(0)

old = """                // Use a LeapC connection for Hints
                SetupDeviceCallbacks();"""
new = """                // VCR_EXIT_COMPARISON_E: diagnostic only. Empty startup
                // hints must not create an unused LeapC Controller/worker.
                // Retain explicit non-empty hint setup for control testing.
                if (currentHints.Count != 0)
                    SetupDeviceCallbacks();"""
if source.count(old) != 1:
    raise SystemExit("Unexpected pinned Ultraleap StartupHints implementation")
p.write_text(source.replace(old, new), encoding="utf-8")
print(f"E diagnostic patch installed (only in this CI workspace): {p}")
