#!/usr/bin/env python3
"""Offline self-test for pinned Ultraleap's opt-in hint connection patch.

Exercises the *actual* bootstrap inline patch against a representative
upstream fixture; no network, Python dependencies, or real hardware needed.
"""
import os
from pathlib import Path
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parent.parent
BOOTSTRAP = ROOT / "tools" / "bootstrap-ultraleap.sh"
ISOLATION = ROOT / "tools" / "prepare-exit-comparison-e.py"

ORIGINAL = """static void StartupHints()
{
    currentHints = settings.startupHints.ToList();
    if (HandTrackingSourceUtility.LeapOpenXRHintingAvailable)
    {
    }
    else
    {
                // Use a LeapC connection for Hints
                SetupDeviceCallbacks();
    }
}
public static void RequestHandTrackingHints(string[] hints)
{
    if (HandTrackingSourceUtility.LeapOpenXRHintingAvailable)
    {
    }
    else
    {
                // Use a LeapC connection for Hints
                RequestHintsOnAllDevices(hints);
    }
}
"""


def main():
    bootstrap = BOOTSTRAP.read_text(encoding="utf-8")
    marker = "# VCR_ULTRALEAP_LAZY_HINT_CONNECTION:"
    assert bootstrap.count(marker) == 1, "Production guard bootstrap missing"
    patch = bootstrap.split(marker, 1)[1].split("python3 - <<'PY'\n", 1)[1].split("\nPY\n", 1)[0]

    with tempfile.TemporaryDirectory(prefix="vcr-ultraleap-hints-") as tmp:
        root = Path(tmp)
        target = root / (
            "unity/Packages/LocalPackages/com.ultraleap.tracking/"
            "Core/Runtime/Scripts/Utils/HandTrackingHintManager.cs"
        )
        target.parent.mkdir(parents=True)
        target.write_text(ORIGINAL, encoding="utf-8")
        env = dict(os.environ, ULTRALEAP_HINT_FILE=str(target))

        def run_patch():
            return subprocess.run([sys.executable, "-c", patch], env=env,
                                  text=True, capture_output=True)

        first = run_patch()
        assert first.returncode == 0, first.stderr + first.stdout
        patched = target.read_text(encoding="utf-8")
        # Startup no longer creates an unused native controller.
        assert "if (currentHints.Count != 0)\n                    SetupDeviceCallbacks();" in patched
        # Explicit nonempty hints still register native callbacks later.
        assert "if (hints.Length != 0)\n                    SetupDeviceCallbacks();" in patched
        # Clearing a never-created hint set must not instantiate Controller.
        assert "if (hints.Length != 0 || _leapController != null)\n                    RequestHintsOnAllDevices(hints);" in patched
        assert patched.count("VCR_ULTRALEAP_LAZY_HINT_CONNECTION") == 1

        second = run_patch()
        assert second.returncode == 0, second.stderr + second.stdout
        assert target.read_text(encoding="utf-8") == patched, "Not idempotent"

        isolation = subprocess.run(
            [sys.executable, str(ISOLATION)], cwd=root,
            text=True, capture_output=True
        )
        assert isolation.returncode == 0, isolation.stderr + isolation.stdout
        assert target.read_text(encoding="utf-8") == patched, "E changed the production guard"

        target.write_text("upstream changed without expected anchors", encoding="utf-8")
        unexpected = run_patch()
        assert unexpected.returncode != 0, "Upstream drift must fail closed"

    print("Ultraleap lazy hint connection bootstrap + historical E control PASS")


if __name__ == "__main__":
    main()
