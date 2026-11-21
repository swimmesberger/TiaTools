using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TiaFormat.Program;

/// <summary>What an export wrote. A block that failed is named in <see cref="Failures"/>; the rest were still written.</summary>
public sealed record TiaExportResult(int BlockFiles, int TagFiles, int Protected, int Stubs, int Empty, long Bytes, int UnknownTags, IReadOnlyList<string> Failures) {
    /// <summary>The files written for HMI devices (spec 0011).</summary>
    public int HmiFiles { get; init; }
}

/// <summary>Writes a program as readable files in a tree, environment / PLC / folder path / block, with an index.tsv of every block. Used by the command line and the viewer alike, so both write the same files.</summary>
public sealed class TiaSourceExport(TiaProgram program, string root) {
    private static readonly SearchValues<char> Forbidden = SearchValues.Create("<>:\"/\\|?*\0");

    private readonly ConcurrentQueue<string> _failures = new();
    private readonly TiaDumpPaths _paths = new();
    private long _bytes;
    private int _unknown;

    /// <summary>Which blocks to write, all if null.</summary>
    public Func<TiaBlock, bool>? Blocks { get; init; }

    /// <summary>Which tag tables to write, all if null.</summary>
    public Func<TiaTagTable, bool>? TagTables { get; init; }

    /// <summary>Also write each HMI device's connections, tag tables, alarms and screens, in a folder of the device beside the PLCs.</summary>
    public bool HmiDevices { get; init; } = true;

    /// <summary>Also write each network as stored, next to its block. Never an encrypted part.</summary>
    public bool Raw { get; init; }

    public TiaExportResult Run() {
        Directory.CreateDirectory(root);
        var work = new List<(TiaBlock Block, string Path)>();
        var protectedCount = 0;
        var stubs = 0;
        var empty = 0;
        var index = new StringBuilder("object\tenvironment\tplc\tkind\tnumber\tlanguage\tknow-how\tfile\tname\n");
        foreach (var block in program.Blocks) {
            if (Blocks is { } include && !include(block)) {
                continue;
            }
            if (block.IsStub) {
                stubs++;
                continue;
            }
            protectedCount += block.IsKnowHowProtected ? 1 : 0;
            // A Siemens system block is usually protected with nothing stored: one line in the index says all there is.
            string? path = null;
            if (block.IsKnowHowProtected && program.ReadInterface(block).Count == 0) {
                empty++;
            } else {
                path = BlockPath(block);
                work.Add((block, path));
            }
            var environment = program.Environment(block.Frame);
            index.Append(CultureInfo.InvariantCulture,
                $"{block.Frame.ObjectId}\t{environment.Kind}\t{program.PlcName(block.Frame)}\t{block.Kind}\t{block.Number}\t{block.Language}\t{(block.IsKnowHowProtected ? "yes" : "no")}\t{(path is null ? "" : Path.GetRelativePath(root, path).Replace('\\', '/'))}\t{block.Name}\n");
        }

        Parallel.ForEach(
            work,
            () => new PooledByteWriter(),
            (item, _, output) => {
                WriteBlock(item.Block, item.Path, output);
                return output;
            },
            output => output.Dispose());

        var tagFiles = WriteTagTables();
        var hmiFiles = HmiDevices ? WriteHmiDevices() : 0;
        File.WriteAllText(Path.Combine(root, "index.tsv"), index.ToString(), new UTF8Encoding(false));
        return new TiaExportResult(work.Count, tagFiles, protectedCount, stubs, empty, Interlocked.Read(ref _bytes), _unknown, [.. _failures]) { HmiFiles = hmiFiles };
    }

    private void WriteBlock(TiaBlock block, string path, PooledByteWriter output) {
        try {
            output.Clear();
            Interlocked.Add(ref _unknown, TiaSourceWriter.Write(program, block, output));
            WriteFile(path, output.WrittenSpan);
            if (Raw && !block.IsKnowHowProtected) {
                WriteRaw(block, path, output);
            }
        } catch (Exception e) when (e is TiaFormatException or ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or IOException) {
            // One block the reader cannot follow must not cost the rest of the dump; the exit code still says so.
            _failures.Enqueue($"{block.Kind} {block.Name} (object {block.Frame.ObjectId}): {e.Message}");
        }
    }

    private void WriteRaw(TiaBlock block, string path, PooledByteWriter output) {
        var networks = program.Networks(block);
        for (var i = 0; i < networks.Count; i++) {
            output.Clear();
            program.WriteRawNetwork(block, i, output);
            if (output.Length != 0) {
                WriteFile(string.Create(CultureInfo.InvariantCulture, $"{path}.{networks[i].Number}.xml"), output.WrittenSpan);
            }
        }
    }

    private int WriteTagTables() {
        var count = 0;
        using var output = new PooledByteWriter();
        foreach (var table in program.ReadTagTables()) {
            if (table.Tags.Count == 0 || (TagTables is { } include && !include(table))) {
                continue;
            }
            output.Clear();
            TiaSourceWriter.WriteTagTable(table, output);
            var folder = ProgramFolder(table.Frame);
            WriteFile(_paths.Reserve(Path.Combine(folder, Safe(table.Name) + ".tags"), table.Frame.ObjectId), output.WrittenSpan);
            count++;
        }
        return count;
    }

    /// <summary>Each HMI device of the project in <c>Project/&lt;HMI device&gt;/</c>, its files as <see cref="TiaHmiWriter.DumpFiles"/> names them.</summary>
    private int WriteHmiDevices() {
        var count = 0;
        TiaHmiReport report;
        try {
            report = TiaHmi.Read(program);
        } catch (Exception e) when (e is TiaFormatException or ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException) {
            _failures.Enqueue($"HMI devices: {e.Message}");
            return 0;
        }
        foreach (var device in report.Devices) {
            var folder = Path.Combine(root, "Project", Safe(device.Name));
            foreach (var (path, text) in TiaHmiWriter.DumpFiles(device)) {
                WriteFile(_paths.Reserve(Path.Combine(folder, path), 0), Encoding.UTF8.GetBytes(text));
                count++;
            }
        }
        return count;
    }

    private string BlockPath(TiaBlock block) {
        var extension = block.Kind switch {
            "UDT" => ".udt",
            _ when block.Kind.StartsWith("DB", StringComparison.Ordinal) => ".db",
            _ when block.Language is "SCL" => ".scl",
            // STL is written as TIA generates its source, which TIA names .awl; the others are named as TIA names their languages.
            _ when block.Language is "STL" => ".awl",
            _ when block.Language is "LAD" or "LAD_CLASSIC" => ".lad",
            _ when block.Language is "FBD" or "FBD_CLASSIC" => ".fbd",
            _ when block.Language is null => ".txt",
            _ when block.Language.StartsWith("GRAPH", StringComparison.Ordinal) => ".graph",
            _ => "." + Safe(block.Language.ToLowerInvariant()),
        };
        return _paths.Reserve(Path.Combine(ProgramFolder(block.Frame), Safe(block.Name) + extension), block.Frame.ObjectId);
    }

    private string ProgramFolder(in Storage.TiaFrame frame) {
        var environment = program.Environment(frame);
        var holder = environment.Kind == "ProjectData" ? "Project" : Safe($"{environment.Kind}-{environment.Name}-{environment.Id}");
        var folder = program.FolderPath(frame);
        var path = Path.Combine(root, holder, Safe(program.PlcName(frame) ?? "NoPlc"));
        if (folder.Length == 0) {
            return Path.Combine(path, "NoFolder");
        }
        foreach (var part in folder.Split('/')) {
            path = Path.Combine(path, Safe(part));
        }
        return path;
    }

    private void WriteFile(string path, ReadOnlySpan<byte> content) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, content.Length);
        RandomAccess.Write(handle, content, 0);
        Interlocked.Add(ref _bytes, content.Length);
    }

    /// <summary>A name made fit for a file or folder on any system: what no system allows becomes an underscore.</summary>
    public static string Safe(string name) {
        if (name.AsSpan().IndexOfAny(Forbidden) < 0 && !name.EndsWith('.') && name.Length != 0) {
            return name;
        }
        var builder = new StringBuilder(name.Length);
        foreach (var c in name) {
            builder.Append(Forbidden.Contains(c) ? '_' : c);
        }
        var cleaned = builder.ToString().TrimEnd('.');
        return cleaned.Length == 0 ? "_" : cleaned;
    }
}
