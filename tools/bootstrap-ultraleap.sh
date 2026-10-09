#!/usr/bin/env bash
set -euo pipefail

# Pinned Ultraleap v7.3.0 source, patched only for Unity 6 legacy XR API removal.
PIN="833d82e7333a5f37ebc0844d02431acf74f35d24"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/unity/Packages/LocalPackages/com.ultraleap.tracking"
if [[ ! -f "$DEST/package.json" ]]; then
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  git clone --quiet --filter=blob:none --no-checkout https://github.com/ultraleap/UnityPlugin.git "$TMP/repo"
  git -C "$TMP/repo" sparse-checkout init --cone
  git -C "$TMP/repo" sparse-checkout set Packages/Tracking
  git -C "$TMP/repo" checkout --quiet "$PIN"
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
