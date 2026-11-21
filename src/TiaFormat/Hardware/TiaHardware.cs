using System.Buffers.Binary;
using System.Globalization;
using TiaFormat.Program;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Hardware;

/// <summary>A project's hardware: its devices, subnets and IO systems (spec 0010, section 19 of the format page).</summary>
public sealed record TiaHardwareReport(IReadOnlyList<TiaDevice> Devices, IReadOnlyList<TiaSubnet> Subnets, IReadOnlyList<TiaIoSystem> IoSystems) {
    /// <summary>Every device item of every device, depth first.</summary>
    public IEnumerable<TiaDeviceItem> AllItems => Devices.SelectMany(device => device.AllItems);
}

/// <summary>One device of the project, such as an S7-1500 station or an ET 200SP, in its device group, with its own system constants.</summary>
public sealed record TiaDevice(string Name, string Group, string? TypeName, IReadOnlyList<TiaSystemConstant> Constants, IReadOnlyList<TiaDeviceItem> Items) {
    /// <summary>Every device item of the device, depth first.</summary>
    public IEnumerable<TiaDeviceItem> AllItems => Items.SelectMany(item => item.AllItems);
}

/// <summary>
/// A device item as TIA lists it: a rack, module, submodule, interface or port. <see cref="Path"/> names the device and
/// each device item down to this one, joined by <see cref="TiaHardware.PathSeparator"/>. A device item plugged into the
/// device has an order number, firmware and type name; one built into another has none, and an interface or port built
/// in has a label instead (<c>X1</c>, <c>X1 P1</c>).
/// </summary>
public sealed record TiaDeviceItem(
    string Name,
    string Path,
    long? Slot,
    string? OrderNumber,
    string? Firmware,
    string? TypeName,
    string? Label,
    IReadOnlyList<TiaSystemConstant> Constants,
    IReadOnlyList<TiaIoAddress> IoAddresses,
    IReadOnlyList<TiaInterfaceAddress> InterfaceAddresses,
    IReadOnlyList<TiaDeviceItem> Items) {
    /// <summary>This device item and those inside it, depth first.</summary>
    public IEnumerable<TiaDeviceItem> AllItems => Items.SelectMany(item => item.AllItems).Prepend(this);
}

/// <summary>
/// A system constant through which the program addresses a device item, a device or an IO system: its name, its value,
/// the hardware identifier, its type, such as <c>Hw_SubModule</c>, <c>Hw_Interface</c>, <c>Hw_Device</c> or
/// <c>Hw_IoSystem</c>, and the PLC whose tag table holds it: an IO device's are its IO controller's.
/// </summary>
public readonly record struct TiaSystemConstant(string Name, long? HardwareIdentifier, string? Type, string? Plc);

/// <summary>Inputs or outputs.</summary>
public enum TiaIoKind : byte { Input, Output }

/// <summary>An I/O address range: its kind, its first byte and its length in bits.</summary>
public readonly record struct TiaIoAddress(TiaIoKind Kind, long StartByte, long LengthInBits) {
    /// <summary>The last byte the range covers.</summary>
    public long LastByte => StartByte + Math.Max(1, (LengthInBits + 7) / 8) - 1;

    /// <summary>As TIA's device overview writes it, with the process image's letter: <c>I 10..13</c>, <c>Q 0</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(Kind == TiaIoKind.Input ? 'I' : 'Q')} {StartByte}{(LastByte != StartByte ? $"..{LastByte}" : "")}");
}

/// <summary>The kind of a subnet and of the interface addresses on it.</summary>
public enum TiaSubnetKind : byte { Other, Ethernet, Profibus }

/// <summary>
/// An interface's addresses on its subnet, by the name TIA gives them (<c>X1</c>, <c>IE1</c>, <c>PB1</c>). On Ethernet the
/// IP address and subnet mask, unless they are set outside the project, and the PROFINET device name; on PROFIBUS the
/// PROFIBUS address.
/// </summary>
public sealed record TiaInterfaceAddress(
    string Name,
    TiaSubnetKind Kind,
    string? Subnet,
    string? IpAddress,
    string? SubnetMask,
    bool IpAddressSetElsewhere,
    string? DeviceName,
    bool? DeviceNameGenerated,
    long? ProfibusAddress);

/// <summary>A subnet and the interfaces connected to it, by their paths.</summary>
public sealed record TiaSubnet(string Name, TiaSubnetKind Kind, IReadOnlyList<string> Interfaces);

/// <summary>
/// An IO system: a PROFINET IO system or a DP master system, with the interface of its IO controller and the interfaces
/// of its IO devices, by their paths.
/// </summary>
public sealed record TiaIoSystem(string Name, long? Number, TiaSubnetKind Kind, string? Subnet, string? Controller, IReadOnlyList<string> IoDevices, IReadOnlyList<TiaSystemConstant> Constants);

/// <summary>
/// Reads a project's hardware (section 19 of the format page). It reads named attributes only, so passwords, keys and
/// protection settings stored on the same objects are never read.
/// </summary>
public static class TiaHardware {
    public const string PathSeparator = " » ";

    private const string Model = "Siemens.Simatic.HwConfiguration.Model.";
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string ItemData = Model + "IDeviceItemData";

    /// <summary>The hardware of the project itself; devices in master copies are left out.</summary>
    public static TiaHardwareReport Read(TiaProgram program) => new Reader(program).Read();

    /// <summary>Whether the project itself has a device, without reading the hardware.</summary>
    public static bool HasDevices(TiaProgram program) {
        var device = program.Database.Schema.FindObjectType(Model + "DeviceData")?.Id;
        foreach (ref readonly var frame in program.Database.Frames) {
            if (frame.TypeId == device && program.Environment(frame).Kind == "ProjectData") {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether TIA lists a stored device item: internal objects of IO controllers and IO devices, and a CPU's proxy, common,
    /// execution unit, F-execution unit, licence manager and virtual communication interface it does not. Inferred from what TIA reports
    /// through Openness for the reference project; nothing stored says it.
    /// </summary>
    internal static bool IsListed(string? subtype, bool isVirtualInterface) {
        if (isVirtualInterface) {
            return false;
        }
        if (subtype is null) {
            return true;
        }
        return !subtype.StartsWith("Internal.", StringComparison.Ordinal)
            && !subtype.EndsWith(".CpuProxy", StringComparison.Ordinal)
            && !subtype.EndsWith(".CpuCommon", StringComparison.Ordinal)
            && !subtype.EndsWith(".CPUexecUnit", StringComparison.Ordinal)
            && !subtype.EndsWith(".CPUFexecUnit", StringComparison.Ordinal)
            && !subtype.EndsWith(".LicenseManager", StringComparison.Ordinal);
    }

    private sealed class Reader {
        private readonly TiaProgram _program;
        private readonly TiaDatabase _db;
        private readonly TiaAttributeHandle _name, _subtype, _order, _firmware, _position, _typeName, _deviceTypeName, _nodeType, _nodeFeatures, _subnetType, _constantValue, _constantType, _itemType;
        private readonly TiaRelationHandle _parent, _nodes, _constants, _subnet, _master, _heads, _subnetNodes, _nodeItem, _target;
        private readonly Dictionary<ulong, List<TiaFrame>> _children = [];
        private readonly Dictionary<ulong, TiaFrame> _items = [];
        private readonly HashSet<ulong> _deviceIds = [];
        private readonly Dictionary<ulong, string> _paths = [];
        private readonly List<TiaFrame> _devices = [], _subnets = [], _systems = [];

        public Reader(TiaProgram program) {
            _program = program;
            _db = program.Database;
            var s = _db.Schema;
            _name = s.Attribute(Core, "Name");
            _subtype = s.Attribute(Core, "Subtype");
            _order = s.Attribute(ItemData, "OrderNumber");
            _firmware = s.Attribute(ItemData, "FwVersion");
            _position = s.Attribute(ItemData, "PositionNumber");
            _typeName = s.Attribute(ItemData, "InvariantTypeName");
            _itemType = s.Attribute(ItemData, "DeviceItemType");
            _deviceTypeName = s.Attribute(Model + "IDeviceData", "InvariantTypeName");
            _nodeType = s.Attribute(Model + "INodeData", "NodeType");
            _nodeFeatures = s.Attribute(Model + "INodeData", "NodeFeatures");
            _subnetType = s.Attribute(Model + "ISubnetData", "NetType");
            _constantValue = s.Attribute("Siemens.Automation.DomainServices.CommonTypeSystem.IDefaultStrategyData", "DefaultValue");
            _constantType = s.Attribute("Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem", "DisplayTypeName");
            _parent = s.Relation("Parent");
            _nodes = s.Relation("DeviceItemNodes");
            _constants = s.Relation("ConstantTags");
            _subnet = s.Relation("Subnet");
            _master = s.Relation("Master");
            _heads = s.Relation("HeadModules");
            _subnetNodes = s.Relation("SubnetNodes");
            _nodeItem = s.Relation("DeviceItem");
            _target = s.Relation("Target");
        }

        public TiaHardwareReport Read() {
            var s = _db.Schema;
            var device = s.FindObjectType(Model + "DeviceData")?.Id;
            var subnet = s.FindObjectType(Model + "SubnetData")?.Id;
            var system = s.FindObjectType(Model + "MastersystemData")?.Id;
            var itemTypes = s.ObjectTypes.Where(type => type.Interfaces.Any(layout => layout.Set.FullName == ItemData)).Select(type => type.Id).ToHashSet();
            foreach (ref readonly var frame in _db.Frames) {
                if (frame.TypeId == device) {
                    if (_program.Environment(frame).Kind == "ProjectData") {
                        _devices.Add(frame);
                    }
                    _deviceIds.Add(frame.ObjectId);
                } else if (frame.TypeId == subnet) {
                    _subnets.Add(frame);
                } else if (frame.TypeId == system) {
                    _systems.Add(frame);
                } else if (itemTypes.Contains(frame.TypeId)) {
                    _items[frame.ObjectId] = frame;
                    if (_db.TryGetTarget(frame, _parent, out var parent)) {
                        (_children.TryGetValue(parent.ObjectId, out var list) ? list : _children[parent.ObjectId] = []).Add(frame);
                    }
                }
            }

            var devices = _devices
                .Select(frame => {
                    var name = _db.ReadString(frame, _name) ?? "";
                    var constants = new List<TiaSystemConstant>();
                    Subtree(frame, constants, DeviceConstant);
                    return new TiaDevice(name, Group(frame), _db.ReadString(frame, _deviceTypeName), [.. constants.OrderBy(constant => constant.Name, StringComparer.Ordinal)], Items(frame.ObjectId, name, name));
                })
                .OrderBy(entry => entry.Group, StringComparer.Ordinal).ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
            var subnets = _subnets
                .Select(frame => new TiaSubnet(_db.ReadString(frame, _name) ?? "", SubnetKind(_db.ReadInteger(frame, _subnetType)), [.. Targets(frame, _subnetNodes)
                    .Select(node => _db.TryGetTarget(node, _nodeItem, out var item) ? ListedPath(item) : null)
                    .OfType<string>().Distinct().Order(StringComparer.Ordinal)]))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
            var systems = _systems
                .Select(frame => new TiaIoSystem(
                    _db.ReadString(frame, _name) ?? "",
                    _db.ReadExpandoInteger(frame, "PositionNumber"),
                    (_db.ReadString(frame, _subtype) ?? "").EndsWith(".IOSystem", StringComparison.Ordinal) ? TiaSubnetKind.Ethernet : TiaSubnetKind.Profibus,
                    _db.TryGetTarget(frame, _subnet, out var on) ? _db.ReadString(on, _name) : null,
                    _db.TryGetTarget(frame, _master, out var master) ? ListedPath(master) : null,
                    [.. Targets(frame, _heads).Select(ListedPath).OfType<string>().Distinct().Order(StringComparer.Ordinal)],
                    _db.TryGetTarget(frame, _master, out var owner) ? SystemConstants(owner) : []))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
            return new TiaHardwareReport(devices, subnets, systems);
        }

        /// <summary>The device group path; empty for a device in no group.</summary>
        private string Group(in TiaFrame device) => _program.FolderPath(device);

        private List<TiaDeviceItem> Items(ulong parent, string parentName, string parentPath) {
            var result = new List<TiaDeviceItem>();
            foreach (var frame in _children.GetValueOrDefault(parent) ?? []) {
                if (!IsListed(_db.ReadString(frame, _subtype), IsVirtualInterface(frame))) {
                    continue;
                }
                result.Add(Item(frame, parent, parentName, parentPath, _deviceIds.Contains(parent)));
            }
            return [.. result.OrderBy(item => item.Slot ?? long.MaxValue).ThenBy(item => item.TypeName == "Rack" ? 0 : 1).ThenBy(item => item.Name, StringComparer.Ordinal)];
        }

        private TiaDeviceItem Item(in TiaFrame frame, ulong parentId, string parentName, string parentPath, bool plugged) {
            var stored = _db.ReadString(frame, _name) ?? "";
            var subtype = _db.ReadString(frame, _subtype);
            var parent = _items.TryGetValue(parentId, out var parentFrame) ? parentFrame : (TiaFrame?)null;
            var parentSubtype = parent is { } owner ? _db.ReadString(owner, _subtype) : null;
            // An interface module's own submodule is stored as "_1", or after the module's type ("IM 155-6 PN HF_1"), and
            // TIA names it as the module.
            var ownSubmodule = (stored.Length > 1 && stored[0] == '_' && stored[1..].All(char.IsAsciiDigit))
                || (subtype?.EndsWith(".IM.Submodule", StringComparison.Ordinal) ?? false);
            var name = ownSubmodule ? parentName : stored;
            var path = parentPath + PathSeparator + name;
            _paths[frame.ObjectId] = path;
            var nodes = Targets(frame, _nodes).ToList();
            var typeName = NonEmpty(_db.ReadString(frame, _typeName));
            var order = NonEmpty(_db.ReadString(frame, _order));
            var connector = IsConnector(_db.ReadInteger(frame, _itemType));
            return new TiaDeviceItem(
                name,
                path,
                _db.ReadInteger(frame, _position),
                // A lowercase letter marks a placeholder, such as an ET 200SP rack's, which TIA does not show.
                plugged && order is not null && !order.Any(char.IsAsciiLetterLower) ? order : null,
                plugged ? NonEmpty(_db.ReadString(frame, _firmware)) : null,
                // A built-in item that is no interface or port, whose subtype extends its module's (S71500.SM.DI.DI16 in
                // S71500.SM.DI), shows the type it stores, as an F-module's submodule does; a plain module's stores none.
                plugged || (!connector && subtype is not null && parentSubtype is not null && subtype.StartsWith(parentSubtype + ".", StringComparison.Ordinal)) ? typeName : null,
                plugged ? null : connector ? typeName : null,
                Constants(frame),
                IoAddresses(frame),
                // A generated PROFINET device name is the CPU's name and the interface's, or the interface module's name,
                // in lowercase: what is stored was generated from the names at the time and may be out of date.
                [.. nodes.Select(node => InterfaceAddress(node, (parent is { } holder && IsCpu(holder) ? parentName + "." + name : parentName).ToLowerInvariant()))],
                Items(frame.ObjectId, name, path));
        }

        /// <summary><c>DeviceItemType</c> with 0x2000 set is an interface, with 0x4000 a port.</summary>
        private static bool IsConnector(long? type) => type is { } value && (value & 0x6000) != 0;

        /// <summary>
        /// The item's own system constants and those of the device items inside it that TIA does not list; a device's
        /// and an IO system's constants belong to those instead.
        /// </summary>
        private List<TiaSystemConstant> Constants(in TiaFrame frame) {
            var result = new List<TiaSystemConstant>();
            Own(frame, result, ItemConstant);
            foreach (var child in _children.GetValueOrDefault(frame.ObjectId) ?? []) {
                if (!IsListed(_db.ReadString(child, _subtype), IsVirtualInterface(child))) {
                    Subtree(child, result, ItemConstant);
                }
            }
            return [.. result.OrderBy(constant => constant.Name, StringComparer.Ordinal)];
        }

        private List<TiaSystemConstant> SystemConstants(in TiaFrame master) {
            var result = new List<TiaSystemConstant>();
            Own(master, result, constant => constant.Type == "Hw_IoSystem");
            return result;
        }

        private static bool ItemConstant(TiaSystemConstant constant) => constant.Type is not ("Hw_Device" or "Hw_DpSlave" or "Hw_IoSystem");

        /// <summary>A device's own identifier: <c>Hw_Device</c>, or <c>Hw_DpSlave</c> for a DP device.</summary>
        private static bool DeviceConstant(TiaSystemConstant constant) => constant.Type is "Hw_Device" or "Hw_DpSlave";

        private void Subtree(in TiaFrame frame, List<TiaSystemConstant> into, Func<TiaSystemConstant, bool> which) {
            Own(frame, into, which);
            foreach (var child in _children.GetValueOrDefault(frame.ObjectId) ?? []) {
                Subtree(child, into, which);
            }
        }

        private void Own(in TiaFrame frame, List<TiaSystemConstant> into, Func<TiaSystemConstant, bool> which) {
            foreach (var target in Targets(frame, _constants)) {
                var name = _db.ReadString(target, _name);
                if (name is null) {
                    continue;
                }
                var constant = new TiaSystemConstant(
                    name,
                    long.TryParse(_db.ReadString(target, _constantValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                    _db.ReadString(target, _constantType),
                    _db.TryGetTarget(target, _target, out var plc) ? _db.ReadString(plc, _name) : null);
                if (which(constant)) {
                    into.Add(constant);
                }
            }
        }

        /// <summary>
        /// The <c>AM_n_AddrData_m</c> expando values, each an <c>AddressItem</c> structure: <c>AdditionalInfo</c> (2 input,
        /// 4 output), <c>AlarmObNumber</c>, <c>Length</c> in bits, <c>ProcessImagePartNumber</c>, <c>StartAddress</c> in bits,
        /// <c>UniqueNumber</c>.
        /// </summary>
        private List<TiaIoAddress> IoAddresses(in TiaFrame frame) {
            var result = new List<TiaIoAddress>();
            foreach (var key in _db.ReadExpandoKeys(frame)) {
                if (!key.Name.StartsWith("AM_", StringComparison.Ordinal) || !key.Name.Contains("_AddrData_", StringComparison.Ordinal)) {
                    continue;
                }
                var value = _db.ReadExpandoStructure(frame, key.Name);
                if (value.Length < 34) {
                    continue;
                }
                var kind = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
                var length = BinaryPrimitives.ReadInt64LittleEndian(value[10..]);
                var start = BinaryPrimitives.ReadInt64LittleEndian(value[22..]);
                if (kind is 2 or 4 && start >= 0 && length > 0) {
                    result.Add(new TiaIoAddress(kind == 2 ? TiaIoKind.Input : TiaIoKind.Output, start / 8, length));
                }
            }
            return [.. result.OrderBy(address => address.Kind).ThenBy(address => address.StartByte)];
        }

        private bool IsCpu(in TiaFrame frame) =>
            _db.Schema.FindObjectType(frame.TypeId)?.FullName.EndsWith(".S7ControllerTargetData", StringComparison.Ordinal) ?? false;

        private TiaInterfaceAddress InterfaceAddress(TiaFrame node, string generatedName) {
            var kind = SubnetKind(_db.ReadInteger(node, _nodeType));
            var subnet = _db.TryGetTarget(node, _subnet, out var on) ? _db.ReadString(on, _name) : null;
            var name = _db.ReadString(node, _name) ?? "";
            if (kind == TiaSubnetKind.Profibus) {
                return new TiaInterfaceAddress(name, kind, subnet, null, null, false, null, null, _db.ReadExpandoInteger(node, "NodePBMpiAddress"));
            }
            // NodeIPConfiguration 3: "IP address is set directly at the device"; the address still stored is not the one used.
            var elsewhere = _db.ReadExpandoInteger(node, "NodeIPConfiguration") == 3;
            var generated = _db.ReadExpandoInteger(node, "PnPnNoSAutoGenerate") is { } flag ? flag != 0 : (bool?)null;
            return new TiaInterfaceAddress(
                name,
                kind,
                subnet,
                elsewhere ? null : Ip(_db.ReadExpandoInteger(node, "NodeIPAddress")),
                elsewhere ? null : Ip(_db.ReadExpandoInteger(node, "NodeIPSubnetMask")),
                elsewhere,
                generated == true ? generatedName : _db.ReadExpandoString(node, "PnNameOfStation"),
                generated,
                null);
        }

        private bool IsVirtualInterface(in TiaFrame item) {
            foreach (var node in Targets(item, _nodes)) {
                if (_db.ReadInteger(node, _nodeFeatures) == 2) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The path of the listed device item an object belongs to: itself, or the nearest listed one above it.</summary>
        private string? ListedPath(TiaFrame frame) {
            var current = frame;
            for (var i = 0; i < 16; i++) {
                if (_paths.TryGetValue(current.ObjectId, out var path)) {
                    return path;
                }
                if (!_db.TryGetTarget(current, _parent, out var parent)) {
                    return null;
                }
                current = parent;
            }
            return null;
        }

        private List<TiaFrame> Targets(in TiaFrame frame, TiaRelationHandle relation) {
            var result = new List<TiaFrame>();
            foreach (var entry in _db.EnumerateRelations(frame)) {
                if (relation.Matches(entry.RelationId) && entry.Target.ObjectId != 0 && _db.TryGetFrame(entry.Target, out var target)) {
                    result.Add(target);
                }
            }
            return result;
        }

        private static TiaSubnetKind SubnetKind(long? type) => type switch {
            3 => TiaSubnetKind.Ethernet,
            1 => TiaSubnetKind.Profibus,
            _ => TiaSubnetKind.Other,
        };

        private static string? Ip(long? value) => value is not { } v || v == 0 ? null
            : string.Create(CultureInfo.InvariantCulture, $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}");

        private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
    }
}
