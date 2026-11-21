using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
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
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.CrossReference;

namespace TiaOpennessExport;

/// <summary>
/// Writes what TIA Portal itself says about a project, for comparing with tiadump: generated source and SimaticML per
/// block, plus the tag tables. On a project it did not create it only reads, and it never saves one; it compiles only a
/// retrieved copy (compiled-copy) and the reference projects it builds itself. It leaves know-how protected blocks alone.
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
            case "reference" when args.Length >= 3:
                return References(args[1], args.Skip(2).ToList());
            case "catalog" when args.Length >= 2:
                return Catalog(args.Skip(1));
            default:
                Console.Error.WriteLine("""
                    usage:
                      TiaOpennessExport probe                            list running TIA Portal processes
                      TiaOpennessExport attach <out>                     export the project open in the one running TIA
                      TiaOpennessExport archive <.zap> <empty dir> <out> retrieve a copy, export it, close it unsaved
                      TiaOpennessExport compiled-copy <.ap21> <out>      compile a retrieved copy, export it, close it unsaved
                      TiaOpennessExport reference <empty dir> <folder>... build reference projects again from their sources
                      TiaOpennessExport catalog <text>...                list hardware catalog entries that match, for hardware.tsv

                    Keep <empty dir> short, such as C:\\Users\\me\\tc: TIA refuses to retrieve into a long path, and only says
                    "Unable to retrieve the project". An export refuses blocks that are not compiled; source generation does not.
                    """);
                return 1;
        }
    }

    private static int Catalog(IEnumerable<string> texts) {
        using var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        foreach (var text in texts) {
            foreach (var entry in portal.HardwareCatalog.Find(text)) {
                Console.WriteLine(string.Join("\t", text, entry.TypeIdentifier, entry.TypeName, (entry.Description ?? "").Replace('\n', ' ').Replace('\r', ' ')));
            }
        }
        return 0;
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

    private static int Export(Project project, string output, bool blockXml = true, bool crossReferences = false) {
        Directory.CreateDirectory(output);
        using var manifest = new StreamWriter(Path.Combine(output, "manifest.tsv"), false, new UTF8Encoding(false)) { AutoFlush = true };
        manifest.WriteLine("plc\tobject\tnumber\tlanguage\tstatus\tsource\txml\tname\tnote");
        var exporter = new Exporter(output, manifest, blockXml);
        foreach (var plc in Plcs(project)) {
            Console.WriteLine($"PLC {plc.Name}");
            exporter.ExportPlc(plc);
            if (crossReferences) {
                exporter.CrossReferences(plc);
            }
        }
        Console.WriteLine($"{exporter.Written} files written, {exporter.Closed} closed, {exporter.Failed} failed");
        return 0;
    }

    private static string Cpu(TiaPortal portal) =>
        (portal.HardwareCatalog.Find("6ES7 516-3AN02-0AB0").OrderBy(entry => entry.TypeIdentifier, StringComparer.Ordinal).LastOrDefault()
            ?? throw new InvalidOperationException("No CPU 1516-3 PN/DP in the hardware catalog.")).TypeIdentifier;

    /// <summary>Compiles the PLC's software and prints every error with where it is; returns how many there are.</summary>
    private static int Compile(PlcSoftware plc) {
        var result = plc.GetService<ICompilable>().Compile();
        Print(result.Messages, "");
        Console.WriteLine($"compiled {plc.Name}: {result.State}, {result.ErrorCount} errors, {result.WarningCount} warnings");
        return result.ErrorCount;

        void Print(CompilerResultMessageComposition messages, string path) {
            foreach (var message in messages) {
                var where = string.IsNullOrEmpty(message.Path) ? path : path.Length == 0 ? message.Path : path + "/" + message.Path;
                if (message.State == CompilerResultState.Error && message.Messages.Count == 0) {
                    Console.WriteLine($"compile error {plc.Name}/{where}: {message.Description}");
                }
                Print(message.Messages, where);
            }
        }
    }

    /// <summary>The password reference projects protect blocks with. Invented, it guards nothing: the reader never uses it.</summary>
    private const string ReferencePassword = "Reference-0008";

    /// <summary>
    /// Builds reference projects again (spec 0008), all in one TIA session: for each folder a new project from the invented
    /// sources in its sources folder, one PLC per subfolder, then its archive and TIA's generated source. A folder's committed
    /// files are replaced only when all of that worked. It opens no project but the ones it creates, and deletes them after.
    /// </summary>
    private static int References(string workFolder, List<string> folders) {
        var work = new DirectoryInfo(workFolder);
        if (work.Exists && work.EnumerateFileSystemInfos().Any()) {
            Console.Error.WriteLine($"{workFolder} is not empty; each project is created in a folder of its own there.");
            return 1;
        }
        var clock = Stopwatch.StartNew();
        var failed = 0;
        using (var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface)) {
            Console.WriteLine($"TIA Portal started in {clock.Elapsed.TotalSeconds:F0} s");
            var cpu = Cpu(portal);
            foreach (var folder in folders.Select(Path.GetFullPath)) {
                failed += Reference(portal, cpu, folder, Path.Combine(work.FullName, Path.GetFileName(folder))) ? 0 : 1;
                Console.WriteLine($"after {clock.Elapsed.TotalSeconds:F0} s");
            }
        }
        try {
            work.Delete(recursive: true);
        } catch (IOException e) {
            Console.Error.WriteLine($"could not delete {work.FullName}: {e.Message}");
        }
        return failed == 0 ? 0 : 1;
    }

    private static bool Reference(TiaPortal portal, string cpu, string folder, string work) {
        var name = Path.GetFileName(folder);
        Console.WriteLine($"== {name}");
        var project = portal.Projects.Create(new DirectoryInfo(work), name);
        try {
            var refused = 0;
            // With a hardware description the devices come from it, and each PLC folder names a CPU it created (spec 0010);
            // without one, each PLC folder gets a CPU of its own.
            var hardware = Path.Combine(folder, "sources", "hardware.tsv");
            if (File.Exists(hardware)) {
                refused += HardwareBuilder.Build(portal, project, hardware);
            }
            foreach (var plcFolder in Directory.GetDirectories(Path.Combine(folder, "sources")).OrderBy(path => path, StringComparer.Ordinal)) {
                var plcName = Path.GetFileName(plcFolder);
                if (!File.Exists(hardware)) {
                    project.Devices.CreateWithItem(cpu, plcName, plcName);
                }
                var plc = Plcs(project).FirstOrDefault(candidate => candidate.Name == plcName);
                if (plc is null) {
                    refused++;
                    Console.WriteLine($"refused {plcName}: hardware.tsv creates no PLC of that name");
                    continue;
                }
                refused += BuildPlc(plc, plcFolder);
            }
            if (refused != 0) {
                Console.Error.WriteLine($"{name}: {refused} sources refused; its files stay as they were.");
                return false;
            }
            project.Save();
            var archived = Path.Combine(work, "out");
            project.Archive(new DirectoryInfo(archived), name + ".zap21", ProjectArchivationMode.DiscardRestorableDataAndCompressed);
            var generated = Path.Combine(work, "generated");
            Export(project, generated, blockXml: false, crossReferences: true);
            HardwareReport.Write(project, Path.Combine(generated, "hardware.tsv"));

            File.Copy(Path.Combine(archived, name + ".zap21"), Path.Combine(folder, name + ".zap21"), overwrite: true);
            var target = Path.Combine(folder, "generated");
            if (Directory.Exists(target)) {
                Directory.Delete(target, recursive: true);
            }
            Directory.Move(generated, target);
            Console.WriteLine($"{name}: archive and generated source replaced");
            StaleExperiment(project, name, work);
            return true;
        } finally {
            project.Close();
        }
    }

    /// <summary>
    /// Whether TIA's cross-reference list follows a change made without a compile (spec 0015). Runs only when
    /// TIA_STALE_EXPERIMENT is set to "&lt;project name&gt;|&lt;source file&gt;|&lt;folder&gt;|&lt;block folder&gt;": after the reference
    /// files are written, the source replaces its block in the first PLC, the project is saved and archived as
    /// &lt;name&gt;.stale.zap21, then compiled and archived as &lt;name&gt;.recompiled.zap21, both into the folder, which must lie
    /// outside the repository. The committed files are written before and never touched.
    /// </summary>
    private static void StaleExperiment(Project project, string name, string work) {
        var setting = Environment.GetEnvironmentVariable("TIA_STALE_EXPERIMENT")?.Split('|');
        if (setting is not { Length: 4 } || setting[0] != name) {
            return;
        }
        var plc = Plcs(project).First();
        var source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(Path.GetFileName(setting[1]), setting[1]);
        source.GenerateBlocksFromSource((PlcBlockUserGroup)BlockGroup(plc, setting[3].Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList()), GenerateBlockOption.None);
        source.Delete();
        Directory.CreateDirectory(setting[2]);
        foreach (var state in new[] { "stale", "recompiled" }) {
            if (state == "recompiled") {
                Compile(plc);
            }
            project.Save();
            var folder = Path.Combine(work, state);
            project.Archive(new DirectoryInfo(folder), name + ".zap21", ProjectArchivationMode.DiscardRestorableDataAndCompressed);
            File.Copy(Path.Combine(folder, name + ".zap21"), Path.Combine(setting[2], name + "." + state + ".zap21"), overwrite: true);
            Console.WriteLine($"{name}: {state} archive written to {setting[2]}");
        }
    }

    /// <summary>
    /// Watch tables and the force table from "Watch and force tables", as SimaticML files in TIA's export form, each into
    /// the table group its folder names. A force table replaces the PLC's own, which every PLC has and none can delete.
    /// Before the first, an empty table with one comment line is exported and printed, to show the form TIA writes.
    /// </summary>
    private static int ImportTables(PlcSoftware plc, string sources) {
        var tables = Path.Combine(sources, "Watch and force tables");
        if (!Directory.Exists(tables)) {
            return 0;
        }
        var probe = plc.WatchAndForceTableGroup.WatchTables.Create("Probe");
        probe.Entries.Create();
        var probeFile = Path.Combine(Path.GetTempPath(), "TiaOpennessExport-watch-probe.xml");
        File.Delete(probeFile);
        probe.Export(new FileInfo(probeFile), ExportOptions.WithDefaults);
        Console.WriteLine(File.ReadAllText(probeFile));
        File.Delete(probeFile);
        probe.Delete();

        var refused = 0;
        foreach (var file in Directory.GetFiles(tables, "*.xml", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal)) {
            var relative = file.Substring(tables.Length).TrimStart('\\', '/').Replace('\\', '/');
            PlcWatchAndForceTableGroup group = plc.WatchAndForceTableGroup;
            foreach (var folder in relative.Split('/').Reverse().Skip(1).Reverse()) {
                group = group.Groups.Find(folder) ?? (PlcWatchAndForceTableGroup)group.Groups.Create(folder);
            }
            try {
                if (File.ReadAllText(file).Contains("<SW.WatchAndForceTables.PlcForceTable ")) {
                    group.ForceTables.Import(new FileInfo(file), ImportOptions.Override);
                } else {
                    group.WatchTables.Import(new FileInfo(file), ImportOptions.None);
                }
                Console.WriteLine($"imported {plc.Name}/{relative}");
            } catch (EngineeringException e) {
                refused++;
                Console.WriteLine($"refused {plc.Name}/{relative}: {e.Message.Replace(Environment.NewLine, " ")}");
            }
        }
        return refused;
    }

    /// <summary>
    /// Tag tables, then the types and blocks in the order order.txt gives, each in the folder its file is in: a text source
    /// generated, a block in SimaticML (.xml) imported. Then the PLC is compiled, the watch and force tables imported,
    /// and protect.txt's blocks are protected.
    /// </summary>
    private static int BuildPlc(PlcSoftware plc, string sources) {
        var tags = Path.Combine(sources, "PLC tags");
        foreach (var file in Directory.Exists(tags) ? Directory.GetFiles(tags, "*.tsv").OrderBy(file => file, StringComparer.Ordinal) : Enumerable.Empty<string>()) {
            var table = plc.TagTableGroup.TagTables.Create(Path.GetFileNameWithoutExtension(file));
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8).Where(line => line.Length != 0)) {
                var fields = line.Split('\t');
                var tag = table.Tags.Create(fields[0], fields[1], fields[2]);
                if (fields.Length > 3) {
                    tag.Comment.Items.First().Text = fields[3];
                }
            }
        }

        // A new PLC comes with an empty Main; a program that wants one brings its own.
        plc.BlockGroup.Blocks.Find("Main")?.Delete();
        var refused = 0;
        foreach (var entry in File.ReadAllLines(Path.Combine(sources, "order.txt")).Where(line => line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal))) {
            var parts = entry.Split('/');
            var folders = parts.Skip(1).Take(parts.Length - 2).ToList();
            var path = Path.GetFullPath(Path.Combine(sources, entry));
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
                try {
                    BlockGroup(plc, folders).Blocks.Import(new FileInfo(path), ImportOptions.None);
                    Console.WriteLine($"imported {plc.Name}/{entry}");
                } catch (EngineeringException e) {
                    refused++;
                    Console.WriteLine($"refused {plc.Name}/{entry}: {e.Message.Replace(Environment.NewLine, " ")}");
                }
                continue;
            }
            var source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(parts.Last(), path);
            // One run reports every source TIA refuses, not only the first.
            try {
                Generate(plc, source, parts[0], folders);
                Console.WriteLine($"generated {plc.Name}/{entry}");
            } catch (EngineeringTargetInvocationException e) {
                refused++;
                Console.WriteLine($"refused {plc.Name}/{entry}: {e.Message.Replace(Environment.NewLine, " ")}");
            }
            source.Delete();
        }
        if (refused == 0) {
            // TIA exports SimaticML only for a compiled block; a compile error counts as a refused source.
            refused += Compile(plc);
        }
        refused += ImportTables(plc, sources);

        var protect = Path.Combine(sources, "protect.txt");
        foreach (var blockName in File.Exists(protect) ? File.ReadAllLines(protect).Where(line => line.Length != 0) : Enumerable.Empty<string>()) {
            var block = FindBlock(plc.BlockGroup, blockName) ?? throw new InvalidOperationException($"protect.txt names {blockName}, which {plc.Name} does not have.");
            using var password = new SecureString();
            foreach (var c in ReferencePassword) {
                password.AppendChar(c);
            }
            block.GetService<PlcBlockProtectionProvider>().Protect(password);
            Console.WriteLine($"protected {plc.Name}/{blockName}");
        }
        return refused;
    }

    private static PlcBlock FindBlock(PlcBlockGroup group, string name) =>
        group.Blocks.Find(name) ?? group.Groups.Select(child => FindBlock(child, name)).FirstOrDefault(block => block is not null);

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
            source.GenerateBlocksFromSource((PlcBlockUserGroup)BlockGroup(plc, folders), GenerateBlockOption.None);
        }
    }

    private static PlcBlockGroup BlockGroup(PlcSoftware plc, List<string> folders) {
        PlcBlockGroup group = plc.BlockGroup;
        foreach (var name in folders) {
            group = group.Groups.Find(name) ?? group.Groups.Create(name);
        }
        return group;
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

internal sealed class Exporter(string root, StreamWriter manifest, bool blockXml) {
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
        WatchAndForceTables(plc, folder, plc.WatchAndForceTableGroup);
    }

    private void WatchAndForceTables(PlcSoftware plc, string folder, PlcWatchAndForceTableGroup group) {
        foreach (var table in group.WatchTables) {
            Table(plc, folder, "WATCH", table.Name, file => table.Export(file, ExportOptions.WithDefaults));
        }
        foreach (var table in group.ForceTables) {
            Table(plc, folder, "FORCE", table.Name, file => table.Export(file, ExportOptions.WithDefaults));
        }
        foreach (var child in group.Groups) {
            WatchAndForceTables(plc, folder, child);
        }
    }

    private void Table(PlcSoftware plc, string folder, string kind, string name, Action<FileInfo> export) {
        var stem = Path.Combine(folder, Unique(kind + "." + Safe(name)));
        var notes = new List<string>();
        var xml = Attempt(notes, "xml", () => export(new FileInfo(stem + ".xml")));
        Row(plc, kind, "", "", notes.Count == 0 ? "ok" : "partial", "", Relative(stem, ".xml", xml), name, string.Join(" | ", notes));
    }

    /// <summary>
    /// TIA's cross-reference list of every block, type and tag table of the PLC, as
    /// CrossReferenceService gives it, one row per location, into xref.tsv beside the PLC's other files (spec 0015).
    /// </summary>
    public void CrossReferences(PlcSoftware plc) {
        var path = Path.Combine(root, Safe(plc.Name), "xref.tsv");
        using var output = new StreamWriter(path, false, new UTF8Encoding(false));
        output.WriteLine("object\tsource\tsource_type\tsource_address\tsource_path\treference\treference_type\treference_address\treference_path\tdevice\trelation\taccess\tlocation\treferenced_as\tlocation_name\tlocation_type\tlocation_address");
        var objects = new List<(string Name, IEngineeringServiceProvider Item)>();
        AddBlocks(plc.BlockGroup, objects);
        AddTypes(plc.TypeGroup, objects);
        AddTagTables(plc.TagTableGroup, objects);
        // Watch and force tables offer no cross-reference service; they appear as users of the tags they name.
        var rows = 0;
        foreach (var (name, item) in objects) {
            try {
                var service = item.GetService<CrossReferenceService>();
                if (service is null) {
                    output.WriteLine(Line(name, "[no cross-reference service]"));
                    continue;
                }
                foreach (var source in service.GetCrossReferences(CrossReferenceFilter.AllObjects).Sources) {
                    rows += Source(output, name, source);
                }
            } catch (EngineeringException e) {
                Failed++;
                output.WriteLine(Line(name, "[failed: " + e.Message + "]"));
            }
        }
        Written++;
        Console.WriteLine($"{plc.Name}: {rows} cross-reference locations of {objects.Count} objects");

        static int Source(StreamWriter output, string name, SourceObject source) {
            var rows = 0;
            foreach (var reference in source.References) {
                foreach (var location in reference.Locations) {
                    output.WriteLine(Line(name, source.Name, source.TypeName, source.Address, source.Path, reference.Name, reference.TypeName, reference.Address, reference.Path, reference.Device,
                        location.ReferenceType.ToString(), location.Access.ToString(), location.ReferenceLocation, location.ReferencedAsName, location.Name, location.TypeName, location.Address));
                    rows++;
                }
                if (reference.Locations.Count == 0) {
                    output.WriteLine(Line(name, source.Name, source.TypeName, source.Address, source.Path, reference.Name, reference.TypeName, reference.Address, reference.Path, reference.Device));
                    rows++;
                }
            }
            foreach (var child in source.Children) {
                rows += Source(output, name, child);
            }
            return rows;
        }

        static string Line(params string[] fields) =>
            string.Join("\t", fields.Select(field => (field ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')));

        static void AddBlocks(PlcBlockGroup group, List<(string, IEngineeringServiceProvider)> into) {
            foreach (var block in group.Blocks) {
                into.Add(("block:" + block.Name, block));
            }
            foreach (var child in group.Groups) {
                AddBlocks(child, into);
            }
        }

        static void AddTypes(PlcTypeGroup group, List<(string, IEngineeringServiceProvider)> into) {
            foreach (var type in group.Types) {
                into.Add(("type:" + type.Name, type));
            }
            foreach (var child in group.Groups) {
                AddTypes(child, into);
            }
        }

        static void AddTagTables(PlcTagTableGroup group, List<(string, IEngineeringServiceProvider)> into) {
            foreach (var table in group.TagTables) {
                into.Add(("tags:" + table.Name, table));
                // A tag table offers no cross-reference service; each tag may, with its uses in watch and force tables.
                foreach (var tag in table.Tags) {
                    into.Add(("tag:" + tag.Name, tag));
                }
            }
            foreach (var child in group.Groups) {
                AddTagTables(child, into);
            }
        }
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
        // A block TIA generates no source for is compared through its SimaticML, so that is always exported.
        var xml = blockXml || extension is null ? Attempt(notes, "xml", () => block.Export(new FileInfo(stem + ".xml"), ExportOptions.WithDefaults)) : "";
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
        var xml = blockXml ? Attempt(notes, "xml", () => type.Export(new FileInfo(stem + ".xml"), ExportOptions.WithDefaults)) : "";
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
