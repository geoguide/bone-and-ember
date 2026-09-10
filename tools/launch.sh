#!/usr/bin/env bash
# Launches Valheim through Steam (so the BepInEx launch options apply) and follows the mod log.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
source "$HERE/lib.sh"
open "steam://run/892970"
LOG="$BEPINEX_DIR/LogOutput.log"
echo "Waiting for $LOG (Ctrl+C to stop following)..."
for _ in $(seq 1 90); do [ -f "$LOG" ] && break; sleep 1; done
[ -f "$LOG" ] || die "No BepInEx log after 90s. Check the Steam launch options."
tail -n 50 -F "$LOG"
