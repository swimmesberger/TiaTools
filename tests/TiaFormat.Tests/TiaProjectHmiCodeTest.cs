using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0017: an HMI device's code as the viewer's project holds it, on the reference project <c>HmiScripts</c>.</summary>
public sealed class TiaProjectHmiCodeTest {
    private static readonly Lazy<TiaProject> Project = new(() => {
        var project = TiaProject.Open(ReferenceProjects.Archive("HmiScripts"));
        project.BuildIndex();
        return project;
    });

    private static TiaTreeNode Device(string name) => Project.Value.Root.Children.Single(node => node.Name == name && node.Item is null && node.Children.Any(child => child.Item?.IsHmi == true));

    private static TiaItem Entry(string device, params string[] path) {
        var node = Device(device);
        foreach (var name in path) {
            node = node.Children.Single(child => child.Name == name);
        }
        return node.Item!;
    }

    [Test]
    public void AChosenHmiScriptIsShownAsItsCodeHighlightedAsJavaScript() {
        var item = Entry("HMI_Panel", "Scripts", "Overview", "StartButton.Tapped");
        var document = Project.Value.Document(item);

        item.IsHmiSource.Should().BeTrue();
        item.Language.Should().Be("JavaScript");
        document.Text.Should().StartWith("let count = Tags(\"StartCount\").Read();");
        document.Tokens.Should().Contain(token => token.Kind == TiaTokenKind.Keyword && document.Text.Substring(token.Start, token.Length) == "let");
        document.Tokens.Should().Contain(token => token.Kind == TiaTokenKind.Literal && document.Text.Substring(token.Start, token.Length) == "\"StartCount\"");
    }

    [Test]
    public void AChosenGlobalScriptModuleIsShownAsItsCode() {
        var document = Project.Value.Document(Entry("HMI_Panel", "Script modules", "DosingFunctions"));

        document.Text.Should().Contain("export function limit(value) {");
        document.Tokens.Should().Contain(token => token.Kind == TiaTokenKind.Comment);
    }

    [Test]
    public void AChosenVbScriptIsShownAsItsSourceHighlightedAsVbScript() {
        var item = Entry("HMI_RT_1", "VB scripts", "CheckPump");
        var document = Project.Value.Document(item);

        item.Language.Should().Be("VBScript");
        document.Text.Should().StartWith("Sub CheckPump()\n");
        document.Tokens.Should().Contain(token => token.Kind == TiaTokenKind.Keyword && document.Text.Substring(token.Start, token.Length) == "Then");
    }

    [Test]
    public void AChosenCustomWebControlListsItsPackagesFilesAndRunsNone() {
        var item = Entry("HMI_Panel", "Custom web controls", "Dosing gauge");
        var before = Directory.GetDirectories(Path.GetTempPath(), "TiaViewer-control-*").Length;

        Project.Value.Document(item).Text.Should().Contain("control/index.html").And.Contain("manifest.json");
        item.IsHmiSource.Should().BeFalse();
        // Showing a control writes nothing: its files reach a folder only when its preview is asked for (FR-009).
        Directory.GetDirectories(Path.GetTempPath(), "TiaViewer-control-*").Length.Should().Be(before);
    }
}
