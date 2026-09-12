#!/usr/bin/env bash
# Shared helpers for the setup scripts. Sourced, not run directly.

VALHEIM_DIR="${VALHEIM_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Valheim}"
BEPINEX_DIR="$VALHEIM_DIR/BepInEx"
THUNDERSTORE_API="${THUNDERSTORE_API:-https://thunderstore.io/api/experimental/package}"

if [ -t 1 ]; then
  C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'; C_DIM=$'\033[2m'; C_B=$'\033[1m'; C_0=$'\033[0m'
else
  C_OK=""; C_WARN=""; C_ERR=""; C_DIM=""; C_B=""; C_0=""
fi

step() { printf '\n%s==> %s%s\n' "$C_B" "$*" "$C_0"; }
ok()   { printf '  %s✓%s %s\n' "$C_OK" "$C_0" "$*"; }
warn() { printf '  %s!%s %s\n' "$C_WARN" "$C_0" "$*"; }
die()  { printf '\n%sError:%s %s\n' "$C_ERR" "$C_0" "$*" >&2; exit 1; }
info() { printf '  %s%s%s\n' "$C_DIM" "$*" "$C_0"; }

# json_get FILE KEYPATH  (KEYPATH like latest.download_url)
json_get() {
  local file="$1" key="$2"
  if command -v plutil >/dev/null 2>&1 && plutil -extract "$key" raw -o - "$file" 2>/dev/null; then
    return 0
  fi
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$file" "$key" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
for k in sys.argv[2].split('.'):
    d = d[k]
print(d)
PY
    return $?
  fi
  die "Need plutil or python3 to read JSON."
}

is_valheim_running() {
  pgrep -f "valheim.app/Contents/MacOS" >/dev/null 2>&1
}

STEAM_LAUNCH_OPTIONS='/usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh %command%'

print_steam_step() {
  step "Last step is yours: Steam launch options"
  if command -v pbcopy >/dev/null 2>&1; then
    printf '%s' "$STEAM_LAUNCH_OPTIONS" | pbcopy
    ok "Already copied to your clipboard:"
  else
    info "Copy this:"
  fi
  printf '\n    %s\n\n' "$STEAM_LAUNCH_OPTIONS"
  cat <<MSG
  In Steam: right-click Valheim > Properties > General > Launch Options, paste, close.
  (The 'arch -x86_64' part runs the game under Rosetta. Without it Valheim still
  starts, just silently without mods.)

  Then launch Valheim from Steam and load a single-player world. It worked if:
    - the wooden UI panels look different (that's Minimal UI)
    - F1 opens Configuration Manager, F7 opens UnityExplorer
    - you see "Bone & Ember ... is running" when you spawn (our own mod, if it built)
    - $BEPINEX_DIR/LogOutput.log exists and lists the plugins
MSG
}
