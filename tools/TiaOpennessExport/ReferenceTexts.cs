using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Alarm;

namespace TiaOpennessExport;

/// <summary>
/// The texts of a reference project in several project languages (spec 0014): its languages, block titles and comments,
/// tag comments, PLC text lists and program alarm texts, built from its sources, and TIA's own view of them exported
/// beside the generated source. Openness takes text lists and alarm texts only as xlsx, so the sources, kept as text,
/// are written into the layouts TIA's own export uses, which it reads back.
/// </summary>
internal static class ReferenceTexts {
    public const string LanguagesFile = "languages.txt";

    private const string Inherited = "### Inherited from Type ###";

    /// <summary>Sets the project languages languages.txt names; returns false if the project has no such file.</summary>
    public static bool SetLanguages(Project project, string sources) {
        var file = Path.Combine(sources, LanguagesFile);
        if (!File.Exists(file)) {
            return false;
        }
        var settings = project.LanguageSettings;
        foreach (var line in Lines(file)) {
            var parts = line.Split(' ');
            var language = settings.Languages.Find(new CultureInfo(parts[1])) ?? throw new InvalidOperationException($"TIA does not know the language {parts[1]}.");
            if (!settings.ActiveLanguages.Contains(language)) {
                settings.ActiveLanguages.Add(language);
            }
            switch (parts[0]) {
                case "editing":
                    settings.EditingLanguage = language;
                    break;
                case "reference":
                    settings.ReferenceLanguage = language;
                    break;
                case "active":
                    break;
                default:
                    throw new InvalidOperationException($"{file}: '{parts[0]}' is none of editing, reference, active.");
            }
        }
        Console.WriteLine("languages " + string.Join(", ", settings.ActiveLanguages.Select(l => l.Culture.Name)) + "; editing " + settings.EditingLanguage.Culture.Name + ", reference " + settings.ReferenceLanguage.Culture.Name);
        return true;
    }

    /// <summary>Sets a multilingual text from fields of the form culture=text; a field without a culture sets the first item, as before.</summary>
    public static void SetText(MultilingualText text, IEnumerable<string> fields) {
        foreach (var field in fields) {
            var culture = Culture(field, out var value);
            var item = culture is null ? text.Items.First() : text.Items.FirstOrDefault(i => i.Language.Culture.Name == culture)
                ?? throw new InvalidOperationException($"{culture} is no project language.");
            item.Text = value;
        }
    }

    /// <summary>block texts.tsv: a block's title or comment per language, set before the compile.</summary>
    public static void SetBlockTexts(PlcSoftware plc, string sources, Func<string, Siemens.Engineering.SW.Blocks.PlcBlock> find) {
        var file = Path.Combine(sources, "block texts.tsv");
        foreach (var line in File.Exists(file) ? Lines(file) : Enumerable.Empty<string>()) {
            var fields = line.Split('\t');
            var block = find(fields[0]) ?? throw new InvalidOperationException($"block texts.tsv names {fields[0]}, which {plc.Name} does not have.");
            SetText(fields[1] switch {
                "Title" => block.Title,
                "Comment" => block.Comment,
                _ => throw new InvalidOperationException($"block texts.tsv: '{fields[1]}' is neither Title nor Comment."),
            }, fields.Skip(2));
            Console.WriteLine($"texts of {plc.Name}/{fields[0]}: {fields[1]}");
        }
    }

    /// <summary>Imports the PLC's text lists; returns how many imports TIA refused.</summary>
    public static int ImportTextLists(PlcSoftware plc, string sources, string work, IReadOnlyList<string> cultures) {
        var folder = Path.Combine(sources, "PLC text lists");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).ToList() : [];
        if (files.Count == 0) {
            return 0;
        }
        var lists = new List<string[]> { new[] { "Name", "ListRange" }.Concat(cultures.Select(c => $"Comment [{c}]")).ToArray() };
        var entries = new List<string[]> { new[] { "Parent", "From", "To" }.Concat(cultures.Select(c => $"Text [{c}]")).ToArray() };
        foreach (var file in files) {
            var name = Path.GetFileNameWithoutExtension(file);
            string range = null;
            var comment = new Dictionary<string, string>();
            foreach (var line in Lines(file)) {
                var fields = line.Split('\t');
                switch (fields[0]) {
                    case "ListRange":
                        range = fields[1];
                        break;
                    case "Comment":
                        comment = ByCulture(fields.Skip(1));
                        break;
                    default:
                        var texts = ByCulture(fields.Skip(2));
                        entries.Add(new[] { name, fields[0], fields[1] }.Concat(cultures.Select(c => texts.TryGetValue(c, out var t) ? t : "")).ToArray());
                        break;
                }
            }
            lists.Add(new[] { name, range ?? throw new InvalidOperationException($"{file} has no ListRange.") }.Concat(cultures.Select(c => comment.TryGetValue(c, out var t) ? t : "")).ToArray());
        }
        var xlsx = Path.Combine(work, "import", plc.Name, "textlists.xlsx");
        Xlsx.Write(xlsx, [("TextList", lists), ("TextListEntry", entries)], ("FileVersion", "1"), ("FileContent", "Alarm text lists"));
        var result = plc.GetService<PlcAlarmTextListProvider>().ImportFromXlsx(new FileInfo(xlsx), ImportOptions.Override);
        return Report(plc, "text lists", result.State.ToString(), result.LogFilePath?.FullName);
    }

    /// <summary>Imports the instance texts of the PLC's program alarms; returns how many imports TIA refused.</summary>
    public static int ImportAlarmTexts(PlcSoftware plc, string sources, string work, IReadOnlyList<string> cultures) {
        var file = Path.Combine(sources, "PLC alarms", "alarms.tsv");
        if (!File.Exists(file)) {
            return 0;
        }
        var lines = Lines(file).Select(line => line.Split('\t')).ToList();
        var columns = lines[0];
        // TIA's columns: Location, Alarm name, Alarm class, then for each text one column per language and its FieldInfo.
        var kinds = new[] { ("Alarm text", "Event text") }.Concat(new[] { "Info text" }.Concat(Enumerable.Range(1, 9).Select(i => $"Additional text {i}")).Select(t => (t, t))).ToList();
        var header = new List<string> { "Location", "Alarm name", "Alarm class" };
        foreach (var (text, column) in kinds) {
            header.AddRange(cultures.Select(c => $"\"{text}\" - {new CultureInfo(c).EnglishName} / [{c}] / {column}"));
            header.Add($"FieldInfo / \"{text}\" / {column}");
        }
        var rows = new List<string[]> { header.ToArray() };
        foreach (var fields in lines.Skip(1)) {
            string Cell(string name) {
                var index = Array.IndexOf(columns, name);
                return index < 0 || index >= fields.Length || fields[index].Length == 0 ? Inherited : fields[index];
            }
            var row = new List<string> { fields[0], fields[1], Cell("Alarm class") };
            foreach (var (text, _) in kinds) {
                row.AddRange(cultures.Select(c => Cell(text + " " + c)));
                row.Add("");
            }
            rows.Add(row.ToArray());
        }
        var xlsx = Path.Combine(work, "import", plc.Name, "alarms.xlsx");
        Xlsx.Write(xlsx, [("Instance alarms", rows)], ("TIA_Version", "2.1"), ("FileContent", "Alarm instances"));
        var result = plc.GetService<PlcAlarmTextProvider>().ImportInstanceTextsFromXlsx(new FileInfo(xlsx), null);
        return Report(plc, "alarm texts", result.State.ToString(), result.LogFilePath?.FullName);
    }

    /// <summary>TIA's view of the project's languages and alarm classes, into the export's root.</summary>
    public static void ExportProject(Project project, string output, Action<string, Action> attempt) {
        var settings = project.LanguageSettings;
        attempt("languages", () => File.WriteAllLines(Path.Combine(output, LanguagesFile),
            new[] { "editing " + settings.EditingLanguage.Culture.Name, "reference " + settings.ReferenceLanguage.Culture.Name }
                .Concat(settings.ActiveLanguages.Select(l => "active " + l.Culture.Name)), new UTF8Encoding(false)));
        attempt("alarm classes", () => {
            var result = project.GetService<AlarmClassDataProvider>().Export(new FileInfo(Path.Combine(output, "alarmclasses.dat")));
            if (result.State == AlarmClassExportImportResultState.Error) {
                throw new IOException(string.Join(" | ", result.Messages.Select(m => m.Message)));
            }
        });
    }

    /// <summary>TIA's view of a PLC's text lists and alarm instance texts, into the PLC's folder; a PLC with none has no file.</summary>
    public static void ExportPlc(PlcSoftware plc, string folder, Action<string, Action> attempt) {
        if (plc.PlcAlarmTextlistGroup.PlcAlarmUserTextlists.Count != 0) {
            attempt("text lists", () => plc.GetService<PlcAlarmTextListProvider>().ExportToXlsx(new FileInfo(Path.Combine(folder, "textlists.xlsx"))));
        }
        attempt("alarm texts", () => plc.GetService<PlcAlarmTextProvider>().ExportInstanceTextsToXlsx(new FileInfo(Path.Combine(folder, "alarms.xlsx")), null, PlcAlarmTextXlsxExportOption.All));
    }

    private static int Report(PlcSoftware plc, string what, string state, string log) {
        Console.WriteLine($"imported {plc.Name} {what}: {state}");
        if (state == "OK") {
            return 0;
        }
        // TIA's log says which row and column it did not take; the build counts it as a refused source.
        Console.WriteLine(File.Exists(log) ? File.ReadAllText(log) : $"no log at {log}");
        return 1;
    }

    private static Dictionary<string, string> ByCulture(IEnumerable<string> fields) =>
        fields.Where(f => f.Length != 0).ToDictionary(f => Culture(f, out _) ?? throw new InvalidOperationException($"'{f}' names no culture."), f => {
            Culture(f, out var value);
            return value;
        });

    /// <summary>The culture of a field culture=text, such as de-DE=Ventil, or null for a plain text.</summary>
    private static string Culture(string field, out string text) {
        var equals = field.IndexOf('=');
        if (equals == 5 && field[2] == '-' && char.IsLower(field[0]) && char.IsUpper(field[3])) {
            text = field.Substring(6);
            return field.Substring(0, 5);
        }
        text = field;
        return null;
    }

    private static IEnumerable<string> Lines(string file) =>
        File.ReadAllLines(file, Encoding.UTF8).Where(line => line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal));
}

/// <summary>
/// The smallest xlsx TIA reads: shared strings, a styles part (TIA's import fails without one) and TIA's custom
/// properties, which say what the file holds.
/// </summary>
internal static class Xlsx {
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Write(string path, IReadOnlyList<(string Name, List<string[]> Rows)> sheets, params (string Name, string Value)[] properties) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var strings = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string text) {
            if (!index.TryGetValue(text, out var id)) {
                index[text] = id = strings.Count;
                strings.Add(text);
            }
            return id;
        }
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var types = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
            + "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
            + "<Override PartName=\"/docProps/custom.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.custom-properties+xml\"/>");
        var workbook = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?><workbook xmlns=\"{Main}\" xmlns:r=\"{Relations}\"><sheets>");
        var workbookRelations = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + $"<Relationship Id=\"Styles\" Type=\"{Relations}/styles\" Target=\"/xl/styles.xml\"/><Relationship Id=\"Strings\" Type=\"{Relations}/sharedStrings\" Target=\"/xl/sharedStrings.xml\"/>");
        for (var s = 0; s < sheets.Count; s++) {
            var number = s + 1;
            types.Append($"<Override PartName=\"/xl/worksheets/sheet{number}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            workbook.Append($"<sheet name=\"{SecurityElement.Escape(sheets[s].Name)}\" sheetId=\"{number}\" r:id=\"Sheet{number}\"/>");
            workbookRelations.Append($"<Relationship Id=\"Sheet{number}\" Type=\"{Relations}/worksheet\" Target=\"/xl/worksheets/sheet{number}.xml\"/>");
            var sheet = new StringBuilder($"<?xml version=\"1.0\" encoding=\"utf-8\"?><worksheet xmlns=\"{Main}\"><sheetData>");
            for (var r = 0; r < sheets[s].Rows.Count; r++) {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < sheets[s].Rows[r].Length; c++) {
                    sheet.Append($"<c r=\"{Column(c)}{r + 1}\" s=\"0\" t=\"s\"><v>{Id(sheets[s].Rows[r][c])}</v></c>");
                }
                sheet.Append("</row>");
            }
            Entry(zip, $"xl/worksheets/sheet{number}.xml", sheet.Append("</sheetData></worksheet>").ToString());
        }
        Entry(zip, "[Content_Types].xml", types.Append("</Types>").ToString());
        Entry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + $"<Relationship Id=\"Book\" Type=\"{Relations}/officeDocument\" Target=\"/xl/workbook.xml\"/>"
            + $"<Relationship Id=\"Custom\" Type=\"{Relations}/custom-properties\" Target=\"/docProps/custom.xml\"/></Relationships>");
        Entry(zip, "xl/workbook.xml", workbook.Append("</sheets></workbook>").ToString());
        Entry(zip, "xl/_rels/workbook.xml.rels", workbookRelations.Append("</Relationships>").ToString());
        Entry(zip, "xl/sharedStrings.xml", $"<?xml version=\"1.0\" encoding=\"utf-8\"?><sst xmlns=\"{Main}\">"
            + string.Concat(strings.Select(text => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(text)}</t></si>")) + "</sst>");
        // The style sheet TIA's own export writes.
        Entry(zip, "xl/styles.xml", $"<?xml version=\"1.0\" encoding=\"utf-8\"?><styleSheet xmlns=\"{Main}\"><numFmts count=\"0\"/><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
            + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
            + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
            + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles><dxfs count=\"0\"/><tableStyles count=\"0\"/></styleSheet>");
        Entry(zip, "docProps/custom.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><op:Properties xmlns:op=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\">"
            + string.Concat(properties.Select((p, i) => $"<op:property fmtid=\"{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}\" pid=\"{i + 2}\" name=\"{p.Name}\">"
                + $"<vt:lpwstr xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">{SecurityElement.Escape(p.Value)}</vt:lpwstr></op:property>"))
            + "</op:Properties>");
    }

    private static void Entry(ZipArchive zip, string name, string content) {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Column(int index) {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) {
            name = (char)('A' + ((index - 1) % 26)) + name;
        }
        return name;
    }
}
