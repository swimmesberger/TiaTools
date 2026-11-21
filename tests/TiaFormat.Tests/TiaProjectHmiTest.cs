using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0011: the HMI devices as the viewer's project holds them, on the reference project <c>Hmi</c>.</summary>
public sealed class TiaProjectHmiTest {
    private static readonly Lazy<TiaProject> Project = new(() => {
        var project = TiaProject.Open(ReferenceProjects.Archive("Hmi"));
        project.BuildIndex();
        return project;
    });

    private static TiaTreeNode Device(string name) => Project.Value.Root.Children.Single(node => node.Name == name && node.Item is null && node.Children.Any(child => child.Item?.IsHmi == true));

    private static TiaItem Entry(TiaTreeNode node, params string[] path) {
        foreach (var name in path) {
            node = node.Children.Single(child => child.Name == name);
        }
        return node.Item!;
    }

    [Test]
    public void TheTreeHoldsEachHmiDeviceWithItsConnectionsTagTablesAlarmsAndScreens() {
        var panel = Device("HMI_Panel");

        panel.Children.Select(child => child.Name).Should().Contain(["Connections", "Alarms", "HMI tags", "Screens"]);
        Entry(panel, "HMI tags", "Tank").HmiPart.Should().Be(TiaHmiPart.TagTable);
        Entry(panel, "Screens", "Overview").HmiPart.Should().Be(TiaHmiPart.Screen);
        Device("HMI_RT_1").Children.Should().Contain(child => child.Name == "HMI tags");
    }

    [Test]
    public void AChosenTagTableAlarmListOrScreenShowsWhatTheReportListsForIt() {
        var panel = Device("HMI_Panel");

        Project.Value.Document(Entry(panel, "HMI tags", "Recipe")).Text.Should().Contain("RecipeSpeed").And.Contain("\"Mixer\".Recipe.Speed");
        Project.Value.Document(Entry(panel, "Alarms")).Text.Should().Contain("ValveFault").And.Contain("bit 3").And.Contain("Inlet valve fault");
        Project.Value.Document(Entry(panel, "Screens", "Overview")).Text.Should().Contain("LevelValue").And.Contain("TankLevel");
        Project.Value.Document(Entry(panel, "Connections")).Text.Should().Contain("PLC_Mixer");
    }

    [Test]
    public void OpeningAnHmiTagsPlcLinkShowsThePlcTagInItsTable_OrTheDataBlock() {
        var project = Project.Value;
        var table = Entry(Device("HMI_Panel"), "HMI tags", "Tank");
        var recipe = Entry(Device("HMI_Panel"), "HMI tags", "Recipe");

        var tag = Follow(project, table, "\"Tank_Level\"");
        var block = Follow(project, recipe, "\"Mixer\"");

        tag.Item.Table!.Name.Should().Be("Tank");
        project.Document(tag.Item).LineText(tag.Line).Should().Contain("Tank_Level");
        block.Item.Name.Should().Be("Mixer");
        block.Item.Kind.Should().StartWith("DB");
    }

    [Test]
    public void HmiEntriesAreNotSearchedCountedOrExported() {
        Project.Value.Items.Should().NotContain(item => item.IsHmi);
        Project.Value.Search("RecipeSpeed").Should().BeEmpty();
    }

    /// <summary>Where the first link in an entry's text that reads <paramref name="text"/> leads.</summary>
    private static TiaLocation Follow(TiaProject project, TiaItem item, string text) {
        var document = project.Document(item);
        var token = document.Tokens.First(candidate => candidate.Target is not null && document.Text.Substring(candidate.Start, candidate.Length) == text);
        return project.Resolve(item, token.Target!)!.Value;
    }
}
