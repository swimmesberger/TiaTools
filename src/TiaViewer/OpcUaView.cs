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
/// A PLC's OPC UA configuration, laid out as TIA Portal's pages are (docs/research/2026-11-20-tia-opc-ua-server-settings.md):
/// the server's settings, its interfaces and roles, and its data blocks and tags with TIA's access columns. Read-only.
/// Built in code, without bindings, so it needs no reflection.
/// </summary>
public sealed class OpcUaView : UserControl {
    private const double RowInset = 20;
    private const string Inferred = "inferred from what is stored, not confirmed against TIA";

    // Column widths as text: a grid owns its definitions, so each row parses its own.
    private const string BlockColumns = "70,*,90,180,80,80,80";
    private const string TagColumns = "160,*,170,110,110,110,110";

    private readonly TabControl _tabs = new() { Padding = new Thickness(0) };

    public OpcUaView() => Content = _tabs;

    /// <summary>A data block or tag the person asked to open, by its name.</summary>
    public event Action<string>? Open;

    public void Show(TiaOpcUaPlc plc) {
        var tags = plc.TagTables.SelectMany(table => table.Tags.Select(tag => (Table: table.Name, Tag: tag))).ToList();
        _tabs.ItemsSource = new[] {
            Tab("Server", Scrolled(Server(plc))),
            Tab("Server interfaces and roles", Scrolled(InterfacesAndRoles(plc.Storage))),
            Tab(Count("Data blocks", plc.DataBlocks.Count), DataBlocks(plc.DataBlocks)),
            Tab(Count("Tags", tags.Count), Tags(tags)),
        };
        _tabs.SelectedIndex = 0;
    }

    // Server --------------------------------------------------------------------------------------------------------

    private static StackPanel Server(TiaOpcUaPlc plc) {
        var page = Page();
        var reachable = plc.DataBlocks.Count(block => block.AccessibleFromOpcUa);
        var allTags = plc.TagTables.SelectMany(table => table.Tags).ToList();
        var accessible = allTags.Count(tag => tag.ExternalAccess is TiaExternalAccess.ReadWrite or TiaExternalAccess.ReadOnly);
        var reach = Invariant($"{reachable} of {plc.DataBlocks.Count} data blocks are accessible from OPC UA, {accessible} of {allTags.Count} tags are accessible.");
        var server = plc.Server;
        page.Children.Add(Banner(
            server switch { null => "No OPC UA device item", { IsEnabled: true } => "Server on", _ => "Server off" },
            server is { IsEnabled: true },
            server is { IsEnabled: true } ? reach : server is null ? "The CPU has no OPC UA settings stored. " + reach : "No client reaches this PLC through its own OPC UA server. Were it on: " + reach));
        if (server is null) {
            return page;
        }

        page.Children.Add(Heading("General"));
        page.Children.Add(Rows(
            ("Activate OPC UA server", Check(server.IsEnabled)),
            ("Application name", server.ApplicationName is { } application ? Text(application) : NotStored("SIMATIC.S7-1500.OPC-UA.Application:" + plc.Name)),
            ("Device item", Text(server.ConfigType is null ? server.DeviceItem : server.DeviceItem + "  (" + server.ConfigType + ")"))));

        page.Children.Add(Heading("Options"));
        var options = new List<(string, Control)> {
            ("Port", server.Port is { } port ? Text(port.ToString(CultureInfo.InvariantCulture)) : NotStored("4840")),
            ("Enable standard SIMATIC server interface", Switch(server.StandardServerInterface, "on")),
            ("Minimum publishing interval", Milliseconds(server.MinimumPublishingInterval, "1000 ms")),
            ("Minimum sampling interval", Milliseconds(server.MinimumSamplingInterval, "1000 ms")),
        };
        options.AddRange(server.OtherSettings.Select(setting => (setting.Name, (Control)Text(setting.Value))));
        page.Children.Add(Rows([.. options]));

        page.Children.Add(Heading("Security  ›  Secure channel"));
        page.Children.Add(Rows(
            ("Server certificate", Text(server.ServerCertificateAssigned ? "assigned" : "none")),
            ("Security policies", Policies(server.SecurityPolicies)),
            ("Trusted clients", Text(server.TrustedClientCertificates == 1 ? "1 certificate" : Invariant($"{server.TrustedClientCertificates} certificates"))),
            ("Automatically accept all client certificates during runtime", Switch(server.AutoAcceptClientCertificates, "on"))));

        page.Children.Add(Heading("Security  ›  User authentication"));
        page.Children.Add(Rows(
            ("Enable guest authentication", Switch(server.GuestAuthentication, "on")),
            ("Enable user name and password authentication", Switch(server.PasswordAuthentication, "off"))));
        page.Children.Add(Note("TIA stores a setting only once it is changed. The defaults shown are what TIA Portal V21 sets for a new S7-1500 with firmware V2.9; other firmware may differ. Users and certificates are not read."));
        return page;
    }

    private static StackPanel Policies(IReadOnlyList<TiaOpcUaSecurityPolicy> policies) {
        var list = new StackPanel { Spacing = 2 };
        foreach (var policy in policies) {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(Check(policy.IsEnabled));
            line.Children.Add(new TextBlock { Text = policy.Name, VerticalAlignment = VerticalAlignment.Center });
            var stored = Muted(policy.IsEnabled is null ? "not stored" : "", 12);
            stored.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(stored);
            ToolTip.SetTip(line, "Stored key EnableSecurityPolicies_" + policy.Key + (policy.Key.StartsWith("256RSA", StringComparison.Ordinal) || policy.Name == policy.Key ? "; the policy name is " + Inferred + "." : "; checked in TIA."));
            list.Children.Add(line);
        }
        list.Children.Add(Muted(policies.All(policy => policy.IsEnabled is null)
            ? "None stored: TIA turns on No security and Basic256Sha256 (its default for firmware V2.9). Hover a line for its stored key."
            : "Hover a line for its stored key.", 12));
        return list;
    }

    // Server interfaces and roles ------------------------------------------------------------------------------------

    private static StackPanel InterfacesAndRoles(TiaOpcUaStorage? storage) {
        var page = Page();
        if (storage is null) {
            page.Children.Add(Muted("This PLC stores no OPC UA server storage: no server interfaces, no roles.", 13));
            return page;
        }
        page.Children.Add(Heading("Server interfaces"));
        page.Children.Add(Rows(
            ("Server interfaces", Text(storage.ServerInterfaces.Count == 0 ? "none" : string.Join(", ", storage.ServerInterfaces))),
            ("Structure changes", Text(storage.HasStructureChanges ? "yes, not decoded" : "none")),
            ("Mapping changes", Text(storage.HasMappingChanges ? "yes, not decoded" : "none")),
            ("Last compiled", Text(Date(storage.LastCompiled))),
            ("Last downloaded", Text(Date(storage.LastDownloaded)))));

        page.Children.Add(Heading("Roles"));
        if (storage.RolesError is { } error) {
            page.Children.Add(Muted("The roles could not be read: " + error, 13));
            return page;
        }
        var roles = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("180,140,80,*"), Margin = new Thickness(0, 0, 0, 8) };
        AddRow(roles, 0, Header("Role"), Header("Project role"), Header("Imported"), Header("Permissions per namespace"));
        var row = 1;
        foreach (var role in storage.Roles) {
            var permissions = string.Join("\n", role.Permissions.Select(permission =>
                (permission.NamespaceUri.Length == 0 ? "(local)" : permission.NamespaceUri) + ":  " + (permission.Bits == 0 ? "none" : string.Join(", ", permission.Names))));
            AddRow(roles, row++, Text(role.Name), Text(role.ProjectRole ?? ""), Text(role.IsImported ? "yes" : "no"), Text(permissions));
        }
        page.Children.Add(roles);
        page.Children.Add(Note(Invariant($"Assignments to project roles: {storage.RoleMappings}. Users stay in the project's user management, which TIA Viewer does not read.")));
        return page;
    }

    // Data blocks and tags ------------------------------------------------------------------------------------------

    private Control DataBlocks(IReadOnlyList<TiaOpcUaDataBlock> blocks) => Table(
        BlockColumns,
        [Header("Number"), Header("Name"), Header("Kind"), Header("DB accessible from OPC UA"), Header("Members"), Header("Closed"), Header("Read-only")],
        blocks,
        block => [
            Text(block.Number?.ToString(CultureInfo.InvariantCulture) ?? ""), Text(block.Name), Text(block.Kind), Check(block.AccessibleFromOpcUa),
            Text(block.IsClosed ? "closed" : block.Members.ToString(CultureInfo.InvariantCulture)),
            Text(block.HiddenMembers.Count.ToString(CultureInfo.InvariantCulture)), Text(block.ReadOnlyMembers.Count.ToString(CultureInfo.InvariantCulture)),
        ],
        block => block.Name,
        "Double-click a data block, or press Enter, to open it. Closed and read-only members are marked in its source.");

    private Control Tags(List<(string Table, TiaTag Tag)> tags) => Table(
        TagColumns,
        [Header("Tag table"), Header("Name"), Header("Data type"), Header("Address"), Header("Accessible¹"), Header("Writable¹"), Header("Visible¹")],
        tags,
        entry => [
            Text(entry.Table), Text(entry.Tag.Name), Text(entry.Tag.Type ?? ""), Text(entry.Tag.Address ?? ""),
            Check(entry.Tag.ExternalAccess is null ? null : entry.Tag.ExternalAccess != TiaExternalAccess.None),
            Check(entry.Tag.ExternalAccess is null ? null : entry.Tag.ExternalAccess == TiaExternalAccess.ReadWrite),
            Check(entry.Tag.ExternalVisible),
        ],
        entry => entry.Tag.Name,
        "¹ From HMI/OPC UA/Web API, and visible in HMI engineering, as TIA's tag table shows them. Double-click a tag, or press Enter, to open its table.");

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
        // Rows start where the header's columns do.
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
        if (list.SelectedItem is T row) {
            Open?.Invoke(name(row));
        }
    }

    // Pieces --------------------------------------------------------------------------------------------------------

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content, FontSize = 13 };

    private static string Count(string what, int count) => Invariant($"{what} ({count})");

    private static ScrollViewer Scrolled(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static StackPanel Page() => new() { Margin = new Thickness(20, 12, 20, 20), Spacing = 4, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

    private static Border Banner(string title, bool on, string text) {
        var line = new StackPanel { Spacing = 4 };
        var chip = new Border {
            Classes = { "chip" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = on ? Brush.Parse("#16A34A") : null,
            Child = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = on ? Brushes.White : null },
        };
        line.Children.Add(chip);
        line.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return new Border { Child = line, Margin = new Thickness(0, 0, 0, 8) };
    }

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

    /// <summary>A switch: its check box, or not stored with TIA's default.</summary>
    private static Control Switch(bool? value, string tiaDefault) => value is null ? NotStored(tiaDefault) : Check(value);

    private static Control Milliseconds(long? value, string tiaDefault) => value is { } ms ? Text(Invariant($"{ms} ms")) : NotStored(tiaDefault);

    /// <summary>A setting the project does not store, with TIA's default where it is known.</summary>
    private static TextBlock NotStored(string? tiaDefault) {
        var text = Muted(tiaDefault is null ? "not stored" : "not stored  ·  TIA's default: " + tiaDefault, 13);
        text.Margin = new Thickness(0, 3);
        return text;
    }

    private static TextBlock Muted(string text, double size) => new() { Text = text, Classes = { "muted" }, FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note(string text) {
        var note = Muted(text, 12);
        note.Margin = new Thickness(0, 10, 0, 0);
        return note;
    }

    /// <summary>A read-only check box; not stored shows as neither checked nor clear, dimmed.</summary>
    private static CheckBox Check(bool? value) => new() {
        IsChecked = value,
        Opacity = value is null ? 0.4 : 1,
        IsThreeState = value is null,
        IsHitTestVisible = false,
        Focusable = false,
        MinHeight = 0,
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "never";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
