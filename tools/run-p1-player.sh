#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./tools/run-p1-player.sh [options]

Options:
  --mode development|release
  --vrm PATH
  --config PATH
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

MODE="release"
VRM=""
CONFIG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      MODE="$2"
      shift 2
      ;;
    --vrm)
      VRM="$2"
      shift 2
      ;;
    --config)
      CONFIG="$2"
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

case "$MODE" in
  development)
    APP="$REPO_ROOT/Builds/P1/macOS-Development/VirtualCharacterRender.app"
    ;;
  release)
    APP="$REPO_ROOT/Builds/P1/macOS/VirtualCharacterRender.app"
    ;;
  *)
    echo "--mode must be development or release." >&2
    exit 2
    ;;
esac

if [[ ! -d "$APP" ]]; then
  echo "P1 player not found: $APP" >&2
  echo "Build it from Unity first." >&2
  exit 1
fi

ARGS=()

if [[ -n "$VRM" ]]; then
  VRM="$(cd "$(dirname "$VRM")" && pwd)/$(basename "$VRM")"
  ARGS+=("--vcr-vrm=$VRM")
fi

if [[ -n "$CONFIG" ]]; then
  CONFIG_DIR="$(cd "$(dirname "$CONFIG")" && pwd)"
  CONFIG="$CONFIG_DIR/$(basename "$CONFIG")"
  ARGS+=("--vcr-config=$CONFIG")
fi

echo "Launching: $APP"
echo "Mode: $MODE"
[[ -n "$VRM" ]] && echo "VRM: $VRM"
[[ -n "$CONFIG" ]] && echo "Config: $CONFIG"

open -W "$APP" --args "${ARGS[@]}"
