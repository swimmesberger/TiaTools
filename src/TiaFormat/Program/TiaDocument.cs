using System.Buffers;
using System.Text;

namespace TiaFormat.Program;

/// <summary>What a piece of source text is, for showing it.</summary>
public enum TiaTokenKind : byte {
    Keyword,
    Comment,
    Literal,
    Type,
    Attribute,

    /// <summary>A name of the block itself: <c>#value</c>.</summary>
    Local,

    /// <summary>A name outside the block: <c>"Other"</c>. Its <see cref="TiaToken.Target"/> is the name without quotes.</summary>
    Global,

    /// <summary>A part after a dot.</summary>
    Member,
    Parameter,

    /// <summary>A called block, by name.</summary>
    Call,

    /// <summary>A called system instruction.</summary>
    Instruction,
}

/// <summary>A piece of source text. <see cref="IsCall"/>: it is what a call statement calls.</summary>
public readonly record struct TiaToken(int Start, int Length, TiaTokenKind Kind, string? Target = null, bool IsCall = false);

/// <summary>A block's source text with its tokens in order. Positions count UTF-16 units, lines count from 0.</summary>
public sealed class TiaDocument {
    private readonly int[] _lineStarts;

    public TiaDocument(string text, IReadOnlyList<TiaToken> tokens) {
        Text = text;
        Tokens = tokens;
        var starts = new List<int> { 0 };
        for (var i = text.IndexOf('\n', StringComparison.Ordinal); i >= 0; i = text.IndexOf('\n', i + 1)) {
            starts.Add(i + 1);
        }
        _lineStarts = [.. starts];
    }

    public string Text { get; }

    public IReadOnlyList<TiaToken> Tokens { get; }

    public int LineCount => _lineStarts.Length;

    public int LineOf(int position) {
        var found = Array.BinarySearch(_lineStarts, position);
        return found >= 0 ? found : ~found - 1;
    }

    public string LineText(int line) {
        var start = _lineStarts[line];
        var end = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] - 1 : Text.Length;
        return Text[start..end];
    }

    public int LineStart(int line) => _lineStarts[line];

    /// <summary>The token at a position, or null.</summary>
    public TiaToken? TokenAt(int position) {
        var low = 0;
        var high = Tokens.Count - 1;
        while (low <= high) {
            var middle = (low + high) / 2;
            var token = Tokens[middle];
            if (position < token.Start) {
                high = middle - 1;
            } else if (position >= token.Start + token.Length) {
                low = middle + 1;
            } else {
                return token;
            }
        }
        return null;
    }

    /// <summary>The line declaring a member of this block, or null: where a click on <c>#value</c> leads.</summary>
    public int? DeclarationLine(string name) {
        var plain = name.TrimStart('#').Trim('"');
        var body = Text.IndexOf("\nBEGIN\n", StringComparison.Ordinal);
        for (var line = 0; line < LineCount && (body < 0 || _lineStarts[line] < body); line++) {
            var text = LineText(line).TrimStart();
            if (text.StartsWith(plain, StringComparison.Ordinal) && IsNameEnd(text, plain.Length)) {
                return line;
            }
            if (text.StartsWith('"') && text.AsSpan(1).StartsWith(plain, StringComparison.Ordinal) && IsNameEnd(text, plain.Length + 1)) {
                return line;
            }
        }
        return null;
    }

    private static bool IsNameEnd(string text, int at) =>
        at < text.Length && text[at] is ' ' or '"' or ':' or '{';
}

/// <summary>Builds a <see cref="TiaDocument"/>: exact tokens from the stored source where there is some, a small scanner for the rest.</summary>
public static class TiaDocumentBuilder {
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase) {
        "FUNCTION_BLOCK", "FUNCTION", "ORGANIZATION_BLOCK", "DATA_BLOCK", "TYPE", "END_FUNCTION_BLOCK", "END_FUNCTION", "END_ORGANIZATION_BLOCK",
        "END_DATA_BLOCK", "END_TYPE", "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP", "END_VAR", "CONSTANT", "RETAIN", "NON_RETAIN",
        "DB_SPECIFIC", "STRUCT", "END_STRUCT", "BEGIN", "TITLE", "AUTHOR", "FAMILY", "NAME", "VERSION", "AT", "OF", "ARRAY", "NOT",
        // The statement form of LAD, FBD and GRAPH (ADR 0013), and STL's network heading.
        "AND", "OR", "XOR", "MOD", "IF", "THEN", "END_IF", "GOTO", "RETURN", "TRUE", "FALSE", "CALL", "NETWORK", "STEP", "TRANSITION", "WHEN",
    };

    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase) {
        "Bool", "Byte", "Word", "DWord", "LWord", "SInt", "Int", "DInt", "LInt", "USInt", "UInt", "UDInt", "ULInt", "Real", "LReal", "Char", "WChar",
        "String", "WString", "Time", "LTime", "Date", "Time_Of_Day", "LTime_Of_Day", "TOD", "LTOD", "DT", "LDT", "DTL", "S5Time", "Date_And_Time",
        "Any", "Pointer", "Variant", "Timer", "Counter", "Void", "DB_ANY", "OB_ANY", "HW_ANY", "HW_IO", "HW_IOSYSTEM", "HW_SUBMODULE", "HW_INTERFACE",
    };

    private static readonly SearchValues<char> WordEnd = SearchValues.Create(" \t\r\n;,()[]{}:=<>+*/&'\"");

    /// <summary>The block as the source writer writes it, with its tokens.</summary>
    public static TiaDocument Build(TiaProgram program, TiaBlock block) {
        using var output = new PooledByteWriter();
        var exact = new TiaExactTokens();
        TiaSourceWriter.Write(program, block, output, exact);
        return Finish(output.WrittenSpan, exact);
    }

    /// <summary>A tag table as the source writer writes it.</summary>
    public static TiaDocument Build(TiaTagTable table) {
        using var output = new PooledByteWriter();
        TiaSourceWriter.WriteTagTable(table, output);
        return Finish(output.WrittenSpan, new TiaExactTokens());
    }

    /// <summary>Scanned text alone, for what has no stored tokens.</summary>
    public static TiaDocument FromText(string text) {
        var tokens = new List<TiaToken>();
        Scan(text, 0, text.Length, tokens);
        return new TiaDocument(text, tokens);
    }

    private static TiaDocument Finish(ReadOnlySpan<byte> bytes, TiaExactTokens exact) {
        var text = Encoding.UTF8.GetString(bytes);
        var map = CharacterPositions(bytes);
        var tokens = new List<TiaToken>(exact.Tokens.Count + 64);
        var position = 0;
        foreach (var (start, end) in exact.Ranges) {
            Scan(text, position, map[start], tokens);
            position = map[end];
        }
        Scan(text, position, text.Length, tokens);
        foreach (var token in exact.Tokens) {
            var start = map[token.Start];
            tokens.Add(token with { Start = start, Length = map[token.Start + token.Length] - start });
        }
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new TiaDocument(text, tokens);
    }

    /// <summary>For each UTF-8 position, the UTF-16 position: tokens are found in bytes and shown in characters.</summary>
    private static int[] CharacterPositions(ReadOnlySpan<byte> bytes) {
        var map = new int[bytes.Length + 1];
        var character = 0;
        for (var i = 0; i < bytes.Length; i++) {
            map[i] = character;
            var b = bytes[i];
            if ((b & 0xC0) != 0x80) {
                character += b >= 0xF0 ? 2 : 1;
            }
        }
        map[bytes.Length] = character;
        return map;
    }

    /// <summary>
    /// Scans declarations and the statement form of LAD, FBD and GRAPH. Knows comments, strings, names and keywords,
    /// which is all this text has.
    /// </summary>
    private static void Scan(string text, int start, int end, List<TiaToken> tokens) {
        var i = start;
        while (i < end) {
            var c = text[i];
            var from = i;
            if (c == '/' && i + 1 < end && text[i + 1] == '/') {
                i = LineEnd(text, i, end);
                tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Comment));
            } else if (c == '{') {
                var close = text.IndexOf('}', i, end - i);
                i = close < 0 ? end : close + 1;
                tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Attribute));
            } else if (c == '"') {
                // A name holds no line break, whatever else it holds.
                var line = LineEnd(text, i, end);
                var close = text.IndexOf('"', i + 1, line - i - 1);
                i = close < 0 ? line : close + 1;
                var target = close < 0 || IsMemberName(text, from, i, end) ? null : text[(from + 1)..(i - 1)];
                tokens.Add(new TiaToken(from, i - from, target is null ? TiaTokenKind.Member : TiaTokenKind.Global, target));
            } else if (c == '\'') {
                var close = text.IndexOf('\'', i + 1, end - i - 1);
                i = close < 0 ? end : close + 1;
                tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Literal));
            } else if (c == '#') {
                i = WordStop(text, i + 1, end);
                tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Local));
            } else if (char.IsDigit(c) || c == '-' && i + 1 < end && char.IsDigit(text[i + 1])) {
                i++;
                while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '#' || (text[i] == '.' && i + 1 < end && char.IsDigit(text[i + 1])))) {
                    i++;
                }
                tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Literal));
            } else if (char.IsLetter(c) || c == '_') {
                i = WordStop(text, i, end);
                var word = text.AsSpan(from, i - from);
                var hash = word.IndexOf('#');
                var dot = word.IndexOf('.');
                if (hash > 0) {
                    tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Literal));
                } else if (dot < 0 && Keywords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word)) {
                    tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Keyword));
                } else if (dot < 0 && Types.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word)) {
                    tokens.Add(new TiaToken(from, i - from, TiaTokenKind.Type));
                }
            } else {
                i++;
            }
        }
    }

    private static int WordStop(string text, int at, int end) {
        var length = text.AsSpan(at, end - at).IndexOfAny(WordEnd);
        return length < 0 ? end : at + length;
    }

    private static int LineEnd(string text, int at, int end) {
        var newLine = text.IndexOf('\n', at, end - at);
        return newLine < 0 ? end : newLine;
    }

    /// <summary>
    /// A quoted name starting a line is a member: <c>"1st" : Int</c> declares it, <c>"1st".x := 1</c> sets it. Only a name
    /// alone on its line leads somewhere, as the type of a data block.
    /// </summary>
    private static bool IsMemberName(string text, int from, int close, int end) {
        var before = from - 1;
        while (before >= 0 && text[before] is ' ' or '\t') {
            before--;
        }
        if (before >= 0 && text[before] != '\n') {
            return false;
        }
        return close < end && text[close] != '\n';
    }
}

/// <summary>What the source writer knows exactly: tokens from stored source, and the text ranges they cover. Positions in bytes.</summary>
public sealed class TiaExactTokens {
    internal List<TiaToken> Tokens { get; } = [];

    internal List<(int Start, int End)> Ranges { get; } = [];

    /// <summary>Adds a body written at <paramref name="at"/> with each line indented by one tab.</summary>
    internal void AddIndented(int at, ReadOnlySpan<byte> source, List<TiaToken> tokens, int end) {
        Ranges.Add((at, end));
        var tabs = 1;
        var scanned = 0;
        foreach (var token in tokens) {
            tabs += source[scanned..token.Start].Count((byte)'\n');
            scanned = token.Start;
            Tokens.Add(token with { Start = at + token.Start + tabs });
        }
    }
}
