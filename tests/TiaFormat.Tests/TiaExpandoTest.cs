using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Dictionary expandos: values with no place in the schema, named through the object type's key table.</summary>
public sealed class TiaExpandoTest {
    private const uint UserType = 0x00221003;
    private const uint DataBlock = 0x00221002;
    private const uint Root = 0x00221005;
    private const uint UdtInterface = 0x00222004;
    private const uint DbInterface = 0x00222003;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/>
        </AttributeSet>
        <AttributeSet name="ICoreExpandoAttributes" id="0x6" persistent="true" expando="true" />
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Structures">
        <AttributeSet name="IInterfacePartData" id="0x2" persistent="true">
          <Attribute name="Payload" id="0" type="pe:XmlT"/><Attribute name="Kind" id="1" type="xs:string"/>
        </AttributeSet>
        <ObjectType name="InterfaceVersionRootData" id="0x00221005">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Structures.IInterfacePartData"/>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <AttributeSet name="IGeneralBlockSourceData" id="0x3" persistent="true">
          <Attribute name="BlockType" id="0" type="xs:string"/><Attribute name="BlockLanguage" id="1" type="xs:string"/><Attribute name="OnlySymbolicAccess" id="2" type="xs:boolean"/>
        </AttributeSet>
        <ObjectType name="UserTypeData" id="0x00221003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="CurrentInterface" id="0x00222004" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
        </ObjectType>
        <ObjectType name="DataBlockData" id="0x00221002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Relation name="CurrentInterface" id="0x00222003" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    /// <summary>Key table: 12 bytes, u32 count, then u32 key, varint length of the name alone, name, u32 value type.</summary>
    private static byte[] KeyTable(params (uint Key, string Name, uint Type)[] keys) {
        var table = new MemoryStream();
        table.Write(new byte[12]);
        table.Write(U32((uint)keys.Length));
        foreach (var (key, name, type) in keys) {
            var text = Encoding.UTF8.GetBytes(name);
            table.Write(U32(key));
            table.Write(TiaFileBuilder.VarInt(text.Length));
            table.Write(text);
            table.Write(U32(type));
        }
        return table.ToArray();
    }

    private static byte[] Mapping(params (uint ObjectType, ulong TableId)[] maps) {
        var table = new MemoryStream();
        table.Write(U32((uint)maps.Length));
        foreach (var (objectType, id) in maps) {
            table.Write(U32(objectType));
            table.Write(U32(0x70011));
            var wide = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(wide, id);
            table.Write(wide);
        }
        return table.ToArray();
    }

    /// <summary>Size, 4 bytes, count at 8, keys at 18, values after them, then what the values refer to.</summary>
    private static byte[] Expando(params (uint Key, uint? Value, byte[]? Blob)[] entries) {
        var heap = new MemoryStream();
        var keys = new MemoryStream();
        var values = new MemoryStream();
        foreach (var (key, value, blob) in entries) {
            keys.Write(U32(key));
            values.Write(U32(value ?? (uint)heap.Length));
            if (blob is not null) {
                heap.Write(blob);
            }
        }
        var size = 18 + (int)keys.Length + (int)values.Length + (int)heap.Length;
        var segment = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(segment, size);
        BinaryPrimitives.WriteInt32LittleEndian(segment.AsSpan(8), entries.Length);
        keys.ToArray().CopyTo(segment, 18);
        values.ToArray().CopyTo(segment, 18 + (int)keys.Length);
        heap.ToArray().CopyTo(segment, 18 + (int)keys.Length + (int)values.Length);
        return segment;
    }

    private static byte[] U32(uint value) {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Part(string xml, string kind) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(xml), compress: false)), (8, TiaFileBuilder.String(kind)));

    private static byte[] Core(string name, string subtype) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));

    private static TiaFileBuilder NewFile() {
        var file = new TiaFileBuilder(Schema);
        file.WriteSystem(0x70002, 7, Mapping((Root, 8), (DataBlock, 9)));
        file.WriteSystem(0x70011, 8, KeyTable((1, "ValuesCRC", 0x80000013), (3, "Values", 0x8000000E)));
        file.WriteSystem(0x70011, 9, KeyTable((2, "NonRetain", 0x80000001), (5, "DBAccessibleFromOPCUA", 0x80000001), (6, "DBAccessibleFromWebserver", 0x80000001)));
        return file;
    }

    private static string Written(TiaFileBuilder file, uint type) {
        file.Commit();
        var program = new TiaProgram(TiaDatabase.Parse(file.ToArray()));
        using var output = new PooledByteWriter();
        TiaSourceWriter.Write(program, program.Blocks.Single(block => block.Frame.TypeId == type), output);
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    [Test]
    public void AUserTypeKeepsItsStartValuesInAnExpandoOfItsInterface() {
        var file = NewFile();
        var values = TiaFileBuilder.Blob(Encoding.UTF8.GetBytes("""<Values><V p="51" v="TRUE" /></Values>"""), compress: false);
        file.WriteObject(Root, 5, [
            Expando((1, 77, null), (3, null, values)),
            Part("""<Root><Member ID="51" Name="A" RID="0x02000001" /><Member ID="52" Name="B" RID="0x02000001" /></Root>""", "DataTypeSource"),
        ]);
        file.WriteObject(UserType, 3, [Core("Udt", "UDT"), TiaFileBuilder.Slots((UdtInterface, Root, 5))]);

        Written(file, UserType).Should().Contain("      A : Bool := TRUE;\n      B : Bool;\n");
    }

    private static string DataBlockWith(params (uint Key, uint? Value, byte[]? Blob)[] settings) {
        var file = NewFile();
        file.WriteObject(Root, 5, [null, Part("""<Root><Member ID="51" Name="A" RID="0x02000001" /></Root>""", "DBSource")]);
        var general = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("DB")), (8, TiaFileBuilder.String("DB")));
        general[12] = 1;
        file.WriteObject(DataBlock, 1, [Core("Db", "DB"), Expando(settings), general, TiaFileBuilder.Slots((DbInterface, Root, 5))]);
        return Written(file, DataBlock);
    }

    [Test]
    public void ADataBlockSaysItselfWhetherItIsNonRetain() {
        DataBlockWith((2, 1, null)).Should().Contain("VERSION : 0.1\nNON_RETAIN\n");
        DataBlockWith((2, 0, null)).Should().NotContain("NON_RETAIN");
    }

    [Test]
    public void ADataBlockClosedToOutsideAccessSaysSoInItsAttributes() =>
        DataBlockWith((5, 0, null), (6, 0, null)).Should().Contain(
            "{ DB_Accessible_From_OPC_UA := 'FALSE' ;\n DB_Accessible_From_Webserver := 'FALSE' ;\n S7_Optimized_Access := 'TRUE' }\n");

    [Test]
    public void ADataBlockOpenToOutsideAccessHasOnlyItsUsualAttribute() =>
        DataBlockWith((5, 1, null), (6, 1, null)).Should().Contain("\n{ S7_Optimized_Access := 'TRUE' }\n");
}
