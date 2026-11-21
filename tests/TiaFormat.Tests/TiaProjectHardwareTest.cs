using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0010, FR-017 to FR-021: the project's devices and networks as the viewer's project holds them.</summary>
public sealed class TiaProjectHardwareTest {
    private static TiaProject Opened() {
        var project = TiaProject.Open(ReferenceProjects.Archive("Hardware"));
        project.BuildIndex();
        return project;
    }

    private static TiaItem Entry(TiaProject project) => project.Root.Children.Single(node => node.Item?.IsHardware == true).Item!;

    [Test]
    public void TheProjectNodeHoldsDevicesAndNetworksFirst() {
        var project = Opened();

        var first = project.Root.Children[0];

        first.Name.Should().Be("Devices & networks");
        first.Item!.IsHardware.Should().BeTrue();
        first.Item.IsReport.Should().BeTrue();
        project.Kinds.Should().NotContain(TiaItem.HardwareReportKind, "one entry needs no switch to filter the tree by");
    }

    [Test]
    public void TheEntryShowsTheHardware_ReadWhenFirstShown() {
        var project = Opened();
        var entry = Entry(project);

        var hardware = project.Hardware(entry)!;

        hardware.Devices.Select(device => device.Name).Should().Contain(["Controller", "Remote_PN_1", "Spare_PN"]);
        project.Hardware(entry).Should().BeSameAs(hardware);
        project.Hardware(project.Items[0]).Should().BeNull();
    }

    [Test]
    public void CopyGivesTheTextTiadumpDevicesWrites() {
        var project = Opened();
        var entry = Entry(project);
        using var output = new StringWriter { NewLine = "\n" };
        TiaFormat.Hardware.TiaHardwareWriter.WriteText(project.Hardware(entry)!, output);

        project.Document(entry).Text.Should().Be(output.ToString());
    }

    [Test]
    public void ASystemConstantOpensInItsPlcsTagTable() {
        var project = Opened();
        var constant = project.Hardware(Entry(project))!.AllItems.Single(item => item.Path == "Controller » DI_Main » DI_Main").Constants.Single();

        var location = project.Resolve(constant.Plc!, constant.Name)!.Value;

        location.Item.Kind.Should().Be("Tags");
        project.Document(location.Item).LineText(location.Line).Should().Contain("Local~DI_Main");
    }

    [Test]
    public void TheHardwareIsNeitherSearchedNorCountedNorExported() {
        var project = Opened();

        project.Items.Should().NotContain(item => item.IsHardware);
        project.Search("Remote_PN_1").Should().NotContain(result => result.Item.IsHardware);
        project.Search("192.168.0.10").Should().BeEmpty();
    }

    [Test]
    public void AnArchiveWithoutDevicesHasNoDevicesEntry() {
        var project = TiaProjectTest.Opened(TiaOpcUaFixture.NewFile());

        project.Root.Children.Should().NotContain(node => node.Item != null && node.Item.IsHardware);
        project.Kinds.Should().NotContain(TiaItem.HardwareReportKind);
    }
}
