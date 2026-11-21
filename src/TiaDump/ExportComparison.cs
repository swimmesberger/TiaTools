using System.Globalization;
using System.Text;
using TiaFormat.Program;

namespace TiaDump;

/// <summary>Compares a dump with a folder written by tools/TiaOpennessExport and prints how much of it is the same text.</summary>
internal static class ExportComparison {
    public static int Run(string dump, string export) {
        Console.OutputEncoding = new UTF8Encoding(false);
        var blocks = TiaFolderComparison.Compare(dump, export);
        var rows = blocks.Where(block => block.Outcome != TiaComparisonOutcome.NotInExport).ToList();
        var onlyOurs = blocks.Where(block => block.Outcome == TiaComparisonOutcome.NotInExport).ToList();

        foreach (var kind in rows.GroupBy(row => row.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            var parts = kind.GroupBy(row => Label(row.Outcome)).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} {group.Key}"));
            Console.Out.WriteLine($"{kind.Key,-12} {string.Join(", ", parts)}");
        }
        foreach (var block in onlyOurs) {
            Console.Out.WriteLine($"only in the dump: {block.Plc} {block.Kind} {block.Name}");
        }
        var differences = rows
            .Where(row => row.Difference.Match != TiaSourceMatch.Identical && row.Difference.Line != 0)
            .OrderBy(row => row.Difference.Match)
            .ThenBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Difference.Theirs, StringComparer.Ordinal)
            .ToList();
        foreach (var row in differences) {
            Console.Out.WriteLine($"\n{row.Kind} {row.Name}, line {row.Difference.Line.ToString(CultureInfo.InvariantCulture)} ({Label(row.Outcome)})");
            Console.Out.WriteLine($"  ours:   {Shown(row.Difference.Ours)}");
            Console.Out.WriteLine($"  theirs: {Shown(row.Difference.Theirs)}");
        }
        return differences.Count == 0 && onlyOurs.Count == 0 ? 0 : 1;
    }

    private static string Label(TiaComparisonOutcome outcome) => outcome switch {
        TiaComparisonOutcome.Identical => "identical",
        TiaComparisonOutcome.TrailingWhitespace => "trailing whitespace only",
        TiaComparisonOutcome.Closed => "closed",
        TiaComparisonOutcome.NoGeneratedSource => "no source from TIA",
        TiaComparisonOutcome.NotInDump => "not in the dump",
        _ => "different",
    };

    private static string Shown(string? line) => line is null ? "(no line)" : line.Length > 160 ? line[..160] + "..." : line;
}
