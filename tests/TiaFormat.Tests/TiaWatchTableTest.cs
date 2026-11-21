using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Spec 0015, the first two user stories: watch tables and the force table, on the Tables reference project.</summary>
public sealed class TiaWatchTableTest {
    private static string[] DumpedFile(string project, string relative) {
        var dump = ReferenceProjects.Dump(project);
        try {
            var file = Path.Combine(dump, relative);
            return File.Exists(file) ? File.ReadAllLines(file) : [];
        } finally {
            Directory.Delete(dump, recursive: true);
        }
    }

    [Test]
    public void EachWatchTableEntryAppearsInTiasOrderWithItsColumns() {
        var lines = DumpedFile("Tables", "Project/PLC_Tables/WatchtablesFolder/Commissioning/Tank.watch");

        lines.Should().StartWith([
            "// watch table Tank",
            "\"Start\" AT %I0.0 : Bool;",
            "\"Setpoint\" AT %MW10 : DEC_signed := 50;   // Half full",
            "\"Speed\" AT %MD20 : Float := 1.5;",
            "\"StatusWord\" AT %MW30 : Hex { MonitorTrigger := 'OnceOnlyAtStart' };",
        ]);
        lines.Should().Contain("\"Recipe\".limits.high : DEC_signed { ModifyTrigger := 'OnceOnlyAtStart' } := 85;");
        lines.Should().Contain("%MW40 : Bin;   // No tag at this address");
    }

    [Test]
    public void ALineThatHoldsOnlyACommentAppearsInItsPlace() {
        var lines = DumpedFile("Tables", "Project/PLC_Tables/WatchtablesFolder/Commissioning/Tank.watch");

        var at = Array.IndexOf(lines, "// The recipe");
        at.Should().BePositive();
        lines[at - 1].Should().StartWith("\"StatusWord\"");
        lines[at + 1].Should().StartWith("\"Recipe\".count");
    }

    [Test]
    public void EachForceTableEntryAppearsWithItsForceValueAndComment() {
        var lines = DumpedFile("Tables", "Project/PLC_Tables/WatchtablesFolder/Force table.force");

        lines.Should().Equal(
            "// force table Force table",
            "\"Valve\":P AT %Q0.2:P : Bool := TRUE;   // Keep the inlet open",
            "\"DoorClosed\":P AT %I0.2:P : Bool := FALSE;");
    }

    [Test]
    public void AnEmptyForceTableGetsNoFile() {
        var program = new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("Languages")));
        TiaWatchTables.Read(program).Should().ContainSingle(table => table.Kind == TiaWatchTableKind.Force && table.Entries.Count == 0);

        var lines = DumpedFile("Languages", "Project/PLC_Languages/WatchtablesFolder/Force table.force");

        lines.Should().BeEmpty();
    }

    [Test]
    public void TheWatchTablesAppearInTheTreeUnderTheirPlcInTheirFolders() {
        var project = TiaProject.Open(ReferenceProjects.Archive("Tables"));

        var plc = project.Root.Children.Single(node => node.Name == "PLC_Tables");
        var tables = plc.Children.Single(node => node.Name == "Watch and force tables");
        var tank = tables.Children.Single(node => node.Name == "Commissioning").Children.Single();

        tank.Item!.Kind.Should().Be("Watch");
        tables.Children.Select(node => (node.Name, node.Item?.Kind)).Should().Contain([("Outputs", "Watch"), ("Force table", "Force")]);
        project.Document(tank.Item).Text.Should().StartWith("// watch table Tank\n\"Start\" AT %I0.0 : Bool;\n");
    }
}
