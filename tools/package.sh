#!/usr/bin/env bash
# Builds a Release DLL and zips it with the install guide, ready to send to
# someone. Output lands in dist/.
#
# Deliberately ships two files and nothing else. The repo also contains
# reference/decompiled/, which is Iron Gate's own code recovered for reading,
# and that must never leave this machine.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
cd "$ROOT"

VERSION="$(grep -o 'ModVersion = "[^"]*"' src/Plugin.cs | head -1 | cut -d'"' -f2)"
[ -n "$VERSION" ] || { echo "Could not read ModVersion from src/Plugin.cs" >&2; exit 1; }

NAME="BoneAndEmber"
STAGE="$ROOT/dist/$NAME-$VERSION"
ZIP="$ROOT/dist/$NAME-$VERSION.zip"

echo "Building $NAME $VERSION (Release)..."
dotnet build -c Release -v quiet --nologo

DLL="$ROOT/bin/Release/$NAME.dll"
[ -f "$DLL" ] || { echo "Build did not produce $DLL" >&2; exit 1; }

rm -rf "$STAGE" "$ZIP"
mkdir -p "$STAGE"
cp "$DLL" "$STAGE/"
cp "$ROOT/docs/INSTALL.md" "$STAGE/"

cd "$ROOT/dist"
zip -qr "$(basename "$ZIP")" "$(basename "$STAGE")"
rm -rf "$STAGE"

echo
echo "Packaged: dist/$(basename "$ZIP")"
unzip -l "$ZIP" | sed 's/^/  /'
echo
echo "Send that zip. They unzip it, read INSTALL.md, and drop the DLL into BepInEx/plugins."
