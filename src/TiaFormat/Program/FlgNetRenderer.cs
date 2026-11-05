using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>
/// Writes one FBD or LAD network as part calls in part order, with the wires resolved to their sources. A network has
/// no text form of its own, so this layout is ours: <c>P23: Coil(in := P21.Q, operand := #x)</c>.
/// </summary>
public static class FlgNetRenderer {
    private sealed class Part(int uid, string call) {
        public int Uid { get; } = uid;
        public string Call { get; set; } = call;
        public List<string> Inputs { get; } = [];
        public List<string> Outputs { get; } = [];
        public HashSet<string> Negated { get; } = [];
    }

    private readonly record struct Connection(bool IsPart, int Uid, string? Pin);

    public static void Render(ReadOnlySpan<byte> xml, TiaIdentTable idents, PooledByteWriter output) {
        var parts = new List<Part>();
        var byUid = new Dictionary<int, Part>();
        var operands = new Dictionary<int, string>();
        var wires = new List<List<Connection>>();

        var reader = new Utf8XmlReader(xml);
        while (reader.Read()) {
            if (reader.IsEnd) {
                continue;
            }
            if (reader.Name.SequenceEqual("Parts"u8) && !reader.IsEmpty) {
                ReadParts(ref reader, idents, parts, byUid, operands);
            } else if (reader.Name.SequenceEqual("Wires"u8) && !reader.IsEmpty) {
                ReadWires(ref reader, wires);
            }
        }
        foreach (var wire in wires) {
            Connect(wire, byUid, operands);
        }
        for (var i = 0; i < parts.Count; i++) {
            var part = parts[i];
            if (i != 0) {
                output.Write((byte)'\n');
            }
            output.Write((byte)'P');
            output.Write(part.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            output.Write(": "u8);
            output.Write(part.Call);
            output.Write((byte)'(');
            var first = true;
            foreach (var list in (ReadOnlySpan<List<string>>)[part.Inputs, part.Outputs]) {
                foreach (var argument in list) {
                    if (!first) {
                        output.Write(", "u8);
                    }
                    output.Write(argument);
                    first = false;
                }
            }
            output.Write((byte)')');
        }
    }

    private static void ReadParts(ref Utf8XmlReader reader, TiaIdentTable idents, List<Part> parts, Dictionary<int, Part> byUid, Dictionary<int, string> operands) {
        while (reader.Read() && !(reader.IsEnd && reader.Name.SequenceEqual("Parts"u8))) {
            if (reader.IsEnd) {
                continue;
            }
            var uid = reader.GetInt32("UId"u8) ?? -1;
            if (reader.Name.SequenceEqual("ORef"u8)) {
                operands[uid] = idents.Name(reader.GetInt32("RefId"u8));
                reader.SkipElement();
                continue;
            }
            if (reader.Name.SequenceEqual("Part"u8)) {
                ReadGate(ref reader, uid, parts, byUid);
            } else if (reader.Name.SequenceEqual("LRef"u8) || reader.Name.SequenceEqual("CRef"u8)) {
                ReadCall(ref reader, uid, idents, parts, byUid);
            } else {
                Add(new Part(uid, "«" + XmlText.ToString(reader.Name) + "»"), parts, byUid);
                reader.SkipElement();
            }
        }
    }

    private static void ReadGate(ref Utf8XmlReader reader, int uid, List<Part> parts, Dictionary<int, Part> byUid) {
        var part = new Part(uid, reader.GetString("Gate"u8) ?? "?");
        Add(part, parts, byUid);
        if (reader.IsEmpty) {
            return;
        }
        List<string>? templates = null;
        var depth = 1;
        while (depth > 0 && reader.Read()) {
            if (reader.IsEnd) {
                depth--;
                continue;
            }
            if (reader.Name.SequenceEqual("Negated"u8)) {
                part.Negated.Add(reader.GetString("PinName"u8) ?? "");
            } else if (reader.Name.SequenceEqual("TemplateValue"u8)) {
                var value = reader.GetString("Value"u8) ?? XmlText.ToString(reader.PeekText());
                (templates ??= []).Add(reader.GetString("Name"u8) + "=" + value);
            }
            if (!reader.IsEmpty) {
                depth++;
            }
        }
        if (templates is not null) {
            part.Call += "<" + string.Join(',', templates) + ">";
        }
    }

    private static void ReadCall(ref Utf8XmlReader reader, int uid, TiaIdentTable idents, List<Part> parts, Dictionary<int, Part> byUid) {
        var target = reader.GetInt32("RefId"u8);
        var part = new Part(uid, "");
        Add(part, parts, byUid);
        string? instance = null;
        if (!reader.IsEmpty) {
            var depth = 1;
            while (depth > 0 && reader.Read()) {
                if (reader.IsEnd) {
                    depth--;
                    continue;
                }
                if (reader.Name.SequenceEqual("Instance"u8)) {
                    instance ??= idents.Name(reader.GetInt32("RefId"u8));
                } else if (reader.Name.SequenceEqual("CodeBlock"u8)) {
                    target ??= reader.GetInt32("RefId"u8);
                } else if (reader.Name.SequenceEqual("Negated"u8)) {
                    part.Negated.Add(reader.GetString("PinName"u8) ?? "");
                }
                if (!reader.IsEmpty) {
                    depth++;
                }
            }
        }
        part.Call = idents.Name(target) + (instance is null ? "" : " [" + instance + "]");
    }

    private static void ReadWires(ref Utf8XmlReader reader, List<List<Connection>> wires) {
        List<Connection>? wire = null;
        while (reader.Read() && !(reader.IsEnd && reader.Name.SequenceEqual("Wires"u8))) {
            if (reader.Name.SequenceEqual("Wire"u8)) {
                if (reader.IsEnd) {
                    continue;
                }
                wire = [];
                wires.Add(wire);
            } else if (!reader.IsEnd && wire is not null) {
                var isPart = reader.Name.SequenceEqual("PCon"u8);
                wire.Add(new Connection(isPart, reader.GetInt32("UId"u8) ?? -1, isPart ? reader.GetString("PinName"u8) : null));
            }
        }
    }

    private static void Connect(List<Connection> wire, Dictionary<int, Part> byUid, Dictionary<int, string> operands) {
        if (wire.Count == 0) {
            return;
        }
        var from = wire[0];
        // A negated pin can be the driver as well as a sink: an inverted output feeds its opposite into the wire.
        var inverted = from.IsPart && byUid.TryGetValue(from.Uid, out var origin) && origin.Negated.Contains(from.Pin ?? "") ? "NOT " : "";
        var source = from.IsPart ? inverted + "P" + from.Uid + "." + from.Pin : operands.GetValueOrDefault(from.Uid, "?");
        for (var i = 1; i < wire.Count; i++) {
            var to = wire[i];
            if (to.IsPart) {
                if (byUid.TryGetValue(to.Uid, out var sink)) {
                    sink.Inputs.Add(to.Pin + " := " + (sink.Negated.Contains(to.Pin ?? "") ? "NOT " : "") + source);
                }
            } else if (from.IsPart && byUid.TryGetValue(from.Uid, out var driver)) {
                driver.Outputs.Add(inverted + from.Pin + " => " + operands.GetValueOrDefault(to.Uid, "?"));
            }
        }
    }

    private static void Add(Part part, List<Part> parts, Dictionary<int, Part> byUid) {
        parts.Add(part);
        byUid[part.Uid] = part;
    }
}
