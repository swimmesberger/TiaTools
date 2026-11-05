using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using TiaFormat;
using TiaFormat.Program;

namespace TiaDump;

/// <summary>Writes one archive's program as a file tree: environment / PLC / folder path / block.</summary>
internal sealed class SourceDump(TiaProgram program, string root, bool raw, Regex? filter) {
    internal sealed record Result(int BlockFiles, int TagFiles, int Protected, int Stubs, int Empty, long Bytes, int UnknownTags, IReadOnlyList<string> Failures);

    private static readonly SearchValues<char> Forbidden = SearchValues.Create("<>:\"/\\|?*\0");

    private readonly ConcurrentQueue<string> _failures = new();
    private readonly TiaDumpPaths _paths = new();
    private long _bytes;
    private int _unknown;

    public Result Run() {
        Directory.CreateDirectory(root);
        var work = new List<(TiaBlock Block, string Path)>();
        var protectedCount = 0;
        var stubs = 0;
        var empty = 0;
        var index = new StringBuilder("object\tenvironment\tplc\tkind\tnumber\tlanguage\tknow-how\tfile\tname\n");
        foreach (var block in program.Blocks) {
            if (filter is not null && !filter.IsMatch(block.Name)) {
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

        var tagFiles = filter is null ? WriteTagTables() : 0;
        File.WriteAllText(Path.Combine(root, "index.tsv"), index.ToString(), new UTF8Encoding(false));
        return new Result(work.Count, tagFiles, protectedCount, stubs, empty, Interlocked.Read(ref _bytes), _unknown, [.. _failures]);
    }

    private void WriteBlock(TiaBlock block, string path, PooledByteWriter output) {
        try {
            output.Clear();
            Interlocked.Add(ref _unknown, TiaSourceWriter.Write(program, block, output));
            WriteFile(path, output.WrittenSpan);
            if (raw && !block.IsKnowHowProtected) {
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
            if (table.Tags.Count == 0) {
                continue;
            }
            output.Clear();
            output.Write("// tag table "u8);
            output.Write(table.Name);
            output.Write((byte)'\n');
            foreach (var tag in table.Tags) {
                WriteTag(output, tag);
            }
            var folder = ProgramFolder(table.Frame);
            WriteFile(_paths.Reserve(Path.Combine(folder, Safe(table.Name) + ".tags"), table.Frame.ObjectId), output.WrittenSpan);
            count++;
        }
        return count;
    }

    private static void WriteTag(PooledByteWriter output, TiaTag tag) {
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

    private string BlockPath(TiaBlock block) {
        var extension = block.Kind switch {
            "UDT" => ".udt",
            _ when block.Kind.StartsWith("DB", StringComparison.Ordinal) => ".db",
            _ when block.Language is "SCL" => ".scl",
            _ when block.Language is null => ".txt",
            _ => "." + Safe(block.Language.ToLowerInvariant()),
        };
        return _paths.Reserve(Path.Combine(ProgramFolder(block.Frame), Safe(block.Name) + extension), block.Frame.ObjectId);
    }

    private string ProgramFolder(in TiaFormat.Storage.TiaFrame frame) {
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

    private static string Safe(string name) {
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
