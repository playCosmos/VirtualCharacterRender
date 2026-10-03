#!/usr/bin/env bash
set -euo pipefail

UNITY_VERSION="6000.3.25f1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/unity"
MEDIAPIPE_PACKAGE="${PROJECT_PATH}/Packages/LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz"
UNITY_EDITOR_PATH="${1:-${UNITY_EDITOR:-}}"

if [[ -z "${UNITY_EDITOR_PATH}" ]]; then
  if [[ "$(uname -s)" == "Darwin" ]]; then
    UNITY_EDITOR_PATH="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
  else
    echo "UNITY_EDITOR is not set." >&2
    exit 2
  fi
fi

if [[ ! -x "${UNITY_EDITOR_PATH}" ]]; then
  echo "Unity Editor was not found: ${UNITY_EDITOR_PATH}" >&2
  exit 2
fi

if [[ ! -f "${MEDIAPIPE_PACKAGE}" ]]; then
  echo "Pinned MediaPipe package is missing: ${MEDIAPIPE_PACKAGE}" >&2
  exit 2
fi

"${UNITY_EDITOR_PATH}" -batchmode -nographics -projectPath "${PROJECT_PATH}" -executeMethod VCR.Editor.P9.P9BatchValidation.RunSourceFreeAndExit -logFile -
