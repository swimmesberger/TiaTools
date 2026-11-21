using System.Globalization;
using System.Text;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A row of the texts table: a text of a PLC, what holds it, its place there, and its text in every language stored.</summary>
/// <param name="Plc">The PLC's name; empty for a text of the whole project, such as an alarm class's name.</param>
public sealed record TiaTextRow(string Plc, string Object, string Place, IReadOnlyList<TiaText> Texts);

/// <summary>
/// The texts table and the tables of alarms and text lists (spec 0014), one each per PLC: tab-separated, a column per
/// project language, each cell what is stored in that language. A tab, a line break or a backslash in a text is
/// written as <c>\t</c>, <c>\n</c>, <c>\r</c> or <c>\\</c>, so that one row stays one line.
/// </summary>
public static class TiaTexts {
    /// <summary>The LCID of a text that holds for every language, such as an alarm class's display name.</summary>
    public const int Neutral = 65535;

    /// <summary>
    /// A text in one language: as stored in it, or the language-neutral text if it stores none for that language, else
    /// empty. With no language, the first text stored. No other language stands in: that is the fallback of the sources.
    /// </summary>
    public static string In(IReadOnlyList<TiaText> texts, TiaLanguage? language) {
        if (language is null) {
            return texts.FirstOrDefault(text => text.Text.Length != 0).Text ?? "";
        }
        foreach (var text in texts) {
            if (text.Lcid == language.Lcid) {
                return text.Text.Length != 0 ? text.Text : NeutralText(texts);
            }
        }
        return NeutralText(texts);
    }

    /// <summary>
    /// Every text of the project's PLCs: block titles and comments, network titles and comments, member comments, tag
    /// comments, text list comments and entries, program alarm texts and alarm class names. Blocks and alarms that are
    /// closed are left out, and an instance alarm's texts only where it stores its own.
    /// </summary>
    public static IReadOnlyList<TiaTextRow> Rows(TiaProgram program, TiaAlarms alarms) {
        var rows = new List<TiaTextRow>();
        foreach (var block in program.Blocks.Where(block => !block.IsStub && !block.IsKnowHowProtected && program.Environment(block.Frame).Kind == "ProjectData")
                     .OrderBy(block => program.PlcName(block.Frame), StringComparer.Ordinal).ThenBy(block => program.FolderPath(block.Frame), StringComparer.Ordinal)
                     .ThenBy(block => block.Name, StringComparer.Ordinal)) {
            var plc = program.PlcName(block.Frame) ?? "";
            rows.AddRange(program.BlockTexts(block).Select(text => new TiaTextRow(plc, $"{block.Kind} {block.Name}", text.Place, text.Texts)));
        }
        foreach (var table in program.ReadTagTables().Where(table => program.Environment(table.Frame).Kind == "ProjectData")) {
            var plc = program.PlcName(table.Frame) ?? "";
            rows.AddRange(program.TagTexts(table).Select(text => new TiaTextRow(plc, "tag table " + table.Name, text.Place, text.Texts)));
        }
        foreach (var list in alarms.TextLists()) {
            rows.Add(new TiaTextRow(list.Plc, "text list " + list.Name, "comment", list.Comment));
            rows.AddRange(list.Entries.Select(entry => new TiaTextRow(list.Plc, "text list " + list.Name, "entry " + Values(entry), entry.Texts)));
        }
        foreach (var alarm in alarms.Alarms().Where(alarm => !alarm.IsClosed)) {
            rows.AddRange(alarm.Texts.Where(text => !text.Inherited).Select(text => new TiaTextRow(alarm.Plc, "alarm " + alarm.Path, text.Kind, text.Texts)));
        }
        rows.AddRange(alarms.Classes().Select(alarmClass => new TiaTextRow("", "alarm class " + alarmClass.Name, "display name", alarmClass.DisplayName)));
        return [.. rows.Where(row => row.Texts.Any(text => text.Text.Length != 0))];
    }

    /// <summary>A PLC's texts table: object, place, then a column per project language; with the project's texts too.</summary>
    public static void WriteTexts(IEnumerable<TiaTextRow> rows, string plc, TiaLanguages languages, TextWriter output) {
        var columns = Columns(languages);
        output.Write("object\tplace");
        WriteHeader(columns, "", output);
        foreach (var row in rows.Where(row => row.Plc == plc || row.Plc.Length == 0)) {
            output.Write(Escape(row.Object) + "\t" + Escape(row.Place));
            WriteCells(row.Texts, columns, output);
        }
    }

    /// <summary>
    /// A PLC's program alarms: each type and each instance, with where it is declared, its class, priority, associated
    /// values and text lists, then each kind of text any alarm has, a column per language. An instance's text, class and
    /// priority are its type's where it stores none. A closed alarm has its place and nothing more.
    /// </summary>
    public static void WriteAlarms(IReadOnlyList<TiaAlarm> alarms, TiaLanguages languages, TextWriter output) {
        var columns = Columns(languages);
        var kinds = TiaAlarms.TextKinds.Where(kind => alarms.Any(alarm => alarm.Texts.Any(text => text.Kind == kind && text.Texts.Any(t => t.Text.Length != 0)))).ToList();
        output.Write("alarm\tblock\tmember\tinstance\tstatus\talarm id\tclass\tpriority\tassociated values\ttext lists");
        foreach (var kind in kinds) {
            foreach (var column in columns) {
                output.Write("\t" + kind.ToLowerInvariant() + (column is null ? "" : " " + column.Name));
            }
        }
        output.Write('\n');
        foreach (var alarm in alarms) {
            output.Write(string.Join('\t',
                Escape(alarm.Path), Escape(alarm.Block), Escape(alarm.Name), Escape(alarm.Instance ?? ""), alarm.IsClosed ? "closed" : alarm.Instance is null ? "type" : "instance",
                alarm.AlarmId?.ToString(CultureInfo.InvariantCulture) ?? "", Escape(alarm.Class ?? ""), alarm.Priority?.ToString(CultureInfo.InvariantCulture) ?? "",
                string.Join("; ", alarm.AssociatedValues.Select(value => string.Create(CultureInfo.InvariantCulture, $"SD_{value.Number} = {Escape(value.Operand)}"))),
                string.Join("; ", alarm.TextLists.Select(Escape))));
            foreach (var kind in kinds) {
                var texts = alarm.Texts.FirstOrDefault(text => text.Kind == kind)?.Texts ?? [];
                foreach (var column in columns) {
                    output.Write("\t" + Escape(In(texts, column)));
                }
            }
            output.Write('\n');
        }
    }

    /// <summary>A PLC's text lists: for each list a row with its comment, then a row per entry with its values, each with a column per language and the alarms that use the list.</summary>
    public static void WriteTextLists(IReadOnlyList<TiaTextList> lists, TiaLanguages languages, TextWriter output) {
        var columns = Columns(languages);
        output.Write("text list\trange\tentry");
        WriteHeader(columns, "", output, "\tused by");
        foreach (var list in lists) {
            var usedBy = string.Join("; ", list.UsedBy.Select(Escape));
            output.Write($"{Escape(list.Name)}\t{list.Range}\tcomment");
            WriteCells(list.Comment, columns, output, "\t" + usedBy);
            foreach (var entry in list.Entries) {
                output.Write($"{Escape(list.Name)}\t{list.Range}\t{Values(entry)}");
                WriteCells(entry.Texts, columns, output, "\t" + usedBy);
            }
        }
    }

    /// <summary>
    /// A text in one language for reading: the first of <paramref name="preference"/>'s languages that has text, else
    /// the language-neutral text, else the first language that has any. The rule titles and comments follow (FR-004).
    /// </summary>
    public static string Pick(IReadOnlyList<TiaText> texts, IReadOnlyList<int> preference) {
        foreach (var lcid in preference) {
            foreach (var text in texts) {
                if (text.Lcid == lcid && text.Text.Length != 0) {
                    return text.Text;
                }
            }
        }
        var neutral = NeutralText(texts);
        return neutral.Length != 0 ? neutral : texts.FirstOrDefault(text => text.Text.Length != 0).Text ?? "";
    }

    /// <summary>A PLC's alarms as a table to read, in one language: what the viewer shows (FR-017).</summary>
    public static void ShowAlarms(IReadOnlyList<TiaAlarm> alarms, IReadOnlyList<int> preference, TextWriter output) {
        var kinds = TiaAlarms.TextKinds.Where(kind => alarms.Any(alarm => alarm.Texts.Any(text => text.Kind == kind && text.Texts.Any(t => t.Text.Length != 0)))).ToList();
        List<string[]> rows = [["Alarm", "Status", "Id", "Class", "Associated values", "Text lists", .. kinds]];
        rows.AddRange(alarms.Select(alarm => (string[])[
            alarm.Path, alarm.IsClosed ? "closed" : alarm.Instance is null ? "type" : "instance", alarm.AlarmId?.ToString(CultureInfo.InvariantCulture) ?? "", alarm.Class ?? "",
            string.Join(", ", alarm.AssociatedValues.Select(value => string.Create(CultureInfo.InvariantCulture, $"SD_{value.Number} = {value.Operand}"))),
            string.Join(", ", alarm.TextLists),
            .. kinds.Select(kind => Pick(alarm.Texts.FirstOrDefault(text => text.Kind == kind)?.Texts ?? [], preference)),
        ]));
        WriteAligned(rows, output);
    }

    /// <summary>A PLC's text lists as a table to read, in one language: what the viewer shows (FR-017).</summary>
    public static void ShowTextLists(IReadOnlyList<TiaTextList> lists, IReadOnlyList<int> preference, TextWriter output) {
        List<string[]> rows = [["Text list", "Range", "Entry", "Text", "Used by"]];
        foreach (var list in lists) {
            var usedBy = string.Join(", ", list.UsedBy);
            rows.Add([list.Name, list.Range, "comment", Pick(list.Comment, preference), usedBy]);
            rows.AddRange(list.Entries.Select(entry => (string[])[list.Name, list.Range, Values(entry), Pick(entry.Texts, preference), usedBy]));
        }
        WriteAligned(rows, output);
    }

    /// <summary>Rows in columns two spaces apart, each line break in a text shown as a space, so a row stays one line.</summary>
    private static void WriteAligned(List<string[]> rows, TextWriter output) {
        var cells = rows.Select(row => row.Select(cell => cell.ReplaceLineEndings(" ")).ToArray()).ToList();
        var widths = Enumerable.Range(0, cells[0].Length).Select(column => cells.Max(row => row[column].Length)).ToArray();
        foreach (var row in cells) {
            output.Write(string.Join("  ", row.Select((cell, column) => column == row.Length - 1 ? cell : cell.PadRight(widths[column]))).TrimEnd() + "\n");
        }
    }

    /// <summary>A text made fit for one cell of a tab-separated row.</summary>
    public static string Escape(string text) {
        if (text.AsSpan().IndexOfAny("\\\t\n\r") < 0) {
            return text;
        }
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text) {
            builder.Append(c switch {
                '\\' => "\\\\",
                '\t' => "\\t",
                '\n' => "\\n",
                '\r' => "\\r",
                _ => c.ToString(),
            });
        }
        return builder.ToString();
    }

    private static string Values(TiaTextListEntry entry) =>
        entry.From == entry.To ? entry.From.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{entry.From}..{entry.To}");

    private static string NeutralText(IReadOnlyList<TiaText> texts) => texts.FirstOrDefault(text => text.Lcid == Neutral).Text ?? "";

    /// <summary>The language columns: the project languages, or one column for a file that names none.</summary>
    private static IReadOnlyList<TiaLanguage?> Columns(TiaLanguages languages) => languages.All.Count == 0 ? [null] : [.. languages.All];

    private static void WriteHeader(IReadOnlyList<TiaLanguage?> columns, string prefix, TextWriter output, string after = "") {
        foreach (var column in columns) {
            output.Write("\t" + prefix + (column?.Name ?? "text"));
        }
        output.Write(after + "\n");
    }

    private static void WriteCells(IReadOnlyList<TiaText> texts, IReadOnlyList<TiaLanguage?> columns, TextWriter output, string after = "") {
        foreach (var column in columns) {
            output.Write("\t" + Escape(In(texts, column)));
        }
        output.Write(after + "\n");
    }
}
