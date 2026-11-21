using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Xml;

namespace TiaFormat.Schema;

/// <summary>The self-describing schema of a database: value types, interfaces, object types and their layout.</summary>
public sealed class TiaSchema {
    private const string CoreTextTypeName = "Siemens.Automation.ObjectFrame.CoreText";

    private readonly Dictionary<string, TiaValueType> _valuesByName = [];
    private readonly Dictionary<string, TiaAttributeSet> _sets = [];
    private readonly Dictionary<string, TiaObjectType> _objectsByName = [];
    private readonly Dictionary<uint, TiaObjectType> _objects = [];
    private readonly Dictionary<string, List<uint>> _relationIdsByName = [];
    private readonly Dictionary<uint, string> _relationNames = [];
    private readonly ConcurrentDictionary<(string, string), TiaAttributeHandle> _attributeHandles = new();
    private readonly ConcurrentDictionary<string, TiaRelationHandle> _relationHandles = new();

    private TiaSchema() {
        void Add(uint id, string name, TiaValueKind kind, int size) => _valuesByName[name] = new TiaValueType(id, name, kind, size);
        Add(0x80000001, "xs:boolean", TiaValueKind.Boolean, 1);
        Add(0x80000002, "xs:unsignedByte", TiaValueKind.UInt8, 1);
        Add(0x80000003, "pe:CharT", TiaValueKind.Char, 2);
        Add(0x80000004, "xs:short", TiaValueKind.Int16, 2);
        Add(0x80000005, "xs:int", TiaValueKind.Int32, 4);
        Add(0x80000006, "xs:long", TiaValueKind.Int64, 8);
        Add(0x80000007, "xs:float", TiaValueKind.Float, 4);
        Add(0x80000008, "xs:double", TiaValueKind.Double, 8);
        Add(0x80000009, "xs:decimal", TiaValueKind.Decimal, 4);
        Add(0x8000000a, "xs:dateTime", TiaValueKind.DateTime, 8);
        Add(0x8000000b, "xs:string", TiaValueKind.String, 4);
        Add(0x8000000c, "pe:GuidT", TiaValueKind.Guid, 16);
        Add(0x8000000d, "pe:BlobT", TiaValueKind.Blob, 4);
        Add(0x8000000e, "pe:XmlT", TiaValueKind.Xml, 4);
        Add(0x80000012, "xs:ushort", TiaValueKind.UInt16, 2);
        Add(0x80000013, "xs:uint", TiaValueKind.UInt32, 4);
        Add(0x80000014, "xs:ulong", TiaValueKind.UInt64, 8);
        Add(0x80000015, "pe:MapT", TiaValueKind.Map, 4);
        Add(0x8000001f, "pe:CoreTextAttributeT", TiaValueKind.CoreText, 4);
    }

    /// <summary>Every object type the file declares.</summary>
    public IReadOnlyCollection<TiaObjectType> ObjectTypes => _objects.Values;

    public TiaObjectType? FindObjectType(uint id) => _objects.GetValueOrDefault(id);

    public TiaObjectType? FindObjectType(string fullName) => _objectsByName.GetValueOrDefault(fullName);

    public string? FindRelationName(uint id) => _relationNames.GetValueOrDefault(id);

    private FrozenDictionary<uint, TiaValueType>? _valuesById;

    /// <summary>A value type by its id, as a dictionary expando's key table names it (section 7.7 of the format page).</summary>
    public TiaValueType? FindValueType(uint id) {
        _valuesById ??= _valuesByName.Values.Where(value => value.Id != 0).GroupBy(value => value.Id).ToFrozenDictionary(group => group.Key, group => group.First());
        return _valuesById.GetValueOrDefault(id);
    }

    /// <summary>Handle for an attribute of an interface, across every object type that implements it.</summary>
    public TiaAttributeHandle Attribute(string setFullName, string attributeName) =>
        _attributeHandles.GetOrAdd((setFullName, attributeName), key => {
            var map = new Dictionary<uint, (int, TiaAttributeSlot)>();
            foreach (var type in _objects.Values) {
                var interfaces = type.Interfaces;
                for (var i = 0; i < interfaces.Length; i++) {
                    if (interfaces[i].Set.FullName != key.Item1) {
                        continue;
                    }
                    foreach (var slot in interfaces[i].Slots) {
                        if (slot.Name == key.Item2) {
                            map[type.Id] = (i, slot);
                        }
                    }
                }
            }
            return new TiaAttributeHandle(map.ToFrozenDictionary());
        });

    /// <summary>Handle for all relations of a bare name, such as <c>Sources</c>.</summary>
    public TiaRelationHandle Relation(string name) =>
        _relationHandles.GetOrAdd(name, n => new TiaRelationHandle(_relationIdsByName.TryGetValue(n, out var ids) ? [.. ids] : []));

    /// <summary>Parses the schema XML. Hold the last one in the file: schemas only grow.</summary>
    public static TiaSchema Parse(Stream xml) {
        var schema = new TiaSchema();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        schema.Load(reader);
        schema.Resolve();
        return schema;
    }

    private static string Qualify(string name, string ns) => name.Contains('.', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal) ? name : ns + "." + name;

    private static uint ParseId(string? text) {
        if (text is null) {
            throw new TiaFormatException("A schema element has no id.");
        }
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(text, CultureInfo.InvariantCulture);
    }

    private static long ParseLong(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(text, CultureInfo.InvariantCulture);

    private void Load(XmlReader r) {
        string ns = "";
        while (r.Read()) {
            if (r.NodeType != XmlNodeType.Element) {
                continue;
            }
            switch (r.LocalName) {
                case "Namespace":
                    ns = r.GetAttribute("name") ?? "";
                    break;
                case "Enumeration":
                    LoadEnumeration(r, ns);
                    break;
                case "Structure":
                    AddValue(new TiaValueType(ParseId(r.GetAttribute("id")), ns + "." + r.GetAttribute("name"), TiaValueKind.Structure, 4));
                    Skip(r);
                    break;
                case "Array":
                    AddValue(new TiaValueType(ParseId(r.GetAttribute("id")), ns + "." + r.GetAttribute("name"), TiaValueKind.Array, 4));
                    Skip(r);
                    break;
                case "AttributeSet":
                    LoadAttributeSet(r, ns);
                    break;
                case "ObjectType":
                    LoadObjectType(r, ns);
                    break;
            }
        }
    }

    private static void Skip(XmlReader r) {
        if (r.IsEmptyElement) {
            return;
        }
        var depth = r.Depth;
        while (r.Read() && r.Depth > depth) {
        }
    }

    private void AddValue(TiaValueType type) => _valuesByName[type.Name] = type;

    private void LoadEnumeration(XmlReader r, string ns) {
        var type = new TiaValueType(ParseId(r.GetAttribute("id")), ns + "." + r.GetAttribute("name"), TiaValueKind.Enumeration, 4) {
            BaseName = r.GetAttribute("base") ?? "xs:int",
            IsFlags = r.GetAttribute("flags") == "true",
        };
        var constants = new Dictionary<long, string>();
        if (!r.IsEmptyElement) {
            var depth = r.Depth;
            while (r.Read() && r.Depth > depth) {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Constant") {
                    constants.TryAdd(ParseLong(r.GetAttribute("value") ?? "0"), r.GetAttribute("name") ?? "");
                }
            }
        }
        type.Constants = constants.ToFrozenDictionary();
        AddValue(type);
    }

    private void LoadAttributeSet(XmlReader r, string ns) {
        var name = ns + "." + r.GetAttribute("name");
        var expando = r.GetAttribute("expando") switch {
            "simple" => TiaExpandoMode.Simple,
            "true" or "dictionary" => TiaExpandoMode.Dictionary,
            _ => TiaExpandoMode.None,
        };
        var attributes = new List<TiaAttributeDefinition>();
        if (!r.IsEmptyElement) {
            var depth = r.Depth;
            while (r.Read() && r.Depth > depth) {
                if (r.NodeType == XmlNodeType.Element && r.Depth == depth + 1 && r.LocalName == "Attribute") {
                    attributes.Add(new TiaAttributeDefinition(
                        r.GetAttribute("name") ?? "",
                        Qualify(r.GetAttribute("type") ?? "", ns),
                        r.GetAttribute("constant") == "true",
                        r.GetAttribute("intrinsic") == "true",
                        r.GetAttribute("virtual") == "true"));
                }
            }
        }
        _sets[name] = new TiaAttributeSet(name, expando, [.. attributes]);
    }

    private void LoadObjectType(XmlReader r, string ns) {
        var type = new TiaObjectType(ParseId(r.GetAttribute("id")), ns + "." + r.GetAttribute("name"));
        _objects[type.Id] = type;
        _objectsByName[type.FullName] = type;
        if (r.IsEmptyElement) {
            return;
        }
        var depth = r.Depth;
        List<(string, bool?)>? implementation = null;
        TiaRelationDefinition? relation = null;
        while (r.Read() && r.Depth > depth) {
            if (r.NodeType != XmlNodeType.Element) {
                continue;
            }
            if (r.Depth == depth + 1) {
                implementation = null;
                relation = null;
                switch (r.LocalName) {
                    case "Base":
                        var baseName = Qualify(r.GetAttribute("ref") ?? "", ns);
                        type.BaseNames.Add(baseName);
                        if (r.GetAttribute("primary") == "true") {
                            type.PrimaryName = baseName;
                        }
                        break;
                    case "Implements":
                        implementation = [];
                        type.ImplementationNames.Add((Qualify(r.GetAttribute("ref") ?? "", ns), implementation));
                        break;
                    case "Relation":
                        relation = new TiaRelationDefinition(r.GetAttribute("name") ?? "", ParseId(r.GetAttribute("id")), r.GetAttribute("cardinality"), r.GetAttribute("abstract") == "true");
                        type.Relations.Add(relation);
                        break;
                }
                continue;
            }
            if (r.Depth != depth + 2) {
                continue;
            }
            if (implementation is not null && r.LocalName == "Attribute") {
                var constant = r.GetAttribute("constant");
                implementation.Add((r.GetAttribute("name") ?? "", constant is null ? null : constant == "true"));
            } else if (relation is not null && r.LocalName == "Target") {
                _pendingTarget[relation.Id] = Qualify(r.GetAttribute("ref") ?? "", ns);
            } else if (relation is not null && r.LocalName == "Inverse" && r.GetAttribute("id") is { } inverseId) {
                type.DeclaredInverses.Add((relation.Id.ToString(CultureInfo.InvariantCulture), new TiaRelationDefinition(r.GetAttribute("name") ?? "", ParseId(inverseId), r.GetAttribute("cardinality"), r.GetAttribute("abstract") == "true")));
            }
        }
    }

    private readonly Dictionary<uint, string> _pendingTarget = [];

    private void Resolve() {
        foreach (var value in _valuesByName.Values) {
            if (value.BaseName is not null && _valuesByName.TryGetValue(value.BaseName, out var b)) {
                value.Size = b.Size;
                value.StorageKind = b.Kind;
            }
        }
        foreach (var set in _sets.Values) {
            foreach (var attribute in set.Attributes) {
                attribute.Type = _valuesByName.TryGetValue(attribute.TypeName, out var t) ? t : new TiaValueType(0, attribute.TypeName, TiaValueKind.ObjectReference, 0);
            }
        }
        foreach (var type in _objects.Values) {
            foreach (var name in type.BaseNames) {
                var resolved = _objectsByName.GetValueOrDefault(name) ?? throw new TiaFormatException($"Unknown base type {name} of {type.FullName}.");
                type.BaseTypes.Add(resolved);
                if (name == type.PrimaryName) {
                    type.Primary = resolved;
                }
            }
            foreach (var (setName, attributes) in type.ImplementationNames) {
                var set = _sets.GetValueOrDefault(setName) ?? throw new TiaFormatException($"Unknown interface {setName} of {type.FullName}.");
                type.Implementations.Add((set, attributes));
            }
        }
        foreach (var type in _objects.Values) {
            foreach (var relation in type.Relations) {
                AddRelationName(relation.Name, relation.Id);
            }
            foreach (var (relationIdText, inverse) in type.DeclaredInverses) {
                AddRelationName(inverse.Name, inverse.Id);
                var relationId = uint.Parse(relationIdText, CultureInfo.InvariantCulture);
                if (_pendingTarget.TryGetValue(relationId, out var target) && _objectsByName.TryGetValue(target, out var targetType)) {
                    targetType.InverseRelations.Add(inverse);
                }
            }
        }
        foreach (var type in _objects.Values) {
            BuildLayout(type);
        }
        _pendingTarget.Clear();
    }

    private void AddRelationName(string name, uint id) {
        if (!_relationIdsByName.TryGetValue(name, out var ids)) {
            _relationIdsByName[name] = ids = [];
        }
        if (!ids.Contains(id)) {
            ids.Add(id);
        }
        _relationNames.TryAdd(id, name);
    }

    private static void BuildLayout(TiaObjectType type) {
        var collected = new Dictionary<TiaAttributeSet, Dictionary<string, bool>>();
        Visit(type, true, collected);

        var interfaces = new List<TiaInterfaceLayout>(collected.Count);
        foreach (var (set, overrides) in collected) {
            var slots = new List<TiaAttributeSlot>();
            var offset = 4;
            foreach (var attribute in set.Attributes) {
                if (!overrides.TryGetValue(attribute.Name, out var persistent)) {
                    persistent = !attribute.IsConstant && !attribute.IsIntrinsic;
                }
                if (persistent) {
                    slots.Add(new TiaAttributeSlot(attribute.Name, attribute.Type!, offset));
                    offset += attribute.Type!.Size;
                }
            }
            interfaces.Add(new TiaInterfaceLayout(set, [.. slots]));
        }
        interfaces.Sort((a, b) => string.CompareOrdinal(a.Set.FullName, b.Set.FullName));
        type.Interfaces = [.. interfaces];

        var found = new Dictionary<uint, string>();
        foreach (var t in type.SelfAndBases()) {
            type.IsCoreText |= t.FullName == CoreTextTypeName;
            foreach (var relation in t.Relations) {
                if (relation.Cardinality != "1" && !relation.IsAbstract) {
                    found[relation.Id] = t.FullName + "." + relation.Name;
                }
            }
            foreach (var inverse in t.InverseRelations) {
                if (inverse.Cardinality != "1" && !inverse.IsAbstract) {
                    found.TryAdd(inverse.Id, t.FullName + "." + inverse.Name);
                }
            }
        }
        var byName = new Dictionary<string, uint>();
        foreach (var (id, name) in found.OrderBy(e => e.Key)) {
            byName.TryAdd(name, id);
        }
        type.ToManyRelations = [.. byName.OrderBy(e => Encoding.UTF8.GetBytes(e.Key), ByteOrder.Instance).Select(e => new TiaToManyRelation(e.Key, e.Value))];
    }

    private static void Visit(TiaObjectType type, bool primary, Dictionary<TiaAttributeSet, Dictionary<string, bool>> collected) {
        foreach (var (set, attributes) in type.Implementations) {
            if (!collected.TryGetValue(set, out var map)) {
                collected[set] = map = [];
            }
            foreach (var (name, constant) in attributes) {
                var original = set.Find(name);
                if (original is null) {
                    continue;
                }
                if (map.ContainsKey(name)) {
                    if (original.IsVirtual && primary && constant == false) {
                        map[name] = true;
                    }
                    continue;
                }
                map[name] = !(constant ?? original.IsConstant) && !original.IsIntrinsic;
            }
        }
        foreach (var b in type.BaseTypes) {
            Visit(b, primary && (type.Primary is null || ReferenceEquals(b, type.Primary)), collected);
        }
    }

    private sealed class ByteOrder : IComparer<byte[]> {
        public static readonly ByteOrder Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
