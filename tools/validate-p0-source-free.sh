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
    echo "UNITY_EDITOR is not set and no default editor path is defined for this OS." >&2
    echo "Usage: $0 /path/to/Unity" >&2
    exit 2
  fi
fi

if [[ ! -x "${UNITY_EDITOR_PATH}" ]]; then
  echo "Unity Editor was not found or is not executable:" >&2
  echo "  ${UNITY_EDITOR_PATH}" >&2
  exit 2
fi

if [[ ! -f "${MEDIAPIPE_PACKAGE}" ]]; then
  echo "Pinned MediaPipe package is missing:" >&2
  echo "  ${MEDIAPIPE_PACKAGE}" >&2
  echo "Run ./tools/bootstrap-mediapipe.sh first." >&2
  exit 2
fi

echo "Running VCR P0 source-free validation with:"
echo "  Unity: ${UNITY_EDITOR_PATH}"
echo "  Project: ${PROJECT_PATH}"

set +e
"${UNITY_EDITOR_PATH}" \
  -batchmode \
  -nographics \
  -projectPath "${PROJECT_PATH}" \
  -executeMethod VCR.Editor.P0.P0BatchValidation.RunSourceFreeAndExit \
  -logFile -
STATUS=$?
set -e

if [[ "${STATUS}" -eq 0 ]]; then
  echo "VCR P0 source-free batch validation: PASS"
else
  echo "VCR P0 source-free batch validation: FAIL (exit=${STATUS})" >&2
fi

exit "${STATUS}"
