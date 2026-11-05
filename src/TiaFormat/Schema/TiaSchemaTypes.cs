using System.Collections.Frozen;

namespace TiaFormat.Schema;

/// <summary>How a value is stored in an attribute slot.</summary>
public enum TiaValueKind : byte {
    Boolean, UInt8, Char, Int16, Int32, Int64, Float, Double, Decimal, DateTime, String, Guid, Blob, Xml, UInt16, UInt32, UInt64, Map, CoreText,
    Enumeration, Structure, Array,

    /// <summary>An object: takes no space, relations hold it.</summary>
    ObjectReference,
}

/// <summary>A value type of the schema, built in or declared.</summary>
public sealed class TiaValueType(uint id, string name, TiaValueKind kind, int size) {
    public uint Id { get; } = id;
    public string Name { get; } = name;
    public TiaValueKind Kind { get; } = kind;
    public int Size { get; internal set; } = size;

    /// <summary>The base kind of an enumeration, which decides how it is read.</summary>
    public TiaValueKind StorageKind { get; internal set; } = kind;
    public string? BaseName { get; internal set; }
    public bool IsFlags { get; internal set; }
    public FrozenDictionary<long, string>? Constants { get; internal set; }
}

/// <summary>An attribute as an attribute set declares it.</summary>
public sealed class TiaAttributeDefinition(string name, string typeName, bool isConstant, bool isIntrinsic, bool isVirtual) {
    public string Name { get; } = name;
    public string TypeName { get; } = typeName;
    public bool IsConstant { get; } = isConstant;
    public bool IsIntrinsic { get; } = isIntrinsic;
    public bool IsVirtual { get; } = isVirtual;
    public TiaValueType? Type { get; internal set; }
}

/// <summary>How an attribute set stores attributes the schema does not list.</summary>
public enum TiaExpandoMode : byte { None, Dictionary, Simple }

/// <summary>An interface: a named list of attributes.</summary>
public sealed class TiaAttributeSet(string fullName, TiaExpandoMode expando, TiaAttributeDefinition[] attributes) {
    public string FullName { get; } = fullName;
    public TiaExpandoMode Expando { get; } = expando;
    public IReadOnlyList<TiaAttributeDefinition> Attributes { get; } = attributes;
    internal TiaAttributeDefinition? Find(string name) => Array.Find(attributes, a => a.Name == name);
}

/// <summary>A persistent attribute's place inside its interface segment.</summary>
public readonly record struct TiaAttributeSlot(string Name, TiaValueType Type, int Offset);

/// <summary>One interface of an object type, with the slots its segment holds.</summary>
public sealed class TiaInterfaceLayout(TiaAttributeSet set, TiaAttributeSlot[] slots) {
    public TiaAttributeSet Set { get; } = set;
    public IReadOnlyList<TiaAttributeSlot> Slots { get; } = slots;
}

/// <summary>A to-many relation of a type, as it orders the per-relation lists.</summary>
public readonly record struct TiaToManyRelation(string QualifiedName, uint Id);

/// <summary>A relation declared on an object type, or on the target of an inverse.</summary>
public sealed record TiaRelationDefinition(string Name, uint Id, string? Cardinality, bool IsAbstract);

/// <summary>An object type with its resolved frame layout.</summary>
public sealed class TiaObjectType(uint id, string fullName) {
    internal readonly List<TiaObjectType> BaseTypes = [];
    internal readonly List<string> BaseNames = [];
    internal string? PrimaryName;
    internal TiaObjectType? Primary;
    internal readonly List<(string SetName, List<(string Name, bool? Constant)> Attributes)> ImplementationNames = [];
    internal readonly List<(TiaAttributeSet Set, List<(string Name, bool? Constant)> Attributes)> Implementations = [];
    internal readonly List<TiaRelationDefinition> Relations = [];
    internal readonly List<TiaRelationDefinition> InverseRelations = [];
    internal readonly List<(string TargetName, TiaRelationDefinition Inverse)> DeclaredInverses = [];

    public uint Id { get; } = id;
    public string FullName { get; } = fullName;
    public TiaInterfaceLayout[] Interfaces { get; internal set; } = [];
    public TiaToManyRelation[] ToManyRelations { get; internal set; } = [];
    public bool IsCoreText { get; internal set; }

    internal IEnumerable<TiaObjectType> SelfAndBases() {
        var seen = new HashSet<TiaObjectType>();
        var stack = new Stack<TiaObjectType>();
        stack.Push(this);
        while (stack.TryPop(out var type)) {
            if (!seen.Add(type)) {
                continue;
            }
            yield return type;
            foreach (var b in type.BaseTypes) {
                stack.Push(b);
            }
        }
    }
}

/// <summary>Where one attribute sits, per object type that has it. Resolve once, read many times.</summary>
public sealed class TiaAttributeHandle {
    private readonly FrozenDictionary<uint, (int Interface, TiaAttributeSlot Slot)> _byType;

    internal TiaAttributeHandle(FrozenDictionary<uint, (int Interface, TiaAttributeSlot Slot)> byType) {
        _byType = byType;
    }

    internal bool TryResolve(uint typeId, out int interfaceIndex, out TiaAttributeSlot slot) {
        if (_byType.TryGetValue(typeId, out var entry)) {
            interfaceIndex = entry.Interface;
            slot = entry.Slot;
            return true;
        }
        interfaceIndex = 0;
        slot = default;
        return false;
    }
}

/// <summary>All relation ids carrying one name.</summary>
public sealed class TiaRelationHandle(uint[] ids) {
    private readonly uint[] _ids = ids;
    public bool Matches(uint relationId) => Array.IndexOf(_ids, relationId) >= 0;
}
