using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0014: the texts of a reference project with several languages, read from its archive and compared with TIA's
/// own exports of them, language by language. Each problem names the project, the object, the place, the language and
/// both texts.
/// </summary>
internal static partial class ReferenceTextChecks {
    private const string Inherited = "### Inherited from Type ###";
    private const string NoValue = "<No value>";

    /// <summary>Whether a reference project has several languages and TIA's exports of its texts.</summary>
    public static bool HasTexts(string name) => File.Exists(Path.Combine(ReferenceProjects.Generated(name), "languages.txt"));

    public static TiaProgram Program(string name) => new(TiaDatabase.Open(ReferenceProjects.Archive(name)));

    /// <summary>The languages TIA's export names: active ones in its order, and the editing and reference language.</summary>
    public static (IReadOnlyList<string> All, string Editing, string Reference) TiasLanguages(string name) {
        var lines = File.ReadAllLines(Path.Combine(ReferenceProjects.Generated(name), "languages.txt")).Select(line => line.Split(' ')).ToList();
        return ([.. lines.Where(line => line[0] == "active").Select(line => line[1])], lines.Single(line => line[0] == "editing")[1], lines.Single(line => line[0] == "reference")[1]);
    }

    /// <summary>Every title and comment of each block TIA exported as SimaticML, and every tag comment, in every language.</summary>
    public static IReadOnlyList<string> BlockAndTagProblems(string name, out int compared) {
        compared = 0;
        var program = Program(name);
        var problems = new List<string>();
        foreach (var row in Manifest(name).Where(row => row.Xml.Length != 0)) {
            var file = Path.Combine(ReferenceProjects.Generated(name), row.Xml);
            if (row.Kind == "TAGS") {
                var table = program.ReadTagTables().Single(candidate => candidate.Name == row.Name && program.PlcName(candidate.Frame) == row.Plc);
                var theirs = XDocument.Load(file).Descendants("SW.Tags.PlcTag").ToDictionary(
                    tag => "tag " + tag.Element("AttributeList")!.Element("Name")!.Value,
                    tag => SimaticMlTexts.All(tag.Elements("ObjectList").Elements("MultilingualText").FirstOrDefault(text => (string?)text.Attribute("CompositionName") == "Comment")));
                compared += Compare($"{name}: {row.Plc} tag table {row.Name}", program, program.TagTexts(table), theirs, problems);
                continue;
            }
            var block = program.Blocks.SingleOrDefault(candidate => candidate.Name == row.Name && program.PlcName(candidate.Frame) == row.Plc && !candidate.IsStub && !candidate.IsKnowHowProtected);
            if (block is null) {
                problems.Add($"{name}: {row.Plc} {row.Kind} {row.Name}: not in the archive");
                continue;
            }
            compared += Compare($"{name}: {row.Plc} {row.Kind} {row.Name}", program, program.BlockTexts(block), BlockTexts(file, program.Networks(block)), problems);
        }
        return problems;
    }

    /// <summary>Every entry of each PLC's text lists, and each list's range and comment, against TIA's text list export.</summary>
    public static IReadOnlyList<string> TextListProblems(string name, out int compared) {
        compared = 0;
        var program = Program(name);
        var lists = new TiaAlarms(program).TextLists();
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(ReferenceProjects.Generated(name), "textlists.xlsx", SearchOption.AllDirectories)) {
            var plc = Path.GetFileName(Path.GetDirectoryName(file)!);
            var sheets = XlsxSheets.Read(file);
            var listRows = sheets["TextList"];
            var entryRows = sheets["TextListEntry"];
            foreach (var row in listRows.Skip(1)) {
                var what = $"{name}: {plc} text list {row[0]}";
                if (lists.SingleOrDefault(list => list.Plc == plc && list.Name == row[0]) is not { } list) {
                    problems.Add($"{what}: not in the archive");
                    continue;
                }
                if (list.Range != row[1]) {
                    problems.Add($"{what}: range ours `{list.Range}`, TIA's `{row[1]}`");
                }
                compared += CompareCells($"{what}, comment", program, list.Comment, listRows[0], row, "Comment", problems);
                var theirs = entryRows.Skip(1).Where(entry => entry[0] == row[0]).ToList();
                if (theirs.Count != list.Entries.Count) {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: {list.Entries.Count} entries read, {theirs.Count} in TIA's export"));
                }
                foreach (var entry in theirs) {
                    var from = long.Parse(entry[1], CultureInfo.InvariantCulture);
                    var to = long.Parse(entry[2], CultureInfo.InvariantCulture);
                    if (list.Entries.SingleOrDefault(candidate => candidate.From == from && candidate.To == to) is not { } ours) {
                        problems.Add($"{what}: entry {entry[1]}..{entry[2]} not in the archive");
                        continue;
                    }
                    compared += CompareCells($"{what}, entry {entry[1]}..{entry[2]}", program, ours.Texts, entryRows[0], entry, "Text", problems);
                }
            }
            foreach (var list in lists.Where(list => list.Plc == plc && listRows.Skip(1).All(row => row[0] != list.Name))) {
                problems.Add($"{name}: {plc} text list {list.Name}: not in TIA's export");
            }
        }
        return problems;
    }

    /// <summary>
    /// Every instance alarm's class and texts against TIA's export of alarm instance texts. Where TIA says a value is
    /// inherited from the type, the instance must store none of its own and take its type's. A closed alarm must be closed.
    /// </summary>
    public static IReadOnlyList<string> AlarmProblems(string name, out int compared) {
        compared = 0;
        var program = Program(name);
        var alarms = new TiaAlarms(program).Alarms();
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(ReferenceProjects.Generated(name), "alarms.xlsx", SearchOption.AllDirectories)) {
            var plc = Path.GetFileName(Path.GetDirectoryName(file)!);
            var rows = XlsxSheets.Read(file)["Instance alarms"];
            var columns = rows[0].Select(header => AlarmColumn().Match(header)).ToList();
            foreach (var row in rows.Skip(1)) {
                var what = $"{name}: {plc} alarm {row[0]}.{row[1]}";
                if (alarms.SingleOrDefault(alarm => alarm.Plc == plc && alarm.Instance == row[0] && alarm.Name == row[1]) is not { } ours) {
                    problems.Add($"{what}: not in the archive");
                    continue;
                }
                if (ours.IsClosed) {
                    // The alarm of a protected FB: TIA shows its texts, the reader must not (FR-018).
                    continue;
                }
                var type = alarms.Single(alarm => alarm.Plc == plc && alarm.Instance is null && alarm.Block == ours.Block && alarm.Name == ours.Name);
                var theirClass = row[2] == Inherited ? type.Class : row[2];
                if (ours.Class != theirClass) {
                    problems.Add($"{what}: class ours `{ours.Class}`, TIA's `{theirClass}`");
                }
                for (var i = 0; i < columns.Count; i++) {
                    if (!columns[i].Success) {
                        continue;
                    }
                    var kind = columns[i].Groups["kind"].Value;
                    var culture = columns[i].Groups["culture"].Value;
                    var text = ours.Texts.Single(candidate => candidate.Kind == kind);
                    var language = program.Languages.Find(culture);
                    compared++;
                    if (row[i] == Inherited) {
                        var typeText = TiaTexts.In(type.Texts.Single(candidate => candidate.Kind == kind).Texts, language);
                        if (!text.Inherited && TiaTexts.In(text.Texts, language).Length != 0) {
                            problems.Add($"{what}, {kind}, {culture}: ours its own `{TiaTexts.In(text.Texts, language)}`, TIA's inherited from the type");
                        } else if (TiaTexts.In(text.Texts, language) != typeText) {
                            problems.Add($"{what}, {kind}, {culture}: ours `{TiaTexts.In(text.Texts, language)}`, the type's `{typeText}`");
                        }
                    } else if (TiaTexts.In(text.Texts, language) != row[i]) {
                        problems.Add($"{what}, {kind}, {culture}: ours `{TiaTexts.In(text.Texts, language)}`, TIA's `{row[i]}`");
                    }
                }
            }
        }
        return problems;
    }

    /// <summary>The project's alarm classes against TIA's export of them: name, display name and id.</summary>
    public static IReadOnlyList<string> AlarmClassProblems(string name, out int compared) {
        compared = 0;
        var program = Program(name);
        var ours = new TiaAlarms(program).Classes();
        using var zip = ZipFile.OpenRead(Path.Combine(ReferenceProjects.Generated(name), "alarmclasses.dat"));
        using var stream = zip.Entries.Single().Open();
        var problems = new List<string>();
        var theirs = XDocument.Load(stream).Root!.Elements("AlarmClass").ToList();
        foreach (var alarmClass in theirs) {
            compared++;
            var className = (string)alarmClass.Attribute("Name")!;
            if (ours.SingleOrDefault(candidate => candidate.Name == className) is not { } mine) {
                problems.Add($"{name}: alarm class {className}: not in the archive");
                continue;
            }
            var displayName = alarmClass.Element("DisplayName")?.Value ?? "";
            foreach (var language in program.Languages.All.Where(language => TiaTexts.In(mine.DisplayName, language) != displayName)) {
                problems.Add($"{name}: alarm class {className}, display name, {language.Name}: ours `{TiaTexts.In(mine.DisplayName, language)}`, TIA's `{displayName}`");
            }
            if (mine.GlobalId?.ToString(CultureInfo.InvariantCulture) != alarmClass.Element("GlobalId")?.Value) {
                problems.Add($"{name}: alarm class {className}: id ours `{mine.GlobalId}`, TIA's `{alarmClass.Element("GlobalId")?.Value}`");
            }
        }
        problems.AddRange(ours.Where(mine => theirs.All(alarmClass => (string)alarmClass.Attribute("Name")! != mine.Name)).Select(mine => $"{name}: alarm class {mine.Name}: not in TIA's export"));
        return problems;
    }

    private static int Compare(string what, TiaProgram program, IReadOnlyList<TiaPlacedText> ours, Dictionary<string, IReadOnlyDictionary<string, string>> theirs, List<string> problems) {
        var mine = ours.ToDictionary(text => text.Place, text => text.Texts);
        var compared = 0;
        foreach (var place in mine.Keys.Union(theirs.Keys).Order(StringComparer.Ordinal)) {
            foreach (var language in program.Languages.All) {
                var ourText = mine.TryGetValue(place, out var texts) ? texts.FirstOrDefault(text => text.Lcid == language.Lcid).Text ?? "" : "";
                var theirText = theirs.TryGetValue(place, out var items) && items.TryGetValue(language.Name, out var item) ? item : "";
                if (ourText.Length == 0 && theirText.Length == 0) {
                    continue;
                }
                compared++;
                if (ourText != theirText) {
                    problems.Add($"{what}, {place}, {language.Name}: ours `{ourText}`, TIA's `{theirText}`");
                }
            }
        }
        return compared;
    }

    private static int CompareCells(string what, TiaProgram program, IReadOnlyList<TiaText> ours, string[] header, string[] row, string column, List<string> problems) {
        var compared = 0;
        foreach (var language in program.Languages.All) {
            var index = Array.IndexOf(header, $"{column} [{language.Name}]");
            var theirs = index < 0 || index >= row.Length || row[index] == NoValue ? "" : row[index];
            var mine = TiaTexts.In(ours, language);
            compared++;
            if (mine != theirs) {
                problems.Add($"{what}, {language.Name}: ours `{mine}`, TIA's `{theirs}`");
            }
        }
        return compared;
    }

    /// <summary>
    /// The texts of a block's SimaticML by the places the reader names them: its title and comment, each compile unit's
    /// title and comment by the number the archive gives that network, and each member's comment by its dotted name.
    /// Members inside an instance's own sections, such as a <c>Program_Alarm</c>'s, belong to their type and are left out.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> BlockTexts(string file, IReadOnlyList<TiaNetwork> networks) {
        var root = XDocument.Load(file).Root!.Elements().Single(element => element.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal));
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var objects = root.Element("ObjectList");
        Add("title", Text(objects, "Title"));
        Add("comment", Text(objects, "Comment"));
        var units = root.Descendants("SW.Blocks.CompileUnit").ToList();
        for (var i = 0; i < units.Count && i < networks.Count; i++) {
            var number = networks[i].Number.ToString(CultureInfo.InvariantCulture);
            Add($"network {number} title", Text(units[i].Element("ObjectList"), "Title"));
            Add($"network {number} comment", Text(units[i].Element("ObjectList"), "Comment"));
        }
        var sections = root.Element("AttributeList")?.Element("Interface")?.Elements().FirstOrDefault();
        foreach (var member in sections?.Elements().SelectMany(section => section.Elements()) ?? []) {
            Member(member, "");
        }
        return result;

        void Member(XElement member, string prefix) {
            var memberName = prefix + (string)member.Attribute("Name")!;
            var comment = member.Elements().FirstOrDefault(element => element.Name.LocalName == "Comment");
            if (comment is not null) {
                Add("member " + memberName, comment.Elements().Where(text => text.Name.LocalName == "MultiLanguageText")
                    .ToDictionary(text => (string)text.Attribute("Lang")!, text => text.Value));
            }
            foreach (var child in member.Elements().Where(element => element.Name.LocalName == "Member")) {
                Member(child, memberName + ".");
            }
        }

        void Add(string place, IReadOnlyDictionary<string, string> texts) {
            if (texts.Values.Any(text => text.Length != 0)) {
                result[place] = texts;
            }
        }

        static IReadOnlyDictionary<string, string> Text(XElement? objects, string composition) =>
            SimaticMlTexts.All(objects?.Elements("MultilingualText").FirstOrDefault(text => (string?)text.Attribute("CompositionName") == composition));
    }

    private sealed record ManifestRow(string Plc, string Kind, string Xml, string Name);

    private static IEnumerable<ManifestRow> Manifest(string name) =>
        File.ReadLines(Path.Combine(ReferenceProjects.Generated(name), "manifest.tsv")).Skip(1).Select(line => line.Split('\t'))
            .Where(columns => columns.Length >= 8 && columns[4] == "ok")
            .Select(columns => new ManifestRow(columns[0], columns[1], columns[6], columns[7]));

    /// <summary>A column of TIA's alarm text export: <c>"Alarm text" - English (United States) / [en-US] / Event text</c>.</summary>
    [GeneratedRegex(@"^""(?<kind>[^""]+)"" - .* / \[(?<culture>[a-zA-Z-]+)\] / ", RegexOptions.CultureInvariant)]
    private static partial Regex AlarmColumn();
}
