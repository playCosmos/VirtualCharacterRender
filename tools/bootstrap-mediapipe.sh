#!/usr/bin/env bash
set -euo pipefail

VERSION="0.16.3"
EXPECTED_SHA256="cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v${VERSION}/com.github.homuler.mediapipe-${VERSION}.tgz"
MODEL_NAMES=(
  "face_landmarker_v2_with_blendshapes.bytes"
  "holistic_landmarker.bytes"
)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TARGET_DIR="${REPO_ROOT}/unity/Packages/LocalPackages"
TARGET="${TARGET_DIR}/com.github.homuler.mediapipe-${VERSION}.tgz"
MODEL_DIR="${REPO_ROOT}/unity/Assets/StreamingAssets/VCR/Models"

mkdir -p "${TARGET_DIR}" "${MODEL_DIR}"

sha256_file() {
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    sha256sum "$1" | awk '{print $1}'
  fi
}

needs_download=1
if [[ -f "${TARGET}" ]]; then
  CURRENT="$(sha256_file "${TARGET}")"
  if [[ "${CURRENT}" == "${EXPECTED_SHA256}" ]]; then
    needs_download=0
    echo "MediaPipeUnityPlugin ${VERSION} already present and verified."
  else
    rm -f "${TARGET}"
  fi
fi

if [[ "${needs_download}" -eq 1 ]]; then
  echo "Downloading MediaPipeUnityPlugin ${VERSION}..."
  # GitHub release assets occasionally return a transient 502/503/504.
  # Retry transport failures with curl's bounded exponential backoff, while
  # retaining the exact pinned SHA-256 as the non-negotiable integrity check.
  # Stage the response separately so failed transfers never become a package.
  DOWNLOAD_TMP="$(mktemp "${TARGET}.download.XXXXXX")"
  if ! curl \
    --fail \
    --location \
    --retry 5 \
    --retry-all-errors \
    --retry-max-time 600 \
    --connect-timeout 20 \
    --max-time 180 \
    --no-progress-meter \
    --output "${DOWNLOAD_TMP}" \
    "${URL}"; then
    rm -f "${DOWNLOAD_TMP}"
    echo "MediaPipeUnityPlugin ${VERSION} download failed after retries." >&2
    exit 1
  fi

  ACTUAL="$(sha256_file "${DOWNLOAD_TMP}")"
  if [[ "${ACTUAL}" != "${EXPECTED_SHA256}" ]]; then
    rm -f "${DOWNLOAD_TMP}"
    echo "SHA-256 mismatch. Expected ${EXPECTED_SHA256}, got ${ACTUAL}" >&2
    exit 1
  fi
  mv -f "${DOWNLOAD_TMP}" "${TARGET}"

  echo "Verified: ${TARGET}"
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

for MODEL_NAME in "${MODEL_NAMES[@]}"; do
  MODEL_ENTRY="$(tar -tzf "${TARGET}" | grep -E "(^|/)${MODEL_NAME}$" | head -n 1 || true)"
  if [[ -z "${MODEL_ENTRY}" ]]; then
    echo "${MODEL_NAME} was not found in ${TARGET}" >&2
    exit 1
  fi

  tar -xzf "${TARGET}" -C "${TMP_DIR}" "${MODEL_ENTRY}"
  cp -f "${TMP_DIR}/${MODEL_ENTRY}" "${MODEL_DIR}/${MODEL_NAME}"
  echo "Prepared model: ${MODEL_DIR}/${MODEL_NAME}"
done
