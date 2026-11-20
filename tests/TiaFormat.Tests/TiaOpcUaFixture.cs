using System.Buffers.Binary;
using System.Text;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// An invented project for spec 0004 and 0005: the PLC <c>Filler</c> with an OPC UA server, roles, data blocks and a
/// tag table, and the PLC <c>Capper</c> with nothing of OPC UA. Kept free of the test framework, so a tool can build it too.
/// </summary>
internal static class TiaOpcUaFixture {
    internal const uint Project = 0x00230001;
    internal const uint Plc = 0x00230002;
    internal const uint DeviceItem = 0x00230003;
    internal const uint Storage = 0x00230004;
    internal const uint Interface = 0x00230005;
    internal const uint DataBlock = 0x00230006;
    internal const uint Root = 0x00230007;
    internal const uint TagTable = 0x00230008;
    internal const uint Tag = 0x00230009;
    internal const uint ProjectUser = 0x0023000A;
    internal const uint MasterCopy = 0x0023000B;

    internal const uint PlcEnvironment = 0x00232001;
    internal const uint ItemTarget = 0x00232002;
    internal const uint StorageTarget = 0x00232003;
    internal const uint InterfaceTarget = 0x00232004;
    internal const uint DbInterface = 0x00232005;
    internal const uint DbTarget = 0x00232006;
    internal const uint DbEnvironment = 0x00232007;
    internal const uint Content = 0x00232008;
    internal const uint TableTarget = 0x00232009;
    internal const uint TableEnvironment = 0x0023200A;
    internal const uint IsGeneratedFrom = 0x0023200B;

    internal const uint BooleanType = 0x80000001;
    internal const uint DateTimeType = 0x8000000A;
    internal const uint StringType = 0x8000000B;
    internal const uint BlobType = 0x8000000D;
    internal const uint UIntType = 0x80000013;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        <AttributeSet name="ICoreExpandoAttributes" id="0x2" persistent="true" expando="true" />
        </Namespace><Namespace name="Siemens.Automation.DomainModel">
        <AttributeSet name="ITagAddress" id="0x3" persistent="true"><Attribute name="LogicalAddress" id="0" type="xs:string"/></AttributeSet>
        <ObjectType name="ProjectData" id="0x00230001"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        <ObjectType name="LibraryMasterCopyData" id="0x0023000B"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        <ObjectType name="EAMTZTagData" id="0x00230009">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.DomainModel.ITagAddress"/>
          <Implements ref="Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem"/>
          <Implements ref="Siemens.Automation.DomainServices.CommonTypeSystem.IDefaultStrategyData"/>
        </ObjectType>
        <ObjectType name="EAMTZTagTableData" id="0x00230008">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Content" id="0x00232008" cardinality="*"><Target ref="Siemens.Automation.DomainModel.EAMTZTagData"/></Relation>
          <Relation name="Target" id="0x00232009" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
          <Relation name="Environment" id="0x0023200A" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Automation.DomainServices.CommonTypeSystem">
        <AttributeSet name="IStructureItem" id="0x4" persistent="true">
          <Attribute name="DisplayTypeName" id="0" type="xs:string"/><Attribute name="HmiStructureTypeRelevant" id="1" type="xs:boolean"/>
          <Attribute name="HmiVisible" id="2" type="xs:boolean"/><Attribute name="HmiReadOnly" id="3" type="xs:boolean"/>
        </AttributeSet>
        <AttributeSet name="IDefaultStrategyData" id="0x5" persistent="true"><Attribute name="DefaultValue" id="0" type="xs:string"/></AttributeSet>
        </Namespace><Namespace name="Siemens.Automation.Umac.Model.UserManagement.Administration">
        <ObjectType name="CustomProjectUser" id="0x0023000A">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.HwConfiguration.Model">
        <AttributeSet name="IConfigBaseData" id="0x6" persistent="true"><Attribute name="ConfigObjectTypeName" id="0" type="xs:string"/></AttributeSet>
        <ObjectType name="S7ControllerTargetData" id="0x00230002">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Relation name="Environment" id="0x00232001" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        <ObjectType name="DeviceItemData" id="0x00230003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Implements ref="Siemens.Simatic.HwConfiguration.Model.IConfigBaseData"/>
          <Relation name="Target" id="0x00232002" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.OpcUa.Model">
        <ObjectType name="OpcUaServerConfigurationData" id="0x00230004">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Relation name="Target" id="0x00232003" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
        </ObjectType>
        <ObjectType name="OpcUaInterfaceData" id="0x00230005">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Target" id="0x00232004" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Structures">
        <AttributeSet name="IInterfacePartData" id="0x7" persistent="true">
          <Attribute name="Payload" id="0" type="pe:XmlT"/><Attribute name="Kind" id="1" type="xs:string"/>
        </AttributeSet>
        <ObjectType name="InterfaceVersionRootData" id="0x00230007">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Structures.IInterfacePartData"/>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <AttributeSet name="IGeneralBlockSourceData" id="0x8" persistent="true">
          <Attribute name="BlockType" id="0" type="xs:string"/><Attribute name="BlockLanguage" id="1" type="xs:string"/><Attribute name="OnlySymbolicAccess" id="2" type="xs:boolean"/>
        </AttributeSet>
        <ObjectType name="DataBlockData" id="0x00230006">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreExpandoAttributes"/>
          <Implements ref="Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData"/>
          <Relation name="CurrentInterface" id="0x00232005" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Structures.InterfaceVersionRootData"/></Relation>
          <Relation name="Target" id="0x00232006" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData"/></Relation>
          <Relation name="Environment" id="0x00232007" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
          <Relation name="IsGeneratedFrom" id="0x0023200B" cardinality="1"><Target ref="Siemens.Simatic.Lang.Model.Blocks.DataBlockData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    /// <summary>Expando keys of each object type, as one key table per type.</summary>
    private static readonly (uint Type, ulong Table, (uint Key, string Name, uint ValueType)[] Keys)[] KeyTables = [
        (DeviceItem, 801, [
            (1, "OpcUaEnableServer", BooleanType), (2, "ApplicationName", StringType), (3, "EnableSecurityPolicies_None", BooleanType),
            (4, "EnableSecurityPolicies_256SHA_SE", BooleanType), (5, "AutoAcceptClient", BooleanType), (6, "MinimumPublishingInterval", UIntType),
            (7, "ServerCertificateId", StringType), (8, "TrustedClientCertificatesIds", StringType), (9, "EncryptedPassword1", BlobType),
            (10, "SamplingQueue", UIntType), (11, "OpcUaPortNumber", UIntType), (12, "OpcUaEnableGuestAuthentication", BooleanType),
            (13, "OpcUaEnablePasswordAuthentication", BooleanType), (14, "EnableStandardAddressSpace", BooleanType),
        ]),
        (Storage, 802, [
            (1, "UsersAndRolesStorage", BlobType), (2, "LastCompiled", DateTimeType), (3, "LastDownloaded", DateTimeType),
            (4, "OPCUA.Expando.StructureJournalChanges", StringType), (5, "OPCUA.Expando.MappingJournalChanges", StringType),
        ]),
        (DataBlock, 803, [(1, "DBAccessibleFromOPCUA", BooleanType)]),
        (Plc, 804, [(1, "MinimumSamplingInterval", UIntType)]),
    ];

    internal static readonly DateTime Compiled = new(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);

    // Builders -------------------------------------------------------------------------------------------------------

    /// <summary>ICoreAttributes: Name and Subtype by reference, then IsKnowHowProtected.</summary>
    private static byte[] Core(string name, string subtype = "", bool isProtected = false) {
        var segment = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));
        segment[12] = isProtected ? (byte)1 : (byte)0;
        return segment;
    }

    private static byte[] OneString(string value) => TiaFileBuilder.Segment(4, (4, TiaFileBuilder.String(value)));

    /// <summary>A dictionary expando segment: count at 8, keys at 18, values after them, then what they refer to.</summary>
    private static byte[] Expando(params (uint Key, uint? Value, byte[]? Heap)[] entries) {
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

    private static (uint, uint?, byte[]?) Flag(uint key, bool value) => (key, value ? 1u : 0u, null);

    private static (uint, uint?, byte[]?) Text(uint key, string value) => (key, null, TiaFileBuilder.String(value));

    private static (uint, uint?, byte[]?) Date(uint key, DateTime value) {
        var ticks = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(ticks, value.Ticks);
        return (key, null, ticks);
    }

    private static byte[] U32(uint value) {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] KeyTable((uint Key, string Name, uint ValueType)[] keys) {
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

    private static byte[] Mapping() {
        var table = new MemoryStream();
        table.Write(U32((uint)KeyTables.Length));
        foreach (var (type, id, _) in KeyTables) {
            table.Write(U32(type));
            table.Write(U32(0x70011));
            var wide = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(wide, id);
            table.Write(wide);
        }
        return table.ToArray();
    }

    /// <summary>A .NET binary serialization stream of an OPC UA roles container, as TIA stores it, with a 5 byte head.</summary>
    internal static byte[] RolesContainer() {
        var s = new MemoryStream();
        void Byte(byte value) => s.WriteByte(value);
        void Int(int value) => s.Write(U32((uint)value));
        void Name(string text) {
            var utf8 = Encoding.UTF8.GetBytes(text);
            s.Write(TiaFileBuilder.VarInt(utf8.Length));
            s.Write(utf8);
        }
        void Strings(params string[] names) {
            Int(names.Length);
            foreach (var name in names) {
                Name(name);
            }
        }

        s.Write([1, 0, 0, 0, 0]);
        Byte(0); Int(1); Int(-1); Int(1); Int(0);
        Byte(12); Int(2); Name("Siemens.Simatic.OpcUa.Bl");
        // The container: its roles list by reference, no role map.
        Byte(5); Int(1); Name("Siemens.Simatic.OpcUa.BusinessLogic.UsersAndRolesStorageContainer");
        Strings("<Roles>k__BackingField", "<RolesMap>k__BackingField");
        Byte(2); Byte(2); Int(2);
        Byte(9); Int(3);
        Byte(10);
        // List<UaRoleMappingInfo>: items, size, version.
        Byte(4); Int(3); Name("System.Collections.Generic.List`1[[Siemens.Simatic.OpcUa.Base.Types.UaRoleMappingInfo]]");
        Strings("_items", "_size", "_version");
        Byte(2); Byte(0); Byte(0); Byte(8); Byte(8);
        Byte(9); Int(4);
        Int(2); Int(2);
        Byte(16); Int(4); Int(3);
        // The first role, with its class described.
        Byte(5); Int(5); Name("Siemens.Simatic.OpcUa.Base.Types.UaRoleMappingInfo");
        Strings("<RoleName>k__BackingField", "<ProjectRoleName>k__BackingField", "<IsImportedRole>k__BackingField", "<RolePermissions>k__BackingField");
        Byte(1); Byte(1); Byte(0); Byte(2); Byte(1); Int(2);
        Byte(6); Int(6); Name("Operator");
        Byte(6); Int(7); Name("Line operators");
        Byte(1);
        Byte(16); Int(8); Int(1);
        Byte(5); Int(9); Name("Siemens.Simatic.OpcUa.Base.Types.UaNamespacePermissionInfo");
        Strings("<NamespaceUri>k__BackingField", "<Permissions>k__BackingField");
        Byte(1); Byte(0); Byte(8); Int(2);
        Byte(6); Int(10); Name("http://www.siemens.com/simatic-s7-opcua");
        Int(35);
        // The second role reuses the first one's class.
        Byte(1); Int(11); Int(5);
        Byte(6); Int(12); Name("Viewer");
        Byte(10);
        Byte(0);
        Byte(10);
        // The list's spare capacity.
        Byte(13); Byte(1);
        Byte(11);
        return s.ToArray();
    }

    /// <summary>IStructureItem: DisplayTypeName by reference, then accessible, visible and read-only flags.</summary>
    private static byte[] StructureItem(string type, bool accessible, bool visible, bool readOnly) {
        var segment = TiaFileBuilder.Segment(7, (4, TiaFileBuilder.String(type)));
        segment[8] = accessible ? (byte)1 : (byte)0;
        segment[9] = visible ? (byte)1 : (byte)0;
        segment[10] = readOnly ? (byte)1 : (byte)0;
        return segment;
    }

    private static byte[] Part(string xml) =>
        TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(xml), compress: false)), (8, TiaFileBuilder.String("DBSource")));

    internal sealed class Options {
        public bool HasItem { get; init; } = true;
        public bool StoresEnable { get; init; } = true;
        public bool HasInterface { get; init; }
        public bool HasUser { get; init; }
        public byte[]? Roles { get; init; }

        /// <summary>Also a master copy holding the PLC <c>Copied</c> with one data block.</summary>
        public bool HasMasterCopy { get; init; }

        /// <summary>
        /// Also, for the node list: the UDT <c>Dose</c>, the FBs <c>Valve</c> and <c>Line</c>, the global data blocks
        /// <c>Doses</c>, <c>Sealed</c> (every member closed) and <c>Locked</c> (know-how protected), and <c>LineData</c>,
        /// an instance of <c>Line</c>.
        /// </summary>
        public bool HasTypes { get; init; }
    }

    /// <summary>
    /// A project with the PLC <c>Filler</c>: an OPC UA device item, a server storage, three data blocks and a tag
    /// table; and the PLC <c>Capper</c> with nothing of OPC UA.
    /// </summary>
    internal static TiaDatabase NewProject(Options? options = null) {
        var file = NewFile(options);
        file.Commit();
        return TiaDatabase.Parse(file.ToArray());
    }

    /// <summary>The project's file, not yet committed.</summary>
    internal static TiaFileBuilder NewFile(Options? options = null) {
        options ??= new Options();
        var file = new TiaFileBuilder(Schema);
        file.WriteSystem(0x70002, 800, Mapping());
        foreach (var (_, id, keys) in KeyTables) {
            file.WriteSystem(0x70011, id, KeyTable(keys));
        }
        file.WriteObject(Project, 1, [Core("Filling line")]);
        file.WriteObject(Plc, 10, [Core("Filler"), Expando((1, 100, null)), TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);
        file.WriteObject(Plc, 11, [Core("Capper"), null, TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);

        if (options.HasItem) {
            var settings = new List<(uint, uint?, byte[]?)> {
                Text(2, "Filler.OPC-UA"), Flag(3, false), Flag(4, true), Flag(5, false), (6, 250, null), Text(7, "3"), Text(8, "4,5"),
                (9, null, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes("not to be seen"), compress: false)), (10, 7, null),
                (11, 4841, null), Flag(12, false), Flag(13, true), Flag(14, false),
            };
            if (options.StoresEnable) {
                settings.Insert(0, Flag(1, true));
            }
            file.WriteObject(DeviceItem, 20, [
                Core("Server", "S71500.CPU.OPC_UA"), Expando([.. settings]), OneString(@"S71500\Submodules\CPU\OPC_UA\MDD_OpcUa_9.9"),
                TiaFileBuilder.Slots((ItemTarget, Plc, 10)),
            ]);
        }
        file.WriteObject(Storage, 30, [
            Core("Storage", "OpcUa.ServerStorage"),
            Expando(
                (1, null, TiaFileBuilder.Blob(options.Roles ?? RolesContainer(), compress: false)), Date(2, Compiled), (3, uint.MaxValue, null),
                Text(4, "[]"), Text(5, "[]")),
            TiaFileBuilder.Slots((StorageTarget, Plc, 10)),
        ]);
        if (options.HasInterface) {
            file.WriteObject(Interface, 31, [Core("Line status"), TiaFileBuilder.Slots((InterfaceTarget, Plc, 10))]);
        }
        if (options.HasUser) {
            file.WriteObject(ProjectUser, 32, [Core("operator.one")]);
        }

        DataBlockObject(file, 40, "Recipe", null, """
            <Root><Member ID="2" Name="Static">
              <Member ID="51" Name="Speed" RID="0x02000001" />
              <Member ID="52" Name="Internal" RID="0x02000001" MFlags="14" />
              <Member ID="53" Name="Limit" RID="0x02000001" MFlags="79" />
              <Member ID="54" Name="Hidden" MFlags="14"><Member ID="55" Name="Inside" RID="0x02000001" /></Member>
              <Member ID="56" Name="Display" RID="0x02000001" MFlags="77" />
              <Member ID="57" Name="Unlisted" RID="0x02000001" MFlags="13" />
            </Member></Root>
            """);
        if (options.HasMasterCopy) {
            file.WriteObject(MasterCopy, 2, [Core("Line template")]);
            file.WriteObject(Plc, 12, [Core("Copied"), null, TiaFileBuilder.Slots((PlcEnvironment, MasterCopy, 2))]);
            file.WriteObject(Root, 142, [null, Part("""<Root><Member ID="2" Name="Static"><Member ID="51" Name="Value" RID="0x02000001" /></Member></Root>""")]);
            file.WriteObject(DataBlock, 42, [
                Core("Template", "DB"), null, TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("DB")), (8, TiaFileBuilder.String("DB"))),
                TiaFileBuilder.Slots((DbInterface, Root, 142), (DbTarget, Plc, 12), (DbEnvironment, MasterCopy, 2)),
            ]);
        }
        DataBlockObject(file, 41, "Service", false, """<Root><Member ID="2" Name="Static"><Member ID="51" Name="Code" RID="0x02000001" /></Member></Root>""");
        if (options.HasTypes) {
            Types(file);
        }

        file.WriteObject(Tag, 60, TagObject("Start", "%I0.0", null, accessible: true, visible: true, readOnly: false));
        file.WriteObject(Tag, 61, TagObject("Level", "%IW2", null, accessible: true, visible: true, readOnly: true));
        file.WriteObject(Tag, 62, TagObject("Spare", "%M9.0", null, accessible: false, visible: false, readOnly: false));
        file.WriteObject(Tag, 63, TagObject("MaxBottles", "", "10", accessible: true, visible: true, readOnly: false));
        file.WriteObject(TagTable, 70, [
            Core("Line tags"),
            TiaFileBuilder.Slots((Content, Tag, 60), (Content, Tag, 61), (Content, Tag, 62), (Content, Tag, 63), (TableTarget, Plc, 10), (TableEnvironment, Project, 1)),
        ]);
        return file;
    }

    /// <summary>A tag's segments in interface order: address, default value, structure item, core attributes.</summary>
    private static byte[]?[] TagObject(string name, string address, string? value, bool accessible, bool visible, bool readOnly) =>
        [OneString(address), value is null ? null : OneString(value), StructureItem("Bool", accessible, visible, readOnly), Core(name)];

    private static void DataBlockObject(TiaFileBuilder file, ulong id, string name, bool? openToOpcUa, string xml, string kind = "DB", ulong? generatedFrom = null, bool isProtected = false) {
        file.WriteObject(Root, id + 100, [null, Part(xml)]);
        var general = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(kind)), (8, TiaFileBuilder.String(kind)));
        var slots = new List<(uint, uint, ulong)> { (DbInterface, Root, id + 100), (DbTarget, Plc, 10), (DbEnvironment, Project, 1) };
        if (generatedFrom is { } type) {
            slots.Add((IsGeneratedFrom, DataBlock, type));
        }
        file.WriteObject(DataBlock, id, [
            Core(name, kind, isProtected),
            openToOpcUa is { } open ? Expando(Flag(1, open)) : null,
            general,
            TiaFileBuilder.Slots([.. slots]),
        ]);
    }

    /// <summary>The blocks of <see cref="Options.HasTypes"/>.</summary>
    private static void Types(TiaFileBuilder file) {
        DataBlockObject(file, 43, "Dose", null, """
            <Root>
              <Member ID="51" Name="volume" RID="0x02000008" />
              <Member ID="52" Name="code" RID="0x02000002" />
            </Root>
            """, kind: "UDT");
        DataBlockObject(file, 44, "Valve", null, """
            <Root>
              <Member ID="2" Name="Input"><Member ID="51" Name="open" RID="0x02000001" /></Member>
              <Member ID="3" Name="Output"><Member ID="52" Name="opened" RID="0x02000001" /></Member>
              <Member ID="4" Name="Static"><Member ID="53" Name="moveTimer" Type="TON_TIME" /></Member>
              <Member ID="5" Name="Temp"><Member ID="54" Name="scratch" RID="0x02000001" /></Member>
            </Root>
            """, kind: "FB");
        DataBlockObject(file, 45, "Line", null, """
            <Root>
              <Member ID="2" Name="Input"><Member ID="51" Name="start" RID="0x02000001" /></Member>
              <Member ID="4" Name="Static"><Member ID="52" Name="valve" Type="&quot;Valve&quot;" /></Member>
            </Root>
            """, kind: "FB");
        DataBlockObject(file, 46, "Doses", null, """
            <Root><Member ID="2" Name="Static">
              <Member ID="51" Name="active" Type="&quot;Dose&quot;" />
              <Member ID="52" Name="list" Type="Array[1..2] of &quot;Dose&quot;" />
              <Member ID="53" Name="stamp" RID="0x02000043" />
              <Member ID="54" Name="limits" RID="0x91010000" SubPartIndex="9">
                <Member ID="55" Name="low" RID="0x02000005" />
                <Member ID="56" Name="high" RID="0x02000005" MFlags="79" />
              </Member>
              <Member ID="57" Name="grid" Type="Array[1..2, 0..1] of Int" />
            </Member></Root>
            """);
        DataBlockObject(file, 47, "Sealed", null, """<Root><Member ID="2" Name="Static"><Member ID="51" Name="Secret" RID="0x02000001" MFlags="14" /></Member></Root>""");
        DataBlockObject(file, 48, "Locked", null, """<Root><Member ID="2" Name="Static"><Member ID="51" Name="Inside" RID="0x02000001" /></Member></Root>""", isProtected: true);
        DataBlockObject(file, 49, "LineData", null, """<Root><Member ID="2" Name="Static" /></Root>""", generatedFrom: 45);
    }
}
