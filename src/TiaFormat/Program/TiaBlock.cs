using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A program block: what it is. Its interface and source are read on demand through <see cref="TiaProgram"/>.</summary>
public sealed record TiaBlock(
    TiaFrame Frame,
    string Name,
    string Kind,
    long? Number,
    string? Language,
    bool IsKnowHowProtected,
    string? Title) {
    /// <summary>A reference to a block held elsewhere: no number, interface or source of its own.</summary>
    public bool IsStub => (Frame.States & TiaFrameStates.Reference) != 0;

    /// <summary>
    /// An OB's event class, such as <c>ProgramCycle</c>, <c>Startup</c> or <c>CyclicInterrupt</c>: the part of its stored
    /// subtype after <c>OB.</c>. Null for any other block, and for an OB that stores none.
    /// </summary>
    public string? EventClass { get; init; }
}

/// <summary>A block's header: the fields of its properties page, and whether it has optimized access.</summary>
public readonly record struct TiaBlockHeader(string? Author, string? Family, string? Name, string? Version, bool OptimizedAccess);

/// <summary>A data block's settings: whether it is non-retain as a whole, and who may access it.</summary>
public readonly record struct TiaDataBlockSettings(bool IsNonRetain, bool AccessibleFromOpcUa, bool AccessibleFromWebserver);

/// <summary>A start value set in a data block itself. Name is the stored path when no member explains it.</summary>
public readonly record struct TiaSetValue(string Name, string Value, bool IsResolved);

/// <summary>
/// One interface member. Children are a struct's members. <see cref="StartValue"/> is the value it starts with in this
/// block, <see cref="Declared"/> the one its declaration states: what a source file writes after the type.
/// </summary>
public sealed record TiaMember(string Name, string Type, string Path, string Section, string? Comment, string? StartValue, IReadOnlyList<TiaMember> Children, bool IsRetain = false) {
    public string? Declared { get; init; }

    /// <summary>Set per instance: a source file gives it a section of its own, <c>VAR DB_SPECIFIC</c>.</summary>
    public bool IsDbSpecific { get; init; }

    /// <summary>The member this one overlays: <c>name AT other : type</c>.</summary>
    public string? AtMember { get; init; }

    /// <summary>Its attributes as a source file writes them, braces included; null if it has none.</summary>
    public string? Attributes { get; init; }

    /// <summary>
    /// What an HMI, OPC UA or Web API client may do with it, as its own flags or its parent's say; null where access
    /// does not apply: temporary and constant members, and members of a function or organization block.
    /// </summary>
    public TiaExternalAccess? ExternalAccess { get; init; }
}

/// <summary>
/// What an outside client (HMI, OPC UA, Web API) may do with a data block member or a PLC tag: TIA's settings
/// <c>ExternalAccessible</c>, <c>ExternalWritable</c> and <c>ExternalVisible</c> taken together.
/// </summary>
public enum TiaExternalAccess : byte {
    /// <summary>Accessible and writable.</summary>
    ReadWrite,

    /// <summary>Accessible, not writable.</summary>
    ReadOnly,

    /// <summary>Not accessible, so neither writable nor visible.</summary>
    None,
}

/// <summary>One compile unit: a network, or an SCL body.</summary>
public readonly record struct TiaNetwork(int Number, string Language, string? Title, string Form, string? Comment = null);

/// <summary>One PLC tag, or a global constant when <see cref="Value"/> is set.</summary>
public sealed record TiaTag(string Name, string? Type, string? Address, string? Value, string? Comment) {
    /// <summary>What an outside client may do with the tag; null for a constant, or a tag that stores no such setting.</summary>
    public TiaExternalAccess? ExternalAccess { get; init; }

    /// <summary>Whether the tag is offered in HMI engineering (<c>ExternalVisible</c>); null where it stores no such setting.</summary>
    public bool? ExternalVisible { get; init; }
}

/// <summary>A tag table with its tags in table order.</summary>
public sealed record TiaTagTable(TiaFrame Frame, string Name, IReadOnlyList<TiaTag> Tags);
