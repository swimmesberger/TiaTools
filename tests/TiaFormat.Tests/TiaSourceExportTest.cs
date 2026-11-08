using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>The files the command line and the viewer both write.</summary>
public sealed class TiaSourceExportTest {
    private const string CallOfOther = """<FctCa UId="1"><Sub UId="2" SI="FC" ODN="&quot;Other&quot;" /><BracO UId="3" /><BracC UId="4" /></FctCa><FiSt UId="5" />""";

    /// <summary>Where a block of a test file goes: the file has no environment, PLC or folder.</summary>
    private const string Here = "--0/NoPlc/NoFolder/";

    private static TiaProject TwoBlocks() {
        var file = TiaProjectTest.File();
        TiaProjectTest.Block(file, 1, "Other", "FC", """<LC TE=" nothing" />""");
        TiaProjectTest.Block(file, 2, "Caller", "FC", CallOfOther);
        return TiaProjectTest.Opened(file);
    }

    private static string[] Exported(TiaProject project, Func<TiaBlock, bool>? blocks, out string root, out TiaExportResult result) {
        var folder = Directory.CreateTempSubdirectory("tiaexport").FullName;
        root = folder;
        result = new TiaSourceExport(project.Program, folder) { Blocks = blocks }.Run();
        return [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/')).Order(StringComparer.Ordinal)];
    }

    [Test]
    public void ABlockIsWrittenAsTheViewerShowsIt() {
        var project = TwoBlocks();
        var caller = project.Items.Single(item => item.Name == "Caller");

        var files = Exported(project, null, out var root, out var result);
        try {
            files.Should().Equal(Here + "Caller.scl", Here + "Other.scl", "index.tsv");
            File.ReadAllText(Path.Combine(root, Here + "Caller.scl")).Should().Be(project.Document(caller).Text);
            result.BlockFiles.Should().Be(2);
            result.Failures.Should().BeEmpty();
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void AChoiceWritesOnlyWhatWasChosenAndTheIndexListsOnlyThat() {
        var project = TwoBlocks();

        var files = Exported(project, block => block.Name == "Caller", out var root, out var result);
        try {
            files.Should().Equal(Here + "Caller.scl", "index.tsv");
            var index = File.ReadAllLines(Path.Combine(root, "index.tsv"));
            index.Should().HaveCount(2);
            index[1].Should().EndWith("\t" + Here + "Caller.scl\tCaller");
            result.BlockFiles.Should().Be(1);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("Motor_1", "Motor_1")]
    [Arguments("a/b:c?", "a_b_c_")]
    [Arguments("ends with.", "ends with")]
    [Arguments("...", "_")]
    public void ANameBecomesOneThatEverySystemAllowsForAFile(string name, string safe) =>
        TiaSourceExport.Safe(name).Should().Be(safe);
}
