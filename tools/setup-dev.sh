#!/usr/bin/env bash
# Sets up the dev side: .NET SDK check, the ilspycmd decompiler, local.props with
# your Valheim paths, and a decompiled copy of the game code for reference.
# Safe to re-run.

set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

step "Finding Valheim's game code"
[ -d "$VALHEIM_DIR/valheim.app" ] || die "valheim.app not found in $VALHEIM_DIR"
ASM="$(find "$VALHEIM_DIR/valheim.app" -name assembly_valheim.dll -path '*Managed*' 2>/dev/null | head -1 || true)"
[ -n "$ASM" ] || die "Couldn't find assembly_valheim.dll inside valheim.app"
MANAGED_DIR="$(dirname "$ASM")"
ok "$MANAGED_DIR"
[ -f "$BEPINEX_DIR/core/BepInEx.dll" ] || warn "BepInEx isn't installed yet. Run tools/install-game-mods.sh first or the build will fail."

step "Writing local.props (your machine's paths, not committed)"
cat > "$ROOT/local.props" <<XML
<Project>
  <PropertyGroup>
    <ValheimDir>$VALHEIM_DIR</ValheimDir>
    <ManagedDir>$MANAGED_DIR</ManagedDir>
  </PropertyGroup>
</Project>
XML
ok "local.props"

step "Checking for the .NET SDK"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet/tools"
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q .; then
  ok "dotnet $(dotnet --version)"
else
  warn ".NET SDK not found. It compiles the C# mod into a .dll."
  if command -v brew >/dev/null 2>&1; then
    read -r -p "  Install it now with Homebrew? (asks for your Mac password) [y/N] " yn
    if [[ "$yn" =~ ^[Yy]$ ]]; then
      brew install --cask dotnet-sdk
      ok "dotnet $(dotnet --version)"
    else
      die "Skipped. Install later with: brew install --cask dotnet-sdk"
    fi
  else
    die "Install the .NET SDK from https://dotnet.microsoft.com/download then re-run."
  fi
fi

step "Installing ilspycmd (turns the game's compiled code back into readable C#)"
if command -v ilspycmd >/dev/null 2>&1; then
  ok "already installed"
else
  dotnet tool install -g ilspycmd >/dev/null && ok "installed" || die "ilspycmd install failed"
fi

step "Decompiling game code into reference/decompiled (gitignored, never commit it)"
"$HERE/decompile.sh"

step "Test build"
if [ -f "$BEPINEX_DIR/core/BepInEx.dll" ]; then
  if (cd "$ROOT" && dotnet build -c Debug -v quiet -nologo); then
    ok "Built and copied to BepInEx/plugins/ValheimUI"
  else
    warn "Build failed. That's fine for now: first job for Claude Code is to fix it."
  fi
else
  warn "Skipped: BepInEx not installed yet."
fi

if [ "${SETUP_ALL:-0}" != "1" ]; then
  printf '\n  Dev setup done. Start Claude Code from this folder:\n    cd "%s" && claude\n' "$ROOT"
fi
