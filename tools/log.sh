#!/usr/bin/env bash
# Prints the BepInEx log. Usage: tools/log.sh [grep-pattern]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
source "$HERE/lib.sh"
LOG="$BEPINEX_DIR/LogOutput.log"
[ -f "$LOG" ] || die "No log yet at $LOG. Launch the game once."
if [ $# -gt 0 ] && [ -n "$1" ]; then grep -n -i -E "$1" "$LOG"; else cat "$LOG"; fi
