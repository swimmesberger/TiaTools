using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0013 on files built here: a technology object of any kind is read by the same rules, and one whose interface is
/// stored encrypted stays closed. The reference project TechnologyObjects checks the kinds TIA can build against TIA.
/// </summary>
public sealed class TiaTechnologyObjectTest {
    private const uint TechnologyObject = 0x00221002;
    private const uint Template = 0x00221003;
    private const uint Load = 0x00221004;
    private const uint Root = 0x00221005;
    private const uint Current = 0x00222001;
    private const uint Used = 0x00222002;
    private const uint Loadables = 0x00222003;
    private const uint Generated = 0x00222004;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Structures">
        <AttributeSet name="IInterfacePartData" id="0x2" persistent="true">
          <Attribute name="Payload" id="0" type="pe:XmlT"/><Attribute name="Kind" id="1" type="xs:string"/>
        </AttributeSet>
        <ObjectType name="InterfaceVersionRootData" id="0x00221005">
          <Implements ref="Siemens.Simatic.Lang.Model.Structures.IInterfacePartData"/>
          <Relation name="UsedParts" id="0x00222002" cardinality="*"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <AttributeSet name="ILoadablePlusDataBlockData" id="0x3" persistent="true"><Attribute name="VolatileBuffer" id="0" type="pe:BlobT"/></AttributeSet>
        <ObjectType name="LoadablePlusDataBlockData" id="0x00221004"><Implements ref="Siemens.Simatic.Lang.Model.Blocks.ILoadablePlusDataBlockData"/></ObjectType>
        <ObjectType name="FunctionBlockTemplateData" id="0x00221003"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        <ObjectType name="TechnologicalDataBlockData" id="0x00221002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="CurrentInterface" id="0x00222001" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
          <Relation name="LoadableBinaries" id="0x00222003" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.LoadablePlusDataBlockData"/></Relation>
          <Relation name="GeneratedFromMaster" id="0x00222004" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.FunctionBlockTemplateData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    /// <summary>A type with two static members: an LReal at bit 0 and a DInt at bit 64, in a volatile area of 96 bits.</summary>
    private const string TypeRoot = """
        <Root><Member ID="5" Name="Static" SubPartIndex="0"><Member ID="51" Name="Gain" RID="0x02000030" /><Member ID="52" Name="Limit" RID="0x02000007" /><Offsets><o o="0" /><o o="64" /></Offsets></Member><Offsets volSize="96" /></Root>
        """;

    private static byte[] Part(string xml, string kind) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(System.Text.Encoding.UTF8.GetBytes(xml), compress: false)), (8, TiaFileBuilder.String(kind)));

    private static byte[] Core(string name, string subtype, bool isProtected = false) {
        var core = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));
        core[12] = isProtected ? (byte)1 : (byte)0;
        return core;
    }

    /// <summary>A technology object of a kind no reference project holds, with one value set on it and an image for the rest.</summary>
    private static TiaProgram Build(string interfacePayload, bool isProtected = false) {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Template, 9, [Core("TO_Kinematics", "FBT")]);
        // Gain 1.5 and Limit 42 in the image, which has no head: it is exactly the volatile area's 12 bytes.
        byte[] image = [.. BitConverter.GetBytes(1.5), .. BitConverter.GetBytes(42)];
        file.WriteObject(Load, 3, [TiaFileBuilder.Segment(4, (4, TiaFileBuilder.Blob(image, compress: false)))]);
        file.WriteObject(Root, 5, [Part(TypeRoot, "BlockSource")]);
        file.WriteObject(Root, 4, [Part(interfacePayload, "Values"), TiaFileBuilder.Slots(), TiaFileBuilder.Slots((Used, Root, 5))], info: 1);
        file.WriteObject(TechnologyObject, 1, [Core("Arm", "DB.TO.Kinematics", isProtected), TiaFileBuilder.Slots((Current, Root, 4), (Loadables, Load, 3), (Generated, Template, 9))]);
        file.Commit();
        return new TiaProgram(TiaDatabase.Parse(file.ToArray()));
    }

    private static string Reference => ReferenceProjects.Archive("TechnologyObjects");

    [Test]
    public void ASystemProtectedTechnologyObjectShowsItsParameters() {
        var program = new TiaProgram(TiaDatabase.Open(Reference));
        var level = program.TechnologyObjects.Single(candidate => candidate.Name == "Level");

        level.IsKnowHowProtected.Should().BeTrue("TIA marks a PID_Compact as protected by Siemens");
        program.ReadParameters(level).Should().Contain(new TiaTechnologyParameter("Config.InputUpperLimit", "Real", "150.0"));
    }

    [Test]
    public void ATechnologyObjectIsDumpedToAFileOfItsOwnAndListedInTheIndex() {
        var folder = ReferenceProjects.Dump("TechnologyObjects");
        try {
            var row = File.ReadLines(Path.Combine(folder, "index.tsv")).Select(line => line.Split('\t')).Single(columns => columns[8] == "Feed");
            var text = File.ReadAllText(Path.Combine(folder, row[7]));

            row[3..6].Should().Equal("TO", "2", "TO_PositioningAxis");
            text.Should().Contain("TECHNOLOGY_OBJECT \"Feed\" : TO_PositioningAxis\n{ VERSION := '8.0'; NUMBER := 2 }\n");
            text.Should().Contain("   Mechanics.LeadScrew : LReal := 12.5;\n");
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void TheTreeShowsATechnologyObjectInItsFolderUnderTechnologyObjects() {
        var project = TiaProject.Open(Reference);

        var plc = project.Root.Children.Single(node => node.Name == "PLC_Motion");
        var axes = plc.Children.Single(node => node.Name == "Technology objects").Children.Single(node => node.Name == "Axes");
        axes.Children.Select(node => node.Name).Should().Equal("Feed", "Follower", "Spindle", "Wheel");
        axes.Children[0].Item!.Kind.Should().Be(TiaItem.TechnologyObjectKind);
    }

    [Test]
    public void SearchFindsATechnologyObject() {
        var project = TiaProject.Open(Reference);

        project.Search("\"Follower\"").Should().Contain(result => result.Item.TechnologyObject != null && result.Item.Name == "Follower");
    }

    [Test]
    public void ANameInCodeResolvesToTheTechnologyObject() {
        var project = TiaProject.Open(Reference);
        project.BuildIndex();
        var watch = project.Items.Single(item => item.Name == "Watch");

        var spindle = project.Resolve(watch, "Spindle")!.Value.Item;

        spindle.TechnologyObject!.Kind.Should().Be("TO_SpeedAxis");
        project.ReferencesTo(spindle).Should().Contain(reference => reference.From == watch);
    }

    [Test]
    public void ATechnologyObjectIsListedApartFromTheBlocks() {
        var program = Build("""<Values><V p="51" v="2.5" /></Values>""");

        program.TechnologyObjects.Should().ContainSingle().Which.Name.Should().Be("Arm");
        program.Blocks.Should().BeEmpty();
    }

    [Test]
    public void ATechnologyObjectOfAnUncheckedKindIsShownByTheSameRules() {
        var program = Build("""<Values><V p="51" v="2.5" /></Values>""");
        var arm = program.TechnologyObjects.Single();

        arm.Kind.Should().Be("TO_Kinematics");
        program.ReadParameters(arm).Should().Equal(
            new TiaTechnologyParameter("Gain", "LReal", "2.5"),
            new TiaTechnologyParameter("Limit", "DInt", "42"));
    }

    [Test]
    public void AnEncryptedPartOfATechnologyObjectStaysClosed() {
        var program = Build("""<EncryptedData Type="http://www.w3.org/2001/04/xmlenc#Element"><CipherData><CipherValue>AAAA</CipherValue></CipherData></EncryptedData>""", isProtected: true);
        var arm = program.TechnologyObjects.Single();
        using var output = new PooledByteWriter();

        TiaSourceWriter.WriteTechnologyObject(program, arm, output);

        program.ReadParameters(arm).Should().BeEmpty();
        System.Text.Encoding.UTF8.GetString(output.WrittenSpan).Should().Contain(System.Text.Encoding.UTF8.GetString(TiaSourceWriter.ProtectedMarker));
    }
}
