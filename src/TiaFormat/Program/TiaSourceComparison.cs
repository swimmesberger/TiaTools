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
