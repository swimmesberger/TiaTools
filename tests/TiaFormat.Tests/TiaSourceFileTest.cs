using System.Text;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>An SCL block comes out as TIA's "Generate source from blocks" writes it, after our own leading comment lines.</summary>
public sealed class TiaSourceFileTest {
    private const uint CodeBlock = 0x00221001;
    private const uint Root = 0x00221005;
    private const uint Unit = 0x00221006;
    private const uint Current = 0x00222001;
    private const uint Sources = 0x00222002;
    private const uint DataBlock = 0x00221002;
    private const uint UserType = 0x00221003;
    private const uint DbInterface = 0x00222003;
    private const uint UdtInterface = 0x00222004;
    private const uint Used = 0x00222005;
    private const uint GeneratedFromUdt = 0x00222006;
    private const uint GeneratedFromFb = 0x00222007;

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
          <Relation name="UsedParts" id="0x00222005" cardinality="*"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <AttributeSet name="IGeneralBlockSourceData" id="0x3" persistent="true">
          <Attribute name="BlockType" id="0" type="xs:string"/><Attribute name="BlockLanguage" id="1" type="xs:string"/><Attribute name="OnlySymbolicAccess" id="2" type="xs:boolean"/>
        </AttributeSet>
        <AttributeSet name="IPlcHeaderData" id="0x4" persistent="true">
          <Attribute name="HeaderAuthor" id="0" type="xs:string"/><Attribute name="HeaderFamily" id="1" type="xs:string"/><Attribute name="HeaderName" id="2" type="xs:string"/><Attribute name="HeaderVersion" id="3" type="xs:string"/>
        </AttributeSet>
        <AttributeSet name="ICompileUnitData" id="0x5" persistent="true">
          <Attribute name="Data" id="0" type="pe:BlobT"/><Attribute name="ProgrammingLanguage" id="1" type="xs:string"/>
        </AttributeSet>
        <ObjectType name="CompileUnitData" id="0x00221006"><Implements ref="Siemens.Simatic.Lang.Model.Blocks.ICompileUnitData"/></ObjectType>
        <ObjectType name="CodeBlockData" id="0x00221001">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IPlcHeaderData"/>
          <Relation name="CurrentInterface" id="0x00222001" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
          <Relation name="Sources" id="0x00222002" cardinality="*"><Target ref="Siemens.Simatic.Lang.Model.Blocks.CompileUnitData"/></Relation>
        </ObjectType>
        <ObjectType name="UserTypeData" id="0x00221003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="CurrentInterface" id="0x00222004" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
        </ObjectType>
        <ObjectType name="DataBlockData" id="0x00221002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IPlcHeaderData"/>
          <Relation name="CurrentInterface" id="0x00222003" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
          <Relation name="GeneratedFromMaster" id="0x00222006" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.UserTypeData"/></Relation>
          <Relation name="IsGeneratedFrom" id="0x00222007" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.CodeBlockData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    private const string Interface = """
        <Root><Member Name="InOut"><Member ID="1" Name="a" Type="Bool" /></Member><Member Name="Temp"><Member ID="2" Name="t" Type="Int" /></Member></Root>
        """;

    private const string Scl = """
        <SCLSource><RootStatements><LC TE=" first" /><NL UId="1" /><NL UId="2" /><LC TE=" last" /><NL UId="3" /></RootStatements></SCLSource>
        """;

    private static string SourceOf(bool optimized, string author = "RN") {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Root, 4, [TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(Interface), compress: false)), (8, TiaFileBuilder.String("Interface")))]);
        file.WriteObject(Unit, 6, [TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(Scl), compress: false)), (8, TiaFileBuilder.String("SCL")))]);
        var general = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("FC")), (8, TiaFileBuilder.String("SCL")));
        general[12] = optimized ? (byte)1 : (byte)0;
        file.WriteObject(CodeBlock, 1, [
            TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("Fn")), (8, TiaFileBuilder.String("FC"))),
            general,
            TiaFileBuilder.Segment(16, (4, TiaFileBuilder.String(author)), (8, TiaFileBuilder.String("FAM")), (12, TiaFileBuilder.String("NM")), (16, TiaFileBuilder.String("2.0"))),
            TiaFileBuilder.Slots((Current, Root, 4)),
            TiaFileBuilder.Targets((Unit, 6)),
        ]);
        file.Commit();

        var program = new TiaProgram(TiaDatabase.Parse(file.ToArray()));
        using var output = new PooledByteWriter();
        TiaSourceWriter.Write(program, program.Blocks.Single(), output);
        var text = Encoding.UTF8.GetString(output.WrittenSpan);
        return text[text.IndexOf("FUNCTION", StringComparison.Ordinal)..];
    }

    private static byte[] Part(string xml, string kind) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(xml), compress: false)), (8, TiaFileBuilder.String(kind)));

    private static byte[] Core(string name, string subtype) =>
        TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));

    private static byte[] General(string blockType, string language) {
        var general = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(blockType)), (8, TiaFileBuilder.String(language)));
        general[12] = 1;
        return general;
    }

    private static byte[] NoHeader() =>
        TiaFileBuilder.Segment(16, (4, TiaFileBuilder.String("")), (8, TiaFileBuilder.String("")), (12, TiaFileBuilder.String("")), (16, TiaFileBuilder.String("")));

    private static string Written(TiaFileBuilder file, uint type) {
        file.Commit();
        var program = new TiaProgram(TiaDatabase.Parse(file.ToArray()));
        using var output = new PooledByteWriter();
        TiaSourceWriter.Write(program, program.Blocks.Single(block => block.Frame.TypeId == type), output);
        var text = Encoding.UTF8.GetString(output.WrittenSpan);
        while (text.StartsWith("// ", StringComparison.Ordinal)) {
            text = text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        }
        return text;
    }

    private const string TypeMembers = """
        <Root><Member ID="51" Name="A" Type="Bool" /><Member ID="53" Name="S" Type="Struct"><Member ID="1" Name="x" Type="Bool" /><Member ID="2" Name="y" Type="Array[1..2] of Int" /></Member></Root>
        """;

    private static string DataBlockOfUserType(string members = TypeMembers) {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Root, 5, [Part(members, "DataTypeSource")]);
        file.WriteObject(UserType, 9, [Core("Udt", "UDT"), TiaFileBuilder.Slots((UdtInterface, Root, 5))]);
        file.WriteObject(Root, 4, [
            Part("""<Values><V p="53:2[2]" v="9" /><V p="53:2[1]" v="8" /><V p="99" v="z" /><V p="53:1" v="TRUE" /><V p="51" v="true" /></Values>""", "Values"),
            TiaFileBuilder.Slots(),
            TiaFileBuilder.Slots((Used, Root, 5)),
        ], info: 1);
        file.WriteObject(DataBlock, 1, [Core("Db", "DB"), General("DB", "DB"), NoHeader(), TiaFileBuilder.Slots((DbInterface, Root, 4), (GeneratedFromUdt, UserType, 9))]);
        return Written(file, DataBlock);
    }

    [Test]
    public void AnSclBlockIsLaidOutAsTiaGeneratesIt() =>
        SourceOf(optimized: false).Should().Be(
            "FUNCTION \"Fn\" : Void\n"
            + "{ S7_Optimized_Access := 'FALSE' }\n"
            + "AUTHOR : RN\nFAMILY : FAM\nNAME : NM\nVERSION : 2.0\n"
            + "   VAR_IN_OUT \n      a : Bool;\n   END_VAR\n\n"
            + "   VAR_TEMP \n      t : Int;\n   END_VAR\n\n\n"
            + "BEGIN\n"
            + "\t// first\n\t\n\t// last\n\t\n"
            + "END_FUNCTION\n\n");

    [Test]
    public void AnOptimizedBlockSaysSo() =>
        SourceOf(optimized: true).Should().Contain("{ S7_Optimized_Access := 'TRUE' }\n");

    [Test]
    public void AnEmptyHeaderFieldIsLeftOut() =>
        SourceOf(optimized: false, author: "").Should().NotContain("AUTHOR").And.Contain("FAMILY : FAM\n");

    [Test]
    public void ABlockCommentFollowsItsSlashesWithoutASpaceOfOurs() {
        using var output = new PooledByteWriter();

        TiaSourceWriter.WriteComment(output, "* history\nsecond");

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("//* history\n//second\n");
    }

    [Test]
    public void ADataBlockOfAUserTypeNamesItAndListsTheValuesSetInItInMemberOrder() =>
        DataBlockOfUserType().Should().StartWith(
            "DATA_BLOCK \"Db\"\n"
            + "{ S7_Optimized_Access := 'TRUE' }\n"
            + "VERSION : 0.1\n"
            + "NON_RETAIN\n"
            + "\"Udt\"\n\n"
            + "BEGIN\n"
            + "   A := true;\n"
            + "   S.x := TRUE;\n"
            + "   S.y[1] := 8;\n"
            + "   S.y[2] := 9;\n"
            + "   // not resolved: 99 := z;\n"
            + "\nEND_DATA_BLOCK\n\n");

    [Test]
    public void TheMembersOfItsTypeFollowTheBlockAsComments() =>
        DataBlockOfUserType().Should().Contain("END_DATA_BLOCK\n\n// The members of its type, with the start values known:\n//   A : Bool");

    [Test]
    public void AMemberThatIsRetainedMeansTheBlockIsNotNonRetain() =>
        DataBlockOfUserType(TypeMembers.Replace("""Name="A" """, """Name="A" Remanence="Retain" """, StringComparison.Ordinal)).Should().NotContain("NON_RETAIN");

    [Test]
    public void AnInstanceDataBlockNamesItsFunctionBlock() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(CodeBlock, 8, [Core("Fb", "FB"), General("FB", "SCL"), NoHeader()]);
        file.WriteObject(Root, 4, [Part("<Values />", "Values")]);
        file.WriteObject(DataBlock, 1, [Core("Fb.IDB", "DB"), General("DB", "DB"), NoHeader(), TiaFileBuilder.Slots((DbInterface, Root, 4), (GeneratedFromFb, CodeBlock, 8))]);

        Written(file, DataBlock).Should().Be(
            "DATA_BLOCK \"Fb.IDB\"\n{ S7_Optimized_Access := 'TRUE' }\nVERSION : 0.1\nNON_RETAIN\n\"Fb\"\n\nBEGIN\n\nEND_DATA_BLOCK\n\n");
    }

    [Test]
    public void AUserTypeWithoutAHeaderHasTheDefaultVersion() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Root, 5, [Part("""<Root><Member ID="51" Name="A" Type="Bool" /></Root>""", "DataTypeSource")]);
        file.WriteObject(UserType, 9, [Core("Udt", "UDT"), TiaFileBuilder.Slots((UdtInterface, Root, 5))]);

        Written(file, UserType).Should().Be("TYPE \"Udt\"\nVERSION : 0.1\n   STRUCT\n      A : Bool;\n   END_STRUCT;\n\nEND_TYPE\n\n");
    }

    [Test]
    public void AnOrganizationBlockLeavesTheSystemsInputsToAComment_AndAnEmptyBodyHasNoLine() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Root, 4, [Part("""<Root><Member Name="Input"><Member ID="1" Name="BlockNr" Type="UInt" /></Member></Root>""", "BlockSource")]);
        file.WriteObject(Unit, 6, [Part("<SCLSource><RootStatements /></SCLSource>", "SCL")]);
        file.WriteObject(CodeBlock, 1, [Core("Ob", "OB"), General("OB", "SCL"), NoHeader(), TiaFileBuilder.Slots((Current, Root, 4)), TiaFileBuilder.Targets((Unit, 6))]);

        Written(file, CodeBlock).Should().Be(
            "ORGANIZATION_BLOCK \"Ob\"\n{ S7_Optimized_Access := 'TRUE' }\nVERSION : 0.1\n\nBEGIN\nEND_ORGANIZATION_BLOCK\n\n"
            + "// The inputs the system provides:\n//   BlockNr : UInt;\n");
    }
}
