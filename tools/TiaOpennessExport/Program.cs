using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;

namespace TiaOpennessExport;

/// <summary>
/// Writes what TIA Portal itself says about a project, for comparing with tiadump: generated source and SimaticML per
/// block, plus the tag tables. It only reads: it never compiles or saves, and it leaves know-how protected blocks alone.
/// </summary>
internal static class Program {
    private const string DefaultPublicApi = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

    private static int Main(string[] args) {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPublicApi;
        try {
            return Run(args);
        } catch (Exception e) {
            Console.Error.WriteLine(e);
            return 2;
        }
    }

    private static Assembly ResolveFromPublicApi(object sender, ResolveEventArgs args) {
        var name = new AssemblyName(args.Name).Name;
        if (!name.StartsWith("Siemens.Engineering", StringComparison.Ordinal)) {
            return null;
        }
        var folder = Environment.GetEnvironmentVariable("TIA_PUBLIC_API") ?? DefaultPublicApi;
        var path = Path.Combine(folder, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static int Run(string[] args) {
        switch (args.FirstOrDefault()) {
            case "probe" when args.Length == 1:
                return Probe();
            case "attach" when args.Length == 2:
                return Attached(args[1]);
            case "archive" when args.Length == 4:
                return FromArchive(args[1], args[2], args[3]);
            case "compiled-copy" when args.Length == 3:
                return CompiledCopy(args[1], args[2]);
            case "demo" when args.Length == 4:
                return Demo(args[1], args[2], args[3]);
            default:
                Console.Error.WriteLine("""
                    usage:
                      TiaOpennessExport probe                            list running TIA Portal processes
                      TiaOpennessExport attach <out>                     export the project open in the one running TIA
                      TiaOpennessExport archive <.zap> <empty dir> <out> retrieve a copy, export it, close it unsaved
                      TiaOpennessExport compiled-copy <.ap21> <out>      compile a retrieved copy, export it, close it unsaved
                      TiaOpennessExport demo <sources> <empty dir> <out>    build the demo project from docs/demo and archive it

                    Keep <empty dir> short, such as C:\\Users\\me\\tc: TIA refuses to retrieve into a long path, and only says
                    "Unable to retrieve the project". An export refuses blocks that are not compiled; source generation does not.
                    """);
                return 1;
        }
    }

    private static int Probe() {
        foreach (var process in TiaPortal.GetProcesses()) {
            Console.WriteLine($"{process.Id}\t{process.Mode}\t{process.ProjectPath?.FullName}");
        }
        return 0;
    }

    private static int Attached(string output) {
        var process = TiaPortal.GetProcesses().Single(candidate => candidate.ProjectPath is not null);
        Console.WriteLine($"attaching to {process.Id}, {process.ProjectPath.FullName}");
        using var portal = process.Attach();
        return Export(portal.Projects.Single(), output);
    }

    private static int FromArchive(string archive, string workFolder, string output) {
        var work = new DirectoryInfo(workFolder);
        if (work.Exists && work.EnumerateFileSystemInfos().Any()) {
            Console.Error.WriteLine($"{workFolder} is not empty; the project is retrieved into a folder of its own.");
            return 1;
        }
        var clock = Stopwatch.StartNew();
        using var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        Console.WriteLine($"TIA Portal started in {clock.Elapsed.TotalSeconds:F0} s");
        var file = new FileInfo(archive);
        // V21 reads its own archives directly; an older one has to be upgraded, and only the retrieved copy changes.
        var project = file.Extension.Equals(".zap21", StringComparison.OrdinalIgnoreCase)
            ? portal.Projects.Retrieve(file, work)
            : portal.Projects.RetrieveWithUpgrade(file, work);
        Console.WriteLine($"retrieved {project.Path.FullName} after {clock.Elapsed.TotalSeconds:F0} s");
        try {
            return Export(project, output);
        } finally {
            project.Close();
        }
    }

    /// <summary>
    /// TIA exports SimaticML only for compiled blocks. This compiles in memory and closes unsaved, so even the copy keeps
    /// nothing, but compiling writes to the project folder, so only ever point it at a retrieved copy.
    /// </summary>
    private static int CompiledCopy(string project, string output) {
        var clock = Stopwatch.StartNew();
        using var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        var opened = portal.Projects.Open(new FileInfo(project));
        Console.WriteLine($"opened {opened.Path.FullName} after {clock.Elapsed.TotalSeconds:F0} s");
        try {
            Directory.CreateDirectory(output);
            using var log = new StreamWriter(Path.Combine(output, "compile.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
            foreach (var plc in Plcs(opened)) {
                // A project with a safety program refuses a compile of the whole PLC without its password. Nothing here
                // has that, or looks for it: each block that is not fail-safe is compiled on its own, and the others stay as they are.
                var counts = new Dictionary<string, int>();
                CompileTypes(plc.TypeGroup, log, counts);
                CompileBlocks(plc.BlockGroup, log, counts);
                Console.WriteLine($"compiled {plc.Name} after {clock.Elapsed.TotalSeconds:F0} s: " + string.Join(", ", counts.Select(count => $"{count.Value} {count.Key}")));
            }
            return Export(opened, output);
        } finally {
            opened.Close();
        }

        static void CompileTypes(PlcTypeGroup group, StreamWriter log, Dictionary<string, int> counts) {
            foreach (var type in group.Types) {
                if (!type.IsKnowHowProtected) {
                    Compile(type, type.Name, log, counts);
                }
            }
            foreach (var child in group.Groups) {
                CompileTypes(child, log, counts);
            }
        }

        static void CompileBlocks(PlcBlockGroup group, StreamWriter log, Dictionary<string, int> counts) {
            foreach (var block in group.Blocks) {
                if (block.IsKnowHowProtected || block.ProgrammingLanguage.ToString().StartsWith("F_", StringComparison.Ordinal)) {
                    Count(counts, "skipped");
                } else {
                    Compile(block, block.Name, log, counts);
                }
            }
            foreach (var child in group.Groups) {
                CompileBlocks(child, log, counts);
            }
        }

        static void Compile(IEngineeringServiceProvider item, string name, StreamWriter log, Dictionary<string, int> counts) {
            try {
                var compilable = item.GetService<ICompilable>();
                if (compilable is null) {
                    Count(counts, "not compilable");
                    return;
                }
                var result = compilable.Compile();
                Count(counts, result.State.ToString());
                log.WriteLine($"{result.State}\t{result.ErrorCount}\t{result.WarningCount}\t{name}");
            } catch (EngineeringException e) {
                Count(counts, "refused");
                log.WriteLine($"refused\t\t\t{name}\t{e.Message.Replace('\r', ' ').Replace('\n', ' ')}");
            }
        }

        static void Count(Dictionary<string, int> counts, string what) => counts[what] = counts.TryGetValue(what, out var count) ? count + 1 : 1;
    }

    private static int Export(Project project, string output) {
        Directory.CreateDirectory(output);
        using var manifest = new StreamWriter(Path.Combine(output, "manifest.tsv"), false, new UTF8Encoding(false)) { AutoFlush = true };
        manifest.WriteLine("plc\tobject\tnumber\tlanguage\tstatus\tsource\txml\tname\tnote");
        var exporter = new Exporter(output, manifest);
        foreach (var plc in Plcs(project)) {
            Console.WriteLine($"PLC {plc.Name}");
            exporter.ExportPlc(plc);
        }
        Console.WriteLine($"{exporter.Written} files written, {exporter.Closed} closed, {exporter.Failed} failed");
        return 0;
    }

    /// <summary>
    /// Builds a new project from invented sources, for pictures and tries that show nothing of anyone's plant: a PLC, its
    /// tag tables, then the types and blocks in the order order.txt gives, each in the folder its file is in. It saves the
    /// new project and writes it as an archive. It opens no other project.
    /// </summary>
    private static int Demo(string sources, string workFolder, string output) {
        var work = new DirectoryInfo(workFolder);
        if (work.Exists && work.EnumerateFileSystemInfos().Any()) {
            Console.Error.WriteLine($"{workFolder} is not empty; the project is created in a folder of its own.");
            return 1;
        }
        var clock = Stopwatch.StartNew();
        using var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        Console.WriteLine($"TIA Portal started in {clock.Elapsed.TotalSeconds:F0} s");
        var project = portal.Projects.Create(work, "FillingLine");
        try {
            var cpu = portal.HardwareCatalog.Find("6ES7 516-3AN02-0AB0").OrderBy(entry => entry.TypeIdentifier, StringComparer.Ordinal).LastOrDefault()
                ?? throw new InvalidOperationException("No CPU 1516-3 PN/DP in the hardware catalog.");
            Console.WriteLine($"CPU {cpu.TypeIdentifier}");
            var device = project.Devices.CreateWithItem(cpu.TypeIdentifier, "PLC_Line", "Line");
            var plc = device.DeviceItems.SelectMany(Software).Single();

            foreach (var file in Directory.GetFiles(Path.Combine(sources, "PLC tags"), "*.tsv").OrderBy(file => file, StringComparer.Ordinal)) {
                var table = plc.TagTableGroup.TagTables.Create(Path.GetFileNameWithoutExtension(file));
                foreach (var line in File.ReadAllLines(file).Where(line => line.Length != 0)) {
                    var fields = line.Split('\t');
                    var tag = table.Tags.Create(fields[0], fields[1], fields[2]);
                    tag.Comment.Items.First().Text = fields[3];
                }
            }

            // A new PLC comes with an empty Main; the demo has its own.
            plc.BlockGroup.Blocks.Find("Main")?.Delete();
            var failed = 0;
            foreach (var entry in File.ReadAllLines(Path.Combine(sources, "order.txt")).Where(line => line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal))) {
                var parts = entry.Split('/');
                var folders = parts.Skip(1).Take(parts.Length - 2).ToList();
                var source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(parts.Last(), Path.GetFullPath(Path.Combine(sources, entry)));
                // One run reports every source TIA refuses, not only the first.
                try {
                    Generate(plc, source, parts[0], folders);
                    Console.WriteLine($"generated {entry}");
                } catch (EngineeringTargetInvocationException e) {
                    failed++;
                    Console.WriteLine($"failed {entry}: {e.Message.Replace(Environment.NewLine, " ")}");
                }
                source.Delete();
            }
            if (failed != 0) {
                Console.Error.WriteLine($"{failed} sources failed; nothing archived.");
                return 1;
            }

            project.Save();
            Directory.CreateDirectory(output);
            project.Archive(new DirectoryInfo(output), "FillingLine.zap21", ProjectArchivationMode.DiscardRestorableDataAndCompressed);
            Console.WriteLine($"archived to {output} after {clock.Elapsed.TotalSeconds:F0} s");
            return 0;
        } finally {
            project.Close();
        }
    }

    private static void Generate(PlcSoftware plc, PlcExternalSource source, string root, List<string> folders) {
        if (root == "PLC data types") {
            if (folders.Count == 0) {
                source.GenerateBlocksFromSource(GenerateBlockOption.None);
            } else {
                PlcTypeUserGroup group = null;
                foreach (var name in folders) {
                    var groups = group is null ? plc.TypeGroup.Groups : group.Groups;
                    group = groups.Find(name) ?? groups.Create(name);
                }
                source.GenerateBlocksFromSource(group, GenerateBlockOption.None);
            }
        } else if (folders.Count == 0) {
            source.GenerateBlocksFromSource(GenerateBlockOption.None);
        } else {
            PlcBlockUserGroup group = null;
            foreach (var name in folders) {
                var groups = group is null ? plc.BlockGroup.Groups : group.Groups;
                group = groups.Find(name) ?? groups.Create(name);
            }
            source.GenerateBlocksFromSource(group, GenerateBlockOption.None);
        }
    }


    private static IEnumerable<PlcSoftware> Plcs(Project project) {
        var devices = new List<Device>(project.Devices);
        devices.AddRange(project.UngroupedDevicesGroup.Devices);
        AddGroups(project.DeviceGroups, devices);
        foreach (var device in devices) {
            foreach (var item in device.DeviceItems) {
                foreach (var plc in Software(item)) {
                    yield return plc;
                }
            }
        }

        static void AddGroups(DeviceUserGroupComposition groups, List<Device> into) {
            foreach (var group in groups) {
                into.AddRange(group.Devices);
                AddGroups(group.Groups, into);
            }
        }

    }

    private static IEnumerable<PlcSoftware> Software(DeviceItem item) {
        if (item.GetService<SoftwareContainer>()?.Software is PlcSoftware plc) {
            yield return plc;
        }
        foreach (var child in item.DeviceItems) {
            foreach (var nested in Software(child)) {
                yield return nested;
            }
        }
    }
}

internal sealed class Exporter(string root, StreamWriter manifest) {
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    public int Written { get; private set; }
    public int Closed { get; private set; }
    public int Failed { get; private set; }

    public void ExportPlc(PlcSoftware plc) {
        var folder = Path.Combine(root, Safe(plc.Name));
        Directory.CreateDirectory(folder);
        Blocks(plc, folder, plc.BlockGroup);
        Types(plc, folder, plc.TypeGroup);
        TagTables(plc, folder, plc.TagTableGroup);
    }

    private void Blocks(PlcSoftware plc, string folder, PlcBlockGroup group) {
        foreach (var block in group.Blocks) {
            Block(plc, folder, block);
        }
        foreach (var child in group.Groups) {
            Blocks(plc, folder, child);
        }
        if (group is PlcBlockSystemGroup system) {
            foreach (var child in system.SystemBlockGroups) {
                SystemBlocks(plc, folder, child);
            }
        }
    }

    private void SystemBlocks(PlcSoftware plc, string folder, PlcSystemBlockGroup group) {
        foreach (var block in group.Blocks) {
            Block(plc, folder, block);
        }
        foreach (var child in group.Groups) {
            SystemBlocks(plc, folder, child);
        }
    }

    private void Block(PlcSoftware plc, string folder, PlcBlock block) {
        var kind = block.GetType().Name;
        var language = block.ProgrammingLanguage.ToString();
        if (block.IsKnowHowProtected) {
            Row(plc, kind, block.Number.ToString(), language, "closed", "", "", block.Name, "");
            Closed++;
            return;
        }
        var extension = block is DataBlock ? ".db" : language == "SCL" ? ".scl" : language == "STL" ? ".awl" : null;
        var stem = Path.Combine(folder, Unique(kind + "." + Safe(block.Name)));
        var notes = new List<string>();
        var source = extension is null ? "" : Attempt(notes, "source", () => Generate(plc, block, stem + extension));
        var xml = Attempt(notes, "xml", () => block.Export(new FileInfo(stem + ".xml"), ExportOptions.WithDefaults));
        Row(plc, kind, block.Number.ToString(), language, notes.Count == 0 ? "ok" : "partial", Relative(stem, extension, source), Relative(stem, ".xml", xml), block.Name, string.Join(" | ", notes));
    }

    private void Types(PlcSoftware plc, string folder, PlcTypeGroup group) {
        foreach (var type in group.Types) {
            Type(plc, folder, type);
        }
        foreach (var child in group.Groups) {
            Types(plc, folder, child);
        }
        if (group is PlcTypeSystemGroup system) {
            foreach (var child in system.SystemTypeGroups) {
                foreach (var type in child.Types) {
                    Type(plc, folder, type);
                }
            }
        }
    }

    private void Type(PlcSoftware plc, string folder, PlcType type) {
        if (type.IsKnowHowProtected) {
            Row(plc, "UDT", "", "UDT", "closed", "", "", type.Name, "");
            Closed++;
            return;
        }
        var stem = Path.Combine(folder, Unique("UDT." + Safe(type.Name)));
        var notes = new List<string>();
        var source = Attempt(notes, "source", () => Generate(plc, type, stem + ".udt"));
        var xml = Attempt(notes, "xml", () => type.Export(new FileInfo(stem + ".xml"), ExportOptions.WithDefaults));
        Row(plc, "UDT", "", "UDT", notes.Count == 0 ? "ok" : "partial", Relative(stem, ".udt", source), Relative(stem, ".xml", xml), type.Name, string.Join(" | ", notes));
    }

    private void TagTables(PlcSoftware plc, string folder, PlcTagTableGroup group) {
        foreach (var table in group.TagTables) {
            var stem = Path.Combine(folder, Unique("TAGS." + Safe(table.Name)));
            var notes = new List<string>();
            var xml = Attempt(notes, "xml", () => table.Export(new FileInfo(stem + ".xml"), ExportOptions.WithDefaults));
            Row(plc, "TAGS", "", "", notes.Count == 0 ? "ok" : "partial", "", Relative(stem, ".xml", xml), table.Name, string.Join(" | ", notes));
        }
        foreach (var child in group.Groups) {
            TagTables(plc, folder, child);
        }
    }

    private static void Generate(PlcSoftware plc, IGenerateSource block, string path) =>
        plc.ExternalSourceGroup.GenerateSource([block], new FileInfo(path), GenerateOptions.None);

    private string Attempt(List<string> notes, string what, Action action) {
        try {
            action();
            Written++;
            return "yes";
        } catch (Exception e) when (e is EngineeringException or IOException or UnauthorizedAccessException) {
            Failed++;
            notes.Add(what + ": " + e.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
            return "";
        }
    }

    private string Relative(string stem, string extension, string written) =>
        written.Length == 0 ? "" : (stem + extension).Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');

    private void Row(PlcSoftware plc, string kind, string number, string language, string status, string source, string xml, string name, string note) {
        manifest.WriteLine(string.Join("\t", plc.Name, kind, number, language, status, source, xml, name, note));
        if (status != "ok" && status != "closed") {
            Console.WriteLine($"{status}: {kind} {name}: {note}");
        }
    }

    private string Unique(string stem) {
        var candidate = stem;
        for (var i = 2; !_taken.Add(candidate); i++) {
            candidate = stem + "." + i;
        }
        return candidate;
    }

    private static string Safe(string name) {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name) {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }
        return builder.ToString().TrimEnd('.');
    }
}
