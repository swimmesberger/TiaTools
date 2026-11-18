#!/bin/sh
# Builds a release's Mac zips: the viewer and tiadump for Apple silicon and Intel in one, packed with their licence and
# third-party notices by tools/package_release.py. See docs/releasing.md.
# Usage: tools/release_macos.sh [--tag vX.Y.Z], from the repository's root, on a Mac with Microsoft's .NET SDK.
# It leaves TIA-Viewer-<version>-macos-universal.zip and tiadump-<version>-macos-universal.zip in artifacts/release.
set -eu

out=artifacts/macos
rm -rf "$out"
src/TiaViewer/macos/bundle.sh --universal "$out"

for runtime in osx-arm64 osx-x64; do
    dotnet publish src/TiaDump -c Release -r "$runtime" -o "$out/tiadump-$runtime"
done
mkdir -p "$out/tiadump"
lipo -create "$out/tiadump-osx-arm64/tiadump" "$out/tiadump-osx-x64/tiadump" -output "$out/tiadump/tiadump"
codesign --force --sign - "$out/tiadump/tiadump"
"$out/tiadump/tiadump" --help > /dev/null

python3 tools/package_release.py --rid macos-universal --viewer "$out/TIA Viewer.app" --tiadump "$out/tiadump" --out artifacts/release "$@"
