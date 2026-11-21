using AwesomeAssertions;
using TiaFormat.Hardware;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0010: the reader's hardware checked against what TIA Portal reported through Openness for the reference projects
/// it built. A project without TIA's report of its hardware was built before spec 0010 and is skipped.
/// </summary>
public sealed class TiaHardwareReferenceTest {
    public static IEnumerable<string> Projects() => ReferenceProjects.Names().Where(name => File.Exists(HardwareReports.Report(name)));

    private static List<string> Problems(string project, params string[] kinds) =>
        HardwareReports.Problems(
            project,
            HardwareReports.Ours(HardwareReports.Read(project)).Where(row => kinds.Contains(row.Kind) && Wanted(row)),
            HardwareReports.Theirs(HardwareReports.Report(project)).Where(row => kinds.Contains(row.Kind) && Wanted(row)));

    /// <summary>
    /// Addresses and hardware identifiers have tests of their own. A label is compared on interfaces only: TIA labels a
    /// port from its catalog (<c>P1R</c> for a ring port), where the archive stores <c>X1 P1</c>.
    /// </summary>
    private static bool Wanted(HardwareRow row) => row.Attribute is not ("address" or "hwid" or "label");

    private static string Device(string path) => path.Split(TiaHardware.PathSeparator)[0];

    [Test]
    public void TheHardwareReferenceProjectHasTiasReport() {
        Projects().Should().Contain("Hardware");
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryDeviceAndDeviceItemMatchesTiasReport(string project) {
        string.Join("\n", Problems(project, "device", "item")).Should().BeEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryIoAddressMatchesTiasReport(string project) {
        var ours = HardwareReports.Ours(HardwareReports.Read(project)).Where(row => row.Attribute == "address");
        var theirs = HardwareReports.Theirs(HardwareReports.Report(project)).Where(row => row.Attribute == "address");

        string.Join("\n", HardwareReports.Problems(project, ours, theirs)).Should().BeEmpty();
    }

    /// <summary>
    /// The archive holds a hardware identifier only as the value of a system constant. TIA reports identifiers no constant
    /// holds, and holds a constant, <c>Local~Configuration</c>, whose identifier it puts on no object. So the identifiers
    /// both sides know in a device are compared, and each must sit where TIA puts it.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryHardwareIdentifierMatchesTiasReport(string project) {
        // Identifiers count per PLC, so the same number can stand for something else in another device.
        static (string, string) Key(HardwareRow row) => (Device(row.Path), row.Value);
        var allOurs = HardwareReports.Ours(HardwareReports.Read(project)).Where(row => row.Attribute == "hwid").ToList();
        var allTheirs = HardwareReports.Theirs(HardwareReports.Report(project)).Where(row => row.Attribute == "hwid").ToList();
        var both = allOurs.Select(Key).Intersect(allTheirs.Select(Key)).ToHashSet();

        both.Should().HaveCountGreaterThan(allOurs.Count / 2, "most identifiers the archive holds are ones TIA reports");
        string.Join("\n", HardwareReports.Problems(project, allOurs.Where(row => both.Contains(Key(row))), allTheirs.Where(row => both.Contains(Key(row))))).Should().BeEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryInterfaceLabelTheArchiveHoldsMatchesTiasReport(string project) {
        var hardware = HardwareReports.Read(project);
        var interfaces = hardware.AllItems.Where(item => item.InterfaceAddresses.Count != 0 && item.Label is not null).Select(item => item.Path).ToHashSet();
        var ours = HardwareReports.Ours(hardware).Where(row => row.Attribute == "label" && interfaces.Contains(row.Path));
        var theirs = HardwareReports.Theirs(HardwareReports.Report(project)).Where(row => row.Attribute == "label" && interfaces.Contains(row.Path));

        interfaces.Should().NotBeEmpty();
        string.Join("\n", HardwareReports.Problems(project, ours, theirs)).Should().BeEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EveryInterfaceAddressMatchesTiasReport(string project) {
        string.Join("\n", Problems(project, "interface")).Should().BeEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Projects))]
    public void EverySubnetAndIoSystemMatchesTiasReport(string project) {
        string.Join("\n", Problems(project, "subnet", "iosystem")).Should().BeEmpty();
    }

    [Test]
    public void EveryIoSystemListsTheIoDevicesTiaAssigned() {
        var systems = HardwareReports.Read("Hardware").IoSystems.ToDictionary(system => system.Name);

        systems["Line IO system"].IoDevices.Should().Equal("Remote_PN_1 » Remote_PN_1 » PROFINET interface", "Remote_PN_2 » Remote_PN_2 » PROFINET interface");
        systems["Line DP system"].IoDevices.Should().Equal("Remote_DP » Remote_DP");
        systems["Line DP system"].Kind.Should().Be(TiaSubnetKind.Profibus);
    }

    [Test]
    public void AnIoDeviceIsListedOnlyUnderItsOwnIoSystem() {
        var hardware = HardwareReports.Read("Hardware");

        hardware.IoSystems.Should().ContainSingle(system => system.IoDevices.Contains("Partner_IO » Partner_IO » PROFINET interface"))
            .Which.Controller.Should().Be("Partner » Partner » PROFINET interface_1");
    }

    [Test]
    public void ADeviceOnASubnetWithoutIoSystemHasNone() {
        var hardware = HardwareReports.Read("Hardware");
        const string spare = "Spare_PN » Spare_PN » PROFINET interface";

        hardware.Subnets.Single(subnet => subnet.Name == "PN/IE_1").Interfaces.Should().Contain(spare);
        hardware.IoSystems.Should().NotContain(system => system.IoDevices.Contains(spare));
        hardware.Devices.Single(device => device.Name == "Spare_PN").Group.Should().Be("Line/Cell");
    }

    [Test]
    public void AnIpAddressSetAtTheDeviceIsListedWithoutAnAddress() {
        var node = HardwareReports.Read("Hardware").AllItems.Single(item => item.Path == "Controller » Controller » PROFINET interface_2").InterfaceAddresses.Single();

        node.IpAddressSetElsewhere.Should().BeTrue();
        node.IpAddress.Should().BeNull();
    }

    [Test]
    public void AModuleShowsItsIoAddressAsTiaWritesIt() {
        var module = HardwareReports.Read("Hardware").AllItems.Single(item => item.Path == "Controller » DI_Main » DI_Main");

        module.IoAddresses.Should().ContainSingle().Which.ToString().Should().Be("I 10..13");
        module.Constants.Should().ContainSingle().Which.Name.Should().Be("Local~DI_Main");
    }

    [Test]
    public void ADifferenceNamesTheProjectThePathTheAttributeAndBothValues() {
        var problems = HardwareReports.Problems("Plant", [new("item", "A » B", "order", "1")], [new("item", "A » B", "order", "2"), new("item", "A » C", "listed", "")]);

        problems.Should().Equal("Plant: item A » B, order: ours `1`, TIA's `2`", "Plant: item A » C, listed: ours `none`, TIA's ``");
    }
}
