using System.Globalization;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A project's OPC UA configuration, one entry per PLC of the project itself.</summary>
public sealed record TiaOpcUaReport(IReadOnlyList<TiaOpcUaPlc> Plcs);

/// <summary>
/// One PLC's OPC UA configuration: its server settings, its server storage, and what its data blocks and tags allow an
/// outside client to do.
/// </summary>
public sealed record TiaOpcUaPlc(
    string Name,
    TiaOpcUaServer? Server,
    TiaOpcUaStorage? Storage,
    IReadOnlyList<TiaOpcUaDataBlock> DataBlocks,
    IReadOnlyList<TiaOpcUaTagTable> TagTables);

/// <summary>
/// The settings of a PLC's OPC UA server, from the CPU's OPC UA device item and, for the minimum sampling interval, the
/// PLC itself. A setting that is not stored is null: TIA's default applies. Certificates are reported only as assigned
/// or counted.
/// </summary>
public sealed record TiaOpcUaServer(
    string DeviceItem,
    string? ConfigType,
    bool IsEnabled,
    string? ApplicationName,
    IReadOnlyList<TiaOpcUaSecurityPolicy> SecurityPolicies,
    bool? AutoAcceptClientCertificates,
    long? MinimumPublishingInterval,
    bool ServerCertificateAssigned,
    int TrustedClientCertificates,
    IReadOnlyList<TiaOpcUaSetting> OtherSettings) {
    /// <summary>The server's TCP port; TIA's default is 4840.</summary>
    public long? Port { get; init; }

    /// <summary>Guest authentication, a session without user name.</summary>
    public bool? GuestAuthentication { get; init; }

    /// <summary>User name and password authentication. Only the switch: users and passwords are never read.</summary>
    public bool? PasswordAuthentication { get; init; }

    /// <summary>The standard SIMATIC server interface, the address space of the program's data blocks and tags.</summary>
    public bool? StandardServerInterface { get; init; }

    /// <summary>In ms; stored on the PLC, not on the OPC UA device item.</summary>
    public long? MinimumSamplingInterval { get; init; }
}

/// <summary>
/// One security policy switch, by the suffix of its stored key (<c>None</c>, <c>256_S</c>, <c>256SHA_SE</c>, ...);
/// null when the key is not stored. Each suffix is TIA Openness's name of the policy flag, checked in TIA; see the
/// format page.
/// </summary>
public readonly record struct TiaOpcUaSecurityPolicy(string Key, bool? IsEnabled) {
    /// <summary>
    /// The OPC UA policy, named after TIA Openness's flag for the key and Siemens' manual (see the format page); the key
    /// itself for a key not seen.
    /// </summary>
    public string Name => Key switch {
        "None" => "No security",
        "128RSA_S" => "Basic128Rsa15 - Sign",
        "128RSA_SE" => "Basic128Rsa15 - Sign & Encrypt",
        "256_S" => "Basic256 - Sign",
        "256_SE" => "Basic256 - Sign & Encrypt",
        "256SHA_S" => "Basic256Sha256 - Sign",
        "256SHA_SE" => "Basic256Sha256 - Sign & Encrypt",
        "256RSA_PSS_SE" => "Aes256Sha256RsaPss - Sign & Encrypt",
        _ => Key,
    };
}

/// <summary>A stored setting this reader does not name, as text.</summary>
public readonly record struct TiaOpcUaSetting(string Name, string Value);

/// <summary>
/// A PLC's OPC UA server storage: when its server interfaces were last compiled and downloaded,
/// whether user-defined interfaces or companion specifications were changed, and its OPC UA roles.
/// </summary>
public sealed record TiaOpcUaStorage(
    DateTime? LastCompiled,
    DateTime? LastDownloaded,
    string? StructureJournal,
    string? MappingJournal,
    int CompiledInterfaceBytes,
    IReadOnlyList<string> ServerInterfaces,
    IReadOnlyList<TiaOpcUaRole> Roles,
    int RoleMappings,
    string? RolesError) {
    /// <summary>The structure journal holds changes: user-defined interfaces or companion specifications exist.</summary>
    public bool HasStructureChanges => IsNonEmptyJournal(StructureJournal);

    /// <summary>The mapping journal holds changes: interface nodes are mapped to PLC data.</summary>
    public bool HasMappingChanges => IsNonEmptyJournal(MappingJournal);

    private static bool IsNonEmptyJournal(string? journal) => journal is not null && journal.Trim() is not ("" or "[]");
}

/// <summary>An OPC UA role of a PLC's server, with what it may do in each namespace.</summary>
public sealed record TiaOpcUaRole(string Name, string? ProjectRole, bool IsImported, IReadOnlyList<TiaOpcUaPermission> Permissions);

/// <summary>A role's permissions in one namespace, as OPC UA's <c>PermissionType</c> bits.</summary>
public readonly record struct TiaOpcUaPermission(string NamespaceUri, int Bits) {
    private static readonly string[] BitNames = [
        "Browse", "ReadRolePermissions", "WriteAttribute", "WriteRolePermissions", "WriteHistorizing", "Read", "Write",
        "ReadHistory", "InsertHistory", "ModifyHistory", "DeleteHistory", "ReceiveEvents", "Call", "AddReference",
        "RemoveReference", "DeleteNode", "AddNode",
    ];

    /// <summary>The bits by their names in OPC UA Part 3, an unknown bit as its number.</summary>
    public IReadOnlyList<string> Names {
        get {
            var result = new List<string>();
            for (var bit = 0; bit < 32; bit++) {
                if ((Bits & (1 << bit)) != 0) {
                    result.Add(bit < BitNames.Length ? BitNames[bit] : "Bit" + bit.ToString(CultureInfo.InvariantCulture));
                }
            }
            return result;
        }
    }
}

/// <summary>
/// A data block as an OPC UA client sees it: whether the block is open to OPC UA at all, and which of its members are
/// closed to outside access or read-only. Member names are dotted paths; a member under a closed or read-only one is
/// not listed again.
/// </summary>
public sealed record TiaOpcUaDataBlock(
    string Name,
    string Kind,
    long? Number,
    bool AccessibleFromOpcUa,
    bool IsClosed,
    int Members,
    IReadOnlyList<string> HiddenMembers,
    IReadOnlyList<string> ReadOnlyMembers);

/// <summary>A tag table's tags with the access an outside client has; constants are left out.</summary>
public sealed record TiaOpcUaTagTable(string Name, IReadOnlyList<TiaTag> Tags);

/// <summary>Reads a project's OPC UA configuration. Never reads users, passwords, keys or certificates.</summary>
public static class TiaOpcUa {
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string ConfigBase = "Siemens.Simatic.HwConfiguration.Model.IConfigBaseData";
    private const string PolicyPrefix = "EnableSecurityPolicies_";
    private static readonly string[] Policies = ["None", "128RSA_S", "128RSA_SE", "256_S", "256_SE", "256SHA_S", "256SHA_SE", "256RSA_PSS_SE"];

    private static readonly HashSet<string> Named = [
        "OpcUaEnableServer", "ApplicationName", "AutoAcceptClient", "MinimumPublishingInterval", "ServerCertificateId", "TrustedClientCertificatesIds",
        PortKey, GuestKey, PasswordKey, StandardInterfaceKey,
    ];

    private const string PortKey = "OpcUaPortNumber";
    private const string GuestKey = "OpcUaEnableGuestAuthentication";
    private const string PasswordKey = "OpcUaEnablePasswordAuthentication";
    private const string StandardInterfaceKey = "EnableStandardAddressSpace";
    private const string SamplingKey = "MinimumSamplingInterval";

    /// <summary>The report of every PLC of the project, or of the one named <paramref name="plc"/> only.</summary>
    public static TiaOpcUaReport Read(TiaProgram program, string? plc = null) {
        var db = program.Database;
        var s = db.Schema;
        var name = s.Attribute(Core, "Name");
        var target = s.Relation("Target");
        var plcType = s.FindObjectType("Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData")?.Id;
        var itemType = s.FindObjectType("Siemens.Simatic.HwConfiguration.Model.DeviceItemData")?.Id;
        var storageType = s.FindObjectType("Siemens.Simatic.OpcUa.Model.OpcUaServerConfigurationData")?.Id;
        var interfaceType = s.FindObjectType("Siemens.Simatic.OpcUa.Model.OpcUaInterfaceData")?.Id;
        var configName = s.Attribute(ConfigBase, "ConfigObjectTypeName");
        var subtype = s.Attribute(Core, "Subtype");

        var plcs = new List<(TiaFrame Frame, string Name)>();
        var items = new Dictionary<ulong, TiaFrame>();
        var storages = new Dictionary<ulong, TiaFrame>();
        var interfaces = new Dictionary<ulong, List<string>>();
        foreach (ref readonly var frame in db.Frames) {
            if (frame.TypeId == plcType) {
                if (program.Environment(frame).Kind == "ProjectData" && db.ReadString(frame, name) is var plcName && (plc is null || plcName == plc)) {
                    plcs.Add((frame, plcName ?? ""));
                }
            } else if (frame.TypeId == itemType) {
                if (IsOpcUaItem(db.ReadString(frame, configName), db.ReadString(frame, subtype)) && db.TryGetTarget(frame, target, out var owner)) {
                    items.TryAdd(owner.ObjectId, frame);
                }
            } else if (frame.TypeId == storageType) {
                if (db.TryGetTarget(frame, target, out var owner)) {
                    storages.TryAdd(owner.ObjectId, frame);
                }
            } else if (frame.TypeId == interfaceType && db.TryGetTarget(frame, target, out var owner)) {
                (interfaces.TryGetValue(owner.ObjectId, out var list) ? list : interfaces[owner.ObjectId] = []).Add(db.ReadString(frame, name) ?? "");
            }
        }

        var blocks = program.Blocks.Where(block => !block.IsStub && block.Kind.StartsWith("DB", StringComparison.Ordinal) && program.Environment(block.Frame).Kind == "ProjectData").ToList();
        var tables = program.ReadTagTables().Where(table => program.Environment(table.Frame).Kind == "ProjectData").ToList();
        var result = new List<TiaOpcUaPlc>();
        foreach (var (frame, plcName) in plcs.OrderBy(entry => entry.Name, StringComparer.Ordinal)) {
            var id = frame.ObjectId;
            bool Owns(in TiaFrame owned) => db.TryGetTarget(owned, target, out var owner) && owner.ObjectId == id;
            result.Add(new TiaOpcUaPlc(
                plcName,
                items.TryGetValue(id, out var item) ? ReadServer(db, item, frame, name, configName) : null,
                storages.TryGetValue(id, out var storage) ? ReadStorage(db, storage, interfaces.GetValueOrDefault(id) ?? []) : null,
                [.. blocks.Where(block => Owns(block.Frame)).OrderBy(block => block.Number ?? long.MaxValue).ThenBy(block => block.Name, StringComparer.Ordinal).Select(block => ReadDataBlock(program, block))],
                [.. tables.Where(table => Owns(table.Frame)).OrderBy(table => table.Name, StringComparer.Ordinal)
                    .Select(table => new TiaOpcUaTagTable(table.Name, [.. table.Tags.Where(tag => tag.Value is null)]))]));
        }
        return new TiaOpcUaReport(result);
    }

    /// <summary>A CPU's OPC UA submodule: <c>S71500\Submodules\CPU\OPC_UA\MDD_OpcUa_&lt;version&gt;</c>, subtype <c>S71500.CPU.OPC_UA</c>.</summary>
    private static bool IsOpcUaItem(string? configName, string? subtype) =>
        (configName?.Contains(@"\OPC_UA\", StringComparison.OrdinalIgnoreCase) ?? false) || (subtype?.EndsWith(".OPC_UA", StringComparison.OrdinalIgnoreCase) ?? false);

    private static TiaOpcUaServer ReadServer(TiaDatabase db, in TiaFrame item, in TiaFrame plc, TiaAttributeHandle name, TiaAttributeHandle configName) {
        var policies = new List<TiaOpcUaSecurityPolicy>();
        foreach (var policy in Policies) {
            policies.Add(new TiaOpcUaSecurityPolicy(policy, db.ReadExpandoBoolean(item, PolicyPrefix + policy)));
        }
        var others = new List<TiaOpcUaSetting>();
        foreach (var key in db.ReadExpandoKeys(item)) {
            if (Named.Contains(key.Name) || TiaSecrets.IsSecretName(key.Name)) {
                continue;
            }
            if (key.Name.StartsWith(PolicyPrefix, StringComparison.Ordinal)) {
                if (!Policies.Contains(key.Name[PolicyPrefix.Length..])) {
                    policies.Add(new TiaOpcUaSecurityPolicy(key.Name[PolicyPrefix.Length..], db.ReadExpandoBoolean(item, key.Name)));
                }
                continue;
            }
            if (ShowValue(db, item, key) is { } value) {
                others.Add(new TiaOpcUaSetting(key.Name, value));
            }
        }
        var trusted = db.ReadExpandoString(item, "TrustedClientCertificatesIds") ?? "";
        return new TiaOpcUaServer(
            db.ReadString(item, name) ?? "",
            db.ReadString(item, configName),
            db.ReadExpandoBoolean(item, "OpcUaEnableServer") ?? false,
            db.ReadExpandoString(item, "ApplicationName"),
            policies,
            db.ReadExpandoBoolean(item, "AutoAcceptClient"),
            db.ReadExpandoInteger(item, "MinimumPublishingInterval"),
            !string.IsNullOrWhiteSpace(db.ReadExpandoString(item, "ServerCertificateId")),
            trusted.Split([',', ';', ' ', '|'], StringSplitOptions.RemoveEmptyEntries).Length,
            others) {
            Port = db.ReadExpandoInteger(item, PortKey),
            GuestAuthentication = db.ReadExpandoBoolean(item, GuestKey),
            // Named like a secret, but only the switch: the reader never looks for users or passwords.
            PasswordAuthentication = db.ReadExpandoBoolean(item, PasswordKey),
            StandardServerInterface = db.ReadExpandoBoolean(item, StandardInterfaceKey),
            MinimumSamplingInterval = db.ReadExpandoInteger(plc, SamplingKey),
        };
    }

    /// <summary>A scalar as text; null for a value this report does not show (blobs, structures).</summary>
    private static string? ShowValue(TiaDatabase db, in TiaFrame frame, TiaExpandoKey key) => key.ValueTypeId switch {
        0x80000001 => db.ReadExpandoBoolean(frame, key.Name) is { } flag ? (flag ? "true" : "false") : null,
        0x8000000A => db.ReadExpandoDateTime(frame, key.Name)?.ToString("u", CultureInfo.InvariantCulture),
        0x8000000B => db.ReadExpandoString(frame, key.Name),
        _ => db.ReadExpandoInteger(frame, key.Name)?.ToString(CultureInfo.InvariantCulture),
    };

    private static TiaOpcUaStorage ReadStorage(TiaDatabase db, in TiaFrame storage, List<string> interfaces) {
        var roles = new List<TiaOpcUaRole>();
        var mappings = 0;
        string? error = null;
        using (var blob = db.ReadExpandoBlob(storage, "UsersAndRolesStorage")) {
            if (!blob.IsEmpty) {
                try {
                    (roles, mappings) = ReadRoles(NrbfReader.Read(blob.Span));
                } catch (TiaFormatException e) {
                    error = e.Message;
                }
            }
        }
        int compiled;
        using (var blob = db.ReadExpandoBlob(storage, "CompiledInterfaceBlob")) {
            compiled = blob.Length;
        }
        interfaces.Sort(StringComparer.Ordinal);
        return new TiaOpcUaStorage(
            db.ReadExpandoDateTime(storage, "LastCompiled"),
            db.ReadExpandoDateTime(storage, "LastDownloaded"),
            db.ReadExpandoString(storage, "OPCUA.Expando.StructureJournalChanges"),
            db.ReadExpandoString(storage, "OPCUA.Expando.MappingJournalChanges"),
            compiled,
            interfaces,
            roles,
            mappings,
            error);
    }

    /// <summary>
    /// The roles of a <c>UsersAndRolesStorageContainer</c>: each <c>UaRoleMappingInfo</c>'s name, project role and
    /// per-namespace permissions; and how many entries its roles map holds. Nothing else of it is read.
    /// </summary>
    private static (List<TiaOpcUaRole> Roles, int Mappings) ReadRoles(object? root) {
        var roles = new List<TiaOpcUaRole>();
        if (root is not NrbfObject container) {
            return (roles, 0);
        }
        foreach (var info in Items(Member(container, "Roles")).OfType<NrbfObject>()) {
            var permissions = new List<TiaOpcUaPermission>();
            foreach (var permission in Items(Member(info, "RolePermissions")).OfType<NrbfObject>()) {
                permissions.Add(new TiaOpcUaPermission(Member(permission, "NamespaceUri") as string ?? "", Member(permission, "Permissions") is int bits ? bits : 0));
            }
            roles.Add(new TiaOpcUaRole(
                Member(info, "RoleName") as string ?? "",
                Member(info, "ProjectRoleName") is string { Length: > 0 } project ? project : null,
                Member(info, "IsImportedRole") is true,
                permissions));
        }
        var mappings = Member(container, "RolesMap") is NrbfObject map && map["KeyValuePairs"] is object?[] pairs ? pairs.Count(pair => pair is not null) : 0;
        return (roles, mappings);
    }

    /// <summary>A member by its property name, as an auto-property's backing field or by itself.</summary>
    private static object? Member(NrbfObject value, string name) =>
        value.Members.TryGetValue("<" + name + ">k__BackingField", out var backing) ? backing : value[name];

    /// <summary>The used elements of a <c>List&lt;T&gt;</c>, or of an array.</summary>
    private static IEnumerable<object?> Items(object? list) => list switch {
        NrbfObject { } generic when generic["_items"] is object?[] items => items.Take(generic["_size"] is int size ? Math.Min(size, items.Length) : items.Length),
        object?[] array => array,
        _ => [],
    };

    private static TiaOpcUaDataBlock ReadDataBlock(TiaProgram program, TiaBlock block) {
        var settings = program.DataBlockSettings(block);
        var hidden = new List<string>();
        var readOnly = new List<string>();
        var count = 0;
        if (!block.IsKnowHowProtected) {
            Walk(program.ReadInterface(block), "");
        }
        return new TiaOpcUaDataBlock(block.Name, block.Kind, block.Number, settings.AccessibleFromOpcUa, block.IsKnowHowProtected, count, hidden, readOnly);

        void Walk(IReadOnlyList<TiaMember> members, string prefix) {
            foreach (var member in members) {
                count++;
                var path = prefix + member.Name;
                switch (member.ExternalAccess) {
                    case TiaExternalAccess.None:
                        hidden.Add(path);
                        count += Count(member.Children);
                        continue;
                    case TiaExternalAccess.ReadOnly:
                        readOnly.Add(path);
                        count += Count(member.Children);
                        continue;
                    default:
                        Walk(member.Children, path + ".");
                        break;
                }
            }
        }

        static int Count(IReadOnlyList<TiaMember> members) => members.Sum(member => 1 + Count(member.Children));
    }
}
