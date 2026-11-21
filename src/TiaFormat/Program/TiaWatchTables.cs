using System.Text;
using System.Xml.Linq;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A watch table or a PLC's force table.</summary>
public enum TiaWatchTableKind : byte {
    Watch,
    Force,
}

/// <summary>
/// One line of a watch or force table, in TIA's order. Format and triggers carry the names TIA's export gives them
/// (<c>DEC_signed</c>, <c>OnceOnlyAtStart</c>). <see cref="Value"/> is the modify value of a watch table and the force
/// value of a force table; <see cref="ValueSelected"/> is the box beside it. A comment line has only a comment.
/// </summary>
public sealed record TiaWatchEntry(
    string? Name,
    string? Address,
    string? Format,
    string? MonitorTrigger,
    string? Value,
    bool ValueSelected,
    string? ModifyTrigger,
    string? Comment,
    bool IsCommentLine);

/// <summary>A watch table or the force table of a PLC, with its folder path below the PLC and its entries.</summary>
public sealed record TiaWatchTable(TiaFrame Frame, TiaWatchTableKind Kind, string Name, string Folder, IReadOnlyList<TiaWatchEntry> Entries);

/// <summary>Reads watch tables and force tables (spec 0015, <c>docs/FORMAT.md</c> section 20).</summary>
public static class TiaWatchTables {
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private static readonly XNamespace Iecpl = "http://www.siemens.com/automation/2010/IECPL";

    /// <summary>The stored display formats whose name differs from the one TIA's export and editor give.</summary>
    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal) {
        ["Dec_signed"] = "DEC_signed",
        ["Dec_unsigned"] = "DEC_unsigned",
        ["Dec_sequence"] = "DEC_sequence",
        ["Real"] = "Float",
    };

    /// <summary>The stored trigger names and the ones TIA's export gives.</summary>
    private static readonly Dictionary<string, string> Triggers = new(StringComparer.Ordinal) {
        ["Smart"] = "Permanent",
        ["BeginOnce"] = "OnceOnlyAtStart",
        ["BeginCyclic"] = "PermanentAtStart",
        ["EndOnce"] = "OnceOnlyAtEnd",
        ["EndCyclic"] = "PermanentAtEnd",
        ["StopOnce"] = "OnceOnlyAtStop",
        ["StopCyclic"] = "PermanentAtStop",
    };

    /// <summary>Every watch table and force table of the archive, in file order.</summary>
    public static List<TiaWatchTable> Read(TiaProgram program) {
        var db = program.Database;
        var s = db.Schema;
        var watch = s.FindObjectType("Siemens.Simatic.Lang.Model.Debugging.VarTableData")?.Id;
        var force = s.FindObjectType("Siemens.Simatic.Lang.Model.Debugging.VarTableForceData")?.Id;
        var result = new List<TiaWatchTable>();
        if (watch is null && force is null) {
            return result;
        }
        var name = s.Attribute(Core, "Name");
        var itemData = s.Attribute("Siemens.Simatic.Lang.Model.Debugging.IVarItemAttributes", "ItemData");
        var container = s.Relation("AggregatedVarItemContainer");
        var items = s.Relation("AggregatedVarItems");
        foreach (ref readonly var frame in db.Frames) {
            if (frame.TypeId != watch && frame.TypeId != force) {
                continue;
            }
            var entries = new List<TiaWatchEntry>();
            if (db.TryGetTarget(frame, container, out var holder)) {
                var comments = db.ReadSimpleExpandoTexts(holder);
                foreach (var relation in db.EnumerateRelations(holder)) {
                    if (items.Matches(relation.RelationId) && db.TryGetFrame(relation.Target, out var item)) {
                        using var data = db.ReadBlob(item, itemData);
                        Entries(data.Span, comments, entries);
                    }
                }
            }
            var folder = program.FolderPath(frame);
            // The top folder is the PLC's own "Watch and force tables"; the path below it is the user's.
            var slash = folder.IndexOf('/', StringComparison.Ordinal);
            result.Add(new TiaWatchTable(frame, frame.TypeId == force ? TiaWatchTableKind.Force : TiaWatchTableKind.Watch,
                db.ReadString(frame, name) ?? "", slash < 0 ? "" : folder[(slash + 1)..], entries));
        }
        return result;
    }

    private static void Entries(ReadOnlySpan<byte> xml, Dictionary<string, string> comments, List<TiaWatchEntry> into) {
        if (xml.IsEmpty) {
            return;
        }
        var text = Encoding.UTF8.GetString(xml).TrimStart('﻿');
        foreach (var line in XDocument.Parse(text).Descendants(Iecpl + "VarLine")) {
            var uid = (string?)line.Attribute("UId") ?? "";
            var comment = comments.GetValueOrDefault(uid);
            if ((string?)line.Attribute("IsCommentLine") == "True") {
                into.Add(new TiaWatchEntry(null, null, null, null, null, false, null, comment, true));
                continue;
            }
            var format = (string?)line.Attribute("Format");
            if (format is null) {
                // The empty line at the end of every table, where the editor takes the next entry.
                continue;
            }
            var monitor = line.Element(Iecpl + "MonitorValue");
            var user = line.Element(Iecpl + "UserValue");
            var value = user?.Value;
            into.Add(new TiaWatchEntry(
                Empty(line.Element(Iecpl + "Name")?.Value),
                Empty(line.Element(Iecpl + "Address")?.Value),
                Formats.GetValueOrDefault(format, format),
                Trigger((string?)monitor?.Attribute("TriggerName")),
                string.IsNullOrEmpty(value) ? null : value.StartsWith("10#", StringComparison.Ordinal) ? value[3..] : value,
                (string?)user?.Attribute("Checked") == "True",
                Trigger((string?)user?.Attribute("TriggerName")),
                comment,
                false));
        }
    }

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    /// <summary>A force table's value has the trigger <c>Now</c>, which is no trigger of TIA's export: none.</summary>
    private static string? Trigger(string? stored) =>
        stored is null or "Now" ? null : Triggers.GetValueOrDefault(stored, stored);

    /// <summary>A table as text, as <see cref="Write(TiaWatchTable, TextWriter)"/> writes it.</summary>
    public static string Text(TiaWatchTable table) {
        var output = new StringWriter { NewLine = "\n" };
        Write(table, output);
        return output.ToString();
    }

    /// <summary>
    /// A table as text, for reading rather than import: one entry per line, its name or address, the address in
    /// brackets after a name, its display format, its triggers and selection where they are not the default, its value
    /// and its comment. A comment line is a comment.
    /// </summary>
    public static void Write(TiaWatchTable table, TextWriter output) {
        output.Write("// ");
        output.Write(table.Kind == TiaWatchTableKind.Force ? "force table " : "watch table ");
        output.Write(table.Name);
        output.Write('\n');
        foreach (var entry in table.Entries) {
            if (entry.IsCommentLine) {
                output.Write(entry.Comment is null ? "//\n" : "// " + entry.Comment + "\n");
                continue;
            }
            var line = new StringBuilder();
            line.Append(entry.Name ?? entry.Address ?? "");
            if (entry.Name is not null && entry.Address is not null) {
                line.Append(" AT ").Append(entry.Address);
            }
            line.Append(" : ").Append(entry.Format);
            var settings = new List<string>();
            if (entry.MonitorTrigger is { } monitor and not "Permanent") {
                settings.Add($"MonitorTrigger := '{monitor}'");
            }
            if (table.Kind == TiaWatchTableKind.Watch && entry.ModifyTrigger is { } modify and not "Permanent") {
                settings.Add($"ModifyTrigger := '{modify}'");
            }
            if (entry.ValueSelected) {
                settings.Add(table.Kind == TiaWatchTableKind.Force ? "Force := 'TRUE'" : "Modify := 'TRUE'");
            }
            if (settings.Count != 0) {
                line.Append(" { ").AppendJoin("; ", settings).Append(" }");
            }
            if (entry.Value is not null) {
                line.Append(" := ").Append(entry.Value);
            }
            line.Append(';');
            if (entry.Comment is not null) {
                line.Append("   // ").Append(entry.Comment);
            }
            output.Write(line.Append('\n').ToString());
        }
    }
}
