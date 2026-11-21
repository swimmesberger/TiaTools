using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>
/// Writes one STL network as TIA's generated source writes it: one statement per line, six blanks in, the operand after
/// one blank, a line comment after <c> ;//</c>, a label before its statement, and a call's parameters one per line, their
/// names padded so that <c>:=</c> stands in one column. An operand is the statement's tokens after the mnemonic in their
/// order, an indirect address among them (docs/FORMAT.md, section 13).
/// </summary>
public static class StlRenderer {
    private const string Indent = "      ";
    private const int ParameterWidth = 28;

    public static void Render(ReadOnlySpan<byte> xml, TiaIdentTable idents, PooledByteWriter output) {
        var reader = new Utf8XmlReader(xml);
        var first = true;
        while (reader.Read()) {
            if (reader.IsEnd || !reader.Name.SequenceEqual("Statement"u8) || reader.IsEmpty) {
                continue;
            }
            var statement = ReadStatement(ref reader, idents);
            if (!first) {
                output.Write((byte)'\n');
            }
            first = false;
            Write(statement, output);
        }
    }

    private sealed class Statement {
        public string? Label { get; set; }
        public string Mnemonic { get; set; } = "";
        public string? Operand { get; set; }
        public string? LineComment { get; set; }
        public int? CallInfo { get; set; }
        public List<(int Number, string Value)> Parameters { get; } = [];
        public List<string> ParameterNames { get; set; } = [];
    }

    private static Statement ReadStatement(ref Utf8XmlReader reader, TiaIdentTable idents) {
        var statement = new Statement();
        var depth = 1;
        var context = "";
        var parameter = 0;
        var operand = new System.Text.StringBuilder();
        while (depth > 0 && reader.Read()) {
            if (reader.IsEnd) {
                depth--;
                if (depth == 1) {
                    context = "";
                }
                continue;
            }
            var name = reader.Name;
            var isEmpty = reader.IsEmpty;
            if (depth == 1) {
                if (name.SequenceEqual("Label"u8)) {
                    context = "label";
                } else if (name.SequenceEqual("CallInfo"u8)) {
                    context = "call";
                    statement.CallInfo = reader.GetInt32("RefId"u8);
                } else if (name.SequenceEqual("Token"u8) && statement.Mnemonic.Length == 0) {
                    statement.Mnemonic = reader.GetString("DispName"u8) ?? "";
                } else if (name.SequenceEqual("Token"u8)) {
                    // After the mnemonic, tokens are parts of the operand: an area or register (MW, AR1), a status bit
                    // (OV), and the brackets and comma of an indirect address, which TIA writes as MW[ AR1, P#2.0].
                    var token = reader.GetString("DispName"u8) ?? reader.GetString("Kw"u8) ?? "";
                    operand.Append(token switch { "[" => "[ ", "," => ", ", _ => token });
                } else if (name.SequenceEqual("OpdAccess"u8)) {
                    operand.Append(idents.Name(reader.GetInt32("RefId"u8)));
                } else if (name.SequenceEqual("UserToken"u8)) {
                    operand.Append(reader.GetString("DispName"u8));
                } else if (name.SequenceEqual("LC"u8)) {
                    statement.LineComment = reader.GetString("DispName"u8);
                }
            } else if (context == "label" && name.SequenceEqual("OpdAccess"u8)) {
                statement.Label = idents.Name(reader.GetInt32("RefId"u8));
            } else if (context == "call") {
                if (name.SequenceEqual("ParaExpression"u8)) {
                    parameter = reader.GetInt32("FPNum"u8) ?? 0;
                } else if (name.SequenceEqual("OpdAccess"u8)) {
                    statement.Parameters.Add((parameter, idents.Name(reader.GetInt32("RefId"u8))));
                } else if (name.SequenceEqual("UserToken"u8)) {
                    statement.Parameters.Add((parameter, reader.GetString("DispName"u8) ?? ""));
                }
            }
            if (!isEmpty) {
                depth++;
            }
        }
        statement.Operand = operand.Length == 0 ? null : operand.ToString();
        if (statement.CallInfo is { } call) {
            var names = idents.Parameters(call);
            statement.Parameters.Sort((a, b) => a.Number.CompareTo(b.Number));
            statement.ParameterNames = [.. statement.Parameters.Select(p => p.Number >= 1 && p.Number <= names.Count ? names[p.Number - 1].Name : "P" + p.Number)];
        }
        return statement;
    }

    private static void Write(Statement statement, PooledByteWriter output) {
        output.Write(statement.Label is null ? Indent : statement.Label + ":" + Indent);
        output.Write(statement.Mnemonic);
        if (statement.Operand is not null) {
            // An address in brackets alone follows the mnemonic without a blank: A[ AR2, P#0.1].
            if (statement.Operand[0] != '[') {
                output.Write((byte)' ');
            }
            output.Write(statement.Operand);
        }
        if (statement.Parameters.Count != 0) {
            for (var i = 0; i < statement.Parameters.Count; i++) {
                output.Write((byte)'\n');
                output.Write(i == 0 ? Indent + "(  " : Indent + "   ");
                var name = statement.ParameterNames[i];
                output.Write(name.PadRight(Math.Max(ParameterWidth, name.Length + 1)));
                output.Write(":= "u8);
                output.Write(statement.Parameters[i].Value);
                if (i < statement.Parameters.Count - 1) {
                    output.Write(" , "u8);
                }
            }
            output.Write((byte)'\n');
            output.Write(Indent + ");");
            return;
        }
        if (statement.LineComment is not null) {
            output.Write(" ;//"u8);
            output.Write(statement.LineComment);
            return;
        }
        output.Write((byte)';');
    }
}
