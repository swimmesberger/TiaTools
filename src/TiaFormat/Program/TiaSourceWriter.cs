using System.Globalization;

namespace TiaFormat.Program;

/// <summary>
/// Writes a whole block as one source text. SCL blocks, UDTs and data blocks match what TIA generates from them, after
/// our leading comment lines and before our trailing ones; FBD follows the same frame. For reading, not for importing.
/// </summary>
public static class TiaSourceWriter {
    /// <summary>What stands where protected source would be. The source is never opened.</summary>
    public static ReadOnlySpan<byte> ProtectedMarker => "// know-how protected: source closed, not read"u8;

    /// <summary>Writes the block; returns the count of SCL tags without a rule.</summary>
    public static int Write(TiaProgram program, TiaBlock block, PooledByteWriter output, TiaExactTokens? exact = null) {
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
        var header = program.Header(block);
        var settings = opener == "DATA_BLOCK" ? program.DataBlockSettings(block) : new TiaDataBlockSettings(false, true, true);
        var instruction = opener == "DATA_BLOCK" ? program.InstructionVersion(block) : null;
        WriteAttributes(output, opener, header, settings, instruction is null ? null : (program.GeneratedFrom(block) ?? "", instruction));
        WriteHeader(output, header);
        if (settings.IsNonRetain) {
            output.Write("NON_RETAIN\n"u8);
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
            output.Write("   END_STRUCT;\n\nEND_TYPE\n\n"u8);
            return 0;
        }
        if (opener == "DATA_BLOCK") {
            WriteDataBlock(output, program, block, members, header.OptimizedAccess, instruction is not null);
            return 0;
        }

        // An OB's inputs are the system's: TIA leaves them out of the source.
        WriteSections(output, opener == "ORGANIZATION_BLOCK" ? [.. members.Where(member => member.Section != "Input")] : members);
        output.Write("\nBEGIN\n"u8);
        var unknown = 0;
        if (block.IsKnowHowProtected) {
            output.Write(ProtectedMarker);
            output.Write((byte)'\n');
        } else if (block.Language?.StartsWith("GRAPH", StringComparison.Ordinal) == true) {
            // A GRAPH block is one sequence spread over many units; it is written as a whole.
            output.Write("\n"u8);
            foreach (var line in TiaGraph.Write(program.ReadGraph(block))) {
                output.Write(line);
                output.Write((byte)'\n');
            }
        } else {
            var networks = program.Networks(block);
            var statementList = networks.Count != 0 && networks.All(network => network.Form == "Statements");
            for (var i = 0; i < networks.Count; i++) {
                var network = networks[i];
                if (statementList) {
                    // An STL block as TIA generates its source: NETWORK, its title and comment, the statements.
                    output.Write("NETWORK\nTITLE = "u8);
                    WriteOneLine(output, network.Title ?? "");
                    output.Write((byte)'\n');
                    foreach (var line in (network.Comment ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                        output.Write("//"u8);
                        output.Write(line.TrimEnd('\r'));
                        output.Write((byte)'\n');
                    }
                    program.WriteNetworkSource(block, i, output, out _);
                    output.Write((byte)'\n');
                    continue;
                }
                if (networks.Count > 1 || network.Form != "SCLSource") {
                    output.Write("\n// ---- network "u8);
                    output.Write(network.Number.ToString(CultureInfo.InvariantCulture));
                    output.Write(" ("u8);
                    // TIA names classic LAD and FBD as LAD and FBD; only it knows them by the longer names.
                    output.Write(network.Language is "LAD_CLASSIC" or "FBD_CLASSIC" ? network.Language[..3] : network.Language);
                    output.Write(")"u8);
                    if (!string.IsNullOrEmpty(network.Title)) {
                        output.Write(": "u8);
                        WriteOneLine(output, network.Title);
                    }
                    output.Write((byte)'\n');
                    foreach (var line in (network.Comment ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                        output.Write("// "u8);
                        output.Write(line.TrimEnd('\r'));
                        output.Write((byte)'\n');
                    }
                }
                int missing;
                if (network.Form == "SCLSource") {
                    using var source = new PooledByteWriter();
                    var tokens = exact is null ? null : new List<TiaToken>();
                    program.WriteNetworkSource(block, i, source, out missing, tokens);
                    if (source.Length != 0) {
                        var at = output.Length;
                        WriteIndented(output, source.WrittenSpan);
                        if (tokens is not null) {
                            exact!.AddIndented(at, source.WrittenSpan, tokens, output.Length);
                        }
                        output.Write((byte)'\n');
                    }
                } else {
                    program.WriteNetworkSource(block, i, output, out missing);
                    output.Write((byte)'\n');
                }
                unknown += missing;
            }
        }
        output.Write("END_"u8);
        output.Write(opener);
        output.Write("\n\n"u8);
        if (opener == "ORGANIZATION_BLOCK") {
            WriteAsComment(output, "// The inputs the system provides:\n"u8, [.. members.Where(member => member.Section == "Input")]);
        }
        return unknown;
    }

    /// <summary>Writes a tag table: <c>"name" : type AT address := value;   // comment</c>.</summary>
    public static void WriteTagTable(TiaTagTable table, PooledByteWriter output) {
        output.Write("// tag table "u8);
        output.Write(table.Name);
        output.Write((byte)'\n');
        foreach (var tag in table.Tags) {
            output.Write((byte)'"');
            output.Write(tag.Name);
            output.Write("\" : "u8);
            output.Write(tag.Type);
            if (!string.IsNullOrEmpty(tag.Address)) {
                output.Write(" AT "u8);
                output.Write(tag.Address);
            }
            if (!string.IsNullOrEmpty(tag.Value)) {
                output.Write(" := "u8);
                output.Write(tag.Value);
            }
            output.Write((byte)';');
            if (!string.IsNullOrEmpty(tag.Comment)) {
                output.Write("   // "u8);
                output.Write(tag.Comment.ReplaceLineEndings(" "));
            }
            output.Write((byte)'\n');
        }
    }

    /// <summary>
    /// Writes a technology object as text of our own (spec 0013): its kind and version, then each parameter as TIA's
    /// parameter view names it, <c>DynamicLimits.MaxVelocity : LReal := 2400.0;</c>. Nothing TIA could import.
    /// </summary>
    public static void WriteTechnologyObject(TiaProgram program, TiaTechnologyObject technologyObject, PooledByteWriter output) {
        output.Write("// TO number="u8);
        output.Write(technologyObject.Number?.ToString(CultureInfo.InvariantCulture) ?? "-");
        output.Write(" kind="u8);
        output.Write(technologyObject.Kind);
        output.Write(" version="u8);
        output.Write(technologyObject.Version ?? "-");
        output.Write(" know-how="u8);
        output.Write(technologyObject.IsKnowHowProtected ? "yes"u8 : "no"u8);
        output.Write(" plc="u8);
        output.Write(program.PlcName(technologyObject.Frame) ?? "-");
        output.Write("\n// folder: "u8);
        output.Write(program.FolderPath(technologyObject.Frame));
        output.Write("\nTECHNOLOGY_OBJECT \""u8);
        output.Write(technologyObject.Name);
        output.Write("\" : "u8);
        output.Write(technologyObject.Kind);
        output.Write("\n{ VERSION := '"u8);
        output.Write(technologyObject.Version ?? "");
        output.Write("'; NUMBER := "u8);
        output.Write(technologyObject.Number?.ToString(CultureInfo.InvariantCulture) ?? "-");
        output.Write(" }\n"u8);
        var parameters = program.ReadParameters(technologyObject);
        if (parameters.Count == 0 && technologyObject.IsKnowHowProtected) {
            output.Write(ProtectedMarker);
            output.Write((byte)'\n');
        }
        foreach (var parameter in parameters) {
            output.Write("   "u8);
            output.Write(parameter.Name);
            output.Write(" : "u8);
            output.Write(parameter.Type);
            if (parameter.Value is { } value) {
                output.Write(" := "u8);
                output.Write(value);
            }
            output.Write(";\n"u8);
        }
        output.Write("END_TECHNOLOGY_OBJECT\n"u8);
    }

    private static string ReturnType(IReadOnlyList<TiaMember> members) {
        foreach (var member in members) {
            if (member.Section == "Return") {
                return member.Type;
            }
        }
        return "Void";
    }

    private static void WriteSections(PooledByteWriter output, IReadOnlyList<TiaMember> members) {
        var i = 0;
        while (i < members.Count) {
            var section = members[i].Section;
            var retain = members[i].IsRetain;
            var specific = members[i].IsDbSpecific;
            var start = i;
            while (i < members.Count && members[i].Section == section && members[i].IsRetain == retain && members[i].IsDbSpecific == specific) {
                i++;
            }
            if (section == "Return") {
                continue;
            }
            output.Write("   "u8);
            output.Write(specific ? "VAR DB_SPECIFIC" : retain && section is "Static" or "" ? "VAR RETAIN" : section switch {
                "Input" => "VAR_INPUT ",
                "Output" => "VAR_OUTPUT ",
                "InOut" => "VAR_IN_OUT ",
                "Temp" => "VAR_TEMP ",
                "Constant" => "VAR CONSTANT ",
                _ => "VAR ",
            });
            output.Write((byte)'\n');
            for (var m = start; m < i; m++) {
                WriteMember(output, members[m], 2);
            }
            // The retained and the other members of one section follow each other with no line between them.
            output.Write(i < members.Count && members[i].Section == section ? "   END_VAR\n"u8 : "   END_VAR\n\n"u8);
        }
    }

    /// <summary>
    /// A data block of a type names the type and lists only the values set in it, a shared one declares its members.
    /// A reader wants a type's members and the known values too, so they follow as comments.
    /// </summary>
    private static void WriteDataBlock(PooledByteWriter output, TiaProgram program, TiaBlock block, IReadOnlyList<TiaMember> members, bool optimized, bool systemType) {
        var type = program.GeneratedFrom(block);
        if (type is not null) {
            // A system type, such as IEC_TIMER, is named without quotes, as a user's type never is.
            if (!systemType) {
                output.Write((byte)'"');
            }
            output.Write(type);
            output.Write(systemType ? "\n\n"u8 : "\"\n\n"u8);
        } else if (optimized) {
            WriteSections(output, members);
            output.Write((byte)'\n');
        } else {
            output.Write("   STRUCT \n"u8);
            WriteMembers(output, members, 2);
            output.Write("   END_STRUCT;\n\n\n"u8);
        }
        output.Write("BEGIN\n"u8);
        foreach (var value in program.ReadSetValues(block)) {
            output.Write(value.IsResolved ? "   "u8 : "   // not resolved: "u8);
            output.Write(value.Name);
            output.Write(" := "u8);
            output.Write(value.Value);
            output.Write(";\n"u8);
        }
        output.Write("\nEND_DATA_BLOCK\n\n"u8);
        WriteProDiagAssignments(output, program.ReadProDiagAssignments(block));
        if (type is not null) {
            WriteAsComment(output, "// The members of its type, with the start values known:\n"u8, members);
        }
    }

    /// <summary>
    /// Which ProDiag function block a data block or its members are assigned to, as comment lines after the block: TIA's
    /// generated source does not show it (spec 0016).
    /// </summary>
    private static void WriteProDiagAssignments(PooledByteWriter output, IReadOnlyList<TiaProDiagAssignment> assignments) {
        foreach (var assignment in assignments) {
            output.Write("// assigned to ProDiag function block "u8);
            output.Write(assignment.FunctionBlock);
            if (assignment.Member is not null) {
                output.Write(": "u8);
                output.Write(assignment.Member);
            }
            output.Write((byte)'\n');
        }
    }

    private static void WriteAsComment(PooledByteWriter output, ReadOnlySpan<byte> heading, IReadOnlyList<TiaMember> members) {
        if (members.Count == 0) {
            return;
        }
        output.Write(heading);
        using var text = new PooledByteWriter();
        WriteMembers(text, members, 1, startValues: true);
        var rest = text.WrittenSpan;
        while (!rest.IsEmpty) {
            var end = rest.IndexOf((byte)'\n');
            output.Write("//"u8);
            output.Write(rest[..(end + 1)]);
            rest = rest[(end + 1)..];
        }
    }

    private static void WriteMembers(PooledByteWriter output, IReadOnlyList<TiaMember> members, int depth, bool startValues = false) {
        foreach (var member in members) {
            WriteMember(output, member, depth, startValues);
        }
    }

    /// <summary>A declaration states the value its member is declared with. Our comments give the value it starts with.</summary>
    private static void WriteMember(PooledByteWriter output, TiaMember member, int depth, bool startValues = false) {
        output.WriteSpaces(depth * 3);
        output.Write(TiaIdentifiers.Quoted(member.Name));
        if (member.Attributes is not null) {
            output.Write((byte)' ');
            output.Write(member.Attributes);
        }
        if (member.AtMember is not null) {
            output.Write(" AT "u8);
            output.Write(TiaIdentifiers.Quoted(member.AtMember));
        }
        output.Write(" : "u8);
        output.Write(member.Type);
        var value = startValues ? member.StartValue : member.Declared;
        if (value is not null) {
            output.Write(" := "u8);
            WriteOneLine(output, value);
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
            WriteMembers(output, member.Children, depth + 1, startValues);
            output.WriteSpaces(depth * 3);
            output.Write("END_STRUCT;\n"u8);
        }
    }

    private static void WriteAttributes(PooledByteWriter output, string opener, TiaBlockHeader header, TiaDataBlockSettings settings, (string Name, string Version)? instruction = null) {
        if (opener is not ("FUNCTION_BLOCK" or "FUNCTION" or "ORGANIZATION_BLOCK" or "DATA_BLOCK")) {
            return;
        }
        // A data block of a system type names it first, right after the brace, as an instruction's member does.
        if (instruction is { } system) {
            output.Write("{InstructionName := '"u8);
            output.Write(system.Name);
            output.Write("';\n LibVersion := '"u8);
            output.Write(system.Version);
            output.Write("';\n "u8);
        } else {
            output.Write("{ "u8);
        }
        if (!settings.AccessibleFromOpcUa) {
            output.Write("DB_Accessible_From_OPC_UA := 'FALSE' ;\n "u8);
        }
        if (!settings.AccessibleFromWebserver) {
            output.Write("DB_Accessible_From_Webserver := 'FALSE' ;\n "u8);
        }
        output.Write(header.OptimizedAccess ? "S7_Optimized_Access := 'TRUE' }\n"u8 : "S7_Optimized_Access := 'FALSE' }\n"u8);
    }

    private static void WriteHeader(PooledByteWriter output, TiaBlockHeader header) {
        WriteField(output, "AUTHOR : "u8, header.Author);
        WriteField(output, "FAMILY : "u8, header.Family);
        WriteField(output, "NAME : "u8, header.Name);
        WriteField(output, "VERSION : "u8, string.IsNullOrEmpty(header.Version) ? "0.1" : header.Version);
    }

    private static void WriteField(PooledByteWriter output, ReadOnlySpan<byte> label, string? value) {
        if (string.IsNullOrEmpty(value)) {
            return;
        }
        output.Write(label);
        output.Write(value);
        output.Write((byte)'\n');
    }

    /// <summary>TIA indents the body of a source file by one tab, its empty lines too.</summary>
    private static void WriteIndented(PooledByteWriter output, ReadOnlySpan<byte> source) {
        while (true) {
            output.Write((byte)'\t');
            var end = source.IndexOf((byte)'\n');
            if (end < 0) {
                output.Write(source);
                return;
            }
            output.Write(source[..(end + 1)]);
            source = source[(end + 1)..];
        }
    }

    internal static void WriteComment(PooledByteWriter output, string? comment) {
        if (string.IsNullOrEmpty(comment)) {
            return;
        }
        foreach (var range in comment.AsSpan().EnumerateLines()) {
            output.Write("//"u8);
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
