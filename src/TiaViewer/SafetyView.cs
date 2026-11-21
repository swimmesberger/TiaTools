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
/// A PLC's safety program (spec 0012), with the labels of TIA's safety administration: its settings and signatures, its
/// F-runtime groups, its F-blocks and the F-parameters of the F-CPU and its F-I/O. Read-only, and never a password: only
/// whether one is set. Built in code, without bindings, so it needs no reflection.
/// </summary>
public sealed class SafetyView : UserControl {
    private const double RowInset = 20;
    private const string GroupColumns = "110,150,110,80,150,170,100,100,*";
    private const string BlockColumns = "60,80,*,90,120,150";
    private const string IoColumns = "100,110,60,60,80,100,170,100,*";

    private readonly TabControl _tabs = new() { Padding = new Thickness(0) };

    public SafetyView() => Content = _tabs;

    /// <summary>A block the person asked to open, by its name.</summary>
    public event Action<string>? Open;

    /// <summary>Shows a PLC's safety program.</summary>
    public void Show(TiaSafetyPlc plc) {
        if (plc.Program is not { } program) {
            _tabs.ItemsSource = new List<TabItem> { Tab("General", Scrolled(Missing(plc.Name))) };
            _tabs.SelectedIndex = 0;
            return;
        }
        _tabs.ItemsSource = new List<TabItem> {
            Tab("General", Scrolled(General(program))),
            Tab(Count("F-runtime groups", program.RuntimeGroups.Count), RuntimeGroups(program.RuntimeGroups)),
            Tab(Count("F-blocks", program.Blocks.Count), Blocks(program.Blocks)),
            Tab(Count("F-I/O", program.Io.Count), Io(program)),
        };
        _tabs.SelectedIndex = 0;
    }

    private static StackPanel Missing(string plc) {
        var page = Page();
        page.Children.Add(Muted(plc + " has no safety program.", 13));
        return page;
    }

    // General --------------------------------------------------------------------------------------------------------

    private static StackPanel General(TiaSafetyProgram program) {
        var page = Page();
        var settings = program.Settings;
        var signatures = program.Signatures;
        page.Children.Add(Heading("Safety administration"));
        page.Children.Add(Rows(
            ("Safety system version", Text(settings.SystemVersion ?? "")),
            ("Safety program password", Text(program.IsPasswordSet ? "set" : "not set")),
            ("F-change history", Check(settings.ChangeHistory)),
            ("Safety mode can be disabled", Check(settings.SafetyModeCanBeDisabled)),
            ("F-block numbers", Text(settings.FixedBlockNumbers
                ? Invariant($"fixed ranges: F-FB {Range(settings.FbNumbers)}, F-FC {Range(settings.FcNumbers)}, F-DB {Range(settings.DbNumbers)}")
                : "managed by the F-system"))));

        page.Children.Add(Heading("Signatures"));
        page.Children.Add(Rows(
            ("Collective F-signature", Signature(signatures.Collective)),
            ("Collective F-SW signature", Signature(signatures.Software)),
            ("Collective F-HW signature", Signature(signatures.Hardware)),
            ("F-communication address signature", Signature(signatures.CommunicationAddress)),
            ("Last compiled", Text(signatures.Compiled?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "never"))));
        page.Children.Add(Note("A signature reads none when the program was never compiled, or was changed since, as setting a password does. The password itself is never read."));
        return page;
    }

    private static Control Signature(uint? value) => value is { } v ? Text(TiaSafety.Hex(v)) : Muted("none", 13);

    // F-runtime groups -----------------------------------------------------------------------------------------------

    private Control RuntimeGroups(IReadOnlyList<TiaRuntimeGroup> groups) => Table(
        GroupColumns,
        [Header("F-runtime group"), Header("F-OB"), Header("Event class"), Header("Cycle time"), Header("Main safety block"), Header("Instance DB"),
            Header("Max. cycle time"), Header("Warning limit"), Header("Information DB")],
        groups,
        group => [
            Text(group.Name), Text(Reference(group.FOb)), Text(group.EventClass ?? ""), Text(Milliseconds(group.CycleTime)), Text(Reference(group.MainBlock)),
            Text(Reference(group.MainInstance)), Text(Milliseconds(group.MaximumCycleTime)), Text(Milliseconds(group.WarningCycleTime)), Text(group.InformationBlock ?? ""),
        ],
        group => group.MainBlock?.Name ?? group.FOb?.Name ?? "",
        "Double-click an F-runtime group, or press Enter, to open its main safety block.");

    // F-blocks -------------------------------------------------------------------------------------------------------

    private Control Blocks(IReadOnlyList<TiaFailsafeBlock> blocks) => Table(
        BlockColumns,
        [Header("Kind"), Header("Number"), Header("Name"), Header("Language"), Header("F-signature"), Header("")],
        blocks,
        block => [
            Text(block.Kind), Text(block.Number?.ToString(CultureInfo.InvariantCulture) ?? ""), Text(block.Name), Text(block.Language ?? ""),
            block.Signature is { } signature ? Text(TiaSafety.Hex(signature)) : Muted("none", 13),
            Muted(string.Join(", ", new[] { block.IsGenerated ? "generated" : null, block.IsKnowHowProtected ? "closed" : null }.Where(note => note is not null)), 12),
        ],
        block => block.Name,
        Invariant($"{blocks.Count(block => !block.IsGenerated)} written in the project, {blocks.Count(block => block.IsGenerated)} generated by TIA. Double-click a block, or press Enter, to open it."));

    // F-I/O ----------------------------------------------------------------------------------------------------------

    private Control Io(TiaSafetyProgram program) {
        var cpu = program.Cpu;
        var page = Page();
        page.Margin = new Thickness(20, 12, 20, 4);
        page.Children.Add(Heading("F-CPU"));
        page.Children.Add(Rows(
            ("Central F-source address", Value(cpu.CentralFSourceAddress)),
            ("F-destination addresses", Text(Invariant($"{cpu.LowestFDestinationAddress.Value} to {cpu.HighestFDestinationAddress.Value}")
                + (cpu.LowestFDestinationAddress.IsDefault || cpu.HighestFDestinationAddress.IsDefault ? "  (default)" : ""))),
            ("Default F-monitoring time for central F-I/O", Value(cpu.DefaultMonitoringTime, " ms"))));
        var table = Table(
            IoColumns,
            [Header("Device"), Header("F-I/O"), Header("Inputs"), Header("Outputs"), Header("F-source"), Header("F-destination"),
                Header("F-monitoring time"), Header("F-par. signature"), Header("F-I/O DB")],
            program.Io,
            io => [
                Text(io.Device), Text(io.Name), Text(io.InputAddress is { } i ? Invariant($"I {i}") : ""), Text(io.OutputAddress is { } q ? Invariant($"Q {q}") : ""),
                Value(io.FSourceAddress), Value(io.FDestinationAddress),
                Text(io.MonitoringTime is { } ms ? Invariant($"{ms} ms") + (io.MonitoringTimeByHand ? "  (set by hand)" : "") : ""),
                Text(io.ParameterSignature?.ToString(CultureInfo.InvariantCulture) ?? ""),
                Text(io.DataBlockNumber is { } number ? Invariant($"DB {number}  {io.DataBlockName}") : io.DataBlockName ?? ""),
            ],
            io => io.DataBlockName ?? "",
            "A value marked default is not stored: TIA uses it. Double-click an F-I/O, or press Enter, to open its F-I/O data block, which is closed.");
        var panel = new DockPanel();
        DockPanel.SetDock(page, Dock.Top);
        panel.Children.Add(page);
        panel.Children.Add(table);
        return panel;
    }

    private static Control Value(TiaSafetyValue value, string unit = "") => value.Value is { } v
        ? value.IsDefault ? Muted(Invariant($"{v}{unit}  (default)"), 13) : Text(Invariant($"{v}{unit}"))
        : Muted("not stored", 13);

    // Pieces ---------------------------------------------------------------------------------------------------------

    /// <summary>A list with a header row and a note below; a row is opened by double-click or Enter.</summary>
    private Control Table<T>(string columns, Control[] header, IReadOnlyList<T> rows, Func<T, Control[]> cells, Func<T, string> name, string note) {
        var head = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns), Margin = new Thickness(RowInset, 8, 16, 4) };
        AddRow(head, 0, header);
        var list = new ListBox {
            Background = Brushes.Transparent,
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<T>((row, _) => {
                var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns) };
                if (row is not null) {
                    AddRow(grid, 0, cells(row));
                }
                return grid;
            }),
        };
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>()) {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(RowInset, 3, 16, 3)) },
        });
        list.DoubleTapped += (_, _) => OpenSelected(list, name);
        list.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) {
                OpenSelected(list, name);
                e.Handled = true;
            }
        };
        var panel = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);
        var footer = Muted(note, 12);
        footer.Margin = new Thickness(16, 6);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(list);
        return panel;
    }

    private void OpenSelected<T>(ListBox list, Func<T, string> name) {
        if (list.SelectedItem is T row && name(row) is { Length: > 0 } target) {
            Open?.Invoke(target);
        }
    }

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content, FontSize = 13 };

    private static string Count(string what, int count) => Invariant($"{what} ({count})");

    private static ScrollViewer Scrolled(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static StackPanel Page() => new() { Margin = new Thickness(20, 12, 20, 20), Spacing = 4, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 4) };

    private static Grid Rows(params (string Label, Control Value)[] rows) {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("320,*") };
        for (var i = 0; i < rows.Length; i++) {
            var label = Muted(rows[i].Label, 13);
            label.Margin = new Thickness(0, 3, 12, 3);
            label.VerticalAlignment = rows[i].Value is CheckBox ? VerticalAlignment.Center : VerticalAlignment.Top;
            AddRow(grid, i, label, rows[i].Value);
        }
        return grid;
    }

    private static void AddRow(Grid grid, int row, params Control[] cells) {
        while (grid.RowDefinitions.Count <= row) {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        for (var column = 0; column < cells.Length; column++) {
            Grid.SetRow(cells[column], row);
            Grid.SetColumn(cells[column], column);
            grid.Children.Add(cells[column]);
        }
    }

    private static TextBlock Header(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Text(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Muted(string text, double size) => new() { Text = text, Classes = { "muted" }, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Note(string text) {
        var note = Muted(text, 12);
        note.Margin = new Thickness(0, 10, 0, 0);
        return note;
    }

    private static CheckBox Check(bool value) => new() {
        IsChecked = value,
        IsHitTestVisible = false,
        Focusable = false,
        MinHeight = 0,
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Range(TiaNumberRange range) => Invariant($"{range.From} to {range.To}");

    private static string Milliseconds(long? microseconds) => microseconds is { } us ? (us / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " ms" : "";

    private static string Reference(TiaSafetyBlockReference? block) => block is { } b ? b.Number is { } n ? Invariant($"{b.Name}  ({b.Kind} {n})") : b.Name : "";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
