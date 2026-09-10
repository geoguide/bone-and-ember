#!/usr/bin/env bash
# Installs the BepInEx mod loader plus a few client-side mods into your Valheim folder.
# Safe to re-run: it updates packages in place and never overwrites your config files.
#
# Usage: tools/install-game-mods.sh            (installs everything in PACKAGES)
#        VALHEIM_DIR=/some/path tools/install-game-mods.sh

set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

# Thunderstore packages (namespace/name). The loader is handled separately below.
LOADER="denikson/BepInExPack_Valheim"
PACKAGES=(
  "Azumatt/Official_BepInEx_ConfigurationManager"  # F1 in game: live settings for every mod
  "Azumatt/Minimal_UI"                             # the mod we're test-driving
  "ValheimModding/UnityExplorer"                   # F7 in game: inspect live UI objects (dev tool)
)


WORK="$(mktemp -d "${TMPDIR:-/tmp}/valheim-mods.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
INSTALL_LOG="$(cd "$HERE/.." && pwd)/.install-log.txt"

# ---------------------------------------------------------------- preflight
step "Checking your setup"
[ -d "$VALHEIM_DIR" ] || die "Valheim folder not found at: $VALHEIM_DIR
  In Steam: right-click Valheim > Manage > Browse local files, then re-run with
  VALHEIM_DIR=\"<that path>\" $0"
[ -d "$VALHEIM_DIR/valheim.app" ] || die "No valheim.app inside $VALHEIM_DIR. Is that the right folder?"
ok "Valheim found"

if is_valheim_running; then die "Valheim is running. Quit the game first, then re-run."; fi
ok "Valheim is not running"

for cmd in curl unzip; do command -v "$cmd" >/dev/null || die "Missing '$cmd'."; done

if [ "$(uname -s)" = "Darwin" ] && [ "$(uname -m)" = "arm64" ]; then
  if /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
    ok "Rosetta is installed (BepInEx needs it on Apple Silicon)"
  else
    die "Rosetta isn't installed. BepInEx only runs under Rosetta on Apple Silicon.
  Install it with:  softwareupdate --install-rosetta --agree-to-license
  then re-run this script."
  fi
fi

# ---------------------------------------------------------------- helpers
# fetch_pkg ns/name -> prints path to unzipped folder, sets PKG_VERSION
fetch_pkg() {
  local id="$1" ns name meta url dir
  ns="${id%%/*}"; name="${id##*/}"
  meta="$WORK/$ns-$name.json"
  curl -fsSL "$THUNDERSTORE_API/$ns/$name/" -o "$meta" || die "Couldn't reach Thunderstore for $id"
  PKG_VERSION="$(json_get "$meta" latest.version_number)"
  url="$(json_get "$meta" latest.download_url)"
  dir="$WORK/$ns-$name"
  curl -fsSL "$url" -o "$dir.zip" || die "Download failed for $id"
  mkdir -p "$dir"
  unzip -q -o "$dir.zip" -d "$dir"
  PKG_DIR="$dir"
}

# copy_no_clobber SRC_DIR DEST_DIR : copies files, never overwriting existing ones
copy_no_clobber() {
  local src="$1" dest="$2"
  mkdir -p "$dest"
  (cd "$src" && find . -type f) | while IFS= read -r f; do
    if [ ! -e "$dest/$f" ]; then
      mkdir -p "$dest/$(dirname "$f")"
      cp "$src/$f" "$dest/$f"
    fi
  done
}

# copy_over SRC_DIR DEST_DIR : copies files, overwriting
copy_over() {
  mkdir -p "$2"
  cp -R "$1/." "$2/"
}

# install_plugin_pkg ns/name : mirrors how r2modman lays out a Thunderstore package
install_plugin_pkg() {
  local id="$1" key src entry base
  key="${id%%/*}-${id##*/}"
  fetch_pkg "$id"; src="$PKG_DIR"

  local plugin_dest="$BEPINEX_DIR/plugins/$key"
  rm -rf "$plugin_dest"; mkdir -p "$plugin_dest"

  for entry in "$src"/* "$src"/.[!.]*; do
    [ -e "$entry" ] || continue
    base="$(basename "$entry")"
    case "$base" in
      manifest.json|icon.png|README.md|CHANGELOG.md|LICENSE|LICENSE.md) ;;
      BepInEx)
        [ -d "$entry/plugins" ]  && copy_over "$entry/plugins" "$plugin_dest"
        [ -d "$entry/patchers" ] && copy_over "$entry/patchers" "$BEPINEX_DIR/patchers/$key"
        [ -d "$entry/config" ]   && copy_no_clobber "$entry/config" "$BEPINEX_DIR/config"
        ;;
      plugins)  copy_over "$entry" "$plugin_dest" ;;
      patchers) copy_over "$entry" "$BEPINEX_DIR/patchers/$key" ;;
      config)   copy_no_clobber "$entry" "$BEPINEX_DIR/config" ;;
      *)        cp -R "$entry" "$plugin_dest/" ;;
    esac
  done

  # Minimal UI looks for its MUI_*.png skins under BepInEx/config. If the package
  # shipped them loose instead, mirror them there (without touching your own files).
  if [ "$key" = "Azumatt-Minimal_UI" ]; then
    local loose
    loose="$(find "$plugin_dest" -type f -name 'MUI_*.png' | head -1 || true)"
    if [ -n "$loose" ] && ! find "$BEPINEX_DIR/config" -name 'MUI_*.png' 2>/dev/null | grep -q .; then
      copy_no_clobber "$(dirname "$loose")" "$BEPINEX_DIR/config/MinimalUI"
      info "copied Minimal UI skin images into BepInEx/config/MinimalUI"
    fi
  fi

  { echo "== $id $PKG_VERSION"; (cd "$src" && find . -type f | sort); } >> "$INSTALL_LOG"
  ok "$id $PKG_VERSION"
}

# ---------------------------------------------------------------- loader
step "Installing the mod loader (BepInEx)"
: > "$INSTALL_LOG"
fetch_pkg "$LOADER"
LOADER_SCRIPT="$(find "$PKG_DIR" -name start_game_bepinex.sh -type f 2>/dev/null | head -1 || true)"
[ -n "$LOADER_SCRIPT" ] || die "BepInExPack layout changed: no start_game_bepinex.sh found"
LOADER_ROOT="$(dirname "$LOADER_SCRIPT")"

# Everything except BepInEx/config goes in as-is; your existing config is left alone.
for entry in "$LOADER_ROOT"/*; do
  base="$(basename "$entry")"
  if [ "$base" = "BepInEx" ]; then
    for sub in "$entry"/*; do
      sb="$(basename "$sub")"
      if [ "$sb" = "config" ]; then copy_no_clobber "$sub" "$BEPINEX_DIR/config"
      elif [ -d "$sub" ]; then copy_over "$sub" "$BEPINEX_DIR/$sb"
      else cp "$sub" "$BEPINEX_DIR/"; fi
    done
  else
    cp -R "$entry" "$VALHEIM_DIR/"
  fi
done
mkdir -p "$BEPINEX_DIR/plugins" "$BEPINEX_DIR/config"
{ echo "== $LOADER $PKG_VERSION"; (cd "$LOADER_ROOT" && find . -type f | sort); } >> "$INSTALL_LOG"
ok "$LOADER $PKG_VERSION"

# ---------------------------------------------------------------- mods
step "Installing mods"
for p in "${PACKAGES[@]}"; do install_plugin_pkg "$p"; done

# ---------------------------------------------------------------- permissions
step "Fixing Mac permissions"
chmod +x "$VALHEIM_DIR/start_game_bepinex.sh"
[ -f "$VALHEIM_DIR/start_server_bepinex.sh" ] && chmod +x "$VALHEIM_DIR/start_server_bepinex.sh"
if command -v xattr >/dev/null 2>&1; then
  (cd "$VALHEIM_DIR" && xattr -dr com.apple.quarantine BepInEx doorstop_libs doorstop_config.ini start_game_bepinex.sh 2>/dev/null) || true
fi
ok "launch script is executable, quarantine flags cleared"

# ---------------------------------------------------------------- steam
if command -v pbcopy >/dev/null 2>&1; then printf '%s' "$STEAM_LAUNCH_OPTIONS" | pbcopy; fi
info "Install details logged to: $INSTALL_LOG"
[ "${SETUP_ALL:-0}" = "1" ] || print_steam_step
