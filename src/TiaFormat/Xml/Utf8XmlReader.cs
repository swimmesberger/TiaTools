using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace TiaFormat.Xml;

/// <summary>
/// A forward-only pull reader over the machine-written UTF-8 XML in a database. It hands out spans and allocates
/// nothing. It is no general parser: no DTDs, no CDATA, and it trusts the writer's well-formedness.
/// </summary>
public ref struct Utf8XmlReader {
    private static readonly SearchValues<byte> Whitespace = SearchValues.Create(" \t\r\n"u8);

    private readonly ReadOnlySpan<byte> _xml;
    private int _position;
    private int _attributesStart;
    private int _attributesEnd;

    public Utf8XmlReader(ReadOnlySpan<byte> xml) {
        _xml = xml.StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? xml[3..] : xml;
    }

    /// <summary>The current tag's local name, without a namespace prefix.</summary>
    public ReadOnlySpan<byte> Name { get; private set; }

    /// <summary>True on a closing tag.</summary>
    public bool IsEnd { get; private set; }

    /// <summary>True on a start tag that closes itself.</summary>
    public bool IsEmpty { get; private set; }

    /// <summary>Moves to the next start or end tag, passing over text, comments and declarations.</summary>
    public bool Read() {
        while (true) {
            var rest = _xml[_position..];
            var open = rest.IndexOf((byte)'<');
            if (open < 0 || open + 1 >= rest.Length) {
                return false;
            }
            _position += open;
            var next = _xml[_position + 1];
            if (next is (byte)'?' or (byte)'!') {
                var close = _xml[_position..].IndexOf((byte)'>');
                if (close < 0) {
                    return false;
                }
                _position += close + 1;
                continue;
            }
            IsEnd = next == (byte)'/';
            var nameStart = _position + (IsEnd ? 2 : 1);
            var tagEnd = FindTagEnd(nameStart);
            var inner = _xml[nameStart..tagEnd];
            var nameEnd = inner.IndexOfAny(Whitespace);
            nameEnd = nameEnd < 0 ? inner.Length : nameEnd;
            IsEmpty = !IsEnd && _xml[tagEnd - 1] == (byte)'/';
            if (IsEmpty && nameEnd == inner.Length) {
                nameEnd--;
            }
            var name = inner[..nameEnd];
            var colon = name.IndexOf((byte)':');
            Name = colon >= 0 ? name[(colon + 1)..] : name;
            _attributesStart = nameStart + nameEnd;
            _attributesEnd = IsEmpty ? tagEnd - 1 : tagEnd;
            _position = tagEnd + 1;
            return true;
        }
    }

    /// <summary>Moves past the end of the current start tag's element.</summary>
    public void SkipElement() {
        if (IsEmpty || IsEnd) {
            return;
        }
        var depth = 1;
        while (depth > 0 && Read()) {
            if (IsEnd) {
                depth--;
            } else if (!IsEmpty) {
                depth++;
            }
        }
    }

    /// <summary>The raw, still escaped value of an attribute of the current tag.</summary>
    public readonly bool TryGetAttribute(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> rawValue) {
        var p = _attributesStart;
        while (p < _attributesEnd) {
            while (p < _attributesEnd && _xml[p] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
                p++;
            }
            var equals = _xml[p.._attributesEnd].IndexOf((byte)'=');
            if (equals < 0) {
                break;
            }
            var key = _xml.Slice(p, equals);
            var quoteAt = p + equals + 1;
            var quote = _xml[quoteAt];
            var length = _xml[(quoteAt + 1).._attributesEnd].IndexOf(quote);
            if (length < 0) {
                break;
            }
            if (key.SequenceEqual(name)) {
                rawValue = _xml.Slice(quoteAt + 1, length);
                return true;
            }
            p = quoteAt + 1 + length + 1;
        }
        rawValue = default;
        return false;
    }

    /// <summary>An attribute unescaped into a string, or null.</summary>
    public readonly string? GetString(ReadOnlySpan<byte> name) => TryGetAttribute(name, out var raw) ? XmlText.ToString(raw) : null;

    /// <summary>An integer attribute, or null when absent or not a number.</summary>
    public readonly int? GetInt32(ReadOnlySpan<byte> name) =>
        TryGetAttribute(name, out var raw) && Utf8Parser.TryParse(raw, out int value, out var used) && used == raw.Length ? value : null;

    /// <summary>The text right after the current start tag, raw.</summary>
    public readonly ReadOnlySpan<byte> PeekText() {
        var rest = _xml[_position..];
        var end = rest.IndexOf((byte)'<');
        return end < 0 ? rest : rest[..end];
    }

    private readonly int FindTagEnd(int from) {
        // A '>' can stand unescaped inside an attribute value, so quotes must be respected.
        byte quote = 0;
        for (var i = from; i < _xml.Length; i++) {
            var c = _xml[i];
            if (quote != 0) {
                if (c == quote) {
                    quote = 0;
                }
            } else if (c is (byte)'"' or (byte)'\'') {
                quote = c;
            } else if (c == (byte)'>') {
                return i;
            }
        }
        throw new TiaFormatException("An XML tag is not closed.");
    }
}

/// <summary>XML text unescaping into a writer or a string.</summary>
public static class XmlText {
    /// <summary>Appends raw XML text with its entities resolved; without entities it is a plain copy.</summary>
    public static void Append(PooledByteWriter output, ReadOnlySpan<byte> raw) {
        while (true) {
            var amp = raw.IndexOf((byte)'&');
            if (amp < 0) {
                output.Write(raw);
                return;
            }
            output.Write(raw[..amp]);
            raw = raw[amp..];
            var end = raw.IndexOf((byte)';');
            if (end < 0) {
                output.Write(raw);
                return;
            }
            WriteEntity(output, raw[1..end]);
            raw = raw[(end + 1)..];
        }
    }

    public static string ToString(ReadOnlySpan<byte> raw) {
        if (raw.IndexOf((byte)'&') < 0) {
            return Encoding.UTF8.GetString(raw);
        }
        using var writer = new PooledByteWriter(raw.Length + 16);
        Append(writer, raw);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }

    private static void WriteEntity(PooledByteWriter output, ReadOnlySpan<byte> entity) {
        if (entity.SequenceEqual("quot"u8)) {
            output.Write((byte)'"');
        } else if (entity.SequenceEqual("amp"u8)) {
            output.Write((byte)'&');
        } else if (entity.SequenceEqual("lt"u8)) {
            output.Write((byte)'<');
        } else if (entity.SequenceEqual("gt"u8)) {
            output.Write((byte)'>');
        } else if (entity.SequenceEqual("apos"u8)) {
            output.Write((byte)'\'');
        } else if (entity.Length > 1 && entity[0] == (byte)'#' && TryParseCodePoint(entity[1..], out var code) && Rune.IsValid(code)) {
            var span = output.GetSpan(4);
            output.Advance(new Rune(code).EncodeToUtf8(span));
        } else {
            output.Write((byte)'&');
            output.Write(entity);
            output.Write((byte)';');
        }
    }

    private static bool TryParseCodePoint(ReadOnlySpan<byte> digits, out int code) {
        var hex = digits[0] is (byte)'x' or (byte)'X';
        return Utf8Parser.TryParse(hex ? digits[1..] : digits, out code, out _, hex ? 'x' : '\0');
    }
}
