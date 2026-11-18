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
MAC = "macos-universal"
# A Mach-O file with several architectures, and one with one, as their first bytes.
FAT = b"\xca\xfe\xba\xbe\x00\x00\x00\x02"
THIN = b"\xcf\xfa\xed\xfe\x0c\x00\x00\x01"


def nuspec(folder, name, licence='<license type="expression">MIT</license>', copyright="Copyright Someone"):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / f"{name.lower()}.nuspec").write_text(
        f'<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
        f"<id>{name}</id>{licence}<copyright>{copyright}</copyright></metadata></package>", encoding="utf-8")


class Fixture:
    """A repository with the viewer and tiadump restored against a package folder, and their publish output."""

    def __init__(self, root, rid=RID):
        self.root = root
        self.rid = rid
        self.packages = root / "packages"
        self.out = root / "out"
        (root / "LICENSE").write_text("the licence", encoding="utf-8")
        (root / "src" / "TiaViewer").mkdir(parents=True)
        (root / "src" / "TiaViewer" / "TiaViewer.csproj").write_text("<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>", encoding="utf-8")
        if rid == MAC:
            self.viewer = self.app()
            self.tiadump = self.publish("tiadump", "tiadump", FAT)
        else:
            self.viewer = self.publish("viewer", "TiaViewer.exe")
            self.tiadump = self.publish("tiadump", "tiadump.exe")
        self.restore("TiaViewer", {"Avalonia/12.1.5": "package", "TiaFormat/1.0.0": "project"})
        self.restore("TiaDump", {})
        nuspec(self.packages / "avalonia" / "12.1.5", "Avalonia", copyright="Copyright The AvaloniaUI Project")

    def publish(self, name, program, content=b"MZ"):
        folder = self.root / name
        folder.mkdir()
        (folder / program).write_bytes(content)
        (folder / program).chmod(0o755)
        (folder / (Path(program).stem + ".pdb")).write_bytes(b"pdb")
        return folder

    def app(self):
        """The viewer as bundle.sh makes it: executable, Info.plist, icon and signature."""
        app = self.root / "mac" / "TIA Viewer.app"
        (app / "Contents" / "MacOS").mkdir(parents=True)
        (app / "Contents" / "Resources").mkdir()
        (app / "Contents" / "_CodeSignature").mkdir()
        (app / "Contents" / "MacOS" / "TiaViewer").write_bytes(FAT)
        (app / "Contents" / "MacOS" / "TiaViewer").chmod(0o755)
        (app / "Contents" / "Info.plist").write_text("<plist/>", encoding="utf-8")
        (app / "Contents" / "Resources" / "icon.icns").write_bytes(b"icns")
        (app / "Contents" / "_CodeSignature" / "CodeResources").write_text("<plist/>", encoding="utf-8")
        return app

    def restore(self, project, libraries):
        obj = self.root / "src" / project / "obj"
        obj.mkdir(parents=True, exist_ok=True)
        # A Mac restore names the runtime published last; the universal build was published for both.
        published = ["osx-arm64", "osx-x64"] if self.rid == MAC else [self.rid]
        assets = {
            "packageFolders": {str(self.packages) + "/": {}},
            "libraries": {key: {"type": kind} for key, kind in libraries.items()},
            "project": {"frameworks": {"net10.0": {"downloadDependencies": [
                {"name": f"Microsoft.NETCore.App.Runtime.NativeAOT.{published[-1]}", "version": "[10.0.15, 10.0.15]"}]}}},
        }
        (obj / "project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        for rid in published:
            runtime = self.packages / f"microsoft.netcore.app.runtime.nativeaot.{rid}" / "10.0.15"
            nuspec(runtime, f"Microsoft.NETCore.App.Runtime.NativeAOT.{rid}", copyright="Microsoft Corporation")
            (runtime / "THIRD-PARTY-NOTICES.TXT").write_text("runtime notices", encoding="utf-8")

    def release(self, tag=None):
        return release(self.rid, self.viewer, self.tiadump, self.out, tag, repo=self.root)


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

    def test_a_restore_without_the_runtime_pack_is_refused(self):
        assets_file = self.f.root / "src" / "TiaViewer" / "obj" / "project.assets.json"
        assets = json.loads(assets_file.read_text(encoding="utf-8"))
        assets["project"]["frameworks"]["net10.0"]["downloadDependencies"] = []
        assets_file.write_text(json.dumps(assets), encoding="utf-8")
        with self.assertRaisesRegex(ReleaseError, "names no Native AOT runtime pack"):
            self.f.release()

    def test_build_tools_and_other_platforms_native_assets_are_left_out(self):
        self.f.restore("TiaViewer", {"Avalonia/12.1.5": "package", "Microsoft.DotNet.ILCompiler/10.0.15": "package",
                                     "SkiaSharp.NativeAssets.Linux/3.0.0": "package"})
        with zipfile.ZipFile(self.f.release()[0]) as z:
            notices = z.read("THIRD-PARTY-NOTICES.txt").decode()
        self.assertNotIn("ILCompiler", notices)
        self.assertNotIn("Linux", notices)


class PackageMacRelease(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.f = Fixture(Path(self._tmp.name), MAC)

    def tearDown(self):
        self._tmp.cleanup()

    def test_the_viewer_zip_holds_the_whole_app_beside_its_licence_and_notices(self):
        made = self.f.release("v1.2.3")
        self.assertEqual([p.name for p in made], ["TIA-Viewer-1.2.3-macos-universal.zip", "tiadump-1.2.3-macos-universal.zip"])
        with zipfile.ZipFile(made[0]) as z:
            self.assertEqual(sorted(z.namelist()), [
                "LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "TIA Viewer.app/Contents/Info.plist",
                "TIA Viewer.app/Contents/MacOS/TiaViewer", "TIA Viewer.app/Contents/Resources/icon.icns",
                "TIA Viewer.app/Contents/_CodeSignature/CodeResources"])

    def test_the_programs_stay_executable(self):
        for zip_path, program in zip(self.f.release(), ["TIA Viewer.app/Contents/MacOS/TiaViewer", "tiadump"]):
            with zipfile.ZipFile(zip_path) as z:
                self.assertEqual(z.getinfo(program).external_attr >> 16 & 0o777, 0o755)

    def test_the_notices_name_the_runtime_of_both_architectures(self):
        with zipfile.ZipFile(self.f.release()[1]) as z:
            notices = z.read("THIRD-PARTY-NOTICES.txt").decode()
        self.assertIn("- Microsoft.NETCore.App.Runtime.NativeAOT.osx-arm64 10.0.15: MIT", notices)
        self.assertIn("- Microsoft.NETCore.App.Runtime.NativeAOT.osx-x64 10.0.15: MIT", notices)

    def test_the_texts_have_unix_line_ends(self):
        with zipfile.ZipFile(self.f.release()[0]) as z:
            self.assertNotIn(b"\r\n", z.read("THIRD-PARTY-NOTICES.txt"))

    def test_a_native_library_from_the_systems_folder_ships_in_the_app(self):
        self.f.restore("TiaViewer", {"Avalonia/12.1.5": "package", "Native.Thing.NativeAssets.macOS/2.0.0": "package"})
        folder = self.f.packages / "native.thing.nativeassets.macos" / "2.0.0"
        nuspec(folder, "Native.Thing.NativeAssets.macOS")
        (folder / "runtimes" / "osx" / "native").mkdir(parents=True)
        (folder / "runtimes" / "osx" / "native" / "libthing.dylib").write_bytes(FAT)
        (self.f.viewer / "Contents" / "MacOS" / "libthing.dylib").write_bytes(FAT)
        with zipfile.ZipFile(self.f.release()[0]) as z:
            self.assertIn("TIA Viewer.app/Contents/MacOS/libthing.dylib", z.namelist())

    def test_a_package_of_another_systems_native_files_is_left_out(self):
        self.f.restore("TiaViewer", {"Avalonia/12.1.5": "package", "Some.Windows.Natives/2.0.0": "package"})
        folder = self.f.packages / "some.windows.natives" / "2.0.0"
        nuspec(folder, "Some.Windows.Natives")
        (folder / "runtimes" / "win-x64" / "native").mkdir(parents=True)
        (folder / "runtimes" / "win-x64" / "native" / "gl.dll").write_bytes(b"MZ")
        with zipfile.ZipFile(self.f.release()[0]) as z:
            self.assertNotIn("Some.Windows.Natives", z.read("THIRD-PARTY-NOTICES.txt").decode())

    def test_a_program_of_one_architecture_is_refused(self):
        (self.f.tiadump / "tiadump").write_bytes(THIN)
        with self.assertRaisesRegex(ReleaseError, "holds one architecture"):
            self.f.release()

    def test_debug_symbols_in_the_app_are_refused(self):
        (self.f.viewer / "Contents" / "MacOS" / "TiaFormat.pdb").write_bytes(b"pdb")
        with self.assertRaisesRegex(ReleaseError, "debug symbols"):
            self.f.release()

    def test_a_viewer_that_is_not_an_app_is_refused(self):
        self.f.viewer = self.f.tiadump
        with self.assertRaisesRegex(ReleaseError, "the app that src/TiaViewer/macos/bundle.sh makes"):
            self.f.release()


if __name__ == "__main__":
    unittest.main()
