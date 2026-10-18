#!/usr/bin/env bash
set -euo pipefail

# Pinned Ultraleap v7.3.0 source, patched only for Unity 6 legacy XR API removal.
PIN="833d82e7333a5f37ebc0844d02431acf74f35d24"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/unity/Packages/LocalPackages/com.ultraleap.tracking"
if [[ ! -f "$DEST/package.json" ]]; then
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  # Ultraleap's upstream Git LFS budget is exhausted. Checkout the EXACT
  # pinned source without smudging, then restore only byte-identical LFS
  # objects whose SHA-256 and length match upstream pointers.
  GIT_LFS_SKIP_SMUDGE=1 git clone --quiet --filter=blob:none --no-checkout https://github.com/ultraleap/UnityPlugin.git "$TMP/repo"
  git -C "$TMP/repo" sparse-checkout init --cone
  git -C "$TMP/repo" sparse-checkout set Packages/Tracking
  GIT_LFS_SKIP_SMUDGE=1 git -C "$TMP/repo" checkout --quiet "$PIN"
  if [[ "$(git -C "$TMP/repo" rev-parse HEAD)" != "$PIN" ]]; then
    echo "Ultraleap immutable source commit mismatch" >&2
    exit 1
  fi
  python3 "$ROOT/tools/restore-ultraleap-lfs.py" "$TMP/repo/Packages/Tracking"
  mkdir -p "$(dirname "$DEST")"
  cp -a "$TMP/repo/Packages/Tracking" "$DEST"
fi

export ULTRALEAP_PATCH_FILE="$DEST/Core/Runtime/Scripts/Utils/XRSupportUtil.cs"
python3 - <<'PY'
import os
from pathlib import Path
p=Path(os.environ["ULTRALEAP_PATCH_FILE"])
s=p.read_text()
marker="VCR_UNITY6_XR_COMPAT"
if marker in s:
    print("Ultraleap Unity 6 XR compatibility already applied")
    raise SystemExit(0)
replacements={
'''#else
            return XRSettings.enabled;
#endif''':'''#else
#if UNITY_6000_0_OR_NEWER // VCR_UNITY6_XR_COMPAT
            return InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid;
#else
            return XRSettings.enabled;
#endif
#endif''',
'''#else
            return XRSettings.isDeviceActive;
#endif''':'''#else
#if UNITY_6000_0_OR_NEWER
            return InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid;
#else
            return XRSettings.isDeviceActive;
#endif
#endif''',
'''            XRStats.TryGetGPUTimeLastFrame(out gpuTime);''':'''#if !UNITY_6000_0_OR_NEWER
            XRStats.TryGetGPUTimeLastFrame(out gpuTime);
#endif''',
'''            return XRSettings.loadedDeviceName;''':'''#if UNITY_6000_0_OR_NEWER
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return device.isValid ? device.name : string.Empty;
#else
            return XRSettings.loadedDeviceName;
#endif'''
}
for original,changed in replacements.items():
    count=s.count(original)
    if count != 1:
        raise SystemExit(f"Unexpected pinned Ultraleap source ({count}): {original[:60]}")
    s=s.replace(original,changed)
p.write_text(s)
print("Patched Ultraleap legacy XRSettings/XRStats usage for Unity 6")
PY

# Ultraleap's optional settings asset may be absent in standalone releases.
# Do not crash application startup while initializing optional hand hints.
export ULTRALEAP_HINT_FILE="$DEST/Core/Runtime/Scripts/Utils/HandTrackingHintManager.cs"
python3 - <<'PY'
import os
from pathlib import Path
p=Path(os.environ["ULTRALEAP_HINT_FILE"])
s=p.read_text()
old="            currentHints = UltraleapSettings.Instance.startupHints.ToList();"
new="""            var settings = UltraleapSettings.Instance;
            currentHints = settings != null && settings.startupHints != null
                ? settings.startupHints.ToList()
                : new List<string>();"""
if old not in s and new not in s:
    raise SystemExit("Unexpected pinned Ultraleap hint manager source")
if old in s:
    s=s.replace(old,new)
    p.write_text(s)
print("Ultraleap standalone startup hints: null-safe")
PY

# VCR_ULTRALEAP_LAZY_HINT_CONNECTION:
# Ultraleap 7.3 automatically invokes StartupHints after scene load even
# when there is no LeapServiceProvider and no startup hand-tracking hints.
# SetupDeviceCallbacks() dereferences LeapController, creating a hidden
# Controller(0) + LeapC Worker that can delay native shutdown ~17 seconds.
# Defer creation until startup hints actually exist OR hints are requested
# later by the user. Do not eagerly start an empty connection.
export ULTRALEAP_HINT_FILE="$DEST/Core/Runtime/Scripts/Utils/HandTrackingHintManager.cs"
python3 - <<'PY'
import os
from pathlib import Path
p = Path(os.environ["ULTRALEAP_HINT_FILE"])
s = p.read_text(encoding="utf-8-sig")
marker = "VCR_ULTRALEAP_LAZY_HINT_CONNECTION"
old_start = """                // Use a LeapC connection for Hints
                SetupDeviceCallbacks();"""
new_start = """                // VCR_ULTRALEAP_LAZY_HINT_CONNECTION:
                // Avoid starting an unused Controller and LeapC worker on
                // every Player launch. Hints configured for startup are
                // still registered, but never subscribed twice.
                if (currentHints.Count != 0 && _leapController == null)
                    SetupDeviceCallbacks();"""
old_update = """                // Use a LeapC connection for Hints
                RequestHintsOnAllDevices(hints);"""
new_update = """                // Explicit hints added after startup must still attach the
                // device callback only once. Clearing a never-started
                // empty hint set must not create a connection either.
                if (hints.Length != 0 && _leapController == null)
                    SetupDeviceCallbacks();
                if (hints.Length != 0 || _leapController != null)
                    RequestHintsOnAllDevices(hints);"""
if marker in s:
    if s.count(new_start) != 1 or s.count(new_update) != 1:
        raise SystemExit("Partially patched Ultraleap lazy hint source")
    print("Ultraleap lazy hand-hint connection already applied")
else:
    if s.count(old_start) != 1 or s.count(old_update) != 1:
        raise SystemExit(
            "Unexpected pinned Ultraleap hint source: refuse unsafe patch")
    s = s.replace(old_start, new_start, 1).replace(old_update, new_update, 1)
    p.write_text(s, encoding="utf-8")
    print("Ultraleap lazy hand-hint connection: startup and dynamic hints guarded")
PY

# In a standalone Player the upstream package cannot create the missing
# Ultraleap Settings asset (CreateSettingsSO is UNITY_EDITOR-only). Provide
# an in-memory default so every consumer of UltraleapSettings.Instance,
# not only StartupHints, remains safe without hardware or asset setup.
export ULTRALEAP_SETTINGS_FILE="$DEST/Core/Runtime/Scripts/UltraleapSettings.cs"
python3 - <<'PY'
import os
from pathlib import Path
p = Path(os.environ["ULTRALEAP_SETTINGS_FILE"])
s = p.read_text()
old = '''#if UNITY_EDITOR
            newSO = ScriptableObject.CreateInstance<UltraleapSettings>();

            Directory.CreateDirectory(Application.dataPath + "/Resources/");
            AssetDatabase.CreateAsset(newSO, "Assets/Resources/Ultraleap Settings.asset");
#endif
            return newSO;'''
new = '''#if UNITY_EDITOR
            newSO = ScriptableObject.CreateInstance<UltraleapSettings>();

            Directory.CreateDirectory(Application.dataPath + "/Resources/");
            AssetDatabase.CreateAsset(newSO, "Assets/Resources/Ultraleap Settings.asset");
#else
            // VCR: A Player cannot write editor assets. Keep startup hints
            // and other settings consumers safe with non-persistent defaults.
            newSO = ScriptableObject.CreateInstance<UltraleapSettings>();
            newSO.hideFlags = HideFlags.DontSave;
#endif
            return newSO;'''
if old not in s and new not in s:
    raise SystemExit("Unexpected upstream UltraleapSettings.CreateSettingsSO")
if old in s:
    s = s.replace(old, new)
    p.write_text(s)
print("Ultraleap standalone settings default: null-safe")
PY
