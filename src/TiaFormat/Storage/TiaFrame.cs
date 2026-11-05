namespace TiaFormat.Storage;

/// <summary>Identity of an object: its type id and object id together.</summary>
public readonly record struct TiaObjectKey(uint TypeId, ulong ObjectId);

/// <summary>Frame state bits.</summary>
[Flags]
public enum TiaFrameStates : ushort {
    None = 0,
    Deleted = 0x04,
    Destroyed = 0x08,

    /// <summary>A reference to an object held elsewhere; a block with it has no source of its own.</summary>
    Reference = 0x20,
}

/// <summary>Frame info bits.</summary>
[Flags]
public enum TiaFrameInfo : uint {
    None = 0,

    /// <summary>The offset table holds a low-filled relation list after the one-to-one list.</summary>
    LowFilledRelations = 1,
}

/// <summary>The fixed header of one frame in the file.</summary>
public readonly record struct TiaFrame(
    int Offset,
    int Size,
    uint TypeId,
    ulong ObjectId,
    uint ClusterId,
    TiaFrameInfo Info,
    TiaFrameStates States,
    byte OffsetCount,
    byte MetaId,
    int Sequence) {
    public TiaObjectKey Key => new(TypeId, ObjectId);
    public bool IsDeleted => (States & TiaFrameStates.Deleted) != 0;
}

/// <summary>One relation slot: which relation, and its target. An empty list place has a default target.</summary>
public readonly record struct TiaRelation(uint RelationId, TiaObjectKey Target) {
    public bool IsEmptyPlace => Target.TypeId == 0 && Target.ObjectId == 0;
}
