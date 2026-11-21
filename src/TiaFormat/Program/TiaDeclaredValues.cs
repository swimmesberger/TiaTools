using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaFormat.Program;

/// <summary>One member as parsed: what the interface states, before it is frozen into a <see cref="TiaMember"/>.</summary>
internal sealed class TiaMemberNode(string name, string type, string path, string section, string? comment, string? startValue, string? rid, List<TiaMemberNode> children) {
    public string Name { get; } = name;
    public string Type { get; } = type;
    public string Path { get; } = path;
    public string Section { get; } = section;
    public string? Comment { get; } = comment;
    public string? StartValue { get; set; } = startValue;
    public string? Rid { get; } = rid;
    public List<TiaMemberNode> Children { get; } = children;

    /// <summary>The members of its UDT or FB type. They are not its own, so a source file does not list them.</summary>
    public List<TiaMemberNode> TypeMembers { get; init; } = [];

    /// <summary>The size of its UDT or FB type in the optimized layout, in bits: the stride of an array of it.</summary>
    public int? TypeOptimizedBits { get; init; }
    public int? OptimizedBitOffset { get; set; }
    public bool IsRetain { get; init; }
    public bool IsDbSpecific { get; init; }
    public int? Flags { get; init; }
    public string Id { get; init; } = "";
    public int? Lid { get; init; }
    public string? AtMemberId { get; init; }
    public string? InstructionVersion { get; init; }
    public IReadOnlyDictionary<string, int> Constants { get; init; } = new Dictionary<string, int>();

    /// <summary>What is inside it, whichever way that is declared.</summary>
    public List<TiaMemberNode> Inner => Children.Count != 0 ? Children : TypeMembers;
}

/// <summary>
/// The value a source file writes after a member's type. Values are stored one by one, with paths such as
/// <c>52[1]:53</c>; a member of a UDT or array type gets them gathered: <c>(TRUE, (), 50)</c>, <c>[3(false), 2(true)]</c>,
/// with <c>()</c> for what is not set. A struct's parts are declared on lines of their own, also in an array of structs,
/// where the first element speaks for all. Inside a type a struct stays closed: its values are not shown.
/// </summary>
internal sealed partial class TiaDeclaredValues(IReadOnlyDictionary<string, string> values) {
    private readonly List<(string Path, string Stripped)> _paths = [.. values.Keys.Select(path => (path, Indexes().Replace(path, "")))];

    /// <summary>The value to write for a member at this indexed path, or null if nothing below it is set.</summary>
    public string? Text(TiaMemberNode member, string indexedPath) {
        if (values.Count == 0 || member.Children.Count != 0) {
            return null;
        }
        var text = Render(member, indexedPath);
        return text.AsSpan().IndexOfAnyExcept(EmptyParts) < 0 ? null : text;
    }

    /// <summary>The indexed path of a member's parts: the first element's, if it is an array.</summary>
    public static string FirstElement(TiaMemberNode member, string indexedPath) {
        if (!IsArray(member)) {
            return indexedPath;
        }
        var bounds = Bounds(member);
        return bounds.Count == 0 ? indexedPath : indexedPath + "[" + string.Join(',', bounds.Select(bound => bound.Low.ToString(CultureInfo.InvariantCulture))) + "]";
    }

    private static readonly SearchValues<char> EmptyParts = SearchValues.Create("()[], ");

    private string Render(TiaMemberNode member, string prefix) {
        if (values.TryGetValue(prefix, out var value)) {
            return value;
        }
        if (IsArray(member)) {
            return member.Children.Count != 0 ? "[()]" : RenderArray(member, prefix);
        }
        if (member.Children.Count != 0) {
            return "()";
        }
        // A UDT with nothing set is empty, unless another element of its array has values there. A system type always shows its parts.
        return member.TypeMembers.Count != 0 && (member.InstructionVersion is not null || HasValueBelow(member)) ? RenderParts(member, prefix) : "()";
    }

    private bool HasValueBelow(TiaMemberNode member) {
        var below = member.Path + ":";
        return _paths.Exists(path => path.Stripped.StartsWith(below, StringComparison.Ordinal));
    }

    private string RenderParts(TiaMemberNode member, string prefix) =>
        "(" + string.Join(", ", member.TypeMembers.Select(part => Render(part, prefix + ":" + part.Id))) + ")";

    private string RenderArray(TiaMemberNode member, string prefix) {
        var set = new SortedDictionary<int, string>();
        var bounds = Bounds(member);
        var open = prefix + "[";
        var seen = new HashSet<int>();
        foreach (var (path, _) in _paths) {
            if (!path.StartsWith(open, StringComparison.Ordinal)) {
                continue;
            }
            var close = path.IndexOf(']', open.Length);
            var at = Position(path[open.Length..close], bounds);
            if (seen.Add(at)) {
                set[at] = Element(member, path[..(close + 1)]);
            }
        }
        if (set.Count == 0) {
            return "[()]";
        }
        var hole = Hole(member);
        var items = new List<string>(set.Count);
        // Elements with parts are all written, the empty ones too. A plain array stops at its last value.
        var count = member.TypeMembers.Count != 0 && bounds.Count != 0
            ? bounds.Aggregate(1, (size, bound) => size * (bound.High - bound.Low + 1))
            : set.Keys.Max() + 1;
        for (var i = 0; i < count; i++) {
            items.Add(set.TryGetValue(i, out var item) ? item : hole);
        }
        return "[" + Compressed(items, member.TypeMembers.Count == 0) + "]";
    }

    private string Element(TiaMemberNode member, string indexed) {
        if (values.TryGetValue(indexed, out var value)) {
            return value;
        }
        return member.TypeMembers.Count != 0 ? RenderParts(member, indexed) : "()";
    }

    /// <summary>An element not set has its type's zero, written out; one with parts has them empty.</summary>
    private string Hole(TiaMemberNode member) {
        if (member.TypeMembers.Count != 0) {
            return RenderParts(member, "?");
        }
        var element = member.Type[(member.Type.LastIndexOf(" of ", StringComparison.Ordinal) + 4)..];
        return element switch {
            "Bool" => "false",
            "Byte" or "Word" or "DWord" or "LWord" => "16#0",
            "Real" or "LReal" => "0.0",
            "SInt" or "Int" or "DInt" or "LInt" or "USInt" or "UInt" or "UDInt" or "ULInt" => "0",
            _ when element.StartsWith("String", StringComparison.Ordinal) => "''",
            _ => "()",
        };
    }

    /// <summary>Equal neighbours of a plain array are written once with their count; elements with parts are written out.</summary>
    private static string Compressed(List<string> items, bool plain) {
        var text = new StringBuilder();
        for (var i = 0; i < items.Count;) {
            var run = i + 1;
            while (plain && run < items.Count && items[run] == items[i]) {
                run++;
            }
            if (text.Length != 0) {
                text.Append(", ");
            }
            if (run - i == 1) {
                text.Append(items[i]);
            } else {
                text.Append(CultureInfo.InvariantCulture, $"{run - i}({items[i]})");
            }
            i = run;
        }
        return text.ToString();
    }

    private static int Position(string indexes, List<(int Low, int High)> bounds) {
        var parts = indexes.Split(',');
        var position = 0;
        for (var i = 0; i < parts.Length; i++) {
            var index = int.Parse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (i >= bounds.Count) {
                return index;
            }
            position = (position * (bounds[i].High - bounds[i].Low + 1)) + index - bounds[i].Low;
        }
        return position;
    }

    internal static bool IsArray(TiaMemberNode member) => member.Type.StartsWith("Array[", StringComparison.Ordinal);

    /// <summary>The array's bounds, with constants resolved; empty if one is not known.</summary>
    internal static List<(int Low, int High)> Bounds(TiaMemberNode member) {
        var close = member.Type.IndexOf(']', StringComparison.Ordinal);
        var result = new List<(int, int)>();
        if (close < 0) {
            return result;
        }
        foreach (var range in member.Type[6..close].Split(',')) {
            var dots = range.IndexOf("..", StringComparison.Ordinal);
            if (dots < 0 || !TryBound(range[..dots].Trim(), member, out var low) || !TryBound(range[(dots + 2)..].Trim(), member, out var high)) {
                return [];
            }
            result.Add((low, high));
        }
        return result;
    }

    private static bool TryBound(string text, TiaMemberNode member, out int bound) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out bound) || member.Constants.TryGetValue(text, out bound);

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex Indexes();
}
