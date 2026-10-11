#!/usr/bin/env bash
set -euo pipefail
# Run on macOS: build our sole project-owned AppKit desktop settings plugin.
# No third-party native libraries, UI frameworks, or package manager.
root="$(cd "$(dirname "$0")/.." && pwd)"
source="$root/native/macos/VcrSettingsWindow.mm"
output="$root/unity/Assets/Plugins/macOS/VcrSettingsWindow.dylib"
test "$(uname -s)" = Darwin
test -s "$source"
mkdir -p "$(dirname "$output")"
clang++ -std=c++17 -fobjc-arc -dynamiclib -arch arm64 \
  -mmacosx-version-min=12.0 -Wl,-install_name,@rpath/VcrSettingsWindow.dylib \
  -framework AppKit -framework Foundation \
  "$source" -o "$output"
test -s "$output"
cp "$root/native/macos/VcrSettingsWindow.dylib.meta" "$output.meta"
file "$output"
lipo -verify_arch arm64 "$output"
nm -gU "$output" | grep -F '_VCR_SettingsCreate'
nm -gU "$output" | grep -F '_VCR_SettingsPollAction'
echo "AppKit Settings native dylib built and symbols verified"
