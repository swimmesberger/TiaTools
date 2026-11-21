using System.Text;
using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>A parameter of a called block: its name and section (Input, Output, InOut, Return).</summary>
public readonly record struct TiaParameter(string Name, string Section);

/// <summary>A block's identifier table: the id a network or SCL symbol refers to, to its display name.</summary>
public sealed class TiaIdentTable {
    private readonly Dictionary<int, string> _names = [];
    private readonly List<(int Id, int Parent, string Path)> _structured = [];
    private readonly List<(int Id, int Target)> _addresses = [];
    private readonly Dictionary<int, List<TiaParameter>> _parameters = [];

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
        // An address-of, P##local, names the identifier it takes the address of.
        foreach (var (id, target) in _addresses) {
            _names[id] = "P#" + Name(target);
        }
        _addresses.Clear();
        foreach (var (id, name) in _names.Where(entry => entry.Value.Contains(IndexMark, StringComparison.Ordinal)).ToList()) {
            _names[id] = ResolveIndexes(name);
        }
    }

    private const char IndexMark = '', IndexEnd = '';

    /// <summary>Replaces each index mark with the name of the identifier it refers to, a constant or a tag.</summary>
    private string ResolveIndexes(string name) {
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++) {
            if (name[i] != IndexMark) {
                builder.Append(name[i]);
                continue;
            }
            var end = name.IndexOf(IndexEnd, i);
            builder.Append(int.TryParse(name.AsSpan(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture, out var id) ? Name(id) : "?");
            i = end;
        }
        return builder.ToString();
    }

    /// <summary>A called block's parameters in their order, from its <c>BlockInterfaceInfo</c>; a call numbers them from 1.</summary>
    public IReadOnlyList<TiaParameter> Parameters(int? id) => id is { } value && _parameters.TryGetValue(value, out var list) ? list : [];

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
        int? size = null, bitOffset = null, addressOf = null;
        string? area = null;
        path.Clear();
        var firstSegment = "";
        var firstQuoted = false;
        var segmentCount = 0;
        var depth = isEmpty ? 0 : 1;
        var kindCopy = KindOf(kind);
        var simpleAccess = kind.SequenceEqual("SimpleAccess"u8);
        List<TiaParameter>? parameters = kind.SequenceEqual("BlockInterfaceInfo"u8) ? [] : null;
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
                // RIDI names the identifiers of an array element's indexes, resolved when all parts are read.
                if (reader.GetString("RIDI"u8) is { Length: > 0 } indexes) {
                    segment = Quote(segment) + "[" + string.Join(",", indexes.Split(',').Select(index => IndexMark + index + IndexEnd)) + "]";
                    if (segmentCount == 0) {
                        firstSegment = segment;
                        firstQuoted = true;
                    } else {
                        path.Append('.').Append(segment);
                    }
                    segmentCount++;
                    continue;
                }
                if (segmentCount == 0) {
                    firstSegment = segment;
                } else {
                    path.Append('.').Append(Quote(segment));
                }
                segmentCount++;
            } else if (tag.SequenceEqual("STAD"u8)) {
                structuredBase = reader.GetInt32("TIRD"u8);
            } else if (tag.SequenceEqual("CB"u8) && literal is null) {
                // SV is the constant as the source writes it (T#2S), escaped as an XML name; V only its value (2000).
                literal = reader.GetString("SV"u8) is { Length: > 0 } source ? Unescape(source) : reader.GetString("V"u8);
            } else if (tag.SequenceEqual("AD"u8)) {
                addressOf = reader.GetInt32("SSRID"u8);
            } else if (tag.SequenceEqual("OD"u8) && size is null) {
                size = reader.GetInt32("S"u8);
            } else if (tag.SequenceEqual("SSD"u8)) {
                bitOffset = reader.GetInt32("AO"u8);
            } else if (tag.SequenceEqual("SAD"u8)) {
                area = reader.GetString("R"u8);
            } else if (parameters is not null && tag.SequenceEqual("BPI"u8)) {
                parameters.Add(new TiaParameter(reader.GetString("N"u8) ?? "", reader.GetString("S"u8) ?? ""));
            }
        }
        if (id is not { } rid) {
            return;
        }
        if (parameters is not null) {
            _parameters[rid] = parameters;
        }
        if (kindCopy == IdentKind.Address) {
            if (addressOf is { } target) {
                _addresses.Add((rid, target));
            }
            return;
        }
        switch (kindCopy) {
            case IdentKind.Local:
                _names[rid] = "#" + (segmentCount == 0 ? Quote(name) : (firstQuoted ? firstSegment : Quote(firstSegment)) + path);
                break;
            case IdentKind.Global:
                _names[rid] = segmentCount == 0 ? name : "\"" + firstSegment + "\"" + path;
                break;
            case IdentKind.Structured:
                _structured.Add((rid, structuredBase ?? -1, (segmentCount == 0 ? "" : firstQuoted ? firstSegment : Quote(firstSegment)) + path));
                break;
            case IdentKind.Quoted:
                // A simple access without a name is an absolute address no tag stands for, such as %DBW2 after an OPN.
                _names[rid] = name.Length == 0 && simpleAccess && Absolute(area, size, bitOffset) is { } address ? address : "\"" + name + "\"";
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

    private enum IdentKind : byte { None, Local, Global, Structured, Quoted, LocalConstant, Literal, Plain, Address }

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
        if (kind.SequenceEqual("Address"u8)) {
            return IdentKind.Address;
        }
        if (kind.SequenceEqual("LocalConstant"u8)) {
            return IdentKind.LocalConstant;
        }
        if (kind.SequenceEqual("LiteralConstant"u8)) {
            return IdentKind.Literal;
        }
        return kind.SequenceEqual("Instruction"u8) || kind.SequenceEqual("DirectType"u8) || kind.SequenceEqual("Label"u8) ? IdentKind.Plain : IdentKind.None;
    }

    /// <summary>
    /// An absolute address from its area, size in bits and bit offset: <c>%DBW2</c>, <c>%MB4</c>, <c>%I0.1</c>; null for an
    /// area or size this does not know.
    /// </summary>
    private static string? Absolute(string? area, int? size, int? bitOffset) {
        var prefix = area switch { "Input" => "I", "Output" => "Q", "Memory" => "M", "Data" => "DB", _ => null };
        if (prefix is null || bitOffset is not { } offset || offset < 0) {
            return null;
        }
        var unit = size switch { 1 => "X", 8 => "B", 16 => "W", 32 => "D", _ => null };
        if (unit is null) {
            return null;
        }
        var (byteOffset, bit) = (offset / 8, offset % 8);
        return unit == "X"
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"%{prefix}{(prefix == "DB" ? "X" : "")}{byteOffset}.{bit}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"%{prefix}{unit}{byteOffset}");
    }

    /// <summary>Undoes XML name escaping: <c>_x0023_</c> is <c>#</c>.</summary>
    private static string Unescape(string text) {
        if (!text.Contains("_x", StringComparison.Ordinal)) {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            if (text[i] == '_' && i + 6 < text.Length && text[i + 1] == 'x' && text[i + 6] == '_'
                && int.TryParse(text.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code)) {
                builder.Append((char)code);
                i += 6;
            } else {
                builder.Append(text[i]);
            }
        }
        return builder.ToString();
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
