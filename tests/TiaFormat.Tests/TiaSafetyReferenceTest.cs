using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0012: the safety report, on the reference project <c>Safety</c> that TIA Portal built and compiled from invented
/// sources, and checked against what TIA reported about it through Openness.
/// </summary>
public sealed class TiaSafetyReferenceTest {
    private const string Project = "Safety";

    /// <summary>The safety password the exporter sets on PLC_Locked: invented, written in TiaOpennessExport, guarding nothing.</summary>
    private const string InventedPassword = "Reference-0012";

    private static readonly Lazy<TiaProgram> ProgramOfProject = new(() => new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive(Project))));

    private static TiaSafetyReport Report() => TiaSafety.Read(ProgramOfProject.Value);

    private static TiaSafetyProgram Plc(string name) => Report().Plcs.Single(plc => plc.Name == name).Program!;

    private static string Markdown(TiaSafetyReport report) {
        var output = new StringWriter();
        TiaSafetyWriter.WriteMarkdown(report, Project, output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    private static string Json(TiaSafetyReport report) {
        using var output = new MemoryStream();
        TiaSafetyWriter.WriteJson(report, output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    // The tests check the report against TIA Portal ------------------------------------------------------------------

    public static IEnumerable<string> ProjectsWithSafety() => ReferenceProjects.Names().Where(project => SafetyReference.Plcs(project).Count != 0);

    [Test]
    [MethodDataSource(nameof(ProjectsWithSafety))]
    public void EverySafetyValueMatchesTiasReport(string project) {
        SafetyReference.Problems(project, out var compared).Should().BeEmpty();

        compared.Should().BeGreaterThan(100, "every setting, group, F-I/O and F-block of the project is compared");
    }

    [Test]
    public void ADifferentValueNamesTheObjectAndBothVersions() {
        var problems = SafetyReference.Problems("Plant", "PLC_A",
            [new SafetyRow("group", "Line", "MaximumCycleTime", "200000")],
            [new SafetyRow("group", "Line", "MaximumCycleTime", "120000")]);

        problems.Should().ContainSingle().Which.Should().Be("Plant: PLC_A group Line MaximumCycleTime: ours `200000`, TIA's `120000`");
    }

    [Test]
    public void AValueOnlyTiaReportsFails() {
        var problems = SafetyReference.Problems("Plant", "PLC_A", [], [new SafetyRow("fio", "Remote/F-DI", "Failsafe_FDestinationAddress", "150")]);

        problems.Should().ContainSingle().Which.Should().Be("Plant: PLC_A fio Remote/F-DI Failsafe_FDestinationAddress: TIA's `150`, the reader reports none");
    }

    [Test]
    public void AValueOnlyTheReportHasFails() {
        var problems = SafetyReference.Problems("Plant", "PLC_A", [new SafetyRow("block", "Guard", "BlockOfflineSignature", "7")], []);

        problems.Should().ContainSingle().Which.Should().Be("Plant: PLC_A block Guard BlockOfflineSignature: ours `7`, TIA reports none");
    }

    [Test]
    public void AnAttributeTheReportDoesNotCoverIsNotCompared() {
        SafetyReference.Problems("Plant", "PLC_A", [], [new SafetyRow("group", "Line", "FOBPhaseShift", "0")]).Should().BeEmpty();
    }

    // See a PLC's safety program at a glance --------------------------------------------------------------------------

    [Test]
    public void ASafetyProgramIsReportedWithItsSettings() {
        var settings = Plc("PLC_Safety").Settings;

        settings.SystemVersion.Should().Be("V2.8");
        settings.ChangeHistory.Should().BeTrue();
        settings.SafetyModeCanBeDisabled.Should().BeTrue();
        settings.FixedBlockNumbers.Should().BeTrue();
        settings.DbNumbers.Should().Be(new TiaNumberRange(40000, 40999));
        Plc("PLC_Safety").IsPasswordSet.Should().BeFalse();
        Markdown(Report()).Should().Contain("| F-block numbers | fixed: F-FB 32767 to 65535, F-FC 32767 to 65535, F-DB 40000 to 40999 |")
            .And.Contain("| Safety program password | not set |");
    }

    [Test]
    public void FBlockNumbersManagedByTheFSystemAreReportedSo() {
        Plc("PLC_Locked").Settings.FixedBlockNumbers.Should().BeFalse();
        Markdown(Report()).Should().Contain("| F-block numbers | managed by the F-system |");
    }

    [Test]
    public void APlcOptionLimitsTheReportToThatPlc() {
        var report = TiaSafety.Read(ProgramOfProject.Value, "PLC_Locked");

        report.Plcs.Should().ContainSingle().Which.Name.Should().Be("PLC_Locked");
        TiaSafety.Read(ProgramOfProject.Value, "PLC_Nowhere").Plcs.Should().BeEmpty();
    }

    [Test]
    public void TheJsonHoldsTheSameContent() {
        using var json = JsonDocument.Parse(Json(Report()));

        var plc = json.RootElement.GetProperty("plcs").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "PLC_Safety");
        var program = plc.GetProperty("safetyProgram");
        program.GetProperty("signatures").GetProperty("collective").GetString().Should().Be(TiaSafety.Hex(Plc("PLC_Safety").Signatures.Collective!.Value));
        program.GetProperty("runtimeGroups").GetArrayLength().Should().Be(2);
        program.GetProperty("fIo").GetArrayLength().Should().Be(3);
        program.GetProperty("blocks").GetArrayLength().Should().Be(Plc("PLC_Safety").Blocks.Count);
        program.GetProperty("blockNumbers").GetProperty("db").GetProperty("from").GetInt64().Should().Be(40000);
    }

    // See the F-runtime groups -----------------------------------------------------------------------------------------

    [Test]
    public void TwoRuntimeGroupsAreReportedWithTheirFObsMainBlocksAndCycleTimes() {
        var groups = Plc("PLC_Safety").RuntimeGroups;

        groups.Select(group => group.Name).Should().Equal("Line", "Guard");
        var line = groups[0];
        line.FOb.Should().Be(new TiaSafetyBlockReference("OB", 123, "FOB_RTG1"));
        line.EventClass.Should().Be("Cyclic interrupt");
        line.CycleTime.Should().Be(50000);
        line.MainBlock.Should().Be(new TiaSafetyBlockReference("FB", 1, "LineSafety"));
        line.MainInstance.Should().Be(new TiaSafetyBlockReference("DB", 1, "LineSafety_DB"));
        line.MaximumCycleTime.Should().Be(200000);
        line.WarningCycleTime.Should().Be(150000);
        Markdown(Report()).Should().Contain("| Line | FOB_RTG1 (OB 123) | Cyclic interrupt | 50 ms | LineSafety (FB 1) | LineSafety_DB (DB 1) | 200 ms | 150 ms | RTG1SysInfo |");
    }

    [Test]
    public void ARuntimeGroupReportsItsInformationDbAndNoPreOrPostProcessing() {
        var guard = Plc("PLC_Safety").RuntimeGroups.Single(group => group.Name == "Guard");

        guard.InformationBlock.Should().Be("RTG2SysInfo");
        guard.PreProcessing.Should().BeNull();
        guard.PostProcessing.Should().BeNull();
    }

    // See the F-signatures ---------------------------------------------------------------------------------------------

    [Test]
    public void ACompiledProgramHasItsSignaturesAndCompileTime() {
        var signatures = Plc("PLC_Safety").Signatures;

        signatures.Collective.Should().NotBeNull();
        signatures.Software.Should().NotBeNull();
        signatures.Hardware.Should().NotBeNull();
        signatures.Compiled.Should().NotBeNull();
        Markdown(Report()).Should().Contain($"| Collective F-signature | {TiaSafety.Hex(signatures.Collective!.Value)} |");
    }

    [Test]
    public void AProgramChangedSinceCompilingHasNoCollectiveSignature() {
        var signatures = Plc("PLC_Locked").Signatures;

        signatures.Collective.Should().BeNull();
        signatures.Software.Should().BeNull();
        Markdown(TiaSafety.Read(ProgramOfProject.Value, "PLC_Locked")).Should().Contain("| Collective F-signature | none |");
    }

    [Test]
    public void EachFailsafeBlockHasItsSignatureAndGeneratedBlocksAreMarked() {
        var blocks = Plc("PLC_Safety").Blocks;

        blocks.Where(block => !block.IsGenerated).Select(block => block.Name).Should().BeEquivalentTo(
            "Enable", "Guard", "SafetyData", "LineSafety", "GuardSafety", "LineSafety_DB", "GuardSafety_DB");
        blocks.Single(block => block.Name == "Enable").Should().Match<TiaFailsafeBlock>(block => block.Language == "F_LAD" && block.Signature != null);
        blocks.Single(block => block.Name == "FOB_RTG1").IsGenerated.Should().BeTrue();
        blocks.Should().Contain(block => block.Name == "F00000_F-DI_Main" && block.IsGenerated);
    }

    [Test]
    public void AKnowHowProtectedFailsafeBlockShowsItsSignatureAndNothingOfItsBody() {
        var fOb = Plc("PLC_Safety").Blocks.Single(block => block.Name == "FOB_RTG1");

        fOb.IsKnowHowProtected.Should().BeTrue();
        fOb.Signature.Should().NotBeNull();
        Markdown(Report()).Should().Contain($"| OB | 123 | FOB_RTG1 | SCL | {TiaSafety.Hex(fOb.Signature!.Value)} | generated, closed |");
    }

    // See the F-parameters of the F-I/O -------------------------------------------------------------------------------

    [Test]
    public void EachFIoIsReportedByDeviceAndNameWithItsFParameters() {
        var io = Plc("PLC_Safety").Io;

        io.Select(entry => (entry.Device, entry.Name)).Should().Equal(("PLC_Safety", "F-DI_Main"), ("PLC_Safety", "F-DQ_Main"), ("Remote_F", "F-DI_Remote"));
        var remote = io[2];
        remote.InputAddress.Should().Be(20);
        remote.FSourceAddress.Should().Be(new TiaSafetyValue(7, false));
        remote.FDestinationAddress.Should().Be(new TiaSafetyValue(150, false));
        remote.MonitoringTime.Should().Be(150);
        remote.DataBlockName.Should().Be("F00020_F-DI_Remote");
    }

    [Test]
    public void AMonitoringTimeSetByHandIsMarkedAndADefaultOneIsNot() {
        var io = Plc("PLC_Safety").Io;

        io.Single(entry => entry.Name == "F-DQ_Main").Should().Match<TiaFailsafeIo>(entry => entry.MonitoringTime == 250 && entry.MonitoringTimeByHand);
        io.Single(entry => entry.Name == "F-DI_Main").Should().Match<TiaFailsafeIo>(entry => entry.MonitoringTime == 200 && !entry.MonitoringTimeByHand);
        Markdown(Report()).Should().Contain("| 250 ms (set by hand) |");
    }

    [Test]
    public void AnFDestinationAddressNotStoredIsTiasDefault() {
        Plc("PLC_Safety").Io.Single(entry => entry.Name == "F-DI_Main").FDestinationAddress.Should().Be(new TiaSafetyValue(TiaSafety.DefaultFDestinationAddress, true));
    }

    [Test]
    public void TheFCpusParametersAreReportedAndADefaultIsMarked() {
        Plc("PLC_Safety").Cpu.Should().Be(new TiaFailsafeCpu(new(7, false), new(100, false), new(400, false), new(200, false)));
        Plc("PLC_Locked").Cpu.DefaultMonitoringTime.Should().Be(new TiaSafetyValue(TiaSafety.DefaultMonitoringTime, true));
        Markdown(TiaSafety.Read(ProgramOfProject.Value, "PLC_Locked")).Should().Contain("| Default F-monitoring time | 150 ms (default) |");
    }

    // Nothing secret comes out -----------------------------------------------------------------------------------------

    [Test]
    public void APasswordIsReportedSetAndNothingOfItAppearsInAnyOutput() {
        Plc("PLC_Locked").IsPasswordSet.Should().BeTrue();
        var stored = StoredPassword();
        stored.Should().NotBeNullOrEmpty("the archive holds a value derived from the password");

        var outputs = new List<string> { Markdown(Report()), Json(Report()) };
        var dump = Directory.CreateTempSubdirectory("tiatools-safety-");
        try {
            new TiaSourceExport(ProgramOfProject.Value, dump.FullName).Run();
            outputs.AddRange(dump.EnumerateFiles("*", SearchOption.AllDirectories).Select(file => File.ReadAllText(file.FullName)));
        } finally {
            dump.Delete(recursive: true);
        }

        foreach (var output in outputs) {
            output.Should().NotContain(InventedPassword).And.NotContain(stored!);
        }
    }

    [Test]
    public void AProgramWithAPasswordIsReportedLikeOneWithout() {
        var locked = Plc("PLC_Locked");

        locked.RuntimeGroups.Should().ContainSingle().Which.MainBlock.Should().Be(new TiaSafetyBlockReference("FB", 1, "LockedSafety"));
        locked.Blocks.Single(block => block.Name == "LockedSafety").Signature.Should().NotBeNull();
    }

    /// <summary>What the archive stores for PLC_Locked's password, read only to prove it never comes out.</summary>
    private static string? StoredPassword() {
        var db = ProgramOfProject.Value.Database;
        var settings = db.Schema.FindObjectType("Siemens.Simatic.Safety.Model.FailsafeProgramSettings")!.Id;
        var password = db.Schema.Attribute("Siemens.Simatic.Safety.Model.IFailsafeProgramSettings", "FProgramPasswordPlus");
        foreach (ref readonly var frame in db.Frames) {
            if (frame.TypeId == settings && db.ReadString(frame, password) is { Length: > 0 } value) {
                return value;
            }
        }
        return null;
    }
}
