using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>
/// An HMI device's entry as a table (spec 0011): its connections, the HMI tags of a tag table, its HMI alarms, or the
/// items of a screen, which a schematic preview draws above them; or a custom web control's files (spec 0017). A row
/// whose HMI tag reads a PLC tag or data block member opens it. Read-only; built in code, without bindings, so it needs
/// no reflection. The text <c>tiadump hmi</c> writes stays behind Copy.
/// </summary>
public sealed class HmiView : UserControl {
    private const double RowInset = 20;

    /// <summary>An HMI tag's PLC link the person asked to open.</summary>
    public event Action<TiaHmiPlcLink>? Open;

    /// <summary>A script of the device the person asked to open, by its position in <see cref="TiaHmiCode.Scripts"/>.</summary>
    public event Action<int>? OpenScript;

    /// <summary>Something to say in the status line.</summary>
    public event Action<string>? Said;

    /// <summary>
    /// Shows one entry of <paramref name="device"/>: what <paramref name="part"/> names, a tag table or screen by
    /// <paramref name="name"/>, a custom web control by <paramref name="index"/>.
    /// </summary>
    public void Show(TiaHmiDevice device, TiaHmiPart part, string name, int index = 0) {
        var code = device.Code ?? TiaHmiCode.Empty;
        var links = device.TagTables.SelectMany(table => table.Tags).Where(tag => tag.PlcLink is not null)
            .GroupBy(tag => tag.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().PlcLink!, StringComparer.Ordinal);
        Content = part switch {
            TiaHmiPart.Connections => Connections(device),
            TiaHmiPart.Alarms => Alarms(device, links),
            TiaHmiPart.TagTable when device.TagTables.FirstOrDefault(table => table.Name == name) is { } table => Tags(device, table),
            TiaHmiPart.Screen when device.Screens.FirstOrDefault(screen => screen.Name == name) is { } screen => Screen(device, screen, links),
            TiaHmiPart.CustomWebControl when index < code.CustomWebControls.Count => Control(device, code.CustomWebControls[index]),
            _ => new Panel(),
        };
    }

    private Control Connections(TiaHmiDevice device) => Page(
        "HMI connections", Device(device),
        Table("*,*,*", ["Connection", "PLC", "Driver"], device.Connections,
            connection => [Text(connection.Name), Text(connection.Plc), Text(connection.DriverName)],
            _ => null, "The PLC each connection leads to, and its communication driver as TIA names it."));

    private Control Tags(TiaHmiDevice device, TiaHmiTagTable table) => Page(
        table.Name, Device(device) + (table.Folder.Length == 0 ? "" : "  ·  " + table.Folder),
        Table("180,110,150,90,*", ["HMI tag", "Data type", "Connection", "Cycle", "Reads"], table.Tags,
            tag => [Text(tag.Name), Text(tag.DataType), tag.Connection is null ? Muted("internal") : Text(tag.Connection), Text(tag.AcquisitionCycle), Reads(tag)],
            tag => tag.PlcLink, "What each HMI tag reads in its PLC: a PLC tag, a data block member or an address. Double-click a row, or press Enter, to open its PLC tag or data block."));

    private Control Alarms(TiaHmiDevice device, Dictionary<string, TiaHmiPlcLink> links) => Page(
        "HMI alarms", Device(device),
        Table("80,70,170,110,150,130,*", ["Kind", "Number", "Alarm", "Class", "Trigger tag", "Raised by", "Text"], device.Alarms,
            alarm => [
                Text(alarm.Kind == TiaHmiAlarmKind.Discrete ? "discrete" : "analog"), Text(alarm.Number?.ToString(CultureInfo.InvariantCulture)), Text(alarm.Name),
                Text(alarm.AlarmClass), Text(alarm.TriggerTag), Text(TiaHmiWriter.RaisedBy(alarm)), Text(alarm.Text),
            ],
            alarm => alarm.TriggerTag is { } tag ? links.GetValueOrDefault(tag) : null,
            "A discrete alarm is raised by a bit of its trigger tag, an analog one by a limit. Double-click a row, or press Enter, to open what its trigger tag reads in the PLC."));

    private Control Screen(TiaHmiDevice device, TiaHmiScreen screen, Dictionary<string, TiaHmiPlcLink> links) {
        var about = Device(device)
            + (screen.Number is { } number ? Invariant($"  ·  number {number}") : "")
            + (screen.Width is { } width && screen.Height is { } height ? Invariant($"  ·  {width} × {height}") : "");
        var scripts = (device.Code ?? TiaHmiCode.Empty).Scripts.Select((script, index) => (script, index)).Where(entry => entry.script.Screen == screen.Name).ToList();
        ListBox? list = null;
        var table = Table("150,110,140,50,50,50,50,110,*", ["Item", "Type", "Text", "Left", "Top", "Width", "Height", "HMI tags", "Scripts"], screen.Items,
            item => [
                Text(item.Name), Text(item.Type), Text(item.Text), Number(item.Left), Number(item.Top), Number(item.Width), Number(item.Height), Text(string.Join(", ", item.Tags)),
                Scripts(scripts.Where(entry => entry.script.Item == item.Name)),
            ],
            item => item.Tags.Select(tag => links.GetValueOrDefault(tag)).FirstOrDefault(link => link is not null),
            "Each item with its position and size, and the HMI tags it shows. Click an item in the preview to find its row; click a script to open it. Double-click a row, or press Enter, to open what its first tag reads in the PLC.",
            created => list = created);
        var page = Page(screen.Name, about, table);
        var own = scripts.Where(entry => entry.script.Item is null).ToList();
        var preview = new HmiScreenPreview(screen, item => {
            if (list is not null) {
                list.SelectedItem = item;
                list.ScrollIntoView(item);
            }
        }) { Margin = new Thickness(RowInset, 10, 16, 0), MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var top = new StackPanel { Spacing = 4 };
        top.Children.Add(preview);
        if (own.Count != 0) {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(RowInset, 2, 16, 0), Spacing = 6 };
            line.Children.Add(Muted("Screen scripts:"));
            line.Children.Add(Scripts(own));
            top.Children.Add(line);
        }
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Insert(1, top);
        return page;
    }

    /// <summary>A screen item's scripts, each a name that opens it.</summary>
    private Control Scripts(IEnumerable<(TiaHmiScript Script, int Index)> scripts) {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (script, index) in scripts) {
            var link = Text(script.Name);
            link.Foreground = Brush.Parse("#2563EB");
            link.TextDecorations = TextDecorations.Underline;
            link.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(link, (script.Kind == TiaHmiScriptKind.Event ? "The script of event " : "The script of property ") + script.Name);
            link.PointerPressed += (_, e) => {
                OpenScript?.Invoke(index);
                e.Handled = true;
            };
            panel.Children.Add(link);
        }
        return panel;
    }

    /// <summary>
    /// A custom web control: where it is used, its package's files, a text file's content beside the list, and its
    /// preview behind a notice (spec 0017).
    /// </summary>
    private Control Control(TiaHmiDevice device, TiaHmiCustomWebControl control) {
        var about = Device(device) + "  ·  " + control.Guid + "  ·  used by "
            + (control.UsedBy.Count == 0 ? "no screen item" : string.Join(", ", control.UsedBy.Select(use => use.Screen + "/" + use.Item)));
        var content = new TextBox {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 12, Margin = new Thickness(0, 10, 16, 6), Text = control.HasPackage ? "" : "The archive does not hold this control's package, " + control.PackagePath + ".",
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(content, ScrollBarVisibility.Auto);
        var table = Table("*,70", ["File", "Bytes"], control.Files,
            file => [Text(file.Path), Number(file.Content.Length)], _ => null,
            "The files of the control's package as the project keeps them. Select a text file to read it.",
            list => list.SelectionChanged += (_, _) => {
                if (list.SelectedItem is TiaHmiPackageFile file) {
                    content.Text = file.Text ?? Invariant($"{file.Path}: {file.Content.Length} bytes, not text.");
                }
            });
        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("360,*") };
        Grid.SetColumn(content, 1);
        body.Children.Add(table);
        body.Children.Add(content);
        var page = Page(control.DisplayName ?? control.Guid, about, body);
        var bar = HmiWebControlPreview.Bar(control, text => Said?.Invoke(text));
        bar.Margin = new Thickness(RowInset, 8, 16, 0);
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Insert(1, bar);
        return page;
    }

    private static string Device(TiaHmiDevice device) => device.Name + "  ·  " + device.FamilyName + (device.DeviceType is { Length: > 0 } type ? ", " + type : "");

    /// <summary>What a tag reads, a PLC link drawn as one.</summary>
    private static Control Reads(TiaHmiTag tag) {
        if (tag.PlcLink is not { } link) {
            return Text(tag.Address);
        }
        var text = Text(link.Display);
        text.Foreground = Brush.Parse(link.IsResolved ? "#7C3AED" : "#B45F06");
        ToolTip.SetTip(text, link.IsResolved ? (link.IsDataBlock ? "A member of data block " : "PLC tag ") + link.Name + (link.Plc is null ? "" : " of " + link.Plc)
            : "A member of data block " + link.Name + " that could not be resolved");
        return text;
    }

    // Pieces --------------------------------------------------------------------------------------------------------

    private static DockPanel Page(string title, string about, Control table) {
        var head = new StackPanel { Margin = new Thickness(RowInset, 12, 16, 0), Spacing = 2 };
        head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        head.Children.Add(Muted(about));
        var page = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        page.Children.Add(head);
        page.Children.Add(table);
        return page;
    }

    /// <summary>A list with a header row and a note below; a row with a PLC link opens it by double-click or Enter.</summary>
    private Control Table<T>(string columns, string[] header, IReadOnlyList<T> rows, Func<T, Control[]> cells, Func<T, TiaHmiPlcLink?> link, string note, Action<ListBox>? created = null) {
        var head = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns), Margin = new Thickness(RowInset, 10, 16, 4) };
        AddRow(head, header.Select(title => (Control)new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }).ToArray());
        Control body;
        if (rows.Count == 0) {
            var none = Muted("None.");
            none.Margin = new Thickness(RowInset, 6);
            body = none;
        } else {
            var list = new ListBox {
                Background = Brushes.Transparent,
                ItemsSource = rows,
                ItemTemplate = new FuncDataTemplate<T>((row, _) => {
                    var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns) };
                    if (row is not null) {
                        AddRow(grid, cells(row));
                    }
                    return grid;
                }),
            };
            // Rows start where the header's columns do.
            list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>()) {
                Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(RowInset, 3, 16, 3)) },
            });
            void OpenSelected() {
                if (list.SelectedItem is T row && link(row) is { } target) {
                    Open?.Invoke(target);
                }
            }
            list.DoubleTapped += (_, _) => OpenSelected();
            list.KeyDown += (_, e) => {
                if (e.Key == Key.Enter) {
                    OpenSelected();
                    e.Handled = true;
                }
            };
            body = list;
            created?.Invoke(list);
        }
        var panel = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);
        var footer = Muted(note);
        footer.FontSize = 12;
        footer.Margin = new Thickness(16, 6);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(body);
        return panel;
    }

    private static void AddRow(Grid grid, Control[] cells) {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < cells.Length; column++) {
            Grid.SetColumn(cells[column], column);
            grid.Children.Add(cells[column]);
        }
    }

    private static TextBlock Text(string? text) => new() { Text = text ?? "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Number(long? value) => Text(value?.ToString(CultureInfo.InvariantCulture));

    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted" }, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
