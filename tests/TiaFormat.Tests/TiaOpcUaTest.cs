using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Spec 0004: the OPC UA report, on a small invented project with two PLCs.</summary>
public sealed class TiaOpcUaTest {
    private const uint Project = 0x00230001;
    private const uint Plc = 0x00230002;
    private const uint DeviceItem = 0x00230003;
    private const uint Storage = 0x00230004;
    private const uint Interface = 0x00230005;
    private const uint DataBlock = 0x00230006;
    private const uint Root = 0x00230007;
    private const uint TagTable = 0x00230008;
    private const uint Tag = 0x00230009;
    private const uint ProjectUser = 0x0023000A;

    private const uint PlcEnvironment = 0x00232001;
    private const uint ItemTarget = 0x00232002;
    private const uint StorageTarget = 0x00232003;
    private const uint InterfaceTarget = 0x00232004;
    private const uint DbInterface = 0x00232005;
    private const uint DbTarget = 0x00232006;
    private const uint DbEnvironment = 0x00232007;
    private const uint Content = 0x00232008;
    private const uint TableTarget = 0x00232009;
    private const uint TableEnvironment = 0x0023200A;

    private const uint BooleanType = 0x80000001;
    private const uint DateTimeType = 0x8000000A;
    private const uint StringType = 0x8000000B;
    private const uint BlobType = 0x8000000D;
    private const uint UIntType = 0x80000013;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        <AttributeSet name="ICoreExpandoAttributes" id="0x2" persistent="true" expando="true" />
        </Namespace><Namespace name="Siemens.Automation.DomainModel">
        <AttributeSet name="ITagAddress" id="0x3" persistent="true"><Attribute name="LogicalAddress" id="0" type="xs:string"/></AttributeSet>
        <ObjectType name="ProjectData" id="0x00230001"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
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
        </ObjectType>
        </Namespace><Namespace name="N">
        """;

    /// <summary>Expando keys of each object type, as one key table per type.</summary>
    private static readonly (uint Type, ulong Table, (uint Key, string Name, uint ValueType)[] Keys)[] KeyTables = [
        (DeviceItem, 801, [
            (1, "OpcUaEnableServer", BooleanType), (2, "ApplicationName", StringType), (3, "EnableSecurityPolicies_None", BooleanType),
            (4, "EnableSecurityPolicies_256SHA_SE", BooleanType), (5, "AutoAcceptClient", BooleanType), (6, "MinimumPublishingInterval", UIntType),
            (7, "ServerCertificateId", StringType), (8, "TrustedClientCertificatesIds", StringType), (9, "EncryptedPassword1", BlobType),
            (10, "SamplingQueue", UIntType),
        ]),
        (Storage, 802, [
            (1, "UsersAndRolesStorage", BlobType), (2, "LastCompiled", DateTimeType), (3, "LastDownloaded", DateTimeType),
            (4, "OPCUA.Expando.StructureJournalChanges", StringType), (5, "OPCUA.Expando.MappingJournalChanges", StringType),
        ]),
        (DataBlock, 803, [(1, "DBAccessibleFromOPCUA", BooleanType)]),
    ];

    private static readonly DateTime Compiled = new(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc);

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
    private static byte[] RolesContainer() {
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

    private sealed class Options {
        public bool HasItem { get; init; } = true;
        public bool StoresEnable { get; init; } = true;
        public bool HasInterface { get; init; }
        public bool HasUser { get; init; }
        public byte[]? Roles { get; init; }
    }

    /// <summary>
    /// A project with the PLC <c>Filler</c>: an OPC UA device item, a server storage, three data blocks and a tag
    /// table; and the PLC <c>Capper</c> with nothing of OPC UA.
    /// </summary>
    private static TiaDatabase NewProject(Options? options = null) {
        options ??= new Options();
        var file = new TiaFileBuilder(Schema);
        file.WriteSystem(0x70002, 800, Mapping());
        foreach (var (_, id, keys) in KeyTables) {
            file.WriteSystem(0x70011, id, KeyTable(keys));
        }
        file.WriteObject(Project, 1, [Core("Filling line")]);
        file.WriteObject(Plc, 10, [Core("Filler"), TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);
        file.WriteObject(Plc, 11, [Core("Capper"), TiaFileBuilder.Slots((PlcEnvironment, Project, 1))]);

        if (options.HasItem) {
            var settings = new List<(uint, uint?, byte[]?)> {
                Text(2, "Filler.OPC-UA"), Flag(3, false), Flag(4, true), Flag(5, false), (6, 250, null), Text(7, "3"), Text(8, "4,5"),
                (9, null, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes("not to be seen"), compress: false)), (10, 7, null),
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
            </Member></Root>
            """);
        DataBlockObject(file, 41, "Service", false, """<Root><Member ID="2" Name="Static"><Member ID="51" Name="Code" RID="0x02000001" /></Member></Root>""");

        file.WriteObject(Tag, 60, TagObject("Start", "%I0.0", null, accessible: true, visible: true, readOnly: false));
        file.WriteObject(Tag, 61, TagObject("Level", "%IW2", null, accessible: true, visible: true, readOnly: true));
        file.WriteObject(Tag, 62, TagObject("Spare", "%M9.0", null, accessible: false, visible: false, readOnly: false));
        file.WriteObject(Tag, 63, TagObject("MaxBottles", "", "10", accessible: true, visible: true, readOnly: false));
        file.WriteObject(TagTable, 70, [
            Core("Line tags"),
            TiaFileBuilder.Slots((Content, Tag, 60), (Content, Tag, 61), (Content, Tag, 62), (Content, Tag, 63), (TableTarget, Plc, 10), (TableEnvironment, Project, 1)),
        ]);
        file.Commit();
        return TiaDatabase.Parse(file.ToArray());
    }

    /// <summary>A tag's segments in interface order: address, default value, structure item, core attributes.</summary>
    private static byte[]?[] TagObject(string name, string address, string? value, bool accessible, bool visible, bool readOnly) =>
        [OneString(address), value is null ? null : OneString(value), StructureItem("Bool", accessible, visible, readOnly), Core(name)];

    private static void DataBlockObject(TiaFileBuilder file, ulong id, string name, bool? openToOpcUa, string xml) {
        file.WriteObject(Root, id + 100, [null, Part(xml)]);
        var general = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("DB")), (8, TiaFileBuilder.String("DB")));
        file.WriteObject(DataBlock, id, [
            Core(name, "DB"),
            openToOpcUa is { } open ? Expando(Flag(1, open)) : null,
            general,
            TiaFileBuilder.Slots((DbInterface, Root, id + 100), (DbTarget, Plc, 10), (DbEnvironment, Project, 1)),
        ]);
    }

    // Helpers --------------------------------------------------------------------------------------------------------

    private static TiaOpcUaReport Report(Options? options = null) => TiaOpcUa.Read(new TiaProgram(NewProject(options)));

    private static TiaOpcUaPlc Filler(Options? options = null) => Report(options).Plcs.Single(plc => plc.Name == "Filler");

    private static string Markdown(TiaOpcUaReport report) {
        var output = new StringWriter();
        TiaOpcUaWriter.WriteMarkdown(report, "Filling line", output);
        return output.ToString();
    }

    private static string Inspected(TiaDatabase database, uint type, ulong id) {
        database.TryGetFrame(new TiaObjectKey(type, id), out var frame).Should().BeTrue();
        var output = new StringWriter();
        TiaInspector.Write(database, frame, output);
        return output.ToString();
    }

    // Is the OPC UA server on, and how is it set up ------------------------------------------------------------------

    [Test]
    public void AnEnabledServerIsReportedOnWithItsSettings() {
        var server = Filler().Server!;

        server.IsEnabled.Should().BeTrue();
        server.ApplicationName.Should().Be("Filler.OPC-UA");
        server.SecurityPolicies.Should().Contain(new TiaOpcUaSecurityPolicy("None", false))
            .And.Contain(new TiaOpcUaSecurityPolicy("256SHA_SE", true))
            .And.Contain(new TiaOpcUaSecurityPolicy("256_S", null));
        server.AutoAcceptClientCertificates.Should().BeFalse();
        server.MinimumPublishingInterval.Should().Be(250);
        Markdown(Report()).Should().Contain("**Server: on.**").And.Contain("| Minimum publishing interval | 250 ms |").And.Contain("`256_S` not stored");
    }

    [Test]
    public void AServerWithoutAnEnableSwitchIsReportedOff() {
        var options = new Options { StoresEnable = false };

        Filler(options).Server!.IsEnabled.Should().BeFalse();
        Markdown(Report(options)).Should().Contain("**Server: off.**");
    }

    [Test]
    public void APlcWithoutAnOpcUaDeviceItemIsSaidToHaveNone() {
        var report = Report();

        report.Plcs.Single(plc => plc.Name == "Capper").Server.Should().BeNull();
        Markdown(report).Should().Contain("## PLC Capper\n\n**Server: no OPC UA device item.**");
    }

    [Test]
    public void ASettingTheReportDoesNotNameIsListedWithItsStoredNameAndValue() =>
        Filler().Server!.OtherSettings.Should().Equal(new TiaOpcUaSetting("SamplingQueue", "7"));

    // What could an OPC UA client reach -------------------------------------------------------------------------------

    [Test]
    public void ADataBlockClosedToOpcUaIsReportedClosed_AndOneThatStoresNoSettingOpen() {
        var blocks = Filler().DataBlocks;

        blocks.Single(block => block.Name == "Service").AccessibleFromOpcUa.Should().BeFalse();
        blocks.Single(block => block.Name == "Recipe").AccessibleFromOpcUa.Should().BeTrue();
    }

    [Test]
    public void ClosedAndReadOnlyMembersAreNamed_AndMembersUnderAClosedStructAreNot() {
        var recipe = Filler().DataBlocks.Single(block => block.Name == "Recipe");

        recipe.HiddenMembers.Should().Equal("Internal", "Hidden");
        recipe.ReadOnlyMembers.Should().Equal("Limit");
        recipe.Members.Should().Be(5);
        Markdown(Report()).Should().Contain("- Recipe.Hidden: closed\n").And.Contain("- Recipe.Limit: read-only\n").And.NotContain("Inside");
    }

    [Test]
    public void TagsAreReportedWithTheirAccess_AndConstantsAreLeftOut() {
        var tags = Filler().TagTables.Single().Tags;

        tags.Select(tag => (tag.Name, tag.ExternalAccess)).Should().Equal(
            ("Start", TiaExternalAccess.ReadWrite), ("Level", TiaExternalAccess.ReadOnly), ("Spare", TiaExternalAccess.None));
        tags.Single(tag => tag.Name == "Spare").ExternalVisible.Should().BeFalse();
        Markdown(Report()).Should().Contain("| Line tags | 3 | 1 | 1 | 1 | 0 |").And.Contain("| Line tags | Spare | Bool | %M9.0 | not accessible | no |");
    }

    [Test]
    public void AnOffServerIsSaidToReachNoClient_AndWhatWouldBeOpenIsStillGiven() =>
        Markdown(Report(new Options { StoresEnable = false })).Should().Contain(
            "**Server: off.** No client reaches this PLC through its own OPC UA server. Were it on: 1 of 2 data blocks are open to OPC UA, 2 of 3 tags are accessible.");

    // Which server interfaces and roles exist -------------------------------------------------------------------------

    [Test]
    public void RolesAreListedWithNameProjectRoleImportAndPermissions() {
        var storage = Filler().Storage!;

        storage.RolesError.Should().BeNull();
        storage.Roles.Select(role => (role.Name, role.ProjectRole, role.IsImported)).Should().Equal(("Operator", "Line operators", true), ("Viewer", null, false));
        var permission = storage.Roles[0].Permissions.Should().ContainSingle().Subject;
        permission.NamespaceUri.Should().Be("http://www.siemens.com/simatic-s7-opcua");
        permission.Names.Should().Equal("Browse", "ReadRolePermissions", "Read");
        Markdown(Report()).Should().Contain("| Operator | Line operators | yes | http://www.siemens.com/simatic-s7-opcua: Browse, ReadRolePermissions, Read |");
    }

    [Test]
    public void RolesThatCannotBeReadAreReportedSo() {
        var broken = RolesContainer()[..80];

        var storage = Filler(new Options { Roles = broken }).Storage!;

        storage.Roles.Should().BeEmpty();
        storage.RolesError.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void EmptyJournalsNoInterfacesAndTheCompileDateAreReported() {
        var storage = Filler().Storage!;

        storage.HasStructureChanges.Should().BeFalse();
        storage.HasMappingChanges.Should().BeFalse();
        storage.ServerInterfaces.Should().BeEmpty();
        storage.LastCompiled.Should().Be(Compiled);
        storage.LastDownloaded.Should().BeNull();
        Markdown(Report()).Should().Contain("No server interface objects.\nStructure journal: empty. Mapping journal: empty.\nLast compiled: 2026-03-01 08:30 UTC. Last downloaded: never.");
    }

    [Test]
    public void ServerInterfaceObjectsAreListedByName() =>
        Filler(new Options { HasInterface = true }).Storage!.ServerInterfaces.Should().Equal("Line status");

    // Nothing secret comes out ----------------------------------------------------------------------------------------

    [Test]
    public void PasswordsAndCertificateIdsDoNotAppear_AndCertificatesAreOnlyMarkedAndCounted() {
        var server = Filler().Server!;
        var text = Markdown(Report());
        var json = new MemoryStream();
        TiaOpcUaWriter.WriteJson(Report(), json);
        var jsonText = Encoding.UTF8.GetString(json.ToArray());

        server.ServerCertificateAssigned.Should().BeTrue();
        server.TrustedClientCertificates.Should().Be(2);
        foreach (var output in new[] { text, jsonText }) {
            output.Should().NotContain("EncryptedPassword").And.NotContain("not to be seen").And.NotContain("ServerCertificateId").And.NotContain("4,5");
        }
    }

    [Test]
    public void AUserManagementObjectIsNotShownWhenInspected() =>
        Inspected(NewProject(new Options { HasUser = true }), ProjectUser, 32).Should().NotContain("operator.one").And.Contain("not shown");

    [Test]
    public void AnExpandoValueNamedLikeAPasswordIsNotShownWhenInspected() =>
        Inspected(NewProject(), DeviceItem, 20).Should().Contain("~EncryptedPassword1 = [not shown]").And.NotContain("not to be seen")
            .And.Contain("~ServerCertificateId = [not shown]");

    // Read it in a program --------------------------------------------------------------------------------------------

    [Test]
    public void TheJsonReportHoldsTheSameFacts() {
        var json = new MemoryStream();
        TiaOpcUaWriter.WriteJson(Report(), json);

        using var document = JsonDocument.Parse(json.ToArray());
        var filler = document.RootElement.GetProperty("plcs").EnumerateArray().Single(plc => plc.GetProperty("name").GetString() == "Filler");
        filler.GetProperty("server").GetProperty("enabled").GetBoolean().Should().BeTrue();
        filler.GetProperty("server").GetProperty("securityPolicies").GetProperty("256SHA_SE").GetBoolean().Should().BeTrue();
        filler.GetProperty("storage").GetProperty("roles")[0].GetProperty("name").GetString().Should().Be("Operator");
        filler.GetProperty("dataBlocks").EnumerateArray().Select(block => (block.GetProperty("name").GetString(), block.GetProperty("accessibleFromOpcUa").GetBoolean()))
            .Should().BeEquivalentTo([("Recipe", true), ("Service", false)]);
        filler.GetProperty("tagTables")[0].GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("access").GetString())
            .Should().Equal("readWrite", "readOnly", "none");
    }

    // Find where a fact lives -----------------------------------------------------------------------------------------

    [Test]
    public void InspectingAnObjectShowsItsExpandoValuesWithTheirKeys() =>
        Inspected(NewProject(), DeviceItem, 20).Should().Contain("~OpcUaEnableServer = 1\n").And.Contain("~ApplicationName = Filler.OPC-UA\n").And.Contain("~MinimumPublishingInterval = 250\n");
}
