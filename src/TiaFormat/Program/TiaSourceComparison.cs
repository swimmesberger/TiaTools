namespace TiaFormat.Program;

/// <summary>How close a dumped block's text comes to what TIA generates.</summary>
public enum TiaSourceMatch {
    Identical,
    TrailingWhitespace,
    Different,
}

/// <summary>The result of one comparison. Line is 1-based and 0 when identical; a missing line is null.</summary>
public readonly record struct TiaSourceDifference(TiaSourceMatch Match, int Line, string? Ours, string? Theirs);

/// <summary>Compares a block file of a dump with TIA's generated source of the same block.</summary>
public static class TiaSourceComparison {
    private const int LeadingCommentLines = 2;

    /// <summary>Cuts what the writer adds to TIA's text: the leading comment lines and all after the closing line.</summary>
    public static string OurText(string dumped) {
        var start = 0;
        for (var i = 0; i < LeadingCommentLines; i++) {
            start = dumped.IndexOf('\n', start) + 1;
        }
        var closing = dumped.LastIndexOf("\nEND_", StringComparison.Ordinal);
        if (closing < start) {
            return dumped[start..];
        }
        // The closing line and the blank line after it are TIA's; our trailing comment starts after them.
        var end = dumped.IndexOf('\n', closing + 1);
        end = end < 0 ? dumped.Length : Math.Min(end + 2, dumped.Length);
        return dumped[start..end];
    }

    /// <summary>TIA writes CRLF where a dump writes LF. No other change is made to its text.</summary>
    public static string TheirText(string generated) => generated.TrimStart('﻿').ReplaceLineEndings("\n");

    /// <summary>Compares exactly, then with trailing whitespace ignored, so a real difference stands out.</summary>
    public static TiaSourceDifference Compare(string ours, string theirs) {
        if (string.Equals(ours, theirs, StringComparison.Ordinal)) {
            return new TiaSourceDifference(TiaSourceMatch.Identical, 0, null, null);
        }
        var ourLines = ours.Split('\n');
        var theirLines = theirs.Split('\n');
        var count = Math.Max(ourLines.Length, theirLines.Length);
        var firstWhitespace = -1;
        for (var i = 0; i < count; i++) {
            var our = i < ourLines.Length ? ourLines[i] : null;
            var their = i < theirLines.Length ? theirLines[i] : null;
            if (string.Equals(our, their, StringComparison.Ordinal)) {
                continue;
            }
            if (our is null || their is null || !string.Equals(our.TrimEnd(), their.TrimEnd(), StringComparison.Ordinal)) {
                return new TiaSourceDifference(TiaSourceMatch.Different, i + 1, our, their);
            }
            if (firstWhitespace < 0) {
                firstWhitespace = i;
            }
        }
        return new TiaSourceDifference(TiaSourceMatch.TrailingWhitespace, firstWhitespace + 1, ourLines[firstWhitespace], theirLines[firstWhitespace]);
    }
}

/// <summary>What became of one block of TIA's export, or of the dump, in a comparison of the two folders.</summary>
public enum TiaComparisonOutcome {
    Identical,
    TrailingWhitespace,
    Different,
    /// <summary>Know-how protected: TIA generates no source for it, and the reader shows none.</summary>
    Closed,
    /// <summary>TIA listed the block but wrote no source for it.</summary>
    NoGeneratedSource,
    /// <summary>TIA wrote source for a block the dump has no file for.</summary>
    NotInDump,
    /// <summary>The dump has a file for a block TIA's export does not list.</summary>
    NotInExport,
}

/// <summary>One block of a comparison of folders. Kind is TIA's kind of block, or <c>block</c> or <c>UDT</c> for one only the dump has.</summary>
public sealed record TiaComparedBlock(string Plc, string Kind, string Name, TiaComparisonOutcome Outcome, TiaSourceDifference Difference);

/// <summary>Compares a dump with a folder that tools/TiaOpennessExport wrote, block by block, as <c>tiadump compare</c> does.</summary>
public static class TiaFolderComparison {
    /// <summary>Every block of the export, then every block only the dump has. A master copy's blocks are not in an export, so they are left out.</summary>
    public static IReadOnlyList<TiaComparedBlock> Compare(string dump, string export) {
        var ours = ReadOurFiles(dump);
        var blocks = new List<TiaComparedBlock>();
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
                blocks.Add(new TiaComparedBlock(plc, kind, name, TiaComparisonOutcome.Closed, default));
            } else if (source.Length == 0) {
                blocks.Add(new TiaComparedBlock(plc, kind, name, TiaComparisonOutcome.NoGeneratedSource, default));
            } else if (!ours.TryGetValue(key, out var file)) {
                blocks.Add(new TiaComparedBlock(plc, kind, name, TiaComparisonOutcome.NotInDump, default));
            } else {
                var difference = TiaSourceComparison.Compare(
                    TiaSourceComparison.OurText(File.ReadAllText(Path.Combine(dump, file), System.Text.Encoding.UTF8)),
                    TiaSourceComparison.TheirText(File.ReadAllText(Path.Combine(export, source), System.Text.Encoding.UTF8)));
                var outcome = difference.Match switch {
                    TiaSourceMatch.Identical => TiaComparisonOutcome.Identical,
                    TiaSourceMatch.TrailingWhitespace => TiaComparisonOutcome.TrailingWhitespace,
                    _ => TiaComparisonOutcome.Different,
                };
                blocks.Add(new TiaComparedBlock(plc, kind, name, outcome, difference));
            }
        }
        foreach (var ((plc, family, name), _) in ours.Where(pair => !inExport.Contains(pair.Key)).OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)) {
            blocks.Add(new TiaComparedBlock(plc, family, name, TiaComparisonOutcome.NotInExport, default));
        }
        return blocks;
    }

    private static string Family(string kind) => kind == "UDT" ? "UDT" : "block";

    /// <summary>The project's unprotected blocks that have a text form.</summary>
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
}
