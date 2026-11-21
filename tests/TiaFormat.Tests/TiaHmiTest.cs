using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0011: a project's HMI devices, read from the reference project <c>Hmi</c>, which TIA Portal V21 built (a Unified
/// panel <c>HMI_Panel</c> on <c>PLC_Mixer</c>, and the WinCC RT Advanced runtime <c>HMI_RT_1</c> of an Open Controller on
/// its software PLC <c>PLC_Station</c>), and from <see cref="TiaHmiFixture"/> for what that project cannot hold.
/// </summary>
public sealed class TiaHmiTest {
    private const string Unified = "HMI_Panel";
    private const string Advanced = "HMI_RT_1";

    private static readonly Lazy<TiaProgram> Program = new(() => new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("Hmi"))));

    private static TiaHmiReport Report() => TiaHmi.Read(Program.Value);

    private static TiaHmiDevice Device(string name) => Report().Devices.Single(device => device.Name == name);

    private static TiaHmiTag Tag(string device, string name) => Device(device).TagTables.SelectMany(table => table.Tags).Single(tag => tag.Name == name);

    // Which HMI devices are there, and which PLCs do they talk to -----------------------------------------------------

    [Test]
    public void EachHmiDeviceIsListedWithItsNameWinCcFamilyAndDeviceType() {
        var devices = Report().Devices;

        devices.Select(device => (device.Name, device.FamilyName)).Should().Equal((Unified, "Unified"), (Advanced, "Comfort/Advanced"));
        Device(Unified).DeviceType.Should().Be("MTP700 Unified Comfort");
        Device(Advanced).DeviceType.Should().Contain("WinCC RT Advanced");
    }

    [Test]
    public void AConnectionIsListedWithItsNameThePlcItLeadsToAndItsDriver() {
        var connection = Device(Unified).Connections.Should().ContainSingle().Subject;

        connection.Name.Should().Be("HMI_Connection_1");
        connection.Plc.Should().Be("PLC_Mixer");
        connection.DriverName.Should().Be("SIMATIC S7 1200/1500");
        Device(Advanced).Connections.Should().ContainSingle().Which.Plc.Should().Be("PLC_Station");
    }

    [Test]
    public void AnHmiDeviceHeldOnlyInAMasterCopyIsNotListed() {
        var devices = TiaHmi.Read(new TiaProgram(TiaHmiFixture.NewProject())).Devices;

        devices.Select(device => device.Name).Should().Equal("Panel");
    }

    [Test]
    public void AnArchiveWithNoHmiDeviceSaysSo() {
        var report = TiaHmi.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("Plcs"))));
        var text = new StringWriter();

        TiaHmiWriter.WriteMarkdown(report, "Plcs", text);

        report.Devices.Should().BeEmpty();
        text.ToString().Should().Contain("The project has no HMI device.");
    }

    // What does each HMI tag read --------------------------------------------------------------------------------------

    [Test]
    public void AnHmiTagLinkedToAPlcTagGivesItsTableNameTypeConnectionCycleAndThePlcTag() {
        var table = Device(Unified).TagTables.Single(candidate => candidate.Tags.Any(tag => tag.Name == "TankLevel"));
        var tag = Tag(Unified, "TankLevel");

        table.Name.Should().Be("Tank");
        tag.DataType.Should().Be("Int");
        tag.Connection.Should().Be("HMI_Connection_1");
        tag.AcquisitionCycle.Should().NotBeNullOrEmpty();
        tag.PlcLink!.Display.Should().Be("\"Tank_Level\"");
        tag.PlcLink.Plc.Should().Be("PLC_Mixer");
    }

    [Test]
    public void AnHmiTagLinkedToADataBlockMemberGivesTheBlockAndTheMembersPath() {
        Tag(Unified, "RecipeSpeed").PlcLink!.Display.Should().Be("\"Mixer\".Recipe.Speed");
        Tag(Unified, "MixerRunning").PlcLink!.Display.Should().Be("\"Mixer\".Running");
    }

    [Test]
    public void AMemberWhosePathCannotBeResolvedGivesTheBlockAndSaysSo() {
        var device = TiaHmi.Read(new TiaProgram(TiaHmiFixture.NewProject())).Devices.Single();

        var link = device.TagTables.Single().Tags.Single(tag => tag.Name == "RecipeSpeed").PlcLink!;

        link.IsResolved.Should().BeFalse();
        link.Display.Should().Be("\"Recipe\".?");
    }

    [Test]
    public void AnHmiTagOfAStructuredTypeGivesTheType_AndDoesNotListTheMembers() {
        var device = TiaHmi.Read(new TiaProgram(TiaHmiFixture.NewProject())).Devices.Single();

        var tags = device.TagTables.Single().Tags;

        tags.Select(tag => tag.Name).Should().Equal("RecipeSpeed", "RecipeData");
        tags.Single(tag => tag.Name == "RecipeData").DataType.Should().Be("typeRecipe");
    }

    [Test]
    public void AnHmiTagWithAnAbsoluteAddressGivesTheAddressAndItsConnection() {
        var tag = Tag(Advanced, "PumpSpeed");

        tag.PlcLink.Should().BeNull();
        tag.Address.Should().Be("%MW10");
        tag.Connection.Should().Be(Device(Advanced).Connections.Single().Name);
    }

    [Test]
    public void AnInternalHmiTagIsListedAsInternalWithItsDataType() {
        var tag = Tag(Unified, "SetpointLevel");

        tag.IsInternal.Should().BeTrue();
        tag.DataType.Should().Be("Int");
        Tag(Advanced, "SpeedSetpoint").IsInternal.Should().BeTrue();
    }

    // Which alarms does the HMI raise ----------------------------------------------------------------------------------

    [Test]
    public void ADiscreteAlarmIsListedWithNameNumberClassTriggerTagBitAndText() {
        var alarm = Device(Unified).Alarms.Single(candidate => candidate.Name == "ValveFault");

        alarm.Kind.Should().Be(TiaHmiAlarmKind.Discrete);
        alarm.Number.Should().NotBeNull();
        alarm.AlarmClass.Should().Be("Alarm");
        alarm.TriggerTag.Should().Be("AlarmWord");
        alarm.Bit.Should().Be(3);
        alarm.Text.Should().Be("Inlet valve fault");
    }

    [Test]
    public void AnAnalogAlarmIsListedWithNameNumberClassTriggerTagLimitModeLimitAndText() {
        var alarm = Device(Unified).Alarms.Single(candidate => candidate.Name == "TempHigh");

        alarm.Kind.Should().Be(TiaHmiAlarmKind.Analog);
        alarm.AlarmClass.Should().Be("Warning");
        alarm.TriggerTag.Should().Be("TankTemp");
        alarm.LimitMode.Should().Be("UpperLimit");
        alarm.Limit.Should().Be("80");
        alarm.Text.Should().Be("Tank temperature high");
    }

    [Test]
    public void AnAlarmsTriggerTagLeadsToThePlcTagItReads() {
        var alarm = Device(Unified).Alarms.Single(candidate => candidate.Name == "TempHigh");

        Tag(Unified, alarm.TriggerTag!).PlcLink!.Display.Should().Be("\"Tank_Temp\"");
    }

    // What is on each screen -------------------------------------------------------------------------------------------

    [Test]
    public void AScreenIsListedWithItsNameNumberWidthAndHeight() {
        var overview = Device(Unified).Screens.Single(screen => screen.Name == "Overview");
        var pump = Device(Advanced).Screens.Single(screen => screen.Name == "Pump");

        (overview.Width, overview.Height).Should().Be((800L, 480L));
        pump.Number.Should().Be(2);
        (pump.Width, pump.Height).Should().NotBe((null, null));
    }

    [Test]
    public void EachScreenItemIsListedWithNameTypePositionSize_AndAnIoFieldWithItsTag() {
        var items = Device(Unified).Screens.Single(screen => screen.Name == "Overview").Items;

        var value = items.Single(item => item.Name == "LevelValue");
        value.Type.Should().Be("HmiIOField");
        (value.Left, value.Top, value.Width, value.Height).Should().Be((200L, 40L, 120L, 30L));
        value.Tags.Should().Equal("TankLevel");
        items.Single(item => item.Name == "LevelLabel").Type.Should().Be("HmiText");
        var comfort = Device(Advanced).Screens.Single(screen => screen.Name == "Pump").Items.Single(item => item.Name == "SpeedValue");
        (comfort.Type, comfort.Left, comfort.Top, comfort.Width, comfort.Height).Should().Be(("IOField", 200L, 40L, 120L, 30L));
    }

    // Read it in the dump and in a program -----------------------------------------------------------------------------

    [Test]
    public void ADumpWritesAFolderPerHmiDevice_AndEveryBlockFileAsBefore() {
        var folder = ReferenceProjects.Dump("Hmi");
        try {
            var device = Path.Combine(folder, "Project", Unified);
            File.ReadAllText(Path.Combine(device, "connections.tsv")).Should().Contain("HMI_Connection_1\tPLC_Mixer");
            File.ReadAllText(Path.Combine(device, "tags", "Tank.tsv")).Should().Contain("TankLevel\tInt\tHMI_Connection_1");
            File.ReadAllText(Path.Combine(device, "alarms.tsv")).Should().Contain("ValveFault");
            File.Exists(Path.Combine(device, "screens", "Overview.tsv")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "Project", "PLC_Mixer", "ProgramBlocksFolder", "Mixer.db")).Should().BeTrue();
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void TheJsonReportHoldsTheSameFactsAsTheText() {
        var stream = new MemoryStream();
        TiaHmiWriter.WriteJson(Report(), stream);

        using var json = JsonDocument.Parse(stream.ToArray());
        var devices = json.RootElement.GetProperty("devices");
        devices.GetArrayLength().Should().Be(2);
        var panel = devices.EnumerateArray().Single(device => device.GetProperty("name").GetString() == Unified);
        panel.GetProperty("tagTables").EnumerateArray().Sum(table => table.GetProperty("tags").GetArrayLength())
            .Should().Be(Device(Unified).TagTables.Sum(table => table.Tags.Count));
        panel.GetProperty("alarms").GetArrayLength().Should().Be(4);
        panel.GetProperty("screens").GetArrayLength().Should().Be(Device(Unified).Screens.Count);
    }

    [Test]
    public void AReportForOneHmiDeviceHoldsOnlyThatDevice_AndNoneForAnUnknownName() {
        TiaHmi.Read(Program.Value, Advanced).Devices.Select(device => device.Name).Should().Equal(Advanced);
        TiaHmi.Read(Program.Value, "NoSuchPanel").Devices.Should().BeEmpty();
    }

    // It agrees with TIA -----------------------------------------------------------------------------------------------

    [Test]
    public void EveryHmiDeviceConnectionTagAlarmScreenAndItemAgreesWithWhatTiaGaveThroughOpenness() {
        var problems = HmiReference.Problems("Hmi", out var compared);

        problems.Should().BeEmpty();
        compared.Should().BeGreaterThan(20);
    }

    // Nothing secret comes out -----------------------------------------------------------------------------------------

    [Test]
    public void NoRuntimeUserOrPasswordAppears_InTheReportOrWhenInspected() {
        var db = Program.Value.Database;
        var text = new StringWriter();
        TiaHmiWriter.WriteMarkdown(Report(), "Hmi", text);
        var inspected = new StringWriter();
        var users = db.Schema.ObjectTypes.Where(type => type.FullName.Contains(".RTSecurity.", StringComparison.Ordinal)).Select(type => type.Id).ToHashSet();
        foreach (ref readonly var frame in db.Frames) {
            if (users.Contains(frame.TypeId)) {
                TiaInspector.Write(db, frame, inspected, full: true);
            }
        }

        text.ToString().Should().NotContainAny("Password", "RTUser");
        inspected.ToString().Should().Contain("not shown").And.NotContain("Password =");
    }
}
