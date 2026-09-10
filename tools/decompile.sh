#!/usr/bin/env bash
# Decompiles Valheim's main assemblies into reference/decompiled/ so you (and Claude Code)
# can read how the vanilla UI actually works. Re-run after every Valheim update.
# This output is the game's code: it's gitignored and must never be committed or shared.

set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
source "$HERE/lib.sh"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet/tools"
# ilspycmd targets an older .NET; let it run on whatever runtime you have.
export DOTNET_ROLL_FORWARD=Major

ASM="$(find "$VALHEIM_DIR/valheim.app" -name assembly_valheim.dll -path '*Managed*' 2>/dev/null | head -1 || true)"
[ -n "$ASM" ] || die "assembly_valheim.dll not found"
MANAGED_DIR="$(dirname "$ASM")"
OUT="$ROOT/reference/decompiled"

for name in assembly_valheim assembly_guiutils assembly_utils; do
  dll="$MANAGED_DIR/$name.dll"
  [ -f "$dll" ] || { warn "$name.dll not found, skipping"; continue; }
  rm -rf "${OUT:?}/$name"; mkdir -p "$OUT/$name"
  ilspycmd -p -o "$OUT/$name" -r "$MANAGED_DIR" "$dll" >/dev/null
  ok "$name -> reference/decompiled/$name ($(find "$OUT/$name" -name '*.cs' | wc -l | tr -d ' ') files)"
done
