#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
font_dir="$repo_root/unity/Assets/VCR/Resources/Fonts"
base_url="https://raw.githubusercontent.com/orioncactus/pretendard/main/packages/pretendard/dist/public/static"

mkdir -p "$font_dir"

install_font() {
  local file_name="$1"
  local expected_git_blob="$2"
  local font_path="$font_dir/$file_name"
  local font_url="$base_url/$file_name"

  if [[ -f "$font_path" ]]; then
    local current_blob
    current_blob="$(git hash-object "$font_path")"
    if [[ "$current_blob" == "$expected_git_blob" ]]; then
      echo "$file_name already present and verified."
      return 0
    fi
  fi

  local tmp_path="$font_path.tmp"
  rm -f "$tmp_path"

  curl \
    --fail \
    --location \
    --retry 4 \
    --retry-all-errors \
    --connect-timeout 20 \
    --output "$tmp_path" \
    "$font_url"

  local actual_git_blob
  actual_git_blob="$(git hash-object "$tmp_path")"
  if [[ "$actual_git_blob" != "$expected_git_blob" ]]; then
    echo "Pretendard font verification failed: $file_name" >&2
    echo "Expected Git blob: $expected_git_blob" >&2
    echo "Actual Git blob:   $actual_git_blob" >&2
    rm -f "$tmp_path"
    exit 1
  fi

  mv "$tmp_path" "$font_path"
  echo "Installed verified $file_name"
}

install_font "Pretendard-Regular.otf"  "08bf4cfc2164a0bff74a4bf844128d3b843bbf87"
install_font "Pretendard-Medium.otf"   "057506983f0a8b0438fc5f24382db64497bf6eae"
install_font "Pretendard-SemiBold.otf" "e7e36abc474796c8b6c383380ddd9f87655682ec"
