using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0014: texts in every project language, program alarms and text lists, read from the reference project Texts,
/// whose languages are en-US, de-DE (editing) and fr-FR, and checked against TIA's own exports of them.
/// </summary>
public sealed class TiaTextsTest {
    private const string Project = "Texts";

    public static IEnumerable<string> ProjectsWithTexts() => ReferenceProjects.Names().Where(ReferenceTextChecks.HasTexts);

    private static TiaProgram Program(string? language = null) => new(TiaDatabase.Open(ReferenceProjects.Archive(Project)), language);

    private static TiaNetwork Network(TiaProgram program, string block, int number) =>
        program.Networks(program.Blocks.Single(candidate => candidate.Name == block)).Single(network => network.Number == number);

    [Test]
    public void TheProjectLanguagesAreNamedWithTheEditingAndTheReferenceLanguage() {
        var languages = Program().Languages;

        languages.All.Select(languages.Describe).Should().Equal("en-US (reference)", "de-DE (editing)", "fr-FR");
    }

    [Test]
    [MethodDataSource(nameof(ProjectsWithTexts))]
    public void TheLanguagesMatchTias(string project) {
        var languages = ReferenceTextChecks.Program(project).Languages;
        var (all, editing, reference) = ReferenceTextChecks.TiasLanguages(project);

        languages.All.Select(language => language.Name).Should().Equal(all);
        languages.Editing!.Name.Should().Be(editing);
        languages.Reference!.Name.Should().Be(reference);
    }

    [Test]
    public void TitlesAndCommentsAreInTheEditingLanguageByDefault() {
        var program = Program();

        program.Blocks.Single(block => block.Name == "Valve").Title.Should().Be("Ventil");
        Network(program, "Valve", 1).Comment.Should().Be("Öffnen setzt das Fahren.");
    }

    [Test]
    public void TitlesAndCommentsAreInTheLanguageTheUserNames() {
        var program = Program("fr-FR");

        program.Blocks.Single(block => block.Name == "Valve").Title.Should().Be("Vanne");
        Network(program, "Valve", 1).Comment.Should().Be("L'ouverture lance le déplacement.");
    }

    [Test]
    public void ATextEmptyInTheChosenLanguageIsShownInTheReferenceLanguage() {
        Network(Program("fr-FR"), "Valve", 2).Comment.Should().Be("Closing stops the travel.");
    }

    [Test]
    public void ATextEmptyInTheReferenceLanguageTooIsShownInTheFirstProjectLanguageWithText() {
        Network(Program(), "Valve", 3).Title.Should().Be("Retour d'information");
    }

    [Test]
    public void ALanguageThatIsNoProjectLanguageFailsAndNamesTheProjectLanguages() {
        var open = () => Program("it-IT");

        open.Should().Throw<TiaLanguageException>().WithMessage("*en-US (reference), de-DE (editing), fr-FR*");
    }

    [Test]
    public void TheDumpHoldsATextsTableWithAColumnPerProjectLanguage() {
        var dump = ReferenceProjects.Dump(Project);
        try {
            var lines = File.ReadAllLines(Path.Combine(dump, "Project", "PLC_Texts", "texts.tsv"));

            lines[0].Should().Be("object\tplace\ten-US\tde-DE\tfr-FR");
            lines.Should().Contain("FB Valve\tnetwork 1 title\tOpen\tÖffnen\tOuvrir");
            lines.Should().Contain("tag table Signals\ttag levelSensor\tLevel sensor\tFüllstandsensor\tCapteur de niveau");
            lines.Should().Contain("text list States\tentry 2..9\tFault\tStörung\tDéfaut");
            lines.Should().Contain("alarm TankA.highAlarm\tAlarm text\tLevel high: @1%d@ mm\tFüllstand hoch: @1%d@ mm\tNiveau haut : @1%d@ mm");
            lines.Should().Contain("alarm class Acknowledgement\tdisplay name\tA\tA\tA");
        } finally {
            Directory.Delete(dump, recursive: true);
        }
    }

    [Test]
    public void ATextEmptyInSomeLanguageStaysEmptyInTheTextsTable() {
        var program = Program();
        var rows = TiaTexts.Rows(program, new TiaAlarms(program));
        using var output = new StringWriter { NewLine = "\n" };

        TiaTexts.WriteTexts(rows, "PLC_Texts", program.Languages, output);

        output.ToString().Split('\n').Should().Contain("FB Valve\tnetwork 2 comment\tClosing stops the travel.\t\t");
    }

    [Test]
    [MethodDataSource(nameof(ProjectsWithTexts))]
    public void EveryTitleAndCommentInEveryLanguageMatchesTiasExport(string project) {
        ReferenceTextChecks.BlockAndTagProblems(project, out var compared).Should().BeEmpty();

        compared.Should().BePositive();
    }

    [Test]
    [MethodDataSource(nameof(ProjectsWithTexts))]
    public void EveryTextListEntryInEveryLanguageMatchesTiasExport(string project) {
        ReferenceTextChecks.TextListProblems(project, out var compared).Should().BeEmpty();

        compared.Should().BePositive();
    }

    [Test]
    [MethodDataSource(nameof(ProjectsWithTexts))]
    public void EveryAlarmTextInEveryLanguageMatchesTiasExport(string project) {
        ReferenceTextChecks.AlarmProblems(project, out var compared).Should().BeEmpty();

        compared.Should().BePositive();
    }

    [Test]
    [MethodDataSource(nameof(ProjectsWithTexts))]
    public void EveryAlarmClassMatchesTiasExport(string project) {
        ReferenceTextChecks.AlarmClassProblems(project, out var compared).Should().BeEmpty();

        compared.Should().BePositive();
    }

    [Test]
    public void AnAlarmIsListedWithItsBlockInstanceClassAndTexts() {
        var alarm = new TiaAlarms(Program()).Alarms().Single(candidate => candidate.Instance == "TankA" && candidate.Name == "highAlarm");

        alarm.Block.Should().Be("Tank");
        alarm.AlarmId.Should().NotBeNull();
        alarm.Class.Should().Be("Acknowledgement");
        alarm.Texts.Single(text => text.Kind == "Info text").Texts.Should().Contain(new TiaText(1031, "Zulauf prüfen"));
    }

    [Test]
    public void AnAlarmNamesTheOperandItsBlockWiresToEachAssociatedValue() {
        var alarm = new TiaAlarms(Program()).Alarms().Single(candidate => candidate.Instance is null && candidate.Name == "lowAlarm");

        alarm.AssociatedValues.Should().Equal(new TiaAssociatedValue(1, "#level"), new TiaAssociatedValue(2, "#state"));
    }

    [Test]
    public void AnAlarmShowsItsTextAsStoredAndNamesTheTextListItUses() {
        var alarm = new TiaAlarms(Program()).Alarms().Single(candidate => candidate.Instance == "TankA" && candidate.Name == "lowAlarm");

        alarm.Texts.Single(text => text.Kind == "Alarm text").Texts.Should().Contain(new TiaText(1033, "Level low, state @2%t#States@"));
        alarm.TextLists.Should().Equal("States");
    }

    [Test]
    public void AnInstanceWithoutTextsOfItsOwnTakesItsTypes() {
        var alarm = new TiaAlarms(Program()).Alarms().Single(candidate => candidate.Instance == "TankB" && candidate.Name == "highAlarm");

        alarm.Texts.Should().OnlyContain(text => text.Inherited);
        alarm.Class.Should().Be("No Acknowledgement");
    }

    [Test]
    public void AnAlarmClassIsListedWithItsDisplayNameAndAcknowledgement() {
        var classes = new TiaAlarms(Program()).Classes();

        classes.Select(alarmClass => (alarmClass.Name, TiaTexts.In(alarmClass.DisplayName, Program().Languages.Editing), alarmClass.Acknowledgement))
            .Should().Equal(("Acknowledgement", "A", true), ("No Acknowledgement", "NA", false));
    }

    [Test]
    public void ATextListNamesItsRangeEntriesAndTheAlarmsThatUseIt() {
        var lists = new TiaAlarms(Program()).TextLists();

        var states = lists.Single(list => list.Name == "States");
        states.Range.Should().Be("Decimal");
        states.Entries.Select(entry => (entry.From, entry.To)).Should().Equal((0, 0), (1, 1), (2, 9));
        states.UsedBy.Should().Equal("TankA.lowAlarm");
        lists.Single(list => list.Name == "Doors").UsedBy.Should().BeEmpty();
    }

    [Test]
    public void AnAlarmOfAProtectedBlockIsListedClosed() {
        var alarms = new TiaAlarms(Program()).Alarms().Where(alarm => alarm.Block == "Sealed").ToList();

        alarms.Should().HaveCount(2).And.OnlyContain(alarm => alarm.IsClosed && alarm.Texts.Count == 0 && alarm.Class == null);
    }

    [Test]
    public void APlcHoldsItsAlarmsAndTextListsInTheTree() {
        var project = TiaProject.Open(ReferenceProjects.Archive(Project));

        var plc = project.Root.Children.Single(node => node.Name == "PLC_Texts");
        plc.Children.Where(node => node.Item is { IsTable: true }).Select(node => node.Name).Should().Equal(TiaItem.AlarmsKind, TiaItem.TextListsKind);
    }

    [Test]
    public void TheAlarmsAndTextListsAreShownInTheChosenLanguage() {
        var project = TiaProject.Open(ReferenceProjects.Archive(Project), "fr-FR");

        var alarms = project.Document(project.Items.Single(item => item.Kind == TiaItem.AlarmsKind)).Text;
        alarms.Should().Contain("Niveau haut : @1%d@ mm").And.Contain("Vérifier l'arrivée").And.NotContain("Level high");
        var lists = project.Document(project.Items.Single(item => item.Kind == TiaItem.TextListsKind)).Text;
        lists.Should().Contain("Défaut").And.NotContain("Störung");
    }

    [Test]
    public void SearchFindsAnAlarmText() {
        var project = TiaProject.Open(ReferenceProjects.Archive(Project));

        project.Search("Zulauf prüfen").Should().ContainSingle().Which.Item.Kind.Should().Be(TiaItem.AlarmsKind);
    }

    [Test]
    public void NoTextOfAProtectedBlocksAlarmAppearsInTheDumpTheTablesTheSearchOrTheViewer() {
        string[] secrets = ["SealedAlarmTextMustStayClosed", "SealedInfoMustStayClosed", "SealedBodyMustStayClosed"];
        var dump = ReferenceProjects.Dump(Project);
        try {
            var written = Directory.GetFiles(dump, "*", SearchOption.AllDirectories).Select(File.ReadAllText);
            written.Should().NotContain(text => secrets.Any(text.Contains));
        } finally {
            Directory.Delete(dump, recursive: true);
        }
        var project = TiaProject.Open(ReferenceProjects.Archive(Project));
        project.BuildIndex();
        foreach (var secret in secrets) {
            project.Search(secret).Should().BeEmpty();
        }
        project.Items.Select(item => project.Document(item).Text).Should().NotContain(text => secrets.Any(text.Contains));
    }
}
