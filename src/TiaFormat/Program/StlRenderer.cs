using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>
/// Writes one STL network as TIA's generated source writes it: one statement per line, six blanks in, the operand after
/// one blank, a line comment after <c> ;//</c>, a label before its statement, and a call's parameters one per line, their
/// names padded so that <c>:=</c> stands in one column (docs/FORMAT.md, section 13).
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
                } else if (name.SequenceEqual("Token"u8)) {
                    statement.Mnemonic = reader.GetString("DispName"u8) ?? "";
                } else if (name.SequenceEqual("OpdAccess"u8)) {
                    statement.Operand = idents.Name(reader.GetInt32("RefId"u8));
                } else if (name.SequenceEqual("UserToken"u8)) {
                    statement.Operand = reader.GetString("DispName"u8);
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
            output.Write((byte)' ');
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
