#!/usr/bin/env bash
set -euo pipefail

VERSION="0.16.3"
EXPECTED_SHA256="cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
URL="https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v${VERSION}/com.github.homuler.mediapipe-${VERSION}.tgz"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TARGET_DIR="${REPO_ROOT}/unity/Packages/LocalPackages"
TARGET="${TARGET_DIR}/com.github.homuler.mediapipe-${VERSION}.tgz"
MODEL_DIR="${REPO_ROOT}/unity/Assets/StreamingAssets/VCR/Models"
MODEL_TARGET="${MODEL_DIR}/holistic_landmarker.bytes"

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
  curl -fL "${URL}" -o "${TARGET}"

  ACTUAL="$(sha256_file "${TARGET}")"
  if [[ "${ACTUAL}" != "${EXPECTED_SHA256}" ]]; then
    rm -f "${TARGET}"
    echo "SHA-256 mismatch. Expected ${EXPECTED_SHA256}, got ${ACTUAL}" >&2
    exit 1
  fi

  echo "Verified: ${TARGET}"
fi

MODEL_ENTRY="$(tar -tzf "${TARGET}" | grep -E '(^|/)holistic_landmarker\.bytes$' | head -n 1 || true)"
if [[ -z "${MODEL_ENTRY}" ]]; then
  echo "holistic_landmarker.bytes was not found in ${TARGET}" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

tar -xzf "${TARGET}" -C "${TMP_DIR}" "${MODEL_ENTRY}"
cp -f "${TMP_DIR}/${MODEL_ENTRY}" "${MODEL_TARGET}"

echo "Prepared model: ${MODEL_TARGET}"
