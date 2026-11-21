using System.Globalization;
using System.Text.Json;

namespace TiaFormat.Program;

/// <summary>Writes a <see cref="TiaSafetyReport"/> as a Markdown report for people, or as JSON for programs (spec 0012).</summary>
public static class TiaSafetyWriter {
    /// <summary>The Markdown report: per PLC its safety program's settings, signatures, F-runtime groups, F-I/O and F-blocks.</summary>
    public static void WriteMarkdown(TiaSafetyReport report, string title, TextWriter output) {
        output.Write("# Safety: ");
        output.WriteLine(title);
        if (report.Plcs.Count == 0) {
            output.WriteLine();
            output.WriteLine("The project has no PLC.");
        }
        foreach (var plc in report.Plcs) {
            output.WriteLine();
            output.Write("## PLC ");
            output.WriteLine(plc.Name);
            output.WriteLine();
            if (plc.Program is not { } program) {
                output.WriteLine("No safety program.");
                continue;
            }
            WriteProgram(program, output);
        }
    }

    private static void WriteProgram(TiaSafetyProgram program, TextWriter output) {
        var settings = program.Settings;
        var signatures = program.Signatures;
        output.WriteLine("| Setting | Value |");
        output.WriteLine("|---|---|");
        Row(output, "Safety system version", settings.SystemVersion ?? "");
        Row(output, "Safety program password", program.IsPasswordSet ? "set" : "not set");
        Row(output, "F-change history", OnOff(settings.ChangeHistory));
        Row(output, "Safety mode can be disabled", settings.SafetyModeCanBeDisabled ? "yes" : "no");
        Row(output, "F-block numbers", settings.FixedBlockNumbers
            ? Invariant($"fixed: F-FB {Range(settings.FbNumbers)}, F-FC {Range(settings.FcNumbers)}, F-DB {Range(settings.DbNumbers)}")
            : "managed by the F-system");
        Row(output, "Collective F-signature", Signature(signatures.Collective));
        Row(output, "Collective F-SW signature", Signature(signatures.Software));
        Row(output, "Collective F-HW signature", Signature(signatures.Hardware));
        Row(output, "F-communication address signature", Signature(signatures.CommunicationAddress));
        Row(output, "Last compiled", signatures.Compiled?.ToString("u", CultureInfo.InvariantCulture) ?? "");

        output.WriteLine();
        output.WriteLine("### F-runtime groups");
        output.WriteLine();
        if (program.RuntimeGroups.Count == 0) {
            output.WriteLine("None.");
        } else {
            output.WriteLine("| F-runtime group | F-OB | Event class | Cycle time | Main safety block | Instance DB | Maximum cycle time | Warning limit | Information DB |");
            output.WriteLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var group in program.RuntimeGroups) {
                output.WriteLine($"| {Cell(group.Name)} | {Reference(group.FOb)} | {Cell(group.EventClass ?? "")} | {Milliseconds(group.CycleTime)} | {Reference(group.MainBlock)} | {Reference(group.MainInstance)} | {Milliseconds(group.MaximumCycleTime)} | {Milliseconds(group.WarningCycleTime)} | {Cell(group.InformationBlock ?? "")} |");
            }
            foreach (var group in program.RuntimeGroups.Where(group => group.PreProcessing is not null || group.PostProcessing is not null)) {
                output.WriteLine();
                output.WriteLine($"{Cell(group.Name)}: pre-processing {group.PreProcessing ?? "none"}, post-processing {group.PostProcessing ?? "none"}.");
            }
        }

        output.WriteLine();
        output.WriteLine("### F-CPU");
        output.WriteLine();
        output.WriteLine("| F-parameter | Value |");
        output.WriteLine("|---|---|");
        Row(output, "Central F-source address", Value(program.Cpu.CentralFSourceAddress));
        Row(output, "Lowest F-destination address", Value(program.Cpu.LowestFDestinationAddress));
        Row(output, "Highest F-destination address", Value(program.Cpu.HighestFDestinationAddress));
        Row(output, "Default F-monitoring time", Value(program.Cpu.DefaultMonitoringTime, " ms"));

        output.WriteLine();
        output.WriteLine("### F-I/O");
        output.WriteLine();
        if (program.Io.Count == 0) {
            output.WriteLine("None.");
        } else {
            output.WriteLine("| Device | F-I/O | Inputs | Outputs | F-source address | F-destination address | F-monitoring time | F-parameter signature | F-I/O DB |");
            output.WriteLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var io in program.Io) {
                var time = io.MonitoringTime is { } ms ? Invariant($"{ms} ms") + (io.MonitoringTimeByHand ? " (set by hand)" : "") : "";
                var db = io.DataBlockNumber is { } number ? Invariant($"DB {number} {io.DataBlockName}") : io.DataBlockName ?? "";
                output.WriteLine($"| {Cell(io.Device)} | {Cell(io.Name)} | {Address("I", io.InputAddress)} | {Address("Q", io.OutputAddress)} | {Value(io.FSourceAddress)} | {Value(io.FDestinationAddress)} | {time} | {Crc(io.ParameterSignature)} | {Cell(db.Trim())} |");
            }
        }

        output.WriteLine();
        output.WriteLine("### F-blocks");
        output.WriteLine();
        var own = program.Blocks.Count(block => !block.IsGenerated);
        output.WriteLine(Invariant($"{own} written in the project, {program.Blocks.Count - own} generated by TIA."));
        output.WriteLine();
        output.WriteLine("| Block | Number | Name | Language | F-signature | |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var block in program.Blocks) {
            var notes = string.Join(", ", new[] { block.IsGenerated ? "generated" : null, block.IsKnowHowProtected ? "closed" : null }.Where(note => note is not null));
            output.WriteLine($"| {block.Kind} | {block.Number?.ToString(CultureInfo.InvariantCulture) ?? ""} | {Cell(block.Name)} | {block.Language ?? ""} | {Signature(block.Signature)} | {notes} |");
        }
    }

    /// <summary>The report as JSON. Signatures are hexadecimal strings, as TIA shows them; times are µs as stored, F-monitoring times ms.</summary>
    public static void WriteJson(TiaSafetyReport report, Stream output) {
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartArray("plcs");
        foreach (var plc in report.Plcs) {
            json.WriteStartObject();
            json.WriteString("name", plc.Name);
            if (plc.Program is not { } program) {
                json.WriteNull("safetyProgram");
                json.WriteEndObject();
                continue;
            }
            json.WriteStartObject("safetyProgram");
            var settings = program.Settings;
            json.WriteString("systemVersion", settings.SystemVersion);
            json.WriteBoolean("passwordSet", program.IsPasswordSet);
            json.WriteBoolean("changeHistory", settings.ChangeHistory);
            json.WriteBoolean("safetyModeCanBeDisabled", settings.SafetyModeCanBeDisabled);
            json.WriteStartObject("blockNumbers");
            json.WriteBoolean("fixed", settings.FixedBlockNumbers);
            RangeJson(json, "fb", settings.FbNumbers);
            RangeJson(json, "fc", settings.FcNumbers);
            RangeJson(json, "db", settings.DbNumbers);
            json.WriteEndObject();

            json.WriteStartObject("signatures");
            SignatureJson(json, "collective", program.Signatures.Collective);
            SignatureJson(json, "software", program.Signatures.Software);
            SignatureJson(json, "hardware", program.Signatures.Hardware);
            SignatureJson(json, "communicationAddress", program.Signatures.CommunicationAddress);
            if (program.Signatures.Compiled is { } compiled) {
                json.WriteString("compiled", compiled.ToString("o", CultureInfo.InvariantCulture));
            } else {
                json.WriteNull("compiled");
            }
            json.WriteEndObject();

            json.WriteStartArray("runtimeGroups");
            foreach (var group in program.RuntimeGroups) {
                json.WriteStartObject();
                json.WriteString("name", group.Name);
                ReferenceJson(json, "fOb", group.FOb);
                json.WriteString("eventClass", group.EventClass);
                Number(json, "cycleTimeUs", group.CycleTime);
                Number(json, "priority", group.Priority);
                ReferenceJson(json, "mainSafetyBlock", group.MainBlock);
                ReferenceJson(json, "mainSafetyInstance", group.MainInstance);
                json.WriteString("informationDb", group.InformationBlock);
                json.WriteString("preProcessing", group.PreProcessing);
                json.WriteString("postProcessing", group.PostProcessing);
                Number(json, "maximumCycleTimeUs", group.MaximumCycleTime);
                Number(json, "warningCycleTimeUs", group.WarningCycleTime);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartObject("fCpu");
            ValueJson(json, "centralFSourceAddress", program.Cpu.CentralFSourceAddress);
            ValueJson(json, "lowestFDestinationAddress", program.Cpu.LowestFDestinationAddress);
            ValueJson(json, "highestFDestinationAddress", program.Cpu.HighestFDestinationAddress);
            ValueJson(json, "defaultMonitoringTimeMs", program.Cpu.DefaultMonitoringTime);
            json.WriteEndObject();

            json.WriteStartArray("fIo");
            foreach (var io in program.Io) {
                json.WriteStartObject();
                json.WriteString("device", io.Device);
                json.WriteString("name", io.Name);
                Number(json, "inputAddress", io.InputAddress);
                Number(json, "outputAddress", io.OutputAddress);
                ValueJson(json, "fSourceAddress", io.FSourceAddress);
                ValueJson(json, "fDestinationAddress", io.FDestinationAddress);
                Number(json, "monitoringTimeMs", io.MonitoringTime);
                json.WriteBoolean("monitoringTimeByHand", io.MonitoringTimeByHand);
                Number(json, "parameterSignature", io.ParameterSignature);
                Number(json, "parameterSignatureWithoutAddresses", io.ParameterSignatureWithoutAddresses);
                Number(json, "dataBlockNumber", io.DataBlockNumber);
                json.WriteString("dataBlockName", io.DataBlockName);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("blocks");
            foreach (var block in program.Blocks) {
                json.WriteStartObject();
                json.WriteString("kind", block.Kind);
                Number(json, "number", block.Number);
                json.WriteString("name", block.Name);
                json.WriteString("language", block.Language);
                SignatureJson(json, "signature", block.Signature);
                json.WriteBoolean("generated", block.IsGenerated);
                json.WriteBoolean("closed", block.IsKnowHowProtected);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Row(TextWriter output, string name, string value) => output.WriteLine($"| {name} | {Cell(value)} |");

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Range(TiaNumberRange range) => Invariant($"{range.From} to {range.To}");

    private static string Signature(uint? value) => value is { } v ? TiaSafety.Hex(v) : "none";

    private static string Crc(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Value(TiaSafetyValue value, string unit = "") =>
        value.Value is { } v ? Invariant($"{v}{unit}") + (value.IsDefault ? " (default)" : "") : "";

    private static string Address(string area, long? value) => value is { } v ? Invariant($"{area} {v}") : "";

    private static string Milliseconds(long? microseconds) => microseconds is { } us ? (us / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " ms" : "";

    private static string Reference(TiaSafetyBlockReference? block) =>
        block is { } b ? Cell(b.Number is { } n ? Invariant($"{b.Name} ({b.Kind} {n})") : b.Name) : "";

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static void Number(Utf8JsonWriter json, string name, long? value) {
        if (value is { } v) {
            json.WriteNumber(name, v);
        } else {
            json.WriteNull(name);
        }
    }

    private static void SignatureJson(Utf8JsonWriter json, string name, uint? value) {
        if (value is { } v) {
            json.WriteString(name, TiaSafety.Hex(v));
        } else {
            json.WriteNull(name);
        }
    }

    private static void RangeJson(Utf8JsonWriter json, string name, TiaNumberRange range) {
        json.WriteStartObject(name);
        json.WriteNumber("from", range.From);
        json.WriteNumber("to", range.To);
        json.WriteEndObject();
    }

    private static void ValueJson(Utf8JsonWriter json, string name, TiaSafetyValue value) {
        json.WriteStartObject(name);
        Number(json, "value", value.Value);
        json.WriteBoolean("default", value.IsDefault);
        json.WriteEndObject();
    }

    private static void ReferenceJson(Utf8JsonWriter json, string name, TiaSafetyBlockReference? block) {
        if (block is not { } b) {
            json.WriteNull(name);
            return;
        }
        json.WriteStartObject(name);
        json.WriteString("kind", b.Kind);
        Number(json, "number", b.Number);
        json.WriteString("name", b.Name);
        json.WriteEndObject();
    }
}
