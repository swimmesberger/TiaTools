using System.Globalization;

namespace TiaFormat.Program;

/// <summary>Writes a whole block as one source text in the style of a TIA source file. For reading, not for importing.</summary>
public static class TiaSourceWriter {
    /// <summary>What stands where protected source would be. The source is never opened.</summary>
    public static ReadOnlySpan<byte> ProtectedMarker => "// know-how protected: source closed, not read"u8;

    /// <summary>Writes the block; returns the count of SCL tags without a rule.</summary>
    public static int Write(TiaProgram program, TiaBlock block, PooledByteWriter output) {
        var frame = block.Frame;
        var plc = program.PlcName(frame);
        var folder = program.FolderPath(frame);
        output.Write("// "u8);
        output.Write(block.Kind);
        output.Write(" number="u8);
        output.Write(block.Number?.ToString(CultureInfo.InvariantCulture) ?? "-");
        output.Write(" language="u8);
        output.Write(block.Language ?? "-");
        output.Write(" know-how="u8);
        output.Write(block.IsKnowHowProtected ? "yes"u8 : "no"u8);
        output.Write(" plc="u8);
        output.Write(plc ?? "-");
        output.Write("\n// folder: "u8);
        output.Write(folder);
        output.Write((byte)'\n');
        var master = program.GeneratedFrom(block);
        if (master is not null) {
            output.Write("// generated from: "u8);
            output.Write(master);
            output.Write((byte)'\n');
        }

        var members = program.ReadInterface(block);
        var opener = block.Kind switch {
            "FB" or "SFB" => "FUNCTION_BLOCK",
            "FC" or "SFC" => "FUNCTION",
            "OB" => "ORGANIZATION_BLOCK",
            "UDT" => "TYPE",
            _ when block.Kind.StartsWith("DB", StringComparison.Ordinal) => "DATA_BLOCK",
            _ => block.Kind,
        };
        output.Write(opener);
        output.Write(" \""u8);
        output.Write(block.Name);
        output.Write((byte)'"');
        if (opener == "FUNCTION") {
            output.Write(" : "u8);
            output.Write(ReturnType(members));
        }
        output.Write((byte)'\n');
        if (!string.IsNullOrEmpty(block.Title)) {
            output.Write("TITLE = "u8);
            WriteOneLine(output, block.Title);
            output.Write((byte)'\n');
        }
        WriteComment(output, program.BlockComment(block));
        foreach (var member in members) {
            if (member.Section == "Return" && !string.IsNullOrEmpty(member.Comment)) {
                output.Write("// returns: "u8);
                WriteOneLine(output, member.Comment);
                output.Write((byte)'\n');
            }
        }

        if (opener == "TYPE") {
            output.Write("   STRUCT\n"u8);
            WriteMembers(output, members, 2);
            output.Write("   END_STRUCT;\nEND_TYPE\n"u8);
            return 0;
        }
        if (opener == "DATA_BLOCK") {
            WriteSections(output, members, "VAR");
            output.Write("BEGIN\nEND_DATA_BLOCK\n"u8);
            return 0;
        }

        WriteSections(output, members, null);
        output.Write("BEGIN\n"u8);
        var unknown = 0;
        if (block.IsKnowHowProtected) {
            output.Write(ProtectedMarker);
            output.Write((byte)'\n');
        } else {
            var networks = program.Networks(block);
            for (var i = 0; i < networks.Count; i++) {
                var network = networks[i];
                if (networks.Count > 1 || network.Form != "SCLSource") {
                    output.Write("\n// ---- network "u8);
                    output.Write(network.Number.ToString(CultureInfo.InvariantCulture));
                    output.Write(" ("u8);
                    output.Write(network.Language);
                    output.Write(")"u8);
                    if (!string.IsNullOrEmpty(network.Title)) {
                        output.Write(": "u8);
                        WriteOneLine(output, network.Title);
                    }
                    output.Write((byte)'\n');
                }
                program.WriteNetworkSource(block, i, output, out var missing);
                unknown += missing;
                output.Write((byte)'\n');
            }
        }
        output.Write("END_"u8);
        output.Write(opener);
        output.Write((byte)'\n');
        return unknown;
    }

    private static string ReturnType(IReadOnlyList<TiaMember> members) {
        foreach (var member in members) {
            if (member.Section == "Return") {
                return member.Type;
            }
        }
        return "Void";
    }

    private static void WriteSections(PooledByteWriter output, IReadOnlyList<TiaMember> members, string? flat) {
        var i = 0;
        while (i < members.Count) {
            var section = members[i].Section;
            var start = i;
            while (i < members.Count && members[i].Section == section) {
                i++;
            }
            if (section == "Return") {
                continue;
            }
            var keyword = flat is not null && section.Length == 0 ? flat : section switch {
                "Input" => "VAR_INPUT",
                "Output" => "VAR_OUTPUT",
                "InOut" => "VAR_IN_OUT",
                "Static" => "VAR",
                "Temp" => "VAR_TEMP",
                "Constant" => "VAR CONSTANT",
                _ => "VAR",
            };
            output.Write("   "u8);
            output.Write(keyword);
            output.Write((byte)'\n');
            for (var m = start; m < i; m++) {
                WriteMember(output, members[m], 2);
            }
            output.Write("   END_VAR\n"u8);
        }
    }

    private static void WriteMembers(PooledByteWriter output, IReadOnlyList<TiaMember> members, int depth) {
        foreach (var member in members) {
            WriteMember(output, member, depth);
        }
    }

    private static void WriteMember(PooledByteWriter output, TiaMember member, int depth) {
        output.WriteSpaces(depth * 3);
        output.Write(member.Name);
        output.Write(" : "u8);
        output.Write(member.Type);
        if (member.StartValue is not null) {
            output.Write(" := "u8);
            WriteOneLine(output, member.StartValue);
        }
        if (member.Children.Count == 0) {
            output.Write((byte)';');
        }
        if (!string.IsNullOrEmpty(member.Comment)) {
            output.Write("   // "u8);
            WriteOneLine(output, member.Comment);
        }
        output.Write((byte)'\n');
        if (member.Children.Count != 0) {
            WriteMembers(output, member.Children, depth + 1);
            output.WriteSpaces(depth * 3);
            output.Write("END_STRUCT;\n"u8);
        }
    }

    private static void WriteComment(PooledByteWriter output, string? comment) {
        if (string.IsNullOrEmpty(comment)) {
            return;
        }
        foreach (var range in comment.AsSpan().EnumerateLines()) {
            output.Write("// "u8);
            output.Write(range.ToString());
            output.Write((byte)'\n');
        }
    }

    private static void WriteOneLine(PooledByteWriter output, string text) {
        var first = true;
        foreach (var line in text.AsSpan().EnumerateLines()) {
            if (!first) {
                output.Write((byte)' ');
            }
            output.Write(line.ToString());
            first = false;
        }
    }
}
