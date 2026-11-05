using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Start values may come from a data block's compiled image only where its layout is known.</summary>
public sealed class TiaDataBlockImageTest {
    private const uint DataBlock = 0x00221002;
    private const uint UserType = 0x00221003;
    private const uint CodeBlock = 0x00221001;
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
        <ObjectType name="UserTypeData" id="0x00221003"/>
        <ObjectType name="CodeBlockData" id="0x00221001"/>
        <ObjectType name="DataBlockData" id="0x00221002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="CurrentInterface" id="0x00222001" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
          <Relation name="LoadableBinaries" id="0x00222003" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.LoadablePlusDataBlockData"/></Relation>
          <Relation name="GeneratedFromMaster" id="0x00222004" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.UserTypeData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    private static byte[] Part(string xml) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(System.Text.Encoding.UTF8.GetBytes(xml), compress: false)), (8, TiaFileBuilder.String("Values")));

    private static string? StartValueOf(uint masterType, bool isProtected = false, byte[]? image = null) {
        image ??= [0, 0, 0, 0, 0, 0, 0, 0, 7, 0];
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(masterType, 9, []);
        file.WriteObject(Load, 3, [TiaFileBuilder.Segment(4, (4, TiaFileBuilder.Blob(image, compress: false)))]);
        file.WriteObject(Root, 5, [Part("""<Root><Member ID="51" Name="A" RID="0x02000005" /><Offsets optSize="16"><o o="0" /></Offsets></Root>""")]);
        file.WriteObject(Root, 4, [Part("<Values />"), TiaFileBuilder.Slots(), TiaFileBuilder.Slots((Used, Root, 5))], info: 1);
        var core = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("Db")), (8, TiaFileBuilder.String("DB")));
        core[12] = isProtected ? (byte)1 : (byte)0;
        file.WriteObject(DataBlock, 1, [core, TiaFileBuilder.Slots((Current, Root, 4), (Loadables, Load, 3), (Generated, masterType, 9))]);
        file.Commit();

        var program = new TiaProgram(TiaDatabase.Parse(file.ToArray()));
        return program.ReadInterface(program.Blocks.Single(block => block.Frame.TypeId == DataBlock)).Single().StartValue;
    }

    [Test]
    public void ADataBlockFromAUserType_TakesItsStartValueFromTheImage() =>
        StartValueOf(UserType).Should().Be("7");

    [Test]
    public void AnInstanceDataBlock_DoesNot_BecauseItsOffsetsCountWithinSections() =>
        StartValueOf(CodeBlock).Should().BeNull();

    [Test]
    public void AProtectedDataBlock_DoesNot() =>
        StartValueOf(UserType, isProtected: true).Should().BeNull();

    [Test]
    public void AnImageShorterThanTheOptimizedLayout_IsNotRead() =>
        StartValueOf(UserType, image: [0, 0, 0, 0, 0, 0, 0, 0, 7]).Should().BeNull();
}
