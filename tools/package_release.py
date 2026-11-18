#!/usr/bin/env python3
"""Pack the published viewer and tiadump into the zips a release offers, with the licence and third-party notices.

Usage: tools/package_release.py --rid win-x64 --viewer DIR --tiadump DIR --out DIR [--tag vX.Y.Z]

DIR are `dotnet publish` output folders for that runtime. The version is the viewer's <Version>; --tag must match it.
For the Mac, --rid is macos-universal, --viewer the app that `src/TiaViewer/macos/bundle.sh --universal` makes and
--tiadump a folder with tiadump joined from both architectures; tools/release_macos.sh does all of it.
Each zip holds the program files, LICENSE.txt and THIRD-PARTY-NOTICES.txt. The notices come from the packages the
project restored (obj/project.assets.json): each package's licence and copyright from its nuspec, and any
THIRD-PARTY-NOTICES file it carries. It stops on a licence it does not know, or on a published file that no package
accounts for, so a new dependency cannot ship without its notice. Standard library only.
"""
import argparse, hashlib, json, re, sys, zipfile
from pathlib import Path
from xml.etree import ElementTree

REPO = Path(__file__).resolve().parent.parent

# Packages that only run at build time: nothing of them is in the published program.
BUILD_ONLY = {"microsoft.dotnet.ilcompiler", "microsoft.net.illink.tasks", "avalonia.buildservices"}
# Native assets per operating system, as the packages name them; only the target's are shipped.
NATIVE_OS = {"win": "win32", "osx": "macos", "linux": "linux"}
# Builds that join several runtimes into one program: the Mac's runs on Apple silicon and Intel.
UNIVERSAL = {"macos-universal": ("osx-arm64", "osx-x64")}
# The first bytes of a Mach-O file that holds several architectures.
FAT_MAGIC = b"\xca\xfe\xba\xbe"

MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."""


class ReleaseError(Exception):
    pass


def version(project=REPO / "src" / "TiaViewer" / "TiaViewer.csproj"):
    match = re.search(r"<Version>([^<]+)</Version>", project.read_text(encoding="utf-8"))
    if not match:
        raise ReleaseError(f"{project}: no <Version>")
    return match.group(1).strip()


def runtimes(rid):
    """The runtimes a build for rid is published for: rid itself, or each one a universal build joins."""
    return UNIVERSAL.get(rid, (rid,))


def native_folders(rid):
    """The folders under a package's runtimes/ whose native files a build for rid takes: the runtime's own
    (win-x64), or the system's (osx, where the Mac's packages put theirs)."""
    return {name for r in runtimes(rid) for name in (r, r.split("-")[0])}


def ships(folder, rid):
    """Whether a package can be in a build for rid. One of native files only, with none for rid, is not."""
    native = folder / "runtimes"
    if not native.is_dir() or (folder / "lib").is_dir():
        return True
    return any((native / sub / "native").is_dir() for sub in native_folders(rid))


def packages(assets_file, rid):
    """The packages whose code or files end up in a program published for rid: (id, version, folder)."""
    assets = json.loads(Path(assets_file).read_text(encoding="utf-8"))
    root = Path(next(iter(assets["packageFolders"])))
    found = [tuple(key.split("/", 1)) for key, lib in assets["libraries"].items() if lib["type"] == "package"]
    # The runtime pack Native AOT links in is downloaded, not restored as a library. A restore names only the pack
    # of the runtime it was for, so a universal build takes the same version of each other runtime's pack.
    for framework in assets["project"]["frameworks"].values():
        for download in framework.get("downloadDependencies", []):
            name = download["name"].lower()
            prefix = "microsoft.netcore.app.runtime.nativeaot."
            if name.startswith(prefix) and name[len(prefix):] in runtimes(rid):
                ver = download["version"].strip("[]").split(",")[0].strip()
                found += [(f"Microsoft.NETCore.App.Runtime.NativeAOT.{r}", ver) for r in runtimes(rid)]
    # A build or restore without a runtime since the publish names no pack, and the runtime's notices would be missing.
    if not any(name.lower().startswith("microsoft.netcore.app.runtime.nativeaot.") for name, _ in found):
        raise ReleaseError(f"{assets_file}: names no Native AOT runtime pack for {rid}; publish for it again right before packing")
    found = list(dict.fromkeys(found))
    target = NATIVE_OS[runtimes(rid)[0].split("-")[0]]
    result = []
    for name, ver in found:
        lower = name.lower()
        if lower in BUILD_ONLY or (lower.startswith("runtime.") and lower.endswith(".microsoft.dotnet.ilcompiler")):
            continue
        native = re.search(r"\.nativeassets\.(\w+)$", lower)
        if native and native.group(1) != target:
            continue
        folder = root / lower / ver.lower()
        if not ships(folder, rid):
            continue
        result.append((name, ver, folder))
    return sorted(result, key=lambda p: p[0].lower())


def notices(packs):
    """THIRD-PARTY-NOTICES.txt for these packages."""
    lines = ["Third-party notices", "", "TiaTools itself is under the licence in LICENSE.txt. This program contains the following components, each under its own licence.", ""]
    sections, seen = [], set()
    for name, ver, folder in packs:
        nuspecs = list(folder.glob("*.nuspec"))
        if not nuspecs:
            raise ReleaseError(f"{name} {ver}: no nuspec in {folder}; restore the project first")
        spec = ElementTree.parse(nuspecs[0]).getroot()
        meta = next(e for e in spec if e.tag.endswith("metadata"))
        field = lambda tag: next((e for e in meta if e.tag.endswith(tag)), None)
        licence, copyright = field("license"), field("copyright")
        holder = (copyright.text or "").strip() if copyright is not None else ""
        if licence is not None and licence.get("type") == "expression" and licence.text.strip() == "MIT":
            lines.append(f"- {name} {ver}: MIT")
            body = f"{name} {ver}\n\n{holder or 'Copyright (c) the ' + name + ' authors'}\n\n{MIT}"
        elif licence is not None and licence.get("type") == "file":
            lines.append(f"- {name} {ver}: the licence below")
            body = f"{name} {ver}\n\n{holder}\n\n{(folder / licence.text.strip()).read_text(encoding='utf-8-sig').strip()}"
        else:
            what = "none" if licence is None else f"{licence.get('type')} {licence.text}"
            raise ReleaseError(f"{name} {ver}: licence {what} is not one this script knows; add it after checking what it asks for")
        sections.append(body)
        for extra in sorted(folder.glob("THIRD-PARTY-NOTICES*")):
            text = extra.read_text(encoding="utf-8-sig", errors="replace").strip()
            digest = hashlib.sha256(text.encode()).hexdigest()
            if digest in seen:
                sections.append(f"{name} {ver}: its third-party notices are the same as those printed above.")
                continue
            seen.add(digest)
            sections.append(f"{name} {ver}: third-party notices\n\n{text}")
    rule = "\n\n" + "=" * 78 + "\n\n"
    return "\n".join(lines) + rule + rule.join(sections) + "\n"


def program_files(publish, program, packs, rid):
    """The files of a publish folder that go into the zip: the program and native libraries from the packages."""
    native = {f.name.lower() for _, _, folder in packs for sub in native_folders(rid) for f in (folder / "runtimes" / sub / "native").glob("*")}
    files = []
    for f in sorted(Path(publish).iterdir()):
        if f.suffix.lower() == ".pdb":
            continue
        if f.name.lower() != program.lower() and f.name.lower() not in native:
            raise ReleaseError(f"{f}: no package accounts for it, so its licence is unknown")
        files.append(f)
    if not any(f.name.lower() == program.lower() for f in files):
        raise ReleaseError(f"{publish}: no {program}")
    return files


def app_entries(app, program, packs, rid):
    """The files of a Mac app and their names in the zip. Its executable folder is checked like a publish folder; the
    rest (Info.plist, icon, signature) is the app's own. All of it is packed, since the signature seals every file."""
    app = Path(app)
    if app.suffix != ".app" or not (app / "Contents" / "MacOS").is_dir():
        raise ReleaseError(f"{app}: for the Mac, --viewer is the app that src/TiaViewer/macos/bundle.sh makes")
    checked = program_files(app / "Contents" / "MacOS", program, packs, rid)
    for f in (app / "Contents" / "MacOS").iterdir():
        if f not in checked:
            raise ReleaseError(f"{f}: debug symbols do not belong in the app; bundle.sh leaves them out")
    return [(f, f"{app.name}/{f.relative_to(app).as_posix()}") for f in sorted(app.rglob("*")) if f.is_file()]


def pack(out, name, entries, notice_text, licence_text, crlf):
    """A zip of (file, name in the zip) with the licence and notices. Files keep their permissions, which the Mac's
    executables need; the texts get Windows line ends for Windows."""
    out.mkdir(parents=True, exist_ok=True)
    path = out / name
    ends = lambda text: text.replace("\r\n", "\n").replace("\n", "\r\n") if crlf else text.replace("\r\n", "\n")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f, arcname in entries:
            z.write(f, arcname)
        z.writestr("LICENSE.txt", ends(licence_text))
        z.writestr("THIRD-PARTY-NOTICES.txt", ends(notice_text))
    return path


def release(rid, viewer, tiadump, out, tag=None, repo=REPO):
    ver = version(repo / "src" / "TiaViewer" / "TiaViewer.csproj")
    if tag is not None and tag != f"v{ver}":
        raise ReleaseError(f"tag {tag} does not match the viewer's version {ver}; tag v{ver}, or change <Version> first")
    licence = (repo / "LICENSE").read_text(encoding="utf-8")
    system = runtimes(rid)[0].split("-")[0]
    exe = ".exe" if system == "win" else ""
    made = []
    for title, publish, project, program in (("TIA-Viewer", viewer, "TiaViewer", "TiaViewer" + exe), ("tiadump", tiadump, "TiaDump", "tiadump" + exe)):
        packs = packages(repo / "src" / project / "obj" / "project.assets.json", rid)
        if system == "osx" and project == "TiaViewer":
            entries = app_entries(publish, program, packs, rid)
        else:
            entries = [(f, f.name) for f in program_files(publish, program, packs, rid)]
        if rid in UNIVERSAL:
            main_file = next(f for f, _ in entries if f.name.lower() == program.lower())
            with open(main_file, "rb") as f:
                if f.read(4) != FAT_MAGIC:
                    raise ReleaseError(f"{main_file}: holds one architecture; a {rid} build joins {' and '.join(runtimes(rid))} with lipo")
        made.append(pack(Path(out), f"{title}-{ver}-{rid}.zip", entries, notices(packs), licence, crlf=system == "win"))
    return made


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--rid", required=True)
    parser.add_argument("--viewer", required=True)
    parser.add_argument("--tiadump", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--tag")
    args = parser.parse_args(argv)
    try:
        for path in release(args.rid, args.viewer, args.tiadump, args.out, args.tag):
            print(path)
    except ReleaseError as e:
        print(f"package_release: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
