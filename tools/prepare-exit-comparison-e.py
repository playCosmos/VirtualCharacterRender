#!/usr/bin/env python3
"""Diagnostic-only build preparation: disable eager Ultraleap hint connection.

Applied only in the E native shutdown comparison workflow, after bootstrap.
Never patch source for normal app builds. Fails closed on upstream differences.
"""
from pathlib import Path

p = Path("unity/Packages/LocalPackages/com.ultraleap.tracking/Runtime/Scripts/Utils/HandTrackingHintManager.cs")
if not p.is_file():
    matches = list(Path("unity/Packages/LocalPackages").glob(
        "*/Runtime/Scripts/Utils/HandTrackingHintManager.cs"))
    if len(matches) != 1:
        raise SystemExit(f"Expected one pinned hint manager, found {len(matches)}: {matches}")
    p = matches[0]

source = p.read_text(encoding="utf-8-sig")
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
