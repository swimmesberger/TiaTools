---
type: Guide
title: "Releasing"
description: "How a version is released: the tag, the workflow that builds the Windows and Mac programs, and what the zips hold."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T16:00:00Z }
status: draft
---
# Releasing

A release is a version tag, `v` and the viewer's `<Version>`, with a GitHub release that offers the viewer and `tiadump` as Windows and Mac programs. The workflow `.github/workflows/release.yml` builds both when a tag is pushed.

## Steps

1. Set the version in `src/TiaViewer/TiaViewer.csproj` (`<Version>`) and in `src/TiaViewer/macos/Info.plist` (`CFBundleShortVersionString`); a test checks that the two agree. Commit it to `main`.
2. Tag that commit and push the tag:

   ```bash
   git tag -a v0.2.0 -m "TiaTools 0.2.0"
   ```

   ```bash
   git push origin v0.2.0
   ```

3. The workflow builds on a Windows and a Mac runner at once. Each publishes both programs through Native AOT, checks that `tiadump` starts and packs its zips; the Mac does what [the Mac](#the-mac) describes. A last job attaches all four zips to a draft release with notes generated from the commits.
4. Edit the draft's notes: what is new, what to download, that the programs are not signed. Then publish it. The README links to the latest release, so it is a release only once it is published and not marked as a pre-release.

Run by hand on a branch (*Run workflow*), it builds and packs the zips without a release, and keeps them as the run's artifacts: a way to try a build before tagging. Run by hand on a tag that has the workflow, it builds that tag's zips again and replaces them in its release.

## What a zip holds

`TIA-Viewer-<version>-win-x64.zip` holds `TiaViewer.exe` and the three native libraries it loads (Skia, HarfBuzz and ANGLE); `tiadump-<version>-win-x64.zip` holds `tiadump.exe`. `TIA-Viewer-<version>-macos-universal.zip` holds `TIA Viewer.app`, with the three native libraries it loads (Skia, HarfBuzz and Avalonia's own) inside; `tiadump-<version>-macos-universal.zip` holds `tiadump`. The Mac programs run on Apple silicon and on Intel, from macOS 13. Each zip also has `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`.

`tools/package_release.py` makes them. It takes the notices from the packages the project restored: each package's licence and copyright from its nuspec, and the third-party notices file a package carries. Packages that only run at build time and native files for other platforms are left out. It stops when a package has a licence other than MIT or a licence file, or when the program's folder holds a file no package accounts for, so a new dependency cannot ship without its notice. It reads what the last restore wrote to `obj/`, so pack right after publishing: a build in between restores without the runtime, and the script stops rather than leave out the runtime's notices. Check what the new licence asks for before teaching the script about it.

## By hand on Windows

The same as the workflow, on Windows with Visual Studio's C++ tools and `vswhere.exe` on the `PATH`:

```bash
dotnet publish src/TiaViewer -c Release -r win-x64 -o artifacts/publish/viewer
```

```bash
dotnet publish src/TiaDump -c Release -r win-x64 -o artifacts/publish/tiadump
```

```bash
python tools/package_release.py --rid win-x64 --viewer artifacts/publish/viewer --tiadump artifacts/publish/tiadump --out artifacts/release
```

The programs are not signed, so Windows SmartScreen warns the first time they start.

## The Mac

The workflow runs `tools/release_macos.sh`. By hand, on a Mac with Xcode's command line tools and Microsoft's .NET SDK, from a checkout of the tag:

```bash
tools/release_macos.sh --tag v0.2.0
```

It publishes the viewer and `tiadump` for Apple silicon and for Intel, joins each pair into one program with `lipo`, wraps the viewer as an app with `src/TiaViewer/macos/bundle.sh --universal`, signs both ad hoc and packs them with `tools/package_release.py` into `artifacts/release`. Native AOT compiles for the other architecture of the same system, so one Mac builds both. To attach zips built by hand to the release:

```bash
gh release upload v0.2.0 artifacts/release/*-macos-universal.zip
```

It must be Microsoft's SDK: a .NET built from source, such as Homebrew's, links the programs against Homebrew's OpenSSL and Brotli, so they would run only where Homebrew has those installed. `bundle.sh --universal` refuses such a .NET. With both installed, put Microsoft's first, for example `PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" tools/release_macos.sh --tag v0.2.0`.

The programs are signed ad hoc, not with a Developer ID, and not notarized, so macOS blocks them the first time: the viewer starts after *Open Anyway* in System Settings › Privacy & Security, and either program after `xattr -d com.apple.quarantine` (`-dr` for the app). A Developer ID and notarization would remove that step; they need an Apple Developer account.
