"""Tests of tools/package_release.py on a fake repository, package folder and publish output."""
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "tools"))

from package_release import ReleaseError, release  # noqa: E402

RID = "win-x64"


def nuspec(folder, name, licence='<license type="expression">MIT</license>', copyright="Copyright Someone"):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / f"{name.lower()}.nuspec").write_text(
        f'<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
        f"<id>{name}</id>{licence}<copyright>{copyright}</copyright></metadata></package>", encoding="utf-8")


class Fixture:
    """A repository with the viewer and tiadump restored against a package folder, and their publish output."""

    def __init__(self, root):
        self.root = root
        self.packages = root / "packages"
        self.out = root / "out"
        (root / "LICENSE").write_text("the licence", encoding="utf-8")
        (root / "src" / "TiaViewer").mkdir(parents=True)
        (root / "src" / "TiaViewer" / "TiaViewer.csproj").write_text("<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>", encoding="utf-8")
        self.viewer = self.publish("viewer", "TiaViewer.exe")
        self.tiadump = self.publish("tiadump", "tiadump.exe")
        self.restore("TiaViewer", {"Avalonia/12.1.5": "package", "TiaFormat/1.0.0": "project"})
        self.restore("TiaDump", {})
        nuspec(self.packages / "avalonia" / "12.1.5", "Avalonia", copyright="Copyright The AvaloniaUI Project")

    def publish(self, name, program):
        folder = self.root / name
        folder.mkdir()
        (folder / program).write_bytes(b"MZ")
        (folder / (Path(program).stem + ".pdb")).write_bytes(b"pdb")
        return folder

    def restore(self, project, libraries):
        obj = self.root / "src" / project / "obj"
        obj.mkdir(parents=True, exist_ok=True)
        assets = {
            "packageFolders": {str(self.packages) + "/": {}},
            "libraries": {key: {"type": kind} for key, kind in libraries.items()},
            "project": {"frameworks": {"net10.0": {"downloadDependencies": [
                {"name": f"Microsoft.NETCore.App.Runtime.NativeAOT.{RID}", "version": "[10.0.15, 10.0.15]"}]}}},
        }
        (obj / "project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        runtime = self.packages / f"microsoft.netcore.app.runtime.nativeaot.{RID}" / "10.0.15"
        nuspec(runtime, f"Microsoft.NETCore.App.Runtime.NativeAOT.{RID}", copyright="Microsoft Corporation")
        (runtime / "THIRD-PARTY-NOTICES.TXT").write_text("runtime notices", encoding="utf-8")

    def release(self, tag=None):
        return release(RID, self.viewer, self.tiadump, self.out, tag, repo=self.root)


class PackageRelease(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.f = Fixture(Path(self._tmp.name))

    def tearDown(self):
        self._tmp.cleanup()

    def test_each_program_gets_a_zip_with_its_licence_and_notices(self):
        made = self.f.release("v1.2.3")
        self.assertEqual([p.name for p in made], ["TIA-Viewer-1.2.3-win-x64.zip", "tiadump-1.2.3-win-x64.zip"])
        with zipfile.ZipFile(made[0]) as z:
            self.assertEqual(sorted(z.namelist()), ["LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "TiaViewer.exe"])
            notices = z.read("THIRD-PARTY-NOTICES.txt").decode()
        self.assertIn("- Avalonia 12.1.5: MIT", notices)
        self.assertIn("Copyright The AvaloniaUI Project", notices)
        self.assertIn("runtime notices", notices)
        self.assertNotIn("TiaFormat", notices)

    def test_a_tag_that_is_not_the_version_is_refused(self):
        with self.assertRaisesRegex(ReleaseError, "does not match"):
            self.f.release("v1.2.4")

    def test_an_unknown_licence_is_refused(self):
        nuspec(self.f.packages / "avalonia" / "12.1.5", "Avalonia", licence='<license type="expression">GPL-3.0-only</license>')
        with self.assertRaisesRegex(ReleaseError, "not one this script knows"):
            self.f.release()

    def test_a_published_file_no_package_accounts_for_is_refused(self):
        (self.f.viewer / "mystery.dll").write_bytes(b"MZ")
        with self.assertRaisesRegex(ReleaseError, "no package accounts for it"):
            self.f.release()

    def test_a_native_library_ships_with_its_package_and_licence_file(self):
        self.f.restore("TiaViewer", {"Avalonia/12.1.5": "package", "Native.Thing/2.0.0": "package"})
        folder = self.f.packages / "native.thing" / "2.0.0"
        nuspec(folder, "Native.Thing", licence='<license type="file">LICENSE</license>')
        (folder / "LICENSE").write_text("a BSD licence", encoding="utf-8")
        (folder / "runtimes" / RID / "native").mkdir(parents=True)
        (folder / "runtimes" / RID / "native" / "libthing.dll").write_bytes(b"MZ")
        (self.f.viewer / "libthing.dll").write_bytes(b"MZ")
        with zipfile.ZipFile(self.f.release()[0]) as z:
            self.assertIn("libthing.dll", z.namelist())
            self.assertIn("a BSD licence", z.read("THIRD-PARTY-NOTICES.txt").decode())

    def test_build_tools_and_other_platforms_native_assets_are_left_out(self):
        self.f.restore("TiaViewer", {"Avalonia/12.1.5": "package", "Microsoft.DotNet.ILCompiler/10.0.15": "package",
                                     "SkiaSharp.NativeAssets.Linux/3.0.0": "package"})
        with zipfile.ZipFile(self.f.release()[0]) as z:
            notices = z.read("THIRD-PARTY-NOTICES.txt").decode()
        self.assertNotIn("ILCompiler", notices)
        self.assertNotIn("Linux", notices)


if __name__ == "__main__":
    unittest.main()
