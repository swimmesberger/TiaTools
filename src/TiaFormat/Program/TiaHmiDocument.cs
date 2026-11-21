using System.Globalization;
using System.Text;

namespace TiaFormat.Program;

/// <summary>
/// The text the viewer shows for an HMI device's entry (spec 0011): its connections, a tag table, its alarms or a screen,
/// as aligned columns under a comment line. A tag's PLC tag or data block is a name that leads there.
/// </summary>
internal static class TiaHmiDocument {
    /// <param name="scopes">The scope of each PLC of the project by its name, for the names that lead into it.</param>
    public static TiaDocument Build(TiaHmiDevice device, TiaHmiPart part, string name, IReadOnlyDictionary<string, string> scopes) {
        var writer = new Writer();
        switch (part) {
            case TiaHmiPart.Connections:
                writer.Comment($"HMI connections of {device.Name}, {device.FamilyName}{(device.DeviceType is { Length: > 0 } type ? ", " + type : "")}");
                writer.Table(["Connection", "PLC", "Driver"], device.Connections.Select(connection => new Cell[] { connection.Name, connection.Plc, connection.DriverName }));
                break;
            case TiaHmiPart.Alarms:
                writer.Comment($"HMI alarms of {device.Name}");
                writer.Table(["Kind", "Number", "Alarm", "Class", "Trigger tag", "Raised by", "Text"], device.Alarms.Select(alarm => new Cell[] {
                    alarm.Kind == TiaHmiAlarmKind.Discrete ? "discrete" : "analog", alarm.Number?.ToString(CultureInfo.InvariantCulture), alarm.Name,
                    alarm.AlarmClass, alarm.TriggerTag, TiaHmiWriter.RaisedBy(alarm), alarm.Text,
                }));
                break;
            case TiaHmiPart.TagTable when device.TagTables.FirstOrDefault(table => table.Name == name) is { } table:
                writer.Comment($"HMI tag table {table.Name} of {device.Name}");
                writer.Table(["HMI tag", "Type", "Connection", "Cycle", "Reads"], table.Tags.Select(tag => new Cell[] {
                    tag.Name, tag.DataType, tag.Connection ?? "internal", tag.AcquisitionCycle, Reads(tag, scopes),
                }));
                break;
            case TiaHmiPart.Screen when device.Screens.FirstOrDefault(screen => screen.Name == name) is { } screen:
                var size = screen.Width is { } width && screen.Height is { } height ? string.Create(CultureInfo.InvariantCulture, $", {width} x {height}") : "";
                writer.Comment($"Screen {(screen.Number is { } number ? number.ToString(CultureInfo.InvariantCulture) + " " : "")}{screen.Name} of {device.Name}{size}");
                writer.Table(["Item", "Type", "Left", "Top", "Width", "Height", "HMI tags"], screen.Items.Select(item => new Cell[] {
                    item.Name, item.Type, Number(item.Left), Number(item.Top), Number(item.Width), Number(item.Height), string.Join(", ", item.Tags),
                }));
                break;
        }
        return writer.Done();
    }

    /// <summary>What a tag reads; a PLC tag or data block in a PLC of the project leads there.</summary>
    private static Cell Reads(TiaHmiTag tag, IReadOnlyDictionary<string, string> scopes) {
        if (tag.PlcLink is not { } link) {
            return tag.Address;
        }
        var target = link.Plc is { } plc && scopes.TryGetValue(plc, out var scope) ? scope + TiaProject.ScopeSeparator + link.Name : null;
        return new Cell(link.Display, target, Length: link.Name.Length + 2);
    }

    private static string? Number(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>A cell's text; with a target, its first <paramref name="Length"/> characters are a name that leads there.</summary>
    private readonly record struct Cell(string? Text, string? Target = null, int Length = 0) {
        public static implicit operator Cell(string? text) => new(text);
    }

    private sealed class Writer {
        private readonly StringBuilder _text = new();
        private readonly List<TiaToken> _tokens = [];

        public void Comment(string line) {
            _tokens.Add(new TiaToken(_text.Length, line.Length + 3, TiaTokenKind.Comment));
            _text.Append("// ").Append(line).Append('\n').Append('\n');
        }

        public void Table(string[] header, IEnumerable<Cell[]> rows) {
            var all = rows.ToList();
            if (all.Count == 0) {
                _text.Append("None.\n");
                return;
            }
            var widths = header.Select((title, column) => Math.Max(title.Length, all.Max(row => (row[column].Text ?? "").Length))).ToArray();
            Line(header.Select(title => new Cell(title)).ToArray(), widths, isHeader: true);
            foreach (var row in all) {
                Line(row, widths, isHeader: false);
            }
        }

        private void Line(Cell[] cells, int[] widths, bool isHeader) {
            for (var i = 0; i < cells.Length; i++) {
                var text = cells[i].Text ?? "";
                if (isHeader) {
                    _tokens.Add(new TiaToken(_text.Length, text.Length, TiaTokenKind.Keyword));
                } else if (cells[i].Target is { } target) {
                    _tokens.Add(new TiaToken(_text.Length, Math.Min(cells[i].Length, text.Length), TiaTokenKind.Global, target));
                }
                _text.Append(text);
                if (i + 1 < cells.Length) {
                    _text.Append(' ', widths[i] - text.Length + 2);
                }
            }
            _text.Append('\n');
        }

        public TiaDocument Done() => new(_text.ToString().TrimEnd('\n') + "\n", _tokens);
    }
}
