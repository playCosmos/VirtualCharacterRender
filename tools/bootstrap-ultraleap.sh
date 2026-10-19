#!/usr/bin/env bash
set -euo pipefail

# Pinned Ultraleap v7.3.0 source, patched only for Unity 6 legacy XR API removal.
#
# Upstream stores its native libraries, editor textures and reference models
# in Git LFS. The git-lfs smudge transfer can fail for every consumer when
# the upstream LFS bandwidth budget is exhausted, and it aborts the whole
# checkout even though the pinned content itself is unchanged. The checkout
# here therefore skips the smudge filter, and every LFS pointer is
# materialized afterwards from the pinned media CDN URL and verified against
# the pointer's own sha256/size before it replaces anything.
PIN="833d82e7333a5f37ebc0844d02431acf74f35d24"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/unity/Packages/LocalPackages/com.ultraleap.tracking"
if [[ ! -f "$DEST/package.json" ]]; then
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  git clone --quiet --filter=blob:none --no-checkout https://github.com/ultraleap/UnityPlugin.git "$TMP/repo"
  git -C "$TMP/repo" sparse-checkout init --cone
  git -C "$TMP/repo" sparse-checkout set Packages/Tracking
  GIT_LFS_SKIP_SMUDGE=1 git -C "$TMP/repo" checkout --quiet "$PIN"
  mkdir -p "$(dirname "$DEST")"
  cp -a "$TMP/repo/Packages/Tracking" "$DEST"
fi

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

urlencode_path() {
  python3 - "$1" <<'PY'
import sys
import urllib.parse

print(urllib.parse.quote(sys.argv[1], safe="/"))
PY
}

# Materialize any remaining Git LFS pointer files inside the package copy.
# This is idempotent and self-heals copies made while the upstream LFS
# budget was exhausted; a fully smudged installation finds nothing to do.
while IFS= read -r candidate_file; do
  # The grep above can match any file that merely contains the spec string;
  # only a true LFS pointer starts with the version marker line.
  if [[ "$(head -n 1 "$candidate_file" | tr -d '\r')" != "version https://git-lfs.github.com/spec/v1" ]]; then
    continue
  fi

  pointer_text="$(tr -d '\r' < "$candidate_file")"
  expected_oid="$(printf '%s\n' "$pointer_text" | awk '$1 == "oid" { print $2 }')"
  expected_oid="${expected_oid#sha256:}"
  expected_size="$(printf '%s\n' "$pointer_text" | awk '$1 == "size" { print $2 }')"
  if [[ ${#expected_oid} -ne 64 || -z "$expected_size" ]]; then
    echo "Malformed Git LFS pointer: $candidate_file" >&2
    exit 1
  fi

  relative_path="${candidate_file#"$DEST"/}"
  encoded_path="$(urlencode_path "Packages/Tracking/$relative_path")"
  pointer_tmp="$candidate_file.lfs-tmp"
  rm -f "$pointer_tmp"

  curl \
    --fail \
    --location \
    --retry 4 \
    --retry-all-errors \
    --connect-timeout 20 \
    --output "$pointer_tmp" \
    "https://media.githubusercontent.com/media/ultraleap/UnityPlugin/$PIN/$encoded_path"

  actual_oid="$(sha256_of "$pointer_tmp")"
  actual_size="$(stat -c%s "$pointer_tmp" 2>/dev/null || stat -f%z "$pointer_tmp")"
  if [[ "$actual_oid" != "$expected_oid" || "$actual_size" != "$expected_size" ]]; then
    rm -f "$pointer_tmp"
    echo "Ultraleap LFS content verification failed: $relative_path" >&2
    echo "Expected $expected_size bytes / $expected_oid" >&2
    echo "Actual   $actual_size bytes / $actual_oid" >&2
    exit 1
  fi

  mv "$pointer_tmp" "$candidate_file"
  echo "Materialized LFS content: $relative_path"
done < <(grep -r -l -m1 'git-lfs.github.com/spec' "$DEST" 2>/dev/null || true)

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
