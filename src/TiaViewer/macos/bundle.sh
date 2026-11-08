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

# A .NET built from source, such as Homebrew's, marks its Native AOT pack non-portable. The link then asks for the system's
# OpenSSL and Brotli, which macOS does not have to link against: take Homebrew's. The app then loads them from Homebrew when
# it runs, so it only runs where they are installed. Microsoft's own SDK links neither, and is what to build with to share it.
sdk="$(dotnet --info | sed -n 's/^ *Base Path: *//p' | head -n 1)"
root="${sdk%/sdk/*}"
for marker in "$root"/packs/Microsoft.NETCore.App.Runtime.NativeAOT."$runtime"/*/runtimes/"$runtime"/native/nonportable.txt; do
    [ -e "$marker" ] || continue
    if ! command -v brew >/dev/null 2>&1; then
        echo "This .NET ($root) needs OpenSSL and Brotli to link, and there is no Homebrew to take them from. Use Microsoft's .NET SDK." >&2
        exit 1
    fi
    openssl="$(brew --prefix openssl@3)/lib"
    brotli="$(brew --prefix brotli)/lib"
    if [ ! -d "$openssl" ] || [ ! -d "$brotli" ]; then
        echo "This .NET ($root) needs OpenSSL and Brotli to link: brew install openssl@3 brotli" >&2
        exit 1
    fi
    echo "Linking against Homebrew's OpenSSL and Brotli, which this .NET asks for: the app runs only where they are installed."
    LIBRARY_PATH="$openssl:$brotli${LIBRARY_PATH:+:$LIBRARY_PATH}"
    export LIBRARY_PATH
    break
done

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
