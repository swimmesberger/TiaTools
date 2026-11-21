using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TiaFormat.Hardware;

namespace TiaViewer;

/// <summary>
/// The project's devices and networks (spec 0010): each device with its device items nested as TIA lists them, with
/// order number, firmware, I/O addresses, interface addresses and system constants, then the subnets and the IO systems.
/// A system constant opens in its PLC's tag table. Read-only, built in code without bindings.
/// </summary>
public sealed class DevicesView : UserControl {
    private readonly TabControl _tabs = new() { Padding = new Thickness(0) };

    public DevicesView() => Content = _tabs;

    /// <summary>A system constant the person asked to open: its PLC and its name.</summary>
    public event Action<string, string>? Open;

    public void Show(TiaHardwareReport report) {
        var devices = new TreeView { Margin = new Thickness(8, 6) };
        foreach (var device in report.Devices) {
            var node = new TreeViewItem { Header = DeviceLine(device), IsExpanded = true };
            foreach (var item in device.Items) {
                node.Items.Add(ItemNode(item));
            }
            devices.Items.Add(node);
        }
        _tabs.ItemsSource = new List<TabItem> {
            Tab(Count("Devices", report.Devices.Count), devices),
            Tab(Count("Subnets", report.Subnets.Count), Scrolled(Subnets(report.Subnets))),
            Tab(Count("IO systems", report.IoSystems.Count), Scrolled(IoSystems(report.IoSystems))),
        };
        _tabs.SelectedIndex = 0;
    }

    private Control DeviceLine(TiaDevice device) {
        var line = Line(Strong(device.Name));
        Add(line, Muted(device.TypeName));
        Add(line, device.Group.Length == 0 ? null : Muted("in " + device.Group));
        AddConstants(line, device.Constants);
        return line;
    }

    private TreeViewItem ItemNode(TiaDeviceItem item) {
        var line = Line(Muted(item.Slot?.ToString(CultureInfo.InvariantCulture) ?? "-", 40)!, Strong(item.Name));
        Add(line, Plain(item.TypeName ?? item.Label));
        Add(line, Muted(item.OrderNumber));
        Add(line, Muted(item.Firmware));
        Add(line, item.IoAddresses.Count == 0 ? null : Plain(string.Join(", ", item.IoAddresses)));
        AddConstants(line, item.Constants);
        var node = new TreeViewItem { Header = line };
        foreach (var address in item.InterfaceAddresses) {
            node.Items.Add(new TreeViewItem { Header = Plain(TiaHardwareWriter.Describe(address)) });
        }
        foreach (var child in item.Items) {
            node.Items.Add(ItemNode(child));
        }
        // Racks and plugged modules open, built-in parts closed below them unless they say something.
        node.IsExpanded = item.Items.Count != 0 && (item.OrderNumber is not null || item.InterfaceAddresses.Count != 0 || item.Items.Any(child => child.InterfaceAddresses.Count != 0 || child.IoAddresses.Count != 0));
        return node;
    }

    private static StackPanel Subnets(IReadOnlyList<TiaSubnet> subnets) {
        var page = Page();
        if (subnets.Count == 0) {
            page.Children.Add(Muted("The project has no subnets.")!);
        }
        foreach (var subnet in subnets) {
            page.Children.Add(Heading(subnet.Name + "  ·  " + (subnet.Kind == TiaSubnetKind.Profibus ? "PROFIBUS" : "PROFINET/Industrial Ethernet")));
            foreach (var path in subnet.Interfaces) {
                page.Children.Add(Plain(path)!);
            }
        }
        return page;
    }

    private StackPanel IoSystems(IReadOnlyList<TiaIoSystem> systems) {
        var page = Page();
        if (systems.Count == 0) {
            page.Children.Add(Muted("The project has no IO systems.")!);
        }
        foreach (var system in systems) {
            var title = system.Name + (system.Number is { } number ? string.Create(CultureInfo.InvariantCulture, $" ({number})") : "") + (system.Subnet is null ? "" : "  on " + system.Subnet);
            page.Children.Add(Heading(title));
            var constants = Line();
            AddConstants(constants, system.Constants);
            if (constants.Children.Count != 0) {
                page.Children.Add(constants);
            }
            page.Children.Add(Line(Muted("IO controller", 110)!, Plain(system.Controller ?? "none")!));
            foreach (var path in system.IoDevices) {
                page.Children.Add(Line(Muted("IO device", 110)!, Plain(path)!));
            }
        }
        return page;
    }

    private void AddConstants(StackPanel line, IReadOnlyList<TiaSystemConstant> constants) {
        foreach (var constant in constants) {
            var text = constant.HardwareIdentifier is { } id ? string.Create(CultureInfo.InvariantCulture, $"{constant.Name} = {id}") : constant.Name;
            if (constant.Plc is not { } plc) {
                line.Children.Add(Muted(text)!);
                continue;
            }
            var link = new Button {
                Classes = { "tool" },
                Padding = new Thickness(4, 0),
                Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline, FontSize = 12 },
            };
            ToolTip.SetTip(link, "Show in " + plc + "'s tag table");
            link.Click += (_, _) => Open?.Invoke(plc, constant.Name);
            line.Children.Add(link);
        }
    }

    // Pieces --------------------------------------------------------------------------------------------------------

    private static StackPanel Line(params Control[] parts) {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in parts) {
            line.Children.Add(part);
        }
        return line;
    }

    private static void Add(StackPanel line, Control? part) {
        if (part is not null) {
            line.Children.Add(part);
        }
    }

    private static TextBlock Strong(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock? Plain(string? text) => text is null ? null : new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock? Muted(string? text, double width = double.NaN) =>
        text is null ? null : new TextBlock { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Width = width };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 4) };

    private static StackPanel Page() => new() { Margin = new Thickness(20, 4, 20, 20), Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };

    private static ScrollViewer Scrolled(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content, FontSize = 13 };

    private static string Count(string what, int count) => string.Create(CultureInfo.InvariantCulture, $"{what} ({count})");
}
