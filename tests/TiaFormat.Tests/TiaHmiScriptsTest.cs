using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0017: an HMI device's code, read from the reference project <c>HmiScripts</c>, which TIA Portal V21 built from
/// invented sources: a Unified panel <c>HMI_Panel</c> with scripts on screen <c>Overview</c>, the global script module
/// <c>DosingFunctions</c> and the custom web control <c>Dosing gauge</c>, and the WinCC RT Advanced runtime
/// <c>HMI_RT_1</c> with the VB script <c>CheckPump</c>.
/// </summary>
public sealed class TiaHmiScriptsTest {
    private const string Unified = "HMI_Panel";
    private const string Advanced = "HMI_RT_1";
    private const string Gauge = "{5C1A7E2B-9D34-4F60-8B21-7A3E0C9D4F12}";

    private static readonly Lazy<TiaProgram> Program = new(() => new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("HmiScripts"))));

    private static TiaHmiReport Report() => TiaHmi.Read(Program.Value, code: true);

    private static TiaHmiCode Code(string device) => Report().Devices.Single(candidate => candidate.Name == device).Code!;

    private static TiaHmiScript Script(string? item, string name) => Code(Unified).Scripts.Single(script => script.Item == item && script.Name == name);

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine([ReferenceProjects.Folder("HmiScripts"), "sources", .. path]), Encoding.UTF8);

    // Read an HMI device's scripts ------------------------------------------------------------------------------------

    [Test]
    public void AnEventScriptOfAScreenItemIsListedWithItsScreenItemEventAndCode() {
        var script = Script("StartButton", "Tapped");

        (script.Screen, script.Kind, script.Language).Should().Be(("Overview", TiaHmiScriptKind.Event, "JavaScript"));
        script.Code.Should().Be(Source("HMI_Panel", "scripts", "start-tapped.js"));
    }

    [Test]
    public void APropertyScriptIsListedWithItsScreenItemPropertyAndCode() {
        var script = Script("LevelValue", "BackColor");

        (script.Screen, script.Kind).Should().Be(("Overview", TiaHmiScriptKind.Property));
        script.Code.Should().Be(Source("HMI_Panel", "scripts", "level-color.js"));
    }

    [Test]
    public void AScriptOnAScreensOwnEventIsListedWithItsScreenAndEvent() {
        var script = Script(null, "Loaded");

        (script.Screen, script.Kind).Should().Be(("Overview", TiaHmiScriptKind.Event));
        script.Code.Should().Be(Source("HMI_Panel", "scripts", "overview-loaded.js"));
    }

    [Test]
    public void AReferenceInAScriptReadsAsTheNameOfTheTagItStandsFor() {
        // TIA stores each tag a script names as a reference; the same tag twice is two references to one object.
        Script("StartButton", "Tapped").Code.Should().Contain("Tags(\"StartCount\").Read()").And.Contain("Tags(\"StartCount\").Write(count + 1)");
        Script("SpeedValue", "Activated").Code.Should().Contain("Tags(\"Speed\").Write(speed + 1)");
    }

    [Test]
    public void AGlobalScriptModuleIsListedWithItsNameAndCode() {
        var module = Code(Unified).Modules.Should().ContainSingle().Subject;

        module.Name.Should().Be("DosingFunctions");
        module.Functions.Should().Equal("limit", "percent");
        module.Code.Should().Contain("const HighLimit = 100;").And.Contain("export function percent(part, whole) {");
    }

    [Test]
    public void AVbScriptIsListedWithItsNameParametersAndCode() {
        var script = Code(Advanced).VbScripts.Should().ContainSingle().Subject;

        script.Name.Should().Be("CheckPump");
        script.Parameters.Should().BeEmpty();
        script.Code.Should().Be("If SmartTags(\"PumpSpeed\") > 100 Then\n  ShowSystemAlarm \"Pump speed high\"\nEnd If\n");
        TiaHmiWriter.VbSource(script).Should().StartWith("Sub CheckPump()\n").And.EndWith("End If\nEnd Sub\n");
    }

    [Test]
    public void AnHmiDeviceWithNoScriptSaysSo() {
        // The reference project of spec 0011 has HMI devices and no code.
        var report = TiaHmi.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive("Hmi"))), "HMI_Panel", code: true);
        var output = new StringWriter { NewLine = "\n" };

        TiaHmiWriter.WriteMarkdown(report, "Hmi", output);

        output.ToString().Should().Contain("### Scripts\n\nNone.");
    }

    [Test]
    public void TheReportWithoutScriptsReadsNoCode() {
        TiaHmi.Read(Program.Value).Devices.Should().OnlyContain(device => device.Code == null);
    }

    // Read the scripts as files ---------------------------------------------------------------------------------------

    [Test]
    public void ADumpWritesEachScriptAndModuleAsAFileNamedAfterWhereItIsAttached() {
        var folder = Dump();
        var panel = Path.Combine(folder, "Project", Unified);

        File.ReadAllText(Path.Combine(panel, "scripts", "Overview", "StartButton.Tapped.js")).Should().Be(Script("StartButton", "Tapped").Code);
        File.ReadAllText(Path.Combine(panel, "scripts", "Overview", "LevelValue.BackColor.js")).Should().Be(Script("LevelValue", "BackColor").Code);
        File.Exists(Path.Combine(panel, "scripts", "Overview", "_screen.Loaded.js")).Should().BeTrue();
        File.ReadAllText(Path.Combine(panel, "modules", "DosingFunctions.js")).Should().Contain("export function limit(value) {");
        File.ReadAllText(Path.Combine(folder, "Project", Advanced, "vbscripts", "CheckPump.vbs")).Should().StartWith("Sub CheckPump()");
    }

    [Test]
    public void TheJsonReportWithScriptsHoldsTheCodeOfEachScript() {
        using var stream = new MemoryStream();
        TiaHmiWriter.WriteJson(Report(), stream);

        using var json = JsonDocument.Parse(stream.ToArray());
        var panel = json.RootElement.GetProperty("devices").EnumerateArray().Single(device => device.GetProperty("name").GetString() == Unified);
        var tapped = panel.GetProperty("scripts").EnumerateArray().Single(script => script.GetProperty("item").GetString() == "StartButton");
        tapped.GetProperty("event").GetString().Should().Be("Tapped");
        tapped.GetProperty("code").GetString().Should().Be(Script("StartButton", "Tapped").Code);
        panel.GetProperty("scriptModules")[0].GetProperty("functions").GetArrayLength().Should().Be(2);
        panel.GetProperty("customWebControls")[0].GetProperty("guid").GetString().Should().Be(Gauge);
    }

    // Read a custom web control ---------------------------------------------------------------------------------------

    [Test]
    public void ACustomWebControlIsListedWithItsNameWhereItIsUsedAndItsPackagesFiles() {
        var control = Code(Unified).CustomWebControls.Should().ContainSingle().Subject;

        (control.Guid, control.DisplayName).Should().Be((Gauge, "Dosing gauge"));
        control.UsedBy.Should().Equal(("Overview", "Dosing gauge_1"));
        control.Files.Select(file => file.Path).Should().BeEquivalentTo(PackageSources().Keys);
        foreach (var file in control.Files) {
            file.Content.Should().Equal(PackageSources()[file.Path], file.Path);
        }
    }

    [Test]
    public void ADumpWritesACustomWebControlsPackageUnpackedAsStored() {
        var folder = Path.Combine(Dump(), "Project", Unified, "custom web controls", "Dosing gauge");

        foreach (var (path, content) in PackageSources()) {
            File.ReadAllBytes(Path.Combine(folder, path)).Should().Equal(content, path);
        }
    }

    // See a screen as a schematic preview ---------------------------------------------------------------------------

    [Test]
    public void AUnifiedScreenItemHasTheTextItShows() {
        var screen = TiaHmi.Read(Program.Value, Unified).Devices.Single().Screens.Single(candidate => candidate.Name == "Overview");

        screen.Items.Single(item => item.Name == "LevelLabel").Text.Should().Be("Tank level");
        screen.Items.Single(item => item.Name == "LevelValue").Text.Should().BeNull();
    }

    // It agrees with TIA -----------------------------------------------------------------------------------------------

    [Test]
    public void EveryScriptModuleVbScriptAndControlFileAgreesWithWhatTiaGaveForIt() {
        var problems = HmiCodeReference.Problems("HmiScripts", out var compared);

        problems.Should().BeEmpty();
        compared.Should().BeGreaterThan(10);
    }

    [Test]
    public void TheReferenceProjectsHmiDevicesAgreeWithWhatTiaGaveThroughOpenness() {
        HmiReference.Problems("HmiScripts", out _).Should().BeEmpty();
    }

    // Helpers -------------------------------------------------------------------------------------------------------

    private static readonly Lazy<string> Dumped = new(() => {
        var folder = Path.Combine(Path.GetTempPath(), "tia-hmiscripts-" + Guid.NewGuid().ToString("N"));
        new TiaSourceExport(Program.Value, folder).Run();
        return folder;
    });

    private static string Dump() => Dumped.Value;

    /// <summary>The files of the control's package as the sources hold them, by their path in the package.</summary>
    private static Dictionary<string, byte[]> PackageSources() {
        var root = Path.Combine(ReferenceProjects.Folder("HmiScripts"), "sources", "HMI_Panel", "custom web controls", Gauge);
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }
}
