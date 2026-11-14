#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./tools/run-p1-player.sh [options]

Options:
  --mode development|release    P1 build variant (default: release)
  --app PATH                    Override the P1 .app bundle path
  --vrm PATH                    Existing VRM file to load
  --config PATH                 Existing runtime configuration file
  --evidence-dir PATH           New directory for a direct-run Player.log and
                                native process exit evidence (no old evidence)
  -h, --help                    Show this help

Without --evidence-dir the app opens via macOS LaunchServices for normal
interactive use. With --evidence-dir the real app executable is run directly
to preserve its process exit code; this is NOT human/GUI acceptance.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd -P)"
MODE="release"
VRM=""
CONFIG=""
APP_OVERRIDE=""
EVIDENCE=""

# Never discard an option value or silently fall back to another build.
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode|--vrm|--config|--app|--evidence-dir)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "$1 requires a nonempty value" >&2
        exit 2
      fi
      case "$1" in
        --mode) MODE="$2" ;;
        --vrm) VRM="$2" ;;
        --config) CONFIG="$2" ;;
        --app) APP_OVERRIDE="$2" ;;
        --evidence-dir) EVIDENCE="$2" ;;
      esac
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
  development) FOLDER="macOS-Development" ;;
  release) FOLDER="macOS" ;;
  *)
    echo "--mode must be development or release." >&2
    exit 2
    ;;
esac

APP="$REPO_ROOT/unity/Builds/P1/$FOLDER/VirtualCharacterRender.app"
if [[ -n "$APP_OVERRIDE" ]]; then
  APP="$APP_OVERRIDE"
fi
if [[ ! -d "$APP" ]]; then
  echo "P1 .app bundle not found: $APP" >&2
  exit 1
fi
APP="$(cd "$APP" && pwd -P)"
PLIST="$APP/Contents/Info.plist"
if [[ ! -f "$PLIST" ]]; then
  echo "P1 .app Info.plist missing: $PLIST" >&2
  exit 1
fi

# CFBundleExecutable, not a guessed file name, defines a macOS Player's
# actual native process. Reject any executable name that escapes Contents.
EXEC_NAME="$(python3 - "$PLIST" <<'PY'
import plistlib
import sys
with open(sys.argv[1], "rb") as source:
    name = plistlib.load(source).get("CFBundleExecutable")
if (not isinstance(name, str) or not name or
        name in (".", "..") or
        any(ch in name for ch in ("/", "\\", ":", "\x00"))):
    raise SystemExit("Invalid P1 macOS CFBundleExecutable")
print(name)
PY
)"
EXECUTABLE="$APP/Contents/MacOS/$EXEC_NAME"
if [[ ! -f "$EXECUTABLE" || ! -x "$EXECUTABLE" ]]; then
  echo "P1 .app native executable missing or not executable: $EXECUTABLE" >&2
  exit 1
fi

ARGS=()
if [[ -n "$VRM" ]]; then
  if [[ ! -f "$VRM" ]]; then
    echo "P1 VRM file missing: $VRM" >&2
    exit 1
  fi
  VRM="$(cd "$(dirname "$VRM")" && pwd -P)/$(basename "$VRM")"
  ARGS+=("--vcr-vrm=$VRM")
fi
if [[ -n "$CONFIG" ]]; then
  if [[ ! -f "$CONFIG" ]]; then
    echo "P1 config file missing: $CONFIG" >&2
    exit 1
  fi
  CONFIG="$(cd "$(dirname "$CONFIG")" && pwd -P)/$(basename "$CONFIG")"
  ARGS+=("--vcr-config=$CONFIG")
fi

echo "Launching P1 macOS $MODE Player: $APP"
[[ -n "$VRM" ]] && echo "VRM: $VRM"
[[ -n "$CONFIG" ]] && echo "Config: $CONFIG"

if [[ -z "$EVIDENCE" ]]; then
  # LaunchServices activates the app as an ordinary desktop application,
  # but open -W is not reliable evidence of the process exit status.
  echo "LaunchServices mode: no native exit evidence; use --evidence-dir for verification."
  open -W "$APP" --args "${ARGS[@]}"
  exit $?
fi

# A new directory per attempt prevents stale Player.log or JSON from being
# misread as evidence of a successful subsequent GUI session.
if [[ -e "$EVIDENCE" || -L "$EVIDENCE" ]]; then
  echo "Evidence directory already exists; use a fresh path: $EVIDENCE" >&2
  exit 1
fi
mkdir -p "$EVIDENCE"
EVIDENCE="$(cd "$EVIDENCE" && pwd -P)"
LOG="$EVIDENCE/Player.log"
ARGS+=("-logFile" "$LOG")
echo "Direct Player process with fresh evidence: $EVIDENCE"

# Running the CFBundleExecutable directly preserves its actual exit status;
# this is the same launch class used by native CI smoke, not the open -W
# LaunchServices status. A nonzero exit must remain nonzero.
if "$EXECUTABLE" "${ARGS[@]}"; then
  EXIT_CODE=0
else
  EXIT_CODE=$?
fi

# Even on failure, preserve partial machine evidence for diagnosis. A missing
# or empty log never qualifies as a verified session.
python3 - "$EXECUTABLE" "$LOG" "$EVIDENCE/launch-evidence.json" "$MODE" "$EXIT_CODE" <<'PY'
import datetime
import hashlib
import json
from pathlib import Path
import sys

exe, log, target = map(Path, sys.argv[1:4])
mode = sys.argv[4]
code = int(sys.argv[5])

def digest(path):
    if not path.is_file():
        return None
    result = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()

report = {
    "platform": "macOS",
    "mode": mode,
    "playerExecutableSha256": digest(exe),
    "exitCode": code,
    "playerLogSha256": digest(log),
    "playerLogBytes": log.stat().st_size if log.is_file() else 0,
    "utcRecordedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "sourceBound": False,
    "physicalHumanAcceptance": False,
}
target.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
PY

if [[ ! -s "$LOG" ]]; then
  echo "P1 macOS Player did not produce a fresh nonempty Player.log: $LOG" >&2
  if [[ $EXIT_CODE -eq 0 ]]; then
    exit 1
  fi
fi
echo "Review Player.log; automated exit evidence is NOT operator acceptance."
exit "$EXIT_CODE"
