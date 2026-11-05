using System.Collections.Frozen;
using System.Text;
using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>
/// Turns an <c>SCLSource</c> compile unit into its source text. The unit is a concrete syntax tree, so emitting each
/// token's text in document order gives the source with its layout and comments. UTF-8 in, UTF-8 out, no strings.
/// </summary>
public static class SclRenderer {
    private enum TokenKind : byte { Fixed, Container, Blank, NewLine, LineComment, BlockCommentStart, Skip }

    private readonly record struct Token(TokenKind Kind, byte[] Text);

    private static readonly FrozenDictionary<string, Token> Tokens = BuildTokens();
    private static readonly FrozenDictionary<string, Token>.AlternateLookup<ReadOnlySpan<char>> TokenLookup = Tokens.GetAlternateLookup<ReadOnlySpan<char>>();

    private static FrozenDictionary<string, Token> BuildTokens() {
        var map = new Dictionary<string, Token> {
            ["BL"] = new(TokenKind.Blank, []),
            ["NL"] = new(TokenKind.NewLine, []),
            ["LC"] = new(TokenKind.LineComment, []),
            ["BC"] = new(TokenKind.BlockCommentStart, []),
            ["Symbols"] = new(TokenKind.Skip, []),
        };
        foreach (var name in new[] { "RootStatements", "Statements", "Fold", "Expression", "Param", "FctCa", "InstCa", "CaseElem", "CaseRange", "CaseSRange", "SCLSource" }) {
            map[name] = new Token(TokenKind.Container, []);
        }
        foreach (var (name, text) in new[] {
            ("OpAs", ":="), ("OpPa", "=>"), ("OpPl", "+"), ("OpMi", "-"), ("OpMu", "*"), ("OpDi", "/"), ("OpPo", "**"),
            ("OpE", "="), ("OpU", "<>"), ("OpG", ">"), ("OpL", "<"), ("OpGE", ">="), ("OpLE", "<="),
            ("OpAND", "AND"), ("OpOR", "OR"), ("OpXOR", "XOR"), ("OpNOT", "NOT"), ("OpMOD", "MOD"), ("OpAAND", "&"),
            ("OpPlAs", "+="), ("OpMinAs", "-="), ("OpMulAs", "*="), ("OpDiAs", "/="),
            ("Colon", ":"), ("Comma", ","), ("Dot", "."), ("BracO", "("), ("BracC", ")"), ("BoxO", "["), ("BoxC", "]"), ("FiSt", ";"), ("LDots", ".."),
            ("KwTHEN", "THEN"), ("KwELSE", "ELSE"), ("KwELSIF", "ELSIF"), ("KwENDIF", "END_IF"),
            ("KwDO", "DO"), ("KwTO", "TO"), ("KwBY", "BY"), ("KwENDFOR", "END_FOR"), ("KwENDW", "END_WHILE"),
            ("KwOF", "OF"), ("KwENDC", "END_CASE"), ("KwEndRegion", "END_REGION"),
            ("KwUNTIL", "UNTIL"), ("KwENDR", "END_REPEAT"), ("BCE", "*)"),
        }) {
            map[name] = new Token(TokenKind.Fixed, Encoding.UTF8.GetBytes(text));
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes the source text. A tag this renderer has no rule for is written as «tag», so a gap shows in the output
    /// instead of silently dropping source, and it is counted in the return value.
    /// </summary>
    public static int Render(ReadOnlySpan<byte> xml, PooledByteWriter output) {
        var symbols = ReadSymbolNames(xml);
        var unknown = 0;
        var reader = new Utf8XmlReader(xml);
        Span<char> tag = stackalloc char[64];
        while (reader.Read()) {
            if (reader.IsEnd) {
                continue;
            }
            var name = reader.Name;
            var known = TryFindToken(name, tag, out var token);
            if (known) {
                switch (token.Kind) {
                    case TokenKind.Blank:
                        output.WriteSpaces(reader.GetInt32("NumBLs"u8) ?? 1);
                        continue;
                    case TokenKind.NewLine:
                        output.Write((byte)'\n');
                        continue;
                    case TokenKind.LineComment:
                        output.Write("//"u8);
                        WriteAttribute(reader, "TE"u8, output);
                        continue;
                    case TokenKind.BlockCommentStart:
                        output.Write("(*"u8);
                        continue;
                    case TokenKind.Fixed:
                        output.Write(token.Text);
                        continue;
                    case TokenKind.Container:
                        continue;
                    case TokenKind.Skip:
                        reader.SkipElement();
                        continue;
                }
            }
            if (WriteAttribute(reader, "TE"u8, output) || WriteAttribute(reader, "ODN"u8, output) || WriteAttribute(reader, "FormalName"u8, output)) {
                continue;
            }
            if (reader.GetInt32("SyId"u8) is { } symbol && symbols is not null && symbols.TryGetValue(symbol, out var range)) {
                XmlText.Append(output, xml.Slice(range.Start, range.Length));
                continue;
            }
            if (name.SequenceEqual("Statement"u8) || name.SequenceEqual("Label"u8) || name.SequenceEqual("Sub"u8) || name.SequenceEqual("BCL"u8)) {
                continue;
            }
            output.Write("«"u8);
            output.Write(name);
            output.Write("»"u8);
            unknown++;
        }
        return unknown;
    }

    private static bool WriteAttribute(in Utf8XmlReader reader, ReadOnlySpan<byte> attribute, PooledByteWriter output) {
        if (!reader.TryGetAttribute(attribute, out var raw)) {
            return false;
        }
        XmlText.Append(output, raw);
        return true;
    }

    private static bool TryFindToken(ReadOnlySpan<byte> name, Span<char> buffer, out Token token) {
        token = default;
        if (name.Length > buffer.Length || Ascii.ToUtf16(name, buffer, out var chars) != System.Buffers.OperationStatus.Done) {
            return false;
        }
        return TokenLookup.TryGetValue(buffer[..chars], out token);
    }

    /// <summary>SymID to the position of its Name, only for symbols with a name: an instruction's name lives only there.</summary>
    private static Dictionary<int, (int Start, int Length)>? ReadSymbolNames(ReadOnlySpan<byte> xml) {
        Dictionary<int, (int, int)>? names = null;
        var reader = new Utf8XmlReader(xml);
        var inside = false;
        while (reader.Read()) {
            if (reader.Name.SequenceEqual("Symbols"u8)) {
                if (reader.IsEnd || reader.IsEmpty) {
                    break;
                }
                inside = true;
                continue;
            }
            if (!inside) {
                if (reader.Name.SequenceEqual("RootStatements"u8)) {
                    break;
                }
                continue;
            }
            if (reader.IsEnd || !reader.TryGetAttribute("Name"u8, out var raw) || reader.GetInt32("SymID"u8) is not { } id) {
                continue;
            }
            xml.Overlaps(raw, out var start);
            (names ??= [])[id] = (start, raw.Length);
        }
        return names;
    }
}
