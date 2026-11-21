using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using TiaFormat;
using TiaFormat.Hardware;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaDump;

/// <summary>The command line. Reads an archive and never writes to it; protected source stays closed.</summary>
internal sealed class TiaDumpCommands {
    /// <summary>Lists the blocks and technology objects of an archive.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="grep">Only blocks whose name matches this regular expression.</param>
    /// <param name="stubs">Also list references to blocks held elsewhere.</param>
    [Command("list")]
    public int List([Argument] string archive, string? grep = null, bool stubs = false) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
        var filter = grep is null ? null : new Regex(grep, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var rows = 0;
        foreach (var block in program.Blocks) {
            if ((block.IsStub && !stubs) || (filter is not null && !filter.IsMatch(block.Name))) {
                continue;
            }
            var (kind, _, _) = program.Environment(block.Frame);
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{program.PlcName(block.Frame),-24} {kind,-22} {block.Kind,-6} {block.Number,6} {(block.IsKnowHowProtected ? "KHP" : "   ")} {(block.IsStub ? "STUB" : "    ")} {block.Language,-10} {block.Name}  [{program.FolderPath(block.Frame)}]"));
            rows++;
        }
        var objects = 0;
        foreach (var technologyObject in program.TechnologyObjects) {
            if (filter is not null && !filter.IsMatch(technologyObject.Name)) {
                continue;
            }
            var (kind, _, _) = program.Environment(technologyObject.Frame);
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{program.PlcName(technologyObject.Frame),-24} {kind,-22} {"TO",-6} {technologyObject.Number,6} {(technologyObject.IsKnowHowProtected ? "KHP" : "   ")}      {technologyObject.Kind + " " + technologyObject.Version,-10} {technologyObject.Name}  [{program.FolderPath(technologyObject.Frame)}]"));
            objects++;
        }
        Console.Error.WriteLine($"{rows} blocks, {objects} technology objects");
        return 0;
    }

    /// <summary>Prints every block, watch table, force table or technology object of a name as text.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="name">The block's name.</param>
    /// <param name="plc">Only the block of this PLC.</param>
    /// <param name="language">Titles and comments in this project language, such as de-DE; the editing language if not given.</param>
    [Command("show")]
    public int Show([Argument] string archive, [Argument] string name, string? plc = null, string? language = null) {
        if (Open(TiaDatabase.Open(archive), language) is not { } program) {
            return 1;
        }
        using var output = new PooledByteWriter();
        using var stdout = Console.OpenStandardOutput();
        var found = false;
        foreach (var block in program.Blocks) {
            if (block.Name != name || block.IsStub || (plc is not null && program.PlcName(block.Frame) != plc)) {
                continue;
            }
            found = true;
            output.Clear();
            TiaSourceWriter.Write(program, block, output);
            stdout.Write(output.WrittenSpan);
        }
        // A watch or force table is shown by its name too, as its .watch or .force file.
        foreach (var table in TiaWatchTables.Read(program)) {
            if (table.Name != name || table.Entries.Count == 0 || (plc is not null && program.PlcName(table.Frame) != plc)) {
                continue;
            }
            found = true;
            stdout.Write(System.Text.Encoding.UTF8.GetBytes(TiaWatchTables.Text(table)));
        }
        foreach (var technologyObject in program.TechnologyObjects) {
            if (technologyObject.Name != name || (plc is not null && program.PlcName(technologyObject.Frame) != plc)) {
                continue;
            }
            found = true;
            output.Clear();
            TiaSourceWriter.WriteTechnologyObject(program, technologyObject, output);
            stdout.Write(output.WrittenSpan);
        }
        if (!found) {
            Console.Error.WriteLine($"No block or table named {name}.");
            return 1;
        }
        return 0;
    }

    /// <summary>Prints what is stored for an object: its attributes and relations, for finding where a fact lives.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="name">A block's name, or an object id.</param>
    /// <param name="plc">Only the block of this PLC.</param>
    /// <param name="full">Print long values whole instead of their start.</param>
    [Command("inspect")]
    public int Inspect([Argument] string archive, [Argument] string name, string? plc = null, bool full = false) {
        var database = TiaDatabase.Open(archive);
        var program = new TiaProgram(database);
        var found = false;
        if (ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) {
            foreach (ref readonly var frame in database.Frames) {
                if (frame.ObjectId == id) {
                    TiaInspector.Write(database, frame, Console.Out, full);
                    found = true;
                }
            }
        } else {
            foreach (var block in program.Blocks) {
                if (block.Name == name && !block.IsStub && (plc is null || program.PlcName(block.Frame) == plc)) {
                    TiaInspector.Write(database, block.Frame, Console.Out, full);
                    found = true;
                }
            }
        }
        if (!found) {
            Console.Error.WriteLine($"Nothing named {name}.");
            return 1;
        }
        return 0;
    }

    /// <summary>Reports each PLC's OPC UA server settings, roles, and which data blocks and tags an OPC UA client could reach.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="json">Write JSON instead of the Markdown report.</param>
    /// <param name="plc">Only this PLC.</param>
    /// <param name="nodes">Write the nodes of the standard server interface as tab-separated text instead.</param>
    /// <param name="nodeset">Write the node set file of the PLC (--plc, unless there is one only) instead.</param>
    [Command("opcua")]
    public int OpcUa([Argument] string archive, bool json = false, string? plc = null, bool nodes = false, bool nodeset = false) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
        if (nodes || nodeset) {
            var lists = TiaOpcUaNodes.Read(program, plc);
            if (plc is not null && lists.Count == 0) {
                Console.Error.WriteLine($"No PLC named {plc}.");
                return 1;
            }
            if (nodeset) {
                if (lists.Count != 1) {
                    Console.Error.WriteLine(lists.Count == 0 ? "The project has no PLC." : "The project has more than one PLC; name one with --plc.");
                    return 1;
                }
                using var stdout = Console.OpenStandardOutput();
                TiaOpcUaNodeSet.Write(lists[0], stdout);
                stdout.Write("\n"u8);
            } else {
                TiaOpcUaNodes.WriteText(lists, Console.Out);
            }
            return 0;
        }
        var report = TiaOpcUa.Read(program, plc);
        if (plc is not null && report.Plcs.Count == 0) {
            Console.Error.WriteLine($"No PLC named {plc}.");
            return 1;
        }
        if (json) {
            using var stdout = Console.OpenStandardOutput();
            TiaOpcUaWriter.WriteJson(report, stdout);
            stdout.Write("\n"u8);
        } else {
            TiaOpcUaWriter.WriteMarkdown(report, TiaOpcUaWriter.Title(archive), Console.Out);
        }
        return 0;
    }

    /// <summary>Lists the devices with their racks, modules, I/O addresses and interfaces, then the subnets and IO systems.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="json">Write JSON instead of text.</param>
    [Command("devices")]
    public int Devices([Argument] string archive, bool json = false) {
        var report = TiaHardware.Read(new TiaProgram(TiaDatabase.Open(archive)));
        if (json) {
            using var stdout = Console.OpenStandardOutput();
            TiaHardwareWriter.WriteJson(report, stdout);
            stdout.Write("\n"u8);
        } else {
            TiaHardwareWriter.WriteText(report, Console.Out);
        }
        return 0;
    }

    /// <summary>
    /// Lists cross references, one use per line: who uses what, with the access, network and line. They come from TIA's
    /// cross-reference list in the archive when it belongs to it, else from the text; the first line says which.
    /// </summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="name">Only uses of, or by, the block, tag, table or member of this name.</param>
    /// <param name="plc">Only uses in this PLC.</param>
    [Command("xref")]
    public int CrossReferences([Argument] string archive, [Argument] string? name = null, string? plc = null) {
        var output = new StringWriter { NewLine = "\n" };
        TiaCrossReferenceWriter.Write(TiaProject.Open(archive), output, name, plc);
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(System.Text.Encoding.UTF8.GetBytes(output.ToString()));
        return 0;
    }

    /// <summary>Reports each PLC's safety program: settings, F-runtime groups, signatures, F-blocks and the F-parameters of its F-I/O. Never a password.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="json">Write JSON instead of the Markdown report.</param>
    /// <param name="plc">Only this PLC.</param>
    [Command("safety")]
    public int Safety([Argument] string archive, bool json = false, string? plc = null) {
        var report = TiaSafety.Read(new TiaProgram(TiaDatabase.Open(archive)), plc);
        if (plc is not null && report.Plcs.Count == 0) {
            Console.Error.WriteLine($"No PLC named {plc}.");
            return 1;
        }
        if (json) {
            using var stdout = Console.OpenStandardOutput();
            TiaSafetyWriter.WriteJson(report, stdout);
            stdout.Write("\n"u8);
        } else {
            TiaSafetyWriter.WriteMarkdown(report, TiaOpcUaWriter.Title(archive), Console.Out);
        }
        return 0;
    }

    /// <summary>Reports each HMI device: its connections, HMI tags with the PLC tags they read, HMI alarms and screens.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="json">Write JSON instead of the Markdown report.</param>
    /// <param name="device">Only this HMI device.</param>
    /// <param name="scripts">Add each device's code: HMI scripts, global script modules, VB scripts and custom web controls.</param>
    [Command("hmi")]
    public int Hmi([Argument] string archive, bool json = false, string? device = null, bool scripts = false) {
        var report = TiaHmi.Read(new TiaProgram(TiaDatabase.Open(archive)), device, scripts);
        if (device is not null && report.Devices.Count == 0) {
            Console.Error.WriteLine($"No HMI device named {device}.");
            return 1;
        }
        if (json) {
            using var stdout = Console.OpenStandardOutput();
            TiaHmiWriter.WriteJson(report, stdout);
            stdout.Write("\n"u8);
        } else {
            TiaHmiWriter.WriteMarkdown(report, TiaOpcUaWriter.Title(archive), Console.Out);
        }
        return 0;
    }

    /// <summary>Compares a dump with what TIA generated, block by block, and exits with 1 if any text differs.</summary>
    /// <param name="dump">A folder written by the dump command.</param>
    /// <param name="export">A folder written by tools/TiaOpennessExport.</param>
    [Command("compare")]
    public int Compare([Argument] string dump, [Argument] string export) => ExportComparison.Run(dump, export);

    /// <summary>Dumps every block and tag table of an archive as source files, in its folder tree.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="output">The folder to write to; created if missing.</param>
    /// <param name="grep">Only blocks whose name matches this regular expression; leaves out tag tables and HMI devices.</param>
    /// <param name="raw">Also write each network as stored, next to the block (never encrypted parts).</param>
    /// <param name="verify">Check every frame's hash on opening.</param>
    /// <param name="language">Titles and comments in this project language, such as de-DE; the editing language if not given. The texts tables hold every language either way.</param>
    [Command("dump")]
    public int Dump([Argument] string archive, [Argument] string output, string? grep = null, bool raw = false, bool verify = false, string? language = null) {
        var clock = Stopwatch.StartNew();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var database = TiaDatabase.Open(archive, verify);
        if (Open(database, language) is not { } program) {
            return 1;
        }
        var opened = clock.Elapsed;
        var filter = grep is null ? null : new Regex(grep, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // A filter on block names leaves the tag tables out.
        var result = new TiaSourceExport(program, output) {
            Blocks = filter is null ? null : block => filter.IsMatch(block.Name),
            TagTables = filter is null ? null : _ => false,
            WatchTables = filter is null ? null : _ => false,
            TechnologyObjects = filter is null ? null : technologyObject => filter.IsMatch(technologyObject.Name),
            HmiDevices = filter is null,
            Texts = filter is null,
            Raw = raw,
        }.Run();
        clock.Stop();
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{database.AllFrameCount} frames, {database.Frames.Length} current; {result.BlockFiles} block files, {result.TagFiles} tag files, {result.TextFiles} text tables, {result.HmiFiles} HMI files, {result.WatchFiles} watch and force tables, {result.TechnologyObjectFiles} technology objects, {result.Protected} protected, {result.Stubs} stubs, {result.Empty} with nothing stored"));
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{result.Bytes / 1024.0 / 1024.0:F1} MB written; opened in {opened.TotalMilliseconds:F0} ms, total {clock.Elapsed.TotalMilliseconds:F0} ms, {(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1024.0 / 1024.0:F0} MB allocated"));
        if (result.UnknownTags != 0) {
            Console.Error.WriteLine($"{result.UnknownTags} SCL tags without a rule, shown as «tag» in the source.");
        }
        foreach (var failure in result.Failures) {
            Console.Error.WriteLine(failure);
        }
        return result.Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Lists the project languages, marking the editing and the reference language.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    [Command("languages")]
    public int Languages([Argument] string archive) {
        var languages = TiaLanguages.Read(TiaDatabase.Open(archive));
        if (languages.All.Count == 0) {
            Console.Error.WriteLine("The file names no project languages.");
            return 1;
        }
        foreach (var language in languages.All) {
            Console.Out.WriteLine(languages.Describe(language));
        }
        return 0;
    }

    /// <summary>Prints every title, comment, alarm text and text list entry of each PLC, a column per project language.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="plc">Only this PLC.</param>
    [Command("texts")]
    public int Texts([Argument] string archive, string? plc = null) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
        var rows = TiaTexts.Rows(program, new TiaAlarms(program));
        return PerPlc(rows.Select(row => row.Plc), plc, name => TiaTexts.WriteTexts(rows, name, program.Languages, Console.Out));
    }

    /// <summary>Prints each PLC's program alarms: where each is declared, its instances, class, associated values and texts in every project language.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="plc">Only this PLC.</param>
    [Command("alarms")]
    public int Alarms([Argument] string archive, string? plc = null) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
        var alarms = new TiaAlarms(program).Alarms();
        return PerPlc(alarms.Select(alarm => alarm.Plc), plc, name => TiaTexts.WriteAlarms([.. alarms.Where(alarm => alarm.Plc == name)], program.Languages, Console.Out));
    }

    /// <summary>Prints each PLC's text lists: every entry in every project language, and the alarms that use each list.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="plc">Only this PLC.</param>
    [Command("textlists")]
    public int TextLists([Argument] string archive, string? plc = null) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
        var lists = new TiaAlarms(program).TextLists();
        return PerPlc(lists.Select(list => list.Plc), plc, name => TiaTexts.WriteTextLists([.. lists.Where(list => list.Plc == name)], program.Languages, Console.Out));
    }

    /// <summary>The program in a language the user named, or null after saying which languages there are.</summary>
    private static TiaProgram? Open(TiaDatabase database, string? language) {
        try {
            return new TiaProgram(database, language);
        } catch (TiaLanguageException e) {
            Console.Error.WriteLine(e.Message);
            return null;
        }
    }

    /// <summary>Writes a table per PLC, each after a line naming its PLC when there are several; 1 if there is nothing to write.</summary>
    private static int PerPlc(IEnumerable<string> plcs, string? only, Action<string> write) {
        var names = plcs.Where(name => name.Length != 0 && (only is null || name == only)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0) {
            Console.Error.WriteLine(only is null ? "Nothing to list." : $"Nothing to list for a PLC named {only}.");
            return 1;
        }
        foreach (var name in names) {
            if (names.Count > 1) {
                Console.Out.WriteLine("== " + name);
            }
            write(name);
        }
        return 0;
    }
}
