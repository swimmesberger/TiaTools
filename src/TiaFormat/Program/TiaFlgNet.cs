using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>What one end of a wire in a LAD or FBD network is connected to.</summary>
public enum TiaFlgEndKind : byte {
    /// <summary>The power rail of a LAD network: always true.</summary>
    Powerrail,

    /// <summary>An operand: a tag, a member, a constant or a label.</summary>
    Operand,

    /// <summary>A pin of a part.</summary>
    Pin,

    /// <summary>An open connection: an output nothing reads.</summary>
    Open,
}

/// <summary>One end of a wire: the power rail, an operand by its id, or a part's pin.</summary>
public readonly record struct TiaFlgEnd(TiaFlgEndKind Kind, int Uid = 0, string? Pin = null);

/// <summary>
/// A part of a network: a gate such as <c>Contact</c> or <c>A</c>, an instruction with an instance such as <c>TON</c>, or a
/// call of a block. Its pins are named by the wires that reach it.
/// </summary>
public sealed class TiaFlgPart(int uid, string name) {
    public int Uid { get; } = uid;

    /// <summary>The gate or instruction (<c>Contact</c>, <c>Move</c>, <c>TON</c>), or the called block as a source names it.</summary>
    public string Name { get; set; } = name;

    /// <summary>A call of a block, whose pins are its parameters.</summary>
    public bool IsCall { get; set; }

    /// <summary>An instruction that keeps its data in an instance, such as a timer or counter.</summary>
    public bool IsInstruction { get; set; }

    /// <summary>The instance of an FB call or an instruction, as a source names it: <c>#delay</c>, <c>"LampDelay"</c>.</summary>
    public string? Instance { get; set; }

    /// <summary>The pins whose signal is inverted.</summary>
    public HashSet<string> Negated { get; } = new(StringComparer.Ordinal);

    /// <summary>A call's parameters in the order the called block declares them.</summary>
    public List<TiaParameter> Parameters { get; } = [];
}

/// <summary>
/// One LAD or FBD network as a graph: parts, operands, the wires between them, and the labels the network carries.
/// The stored form and TIA's SimaticML hold the same graph under different names (docs/FORMAT.md, section 13), so both
/// are read into this, and <see cref="NetworkStatements"/> writes it in statement form (ADR 0013).
/// </summary>
public sealed class TiaFlgNet {
    public List<TiaFlgPart> Parts { get; } = [];

    /// <summary>Each operand by its id, as a source names it; null for an open connection.</summary>
    public Dictionary<int, string?> Operands { get; } = [];

    public List<List<TiaFlgEnd>> Wires { get; } = [];

    public List<string> Labels { get; } = [];

    /// <summary>Reads a network as TIA stores it: <c>Part</c>, <c>LRef</c>, <c>CRef</c> and <c>ORef</c>, wired by <c>PCon</c> and <c>OCon</c>.</summary>
    public static TiaFlgNet Read(ReadOnlySpan<byte> xml, TiaIdentTable idents) {
        var net = new TiaFlgNet();
        var reader = new Utf8XmlReader(xml);
        while (reader.Read()) {
            if (reader.IsEnd || reader.IsEmpty) {
                continue;
            }
            if (reader.Name.SequenceEqual("Labels"u8)) {
                ReadLabels(ref reader, idents, net);
            } else if (reader.Name.SequenceEqual("Parts"u8)) {
                ReadParts(ref reader, idents, net);
            } else if (reader.Name.SequenceEqual("Wires"u8)) {
                ReadWires(ref reader, net);
            }
        }
        return net;
    }

    private static void ReadLabels(ref Utf8XmlReader reader, TiaIdentTable idents, TiaFlgNet net) {
        while (reader.Read() && !(reader.IsEnd && reader.Name.SequenceEqual("Labels"u8))) {
            if (!reader.IsEnd && reader.Name.SequenceEqual("ORef"u8)) {
                net.Labels.Add(idents.Name(reader.GetInt32("RefId"u8)));
            }
        }
    }

    private static void ReadParts(ref Utf8XmlReader reader, TiaIdentTable idents, TiaFlgNet net) {
        while (reader.Read() && !(reader.IsEnd && reader.Name.SequenceEqual("Parts"u8))) {
            if (reader.IsEnd) {
                continue;
            }
            var uid = reader.GetInt32("UId"u8) ?? -1;
            if (reader.Name.SequenceEqual("ORef"u8)) {
                var refId = reader.GetInt32("RefId"u8);
                net.Operands[uid] = refId is null ? null : idents.Name(refId);
                if (!reader.IsEmpty) {
                    reader.SkipElement();
                }
                continue;
            }
            TiaFlgPart part;
            if (reader.Name.SequenceEqual("Part"u8)) {
                part = new TiaFlgPart(uid, reader.GetString("Gate"u8) ?? "?");
            } else if (reader.Name.SequenceEqual("LRef"u8)) {
                part = new TiaFlgPart(uid, idents.Name(reader.GetInt32("RefId"u8))) { IsInstruction = true };
            } else if (reader.Name.SequenceEqual("CRef"u8)) {
                part = new TiaFlgPart(uid, "?") { IsCall = true };
                part.Parameters.AddRange(idents.Parameters(reader.GetInt32("RefId"u8)));
            } else {
                part = new TiaFlgPart(uid, XmlText.ToString(reader.Name));
            }
            net.Parts.Add(part);
            if (reader.IsEmpty) {
                continue;
            }
            var depth = 1;
            while (depth > 0 && reader.Read()) {
                if (reader.IsEnd) {
                    depth--;
                    continue;
                }
                var name = reader.Name;
                if (name.SequenceEqual("Negated"u8)) {
                    part.Negated.Add(reader.GetString("PinName"u8) ?? "");
                } else if (name.SequenceEqual("Instance"u8)) {
                    part.Instance = idents.Name(reader.GetInt32("RefId"u8));
                } else if (name.SequenceEqual("CodeBlock"u8)) {
                    part.Name = idents.Name(reader.GetInt32("RefId"u8));
                }
                if (!reader.IsEmpty) {
                    depth++;
                }
            }
        }
    }

    private static void ReadWires(ref Utf8XmlReader reader, TiaFlgNet net) {
        List<TiaFlgEnd>? wire = null;
        while (reader.Read() && !(reader.IsEnd && reader.Name.SequenceEqual("Wires"u8))) {
            if (reader.Name.SequenceEqual("Wire"u8)) {
                if (!reader.IsEnd) {
                    wire = [];
                    net.Wires.Add(wire);
                }
                continue;
            }
            if (reader.IsEnd || wire is null) {
                continue;
            }
            if (reader.Name.SequenceEqual("Powerrail"u8)) {
                wire.Add(new TiaFlgEnd(TiaFlgEndKind.Powerrail));
            } else if (reader.Name.SequenceEqual("PCon"u8)) {
                wire.Add(new TiaFlgEnd(TiaFlgEndKind.Pin, reader.GetInt32("UId"u8) ?? -1, reader.GetString("PinName"u8)));
            } else if (reader.Name.SequenceEqual("OCon"u8)) {
                wire.Add(new TiaFlgEnd(TiaFlgEndKind.Operand, reader.GetInt32("UId"u8) ?? -1));
            } else if (reader.Name.SequenceEqual("OpenCon"u8)) {
                wire.Add(new TiaFlgEnd(TiaFlgEndKind.Open, reader.GetInt32("UId"u8) ?? -1));
            }
        }
    }
}
