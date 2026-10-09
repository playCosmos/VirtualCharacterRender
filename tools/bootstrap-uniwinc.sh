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
