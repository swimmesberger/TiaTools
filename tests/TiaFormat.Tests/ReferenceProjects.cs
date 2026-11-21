using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>A block a reference project lists in its known.tsv, with the reason it is known.</summary>
internal sealed record KnownBlock(string Plc, string Kind, string Name, string Reason);

/// <summary>
/// The reference projects in tests/reference (spec 0008): invented sources, the archive TIA Portal built from them, and
/// the source TIA generated for it. They are read from the repository, found from the test assembly's folder upwards.
/// </summary>
internal static class ReferenceProjects {
    public static string Root { get; } = FindRoot();

    /// <summary>Every reference project, by its folder's name.</summary>
    public static IEnumerable<string> Names() =>
        Directory.GetDirectories(Root).Where(folder => Directory.Exists(Path.Combine(folder, "sources"))).Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    public static string Folder(string name) => Path.Combine(Root, name);

    public static string Archive(string name) => Path.Combine(Folder(name), name + ".zap21");

    public static string Generated(string name) => Path.Combine(Folder(name), "generated");

    /// <summary>Dumps the project's archive into a new temporary folder, which the caller deletes.</summary>
    public static string Dump(string name) {
        var folder = Directory.CreateTempSubdirectory("tiareference").FullName;
        new TiaSourceExport(new TiaProgram(TiaDatabase.Open(Archive(name))), folder).Run();
        return folder;
    }

    public static IReadOnlyList<TiaComparedBlock> Compare(string name) {
        var dump = Dump(name);
        try {
            return TiaFolderComparison.Compare(dump, Generated(name));
        } finally {
            Directory.Delete(dump, recursive: true);
        }
    }

    public static IReadOnlyList<KnownBlock> Known(string name) {
        var file = Path.Combine(Folder(name), "known.tsv");
        return !File.Exists(file) ? [] : [.. File.ReadLines(file).Skip(1).Where(line => line.Length != 0).Select(line => line.Split('\t')).Select(columns => new KnownBlock(columns[0], columns[1], columns[2], columns[3]))];
    }

    /// <summary>The blocks TIA exported as SimaticML instead of generating source for (spec 0009): LAD, FBD and GRAPH.</summary>
    public static HashSet<(string Plc, string Kind, string Name)> ExportedAsXml(string name) =>
        [.. Manifest(name).Where(row => row.Kind is not ("TAGS" or "WATCH" or "FORCE") && row.Source.Length == 0 && row.Xml.Length != 0).Select(row => (row.Plc, row.Kind, row.Name))];

    /// <summary>
    /// Spec 0015: what differs between the project's watch and force tables as the reader reads them and as TIA exported
    /// them, entry by entry: name or address, display format, triggers, value and comment, and comment lines; and every
    /// table only one side has. TIA's export gives no address beside a name, and no selection box, so neither is compared.
    /// </summary>
    public static IReadOnlyList<string> WatchTableProblems(string name, out int compared) {
        compared = 0;
        var program = new TiaProgram(TiaDatabase.Open(Archive(name)));
        var ours = TiaWatchTables.Read(program)
            .Where(table => program.Environment(table.Frame).Kind == "ProjectData")
            .ToDictionary(table => (program.PlcName(table.Frame) ?? "", table.Kind == TiaWatchTableKind.Force ? "FORCE" : "WATCH", table.Name));
        var problems = new List<string>();
        var seen = new HashSet<(string, string, string)>();
        foreach (var row in Manifest(name).Where(row => row.Kind is "WATCH" or "FORCE")) {
            var key = (row.Plc, row.Kind, row.Name);
            seen.Add(key);
            var what = $"{name}: {row.Plc} {row.Kind} {row.Name}";
            if (!ours.TryGetValue(key, out var table)) {
                problems.Add($"{what}: not in the archive as the reader reads it");
                continue;
            }
            var force = row.Kind == "FORCE";
            var mine = table.Entries.Select(entry => DescribeEntry(entry, force)).ToList();
            var theirs = ExportedEntries(Path.Combine(Generated(name), row.Xml), force);
            if (mine.Count != theirs.Count) {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: {mine.Count} entries read, {theirs.Count} in TIA's export"));
            }
            foreach (var (index, (ourEntry, theirEntry)) in mine.Zip(theirs).Index()) {
                compared++;
                if (ourEntry != theirEntry) {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}, entry {index + 1}: ours `{ourEntry}`, TIA's `{theirEntry}`"));
                }
            }
        }
        foreach (var key in ours.Keys.Where(key => !seen.Contains(key) && ours[key].Entries.Count != 0)) {
            problems.Add($"{name}: {key.Item1} {key.Item2} {key.Item3}: not in TIA's export");
        }
        return problems;
    }

    /// <summary>
    /// Spec 0015: the cross references of every block TIA listed in xref.tsv, as the reader reads them from XRef.db and as
    /// TIA's CrossReferenceService gave them, each written as `user | used | access | place`. TIA's list names the data
    /// block where its user's rows use a member, and the member in the data block's rows, so both are compared; the place
    /// is the network of a LAD, FBD or STL block, and nothing for SCL and GRAPH, which TIA gives no network number. Rows
    /// that differ for a reason known.tsv gives, with kind `xref` and the row as the name, are excused.
    /// </summary>
    public static IReadOnlyList<string> CrossReferenceProblems(string name, out int compared) {
        compared = 0;
        var problems = new List<string>();
        var files = Directory.GetDirectories(Generated(name)).Select(folder => Path.Combine(folder, "xref.tsv")).Where(File.Exists).ToList();
        if (files.Count == 0) {
            return problems;
        }
        var program = new TiaProgram(TiaDatabase.Open(Archive(name)));
        var list = TiaCrossReferences.Read(program);
        if (list is null) {
            return [$"{name}: XRef.db could not be read"];
        }
        if (!list.IsCurrent) {
            problems.Add($"{name}: XRef.db does not belong to the database");
        }
        var theirs = new HashSet<string>(StringComparer.Ordinal);
        var users = new HashSet<(string Plc, string Block)>();
        foreach (var file in files) {
            var plc = Path.GetFileName(Path.GetDirectoryName(file)!);
            var all = File.ReadLines(file).Skip(1).Select(line => line.Split('\t')).Where(row => row.Length >= 13).ToList();
            // A tag's rows name the blocks that use it, as their own rows do: both are compared.
            foreach (var row in all.Where(row => row[0].StartsWith("tag:", StringComparison.Ordinal) && row[10] == "UsedBy")) {
                theirs.Add($"{plc} {row[5]} | {row[1]} | {row[11]} | {PlaceOf(row[12])}");
            }
            var rows = all.Where(row => row[0].StartsWith("block:", StringComparison.Ordinal)).ToList();
            users.UnionWith(rows.Select(row => (plc, row[0]["block:".Length..])));
            // An instance data block's rows list its function block's uses of its own members again, as members of the block.
            var instances = rows.Where(row => row[10] == "InstanceType" && row[11] == "InstanceDB" && row[1] == row[0]["block:".Length..])
                .Select(row => (Db: row[1], Fb: row[5])).ToHashSet();
            foreach (var row in rows) {
                var block = row[0]["block:".Length..];
                var (source, reference, type, relation, access, location) = (row[1], row[5], row[6], row[10], row[11], row[12]);
                if (type == "Jump label") {
                    continue;
                }
                var place = PlaceOf(location);
                if (relation == "Uses" && source == block) {
                    theirs.Add($"{plc} {block} | {Regex.Replace(reference, @" \[V[0-9.]+\]$", "")} | {access} | {place}");
                } else if (relation == "UsedBy" && source != block) {
                    var own = instances.Contains((block, reference)) && source.StartsWith("\"" + block + "\".", StringComparison.Ordinal);
                    theirs.Add($"{plc} {reference} | {(own ? source[(block.Length + 3)..] : source)} | {access} | {place}");
                }
            }
        }
        var ours = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in list.References) {
            var plc = program.PlcName(reference.User.Frame) ?? "";
            if (!users.Contains((plc, reference.User.Name)) || program.Environment(reference.User.Frame).Kind != "ProjectData") {
                continue;
            }
            var place = reference.Network is { } network && reference.User.Language is not ("SCL" or null) && !reference.User.Language.StartsWith("GRAPH", StringComparison.Ordinal)
                ? string.Create(CultureInfo.InvariantCulture, $"NW{network}") : "";
            var used = reference.Used.DisplayName(reference.User.Frame.Key);
            ours.Add($"{plc} {reference.User.Name} | {used} | {reference.Access} | {place}");
            if (reference.Used.Member is not null && reference.Used.Key != reference.User.Frame.Key) {
                ours.Add($"{plc} {reference.User.Name} | {reference.Used.Name} | {reference.Access} | {place}");
            }
        }
        var known = Known(name).Where(entry => entry.Kind == "xref").Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var row in theirs.Order(StringComparer.Ordinal)) {
            compared++;
            if (!ours.Contains(row) && !known.Remove("TIA's: " + row)) {
                problems.Add($"{name}: only TIA's list has `{row}`");
            }
        }
        foreach (var row in ours.Where(row => !theirs.Contains(row)).Order(StringComparer.Ordinal)) {
            if (!known.Remove("ours: " + row)) {
                problems.Add($"{name}: only the reader has `{row}`");
            }
        }
        problems.AddRange(known.Select(row => $"{name}: known.tsv lists `{row}`, which no longer differs; take it off"));
        return problems;

        static string PlaceOf(string location) {
            var network = Regex.Match(location, @"▶ NW(\d+)");
            return network.Success ? "NW" + network.Groups[1].Value : "";
        }
    }

    private static string DescribeEntry(TiaWatchEntry entry, bool force) => entry.IsCommentLine
        ? $"comment | {entry.Comment}"
        : $"{entry.Name ?? entry.Address} | {entry.Format} | {entry.MonitorTrigger} | {(force ? "" : entry.ModifyTrigger)} | {entry.Value} | {entry.Comment}";

    private static List<string> ExportedEntries(string file, bool force) {
        var table = XDocument.Load(file).Root!.Elements().Single(element => element.Name.LocalName.StartsWith("SW.WatchAndForceTables.", StringComparison.Ordinal));
        return [.. table.Elements("ObjectList").Elements().Select(entry => {
            var attributes = entry.Element("AttributeList");
            string? Value(string element) => attributes?.Element(element)?.Value is { Length: > 0 } text ? text : null;
            var comment = entry.Elements("ObjectList").Elements("MultilingualText").Where(text => (string?)text.Attribute("CompositionName") == "Comment")
                .Descendants("Text").Select(text => text.Value).FirstOrDefault(text => text.Length != 0);
            if (entry.Name.LocalName == "SW.WatchAndForceTables.PlcTableCommentEntry") {
                return $"comment | {comment}";
            }
            return $"{Value("Name") ?? Value("Address")} | {Value("DisplayFormat")} | {Value("MonitorTrigger")} | {(force ? "" : Value("ModifyTrigger"))} | {Value(force ? "ForceValue" : "ModifyValue")} | {comment}";
        })];
    }

    private sealed record ManifestRow(string Plc, string Kind, string Language, string Source, string Xml, string Name);

    private static IEnumerable<ManifestRow> Manifest(string name) =>
        File.ReadLines(Path.Combine(Generated(name), "manifest.tsv")).Skip(1).Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= 8 && columns[4] == "ok")
            .Select(columns => new ManifestRow(columns[0], columns[1], columns[3], columns[5], columns[6], columns[7]));

    /// <summary>What fails a reference project: each problem names the project, the block, and what is wrong with it.</summary>
    public static IReadOnlyList<string> Problems(string project, IReadOnlyList<TiaComparedBlock> blocks, IReadOnlyList<KnownBlock> known, IReadOnlySet<(string Plc, string Kind, string Name)>? exportedAsXml = null) {
        var problems = new List<string>();
        foreach (var block in blocks) {
            // Watch and force tables are compared entry by entry (spec 0015), not as source.
            if (block.Kind is "WATCH" or "FORCE") {
                continue;
            }
            if (block.Outcome == TiaComparisonOutcome.NoGeneratedSource && exportedAsXml is not null && exportedAsXml.Contains((block.Plc, block.Kind, block.Name))) {
                continue;
            }
            var listed = known.Any(entry => entry.Plc == block.Plc && entry.Kind == block.Kind && entry.Name == block.Name);
            var what = $"{project}: {block.Plc} {block.Kind} {block.Name}";
            switch (block.Outcome) {
                case TiaComparisonOutcome.Closed:
                    break;
                case TiaComparisonOutcome.Identical:
                    if (listed) {
                        problems.Add($"{what} matches TIA's generated source now; take it off known.tsv");
                    }
                    break;
                case TiaComparisonOutcome.TrailingWhitespace or TiaComparisonOutcome.Different:
                    if (!listed) {
                        problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}, line {block.Difference.Line}: ours `{block.Difference.Ours}`, TIA's `{block.Difference.Theirs}`"));
                    }
                    break;
                default:
                    if (!listed) {
                        problems.Add($"{what}: {block.Outcome}");
                    }
                    break;
            }
        }
        foreach (var entry in known.Where(entry => entry.Kind != "xref" && !blocks.Any(block => block.Plc == entry.Plc && block.Kind == entry.Kind && block.Name == entry.Name))) {
            problems.Add($"{project}: known.tsv lists {entry.Plc} {entry.Kind} {entry.Name}, which is in neither the archive nor TIA's export");
        }
        return problems;
    }

    /// <summary>
    /// What differs between the project's tag tables as the reader reads them and as TIA exported them: each tag's name,
    /// data type, address, comment and external access, in table order, and every table that only one side has.
    /// Constants are left out: TIA's export leaves out the system constants, and the sources define no constants.
    /// </summary>
    public static IReadOnlyList<string> TagProblems(string name) {
        var program = new TiaProgram(TiaDatabase.Open(Archive(name)));
        var ours = program.ReadTagTables()
            .Where(table => program.Environment(table.Frame).Kind == "ProjectData")
            .Select(table => (Key: (program.PlcName(table.Frame) ?? "", table.Name), Tags: table.Tags.Where(tag => tag.Value is null).ToList()))
            .Where(table => table.Tags.Count != 0)
            .ToDictionary(table => table.Key, table => table.Tags);
        var theirs = ExportedTagTables(name);
        var problems = new List<string>();
        foreach (var ((plc, table), tags) in theirs) {
            var what = $"{name}: {plc} tag table {table}";
            if (!ours.TryGetValue((plc, table), out var read)) {
                problems.Add($"{what}: not in the archive as the reader reads it");
                continue;
            }
            var mine = read.Select(Describe).ToList();
            if (mine.Count != tags.Count) {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: {mine.Count} tags read, {tags.Count} in TIA's export"));
            }
            foreach (var (ourTag, theirTag) in mine.Zip(tags).Where(pair => pair.First != pair.Second)) {
                problems.Add($"{what}: ours `{ourTag}`, TIA's `{theirTag}`");
            }
        }
        foreach (var (plc, table) in ours.Keys.Where(key => !theirs.ContainsKey(key))) {
            problems.Add($"{name}: {plc} tag table {table}: not in TIA's export");
        }
        return problems;
    }

    private static string Describe(TiaTag tag) =>
        $"{tag.Name} | {tag.Type} | {tag.Address} | {tag.Comment} | {tag.ExternalAccess} | {tag.ExternalVisible}";

    /// <summary>The tags of each table TIA exported, described as <see cref="Describe"/> describes ours.</summary>
    private static Dictionary<(string Plc, string Table), List<string>> ExportedTagTables(string name) {
        var result = new Dictionary<(string, string), List<string>>();
        foreach (var row in File.ReadLines(Path.Combine(Generated(name), "manifest.tsv")).Skip(1).Select(line => line.Split('\t'))) {
            if (row[1] != "TAGS" || row[4] != "ok") {
                continue;
            }
            var document = XDocument.Load(Path.Combine(Generated(name), row[6]));
            var tags = document.Descendants("SW.Tags.PlcTag").Select(tag => {
                var attributes = tag.Element("AttributeList")!;
                string? Value(string element) => attributes.Element(element)?.Value;
                var comment = tag.Elements("ObjectList").Elements("MultilingualText").Where(text => (string?)text.Attribute("CompositionName") == "Comment")
                    .Descendants("Text").Select(text => text.Value).FirstOrDefault(text => text.Length != 0);
                var access = Value("ExternalAccessible") == "false" ? TiaExternalAccess.None
                    : Value("ExternalWritable") == "false" ? TiaExternalAccess.ReadOnly : TiaExternalAccess.ReadWrite;
                var visible = Value("ExternalAccessible") == "false" ? false : Value("ExternalVisible") == "true";
                return Describe(new TiaTag(Value("Name")!, Value("DataTypeName"), Value("LogicalAddress"), null, comment) { ExternalAccess = access, ExternalVisible = visible });
            }).ToList();
            if (tags.Count != 0) {
                result[(row[0], row[7])] = tags;
            }
        }
        return result;
    }

    /// <summary>
    /// Spec 0009: each LAD and FBD network of the blocks TIA exported as SimaticML, written in statement form from the
    /// archive and from TIA's export, with its title and comment. Each problem names the block, the network and both texts.
    /// </summary>
    public static IReadOnlyList<string> NetworkProblems(string name, out int compared) {
        compared = 0;
        var program = new TiaProgram(TiaDatabase.Open(Archive(name)));
        var problems = new List<string>();
        foreach (var row in Manifest(name).Where(row => row.Kind != "TAGS" && row.Source.Length == 0 && row.Xml.Length != 0 && row.Language is "LAD" or "FBD" or "GRAPH")) {
            var what = $"{name}: {row.Plc} {row.Kind} {row.Name}";
            var block = program.Blocks.SingleOrDefault(candidate => candidate.Name == row.Name && program.PlcName(candidate.Frame) == row.Plc && !candidate.IsKnowHowProtected);
            if (block is null) {
                problems.Add($"{what}: not in the archive");
                continue;
            }
            var units = SimaticMlNetworks.Units(Path.Combine(Generated(name), row.Xml));
            if (row.Language == "GRAPH") {
                compared++;
                var ourSequence = TiaGraph.Write(program.ReadGraph(block));
                var theirSequence = TiaGraph.Write(SimaticMlNetworks.ReadGraph(units.Single().Source));
                var line = Enumerable.Range(0, Math.Max(ourSequence.Count, theirSequence.Count))
                    .FirstOrDefault(i => i >= ourSequence.Count || i >= theirSequence.Count || ourSequence[i] != theirSequence[i], -1);
                if (line >= 0) {
                    problems.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{what}, sequence line {line + 1}: ours `{(line < ourSequence.Count ? ourSequence[line] : "")}`, TIA's `{(line < theirSequence.Count ? theirSequence[line] : "")}`"));
                }
                continue;
            }
            var networks = program.Networks(block);
            if (units.Count != networks.Count) {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: {networks.Count} networks read, {units.Count} in TIA's export"));
                continue;
            }
            var idents = program.ReadIdents(block);
            for (var i = 0; i < units.Count; i++) {
                var network = $"{what}, network {i + 1}";
                if ((networks[i].Title ?? "") != units[i].Title) {
                    problems.Add($"{network}: title ours `{networks[i].Title}`, TIA's `{units[i].Title}`");
                }
                if ((networks[i].Comment ?? "") != units[i].Comment) {
                    problems.Add($"{network}: comment ours `{networks[i].Comment}`, TIA's `{units[i].Comment}`");
                }
                if (networks[i].Form != "FlgNet" || units[i].Source.Name.LocalName != "FlgNet") {
                    continue;
                }
                compared++;
                using var raw = new PooledByteWriter();
                program.WriteRawNetwork(block, i, raw);
                var ours = string.Join("\n", NetworkStatements.Write(TiaFlgNet.Read(raw.WrittenSpan, idents)));
                var theirs = string.Join("\n", NetworkStatements.Write(SimaticMlNetworks.Read(units[i].Source)));
                if (ours != theirs) {
                    problems.Add($"{network}:\nours:\n{ours}\nTIA's:\n{theirs}");
                }
            }
        }
        return problems;
    }

    private static string FindRoot() {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent) {
            if (File.Exists(Path.Combine(folder.FullName, "TiaTools.slnx"))) {
                return Path.Combine(folder.FullName, "tests", "reference");
            }
        }
        throw new InvalidOperationException("No TiaTools.slnx above " + AppContext.BaseDirectory + ": the reference projects are read from the repository.");
    }
}
