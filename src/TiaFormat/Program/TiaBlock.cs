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
}

/// <summary>One interface member. Children are a struct's members.</summary>
public sealed record TiaMember(string Name, string Type, string Path, string Section, string? Comment, string? StartValue, IReadOnlyList<TiaMember> Children);

/// <summary>One compile unit: a network, or an SCL body.</summary>
public readonly record struct TiaNetwork(int Number, string Language, string? Title, string Form);

/// <summary>One PLC tag, or a global constant when <see cref="Value"/> is set.</summary>
public sealed record TiaTag(string Name, string? Type, string? Address, string? Value, string? Comment);

/// <summary>A tag table with its tags in table order.</summary>
public sealed record TiaTagTable(TiaFrame Frame, string Name, IReadOnlyList<TiaTag> Tags);
