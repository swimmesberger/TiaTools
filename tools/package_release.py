#!/usr/bin/env python3
"""Pack the published viewer and tiadump into the zips a release offers, with the licence and third-party notices.

Usage: tools/package_release.py --rid win-x64 --viewer DIR --tiadump DIR --out DIR [--tag vX.Y.Z]

DIR are `dotnet publish` output folders for that runtime. The version is the viewer's <Version>; --tag must match it.
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


def packages(assets_file, rid):
    """The packages whose code or files end up in a program published for rid: (id, version, folder)."""
    assets = json.loads(Path(assets_file).read_text(encoding="utf-8"))
    root = Path(next(iter(assets["packageFolders"])))
    found = [tuple(key.split("/", 1)) for key, lib in assets["libraries"].items() if lib["type"] == "package"]
    # The runtime pack Native AOT links in is downloaded, not restored as a library.
    for framework in assets["project"]["frameworks"].values():
        for download in framework.get("downloadDependencies", []):
            if download["name"].lower() == f"microsoft.netcore.app.runtime.nativeaot.{rid}":
                found.append((download["name"], download["version"].strip("[]").split(",")[0].strip()))
    target = NATIVE_OS[rid.split("-")[0]]
    result = []
    for name, ver in found:
        lower = name.lower()
        if lower in BUILD_ONLY or (lower.startswith("runtime.") and lower.endswith(".microsoft.dotnet.ilcompiler")):
            continue
        native = re.search(r"\.nativeassets\.(\w+)$", lower)
        if native and native.group(1) != target:
            continue
        result.append((name, ver, root / lower / ver.lower()))
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
    native = {f.name.lower() for _, _, folder in packs for f in (folder / "runtimes" / rid / "native").glob("*")}
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


def pack(out, name, files, notice_text, licence_text):
    out.mkdir(parents=True, exist_ok=True)
    path = out / name
    crlf = lambda text: text.replace("\r\n", "\n").replace("\n", "\r\n")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f in files:
            z.write(f, f.name)
        z.writestr("LICENSE.txt", crlf(licence_text))
        z.writestr("THIRD-PARTY-NOTICES.txt", crlf(notice_text))
    return path


def release(rid, viewer, tiadump, out, tag=None, repo=REPO):
    ver = version(repo / "src" / "TiaViewer" / "TiaViewer.csproj")
    if tag is not None and tag != f"v{ver}":
        raise ReleaseError(f"tag {tag} does not match the viewer's version {ver}; tag v{ver}, or change <Version> first")
    licence = (repo / "LICENSE").read_text(encoding="utf-8")
    exe = ".exe" if rid.startswith("win") else ""
    made = []
    for title, publish, project, program in (("TIA-Viewer", viewer, "TiaViewer", "TiaViewer" + exe), ("tiadump", tiadump, "TiaDump", "tiadump" + exe)):
        packs = packages(repo / "src" / project / "obj" / "project.assets.json", rid)
        files = program_files(publish, program, packs, rid)
        made.append(pack(Path(out), f"{title}-{ver}-{rid}.zip", files, notices(packs), licence))
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
