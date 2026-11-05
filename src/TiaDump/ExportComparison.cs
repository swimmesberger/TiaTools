using System.Globalization;
using System.Text;
using TiaFormat.Program;

namespace TiaDump;

/// <summary>Compares a dump with a folder written by tools/TiaOpennessExport and prints how much of it is the same text.</summary>
internal static class ExportComparison {
    public static int Run(string dump, string export) {
        Console.OutputEncoding = new UTF8Encoding(false);
        var ours = ReadOurFiles(dump);
        var rows = new List<Row>();
        var inExport = new HashSet<(string, string, string)>();
        foreach (var line in File.ReadLines(Path.Combine(export, "manifest.tsv")).Skip(1)) {
            var columns = line.Split('\t');
            var (plc, kind, status, source, name) = (columns[0], columns[1], columns[4], columns[5], columns[7]);
            if (kind == "TAGS") {
                continue;
            }
            var key = (plc, Family(kind), name);
            inExport.Add(key);
            if (status == "closed") {
                rows.Add(new Row(kind, name, "closed", default));
            } else if (source.Length == 0) {
                rows.Add(new Row(kind, name, "no source from TIA", default));
            } else if (!ours.TryGetValue(key, out var file)) {
                rows.Add(new Row(kind, name, "not in the dump", default));
            } else {
                var difference = TiaSourceComparison.Compare(
                    TiaSourceComparison.OurText(File.ReadAllText(Path.Combine(dump, file), Encoding.UTF8)),
                    TiaSourceComparison.TheirText(File.ReadAllText(Path.Combine(export, source), Encoding.UTF8)));
                rows.Add(new Row(kind, name, Label(difference.Match), difference));
            }
        }
        var onlyOurs = ours.Keys.Where(key => !inExport.Contains(key)).ToList();

        foreach (var kind in rows.GroupBy(row => row.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            var parts = kind.GroupBy(row => row.Outcome).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} {group.Key}"));
            Console.Out.WriteLine($"{kind.Key,-12} {string.Join(", ", parts)}");
        }
        foreach (var (plc, family, name) in onlyOurs.OrderBy(key => key.Item3, StringComparer.Ordinal)) {
            Console.Out.WriteLine($"only in the dump: {plc} {family} {name}");
        }
        var differences = rows
            .Where(row => row.Difference.Match != TiaSourceMatch.Identical && row.Difference.Line != 0)
            .OrderBy(row => row.Difference.Match)
            .ThenBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Difference.Theirs, StringComparer.Ordinal)
            .ToList();
        foreach (var row in differences) {
            Console.Out.WriteLine($"\n{row.Kind} {row.Name}, line {row.Difference.Line.ToString(CultureInfo.InvariantCulture)} ({row.Outcome})");
            Console.Out.WriteLine($"  ours:   {Shown(row.Difference.Ours)}");
            Console.Out.WriteLine($"  theirs: {Shown(row.Difference.Theirs)}");
        }
        return differences.Count == 0 && onlyOurs.Count == 0 ? 0 : 1;
    }

    private static string Label(TiaSourceMatch match) => match switch {
        TiaSourceMatch.Identical => "identical",
        TiaSourceMatch.TrailingWhitespace => "trailing whitespace only",
        _ => "different",
    };

    private static string Family(string kind) => kind == "UDT" ? "UDT" : "block";

    private static string Shown(string? line) => line is null ? "(no line)" : line.Length > 160 ? line[..160] + "..." : line;

    /// <summary>The project's unprotected blocks that have a text form; a master copy's blocks are not in an export.</summary>
    private static Dictionary<(string Plc, string Family, string Name), string> ReadOurFiles(string dump) {
        var files = new Dictionary<(string, string, string), string>();
        foreach (var line in File.ReadLines(Path.Combine(dump, "index.tsv")).Skip(1)) {
            var columns = line.Split('\t');
            var (environment, plc, kind, knowHow, file, name) = (columns[1], columns[2], columns[3], columns[6], columns[7], columns[8]);
            if (environment == "ProjectData" && knowHow == "no" && file.Length != 0 && !file.EndsWith(".f_fbd", StringComparison.Ordinal)) {
                files[(plc, Family(kind), name)] = file;
            }
        }
        return files;
    }

    private readonly record struct Row(string Kind, string Name, string Outcome, TiaSourceDifference Difference);
}
