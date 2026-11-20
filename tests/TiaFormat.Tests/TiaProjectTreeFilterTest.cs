using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0007: what the viewer's tree filters by, and which OBs it marks as where the program's cycle starts.</summary>
public sealed class TiaProjectTreeFilterTest {
    private const uint ProjectType = 0x00240001;
    private const uint MasterCopyType = 0x00240002;
    private const uint CodeBlock = 0x00240003;
    private const uint DataBlock = 0x00240004;
    private const uint UserType = 0x00240005;
    private const uint CodeEnvironment = 0x00242001;
    private const uint DataEnvironment = 0x00242002;
    private const uint TypeEnvironment = 0x00242003;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        </Namespace><Namespace name="Siemens.Automation.DomainModel">
        <ObjectType name="ProjectData" id="0x00240001"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        <ObjectType name="LibraryMasterCopyData" id="0x00240002"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <AttributeSet name="IGeneralBlockSourceData" id="0x2" persistent="true">
          <Attribute name="BlockType" id="0" type="xs:string"/><Attribute name="Number" id="1" type="xs:int"/>
        </AttributeSet>
        <ObjectType name="CodeBlockData" id="0x00240003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Relation name="Environment" id="0x00242001" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        <ObjectType name="DataBlockData" id="0x00240004">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Relation name="Environment" id="0x00242002" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        <ObjectType name="UserTypeData" id="0x00240005">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Environment" id="0x00242003" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    private static TiaFileBuilder File() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ProjectType, 1, [Core("Filling line", null)]);
        file.WriteObject(MasterCopyType, 2, [Core("Copies", null)]);
        return file;
    }

    private static byte[] Core(string name, string? subtype, bool closed = false) {
        var core = subtype is null
            ? TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)))
            : TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));
        core[12] = closed ? (byte)1 : (byte)0;
        return core;
    }

    private static byte[] General(string kind, int number) {
        var general = TiaFileBuilder.Segment(8, (4, TiaFileBuilder.String(kind)));
        BitConverter.TryWriteBytes(general.AsSpan(8), number);
        return general;
    }

    /// <summary>A code block of the project, or of the master copy, with the subtype TIA stores for it.</summary>
    private static void Code(TiaFileBuilder file, ulong id, string name, string kind, int number, string? subtype, bool inMasterCopy = false, bool closed = false) =>
        file.WriteObject(CodeBlock, id, [
            Core(name, subtype, closed), General(kind, number),
            TiaFileBuilder.Slots(inMasterCopy ? (CodeEnvironment, MasterCopyType, 2UL) : (CodeEnvironment, ProjectType, 1UL)),
        ]);

    private static TiaItem Item(TiaProject project, string name) => project.Items.Single(item => item.Name == name);

    // See where the program starts --------------------------------------------------------------------------------------

    [Test]
    public void AnObWhoseEventClassIsProgramCycleCarriesTheMark() {
        var file = File();
        Code(file, 10, "Main", "OB", 1, "OB.ProgramCycle");

        var main = Item(TiaProjectTest.Opened(file), "Main");

        main.Block!.EventClass.Should().Be("ProgramCycle");
        main.IsProgramCycle.Should().BeTrue();
    }

    [Test]
    public void SeveralProgramCycleObsEachCarryTheMark() {
        var file = File();
        Code(file, 10, "Main", "OB", 1, "OB.ProgramCycle");
        Code(file, 11, "Second cycle", "OB", 123, "OB.ProgramCycle");

        var project = TiaProjectTest.Opened(file);

        project.Items.Where(item => item.IsProgramCycle).Select(item => item.Name).Should().BeEquivalentTo(["Main", "Second cycle"]);
    }

    [Test]
    public void AnObWithAnotherEventClassCarriesNoMark_WhateverItsNumber() {
        var file = File();
        Code(file, 10, "Startup", "OB", 1, "OB.Startup");
        Code(file, 11, "Cyclic", "OB", 30, "OB.CyclicInterrupt");

        var project = TiaProjectTest.Opened(file);

        Item(project, "Startup").Block!.EventClass.Should().Be("Startup");
        project.Items.Should().OnlyContain(item => !item.IsProgramCycle);
    }

    [Test]
    public void AFailSafeObsEventClassLeavesOutItsSafetySuffix() {
        var file = File();
        Code(file, 10, "Safety cycle", "OB", 35, "OB.CyclicInterrupt.Safety");

        Item(TiaProjectTest.Opened(file), "Safety cycle").Block!.EventClass.Should().Be("CyclicInterrupt");
    }

    [Test]
    public void AnObThatStoresNoEventClassCarriesTheMarkOnlyAsNumber1() {
        var file = File();
        Code(file, 10, "First", "OB", 1, "OB");
        Code(file, 11, "Other", "OB", 2, null);

        var project = TiaProjectTest.Opened(file);

        Item(project, "First").Block!.EventClass.Should().BeNull();
        Item(project, "First").IsProgramCycle.Should().BeTrue();
        Item(project, "Other").IsProgramCycle.Should().BeFalse();
    }

    [Test]
    public void AProgramCycleObInAMasterCopyCarriesNoMark() {
        var file = File();
        Code(file, 10, "Copied main", "OB", 1, "OB.ProgramCycle", inMasterCopy: true);

        var copied = Item(TiaProjectTest.Opened(file), "Copied main");

        copied.IsProject.Should().BeFalse();
        copied.IsProgramCycle.Should().BeFalse();
    }

    [Test]
    public void AClosedProgramCycleObCarriesTheMarkAsWellAsTheLock() {
        var file = File();
        Code(file, 10, "Main", "OB", 1, "OB.ProgramCycle", closed: true);

        var main = Item(TiaProjectTest.Opened(file), "Main");

        main.IsClosed.Should().BeTrue();
        main.IsProgramCycle.Should().BeTrue();
    }

    [Test]
    public void OnlyAnObCanBeAProgramCycleOb() {
        var file = File();
        Code(file, 10, "Function", "FC", 1, "OB.ProgramCycle");

        var function = Item(TiaProjectTest.Opened(file), "Function");

        function.Block!.EventClass.Should().BeNull();
        function.IsProgramCycle.Should().BeFalse();
    }

    // Show only blocks of chosen kinds ----------------------------------------------------------------------------------

    private static TiaProject Mixed() {
        var file = File();
        file.WriteObject(UserType, 20, [Core("Recipe", "UDT"), TiaFileBuilder.Slots((TypeEnvironment, ProjectType, 1))]);
        file.WriteObject(DataBlock, 21, [Core("LineData", "DB"), General("DB", 1), TiaFileBuilder.Slots((DataEnvironment, ProjectType, 1))]);
        Code(file, 22, "Scale", "FC", 1, "FC");
        Code(file, 23, "System call", "SFC", 20, "SFC");
        Code(file, 24, "Filler", "FB", 1, "FB");
        Code(file, 25, "Main", "OB", 1, "OB.ProgramCycle");
        return TiaProjectTest.Opened(file);
    }

    [Test]
    public void TheTreeOffersOneSwitchPerKindItHolds_InTiasOrder() {
        Mixed().Kinds.Should().Equal("OB", "FB", "FC", "DB", "UDT", "SFC");
    }

    [Test]
    public void TagTablesAndTheOpcUaEntryComeAfterTheBlocks() {
        var kinds = TiaProjectTest.Opened(TiaOpcUaFixture.NewFile()).Kinds;

        kinds.Should().EndWith(["Tags", TiaItem.OpcUaReportKind]);
        kinds.Should().Contain("DB");
    }

    [Test]
    public void WithNoKindChosenTheTreeHoldsEveryKind() {
        Mixed().Items.Should().OnlyContain(item => item.Matches("", Array.Empty<string>()));
    }

    [Test]
    public void WithOneKindChosenTheTreeHoldsOnlyThatKind() {
        Mixed().Items.Where(item => item.Matches("", ["OB"])).Select(item => item.Name).Should().Equal("Main");
    }

    [Test]
    public void WithSeveralKindsChosenTheTreeHoldsAnyOfThem() {
        Mixed().Items.Where(item => item.Matches("", ["DB", "UDT"])).Select(item => item.Name).Should().BeEquivalentTo(["LineData", "Recipe"]);
    }

    [Test]
    public void AKindAndANameBothApply() {
        var shown = Mixed().Items.Where(item => item.Matches("l", ["FB", "FC"])).Select(item => item.Name);

        shown.Should().BeEquivalentTo(["Scale", "Filler"]);
        Mixed().Items.Where(item => item.Matches("SCALE", ["FB"])).Should().BeEmpty();
    }
}
