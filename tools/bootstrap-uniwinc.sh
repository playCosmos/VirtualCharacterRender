#!/usr/bin/env bash
set -euo pipefail

# UniWinC v0.9.8: pin upstream commit and patch duplicate native cleanup.
# Upstream UniWindowController invokes UniWinCore.Dispose from both
# OnApplicationQuit and OnDestroy, and UniWinCore has a finalizer. Native
# callback unregistration must only run once.
PIN="ff036884789ce3106e2b28c524b80d0289247d45"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/unity/Packages/LocalPackages/com.kirurobo.uniwinc"

if [[ ! -f "$DEST/package.json" ]]; then
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  git clone --quiet --filter=blob:none --no-checkout https://github.com/kirurobo/UniWindowController.git "$TMP/repo"
  git -C "$TMP/repo" sparse-checkout init --cone
  git -C "$TMP/repo" sparse-checkout set UniWinC/Assets/Kirurobo/UniWindowController
  git -C "$TMP/repo" checkout --quiet "$PIN"
  mkdir -p "$(dirname "$DEST")"
  cp -a "$TMP/repo/UniWinC/Assets/Kirurobo/UniWindowController" "$DEST"
fi

export UNIWINC_CORE_FILE="$DEST/Runtime/Scripts/LowLevel/UniWinCore.cs"
python3 - <<'PY'
import os
from pathlib import Path
p = Path(os.environ["UNIWINC_CORE_FILE"])
s = p.read_text(encoding="utf-8-sig")
marker = "VCR_UNIWINC_DISPOSE_ONCE"
if marker in s:
    print("UniWinC native dispose guard already applied")
    raise SystemExit(0)

old = """        public void Dispose()
        {
            // 最後にウィンドウ状態を戻すとそれが目についてしまうので、あえて戻さないことにしてみるためコメントアウト
            //DetachWindow();

            // Instead of DetachWindow()
            LibUniWinC.UnregisterDropFilesCallback();
            LibUniWinC.UnregisterMonitorChangedCallback();
            LibUniWinC.UnregisterWindowStyleChangedCallback();
        }"""
new = """        // VCR_UNIWINC_DISPOSE_ONCE: OnApplicationQuit, OnDestroy and the
        // finalizer may all call Dispose. Only the first call may touch native
        // callbacks, otherwise teardown may invoke a plugin being unloaded.
        private int _nativeDisposeOnce;

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _nativeDisposeOnce, 1) != 0)
                return;

            try
            {
                LibUniWinC.UnregisterDropFilesCallback();
                LibUniWinC.UnregisterMonitorChangedCallback();
                LibUniWinC.UnregisterWindowStyleChangedCallback();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }"""
if s.count(old) != 1:
    raise SystemExit("Unexpected pinned UniWinC UniWinCore.Dispose source")
p.write_text(s.replace(old, new), encoding="utf-8")
print("Patched UniWinC native disposal to at-most-once")
PY

# Preserve UniWinC in native Player builds, but never attach a desktop
# window during the opt-in -batchmode -nographics startup smoke. macOS
# libUniWinC can crash inside AttachMyWindow when no GUI window exists.
export UNIWINC_CONTROLLER_FILE="$DEST/Runtime/Scripts/UniWindowController.cs"
python3 - <<'PY'
import os
from pathlib import Path

p = Path(os.environ["UNIWINC_CONTROLLER_FILE"])
s = p.read_text(encoding="utf-8-sig")
marker = "VCR_UNIWINC_HEADLESS_SMOKE"
if marker in s:
    print("UniWinC headless smoke guard already applied")
    raise SystemExit(0)

helper = """        // VCR_UNIWINC_HEADLESS_SMOKE: this CI-only process has no desktop
        // window. Retain the native implementation for normal launches.
        private static bool IsVcrHeadlessSmoke()
        {
#if UNITY_EDITOR
            return false;
#else
            bool report = false;
            bool batch = false;
            bool noGraphics = false;
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg != null && arg.StartsWith("--vcr-smoke-report=", StringComparison.Ordinal))
                    report = true;
                else if (string.Equals(arg, "-batchmode", StringComparison.OrdinalIgnoreCase))
                    batch = true;
                else if (string.Equals(arg, "-nographics", StringComparison.OrdinalIgnoreCase))
                    noGraphics = true;
            }
            return report && batch && noGraphics;
#endif
        }

"""
replacements = [
    ("        // Use this for initialization\n        void Awake()\n        {",
     helper + "        // Use this for initialization\n        void Awake()\n        {\n            if (IsVcrHeadlessSmoke())\n            {\n                enabled = false;\n                return;\n            }"),
    ("        void Update()\n        {\n            // 自ウィンドウ取得ができていなければ、取得",
     "        void Update()\n        {\n            if (IsVcrHeadlessSmoke()) return;\n            // 自ウィンドウ取得ができていなければ、取得"),
    ("        private void OnApplicationFocus(bool focus)\n        {",
     "        private void OnApplicationFocus(bool focus)\n        {\n            if (IsVcrHeadlessSmoke()) return;"),
]
for old, new in replacements:
    if s.count(old) != 1:
        raise SystemExit("Unexpected pinned UniWindowController.cs source for " + old[:55])
    s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")
print("Patched UniWinC native window attach to skip only explicit headless Player smoke")
PY
