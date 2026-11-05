#!/bin/sh
# Publishes the viewer with Native AOT and wraps it as an app, on a Mac: AOT does not cross-compile.
# Usage: src/TiaViewer/macos/bundle.sh [output folder], from the repository's root. It leaves "TIA Viewer.app" there.
# Signed ad hoc, which is enough to run on this Mac. To hand it to others it needs a Developer ID and notarization.
set -eu

output="${1:-artifacts}"
case "$(uname -m)" in
    arm64) runtime=osx-arm64 ;;
    *) runtime=osx-x64 ;;
esac

publish="$output/publish-$runtime"
dotnet publish src/TiaViewer -c Release -r "$runtime" -o "$publish"

app="$output/TIA Viewer.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp src/TiaViewer/macos/Info.plist "$app/Contents/Info.plist"
cp src/TiaViewer/Assets/icon.icns "$app/Contents/Resources/icon.icns"
# The native libraries have to sit beside the executable; debug symbols do not.
find "$publish" -maxdepth 1 -type f ! -name '*.dbg' ! -name '*.dSYM' -exec cp {} "$app/Contents/MacOS/" \;
codesign --force --deep --sign - "$app"
# Tells Finder about the new app, so a double-click finds it.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$app"

echo "Built $app"
