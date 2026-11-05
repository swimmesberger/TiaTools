using System.Text;
using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>A block's identifier table: the id a network or SCL symbol refers to, to its display name.</summary>
public sealed class TiaIdentTable {
    private readonly Dictionary<int, string> _names = [];
    private readonly List<(int Id, int Parent, string Path)> _structured = [];

    /// <summary>Adds one <c>IdentXmlPart</c>. Call <see cref="Complete"/> after the last.</summary>
    public void Add(ReadOnlySpan<byte> part) {
        if (part.IsEmpty || part[..Math.Min(part.Length, 200)].IndexOf("<EncryptedData"u8) >= 0) {
            return;
        }
        var reader = new Utf8XmlReader(part);
        if (!reader.Read()) {
            return;
        }
        var segments = new StringBuilder();
        while (reader.Read()) {
            if (reader.IsEnd) {
                continue;
            }
            ReadElement(ref reader, segments);
        }
    }

    /// <summary>Resolves structured tags onto their base, which may come from another part.</summary>
    public void Complete() {
        foreach (var (id, parent, path) in _structured) {
            _names[id] = _names.TryGetValue(parent, out var head) && head.Length != 0 ? head + "." + path : path;
        }
        _structured.Clear();
    }

    public string Name(int? id) {
        if (id is not { } value) {
            return "(not connected)";
        }
        return _names.TryGetValue(value, out var name) ? name : "RID" + value;
    }

    private void ReadElement(ref Utf8XmlReader reader, StringBuilder path) {
        var kind = reader.Name;
        var isEmpty = reader.IsEmpty;
        int? id = null;
        var name = "";
        int? structuredBase = null;
        string? literal = null;
        path.Clear();
        var firstSegment = "";
        var segmentCount = 0;
        var depth = isEmpty ? 0 : 1;
        var kindCopy = KindOf(kind);
        while (depth > 0 && reader.Read()) {
            if (reader.IsEnd) {
                depth--;
                continue;
            }
            if (!reader.IsEmpty) {
                depth++;
            }
            var tag = reader.Name;
            if (tag.SequenceEqual("ID"u8) && id is null) {
                id = reader.GetInt32("RID"u8);
                name = reader.GetString("N"u8) ?? "";
            } else if (tag.SequenceEqual("AO"u8)) {
                var segment = reader.GetString("N"u8) ?? "";
                if (segmentCount == 0) {
                    firstSegment = segment;
                } else {
                    path.Append('.').Append(Quote(segment));
                }
                segmentCount++;
            } else if (tag.SequenceEqual("STAD"u8)) {
                structuredBase = reader.GetInt32("TIRD"u8);
            } else if (tag.SequenceEqual("CB"u8) && literal is null) {
                literal = reader.GetString("V"u8);
            }
        }
        if (id is not { } rid) {
            return;
        }
        switch (kindCopy) {
            case IdentKind.Local:
                _names[rid] = "#" + (segmentCount == 0 ? Quote(name) : Quote(firstSegment) + path);
                break;
            case IdentKind.Global:
                _names[rid] = segmentCount == 0 ? name : "\"" + firstSegment + "\"" + path;
                break;
            case IdentKind.Structured:
                _structured.Add((rid, structuredBase ?? -1, (segmentCount == 0 ? "" : Quote(firstSegment)) + path));
                break;
            case IdentKind.Quoted:
                _names[rid] = "\"" + name + "\"";
                break;
            case IdentKind.LocalConstant:
                _names[rid] = "#" + name;
                break;
            case IdentKind.Literal:
                _names[rid] = literal ?? "?";
                break;
            case IdentKind.Plain:
                _names[rid] = name;
                break;
        }
    }

    private enum IdentKind : byte { None, Local, Global, Structured, Quoted, LocalConstant, Literal, Plain }

    private static IdentKind KindOf(ReadOnlySpan<byte> kind) {
        if (kind.SequenceEqual("InterfaceAccess"u8) || kind.SequenceEqual("MultInstAccess"u8)) {
            return IdentKind.Local;
        }
        if (kind.SequenceEqual("GlobalAccess"u8)) {
            return IdentKind.Global;
        }
        if (kind.SequenceEqual("StructuredTagAccess"u8)) {
            return IdentKind.Structured;
        }
        if (kind.SequenceEqual("SimpleAccess"u8) || kind.SequenceEqual("AufDBBlock"u8) || kind.SequenceEqual("DepDBBlock"u8) || kind.SequenceEqual("FCBlock"u8)
            || kind.SequenceEqual("FBBlock"u8) || kind.SequenceEqual("OBBlock"u8) || kind.SequenceEqual("DataTypeBlock"u8) || kind.SequenceEqual("GlobalConstant"u8)) {
            return IdentKind.Quoted;
        }
        if (kind.SequenceEqual("LocalConstant"u8)) {
            return IdentKind.LocalConstant;
        }
        if (kind.SequenceEqual("LiteralConstant"u8)) {
            return IdentKind.Literal;
        }
        return kind.SequenceEqual("Instruction"u8) || kind.SequenceEqual("DirectType"u8) || kind.SequenceEqual("Label"u8) ? IdentKind.Plain : IdentKind.None;
    }

    /// <summary>TIA quotes a name that is not a plain identifier.</summary>
    private static string Quote(string segment) {
        var plain = segment.Length != 0 && !char.IsAsciiDigit(segment[0]);
        foreach (var c in segment) {
            plain &= char.IsAsciiLetterOrDigit(c) || c == '_';
        }
        return plain ? segment : "\"" + segment + "\"";
    }
}
