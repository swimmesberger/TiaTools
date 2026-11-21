using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0012, the viewer: a PLC's safety program as the viewer's project holds it, on the reference projects.</summary>
public sealed class TiaProjectSafetyTest {
    private static readonly Lazy<TiaProject> SafetyProject = new(() => TiaProject.Open(ReferenceProjects.Archive("Safety")));

    private static TiaTreeNode Plc(TiaProject project, string name) => project.Root.Children.Single(node => node.Name == name);

    private static TiaItem Entry(TiaProject project, string plc) =>
        Plc(project, plc).Children.Single(node => node.Item?.Kind == TiaItem.SafetyReportKind).Item!;

    [Test]
    public void APlcWithASafetyProgramHoldsASafetyEntry_AfterOpcUa() {
        var plc = Plc(SafetyProject.Value, "PLC_Safety");

        plc.Children[0].Name.Should().Be(TiaItem.OpcUaReportKind);
        plc.Children[1].Name.Should().Be(TiaItem.SafetyReportKind);
        plc.Children[1].Item!.ReportPlc.Should().Be("PLC_Safety");
        plc.Children[1].Item!.IsReport.Should().BeTrue();
    }

    [Test]
    public void APlcWithoutASafetyProgramHasNoSafetyEntry() {
        var project = TiaProject.Open(ReferenceProjects.Archive("Languages"));

        project.Reports.Should().NotContain(report => report.Kind == TiaItem.SafetyReportKind);
        project.Kinds.Should().NotContain(TiaItem.SafetyReportKind);
    }

    [Test]
    public void TheEntryShowsThePlcsSafetyProgram_ReadWhenFirstShown() {
        var project = SafetyProject.Value;
        var entry = Entry(project, "PLC_Safety");

        var safety = project.Safety(entry)!;

        safety.Name.Should().Be("PLC_Safety");
        safety.Program!.RuntimeGroups.Should().HaveCount(2);
        project.Safety(entry).Should().BeSameAs(safety);
        project.Safety(Plc(project, "PLC_Safety").Children[0].Item!).Should().BeNull("the OPC UA entry is no safety entry");
        project.OpcUa(entry).Should().BeNull("the safety entry is no OPC UA entry");
    }

    [Test]
    public void ABlockOrRuntimeGroupRowLeadsToItsBlock() {
        var project = SafetyProject.Value;
        project.BuildIndex();
        var entry = Entry(project, "PLC_Safety");

        project.Resolve(entry, "LineSafety")!.Value.Item.Name.Should().Be("LineSafety");
        project.Resolve(entry, "FOB_RTG2")!.Value.Item.Kind.Should().Be("OB");
        project.Resolve(Entry(project, "PLC_Locked"), "LineSafety").Should().BeNull("a name means the block of the entry's own PLC");
    }

    [Test]
    public void CopyGivesTheTextTiadumpSafetyWritesForThatPlc() {
        var project = SafetyProject.Value;
        var expected = new StringWriter { NewLine = "\n" };
        TiaSafetyWriter.WriteMarkdown(TiaSafety.Read(project.Program, "PLC_Locked"), "Safety.zap21", expected);

        project.Document(Entry(project, "PLC_Locked")).Text.Should().Be(expected.ToString());
    }

    [Test]
    public void TheSafetyReportIsNotSearchedCountedOrExported() {
        var project = SafetyProject.Value;
        var entry = Entry(project, "PLC_Safety");

        project.Items.Should().NotContain(entry);
        project.Reports.Should().Contain(entry);
        project.Search("Collective F-signature").Should().BeEmpty();
        project.ReferencesTo(entry).Should().BeEmpty();
        project.ClosedBlocksAround(entry).Should().Be(0);
        project.Kinds.Should().EndWith(TiaItem.SafetyReportKind);
    }
}
