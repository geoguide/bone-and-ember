#!/usr/bin/env bash
# One command to get everything ready: game mods first, then the dev toolchain.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
source "$HERE/lib.sh"
export SETUP_ALL=1
"$HERE/install-game-mods.sh"
"$HERE/setup-dev.sh"
print_steam_step
ROOT="$(cd "$HERE/.." && pwd)"
printf '\n%s==> Then start Claude Code here:%s\n    cd "%s" && claude\n\n' "$C_B" "$C_0" "$ROOT"
