#!/usr/bin/env bash
# Builds the zip Thunderstore wants, which is a different shape from the plain
# release zip that tools/package.sh makes.
#
# Thunderstore requires manifest.json, icon.png and README.md at the root of the
# archive. The DLL goes under plugins/, which BepInExPack_Valheim maps onto
# BepInEx/plugins, so mod managers install it to the right place.
#
# Ships the built DLL and nothing else from the repo. reference/decompiled/ is
# Iron Gate's own code recovered for reading, and it must never leave here.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
cd "$ROOT"

NAME="BoneAndEmber"
MANIFEST="$ROOT/thunderstore/manifest.json"
ICON="$ROOT/thunderstore/icon.png"

for f in "$MANIFEST" "$ICON"; do
    [ -f "$f" ] || { echo "Missing $f" >&2; exit 1; }
done

VERSION="$(grep -o 'ModVersion = "[^"]*"' src/Plugin.cs | head -1 | cut -d'"' -f2)"
[ -n "$VERSION" ] || { echo "Could not read ModVersion from src/Plugin.cs" >&2; exit 1; }

MANIFEST_VERSION="$(python3 -c "import json;print(json.load(open('$MANIFEST'))['version_number'])")"
if [ "$VERSION" != "$MANIFEST_VERSION" ]; then
    echo "Version mismatch: src/Plugin.cs says $VERSION, thunderstore/manifest.json says $MANIFEST_VERSION." >&2
    echo "Thunderstore rejects a version that already exists, and it can never be reused. Make them match." >&2
    exit 1
fi

# Thunderstore icons must be exactly 256x256.
python3 - "$ICON" <<'PY'
import struct, sys
d = open(sys.argv[1], 'rb').read()
assert d[:8] == b'\x89PNG\r\n\x1a\n', 'icon.png is not a PNG'
w, h = struct.unpack('>II', d[16:24])
assert (w, h) == (256, 256), 'icon.png must be exactly 256x256, got %dx%d' % (w, h)
PY

STAGE="$ROOT/dist/thunderstore-$VERSION"
ZIP="$ROOT/dist/$NAME-$VERSION-thunderstore.zip"

echo "Building $NAME $VERSION (Release)..."
dotnet build -c Release -v quiet --nologo

DLL="$ROOT/bin/Release/$NAME.dll"
[ -f "$DLL" ] || { echo "Build did not produce $DLL" >&2; exit 1; }

rm -rf "$STAGE" "$ZIP"
mkdir -p "$STAGE/plugins"
cp "$MANIFEST" "$ICON" "$STAGE/"
cp "$ROOT/README.md" "$STAGE/"
cp "$DLL" "$STAGE/plugins/"

cd "$STAGE"
zip -qr "$ZIP" .
cd "$ROOT"
rm -rf "$STAGE"

echo
echo "Packaged: dist/$(basename "$ZIP")"
unzip -l "$ZIP" | sed 's/^/  /'
echo
echo "Upload at https://thunderstore.io/c/valheim/create/"
echo "Version numbers are permanent there: you cannot re-upload $VERSION, even if you delete it."
