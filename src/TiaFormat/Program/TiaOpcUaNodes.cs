using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>Whether a node is an object (the PLC, a folder, a data block, a section, a multi-instance) or a variable.</summary>
public enum TiaOpcUaNodeClass : byte {
    Object,
    Variable,
}

/// <summary>
/// One node of a PLC's standard SIMATIC server interface, as TIA Portal V21's node set export gives it (spec 0006).
/// Node ids are in the Siemens namespace, index 3 by default. Derived from the program, not read from TIA.
/// </summary>
public sealed record TiaOpcUaNode(string NodeId, TiaOpcUaNodeClass NodeClass, string BrowseName, string BrowsePath, string ParentNodeId, string ParentReference) {
    /// <summary>The type as the program declares it; null for the PLC, its folders and sections.</summary>
    public string? SimaticType { get; init; }

    /// <summary>The OPC UA data type's node id, <c>i=1</c> or <c>ns=3;i=3014</c> or <c>ns=3;s=DT_"Udt"</c>; null for an object.</summary>
    public string? DataType { get; init; }

    /// <summary>The data type's name: the alias TIA's export gives it, such as <c>BOOL</c>, or the node id.</summary>
    public string? DataTypeName { get; init; }

    /// <summary>The length of each dimension of an array; null if the node is no array.</summary>
    public IReadOnlyList<int>? ArrayDimensions { get; init; }

    /// <summary>Whether a client may write it; a variable that is not is read-only.</summary>
    public bool IsWritable { get; init; }

    /// <summary>The node's type definition, as TIA's export gives it.</summary>
    public string? TypeDefinition { get; init; }

    /// <summary>The comment of the member or tag.</summary>
    public string? Description { get; init; }

    /// <summary>The data block or tag the node comes from, by name, for opening it.</summary>
    public string? Source { get; init; }

    /// <summary>A know-how protected data block: it has members, but they are not known.</summary>
    public bool MembersUnknown { get; init; }
}

/// <summary>The nodes of one PLC, the PLC's object and folders first.</summary>
public sealed record TiaOpcUaNodeList(string Plc, IReadOnlyList<TiaOpcUaNode> Nodes);

/// <summary>
/// Derives the nodes of a PLC's standard SIMATIC OPC UA server interface from its program, by the rules TIA Portal V21's
/// node set export follows (docs/research/2026-11-20-tia-opc-ua-server-settings.md).
/// </summary>
public static partial class TiaOpcUaNodes {
    /// <summary>Said by every list and export: the nodes are derived, not read from TIA.</summary>
    public const string Derived =
        "Derived by TiaTools from the program by the rules of TIA Portal V21's node set export, not read from TIA. Namespace index 3 is the default and may differ; TIA's own export (OPC UA > Server > Export) is the server's real list.";

    /// <summary>The Siemens namespace of the PLC's nodes.</summary>
    public const string Namespace = "http://www.siemens.com/simatic-s7-opcua";

    internal const string Organizes = "Organizes";
    internal const string HasComponent = "HasComponent";

    /// <summary>The nodes of every PLC of the project itself, or of the one named <paramref name="plc"/> only.</summary>
    public static IReadOnlyList<TiaOpcUaNodeList> Read(TiaProgram program, string? plc = null) {
        var db = program.Database;
        var scan = TiaOpcUa.Scan(program, plc);
        var blocks = program.Blocks.Where(block => !block.IsStub && program.Environment(block.Frame).Kind == "ProjectData").ToList();
        var tables = program.ReadTagTables().Where(table => program.Environment(table.Frame).Kind == "ProjectData").ToList();
        var result = new List<TiaOpcUaNodeList>();
        foreach (var (frame, name) in scan.Plcs.OrderBy(entry => entry.Name, StringComparer.Ordinal)) {
            var id = frame.ObjectId;
            var owned = blocks.Where(block => TiaOpcUa.Owns(db, block.Frame, id)).ToList();
            var builder = new Builder(program, name, owned);
            builder.Build(owned, tables.Where(table => TiaOpcUa.Owns(db, table.Frame, id)).OrderBy(table => table.Name, StringComparer.Ordinal));
            result.Add(new TiaOpcUaNodeList(name, builder.Nodes));
        }
        return result;
    }

    // Data types ----------------------------------------------------------------------------------------------------

    /// <summary>An elementary type's alias and node id in TIA's export, by the type's name in capitals.</summary>
    internal static readonly Dictionary<string, (string Alias, string NodeId)> Elementary = new(StringComparer.Ordinal) {
        ["BOOL"] = ("BOOL", "i=1"), ["SINT"] = ("SINT", "i=2"), ["USINT"] = ("USINT", "i=3"), ["INT"] = ("INT", "i=4"),
        ["UINT"] = ("UINT", "i=5"), ["DINT"] = ("DINT", "i=6"), ["UDINT"] = ("UDINT", "i=7"), ["LINT"] = ("LINT", "i=8"),
        ["ULINT"] = ("ULINT", "i=9"), ["REAL"] = ("REAL", "i=10"), ["LREAL"] = ("LREAL", "i=11"), ["WSTRING"] = ("WSTRING", "i=12"),
        ["LDT"] = ("LDT", "i=13"), ["BYTE"] = ("BYTE", "ns=3;i=3001"), ["WORD"] = ("WORD", "ns=3;i=3002"),
        ["DWORD"] = ("DWORD", "ns=3;i=3003"), ["LWORD"] = ("LWORD", "ns=3;i=3004"), ["S5TIME"] = ("S5TIME", "ns=3;i=3005"),
        ["TIME"] = ("TIME", "ns=3;i=3006"), ["LTIME"] = ("LTIME", "ns=3;i=3007"), ["DATE"] = ("DATE", "ns=3;i=3008"),
        ["TOD"] = ("TOD", "ns=3;i=3009"), ["TIME_OF_DAY"] = ("TOD", "ns=3;i=3009"), ["LTOD"] = ("LTOD", "ns=3;i=3010"),
        ["LTIME_OF_DAY"] = ("LTOD", "ns=3;i=3010"), ["DT"] = ("DT", "ns=3;i=3011"), ["DATE_AND_TIME"] = ("DT", "ns=3;i=3011"),
        ["CHAR"] = ("CHAR", "ns=3;i=3012"), ["WCHAR"] = ("WCHAR", "ns=3;i=3013"), ["STRING"] = ("STRING", "ns=3;i=3014"),
    };

    /// <summary>DTL's members.</summary>
    private static readonly (string Name, string Type)[] DtlMembers = [
        ("YEAR", "UInt"), ("MONTH", "USInt"), ("DAY", "USInt"), ("WEEKDAY", "USInt"), ("HOUR", "USInt"), ("MINUTE", "USInt"),
        ("SECOND", "USInt"), ("NANOSECOND", "UDInt"),
    ];

    private static readonly (string Name, string Type, bool Writable)[] Timer = [("PT", "Time", true), ("ET", "Time", false), ("IN", "Bool", true), ("Q", "Bool", false)];
    private static readonly (string Name, string Type, bool Writable)[] LTimer = [("PT", "LTime", true), ("ET", "LTime", false), ("IN", "Bool", true), ("Q", "Bool", false)];
    private static readonly (string Name, string Type, bool Writable)[] Counter = [
        ("CU", "Bool", true), ("CD", "Bool", true), ("R", "Bool", true), ("LD", "Bool", true), ("QU", "Bool", true), ("QD", "Bool", true),
        ("PV", "Int", true), ("CV", "Int", true),
    ];

    /// <summary>System types whose members TIA's export lists, named without quotes, by the type TIA gives them.</summary>
    private static readonly Dictionary<string, (string Type, (string Name, string Type, bool Writable)[] Members)> SystemTypes = new(StringComparer.Ordinal) {
        ["DTL"] = ("DTL", [.. DtlMembers.Select(member => (member.Name, member.Type, true))]),
        ["IEC_TIMER"] = ("IEC_TIMER", Timer), ["TON_TIME"] = ("IEC_TIMER", Timer), ["TOF_TIME"] = ("IEC_TIMER", Timer),
        ["TP_TIME"] = ("IEC_TIMER", Timer), ["TONR_TIME"] = ("IEC_TIMER", Timer),
        ["IEC_LTIMER"] = ("IEC_LTIMER", LTimer), ["TON_LTIME"] = ("IEC_LTIMER", LTimer), ["TOF_LTIME"] = ("IEC_LTIMER", LTimer),
        ["TP_LTIME"] = ("IEC_LTIMER", LTimer), ["TONR_LTIME"] = ("IEC_LTIMER", LTimer),
        ["IEC_COUNTER"] = ("IEC_COUNTER", Counter), ["CTU_INT"] = ("IEC_COUNTER", Counter), ["CTD_INT"] = ("IEC_COUNTER", Counter),
        ["CTUD_INT"] = ("IEC_COUNTER", Counter),
    };

    [GeneratedRegex(@"^Array\s*\[(?<bounds>[^\]]*)\]\s*of\s+(?<element>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArrayType();

    private static string Quote(string name) => "\"" + name + "\"";

    private static string Base(string type) {
        var bracket = type.IndexOf('[', StringComparison.Ordinal);
        return (bracket < 0 ? type : type[..bracket]).Trim().ToUpperInvariant();
    }

    /// <summary>The length of each dimension, or null if a bound is not a number.</summary>
    private static int[]? Dimensions(string bounds) {
        var parts = bounds.Split(',');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++) {
            var range = parts[i].Split("..", StringSplitOptions.TrimEntries);
            if (range.Length != 2
                || !long.TryParse(range[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var low)
                || !long.TryParse(range[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var high)
                || high < low || high - low >= int.MaxValue) {
                return null;
            }
            result[i] = (int)(high - low + 1);
        }
        return result;
    }

    /// <summary>Every index of an array in reading order, counted from 0 in each dimension, as <c>0,1</c>.</summary>
    private static IEnumerable<string> Indices(int[] dimensions) {
        var index = new int[dimensions.Length];
        if (dimensions.Any(length => length <= 0)) {
            yield break;
        }
        while (true) {
            yield return string.Join(",", index.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var d = dimensions.Length - 1;
            while (d >= 0 && ++index[d] == dimensions[d]) {
                index[d] = 0;
                d--;
            }
            if (d < 0) {
                yield break;
            }
        }
    }

    private static TiaExternalAccess Combine(TiaExternalAccess inherited, TiaExternalAccess? own) => (inherited, own ?? TiaExternalAccess.ReadWrite) switch {
        (TiaExternalAccess.None, _) or (_, TiaExternalAccess.None) => TiaExternalAccess.None,
        (TiaExternalAccess.ReadOnly, _) or (_, TiaExternalAccess.ReadOnly) => TiaExternalAccess.ReadOnly,
        _ => TiaExternalAccess.ReadWrite,
    };

    // Building ------------------------------------------------------------------------------------------------------

    /// <summary>Where a node goes: its parent, its browse path, and the reference type from the parent.</summary>
    private readonly record struct Parent(string NodeId, string BrowsePath, string Reference);

    private sealed class Builder(TiaProgram program, string plc, List<TiaBlock> blocks) {
        private const string Ns = "ns=3;s=";
        private readonly Dictionary<string, TiaBlock> _types = blocks.Where(block => block.Kind is "UDT" or "FB")
            .GroupBy(block => block.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        private readonly Dictionary<TiaBlock, IReadOnlyList<TiaMember>> _interfaces = [];
        private readonly HashSet<string> _folders = [];

        public List<TiaOpcUaNode> Nodes { get; } = [];

        private Parent PlcNode => new(Ns + "PLC", plc, HasComponent);

        public void Build(List<TiaBlock> owned, IEnumerable<TiaTagTable> tables) {
            Nodes.Add(new TiaOpcUaNode(Ns + "PLC", TiaOpcUaNodeClass.Object, plc, plc, "ns=2;i=5001", HasComponent) { TypeDefinition = "ns=3;i=1002" });
            foreach (var folder in (string[])["Memory", "Inputs", "Outputs", "DataBlocksGlobal", "DataBlocksInstance"]) {
                Folder(folder);
            }
            foreach (var table in tables) {
                foreach (var tag in table.Tags.Where(tag => tag.Value is null)) {
                    Tag(tag);
                }
            }
            foreach (var block in owned.Where(block => block.Kind.StartsWith("DB", StringComparison.Ordinal))
                         .OrderBy(block => block.Number ?? long.MaxValue).ThenBy(block => block.Name, StringComparer.Ordinal)) {
                DataBlock(block);
            }
        }

        private Parent Folder(string name) {
            var id = Ns + name;
            if (_folders.Add(name)) {
                Nodes.Add(new TiaOpcUaNode(id, TiaOpcUaNodeClass.Object, name, plc + "/" + name, PlcNode.NodeId, HasComponent) { TypeDefinition = "ns=2;i=1005" });
            }
            return new Parent(id, plc + "/" + name, Organizes);
        }

        private IReadOnlyList<TiaMember> Interface(TiaBlock block) =>
            _interfaces.TryGetValue(block, out var members) ? members : _interfaces[block] = block.IsKnowHowProtected ? [] : program.ReadInterface(block);

        private void Tag(TiaTag tag) {
            var access = tag.ExternalAccess ?? TiaExternalAccess.ReadWrite;
            if (access == TiaExternalAccess.None || tag.Type is not { } type) {
                return;
            }
            var area = tag.Address?.TrimStart('%') is { Length: > 0 } address ? char.ToUpperInvariant(address[0]) : ' ';
            var folder = area switch {
                'I' or 'E' => "Inputs",
                'Q' or 'A' => "Outputs",
                'M' => "Memory",
                'T' => "Timers",
                'C' or 'Z' => "Counters",
                _ => null,
            };
            if (folder is null) {
                return;
            }
            Typed(type, [], Quote(tag.Name), tag.Name, Folder(folder), Quote(tag.Name), access, tag.Comment, tag.Name);
        }

        private void DataBlock(TiaBlock block) {
            if (!program.DataBlockSettings(block).AccessibleFromOpcUa) {
                return;
            }
            var type = program.GeneratedFrom(block) is { } from && _types.TryGetValue(from, out var found) ? found : null;
            var instance = type?.Kind == "FB";
            var folder = Folder(instance ? "DataBlocksInstance" : "DataBlocksGlobal");
            var id = Quote(block.Name);
            var start = Nodes.Count;
            var node = new TiaOpcUaNode(Ns + id, TiaOpcUaNodeClass.Object, block.Name, folder.BrowsePath + "/" + block.Name, folder.NodeId, Organizes) {
                TypeDefinition = instance ? Ns + "OT_" + Quote(type!.Name) : "i=58",
                Source = block.Name,
                MembersUnknown = block.IsKnowHowProtected,
                Description = program.BlockComment(block),
            };
            Nodes.Add(node);
            if (block.IsKnowHowProtected) {
                return;
            }
            var self = new Parent(node.NodeId, node.BrowsePath, HasComponent);
            if (instance) {
                Sections(Interface(type!), self, id, Quote(type!.Name), block.Name);
            } else {
                var members = type is not null ? Interface(type) : Interface(block);
                var owner = type is not null ? Quote(type.Name) : id;
                foreach (var member in members) {
                    Member(member, self, id, owner, TiaExternalAccess.ReadWrite, block.Name);
                }
            }
            if (Nodes.Count == start + 1) {
                Nodes.RemoveAt(start);
            }
        }

        /// <summary>An FB's members in objects for their sections; the members' ids leave the section out.</summary>
        private void Sections(IReadOnlyList<TiaMember> members, Parent owner, string id, string type, string source) {
            foreach (var (section, name, reference) in (ReadOnlySpan<(string, string, string)>)[
                         ("Input", "Inputs", "HasInputVar"), ("Output", "Outputs", "HasOutputVar"), ("InOut", "InOuts", "HasInOutVar"), ("Static", "Static", "HasLocalVar")]) {
                var start = Nodes.Count;
                var folder = new TiaOpcUaNode(owner.NodeId + "." + name, TiaOpcUaNodeClass.Object, name, owner.BrowsePath + "/" + name, owner.NodeId, reference) {
                    TypeDefinition = "i=61",
                    Source = source,
                };
                Nodes.Add(folder);
                var parent = new Parent(folder.NodeId, folder.BrowsePath, reference);
                foreach (var member in members.Where(member => member.Section == section)) {
                    Member(member, parent, id, type, TiaExternalAccess.ReadWrite, source);
                }
                if (Nodes.Count == start + 1) {
                    Nodes.RemoveAt(start);
                }
            }
        }

        /// <summary>A member under <paramref name="parent"/>; <paramref name="owner"/> names the type its structs belong to.</summary>
        private void Member(TiaMember member, Parent parent, string idPrefix, string owner, TiaExternalAccess inherited, string source) {
            if (member.Section is "Temp" or "Constant" or "Return") {
                return;
            }
            var access = Combine(inherited, member.ExternalAccess);
            if (access == TiaExternalAccess.None) {
                return;
            }
            Typed(member.Type, member.Children, idPrefix + "." + Quote(member.Name), member.Name, parent, owner + "." + Quote(member.Name), access, member.Comment, source);
        }

        /// <summary>A node of the given type: its own and the ones below it. Returns it, or null if it has none.</summary>
        private TiaOpcUaNode? Typed(string type, IReadOnlyList<TiaMember> children, string id, string browseName, Parent parent, string structKey,
            TiaExternalAccess access, string? description, string source) {
            var path = parent.BrowsePath + "/" + browseName;
            var self = new Parent(Ns + id, path, HasComponent);
            var node = new TiaOpcUaNode(Ns + id, TiaOpcUaNodeClass.Variable, browseName, path, parent.NodeId, parent.Reference) {
                SimaticType = type,
                Description = string.IsNullOrEmpty(description) ? null : description,
                Source = source,
            };

            if (ArrayType().Match(type) is { Success: true } array) {
                var element = array.Groups["element"].Value.Trim();
                var dimensions = Dimensions(array.Groups["bounds"].Value);
                var (dataType, name, _) = DataTypeOf(element, structKey);
                var index = Add(node with {
                    DataType = dataType, DataTypeName = name, ArrayDimensions = dimensions ?? [],
                    TypeDefinition = IsStructured(element) ? "i=62" : "i=63",
                });
                // An array keeps its own access, even where its elements are read-only. TIA's export lists the elements
                // even with its option to export them as separate nodes off.
                if (dimensions is not null) {
                    foreach (var position in Indices(dimensions)) {
                        Typed(element, children, id + "[" + position + "]", position, self, structKey, access, null, source);
                    }
                }
                return Nodes[index] = Nodes[index] with { IsWritable = access == TiaExternalAccess.ReadWrite };
            }

            var (typeId, typeName, typeDefinition) = DataTypeOf(type, structKey);
            if (Base(type) == "DATE_AND_TIME" || Base(type) == "DT") {
                return Nodes[Add(node with { DataType = typeId, DataTypeName = typeName, ArrayDimensions = [8], TypeDefinition = "i=63", IsWritable = access == TiaExternalAccess.ReadWrite })];
            }
            if (type.Length > 1 && type[0] == '"' && _types.TryGetValue(type.Trim('"'), out var block) && block.Kind == "FB") {
                // A multi-instance: an object with sections, as an instance data block.
                var instance = node with { NodeClass = TiaOpcUaNodeClass.Object, TypeDefinition = Ns + "OT_" + type };
                var start = Add(instance);
                Sections(Interface(block), self, id, type, source);
                if (Nodes.Count == start + 1) {
                    Nodes.RemoveAt(start);
                    return null;
                }
                return instance;
            }

            var at = Add(node with { DataType = typeId, DataTypeName = typeName, TypeDefinition = typeDefinition });
            var below = true;
            var hasBelow = false;
            if (SystemTypes.TryGetValue(Base(type), out var system)) {
                foreach (var (name, memberType, writable) in system.Members) {
                    var item = Typed(memberType, [], id + "." + name, name, self, structKey, writable ? access : Combine(access, TiaExternalAccess.ReadOnly), null, source);
                    below &= item?.IsWritable ?? true;
                }
            } else if (type.Length > 1 && type[0] == '"' && _types.TryGetValue(type.Trim('"'), out var udt)) {
                foreach (var member in Interface(udt)) {
                    var item = MemberNode(member, self, id, type, access, source);
                    hasBelow |= item is not null;
                    below &= item?.IsWritable ?? true;
                }
                if (!hasBelow && Interface(udt).Count != 0) {
                    Nodes.RemoveAt(at);
                    return null;
                }
            } else if (string.Equals(type, "Struct", StringComparison.OrdinalIgnoreCase)) {
                foreach (var member in children) {
                    var item = MemberNode(member, self, id, structKey, access, source);
                    hasBelow |= item is not null;
                    below &= item?.IsWritable ?? true;
                }
                if (!hasBelow && children.Count != 0) {
                    Nodes.RemoveAt(at);
                    return null;
                }
            }
            return Nodes[at] = Nodes[at] with { IsWritable = access == TiaExternalAccess.ReadWrite && below };
        }

        private TiaOpcUaNode? MemberNode(TiaMember member, Parent parent, string idPrefix, string owner, TiaExternalAccess inherited, string source) {
            var count = Nodes.Count;
            Member(member, parent, idPrefix, owner, inherited, source);
            return Nodes.Count > count ? Nodes[count] : null;
        }

        private int Add(TiaOpcUaNode node) {
            Nodes.Add(node);
            return Nodes.Count - 1;
        }

        private bool IsStructured(string type) =>
            string.Equals(type, "Struct", StringComparison.OrdinalIgnoreCase) || (type.Length > 1 && type[0] == '"') || !Elementary.ContainsKey(Base(type));

        /// <summary>The data type's node id, its name in the list, and the variable's type definition.</summary>
        private (string Id, string Name, string TypeDefinition) DataTypeOf(string type, string structKey) {
            if (Elementary.TryGetValue(Base(type), out var elementary)) {
                return (elementary.NodeId, elementary.Alias, "i=63");
            }
            var key = string.Equals(type, "Struct", StringComparison.OrdinalIgnoreCase) ? structKey
                : type.Length > 1 && type[0] == '"' ? type
                : SystemTypes.TryGetValue(Base(type), out var system) ? system.Type
                : Base(type);
            return (Ns + "DT_" + key, "DT_" + key, Ns + "VT_" + key);
        }
    }

    // Writing -------------------------------------------------------------------------------------------------------

    /// <summary>The nodes as tab-separated text: a note, a header line, then one line per node.</summary>
    public static void WriteText(IEnumerable<TiaOpcUaNodeList> lists, TextWriter output) {
        output.WriteLine("# " + Derived);
        output.WriteLine("PLC\tNodeId\tNodeClass\tBrowsePath\tSimaticType\tDataType\tArrayDimensions\tAccess\tSource");
        foreach (var list in lists) {
            foreach (var node in list.Nodes) {
                var line = new StringBuilder();
                line.Append(Cell(list.Plc)).Append('\t').Append(Cell(node.NodeId)).Append('\t').Append(node.NodeClass).Append('\t')
                    .Append(Cell(node.BrowsePath)).Append('\t').Append(Cell(node.SimaticType)).Append('\t').Append(Cell(node.DataTypeName)).Append('\t')
                    .Append(node.ArrayDimensions is { } dimensions ? string.Join(",", dimensions.Select(d => d.ToString(CultureInfo.InvariantCulture))) : "").Append('\t')
                    .Append(Access(node)).Append('\t').Append(Cell(node.Source));
                output.WriteLine(line.ToString());
            }
        }
    }

    /// <summary>A node's access as the list says it: read-write, read-only, or members unknown; empty for a folder.</summary>
    public static string Access(TiaOpcUaNode node) => node switch {
        { MembersUnknown: true } => "members unknown",
        { NodeClass: TiaOpcUaNodeClass.Variable, IsWritable: true } => "read-write",
        { NodeClass: TiaOpcUaNodeClass.Variable } => "read-only",
        _ => "",
    };

    private static string Cell(string? text) => text is null ? "" : text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
