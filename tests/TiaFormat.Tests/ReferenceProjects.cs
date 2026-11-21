using System.Xml.Linq;
using System.Globalization;
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
        [.. Manifest(name).Where(row => row.Kind is not ("TAGS" or "TO") && row.Source.Length == 0 && row.Xml.Length != 0).Select(row => (row.Plc, row.Kind, row.Name))];

    private sealed record ManifestRow(string Plc, string Kind, string Language, string Source, string Xml, string Name);

    private static IEnumerable<ManifestRow> Manifest(string name) =>
        File.ReadLines(Path.Combine(Generated(name), "manifest.tsv")).Skip(1).Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= 8 && columns[4] == "ok")
            .Select(columns => new ManifestRow(columns[0], columns[1], columns[3], columns[5], columns[6], columns[7]));

    /// <summary>What fails a reference project: each problem names the project, the block, and what is wrong with it.</summary>
    public static IReadOnlyList<string> Problems(string project, IReadOnlyList<TiaComparedBlock> blocks, IReadOnlyList<KnownBlock> known, IReadOnlySet<(string Plc, string Kind, string Name)>? exportedAsXml = null) {
        var problems = new List<string>();
        foreach (var block in blocks) {
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
        // Technology objects in known.tsv are TechnologyObjectProblems's to check.
        foreach (var entry in known.Where(entry => entry.Kind != "TO" && !blocks.Any(block => block.Plc == entry.Plc && block.Kind == entry.Kind && block.Name == entry.Name))) {
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

    /// <summary>
    /// Spec 0013: each technology object TIA reported, by its kind, version and the value of every parameter TIA gives,
    /// against the reader's. Parameters starting with <c>_</c> are settings outside the data block, and one whose value is
    /// a device item names hardware; neither is a member. known.tsv lists, with kind TO, an object that TIA did not compile,
    /// whose defaults are not in the archive, so only values stored on it are compared; and <c>object/parameter</c> where
    /// Openness gives a value in other units than the data block holds. A listed difference that is gone is a problem too,
    /// so the list stays true. Each problem names the project, the technology object, the parameter and both values.
    /// </summary>
    public static IReadOnlyList<string> TechnologyObjectProblems(string name, out int objects, out int compared) {
        objects = 0;
        compared = 0;
        var program = new TiaProgram(TiaDatabase.Open(Archive(name)));
        var known = Known(name).Where(entry => entry.Kind == "TO").ToList();
        var problems = new List<string>();
        foreach (var row in File.ReadLines(Path.Combine(Generated(name), "manifest.tsv")).Skip(1).Select(line => line.Split('\t')).Where(row => row[1] == "TO")) {
            var (plc, kind, status, file, objectName, version) = (row[0], row[3], row[4], row[6], row[7], row[8]);
            var what = $"{name}: {plc} technology object {objectName}";
            if (status != "ok") {
                problems.Add($"{what}: TIA's parameters were not exported ({row[8]})");
                continue;
            }
            var technologyObject = program.TechnologyObjects.SingleOrDefault(candidate => candidate.Name == objectName && program.PlcName(candidate.Frame) == plc);
            if (technologyObject is null) {
                problems.Add($"{what}: not in the archive as the reader reads it");
                continue;
            }
            objects++;
            if (technologyObject.Kind != kind || technologyObject.Version != version) {
                problems.Add($"{what}: ours {technologyObject.Kind} {technologyObject.Version}, TIA's {kind} {version}");
            }
            var notCompiled = known.Any(entry => entry.Plc == plc && entry.Name == objectName);
            var withoutValue = 0;
            var ours = program.ReadParameters(technologyObject).ToDictionary(parameter => parameter.Name);
            foreach (var parameter in File.ReadLines(Path.Combine(Generated(name), file)).Skip(1).Select(line => line.Split('\t'))) {
                var (parameterName, type, theirs) = (parameter[0], parameter[1], parameter[2]);
                if (parameterName.StartsWith('_') || type.StartsWith("DeviceItem", StringComparison.Ordinal) || theirs.Length == 0) {
                    continue;
                }
                if (!ours.TryGetValue(parameterName, out var our)) {
                    problems.Add($"{what}, {parameterName}: not among the parameters the reader reads, TIA's `{theirs}`");
                    continue;
                }
                if (our.Value is null && notCompiled) {
                    withoutValue++;
                    continue;
                }
                compared++;
                var listed = known.Any(entry => entry.Plc == plc && entry.Name == objectName + "/" + parameterName);
                var same = SameValue(our.Value, theirs);
                if (same && listed) {
                    problems.Add($"{what}, {parameterName}: matches TIA now; take it off known.tsv");
                } else if (!same && !listed) {
                    problems.Add($"{what}, {parameterName}: ours `{our.Value}`, TIA's `{theirs}`");
                }
            }
            if (notCompiled && withoutValue == 0) {
                problems.Add($"{what}: every parameter has a value now; take it off known.tsv");
            }
        }
        return problems;
    }

    /// <summary>
    /// A value as the reader writes it and as Openness gives it: numbers by value, also where ours is written in hex
    /// (<c>16#07</c>, <c>DW#16#03000000</c>), truth values in either case.
    /// </summary>
    private static bool SameValue(string? ours, string theirs) {
        if (ours is null) {
            return false;
        }
        if (Number(ours) is { } a && Number(theirs) is { } b) {
            return a == b || (float)a == (float)b;
        }
        return string.Equals(ours, theirs, StringComparison.OrdinalIgnoreCase);

        static double? Number(string text) {
            var hex = text.IndexOf("16#", StringComparison.Ordinal);
            if (hex >= 0) {
                return ulong.TryParse(text[(hex + 3)..].Replace("_", "", StringComparison.Ordinal), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
            }
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
        }
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
