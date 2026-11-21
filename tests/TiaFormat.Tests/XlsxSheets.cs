using System.IO.Compression;
using System.Xml.Linq;

namespace TiaFormat.Tests;

/// <summary>
/// Reads the sheets of an xlsx file as rows of strings, enough for TIA's exports of text lists and alarm texts (spec
/// 0014). A cell left out of a row reads as empty.
/// </summary>
internal static class XlsxSheets {
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelations = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Each sheet by its name, as rows of cells from column A on.</summary>
    public static Dictionary<string, List<string[]>> Read(string path) {
        using var zip = ZipFile.OpenRead(path);
        var shared = zip.GetEntry("xl/sharedStrings.xml") is { } strings
            ? Load(strings).Root!.Elements(Main + "si").Select(item => string.Concat(item.Descendants(Main + "t").Select(text => text.Value))).ToList()
            : [];
        var targets = Load(zip.GetEntry("xl/_rels/workbook.xml.rels")!).Root!.Elements(PackageRelations + "Relationship")
            .ToDictionary(relation => (string)relation.Attribute("Id")!, relation => ((string)relation.Attribute("Target")!).TrimStart('/'));
        var result = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var sheet in Load(zip.GetEntry("xl/workbook.xml")!).Root!.Descendants(Main + "sheet")) {
            var target = targets[(string)sheet.Attribute(Relations + "id")!];
            var entry = zip.GetEntry(target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target)!;
            result[(string)sheet.Attribute("name")!] = [.. Load(entry).Descendants(Main + "row").Select(row => Row(row, shared))];
        }
        return result;
    }

    private static string[] Row(XElement row, List<string> shared) {
        var cells = new SortedDictionary<int, string>();
        foreach (var cell in row.Elements(Main + "c")) {
            var value = cell.Element(Main + "v")?.Value;
            var text = (string?)cell.Attribute("t") switch {
                "s" when value is not null => shared[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)],
                "inlineStr" => string.Concat(cell.Descendants(Main + "t").Select(t => t.Value)),
                _ => value ?? "",
            };
            cells[Column((string)cell.Attribute("r")!)] = text;
        }
        var result = new string[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
        Array.Fill(result, "");
        foreach (var (column, text) in cells) {
            result[column] = text;
        }
        return result;
    }

    /// <summary>The zero-based column of a cell reference such as AB12.</summary>
    private static int Column(string reference) {
        var column = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter)) {
            column = (column * 26) + (c - 'A' + 1);
        }
        return column - 1;
    }

    private static XDocument Load(ZipArchiveEntry entry) {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
