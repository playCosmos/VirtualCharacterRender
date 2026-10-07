#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
font_dir="$repo_root/unity/Assets/VCR/Resources/Fonts"
font_path="$font_dir/Pretendard-Regular.otf"
font_url="https://raw.githubusercontent.com/orioncactus/pretendard/main/packages/pretendard/dist/public/static/Pretendard-Regular.otf"
expected_git_blob="08bf4cfc2164a0bff74a4bf844128d3b843bbf87"

mkdir -p "$font_dir"

if [[ -f "$font_path" ]]; then
  current_blob="$(git hash-object "$font_path")"
  if [[ "$current_blob" == "$expected_git_blob" ]]; then
    echo "Pretendard-Regular.otf already present and verified."
    exit 0
  fi
fi

tmp_path="$font_path.tmp"
rm -f "$tmp_path"

curl   --fail   --location   --retry 4   --retry-all-errors   --connect-timeout 20   --output "$tmp_path"   "$font_url"

actual_git_blob="$(git hash-object "$tmp_path")"
if [[ "$actual_git_blob" != "$expected_git_blob" ]]; then
  echo "Pretendard font verification failed." >&2
  echo "Expected Git blob: $expected_git_blob" >&2
  echo "Actual Git blob:   $actual_git_blob" >&2
  rm -f "$tmp_path"
  exit 1
fi

mv "$tmp_path" "$font_path"
echo "Installed verified Pretendard-Regular.otf to $font_path"
