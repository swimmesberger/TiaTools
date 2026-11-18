---
type: Guide
title: "Releasing"
description: "How a version is released: the tag, the workflow that builds the Windows programs, and what the zips hold."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T16:00:00Z }
status: draft
---
# Releasing

A release is a version tag, `v` and the viewer's `<Version>`, with a GitHub release that offers the viewer and `tiadump` as Windows programs. The workflow `.github/workflows/release.yml` builds them when a tag is pushed.

## Steps

1. Set the version in `src/TiaViewer/TiaViewer.csproj` (`<Version>`) and in `src/TiaViewer/macos/Info.plist` (`CFBundleShortVersionString`); a test checks that the two agree. Commit it to `main`.
2. Tag that commit and push the tag:

   ```bash
   git tag -a v0.2.0 -m "TiaTools 0.2.0"
   ```

   ```bash
   git push origin v0.2.0
   ```

3. The workflow publishes both programs through Native AOT on Windows, checks that `tiadump` starts, packs the zips and attaches them to a draft release with notes generated from the commits.
4. Edit the draft's notes: what is new, what to download, that the programs are not signed. Then publish it. The README links to the latest release, so it is a release only once it is published and not marked as a pre-release.

Run by hand on a branch (*Run workflow*), it builds and packs the zips without a release, and keeps them as the run's artifact: a way to try a build before tagging. Run by hand on a tag that has the workflow, it builds that tag's zips again and replaces them in its release.

## What a zip holds

`TIA-Viewer-<version>-win-x64.zip` holds `TiaViewer.exe` and the three native libraries it loads (Skia, HarfBuzz and ANGLE); `tiadump-<version>-win-x64.zip` holds `tiadump.exe`. Each also has `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`.

`tools/package_release.py` makes them. It takes the notices from the packages the project restored: each package's licence and copyright from its nuspec, and the third-party notices file a package carries. Packages that only run at build time and native assets for other platforms are left out. It stops when a package has a licence other than MIT or a licence file, or when the publish folder holds a file no package accounts for, so a new dependency cannot ship without its notice. Check what the new licence asks for before teaching the script about it.

## By hand

The same, without the workflow, on Windows with Visual Studio's C++ tools and `vswhere.exe` on the `PATH`:

```bash
dotnet publish src/TiaViewer -c Release -r win-x64 -o artifacts/publish/viewer
```

```bash
dotnet publish src/TiaDump -c Release -r win-x64 -o artifacts/publish/tiadump
```

```bash
python tools/package_release.py --rid win-x64 --viewer artifacts/publish/viewer --tiadump artifacts/publish/tiadump --out artifacts/release
```

The programs are not signed, so Windows SmartScreen warns the first time they start. A Mac build is not part of a release yet: `src/TiaViewer/macos/bundle.sh` builds the app, and handing it to others needs signing and notarization.
