using System.Xml;
using System.Xml.Linq;
using AwesomeAssertions;

namespace TiaViewer.Tests;

/// <summary>The open dialog and the Mac app's file types accept every archive, project and global library version.</summary>
public sealed class TiaFileTypesTest {
    [Test]
    public void Every_version_from_V11_to_V30_is_listed_with_and_without_service_pack() {
        TiaFileTypes.Extensions(TiaFileTypes.ArchivePrefix).Should().HaveCount(40)
            .And.StartWith("zap11").And.EndWith("zap30_1").And.Contain(["zap15_1", "zap18", "zap21"]);
    }

    [Test]
    public void Archives_projects_and_global_libraries_are_all_listed() {
        TiaFileTypes.AllExtensions.Should().Contain(["zap18", "ap15_1", "al21"]).And.HaveCount(120).And.OnlyHaveUniqueItems();
    }

    [Test]
    public void The_Mac_dialog_gets_each_extension_because_it_ignores_wildcards() {
        var patterns = TiaFileTypes.PickerType(wildcards: false).Patterns!;

        patterns.Should().Contain(["*.zap18", "*.zap15_1", "*.ap21", "*.al20", TiaFileTypes.DatabaseName]);
        patterns.Select(Path.GetExtension).Should().NotContain(extension => extension!.Contains('*'));
    }

    [Test]
    public void Other_dialogs_get_wildcards_that_also_match_later_versions() {
        TiaFileTypes.PickerType(wildcards: true).Patterns.Should().Equal("*.zap*", "*.ap*", "*.al*", TiaFileTypes.DatabaseName);
    }

    [Test]
    [Arguments("TIA Portal archive", TiaFileTypes.ArchivePrefix)]
    [Arguments("TIA Portal project", TiaFileTypes.ProjectPrefix)]
    [Arguments("TIA Portal global library", TiaFileTypes.LibraryPrefix)]
    public void The_Mac_app_opens_the_same_files_by_double_click(string typeName, string prefix) {
        var type = DocumentTypes().Single(entry => Value(entry, "CFBundleTypeName")?.Value == typeName);

        Value(type, "CFBundleTypeExtensions")!.Elements("string").Select(element => element.Value)
            .Should().Equal(TiaFileTypes.Extensions(prefix));
    }

    [Test]
    public void The_Mac_app_has_the_version_About_shows() {
        Value(Plist().Root!.Element("dict")!, "CFBundleShortVersionString")!.Value.Should().Be(AppInfo.Version);
    }

    private static IEnumerable<XElement> DocumentTypes() =>
        Value(Plist().Root!.Element("dict")!, "CFBundleDocumentTypes")!.Elements("dict");

    /// <summary>The element after a plist dictionary's key.</summary>
    private static XElement? Value(XElement dict, string key) =>
        dict.Elements("key").FirstOrDefault(element => element.Value == key)?.ElementsAfterSelf().First();

    private static XDocument Plist() {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "TiaTools.slnx"))) {
            folder = folder.Parent;
        }
        var path = Path.Combine(folder?.FullName ?? throw new InvalidOperationException("No TiaTools.slnx above the tests."), "src", "TiaViewer", "macos", "Info.plist");
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return XDocument.Load(reader);
    }
}
