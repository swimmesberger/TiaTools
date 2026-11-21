using System.IO.Compression;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Spec 0015, the third to fifth user stories: who uses an object, what a closed block uses, and an archive without TIA's list.</summary>
public sealed class TiaCrossReferenceTest {
    private static readonly Lazy<(TiaProgram Program, TiaCrossReferences List)> Tables = new(() => {
        var program = new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("Tables")));
        return (program, TiaCrossReferences.Read(program)!);
    });

    private static readonly Lazy<TiaProject> TablesProject = new(() => {
        var project = TiaProject.Open(ReferenceProjects.Archive("Tables"));
        project.BuildIndex();
        return project;
    });

    private static readonly Lazy<TiaProject> FillingLineProject = new(() => {
        var project = TiaProject.Open(ReferenceProjects.Archive("FillingLine"));
        project.BuildIndex();
        return project;
    });

    /// <summary>The lines of a project's text where one item uses another with this access, trimmed.</summary>
    private static IEnumerable<string> PlacesOf(TiaProject project, string user, string used, string access) =>
        project.ReferencesTo(project.Items.Single(item => item.Name == used && item.Block is not null))
            .Where(reference => reference.From.Name == user && reference.Access == access)
            .Select(reference => project.Document(reference.From).LineText(reference.Line).Trim());

    private static IEnumerable<string> UsesBy(string user) =>
        Tables.Value.List.References.Where(reference => reference.User.Name == user)
            .Select(reference => $"{reference.Used.DisplayName(reference.User.Frame.Key)} {reference.Access} {reference.Network}");

    /// <summary>A project folder of a reference archive's database, with another archive's cross-reference list or none.</summary>
    private static string Folder(string project, string? crossReferencesOf) {
        var folder = Directory.CreateTempSubdirectory("tiaxref").FullName;
        Extract(project, "System/PEData.plf", folder);
        if (crossReferencesOf is not null) {
            Extract(crossReferencesOf, "XRef/XRef.db", folder);
        }
        return folder;

        static void Extract(string project, string entry, string folder) {
            using var zip = ZipFile.OpenRead(ReferenceProjects.Archive(project));
            var target = Path.Combine(folder, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            zip.Entries.Single(candidate => candidate.FullName.Replace('\\', '/').EndsWith(entry, StringComparison.Ordinal)).ExtractToFile(target);
        }
    }

    [Test]
    public void EachUseIsListedWithItsUserAccessAndNetwork() {
        UsesBy("LadBoxes").Should().Contain(["Lamp Write 5", "Setpoint Read 7", "level Read 1", "LampDelay InstanceDB 5"]);
        UsesBy("UseRecipe").Should().Contain(["Speed Write 1", "\"Recipe\".count Write 1", "\"Recipe\".steps[1].target Read 1"]);
    }

    [Test]
    public void ACallInEveryLanguageIsListedAsACallOfTheBlockItCalls() {
        UsesBy("UseRecipe").Should().Contain("Limit Call 1", "SCL");
        UsesBy("LadBoxes").Should().Contain("Limit Call 7", "LAD");
        UsesBy("FbdBoxes").Should().Contain("Limit Call 7", "FBD");
        UsesBy("StlBasics").Should().Contain("Limit Call 4", "STL");
        UsesBy("Batch").Should().Contain(use => use.StartsWith("Tick Call ", StringComparison.Ordinal), "GRAPH");
    }

    [Test]
    public void ACallThroughADataBlockOfItsOwnOrAMultiInstanceIsACall() {
        UsesBy("LadBoxes").Should().Contain(["TON Call 5", "Pulse Call 8"]);
        UsesBy("Main").Should().Contain("LadBoxes Call 1");
    }

    [Test]
    public void ACallThroughAnInstanceIsPlacedOnTheLineThatCallsTheInstance() {
        var line = FillingLineProject.Value;

        // Regression: these calls name the instance, not the block they call, and were placed on BEGIN.
        PlacesOf(line, "Conveyor", "Motor", "Call").Should().ContainSingle().Which.Should().StartWith("#drive(");
        PlacesOf(line, "FillingLine", "Conveyor", "Call").Select(text => text[..text.IndexOf('(', StringComparison.Ordinal)])
            .Should().BeEquivalentTo("#infeed", "#outfeed");
        PlacesOf(line, "Main", "FillingLine", "Call").Should().ContainSingle().Which.Should().StartWith("\"LineData\"(");
        PlacesOf(TablesProject.Value, "LadBoxes", "Pulse", "Call").Should().ContainSingle().Which.Should().StartWith("#pulser(", "LAD");
    }

    [Test]
    public void AUseInTheInterfaceIsPlacedOnTheLineThatDeclaresTheMember() {
        var line = FillingLineProject.Value;

        PlacesOf(line, "Conveyor", "Motor", "Multiinstance").Should().ContainSingle().Which.Should().StartWith("drive : \"Motor\";");
        PlacesOf(line, "Filler", "typeRecipe", "Declaration").Should().ContainSingle().Which.Should().StartWith("recipe : \"typeRecipe\";");
    }

    [Test]
    public void AWatchTableIsAmongTheUsesOfATagItNames() {
        var project = TablesProject.Value;
        var io = project.Items.Single(item => item.Kind == "Tags" && item.Name == "IO");

        var uses = project.ReferencesTo(io).Where(reference => reference.Target == "Setpoint").Select(reference => $"{reference.From.Name} {reference.Access}");

        uses.Should().Contain(["Tank Monitor", "Tank Modify", "LadBoxes Read", "UseRecipe Write"]);
    }

    [Test]
    public void TheUsesOfATagTableNameEachTag() {
        var project = TablesProject.Value;
        var io = project.Items.Single(item => item.Kind == "Tags" && item.Name == "IO");

        var lamp = project.ReferencesTo(io).Where(reference => reference.Target == "Lamp" && reference.From.Name == "LadBoxes").ToList();

        lamp.Should().ContainSingle().Which.Access.Should().Be("Write");
        project.Document(lamp[0].From).LineText(lamp[0].Line).Should().Contain("\"Lamp\"");
    }

    [Test]
    public void WhatAClosedBlockUsesIsListedWithoutAPlace() {
        var sealedUses = Tables.Value.List.References.Where(reference => reference.User.Name == "Sealed").ToList();

        sealedUses.Select(reference => $"{reference.Used.Name} {reference.Access}").Should().BeEquivalentTo("Limit Call", "Level Read", "Alarm Write");
        sealedUses.Should().OnlyContain(reference => reference.Network == null && reference.UId == null && reference.Declaration == null);
    }

    [Test]
    public void AClosedBlockListsNoUseOfItsOwnMembers() {
        Tables.Value.List.References.Should().NotContain(reference => reference.User.Name == "Sealed" && reference.Used.Key == reference.User.Frame.Key);
        TablesProject.Value.Document(TablesProject.Value.Items.Single(item => item.Name == "Sealed")).Text.Should().NotContain("SEALED-MARKER");
    }

    [Test]
    public void AnOpenBlocksCallOfAClosedOneIsListed() {
        UsesBy("Main").Should().Contain("Sealed Call 1");
    }

    [Test]
    public void TiasListIsUsedWhenItBelongsToTheDatabase() {
        Tables.Value.List.IsCurrent.Should().BeTrue();
        TablesProject.Value.ReferencesFromText.Should().BeFalse();
    }

    [Test]
    public void WithoutTiasListTheReferencesComeFromTheTextAndSaySo() {
        var folder = Folder("Languages", crossReferencesOf: null);
        try {
            var project = TiaProject.Open(folder);
            var output = new StringWriter();

            TiaCrossReferenceWriter.Write(project, output);

            project.ReferencesFromText.Should().BeTrue();
            output.ToString().Should().StartWith("# from the text");
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void AListThatDoesNotBelongToTheDatabaseIsNotUsed() {
        var folder = Folder("Tables", crossReferencesOf: "Languages");
        try {
            var program = new TiaProgram(TiaDatabase.Open(folder));
            var project = TiaProject.Open(folder);
            project.BuildIndex();

            TiaCrossReferences.Read(program)!.IsCurrent.Should().BeFalse();
            project.ReferencesFromText.Should().BeTrue();
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void FromTheTextACallInLadFbdStlAndGraphIsACallAndAGlobalAtTheStartOfALineIsFound() {
        var folder = Folder("Languages", crossReferencesOf: null);
        try {
            var project = TiaProject.Open(folder);
            project.BuildIndex();
            string Of(string name) => string.Join(",", project.ReferencesFrom(project.Items.Single(item => item.Name == name))
                .Select(reference => $"{reference.To.Name} {reference.Kind}").Distinct());

            Of("LadBoxes").Should().Contain("Limit Call").And.Contain("Pulse Call").And.Contain("LampDelay Call");
            Of("FbdBoxes").Should().Contain("Limit Call");
            Of("StlBasics").Should().Contain("Limit Call");
            Of("Batch").Should().Contain("Tick Call");
            project.ReferencesFrom(project.Items.Single(item => item.Name == "LadBoxes")).Should().Contain(reference => reference.To.Name == "IO" && reference.Text.StartsWith("\"Valve\" :=", StringComparison.Ordinal));
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }
}
