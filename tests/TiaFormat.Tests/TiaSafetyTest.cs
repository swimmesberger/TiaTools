using System.Text;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0012: what the safety report does with what no reference project holds, on small invented files: no PLC, a PLC
/// without a safety program, and a password value that must never come out.
/// </summary>
public sealed class TiaSafetyTest {
    private const uint Project = 0x00240001;
    private const uint Plc = 0x00240002;
    private const uint Info = 0x00240003;
    private const uint Settings = 0x00240004;
    private const uint PlcEnvironment = 0x00242001;
    private const uint InfoTarget = 0x00242002;
    private const uint InfoEnvironment = 0x00242003;
    private const uint InfoSettings = 0x00242004;

    /// <summary>Stands in for what TIA stores of a safety password; it must never appear in a report.</summary>
    private const string PasswordMarker = "PASSWORD-MARKER-0012";

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        </Namespace><Namespace name="Siemens.Automation.DomainModel">
        <ObjectType name="ProjectData" id="0x00240001"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.HwConfiguration.Model">
        <ObjectType name="S7ControllerTargetData" id="0x00240002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Environment" id="0x00242001" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Safety.Model">
        <AttributeSet name="IFailsafeProgramSettings" id="0x2" persistent="true">
          <Attribute name="SafetySystemVersion" id="0" type="xs:string"/><Attribute name="FProgramPasswordPlus" id="1" type="xs:string"/><Attribute name="ActivateFChangelog" id="2" type="xs:boolean"/>
        </AttributeSet>
        <ObjectType name="FailsafeProgramSettings" id="0x00240004">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Simatic.Safety.Model.IFailsafeProgramSettings"/>
        </ObjectType>
        <ObjectType name="FailsafeInfo" id="0x00240003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Target" id="0x00242002" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
          <Relation name="Environment" id="0x00242003" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
          <Relation name="FailsafeInfoToFailsafeProgramSettings" id="0x00242004" cardinality="1"><Target ref="Siemens.Simatic.Safety.Model.FailsafeProgramSettings"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    private static byte[] Core(string name) => TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String("")));

    /// <summary>A project with the PLC <c>Press</c>, which has a safety program unless told otherwise, and the PLC <c>Labeler</c> without one.</summary>
    private static TiaProgram NewProject(bool withPlcs = true, string password = PasswordMarker) {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Project, 1, [Core("Line"), null]);
        if (withPlcs) {
            file.WriteObject(Plc, 10, [Core("Press"), TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);
            file.WriteObject(Plc, 11, [Core("Labeler"), TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);
            var settings = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("V2.8")), (8, TiaFileBuilder.String(password)));
            settings[12] = 1;
            file.WriteObject(Settings, 30, [Core(""), settings, null]);
            file.WriteObject(Info, 20, [Core(""), TiaFileBuilder.Slots((InfoTarget, Plc, 10), (InfoEnvironment, Project, 1), (InfoSettings, Settings, 30))]);
        }
        file.Commit();
        return new TiaProgram(TiaDatabase.Parse(file.ToArray()));
    }

    private static string Markdown(TiaSafetyReport report) {
        var output = new StringWriter();
        TiaSafetyWriter.WriteMarkdown(report, "Line", output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    private static string Json(TiaSafetyReport report) {
        using var output = new MemoryStream();
        TiaSafetyWriter.WriteJson(report, output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Test]
    public void AnArchiveWithoutAPlcGivesAnEmptyReport() {
        var report = TiaSafety.Read(NewProject(withPlcs: false));

        report.Plcs.Should().BeEmpty();
        Markdown(report).Should().Contain("The project has no PLC.");
    }

    [Test]
    public void APlcWithoutASafetyProgramIsReportedSo() {
        var report = TiaSafety.Read(NewProject());

        report.Plcs.Single(plc => plc.Name == "Labeler").Program.Should().BeNull();
        Markdown(report).Should().Contain("## PLC Labeler\n\nNo safety program.");
        Json(report).Should().Contain("\"safetyProgram\": null");
    }

    [Test]
    public void APasswordValueIsReportedSetAndNeverShown() {
        var report = TiaSafety.Read(NewProject());

        report.Plcs.Single(plc => plc.Name == "Press").Program!.IsPasswordSet.Should().BeTrue();
        Markdown(report).Should().Contain("| Safety program password | set |").And.NotContain(PasswordMarker);
        Json(report).Should().Contain("\"passwordSet\": true").And.NotContain(PasswordMarker);
    }

    [Test]
    public void AnEmptyPasswordValueMeansNoPassword() {
        TiaSafety.Read(NewProject(password: "")).Plcs.Single(plc => plc.Name == "Press").Program!.IsPasswordSet.Should().BeFalse();
    }

    [Test]
    public void TheInspectorShowsNoPasswordValue() {
        var program = NewProject();
        program.Database.TryGetFrame(new TiaObjectKey(Settings, 30), out var frame).Should().BeTrue();
        var output = new StringWriter();

        TiaInspector.Write(program.Database, frame, output);

        output.ToString().Should().Contain("FProgramPasswordPlus = [not shown]").And.NotContain(PasswordMarker);
    }

    [Test]
    public void APlcsSafetyProgramIsFoundWithoutReadingTheReport() {
        TiaSafety.PlcsWithSafetyProgram(NewProject()).Should().BeEquivalentTo("Press");
    }

    [Test]
    public void AnEventClassIsWrittenAsTiaWritesIt() {
        TiaSafety.Words("CyclicInterrupt").Should().Be("Cyclic interrupt");
    }
}
