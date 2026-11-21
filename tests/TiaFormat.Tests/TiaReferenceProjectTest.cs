using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0008: the reader checked against what TIA Portal itself built and generated, in the reference projects. These are
/// the one place a test reads an archive TIA wrote; their content is invented, like the demo project's.
/// </summary>
public sealed class TiaReferenceProjectTest {
    public static IEnumerable<string> Projects() => ReferenceProjects.Names();

    private static TiaComparedBlock Block(string name, TiaComparisonOutcome outcome, TiaSourceDifference difference = default) =>
        new("PLC_A", "FB", name, outcome, difference);

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryBlockMatchesTiasGeneratedSource(string project) {
        File.Exists(ReferenceProjects.Archive(project)).Should().BeTrue("a reference project holds the archive TIA built from its sources");

        var blocks = ReferenceProjects.Compare(project);

        blocks.Should().Contain(block => block.Outcome == TiaComparisonOutcome.Identical);
        ReferenceProjects.Problems(project, blocks, ReferenceProjects.Known(project), ReferenceProjects.ExportedAsXml(project)).Should().BeEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryNetworkMatchesTiasExport(string project) {
        ReferenceProjects.NetworkProblems(project, out var compared).Should().BeEmpty();

        if (ReferenceProjects.ExportedAsXml(project).Count != 0) {
            compared.Should().BePositive("a project with blocks TIA exported as SimaticML has networks to compare");
        }
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryTagMatchesTiasExport(string project) {
        ReferenceProjects.TagProblems(project).Should().BeEmpty();
    }

    [Test]
    public void ADifferenceNamesTheProjectTheBlockAndTheFirstDifferingLineInBothVersions() {
        var blocks = new[] { Block("Pump", TiaComparisonOutcome.Different, new TiaSourceDifference(TiaSourceMatch.Different, 12, "ours", "theirs")) };

        var problem = ReferenceProjects.Problems("Plant", blocks, []).Should().ContainSingle().Subject;

        problem.Should().Be("Plant: PLC_A FB Pump, line 12: ours `ours`, TIA's `theirs`");
    }

    [Test]
    public void AKnownDifferenceThatStillDiffersPasses() {
        var blocks = new[] { Block("Pump", TiaComparisonOutcome.Different, new TiaSourceDifference(TiaSourceMatch.Different, 3, "a", "b")) };

        ReferenceProjects.Problems("Plant", blocks, [new KnownBlock("PLC_A", "FB", "Pump", "a reason")]).Should().BeEmpty();
    }

    [Test]
    public void AKnownDifferenceThatMatchesFails() {
        var blocks = new[] { Block("Pump", TiaComparisonOutcome.Identical) };

        ReferenceProjects.Problems("Plant", blocks, [new KnownBlock("PLC_A", "FB", "Pump", "a reason")]).Should().ContainSingle().Which.Should().Contain("take it off known.tsv");
    }

    [Test]
    public void ABlockWithoutGeneratedSourceFails() {
        var blocks = new[] { Block("Pump", TiaComparisonOutcome.NoGeneratedSource) };

        ReferenceProjects.Problems("Plant", blocks, []).Should().ContainSingle().Which.Should().Contain("Pump");
    }

    [Test]
    public void GeneratedSourceWithoutABlockInTheArchiveFails() {
        var blocks = new[] { Block("Pump", TiaComparisonOutcome.NotInDump), Block("Valve", TiaComparisonOutcome.NotInExport) };

        ReferenceProjects.Problems("Plant", blocks, []).Should().HaveCount(2);
    }

    [Test]
    public void AClosedBlockShowsNothingOfItsBody() {
        var body = BodyLines(Path.Combine(ReferenceProjects.Folder("Closed"), "sources", "PLC_Closed", "Program blocks", "Vault", "Secret.scl"));
        var project = TiaProject.Open(ReferenceProjects.Archive("Closed"));
        project.BuildIndex();
        var secret = project.Items.Single(item => item.Name == "Secret" && item.IsProject);
        var dump = ReferenceProjects.Dump("Closed");
        try {
            var shown = string.Join("\n", project.Items.Select(item => project.Document(item).Text));
            var dumped = string.Join("\n", Directory.GetFiles(dump, "*", SearchOption.AllDirectories).Select(File.ReadAllText));

            secret.IsClosed.Should().BeTrue();
            body.Should().NotBeEmpty();
            foreach (var line in body) {
                shown.Should().NotContain(line);
                dumped.Should().NotContain(line);
                project.Search(line).Should().BeEmpty();
            }
        } finally {
            Directory.Delete(dump, recursive: true);
        }
    }

    [Test]
    public void ANetworkOfAnotherLanguageInALadderBlockIsShownInItsOwnLanguage() {
        var project = TiaProject.Open(ReferenceProjects.Archive("Languages"));
        project.BuildIndex();
        var text = project.Document(project.Items.Single(item => item.Name == "Mixed" && item.IsProject)).Text;

        text.Should().Contain("// ---- network 1 (LAD): Ladder\n// A ladder network first.\n#lamp := #start;");
        text.Should().Contain("// ---- network 2 (SCL): Structured text\n// An SCL network inside a ladder block.\n\t#motor := #start AND NOT #stop;");
        text.Should().Contain("// ---- network 3 (STL): Statement list\n// An STL network inside a ladder block.\n      A #door;\n      AN #stop;\n      = #fault;");
    }

    [Test]
    public void SearchFindsAnOperandUsedOnlyInLadderAndFunctionBlockNetworks() {
        var project = TiaProject.Open(ReferenceProjects.Archive("Languages"));
        project.BuildIndex();

        // The interface declares edgeMemory; only a network's statements name it with its #.
        var found = project.Search("#edgeMemory").Select(result => result.Item.Name).Distinct().Order(StringComparer.Ordinal);

        found.Should().Equal("FbdLogic", "LadLogic");
    }

    /// <summary>The statements and comments between BEGIN and the block's end, as the sources wrote them.</summary>
    private static List<string> BodyLines(string source) =>
        [.. File.ReadAllLines(source).SkipWhile(line => line != "BEGIN").Skip(1).TakeWhile(line => !line.StartsWith("END_", StringComparison.Ordinal))
            .Select(line => line.Trim()).Where(line => line.Length >= 8)];
}
