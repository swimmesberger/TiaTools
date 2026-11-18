#!/bin/sh
# Publishes the viewer with Native AOT and wraps it as an app, on a Mac: AOT does not compile for another system.
# Usage: src/TiaViewer/macos/bundle.sh [--universal] [output folder], from the repository's root. It leaves "TIA Viewer.app" there.
# Without --universal the app is for this Mac's architecture; with it, for Apple silicon and Intel in one, as a release
# needs (docs/releasing.md). Signed ad hoc, which is enough to run on this Mac; others have to allow it once.
set -eu

universal=false
if [ "${1:-}" = "--universal" ]; then
    universal=true
    shift
fi
output="${1:-artifacts}"
if $universal; then
    runtimes="osx-arm64 osx-x64"
else
    case "$(uname -m)" in
        arm64) runtimes=osx-arm64 ;;
        *) runtimes=osx-x64 ;;
    esac
fi

# A .NET built from source, such as Homebrew's, marks its Native AOT pack non-portable. The link then asks for the system's
# OpenSSL and Brotli, which macOS does not have to link against: take Homebrew's. The app then loads them from Homebrew when
# it runs, so it only runs where they are installed. Microsoft's own SDK links neither, and is what to build with to share it.
sdk="$(dotnet --info | sed -n 's/^ *Base Path: *//p' | head -n 1)"
root="${sdk%/sdk/*}"
for runtime in $runtimes; do
    for marker in "$root"/packs/Microsoft.NETCore.App.Runtime.NativeAOT."$runtime"/*/runtimes/"$runtime"/native/nonportable.txt; do
        [ -e "$marker" ] || continue
        if $universal; then
            echo "This .NET ($root) would make the app need Homebrew's OpenSSL and Brotli, so it is not one to hand to others. Build it with Microsoft's .NET SDK." >&2
            exit 1
        fi
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
done

for runtime in $runtimes; do
    dotnet publish src/TiaViewer -c Release -r "$runtime" -o "$output/publish-$runtime"
done

app="$output/TIA Viewer.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp src/TiaViewer/macos/Info.plist "$app/Contents/Info.plist"
cp src/TiaViewer/Assets/icon.icns "$app/Contents/Resources/icon.icns"
# The native libraries have to sit beside the executable; debug symbols do not. A file that differs between the
# architectures is joined into one with lipo; the packages' native libraries already hold both, so they are the same.
set -- $runtimes
first="$output/publish-$1"
for file in "$first"/*; do
    name="$(basename "$file")"
    case "$name" in *.dbg|*.dSYM|*.pdb) continue ;; esac
    [ -f "$file" ] || continue
    if [ $# -eq 1 ] || cmp -s "$file" "$output/publish-$2/$name"; then
        cp "$file" "$app/Contents/MacOS/"
    else
        lipo -create "$file" "$output/publish-$2/$name" -output "$app/Contents/MacOS/$name"
    fi
done
codesign --force --deep --sign - "$app"
# Tells Finder about the new app, so a double-click finds it.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$app"

echo "Built $app"
