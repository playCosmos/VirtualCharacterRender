#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./tools/run-p0-player.sh --vrm /absolute/or/relative/model.vrm [options]

Options:
  --mode evidence|performance
  --shader-bundle PATH
  --shader-id ID
  --material-slot ID
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

VRM=""
MODE="evidence"
SHADER_BUNDLE=""
SHADER_ID=""
MATERIAL_SLOT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --vrm)
      VRM="$2"
      shift 2
      ;;
    --mode)
      MODE="$2"
      shift 2
      ;;
    --shader-bundle)
      SHADER_BUNDLE="$2"
      shift 2
      ;;
    --shader-id)
      SHADER_ID="$2"
      shift 2
      ;;
    --material-slot)
      MATERIAL_SLOT="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$VRM" ]]; then
  echo "--vrm is required." >&2
  usage >&2
  exit 2
fi

case "$MODE" in
  evidence)
    APP="$REPO_ROOT/Builds/P0/macOS-Evidence/VirtualCharacterRender-P0.app"
    ;;
  performance)
    APP="$REPO_ROOT/Builds/P0/macOS-Performance/VirtualCharacterRender-P0.app"
    ;;
  *)
    echo "--mode must be evidence or performance." >&2
    exit 2
    ;;
esac

if [[ ! -d "$APP" ]]; then
  echo "P0 player not found: $APP" >&2
  echo "Build it from Unity first." >&2
  exit 1
fi

VRM="$(cd "$(dirname "$VRM")" && pwd)/$(basename "$VRM")"

ARGS=("--vcr-vrm=$VRM")

if [[ -n "$SHADER_BUNDLE" ]]; then
  SHADER_BUNDLE="$(cd "$(dirname "$SHADER_BUNDLE")" && pwd)/$(basename "$SHADER_BUNDLE")"
  ARGS+=("--vcr-shader-bundle=$SHADER_BUNDLE")
fi

if [[ -n "$SHADER_ID" ]]; then
  ARGS+=("--vcr-shader-id=$SHADER_ID")
fi

if [[ -n "$MATERIAL_SLOT" ]]; then
  ARGS+=("--vcr-material-slot=$MATERIAL_SLOT")
fi

echo "Launching: $APP"
echo "Mode: $MODE"
echo "VRM: $VRM"

open -W "$APP" --args "${ARGS[@]}"
