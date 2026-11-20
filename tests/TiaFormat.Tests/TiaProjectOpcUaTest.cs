using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0005: a PLC's OPC UA configuration as the viewer's project holds it, beside the PLC's blocks.</summary>
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
    public void TheEntryShowsThePlcsOpcUaConfiguration_ReadWhenFirstShown() {
        var project = Opened();
        var report = Report(project, "Filler");

        var opcUa = project.OpcUa(report)!;

        opcUa.Name.Should().Be("Filler");
        opcUa.Server!.IsEnabled.Should().BeTrue();
        opcUa.DataBlocks.Select(block => block.Name).Should().Equal("Recipe", "Service");
        project.OpcUa(report).Should().BeSameAs(opcUa);
        project.OpcUa(project.Items[0]).Should().BeNull();
    }

    [Test]
    public void SecurityPoliciesCarryTiasNamesInTiasOrder() {
        var project = Opened();

        project.OpcUa(Report(project, "Filler"))!.Server!.SecurityPolicies.Select(policy => (policy.Name, policy.Key)).Should().Equal(
            ("No security", "None"), ("Basic128Rsa15 - Sign", "128RSA_S"), ("Basic128Rsa15 - Sign & Encrypt", "128RSA_SE"), ("Basic256 - Sign", "256_S"), ("Basic256 - Sign & Encrypt", "256_SE"), ("Basic256Sha256 - Sign", "256SHA_S"),
            ("Basic256Sha256 - Sign & Encrypt", "256SHA_SE"), ("Aes128Sha256RsaOaep - Sign", "256RSA_OAEP_S"), ("Aes128Sha256RsaOaep - Sign & Encrypt", "256RSA_OAEP_SE"),
            ("Aes256Sha256RsaPss - Sign", "256RSA_PSS_S"), ("Aes256Sha256RsaPss - Sign & Encrypt", "256RSA_PSS_SE"));
    }

    [Test]
    public void ADataBlockOrTagRowLeadsToItsSource() {
        var project = Opened();
        var report = Report(project, "Filler");

        project.Resolve(report, "Recipe")!.Value.Item.Name.Should().Be("Recipe");
        var tag = project.Resolve(report, "Level")!.Value;
        tag.Item.Name.Should().Be("Line tags");
        project.Document(tag.Item).LineText(tag.Line).Should().Contain("Level");
    }

    // Spec 0006: its nodes ------------------------------------------------------------------------------------------

    [Test]
    public void TheNodesAreDerivedWhenFirstAskedFor_AndOnlyOnce() {
        var project = Opened();
        var report = Report(project, "Filler");

        var nodes = project.OpcUaNodes(report)!;

        nodes.Plc.Should().Be("Filler");
        project.OpcUaNodes(report).Should().BeSameAs(nodes);
        project.OpcUaNodes(project.Items[0]).Should().BeNull();
    }

    [Test]
    public void ANodeRowLeadsToItsDataBlockOrTagTable() {
        var project = Opened();
        var report = Report(project, "Filler");
        var nodes = project.OpcUaNodes(report)!.Nodes;

        project.Resolve(report, nodes.Single(node => node.NodeId == "ns=3;s=\"Recipe\".\"Limit\"").Source!)!.Value.Item.Name.Should().Be("Recipe");
        project.Resolve(report, nodes.Single(node => node.NodeId == "ns=3;s=\"Level\"").Source!)!.Value.Item.Name.Should().Be("Line tags");
    }

    // Copy it as text -------------------------------------------------------------------------------------------------

    [Test]
    public void CopyGivesTheTextTiadumpWritesForThatPlc() {
        var project = Opened();
        var expected = new StringWriter { NewLine = "\n" };
        TiaOpcUaWriter.WriteMarkdown(TiaOpcUa.Read(project.Program, "Filler"), "Project.ap21", expected);

        project.Document(Report(project, "Filler")).Text.Should().Be(expected.ToString());
    }

    [Test]
    public void APlcWithoutAnOpcUaDeviceItemSaysSo() {
        var project = Opened(new TiaOpcUaFixture.Options { HasItem = false });

        project.OpcUa(Report(project, "Filler"))!.Server.Should().BeNull();
        project.Document(Report(project, "Filler")).Text.Should().Contain("**Server: no OPC UA device item.**");
    }

    [Test]
    public void APlcHeldOnlyInAMasterCopyHasNoOpcUaEntry() {
        var project = Opened(new TiaOpcUaFixture.Options { HasMasterCopy = true });

        project.Reports.Select(report => report.ReportPlc).Should().Equal("Filler");
        project.Items.Should().Contain(item => item.Name == "Template");
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

