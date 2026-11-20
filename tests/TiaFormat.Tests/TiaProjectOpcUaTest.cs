using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0005: a PLC's OPC UA report as the viewer's project holds it, beside the PLC's blocks.</summary>
public sealed class TiaProjectOpcUaTest {
    private static TiaProject Opened(TiaOpcUaFixture.Options? options = null) => TiaProjectTest.Opened(TiaOpcUaFixture.NewFile(options));

    private static TiaTreeNode Plc(TiaProject project, string name) => project.Root.Children.Single(node => node.Name == name);

    private static TiaItem Report(TiaProject project, string plc) =>
        Plc(project, plc).Children.Single(node => node.Item?.IsReport == true).Item!;

    // See a PLC's OPC UA report ---------------------------------------------------------------------------------------

    [Test]
    public void APlcOfTheProjectHoldsAnOpcUaEntry_First() {
        var plc = Plc(Opened(), "Filler");

        var entry = plc.Children.Where(node => node.Item?.IsReport == true).Should().ContainSingle().Subject;

        entry.Name.Should().Be("OPC UA");
        entry.Item!.ReportPlc.Should().Be("Filler");
        plc.Children[0].Should().BeSameAs(entry);
    }

    [Test]
    public void TheEntryShowsTheReportTiadumpWritesForThatPlc() {
        var project = Opened();
        var expected = new StringWriter { NewLine = "\n" };
        TiaOpcUaWriter.WriteMarkdown(TiaOpcUa.Read(project.Program, "Filler"), "Project.ap21", expected);

        project.Document(Report(project, "Filler")).Text.Should().Be(expected.ToString());
    }

    [Test]
    public void APlcWithoutAnOpcUaDeviceItemSaysSoInItsReport() {
        var project = Opened(new TiaOpcUaFixture.Options { HasItem = false });

        project.Document(Report(project, "Filler")).Text.Should().Contain("**Server: no OPC UA device item.**");
    }

    [Test]
    public void APlcHeldOnlyInAMasterCopyHasNoOpcUaEntry() {
        var project = Opened(new TiaOpcUaFixture.Options { HasMasterCopy = true });

        project.Reports.Select(report => report.ReportPlc).Should().Equal("Filler");
        project.Items.Should().Contain(item => item.Name == "Template");
    }

    // Copy it -------------------------------------------------------------------------------------------------------

    [Test]
    public void TheReportsTextIsWhatIsCopied() {
        var project = Opened();

        project.Document(Report(project, "Filler")).Text.Should().StartWith("# OPC UA: Project.ap21\n\n## PLC Filler\n");
    }

    // Keep it apart from the source -------------------------------------------------------------------------------

    [Test]
    public void TheReportUsesNothingAndIsUsedByNothing() {
        var project = Opened();
        var report = Report(project, "Filler");

        project.ReferencesTo(report).Should().BeEmpty();
        project.ReferencesFrom(report).Should().BeEmpty();
        project.ClosedBlocksAround(report).Should().Be(0);
    }

    [Test]
    public void TextOnlyInAReportIsNotFoundBySearch() {
        var project = Opened();
        project.Document(Report(project, "Filler")).Text.Should().Contain("Filler.OPC-UA");

        project.Search("Filler.OPC-UA").Should().BeEmpty();
    }

    [Test]
    public void TheReportIsNoItemOfTheProject_SoItIsNeitherCountedNorExported() {
        var project = Opened();

        project.Items.Should().NotContain(item => item.IsReport);
        project.Items.Select(item => item.Name).Should().BeEquivalentTo(["Recipe", "Service", "Line tags"]);
    }
}
