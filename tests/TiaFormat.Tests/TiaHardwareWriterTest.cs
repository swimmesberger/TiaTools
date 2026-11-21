using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Hardware;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Spec 0010: what <c>tiadump devices</c> writes, as text and as JSON, from the hardware reference project.</summary>
public sealed class TiaHardwareWriterTest {
    private static string Text(TiaHardwareReport report) {
        using var output = new StringWriter { NewLine = "\n" };
        TiaHardwareWriter.WriteText(report, output);
        return output.ToString();
    }

    private static JsonDocument Json(TiaHardwareReport report) {
        using var output = new MemoryStream();
        TiaHardwareWriter.WriteJson(report, output);
        return JsonDocument.Parse(output.ToArray());
    }

    [Test]
    public void TheTextListsEveryDeviceSubnetAndIoSystem() {
        var report = HardwareReports.Read("Hardware");

        var text = Text(report);

        foreach (var device in report.Devices) {
            text.Should().Contain($"Device {device.Name}  ");
        }
        foreach (var subnet in report.Subnets) {
            text.Should().Contain($"Subnet {subnet.Name}  ");
        }
        foreach (var system in report.IoSystems) {
            text.Should().Contain($"IO system {system.Name}  ");
        }
    }

    [Test]
    public void TheTextShowsAModuleWithItsOrderNumberFirmwareAndIoAddress() {
        var text = Text(HardwareReports.Read("Hardware"));

        text.Should().Contain("\n  2 DI_Main  DI 32x24VDC HF  6ES7 521-1BL00-0AB0  V2.2\n    1 DI_Main  I 10..13  Local~DI_Main = 257\n");
    }

    [Test]
    public void TheTextShowsAnInterfacesAddresses() {
        var text = Text(HardwareReports.Read("Hardware"));

        text.Should().Contain("X1 on PN/IE_1: IP 192.168.0.1, mask 255.255.255.0, device name controller\n");
        text.Should().Contain("X2 on PN/IE_2: IP address set at the device, device name controller.profinet interface_2 (generated)\n");
        text.Should().Contain("X3 on PROFIBUS_1: PROFIBUS address 2\n");
    }

    [Test]
    public void TheTextShowsWhichIoDevicesBelongToWhichIoController() {
        var text = Text(HardwareReports.Read("Hardware"));

        text.Should().Contain("IO system Partner IO system  (101)  on PN/IE_1  Local~Partner_IO_system = 257\n  IO controller Partner » Partner » PROFINET interface_1\n  IO device Partner_IO » Partner_IO » PROFINET interface\n");
    }

    [Test]
    public void TheJsonHoldsWhatTheTextHolds() {
        var report = HardwareReports.Read("Hardware");

        using var json = Json(report);

        var root = json.RootElement;
        root.GetProperty("devices").EnumerateArray().Select(device => device.GetProperty("name").GetString()).Should().Equal(report.Devices.Select(device => device.Name));
        root.GetProperty("subnets").GetArrayLength().Should().Be(report.Subnets.Count);
        root.GetProperty("ioSystems").GetArrayLength().Should().Be(report.IoSystems.Count);
        var items = Flatten(root.GetProperty("devices").EnumerateArray().SelectMany(device => device.GetProperty("items").EnumerateArray())).ToList();
        items.Select(item => item.GetProperty("path").GetString()).Should().Equal(report.AllItems.Select(item => item.Path));
        items.Single(item => item.GetProperty("path").GetString() == "Controller » DI_Main » DI_Main")
            .GetProperty("ioAddresses")[0].GetProperty("text").GetString().Should().Be("I 10..13");

        static IEnumerable<JsonElement> Flatten(IEnumerable<JsonElement> items) =>
            items.SelectMany(item => Flatten(item.GetProperty("items").EnumerateArray()).Prepend(item));
    }

    [Test]
    public void NoSecretIsListed() {
        var report = HardwareReports.Read("Hardware");
        using var output = new MemoryStream();
        TiaHardwareWriter.WriteJson(report, output);

        var json = Encoding.UTF8.GetString(output.ToArray());

        json.Should().NotContainAny("Password", "password", "Salt", "Certificate", "UserManagement");
    }

    [Test]
    public void AnArchiveWithoutDevicesGivesAnEmptyList() {
        var file = new TiaFileBuilder("""<ObjectType name="Thing" id="0x00100001" />""");
        file.WriteObject(0x00100001, 1, []);
        file.Commit();

        var report = TiaHardware.Read(new TiaProgram(TiaDatabase.Parse(file.ToArray())));

        report.Devices.Should().BeEmpty();
        report.Subnets.Should().BeEmpty();
        Text(report).Should().Be("The project has no devices.\n");
    }
}
