using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using TiaFormat;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaDump;

/// <summary>The command line. Reads an archive and never writes to it; protected source stays closed.</summary>
internal sealed class TiaDumpCommands {
    /// <summary>Lists the blocks of an archive.</summary>
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
        Console.Error.WriteLine($"{rows} blocks");
        return 0;
    }

    /// <summary>Prints every block of a name as source text.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="name">The block's name.</param>
    /// <param name="plc">Only the block of this PLC.</param>
    [Command("show")]
    public int Show([Argument] string archive, [Argument] string name, string? plc = null) {
        var program = new TiaProgram(TiaDatabase.Open(archive));
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
            output.Write((byte)'\n');
            stdout.Write(output.WrittenSpan);
        }
        if (!found) {
            Console.Error.WriteLine($"No block named {name}.");
            return 1;
        }
        return 0;
    }

    /// <summary>Dumps every block and tag table of an archive as source files, in its folder tree.</summary>
    /// <param name="archive">A .zap* archive, a project or library folder, or a PEData.plf.</param>
    /// <param name="output">The folder to write to; created if missing.</param>
    /// <param name="grep">Only blocks whose name matches this regular expression.</param>
    /// <param name="raw">Also write each network as stored, next to the block (never encrypted parts).</param>
    /// <param name="verify">Check every frame's hash on opening.</param>
    [Command("dump")]
    public int Dump([Argument] string archive, [Argument] string output, string? grep = null, bool raw = false, bool verify = false) {
        var clock = Stopwatch.StartNew();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var database = TiaDatabase.Open(archive, verify);
        var program = new TiaProgram(database);
        var opened = clock.Elapsed;
        var result = new SourceDump(program, output, raw, grep is null ? null : new Regex(grep, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Run();
        clock.Stop();
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{database.AllFrameCount} frames, {database.Frames.Length} current; {result.BlockFiles} block files, {result.TagFiles} tag files, {result.Protected} protected, {result.Stubs} stubs, {result.Empty} with nothing stored"));
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
}
