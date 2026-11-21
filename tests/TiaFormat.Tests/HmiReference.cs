using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0011, FR-014: what the reader reports of a reference project's HMI devices against what TIA Portal gave for them
/// through Openness when it built the project, in <c>generated/&lt;HMI device&gt;/</c>. A Unified device is described
/// by the files the exporter wrote from TIA's HmiUnified API, a Comfort/Advanced one by TIA's SimaticML export of its
/// tag tables and screens and by the hardware API's connections.
/// </summary>
internal static partial class HmiReference {
    /// <summary>Each difference, naming the project, the device and what differs; <paramref name="compared"/> counts the objects compared.</summary>
    public static IReadOnlyList<string> Problems(string name, out int compared) {
        compared = 0;
        var report = TiaHmi.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive(name))));
        var problems = new List<string>();
        var generated = ReferenceProjects.Generated(name);
        var described = Directory.GetDirectories(generated).Where(folder => File.Exists(Path.Combine(folder, "device.tsv"))).ToList();
        foreach (var device in report.Devices.Where(device => !described.Any(folder => Path.GetFileName(folder) == device.Name))) {
            problems.Add($"{name}: HMI device {device.Name} is not in TIA's export");
        }
        foreach (var folder in described) {
            var row = Rows(Path.Combine(folder, "device.tsv")).Single();
            var what = $"{name}: HMI device {row[0]}";
            var ours = report.Devices.FirstOrDefault(device => device.Name == row[0]);
            if (ours is null) {
                problems.Add($"{what}: not in the archive as the reader reads it");
                continue;
            }
            compared++;
            Expect(problems, what, "family", ours.FamilyName, row[1]);
            foreach (var connection in Rows(Path.Combine(folder, "connections.tsv"))) {
                compared++;
                var mine = ours.Connections.FirstOrDefault(candidate => candidate.Name == connection[0]);
                if (mine is null) {
                    problems.Add($"{what}: connection {connection[0]} not read");
                    continue;
                }
                Expect(problems, what, $"connection {connection[0]} PLC", mine.Plc, connection[1]);
                if (connection[2].Length != 0) {
                    Expect(problems, what, $"connection {connection[0]} driver", mine.DriverName, connection[2]);
                }
            }
            if (ours.Family == TiaWinCcFamily.Unified) {
                compared += Unified(ours, folder, what, problems);
            } else {
                compared += Advanced(ours, folder, what, problems);
            }
        }
        return problems;
    }

    private static int Unified(TiaHmiDevice ours, string folder, string what, List<string> problems) {
        var compared = 0;
        var tags = ours.TagTables.SelectMany(table => table.Tags.Select(tag => (Table: table.Name, Tag: tag))).ToList();
        var theirTags = Rows(Path.Combine(folder, "tags.tsv")).ToList();
        Expect(problems, what, "HMI tag count", tags.Count.ToString(CultureInfo.InvariantCulture), theirTags.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var row in theirTags) {
            compared++;
            var (table, tag) = tags.FirstOrDefault(candidate => candidate.Tag.Name == row[1]);
            if (tag is null) {
                problems.Add($"{what}: HMI tag {row[1]} not read");
                continue;
            }
            var where = $"HMI tag {row[1]}";
            Expect(problems, what, where + " table", table, row[0]);
            Expect(problems, what, where + " connection", tag.Connection ?? "<Internal tag>", row[2]);
            Expect(problems, what, where + " PLC tag", tag.PlcLink is not { } link ? "" : link.IsDataBlock && link.Member is { Length: > 0 } member ? $"{link.Name}.{member}" : link.Name, row[3]);
            // TIA's API gives a string's length apart; the reader keeps it in the type, as stored: WString[254].
            Expect(problems, what, where + " data type", Length().Replace(tag.DataType ?? "", ""), row[4]);
            Expect(problems, what, where + " acquisition cycle", tag.AcquisitionCycle, row[5]);
        }
        var theirAlarms = Rows(Path.Combine(folder, "alarms.tsv")).ToList();
        Expect(problems, what, "alarm count", ours.Alarms.Count.ToString(CultureInfo.InvariantCulture), theirAlarms.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var row in theirAlarms) {
            compared++;
            var alarm = ours.Alarms.FirstOrDefault(candidate => candidate.Name == row[1]);
            if (alarm is null) {
                problems.Add($"{what}: alarm {row[1]} not read");
                continue;
            }
            var where = $"alarm {row[1]}";
            Expect(problems, what, where + " kind", alarm.Kind == TiaHmiAlarmKind.Discrete ? "discrete" : "analog", row[0]);
            Expect(problems, what, where + " number", Number(alarm.Number), row[2]);
            Expect(problems, what, where + " class", alarm.AlarmClass, row[3]);
            Expect(problems, what, where + " trigger tag", alarm.TriggerTag, row[4]);
            Expect(problems, what, where + (alarm.Kind == TiaHmiAlarmKind.Discrete ? " bit" : " limit mode"), alarm.Kind == TiaHmiAlarmKind.Discrete ? Number(alarm.Bit) : alarm.LimitMode, row[5]);
            Expect(problems, what, where + " limit", alarm.Limit ?? "", row[6]);
            Expect(problems, what, where + " text", alarm.Text, Markup().Replace(row[7], ""));
        }
        foreach (var row in Rows(Path.Combine(folder, "screens.tsv"))) {
            compared++;
            var screen = ours.Screens.FirstOrDefault(candidate => candidate.Name == row[0]);
            var item = screen?.Items.FirstOrDefault(candidate => candidate.Name == row[4]);
            if (screen is null || item is null) {
                problems.Add($"{what}: screen {row[0]} item {row[4]} not read");
                continue;
            }
            var where = $"screen {row[0]}";
            Expect(problems, what, where + " number, width, height", $"{Number(screen.Number)} {Number(screen.Width)} {Number(screen.Height)}", $"{row[1]} {row[2]} {row[3]}");
            Expect(problems, what, $"{where} item {row[4]}", $"{item.Type} {Number(item.Left)} {Number(item.Top)} {Number(item.Width)} {Number(item.Height)} {string.Join(",", item.Tags)}",
                $"{row[5]} {row[6]} {row[7]} {row[8]} {row[9]} {row[10]}");
        }
        return compared;
    }

    private static int Advanced(TiaHmiDevice ours, string folder, string what, List<string> problems) {
        var compared = 0;
        foreach (var file in Directory.GetFiles(folder, "TAGS.*.xml")) {
            var table = XDocument.Load(file).Descendants("Hmi.Tag.TagTable").Single();
            var tableName = Attribute(table, "Name");
            var theirTags = table.Descendants("Hmi.Tag.Tag").ToList();
            var mine = ours.TagTables.FirstOrDefault(candidate => candidate.Name == tableName);
            if (mine is null) {
                if (theirTags.Count != 0) {
                    problems.Add($"{what}: tag table {tableName} not read");
                }
                continue;
            }
            Expect(problems, what, $"tag table {tableName} tag count", mine.Tags.Count.ToString(CultureInfo.InvariantCulture), theirTags.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var their in theirTags) {
                compared++;
                var tagName = Attribute(their, "Name");
                var tag = mine.Tags.FirstOrDefault(candidate => candidate.Name == tagName);
                if (tag is null) {
                    problems.Add($"{what}: HMI tag {tagName} not read");
                    continue;
                }
                var where = $"HMI tag {tagName}";
                Expect(problems, what, where + " address", tag.Address ?? "", Attribute(their, "LogicalAddress"));
                Expect(problems, what, where + " connection", tag.Connection ?? "", Link(their, "Connection"));
                Expect(problems, what, where + " acquisition cycle", tag.Connection is null ? "" : tag.AcquisitionCycle, Link(their, "AcquisitionCycle"));
                Expect(problems, what, where + " data type", tag.DataType, Link(their, "DataType"));
            }
        }
        foreach (var file in Directory.GetFiles(folder, "SCREEN.*.xml")) {
            var their = XDocument.Load(file).Descendants("Hmi.Screen.Screen").Single();
            var screenName = Attribute(their, "Name");
            var screen = ours.Screens.FirstOrDefault(candidate => candidate.Name == screenName);
            if (screen is null) {
                problems.Add($"{what}: screen {screenName} not read");
                continue;
            }
            compared++;
            Expect(problems, what, $"screen {screenName} number, width, height", $"{Number(screen.Number)} {Number(screen.Width)} {Number(screen.Height)}",
                $"{Attribute(their, "Number")} {Attribute(their, "Width")} {Attribute(their, "Height")}");
            var items = their.Descendants().Where(element => (string?)element.Attribute("CompositionName") == "ScreenItems").ToList();
            Expect(problems, what, $"screen {screenName} item count", screen.Items.Count.ToString(CultureInfo.InvariantCulture), items.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var element in items) {
                compared++;
                var itemName = Attribute(element, "ObjectName");
                var item = screen.Items.FirstOrDefault(candidate => candidate.Name == itemName);
                if (item is null) {
                    problems.Add($"{what}: screen {screenName} item {itemName} not read");
                    continue;
                }
                Expect(problems, what, $"screen {screenName} item {itemName}", $"{item.Type} {Number(item.Left)} {Number(item.Top)} {Number(item.Width)} {Number(item.Height)}",
                    $"{element.Name.LocalName["Hmi.Screen.".Length..]} {Attribute(element, "Left")} {Attribute(element, "Top")} {Attribute(element, "Width")} {Attribute(element, "Height")}");
            }
        }
        return compared;
    }

    private static void Expect(List<string> problems, string what, string field, string? ours, string? theirs) {
        if ((ours ?? "") != (theirs ?? "")) {
            problems.Add($"{what}: {field}: ours `{ours}`, TIA's `{theirs}`");
        }
    }

    private static string Attribute(XElement element, string name) => element.Element("AttributeList")?.Element(name)?.Value ?? "";

    private static string Link(XElement element, string name) => element.Element("LinkList")?.Element(name)?.Element("Name")?.Value ?? "";

    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>The rows of a tab-separated file after its header.</summary>
    private static IEnumerable<string[]> Rows(string file) =>
        File.Exists(file) ? File.ReadLines(file).Skip(1).Where(line => line.Length != 0).Select(line => line.Split('\t')) : [];

    [GeneratedRegex(@"\[\d+\]$", RegexOptions.CultureInvariant)]
    private static partial Regex Length();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();
}
